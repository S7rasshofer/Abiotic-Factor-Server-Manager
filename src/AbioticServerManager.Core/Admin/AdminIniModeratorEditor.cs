namespace AbioticServerManager.Core.Admin;

/// <summary>
/// Pure editor for the <c>[Moderators]</c> section of Abiotic Factor's sectioned
/// <c>Admin.ini</c>. Mirrors <see cref="AdminIniBanEditor"/> so the moderator
/// list and the ban list share the same file format and preservation rules:
/// <code>
/// [Moderators]
/// Moderator=ID
///
/// [BannedPlayers]
/// BannedPlayer=ID
/// </code>
/// Everything outside the managed <c>Moderator=</c> lines (the
/// <c>[BannedPlayers]</c> section, comments, blank lines, placeholder examples)
/// is preserved verbatim. This is the Sec 2.2 unifier - never round-trip
/// moderators through a flat file again.
/// </summary>
public static class AdminIniModeratorEditor
{
    private const string Section = "[Moderators]";
    private const string Prefix = "Moderator=";

    public static IReadOnlyList<string> ListModerators(string? iniText)
    {
        var mods = new List<string>();
        if (string.IsNullOrEmpty(iniText))
        {
            return mods;
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
                    mods.Add(id);
                }
            }
        }

        return mods;
    }

    public static bool IsModerator(string? iniText, string id) =>
        ListModerators(iniText).Any(m => m.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string AddModerator(string? iniText, string id)
    {
        id = id.Trim();
        if (id.Length == 0 || IsModerator(iniText, id))
        {
            return iniText ?? "";
        }

        var lines = new List<string>(SplitLines(iniText ?? ""));
        var sectionIndex = lines.FindIndex(l =>
            l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            // No [Moderators] yet. Prepend it (moderators come first by
            // convention in the real Abiotic Factor file), keeping anything
            // else - comments, the [BannedPlayers] section, blank lines -
            // intact after a blank separator line.
            var prepended = new List<string> { Section, Prefix + id };
            if (lines.Count > 0)
            {
                if (lines[0].Trim().Length > 0)
                {
                    prepended.Add("");
                }
                prepended.AddRange(lines);
            }

            return string.Join("\n", prepended);
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

    public static string RemoveModerator(string? iniText, string id)
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
                continue; // drop only this moderator line
            }

            result.Add(raw);
        }

        return string.Join("\n", result);
    }

    /// <summary>
    /// Rewrites the <c>[Moderators]</c> section to exactly the given ordered set
    /// of ids while leaving every other byte (comments, blank lines, the
    /// <c>[BannedPlayers]</c> section, unrelated sections) untouched. Used by
    /// the Admin tab when the user finishes an edit batch (add/remove rows).
    /// </summary>
    public static string ReplaceModerators(string? iniText, IReadOnlyList<string> ids)
    {
        var cleanedIds = ids
            .Select(i => (i ?? string.Empty).Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var lines = new List<string>(SplitLines(iniText ?? ""));
        var sectionIndex = lines.FindIndex(l =>
            l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            if (cleanedIds.Count == 0)
            {
                return iniText ?? "";
            }

            // Need a section. Prepend it (moderators come first by convention)
            // so we never touch the [BannedPlayers] block.
            var prepended = new List<string> { Section };
            foreach (var id in cleanedIds)
            {
                prepended.Add(Prefix + id);
            }

            if (lines.Count > 0)
            {
                if (lines[0].Trim().Length > 0)
                {
                    prepended.Add("");
                }
                prepended.AddRange(lines);
            }

            return string.Join("\n", prepended);
        }

        // Find the section's end (first line that is the next section header
        // or end-of-file). Replace only the managed Moderator= entries inside
        // it, preserving any comments / blank lines / placeholders.
        var sectionEnd = lines.Count;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i].Trim()))
            {
                sectionEnd = i;
                break;
            }
        }

        var preserved = new List<string>();
        for (var i = sectionIndex + 1; i < sectionEnd; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue; // managed line - drop, rewrite below
            }
            preserved.Add(lines[i]);
        }

        var rewritten = new List<string>();
        rewritten.AddRange(lines.Take(sectionIndex + 1));
        foreach (var id in cleanedIds)
        {
            rewritten.Add(Prefix + id);
        }

        rewritten.AddRange(preserved);
        rewritten.AddRange(lines.Skip(sectionEnd));

        return string.Join("\n", rewritten);
    }

    private static bool IsSectionHeader(string line) =>
        line.StartsWith('[') && line.EndsWith(']');

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}
