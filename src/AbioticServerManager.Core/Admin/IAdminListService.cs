using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Admin;

/// <summary>
/// Reads and writes the dedicated server's sectioned <c>Admin.ini</c>
/// (<c>[Moderators]</c> + <c>[BannedPlayers]</c>). The Sec 2.2 unifier - both the
/// Admin tab editor and the Ban/Unban commands target this single file via
/// the pure <see cref="AdminIniModeratorEditor"/> / <see cref="AdminIniBanEditor"/>
/// helpers. Comments, blank lines, and example placeholders are preserved.
/// </summary>
public interface IAdminListService
{
    /// <summary>Resolves where the sectioned <c>Admin.ini</c> should live for this world, even if blank.</summary>
    string ResolveAdminIniPath(ServerInstance instance);

    /// <summary>SteamID64 entries from the file's <c>[Moderators]</c> section (empty if none/missing).</summary>
    IReadOnlyList<string> Load(string path);

    /// <summary>
    /// Rewrites the <c>[Moderators]</c> section in place, preserving everything else
    /// (comments, blank lines, the <c>[BannedPlayers]</c> section, examples) byte-for-byte.
    /// </summary>
    void Save(string path, IReadOnlyList<string> adminIds);

    /// <summary>True when <paramref name="value"/> is a syntactically valid SteamID64.</summary>
    static bool IsValidSteamId(string? value) =>
        value is { Length: 17 } && value.All(char.IsDigit) && value[0] == '7';
}
