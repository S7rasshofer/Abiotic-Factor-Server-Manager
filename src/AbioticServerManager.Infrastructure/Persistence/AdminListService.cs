using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Sectioned <c>Admin.ini</c> store. Sec 2.2 unifier - both the Admin tab editor and the
/// Ban/Unban commands target this single file. Moderator changes are routed through
/// <see cref="AdminIniModeratorEditor"/>; the <c>[BannedPlayers]</c> section is left
/// byte-identical, as are comments / blank lines / example placeholders.
/// </summary>
public sealed class AdminListService : IAdminListService
{
    public string ResolveAdminIniPath(ServerInstance instance)
    {
        // Sec 2.1: after the world-identity migration, instance.AdminIniPath points
        // at <DataRoot>/worlds/<id>/config/Admin.ini. Honor it FIRST so a
        // SteamCMD validate or a server reinstall cannot route us back to a
        // freshly-wiped in-install path.
        if (!string.IsNullOrWhiteSpace(instance.AdminIniPath))
        {
            return instance.AdminIniPath;
        }

        // Legacy fallback: a real dedicated-server install keeps Admin.ini under
        // Saved/SaveGames/Server/. This path only runs when the migration has not
        // yet been applied for this instance.
        if (HasInstalledServerLayout(instance))
        {
            return Path.Combine(
                instance.InstallPath,
                "AbioticFactor",
                "Saved",
                "SaveGames",
                "Server",
                "Admin.ini");
        }

        if (!string.IsNullOrWhiteSpace(instance.SandboxIniPath))
        {
            var dir = Path.GetDirectoryName(instance.SandboxIniPath);
            if (!string.IsNullOrEmpty(dir))
            {
                return Path.Combine(dir, "Admin.ini");
            }
        }

        return string.IsNullOrWhiteSpace(instance.InstallPath)
            ? string.Empty
            : Path.Combine(instance.InstallPath, "Admin.ini");
    }

    private static bool HasInstalledServerLayout(ServerInstance instance) =>
        !string.IsNullOrWhiteSpace(instance.InstallPath) &&
        Directory.Exists(Path.Combine(instance.InstallPath, "AbioticFactor"));

    public IReadOnlyList<string> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        return AdminIniModeratorEditor.ListModerators(File.ReadAllText(path));
    }

    public void Save(string path, IReadOnlyList<string> adminIds)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var ids = adminIds
            .Select(a => a.Trim())
            .Where(IAdminListService.IsValidSteamId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var original = File.Exists(path) ? File.ReadAllText(path) : "";
        var updated = AdminIniModeratorEditor.ReplaceModerators(original, ids);

        var temp = path + ".tmp";
        File.WriteAllText(temp, updated);
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
