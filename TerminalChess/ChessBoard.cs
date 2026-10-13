using System.Runtime.CompilerServices;

namespace TerminalChess;

public class ChessBoard
{
    /* Os 8 bits mais significativos possuem o caractere da peça
     * Os 6 bits menos significativos possuem o tipo da peça
     * O 7° bit menos significativo possui a cor da peça
     * 0101 0010 0000 0010 - Torre branca
     * --------- > Letra R
     *            - > 0 é branco
     *             ------- > Torre
     *
     * 0110 1011 0101 0000 - Rei preto
     * --------- > Letra k
     *            - > 1 é preto
     *             ------- > Rei
     *
     * O índice [0,0] representa a célula A8 do board
     * O índice [7,0] representa a célula H8 do board
     * O índice [7,7] representa a célula H1 do board
     * O índice [0,7] representa a célula A1 do board
     */

    private static readonly (int x, int y)[] KnightOffsets =
        [(-2, -1), (-2, 1), (2, -1), (2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2)];

    private static readonly (int x, int y)[] KingOffsets =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private static readonly (int x, int y)[] OrthogonalDirections = [(0, -1), (0, 1), (-1, 0), (1, 0)];
    private static readonly (int x, int y)[] DiagonalDirections = [(-1, -1), (1, -1), (-1, 1), (1, 1)];

    private readonly Pieces[,] _board;

    // Quantas vezes cada posição (FEN sem os contadores) já ocorreu, usado para as regras de repetição
    private readonly Dictionary<string, int> _positionHistory = new();

    // Lances jogados desde a posição inicial, em notação UCI
    private readonly List<string> _moves = new();

    private PieceColor _nextRound;
    private (int x, int y) _blackKingPosition, _whiteKingPosition;

    // Peão que acabou de avançar duas casas e pode ser capturado en passant
    private (int x, int y)? _enPassantCapture;

    private int _movesCounter, _halfMoveCounter;

    // Peças que já se moveram (ou torres capturadas), usadas para os direitos de roque.
    // Torre 1 é a da coluna A (roque grande) e torre 2 a da coluna H (roque pequeno)
    private const byte WHITE_KING_MOVED = 1;
    private const byte BLACK_KING_MOVED = 1 << 1;
    private const byte WHITE_ROOK_1_MOVED = 1 << 2;
    private const byte WHITE_ROOK_2_MOVED = 1 << 3;
    private const byte BLACK_ROOK_1_MOVED = 1 << 4;
    private const byte BLACK_ROOK_2_MOVED = 1 << 5;

    private byte _movedFlags;

    public ChessBoard()
    {
        _board = new Pieces[8, 8];

        ResetBoard();
    }

    public void ResetBoard()
    {
        var board = _board;

        board[0, 0] = Pieces.BlackRook;
        board[1, 0] = Pieces.BlackKnight;
        board[2, 0] = Pieces.BlackBishop;
        board[3, 0] = Pieces.BlackQueen;
        board[4, 0] = Pieces.BlackKing;
        board[5, 0] = Pieces.BlackBishop;
        board[6, 0] = Pieces.BlackKnight;
        board[7, 0] = Pieces.BlackRook;

        _blackKingPosition = (4, 0);

        for (var i = 0; i < 8; i++)
            board[i, 1] = Pieces.BlackPawn;

        for (var j = 2; j < 6; j++)
        for (var i = 0; i < 8; i++)
            board[i, j] = Pieces.Empty;

        for (var i = 0; i < 8; i++)
            board[i, 6] = Pieces.WhitePawn;

        board[0, 7] = Pieces.WhiteRook;
        board[1, 7] = Pieces.WhiteKnight;
        board[2, 7] = Pieces.WhiteBishop;
        board[3, 7] = Pieces.WhiteQueen;
        board[4, 7] = Pieces.WhiteKing;
        board[5, 7] = Pieces.WhiteBishop;
        board[6, 7] = Pieces.WhiteKnight;
        board[7, 7] = Pieces.WhiteRook;

        _whiteKingPosition = (4, 7);

        _enPassantCapture = null;

        _movedFlags = 0;

        _nextRound = PieceColor.White;

        _movesCounter = 1;
        _halfMoveCounter = 0;

        _positionHistory.Clear();
        _moves.Clear();
        RegisterPosition();
    }

