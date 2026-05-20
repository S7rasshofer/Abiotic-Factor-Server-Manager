using AbioticServerManager.Core.Services;

namespace AbioticServerManager.Infrastructure.FileSystem;

public sealed class AppPaths : IAppPaths
{
    private const string ProductFolder = "FacilityOverseer";
    private const string PortableDataFolder = "FacilityOverseerData";

    public AppPaths()
        : this(ResolveDefaultDataRoot())
    {
    }

    public AppPaths(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);

        // SteamCMD and the dedicated server are rewritten in place constantly and
        // must NOT live in a synced folder (OneDrive et al. lock/dehydrate files
        // mid-write -> "Failed to load steam.dll"). Config/backups/logs are small
        // and safe to keep where the user put them, so only the volatile tool and
        // server payloads are redirected to a plain local path.
        VolatileRoot = IsSyncedLocation(DataRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolder)
            : DataRoot;
    }

    public AppPaths(string appDataRoot, string localAppDataRoot)
        : this(appDataRoot)
    {
    }

    public string DataRoot { get; }

    /// <summary>
    /// Where rewrite-heavy payloads (SteamCMD, server files) live. Equals
    /// <see cref="DataRoot"/> unless that root is a synced location, in which
    /// case it points at a non-synced local path.
    /// </summary>
    public string VolatileRoot { get; }

    public string ConfigDirectory => Path.Combine(DataRoot, "config");

    public string ToolsDirectory => Path.Combine(VolatileRoot, "tools");

    public string ServersDirectory => Path.Combine(VolatileRoot, "servers");

    public string ManagedServerDirectory => Path.Combine(ServersDirectory, "abiotic-factor-dedicated");

    public string AppDataRoot => DataRoot;

    public string LocalAppDataRoot => DataRoot;

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string InstancesFile => Path.Combine(ConfigDirectory, "instances.json");

    public string SchemaCacheFile => Path.Combine(ConfigDirectory, "schema-cache.json");

    public string SteamCmdDirectory => Path.Combine(ToolsDirectory, "steamcmd");

    public string DefaultServerInstallDirectory => ManagedServerDirectory;

    public string BackupsRoot => Path.Combine(DataRoot, "backups");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public string PlayersDirectory => Path.Combine(DataRoot, "players");

    // §2.1: World config (sandbox/admin/etc.) lives under DataRoot/worlds/<id>/
    // so a SteamCMD validate, a server reinstall, or a VolatileRoot wipe cannot
    // destroy per-world tuning, admins, or bans.
    public string WorldsDirectory => Path.Combine(DataRoot, "worlds");

    public string WorldDirectory(string worldId) =>
        Path.Combine(WorldsDirectory, SanitizeWorldId(worldId));

    public string WorldConfigDirectory(string worldId) =>
        Path.Combine(WorldDirectory(worldId), "config");

    public string WorldSandboxIniPath(string worldId) =>
        Path.Combine(WorldConfigDirectory(worldId), "SandboxSettings.ini");

    public string WorldAdminIniPath(string worldId) =>
        Path.Combine(WorldConfigDirectory(worldId), "Admin.ini");

    public string WorldMetadataJsonPath(string worldId) =>
        Path.Combine(WorldConfigDirectory(worldId), "metadata.json");

    public string WorldSavesDirectory(string worldId) =>
        Path.Combine(WorldDirectory(worldId), "saves");

    public string WorldRosterDirectory(string worldId) =>
        Path.Combine(WorldDirectory(worldId), "roster");

    public string WorldRuntimeDirectory(string worldId) =>
        Path.Combine(WorldDirectory(worldId), "runtime");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(SteamCmdDirectory);
        Directory.CreateDirectory(ServersDirectory);
        Directory.CreateDirectory(BackupsRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(WorldsDirectory);
    }

    public void EnsureWorldCreated(string worldId)
    {
        var safe = SanitizeWorldId(worldId);
        if (string.IsNullOrEmpty(safe))
        {
            return;
        }

        Directory.CreateDirectory(WorldsDirectory);
        Directory.CreateDirectory(WorldDirectory(safe));
        Directory.CreateDirectory(WorldConfigDirectory(safe));
        Directory.CreateDirectory(WorldSavesDirectory(safe));
        Directory.CreateDirectory(WorldRosterDirectory(safe));
        Directory.CreateDirectory(WorldRuntimeDirectory(safe));
    }

    /// <summary>
    /// Strips characters that would escape <see cref="WorldsDirectory"/>. A blank or
    /// purely-invalid id collapses to the literal "_invalid" so we never silently
    /// land at the worlds root itself. Caller is still expected to supply the
    /// real <see cref="Core.Models.ServerInstance.Id"/>; this is a defence-in-depth
    /// scrub, not a substitute for stable ids.
    /// </summary>
    internal static string SanitizeWorldId(string worldId)
    {
        if (string.IsNullOrWhiteSpace(worldId))
        {
            return "_invalid";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. worldId.Where(c => !invalid.Contains(c))]).Trim();
        return cleaned.Length == 0 ? "_invalid" : cleaned;
    }

    public static string ResolveDataRoot(
        string appBaseDirectory,
        string localApplicationDataRoot,
        bool appDirectoryWritable)
        => ResolveDataRoot(null, appBaseDirectory, localApplicationDataRoot, appDirectoryWritable);

    /// <summary>
    /// Resolves the data root with an optional persisted user choice. A non-empty
    /// <paramref name="savedChoice"/> always wins so that, e.g., a user who picked
    /// AppData on first run never sees a portable data folder reappear beside the
    /// exe on the next launch. Pure for unit-testability.
    /// </summary>
    public static string ResolveDataRoot(
        string? savedChoice,
        string appBaseDirectory,
        string localApplicationDataRoot,
        bool appDirectoryWritable)
    {
        if (!string.IsNullOrWhiteSpace(savedChoice))
        {
            return Path.GetFullPath(savedChoice);
        }

        return appDirectoryWritable
            ? Path.Combine(appBaseDirectory, PortableDataFolder)
            : Path.Combine(localApplicationDataRoot, ProductFolder);
    }

    private static string ResolveDefaultDataRoot()
    {
        var savedChoice = DataRootChoiceFile.TryLoad();
        var appBase = AppContext.BaseDirectory;
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var resolved = ResolveDataRoot(savedChoice, appBase, localRoot, CanWriteToDirectory(appBase));

        // First-launch only: pin the auto-detected choice so subsequent launches
        // are deterministic. A user who later picks AppData (or any other path)
        // overwrites this file via the same helper.
        if (string.IsNullOrWhiteSpace(savedChoice))
        {
            DataRootChoiceFile.TrySave(resolved);
        }

        return resolved;
    }

    /// <summary>
    /// True when the path sits inside a known cloud-sync root (OneDrive,
    /// Dropbox, Google Drive). Pure path inspection so it is unit-testable.
    /// </summary>
    public static bool IsSyncedLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith("OneDrive -", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith("OneDrive_", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Dropbox", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Google Drive", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".facility-overseer-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// Persists the user's chosen data-root path in a tiny pointer file under
/// <c>%LOCALAPPDATA%\FacilityOverseer\data-root.txt</c>. The pointer lives
/// at a fixed, known-safe location so it can be consulted before the data
/// root itself is known — and so the user's choice survives moving or
/// republishing the exe.
/// </summary>
public static class DataRootChoiceFile
{
    private const string ProductFolder = "FacilityOverseer";
    private const string FileName = "data-root.txt";

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductFolder,
        FileName);

    public static string? TryLoad(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var content = File.ReadAllText(path).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TrySave(string dataRoot, string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, dataRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort. If we can't write the pointer file, the next launch
            // will simply re-detect; we have not lost user data.
            return false;
        }
    }
}
