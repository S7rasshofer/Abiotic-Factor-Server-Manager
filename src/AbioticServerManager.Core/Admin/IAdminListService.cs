using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Admin;

/// <summary>
/// Reads and writes the server's <c>Admins.ini</c> (a flat list of SteamID64s).
/// Non-ID content (comments, blank lines) is preserved so hand-written notes are
/// never lost — consistent with the project's loss-less ethos.
/// </summary>
public interface IAdminListService
{
    /// <summary>Resolves where <c>Admins.ini</c> should live for this world, even if blank.</summary>
    string ResolveAdminIniPath(ServerInstance instance);

    /// <summary>The SteamID64 admin entries currently in the file (empty if none/missing).</summary>
    IReadOnlyList<string> Load(string path);

    /// <summary>Writes the admin set, preserving any comment/blank lines already in the file.</summary>
    void Save(string path, IReadOnlyList<string> adminIds);

    /// <summary>True when <paramref name="value"/> is a syntactically valid SteamID64.</summary>
    static bool IsValidSteamId(string? value) =>
        value is { Length: 17 } && value.All(char.IsDigit) && value[0] == '7';
}