    public IReadOnlyList<string> Moves => _moves;

    public ChessboardState GetState(out GameEndReason reason)
    {
        if (!HasAnyLegalMove())
        {
            if (IsInCheck())
            {
                reason = GameEndReason.Checkmate;
                return _nextRound == PieceColor.White ? ChessboardState.BlackWon : ChessboardState.WhiteWon;
            }

            reason = GameEndReason.Stalemate;
            return ChessboardState.Draw;
        }

        if (HasInsufficientMaterial())
        {
            reason = GameEndReason.InsufficientMaterial;
            return ChessboardState.Draw;
        }

        if (GetRepetitionCount() >= 5)
        {
            reason = GameEndReason.FivefoldRepetition;
            return ChessboardState.Draw;
        }

        if (_halfMoveCounter >= 150)
        {
            reason = GameEndReason.SeventyFiveMoveRule;
            return ChessboardState.Draw;
        }

        reason = GameEndReason.None;
        return ChessboardState.Playable;
    }

    /// <summary>
    /// Verifica se o jogador da vez pode reivindicar empate (repetição tripla ou regra dos 50 lances)
    /// </summary>
    public bool CanClaimDraw(out GameEndReason reason)
    {
        if (GetRepetitionCount() >= 3)
        {
            reason = GameEndReason.ThreefoldRepetition;
            return true;
        }

        if (_halfMoveCounter >= 100)
        {
            reason = GameEndReason.FiftyMoveRule;
            return true;
        }

        reason = GameEndReason.None;
        return false;
    }

    public bool IsInCheck() => IsSquareAttacked(GetKingPosition(_nextRound), Opposite(_nextRound));

    public string ToFenString() => BuildFen(includeCounters: true, strictEnPassant: false);

    private string BuildFen(bool includeCounters, bool strictEnPassant)
    {
        Span<char> fen = stackalloc char[128];
        var fenPosition = 0;

        var board = _board;

        for (var y = 0; y < 8; y++)
        {
            var emptySpaceCounter = 0;
            for (var x = 0; x < 8; x++)
            {
                var piece = board[x, y];

                if (piece == Pieces.Empty)
                {
                    emptySpaceCounter++;
                }
                else
                {
                    if (emptySpaceCounter > 0)
                    {
                        emptySpaceCounter.TryFormat(fen.Slice(fenPosition), out var written);
                        fenPosition += written;
                        emptySpaceCounter = 0;
                    }

                    fen[fenPosition++] = piece.GetChar();
                }
            }

            if (emptySpaceCounter > 0)
            {
                emptySpaceCounter.TryFormat(fen.Slice(fenPosition), out var written);
                fenPosition += written;
            }

            if (y < 7)
                fen[fenPosition++] = '/';
        }

        fen[fenPosition++] = ' ';
        fen[fenPosition++] = _nextRound == PieceColor.White ? 'w' : 'b';
        fen[fenPosition++] = ' ';

        var castlingStart = fenPosition;
        if (HasCastlingRight(PieceColor.White, kingSide: true))
            fen[fenPosition++] = 'K';
        if (HasCastlingRight(PieceColor.White, kingSide: false))
            fen[fenPosition++] = 'Q';
        if (HasCastlingRight(PieceColor.Black, kingSide: true))
            fen[fenPosition++] = 'k';
        if (HasCastlingRight(PieceColor.Black, kingSide: false))
            fen[fenPosition++] = 'q';

        if (fenPosition == castlingStart)
            fen[fenPosition++] = '-';

        fen[fenPosition++] = ' ';

        // Para a repetição de posições, o en passant só conta se a captura for realmente possível
        if (_enPassantCapture is { } enPassant && (!strictEnPassant || HasEnPassantCapture(enPassant)))
        {
            var color = _board[enPassant.x, enPassant.y].GetColor();
            var pos = (enPassant.x, color == PieceColor.Black ? enPassant.y - 1 : enPassant.y + 1);

            ConvertPositionToString(pos, fen.Slice(fenPosition));
            fenPosition += 2;
        }
        else
        {
            fen[fenPosition++] = '-';
        }

        if (includeCounters)
        {
            fen[fenPosition++] = ' ';

            _halfMoveCounter.TryFormat(fen.Slice(fenPosition), out var w);
            fenPosition += w;
            fen[fenPosition++] = ' ';
            _movesCounter.TryFormat(fen.Slice(fenPosition), out w);
            fenPosition += w;
        }

        return new string(fen.Slice(0, fenPosition));
    }

