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

    /// <summary>
    /// Root for durable per-world config/saves/roster/runtime. Sits in
    /// <see cref="DataRoot"/> so it survives a SteamCMD validate or a
    /// <c>%LOCALAPPDATA%</c> wipe — i.e. the server install can be replaced
    /// at will without losing world tuning, admins or bans (§2.1).
    /// </summary>
    string WorldsDirectory { get; }

    /// <summary>The world's durable root: <c>DataRoot/worlds/&lt;id&gt;</c>.</summary>
    string WorldDirectory(string worldId);

    /// <summary>The world's durable config folder: <c>DataRoot/worlds/&lt;id&gt;/config</c>.</summary>
    string WorldConfigDirectory(string worldId);

    /// <summary>
    /// Canonical absolute path of this world's <c>SandboxSettings.ini</c>.
    /// Always under <see cref="WorldConfigDirectory"/>; the server install
    /// is treated as runtime only.
    /// </summary>
    string WorldSandboxIniPath(string worldId);

    /// <summary>
    /// Canonical absolute path of this world's <c>Admin.ini</c> (sectioned
    /// admins + bans). Always under <see cref="WorldConfigDirectory"/>.
    /// </summary>
    string WorldAdminIniPath(string worldId);

    /// <summary>
    /// Path of this world's lightweight metadata sidecar
    /// (<c>config/metadata.json</c>): name, created, last-played, fingerprint.
    /// </summary>
    string WorldMetadataJsonPath(string worldId);

    /// <summary>Room for future per-world backups/exports.</summary>
    string WorldSavesDirectory(string worldId);

    /// <summary>Per-world roster folder (room to move <c>roster.json</c> here later).</summary>
    string WorldRosterDirectory(string worldId);

    /// <summary>Transient cache/temp; safe to nuke on reset.</summary>
    string WorldRuntimeDirectory(string worldId);

    /// <summary>Creates every root directory if it does not already exist.</summary>
    void EnsureCreated();

    /// <summary>
    /// Creates the per-world durable folders (<c>config/</c>, <c>saves/</c>,
    /// <c>roster/</c>, <c>runtime/</c>) under <see cref="WorldDirectory"/>.
    /// Pure path I/O — does not touch the server install.
    /// </summary>
    void EnsureWorldCreated(string worldId);
}
