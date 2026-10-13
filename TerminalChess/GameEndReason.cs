namespace TerminalChess;

public enum GameEndReason
{
    None,
    Checkmate,
    Resignation,
    Stalemate,
    InsufficientMaterial,
    ThreefoldRepetition,
    FivefoldRepetition,
    FiftyMoveRule,
    SeventyFiveMoveRule,
    EngineForfeit
}