    public ApplyMoveResult MovePiece(string move)
    {
        if (!RegexUtils.MoveRegex().IsMatch(move))
            return ApplyMoveResult.Invalid;

        var origin = ConvertPosition(move.AsSpan(0, 2));
        var destiny = ConvertPosition(move.AsSpan(2, 2));
        var promotion = GetPromotion(move);

        if (!ValidateMove(origin, destiny, promotion))
            return ApplyMoveResult.Invalid;

        MovePiece(origin, destiny, promotion);

        return ApplyMoveResult.Ok;
    }

    private bool ValidateMove((int x, int y) origin, (int x, int y) destiny, char promotion)
    {
        if (origin == destiny)
            return false;

        var board = _board;
        var boardPiece = board[origin.x, origin.y];

        if (boardPiece == Pieces.Empty || boardPiece.GetColor() != _nextRound)
            return false;

        var color = boardPiece.GetColor();

        var toCapture = board[destiny.x, destiny.y];
        if (toCapture != Pieces.Empty && toCapture.GetColor() == color)
            return false;

        var piece = boardPiece.GetPieceType();

        // A promoção é obrigatória ao chegar na última fileira e só é permitida nesse caso
        var isPromotion = piece == ChessPiece.Pawn && destiny.y == (color == PieceColor.White ? 0 : 7);
        if (isPromotion ? promotion is not ('q' or 'r' or 'b' or 'n') : promotion != '\0')
            return false;

        var isValid = piece switch
        {
            ChessPiece.Pawn => PawnMoveIsValid(origin, destiny),
            ChessPiece.King => KingMoveIsValid(origin, destiny),
            ChessPiece.Bishop => BishopMoveIsValid(origin, destiny),
            ChessPiece.Queen => QueenMoveIsValid(origin, destiny),
            ChessPiece.Rook => RookMoveIsValid(origin, destiny),
            ChessPiece.Knight => KnightMoveIsValid(origin, destiny),
            _ => false
        };

        return isValid && !LeavesKingInCheck(origin, destiny);
    }

    /// <summary>
    /// Simula o lance e verifica se o rei de quem moveu fica atacado.
    /// Cobre peças cravadas, a obrigação de sair do xeque e o rei andando para casas atacadas
    /// </summary>
    private bool LeavesKingInCheck((int x, int y) origin, (int x, int y) destiny)
    {
        var board = _board;
        var piece = board[origin.x, origin.y];
        var color = piece.GetColor();
        var captured = board[destiny.x, destiny.y];

        (int x, int y)? enPassantPosition = null;
        var enPassantPiece = Pieces.Empty;
        if (piece.GetPieceType() == ChessPiece.Pawn && origin.x != destiny.x && captured == Pieces.Empty)
        {
            enPassantPosition = (destiny.x, origin.y);
            enPassantPiece = board[destiny.x, origin.y];
            board[destiny.x, origin.y] = Pieces.Empty;
        }

        board[destiny.x, destiny.y] = piece;
        board[origin.x, origin.y] = Pieces.Empty;

        var kingPosition = piece.GetPieceType() == ChessPiece.King ? destiny : GetKingPosition(color);
        var inCheck = IsSquareAttacked(kingPosition, Opposite(color));

        board[origin.x, origin.y] = piece;
        board[destiny.x, destiny.y] = captured;
        if (enPassantPosition is { } position)
            board[position.x, position.y] = enPassantPiece;

        return inCheck;
    }

