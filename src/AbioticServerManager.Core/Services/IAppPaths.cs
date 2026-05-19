namespace AbioticServerManager.Core.Services;

/// <summary>
/// Centralises every well-known folder the app uses. New installs use one canonical
/// data root so the portable app can manage its own mutable files without scattering
/// state across Windows profile and drive-root folders.
/// </summary>
public interface IAppPaths
{
    /// <summary>The single managed root for config, tools, logs, backups and server files.</summary>
    string DataRoot { get; }

    string ConfigDirectory { get; }
    string ToolsDirectory { get; }
    string ServersDirectory { get; }
    string ManagedServerDirectory { get; }

    /// <summary>Compatibility alias for legacy code and migration from old roaming state.</summary>
    string AppDataRoot { get; }

    /// <summary>Compatibility alias for legacy code and migration from old local state.</summary>
    string LocalAppDataRoot { get; }

    string SettingsFile { get; }
    string InstancesFile { get; }
    string SchemaCacheFile { get; }

    string SteamCmdDirectory { get; }
    string DefaultServerInstallDirectory { get; }
    string BackupsRoot { get; }
    string LogsDirectory { get; }

    /// <summary>Durable per-world player roster state (small, not rewrite-heavy).</summary>
    string PlayersDirectory { get; }

    /// <summary>Creates every root directory if it does not already exist.</summary>
    void EnsureCreated();
}
