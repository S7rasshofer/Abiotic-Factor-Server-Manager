using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Flat-file <c>Admins.ini</c> store. Admin entries are SteamID64 lines; every other
/// line (comments, blanks) is kept verbatim and re-emitted before the managed ID list
/// so user notes survive an edit. "Discover it, do not hardcode it."
/// </summary>
public sealed class AdminListService : IAdminListService
{
    public string ResolveAdminIniPath(ServerInstance instance)
    {
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

        if (!string.IsNullOrWhiteSpace(instance.AdminIniPath))
        {
            return instance.AdminIniPath;
        }

        if (!string.IsNullOrWhiteSpace(instance.SandboxIniPath))
        {
            var dir = Path.GetDirectoryName(instance.SandboxIniPath);
            if (!string.IsNullOrEmpty(dir))
            {
                return Path.Combine(dir, "Admins.ini");
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

        return [.. File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(IAdminListService.IsValidSteamId)
            .Distinct(StringComparer.Ordinal)];
    }

    public void Save(string path, IReadOnlyList<string> adminIds)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var preserved = File.Exists(path)
            ? File.ReadAllLines(path)
                .Where(l => !IAdminListService.IsValidSteamId(l.Trim()))
                .ToList()
            : [];

        var ids = adminIds
            .Select(a => a.Trim())
            .Where(IAdminListService.IsValidSteamId)
            .Distinct(StringComparer.Ordinal);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllLines(path, [.. preserved, .. ids]);
    }
}