    private bool HasAnyLegalMove()
    {
        var board = _board;

        for (var originY = 0; originY < 8; originY++)
        for (var originX = 0; originX < 8; originX++)
        {
            var piece = board[originX, originY];
            if (piece == Pieces.Empty || piece.GetColor() != _nextRound)
                continue;

            var isPawn = piece.GetPieceType() == ChessPiece.Pawn;

            for (var destinyY = 0; destinyY < 8; destinyY++)
            for (var destinyX = 0; destinyX < 8; destinyX++)
            {
                var promotion = isPawn && destinyY is 0 or 7 ? 'q' : '\0';
                if (ValidateMove((originX, originY), (destinyX, destinyY), promotion))
                    return true;
            }
        }

        return false;
    }

    private bool HasInsufficientMaterial()
    {
        var board = _board;
        int knights = 0, bishops = 0;
        bool bishopOnLight = false, bishopOnDark = false;

        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var piece = board[x, y];
            if (piece == Pieces.Empty)
                continue;

            switch (piece.GetPieceType())
            {
                case ChessPiece.King:
                    break;
                case ChessPiece.Knight:
                    knights++;
                    break;
                case ChessPiece.Bishop:
                    bishops++;
                    if (((x + y) & 1) == 0)
                        bishopOnLight = true;
                    else
                        bishopOnDark = true;
                    break;
                default:
                    // Peão, torre ou dama sempre permitem mate
                    return false;
            }
        }

        // Rei contra rei, ou rei e uma peça menor contra rei
        if (knights + bishops <= 1)
            return true;

