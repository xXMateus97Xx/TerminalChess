namespace TerminalChess;

[Flags]
public enum ChessPiece : byte
{
    Pawn = 1,
    Rook = 1 << 1,
    Knight = 1 << 2,
    Bishop = 1 << 3,
    King = 1 << 4,
    Queen = 1 << 5,
    
    All = Pawn | Rook | Knight | Bishop | King | Queen
}