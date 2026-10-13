using System.CommandLine;
using System.CommandLine.Parsing;
using TerminalChess;

GameLevel[] levels =
[
    GameLevel.Beginner, GameLevel.Novice, GameLevel.Medium, GameLevel.Hard, GameLevel.Expert, GameLevel.WorldChampion
];

var defaultThreads = Math.Max(1, Environment.ProcessorCount / 2);

var engineOption = new Option<FileInfo>("--engine", "-e")
{
    Description = "Path to the UCI engine executable (default: stockfish.exe)"
}.AcceptExistingOnly();

var levelOption = new Option<GameLevel>("--level", "-l")
{
    Description = "Game level: 1 - beginner, 2 - novice, 3 - medium, 4 - hard, 5 - expert, 6 - worldchampion",
    DefaultValueFactory = _ => GameLevel.Novice,
    CustomParser = ParseLevel
};

var colorOption = new Option<PieceColor?>("--color", "-c")
{
    Description = "Your pieces color: white or black (default: random)",
    CustomParser = ParseColor
};

var threadsOption = new Option<int>("--threads", "-t")
{
    Description = $"Threads the engine can use (min: 1, max: {Environment.ProcessorCount})",
    DefaultValueFactory = _ => defaultThreads
};
threadsOption.Validators.Add(result =>
{
    var threads = result.GetValueOrDefault<int>();
    if (threads < 1 || threads > Environment.ProcessorCount)
        result.AddError($"Threads must be between 1 and {Environment.ProcessorCount}.");
});

var rootCommand = new RootCommand("Play chess against an UCI engine in the terminal")
{
    engineOption,
    levelOption,
    colorOption,
    threadsOption
};

rootCommand.SetAction(parseResult =>
{
    var options = new MatchOptions
    {
        PlayerColor = parseResult.GetValue(colorOption),
        Level = parseResult.GetValue(levelOption),
        EngineThreads = parseResult.GetValue(threadsOption),
        EnginePath = parseResult.GetValue(engineOption)?.FullName,
    };

    using var game = new ChessMatch(options);

    try
    {
        game.Loop();
        return 0;
    }
    catch (UCIConnectionException ex)
    {
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Engine error: {0}", ex.InnerException?.Message ?? ex.Message);
        return 1;
    }
});

return rootCommand.Parse(args).Invoke();

// Aceita o número do nível (1 a 6) ou o nome, ex.: "hard", "WorldChampion", "world-champion"
GameLevel ParseLevel(ArgumentResult result)
{
    if (result.Tokens.Count == 0)
        return GameLevel.Novice;

    var value = result.Tokens[0].Value;

    if (int.TryParse(value, out var number))
    {
        if (number >= 1 && number <= levels.Length)
            return levels[number - 1];
    }
    else
    {
        var name = value.Replace("-", string.Empty).Replace("_", string.Empty);
        foreach (var level in levels)
        {
            if (level.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                return level;
        }
    }

    result.AddError($"Invalid level '{value}'. Use 1 to {levels.Length} or one of: {string.Join(", ", levels)}.");
    return default;
}

PieceColor? ParseColor(ArgumentResult result)
{
    if (result.Tokens.Count == 0)
        return null;

    var value = result.Tokens[0].Value;
    if (value.Equals("white", StringComparison.OrdinalIgnoreCase) || value.Equals("w", StringComparison.OrdinalIgnoreCase))
        return PieceColor.White;
    if (value.Equals("black", StringComparison.OrdinalIgnoreCase) || value.Equals("b", StringComparison.OrdinalIgnoreCase))
        return PieceColor.Black;

    result.AddError($"Invalid color '{value}'. Use white or black.");
    return null;
}
