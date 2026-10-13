using System.Runtime.CompilerServices;

namespace TerminalChess;

internal enum Pieces : ushort
{
    Empty = 0,

    BlackPawn = 'p' << 8 | PieceColor.Black | ChessPiece.Pawn,
    BlackRook = 'r' << 8 | PieceColor.Black | ChessPiece.Rook,
    BlackBishop = 'b' << 8 | PieceColor.Black | ChessPiece.Bishop,
    BlackKnight = 'n' << 8 | PieceColor.Black | ChessPiece.Knight,
    BlackQueen = 'q' << 8 | PieceColor.Black | ChessPiece.Queen,
    BlackKing = 'k' << 8 | PieceColor.Black | ChessPiece.King,

    WhitePawn = 'P' << 8 | PieceColor.White | ChessPiece.Pawn,
    WhiteRook = 'R' << 8 | PieceColor.White | ChessPiece.Rook,
    WhiteBishop = 'B' << 8 | PieceColor.White | ChessPiece.Bishop,
    WhiteKnight = 'N' << 8 | PieceColor.White | ChessPiece.Knight,
    WhiteQueen = 'Q' << 8 | PieceColor.White | ChessPiece.Queen,
    WhiteKing = 'K' << 8 | PieceColor.White | ChessPiece.King,
}

internal static class PiecesExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PieceColor GetColor(this Pieces piece)
    {
        return (PieceColor)piece & PieceColor.Black;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChessPiece GetPieceType(this Pieces piece)
    {
        return (ChessPiece)((ushort)piece & (ushort)ChessPiece.All);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char GetChar(this Pieces piece)
    {
        return (char)((ushort)piece >> 8);
    }
}