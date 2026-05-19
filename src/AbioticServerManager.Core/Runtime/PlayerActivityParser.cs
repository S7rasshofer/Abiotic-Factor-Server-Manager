using System.Text.RegularExpressions;

namespace AbioticServerManager.Core.Runtime;

public static class PlayerActivityParser
{
    private static readonly Regex[] JoinPatterns =
    [
        new(@"\bplayer\s+[""']?(?<name>[^""'\[\]\r\n]+?)[""']?\s+(?:has\s+)?(?:joined|connected)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?<name>[^""'\[\]\r\n:]+?)\s+(?:has\s+)?(?:joined|connected)(?:\s+the\s+server)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    private static readonly Regex[] LeavePatterns =
    [
        new(@"\bplayer\s+[""']?(?<name>[^""'\[\]\r\n]+?)[""']?\s+(?:has\s+)?(?:left|disconnected)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?<name>[^""'\[\]\r\n:]+?)\s+(?:has\s+)?(?:left|disconnected)(?:\s+the\s+server)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    public static PlayerActivityEvent? TryParse(ServerLogLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            return null;
        }

        return TryMatch(line, JoinPatterns, PlayerActivityKind.Joined) ??
               TryMatch(line, LeavePatterns, PlayerActivityKind.Left);
    }

    private static PlayerActivityEvent? TryMatch(
        ServerLogLine line,
        IReadOnlyList<Regex> patterns,
        PlayerActivityKind kind)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(line.Text);
            if (!match.Success)
            {
                continue;
            }

            var name = CleanName(match.Groups["name"].Value);
            if (name.Length == 0)
            {
                continue;
            }

            return new PlayerActivityEvent(line.Timestamp, name, kind, line.Text);
        }

        return null;
    }

    private static string CleanName(string value)
    {
        var cleaned = value.Trim().Trim('"', '\'', ':', '-', ' ');
        return cleaned.Length > 64 ? cleaned[..64].Trim() : cleaned;
    }
}
