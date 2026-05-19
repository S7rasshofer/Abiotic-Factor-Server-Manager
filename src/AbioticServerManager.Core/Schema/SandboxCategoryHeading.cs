using System.Text.RegularExpressions;

namespace AbioticServerManager.Core.Schema;

/// <summary>
/// Abiotic Factor's live SandboxSettings.ini keeps everything in one
/// <c>[SandboxSettings]</c> section and groups settings with banner comments such as
/// <c>; === WORLD ===</c> / <c>; === ENEMY ===</c>. This recognises those banners so each
/// setting can be routed to its tab from the file's own grouping rather than a hardcoded
/// assumption. "Do not hardcode the facility. Discover it."
/// </summary>
public static partial class SandboxCategoryHeading
{
    // ; === WORLD ===   |   # --- Player ---   |   ;==== Items ====
    [GeneratedRegex(@"^[;#]\s*[=\-]{2,}\s*(?<name>[^=\-][^=]*?)\s*[=\-]{2,}\s*$")]
    private static partial Regex HeadingRegex();

    public static bool TryParse(string commentRaw, out string category)
    {
        var match = HeadingRegex().Match(commentRaw.Trim());
        if (!match.Success)
        {
            category = string.Empty;
            return false;
        }

        category = TitleCase(match.Groups["name"].Value.Trim());
        return category.Length > 0;
    }

    private static string TitleCase(string text) =>
        string.Join(
            ' ',
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
