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

    public static string ResolveDataRoot(
        string appBaseDirectory,
        string localApplicationDataRoot,
        bool appDirectoryWritable)
    {
        return appDirectoryWritable
            ? Path.Combine(appBaseDirectory, PortableDataFolder)
            : Path.Combine(localApplicationDataRoot, ProductFolder);
    }

    private static string ResolveDefaultDataRoot()
    {
        var appBase = AppContext.BaseDirectory;
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ResolveDataRoot(appBase, localRoot, CanWriteToDirectory(appBase));
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
