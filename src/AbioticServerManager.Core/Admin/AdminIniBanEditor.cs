namespace AbioticServerManager.Core.Admin;

/// <summary>
/// Pure editor for Abiotic Factor's <c>Admin.ini</c> ban list. The real file is
/// INI-sectioned:
/// <code>
/// [Moderators]
/// Moderator=ID
///
/// [BannedPlayers]
/// BannedPlayer=ID
/// </code>
/// Everything outside the managed <c>BannedPlayer=</c> lines (the
/// <c>[Moderators]</c> section, comments, blank lines, placeholder examples) is
/// preserved verbatim. Pure so it is unit-testable without a real server.
/// </summary>
public static class AdminIniBanEditor
{
    private const string Section = "[BannedPlayers]";
    private const string Prefix = "BannedPlayer=";

    public static IReadOnlyList<string> ListBans(string? iniText)
    {
        var bans = new List<string>();
        if (string.IsNullOrEmpty(iniText))
        {
            return bans;
        }

        var inSection = false;
        foreach (var raw in SplitLines(iniText))
        {
            var line = raw.Trim();
            if (IsSectionHeader(line))
            {
                inSection = line.Equals(Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inSection && line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = line[Prefix.Length..].Trim();
                if (id.Length > 0)
                {
                    bans.Add(id);
                }
            }
        }

        return bans;
    }

    public static bool IsBanned(string? iniText, string id) =>
        ListBans(iniText).Any(b => b.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string AddBan(string? iniText, string id)
    {
        id = id.Trim();
        if (id.Length == 0 || IsBanned(iniText, id))
        {
            return iniText ?? "";
        }

        var lines = new List<string>(SplitLines(iniText ?? ""));
        var sectionIndex = lines.FindIndex(l =>
            l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add("");
            }

            lines.Add(Section);
            lines.Add(Prefix + id);
            return string.Join("\n", lines);
        }

        // Insert after the last existing entry in the section (before the next
        // section header or end of file).
        var insertAt = sectionIndex + 1;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i].Trim()))
            {
                break;
            }

            insertAt = i + 1;
        }

        lines.Insert(insertAt, Prefix + id);
        return string.Join("\n", lines);
    }

    public static string RemoveBan(string? iniText, string id)
    {
        id = id.Trim();
        if (string.IsNullOrEmpty(iniText) || id.Length == 0)
        {
            return iniText ?? "";
        }

        var result = new List<string>();
        var inSection = false;
        foreach (var raw in SplitLines(iniText))
        {
            var trimmed = raw.Trim();
            if (IsSectionHeader(trimmed))
            {
                inSection = trimmed.Equals(Section, StringComparison.OrdinalIgnoreCase);
                result.Add(raw);
                continue;
            }

            if (inSection &&
                trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
                trimmed[Prefix.Length..].Trim().Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue; // drop only this ban line
            }

            result.Add(raw);
        }

        return string.Join("\n", result);
    }

    private static bool IsSectionHeader(string line) =>
        line.StartsWith('[') && line.EndsWith(']');

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}