        // Apenas bispos, todos em casas da mesma cor
        return knights == 0 && !(bishopOnLight && bishopOnDark);
    }

    private int GetRepetitionCount() => _positionHistory.GetValueOrDefault(GetPositionKey());

    private string GetPositionKey() => BuildFen(includeCounters: false, strictEnPassant: true);

    private void RegisterPosition()
    {
        var key = GetPositionKey();
        _positionHistory[key] = _positionHistory.GetValueOrDefault(key) + 1;
    }

    private bool HasEnPassantCapture((int x, int y) enPassant)
    {
        var direction = _nextRound == PieceColor.White ? -1 : 1;
        var pawn = _nextRound == PieceColor.White ? Pieces.WhitePawn : Pieces.BlackPawn;
        var target = (enPassant.x, enPassant.y + direction);

        for (var dx = -1; dx <= 1; dx += 2)
        {
            var origin = (x: enPassant.x + dx, enPassant.y);
            if (PieceIsInPosition(origin.x, origin.y, pawn) && ValidateMove(origin, target, '\0'))
                return true;
        }

        return false;
    }

    private bool IsSquareAttacked((int x, int y) square, PieceColor attacker)
    {
        Pieces pawn, knight, bishop, rook, queen, king;
        int pawnY;

        if (attacker == PieceColor.White)
        {
            (pawn, knight, bishop, rook, queen, king) = (Pieces.WhitePawn, Pieces.WhiteKnight, Pieces.WhiteBishop,
                Pieces.WhiteRook, Pieces.WhiteQueen, Pieces.WhiteKing);
            // Peões brancos avançam para y menor, então atacam a partir da linha de baixo
            pawnY = square.y + 1;
        }
        else
        {
            (pawn, knight, bishop, rook, queen, king) = (Pieces.BlackPawn, Pieces.BlackKnight, Pieces.BlackBishop,
                Pieces.BlackRook, Pieces.BlackQueen, Pieces.BlackKing);
            pawnY = square.y - 1;
        }

        if (PieceIsInPosition(square.x - 1, pawnY, pawn) || PieceIsInPosition(square.x + 1, pawnY, pawn))
            return true;

        foreach (var (dx, dy) in KnightOffsets)
        {
            if (PieceIsInPosition(square.x + dx, square.y + dy, knight))
                return true;
        }

        foreach (var (dx, dy) in KingOffsets)
        {
            if (PieceIsInPosition(square.x + dx, square.y + dy, king))
                return true;
        }

        if (IsAttackedBySlider(square, OrthogonalDirections, rook, queen))
            return true;

        return IsAttackedBySlider(square, DiagonalDirections, bishop, queen);
    }

    private bool IsAttackedBySlider((int x, int y) square, (int x, int y)[] directions, Pieces piece, Pieces queen)
    {
        var board = _board;

        foreach (var (dx, dy) in directions)
        {
            for (int x = square.x + dx, y = square.y + dy; x is >= 0 and <= 7 && y is >= 0 and <= 7; x += dx, y += dy)
            {
                var possibleAttacker = board[x, y];
                if (possibleAttacker == Pieces.Empty)
                    continue;

                if (possibleAttacker == piece || possibleAttacker == queen)
                    return true;

                break;
            }
        }

        return false;
    }

    private bool PieceIsInPosition(int x, int y, Pieces piece)
    {
        if (x < 0 || x > 7 || y < 0 || y > 7)
            return false;

        return _board[x, y] == piece;
    }

    private bool KingMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        var xSteps = Math.Abs(origin.x - destiny.x);
        var ySteps = Math.Abs(origin.y - destiny.y);

        if (xSteps <= 1 && ySteps <= 1)
            return true;

        if (xSteps != 2 || ySteps != 0)
            return false;

        return CastleIsValid(origin, destiny, _board[origin.x, origin.y].GetColor());
    }

    private bool CastleIsValid((int x, int y) origin, (int x, int y) destiny, PieceColor color)
    {
        var row = color == PieceColor.White ? 7 : 0;
        if (origin != (4, row) || destiny.y != row)
            return false;

        var kingSide = destiny.x > origin.x;
        if (!HasCastlingRight(color, kingSide))
            return false;

        var rookPosition = (x: kingSide ? 7 : 0, y: row);
        var rook = color == PieceColor.White ? Pieces.WhiteRook : Pieces.BlackRook;
        if (_board[rookPosition.x, rookPosition.y] != rook)
            return false;

        // Todas as casas entre o rei e a torre precisam estar vazias
        if (!IsPathClear(origin, rookPosition))
            return false;

        // O rei não pode estar em xeque nem passar por casa atacada.
        // A casa de destino é verificada em LeavesKingInCheck
        var enemy = Opposite(color);
        var step = kingSide ? 1 : -1;
        return !IsSquareAttacked(origin, enemy) && !IsSquareAttacked((origin.x + step, row), enemy);
    }

    private bool HasCastlingRight(PieceColor color, bool kingSide)
    {
        if (color == PieceColor.White)
            return !WhiteKingMoved() && !(kingSide ? WhiteRook2Moved() : WhiteRook1Moved());

        return !BlackKingMoved() && !(kingSide ? BlackRook2Moved() : BlackRook1Moved());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WhiteKingMoved() => (_movedFlags & WHITE_KING_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool BlackKingMoved() => (_movedFlags & BLACK_KING_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WhiteRook1Moved() => (_movedFlags & WHITE_ROOK_1_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WhiteRook2Moved() => (_movedFlags & WHITE_ROOK_2_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool BlackRook1Moved() => (_movedFlags & BLACK_ROOK_1_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool BlackRook2Moved() => (_movedFlags & BLACK_ROOK_2_MOVED) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetMovedFlag(byte flag) => _movedFlags |= flag;

    private static bool KnightMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        var xSteps = Math.Abs(origin.x - destiny.x);
        var ySteps = Math.Abs(origin.y - destiny.y);

        return (xSteps == 2 && ySteps == 1) || (xSteps == 1 && ySteps == 2);
    }

    private bool QueenMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        if (destiny.x == origin.x || destiny.y == origin.y)
            return RookMoveIsValid(origin, destiny);

        return BishopMoveIsValid(origin, destiny);
    }

    private bool RookMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        if (destiny.x != origin.x && destiny.y != origin.y)
            return false;

        return IsPathClear(origin, destiny);
    }

    private bool BishopMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        if (Math.Abs(origin.x - destiny.x) != Math.Abs(origin.y - destiny.y))
            return false;

        return IsPathClear(origin, destiny);
    }

    /// <summary>
    /// Verifica se todas as casas entre a origem e o destino (exclusive) estão vazias,
    /// em linha reta ou diagonal
    /// </summary>
    private bool IsPathClear((int x, int y) origin, (int x, int y) destiny)
    {
        var board = _board;
        var stepX = Math.Sign(destiny.x - origin.x);
        var stepY = Math.Sign(destiny.y - origin.y);

        for (int x = origin.x + stepX, y = origin.y + stepY; (x, y) != destiny; x += stepX, y += stepY)
        {
            if (board[x, y] != Pieces.Empty)
                return false;
        }

        return true;
    }

    private bool PawnMoveIsValid((int x, int y) origin, (int x, int y) destiny)
    {
        var board = _board;
        var isWhite = board[origin.x, origin.y].GetColor() == PieceColor.White;
        var direction = isWhite ? -1 : 1;
        var initialLine = isWhite ? 6 : 1;

        var xSteps = destiny.x - origin.x;
        var ySteps = destiny.y - origin.y;

        if (xSteps == 0)
        {
            if (board[destiny.x, destiny.y] != Pieces.Empty)
                return false;

            if (ySteps == direction)
                return true;

            return ySteps == 2 * direction && origin.y == initialLine &&
                   board[origin.x, origin.y + direction] == Pieces.Empty;
        }

        if (Math.Abs(xSteps) != 1 || ySteps != direction)
            return false;

        if (board[destiny.x, destiny.y] != Pieces.Empty)
            return true;

        return _enPassantCapture == (destiny.x, origin.y);
    }

    // O lance da engine passa pela mesma validação, um lance ilegal indica que tabuleiro e engine estão dessincronizados
    public ApplyMoveResult MoveEnginePiece(string move) => MovePiece(move);

    private static char GetPromotion(string move) =>
        RegexUtils.IsPromotionMoveRegex().IsMatch(move) ? char.ToLowerInvariant(move[4]) : '\0';

    private void MovePiece((int x, int y) origin, (int x, int y) destiny, char promotion)
    {
        var board = _board;

        var piece = board[origin.x, origin.y];
        var pieceType = piece.GetPieceType();
        var color = piece.GetColor();
        var isCapture = board[destiny.x, destiny.y] != Pieces.Empty;

        // En passant: o peão capturado não está na casa de destino
        if (pieceType == ChessPiece.Pawn && origin.x != destiny.x && !isCapture)
        {
            board[destiny.x, origin.y] = Pieces.Empty;
            isCapture = true;
        }

        if (pieceType == ChessPiece.King)
        {
            if (color == PieceColor.Black)
            {
                _blackKingPosition = destiny;
                SetMovedFlag(BLACK_KING_MOVED);
            }
            else
            {
                _whiteKingPosition = destiny;
                SetMovedFlag(WHITE_KING_MOVED);
            }

            // Roque: a torre passa para o outro lado do rei
            var steps = destiny.x - origin.x;
            if (steps == 2)
            {
                board[5, origin.y] = board[7, origin.y];
                board[7, origin.y] = Pieces.Empty;
            }
            else if (steps == -2)
            {
                board[3, origin.y] = board[0, origin.y];
                board[0, origin.y] = Pieces.Empty;
            }
        }

        // Qualquer lance saindo de um canto ou capturando nele invalida o roque com a torre daquele canto
        MarkRookMoved(origin);
        MarkRookMoved(destiny);

        _enPassantCapture = pieceType == ChessPiece.Pawn && Math.Abs(destiny.y - origin.y) == 2 ? destiny : null;

        _halfMoveCounter = pieceType == ChessPiece.Pawn || isCapture ? 0 : _halfMoveCounter + 1;

        board[destiny.x, destiny.y] = promotion == '\0' ? piece : CreatePromotionPiece(promotion, color);
        board[origin.x, origin.y] = Pieces.Empty;

        _nextRound = Opposite(_nextRound);

        if (_nextRound == PieceColor.White)
            _movesCounter++;

        Span<char> move = stackalloc char[5];
        ConvertPositionToString(origin, move);
        ConvertPositionToString(destiny, move.Slice(2));
        move[4] = promotion;
        _moves.Add(new string(promotion == '\0' ? move.Slice(0, 4) : move));

        RegisterPosition();
    }

    private void MarkRookMoved((int x, int y) position)
    {
        switch (position)
        {
            case (0, 0):
                SetMovedFlag(BLACK_ROOK_1_MOVED);
                break;
            case (7, 0):
                SetMovedFlag(BLACK_ROOK_2_MOVED);
                break;
            case (0, 7):
                SetMovedFlag(WHITE_ROOK_1_MOVED);
                break;
            case (7, 7):
                SetMovedFlag(WHITE_ROOK_2_MOVED);
                break;
        }
    }

    private static Pieces CreatePromotionPiece(char promotion, PieceColor color) => promotion switch
    {
        'q' => color == PieceColor.Black ? Pieces.BlackQueen : Pieces.WhiteQueen,
        'r' => color == PieceColor.Black ? Pieces.BlackRook : Pieces.WhiteRook,
        'b' => color == PieceColor.Black ? Pieces.BlackBishop : Pieces.WhiteBishop,
        'n' => color == PieceColor.Black ? Pieces.BlackKnight : Pieces.WhiteKnight,
        _ => throw new ArgumentOutOfRangeException(nameof(promotion))
    };

    private (int x, int y) GetKingPosition(PieceColor color) =>
        color == PieceColor.White ? _whiteKingPosition : _blackKingPosition;

    private static PieceColor Opposite(PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    private static (int x, int y) ConvertPosition(ReadOnlySpan<char> position) =>
        (char.ToLower(position[0]) - 'a', -(position[1] - '8'));

    private static void ConvertPositionToString((int x, int y) position, Span<char> destination)
    {
        destination[0] = (char)(position.x + 'a');
        destination[1] = (char)(-position.y + '8');
    }

    public void PrintBoard(TextWriter textWriter, PieceColor direction, Action<int> beforePrintLine,
        Action beforePrintPiece, Action afterPrintLine)
    {
        if (direction == PieceColor.White)
            PrintWhiteBoard();
        else
            PrintBlackBoard();
        return;

        void PrintBlackBoard()
        {
            for (int i = 7, l = 1; i >= 0; i--, l++)
            {
                beforePrintLine(i + 1);
                for (var j = 7; j >= 0; j--)
                {
                    beforePrintPiece();
                    textWriter.Write(' ');
                    var piece = _board[j, i].GetChar();
                    textWriter.Write(piece == '\0' ? ' ' : piece);
                    textWriter.Write(' ');
                }

                afterPrintLine();

                textWriter.Write(' ');
                textWriter.Write((char)('0' + l));

                textWriter.WriteLine();
            }

            textWriter.WriteLine(" H  G  F  E  D  C  B  A ");
        }

        void PrintWhiteBoard()
        {
            for (int i = 0, l = 8; i < 8; i++, l--)
            {
                beforePrintLine(i);
                for (var j = 0; j < 8; j++)
                {
                    beforePrintPiece();
                    textWriter.Write(' ');
                    var piece = _board[j, i].GetChar();
                    textWriter.Write(piece == '\0' ? ' ' : piece);
                    textWriter.Write(' ');
                }

                afterPrintLine();

                textWriter.Write(' ');
                textWriter.Write((char)('0' + l));

                textWriter.WriteLine();
            }

            textWriter.WriteLine(" A  B  C  D  E  F  G  H ");
        }
    }
}
