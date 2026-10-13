namespace TerminalChess;

[Flags]
public enum PieceColor : byte
{
    Black = 1 << 6,
    White = 0,
}