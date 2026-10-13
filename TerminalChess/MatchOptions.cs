namespace TerminalChess;

public class MatchOptions
{
    public PieceColor? PlayerColor { get; init; }
    public GameLevel Level { get; init; }

    // Força aproximada do adversário em cada nível, usada pela opção UCI_Elo
    public int EngineElo => Level switch
    {
        GameLevel.Beginner => 800,
        GameLevel.Novice => 1100,
        GameLevel.Medium => 1400,
        GameLevel.Hard => 1800,
        GameLevel.Expert => 2200,
        _ => 2850
    };

    public string? EnginePath { get; init; }
    public int EngineThreads { get; init; }
}