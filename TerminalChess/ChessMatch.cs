namespace TerminalChess;

public class ChessMatch : IDisposable
{
    private readonly ChessBoard _board;
    private readonly UciConnector _engine;
    private readonly PieceColor _playerColor;

    private string _engineInvalidMove = string.Empty;

    public ChessMatch(MatchOptions options)
    {
        _board = new ChessBoard();
        _engine = new UciConnector(options);

        _playerColor = options.PlayerColor ?? ((Random.Shared.Next() & 1) == 0 ? PieceColor.White : PieceColor.Black);
    }

    public void Loop()
    {
        _engine.Connect();
        _engine.StartGame();

        var state = ChessboardState.Playable;
        var reason = GameEndReason.None;
        var interrupted = false;

        if (_playerColor == PieceColor.Black)
            state = PlayEngineMove(out reason);

        while (state == ChessboardState.Playable)
        {
            RenderBoard();

            Console.WriteLine();
            Console.WriteLine("What is your next move? (type 'resign' to resign or 'draw' to claim a draw)");
            while (true)
            {
                var input = Console.ReadLine();
                if (input == null)
                {
                    // A entrada foi fechada (Ctrl+Z ou fim do redirecionamento), não há como continuar a partida
                    interrupted = true;
                    break;
                }

                var userMove = input.Trim();

                if (userMove.Equals("resign", StringComparison.OrdinalIgnoreCase))
                {
                    state = _playerColor == PieceColor.White ? ChessboardState.BlackWon : ChessboardState.WhiteWon;
                    reason = GameEndReason.Resignation;
                    break;
                }

                if (userMove.Equals("draw", StringComparison.OrdinalIgnoreCase))
                {
                    if (_board.CanClaimDraw(out reason))
                    {
                        state = ChessboardState.Draw;
                        break;
                    }

                    Console.WriteLine("A draw cannot be claimed now, type a valid move: ");
                    continue;
                }

                if (_board.MovePiece(userMove) == ApplyMoveResult.Ok)
                    break;

                Console.WriteLine("Invalid move, type a valid move: ");
            }

            if (interrupted || state != ChessboardState.Playable)
                break;

            state = _board.GetState(out reason);
            if (state != ChessboardState.Playable)
                break;

            state = PlayEngineMove(out reason);
        }

        RenderBoard();

        switch (state)
        {
            case ChessboardState.Playable:
                // Só sai do laço com a partida em andamento quando a entrada é fechada
                Console.WriteLine("Game interrupted");
                break;
            case ChessboardState.Draw:
                Console.WriteLine("Draw by {0}", DescribeReason(reason));
                break;
            case ChessboardState.WhiteWon:
                Console.WriteLine("White won by {0}", DescribeReason(reason));
                break;
            case ChessboardState.BlackWon:
                Console.WriteLine("Black won by {0}", DescribeReason(reason));
                break;
        }

        if (reason == GameEndReason.EngineForfeit)
            Console.WriteLine("The engine did not return a valid move, it answered: {0}", _engineInvalidMove);
    }

    private ChessboardState PlayEngineMove(out GameEndReason reason)
    {
        // Mostra o lance do jogador já aplicado enquanto a engine calcula a resposta
        RenderBoard();
        Console.WriteLine();
        Console.WriteLine("Engine is thinking...");

        var engineMove = _engine.GetNextMove(_board.Moves);

        // Só pedimos lance à engine com o jogo em andamento, então não receber um lance (ou receber um ilegal)
        // significa que a engine abandonou ou está dessincronizada: a partida é encerrada a favor do jogador
        if (engineMove == null || _board.MoveEnginePiece(engineMove) != ApplyMoveResult.Ok)
        {
            _engineInvalidMove = engineMove ?? "no move";
            reason = GameEndReason.EngineForfeit;
            return _playerColor == PieceColor.White ? ChessboardState.WhiteWon : ChessboardState.BlackWon;
        }

        return _board.GetState(out reason);
    }

    private static string DescribeReason(GameEndReason reason) => reason switch
    {
        GameEndReason.Checkmate => "checkmate",
        GameEndReason.Resignation => "resignation",
        GameEndReason.Stalemate => "stalemate",
        GameEndReason.InsufficientMaterial => "insufficient material",
        GameEndReason.ThreefoldRepetition => "threefold repetition",
        GameEndReason.FivefoldRepetition => "fivefold repetition",
        GameEndReason.FiftyMoveRule => "the fifty-move rule",
        GameEndReason.SeventyFiveMoveRule => "the seventy-five-move rule",
        GameEndReason.EngineForfeit => "engine forfeit",
        _ => "unknown reason"
    };

    private void RenderBoard()
    {
        // Com a saída redirecionada não há tela para limpar e Console.Clear lança exceção
        if (!Console.IsOutputRedirected)
            Console.Clear();

        _board.PrintBoard(Console.Out, _playerColor, BeforePrintLine, BeforePrintPiece, AfterPrintLine);
        Console.WriteLine();
        Console.WriteLine("You are playing with {0}", _playerColor == PieceColor.White ? "White" : "Black");
        Console.WriteLine();
        Console.WriteLine("Last white move: {0}", GetLastMove(PieceColor.White));
        Console.WriteLine("Last black move: {0}", GetLastMove(PieceColor.Black));
        if (_board.IsInCheck())
            Console.WriteLine("Check!");
        Console.WriteLine();
        Console.WriteLine("Current Position: {0}", _board.ToFenString());
    }

    /// <summary>
    /// As brancas jogam os lances de índice par e as pretas os de índice ímpar
    /// </summary>
    private string GetLastMove(PieceColor color)
    {
        var moves = _board.Moves;
        var lastIndex = moves.Count - 1;

        var isLastMoveWhite = (lastIndex & 1) == 0;
        if (isLastMoveWhite != (color == PieceColor.White))
            lastIndex--;

        return lastIndex >= 0 ? moves[lastIndex] : string.Empty;
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    private static void AfterPrintLine()
    {
        Console.ResetColor();
    }

    private static void BeforePrintPiece()
    {
        (Console.ForegroundColor, Console.BackgroundColor) = (Console.BackgroundColor, Console.ForegroundColor);
    }

    private static void BeforePrintLine(int i)
    {
        if ((i & 1) == 0)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
        }
    }
}
