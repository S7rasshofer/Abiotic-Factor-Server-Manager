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
    }

    /// <summary>
    /// The single managed root. Everything the app owns - config, logs, backups,
    /// rosters, SteamCMD and the dedicated server - lives under this one folder.
    /// </summary>
    public string DataRoot { get; }

    public string ConfigDirectory => Path.Combine(DataRoot, "config");

    public string ToolsDirectory => Path.Combine(DataRoot, "tools");

    public string ServersDirectory => Path.Combine(DataRoot, "servers");

    public string ManagedServerDirectory => Path.Combine(ServersDirectory, "abiotic-factor-dedicated");

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string InstancesFile => Path.Combine(ConfigDirectory, "instances.json");

    public string SchemaCacheFile => Path.Combine(ConfigDirectory, "schema-cache.json");

    public string SteamCmdDirectory => Path.Combine(ToolsDirectory, "steamcmd");

    public string DefaultServerInstallDirectory => ManagedServerDirectory;

    public string BackupsRoot => Path.Combine(DataRoot, "backups");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public string PlayersDirectory => Path.Combine(DataRoot, "players");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(SteamCmdDirectory);
        Directory.CreateDirectory(ServersDirectory);
        Directory.CreateDirectory(BackupsRoot);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Decides the one managed root. The portable folder beside the exe is used
    /// only when that location is both writable and NOT cloud-synced. OneDrive,
    /// Dropbox and Google Drive lock/dehydrate files mid-write, which corrupts
    /// SteamCMD's <c>steam.dll</c>; in that case the whole root falls back to a
    /// plain local folder so nothing is split across two places.
    /// </summary>
    public static string ResolveDataRoot(
        string appBaseDirectory,
        string localApplicationDataRoot,
        bool appDirectoryWritable,
        bool appDirectorySynced)
    {
        return appDirectoryWritable && !appDirectorySynced
            ? Path.Combine(appBaseDirectory, PortableDataFolder)
            : Path.Combine(localApplicationDataRoot, ProductFolder);
    }

    private static string ResolveDefaultDataRoot()
    {
        var appBase = AppContext.BaseDirectory;
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ResolveDataRoot(
            appBase,
            localRoot,
            CanWriteToDirectory(appBase),
            IsSyncedLocation(appBase));
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
