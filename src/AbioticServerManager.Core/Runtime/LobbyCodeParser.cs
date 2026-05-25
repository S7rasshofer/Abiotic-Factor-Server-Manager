using System.Text.RegularExpressions;

namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Extracts the Abiotic Factor "lobby code" from a dedicated-server log line.
/// The server publishes it as an EOS session attribute named <c>ShortCode</c> -
/// confirmed verbatim from a real captured server log, e.g.:
/// <c>LogOnlineSession: EOS: EOS_SessionModification_AddAttribute() named (ShortCode) with value (O8TXQ)</c>.
/// This is the same short code the game shows in-game as "LOBBY CODE"; it
/// changes every time the session is recreated (i.e. on a server restart).
/// </summary>
public static class LobbyCodeParser
{
    // ... named (ShortCode) with value (O8TXQ) - mirrors the PlayerCount pattern.
    private static readonly Regex ShortCode = new(
        @"\(ShortCode\)\s*with value\s*\((?<code>[^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns the lobby code carried by a log line, or null when the line is
    /// not a <c>ShortCode</c> session attribute (or carries an empty value).
    /// </summary>
    public static string? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ShortCode.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var code = match.Groups["code"].Value.Trim();
        return code.Length == 0 ? null : code;
    }
}
