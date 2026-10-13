using System.Text.RegularExpressions;

namespace TerminalChess;

public static partial class RegexUtils
{
    [GeneratedRegex("^([a-h][1-8]){2}[rnbq]?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex MoveRegex();

    [GeneratedRegex("^([a-h][1-8]){2}[rnbq]$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex IsPromotionMoveRegex();
}