namespace AbioticServerManager.Core.Diagnostics;

/// <summary>
/// One step in a <see cref="RecoveryFlow"/>. <see cref="ActionHint"/> names the
/// observable App command (e.g., <c>CreateFreshWorldCommand</c>) so the UI
/// renders a real button — flows are data, not prose.
/// </summary>
public sealed record RecoveryStep
{
    public required int Order { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }

    /// <summary>Optional command to bind to a button on this step. Empty = informational only.</summary>
    public string ActionHint { get; init; } = "";

    /// <summary>Label for the optional command button. Defaults to a generic verb.</summary>
    public string ActionLabel { get; init; } = "";

    /// <summary>True when this step warns about a destructive action.</summary>
    public bool IsDestructive { get; init; }
}

public sealed record RecoveryFlow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<RecoveryStep> Steps { get; init; }

    /// <summary>The state pattern that triggers the flow, expressed as a tag the App can match on.</summary>
    public required string TriggerTag { get; init; }
}

/// <summary>
/// Data-driven catalog of recovery flows. Pure Core so flows can be reviewed,
/// edited, and unit-tested without touching the UI or the IO layer.
/// </summary>
public static class RecoveryFlows
{
    /// <summary>
    /// World save reports corrupt. Quarantine + create fresh, with the
    /// reassuring "we never delete your save" framing.
    /// </summary>
    public static readonly RecoveryFlow CorruptWorld = new()
    {
        Id = "CORRUPT_WORLD",
        Title = "World save appears to be corrupt",
        Summary =
            "Abiotic Factor reported that the world save can't be loaded. " +
            "Facility Overseer never deletes saves — recovery quarantines the " +
            "corrupt folder so you can investigate, then creates a fresh world " +
            "with your existing sandbox tuning.",
        TriggerTag = "world.corrupt",
        Steps =
        [
            new RecoveryStep
            {
                Order = 1,
                Title = "Stop the server",
                Detail = "Stop the failing server so the world folder can be moved safely.",
                ActionHint = "StopServerCommand",
                ActionLabel = "Stop server",
            },
            new RecoveryStep
            {
                Order = 2,
                Title = "Open the world folder",
                Detail = "Inspect the contents (player save files, sandbox INI) before quarantining.",
                ActionHint = "OpenWorldFolderCommand",
                ActionLabel = "Open folder",
            },
            new RecoveryStep
            {
                Order = 3,
                Title = "Restore from backup (recommended first)",
                Detail =
                    "If a recent backup exists, restoring is the cheapest fix. " +
                    "The Backups tab shows the per-backup confidence (Full / Partial / Limited).",
                ActionHint = "OpenBackupsFolderCommand",
                ActionLabel = "Open backups",
            },
            new RecoveryStep
            {
                Order = 4,
                Title = "Create a fresh world (quarantines the corrupt one)",
                Detail =
                    "Moves the failing world folder to a timestamped quarantine subfolder " +
                    "and starts the server with a clean slate. Your sandbox tuning, " +
                    "admins, and bans (under <DataRoot>/worlds/<id>/) are preserved.",
                ActionHint = "CreateFreshWorldCommand",
                ActionLabel = "Create fresh world",
                IsDestructive = true,
            },
        ],
    };

    /// <summary>
    /// Game or query port could not be bound — another process is holding it.
    /// </summary>
    public static readonly RecoveryFlow PortConflict = new()
    {
        Id = "PORT_CONFLICT",
        Title = "Game / query port is in use",
        Summary =
            "The dedicated server could not bind the configured UDP port. " +
            "Either another process is already listening on it, or a prior " +
            "instance did not release it cleanly.",
        TriggerTag = "port.bind_fail",
        Steps =
        [
            new RecoveryStep
            {
                Order = 1,
                Title = "Confirm no other server is running",
                Detail = "Stop every world on this PC and check Task Manager for stale AbioticFactorServer-Win64-Shipping.exe processes.",
            },
            new RecoveryStep
            {
                Order = 2,
                Title = "Change the game / query port",
                Detail =
                    "On the Server tab, pick a different game port (7777–7780) or query port (27015–27020). " +
                    "Remember to update your router port forwarding to match.",
            },
            new RecoveryStep
            {
                Order = 3,
                Title = "Restart the server",
                Detail = "Use Restart to bind the new port.",
                ActionHint = "RestartServerCommand",
                ActionLabel = "Restart",
            },
        ],
    };

    /// <summary>Dedicated server executable can't be located.</summary>
    public static readonly RecoveryFlow MissingExecutable = new()
    {
        Id = "MISSING_EXECUTABLE",
        Title = "Dedicated server executable not found",
        Summary =
            "Facility Overseer couldn't locate AbioticFactorServer-Win64-Shipping.exe. " +
            "Run Prepare / Update Server to install or repair the dedicated server " +
            "under the managed data root.",
        TriggerTag = "exe.missing",
        Steps =
        [
            new RecoveryStep
            {
                Order = 1,
                Title = "Run Prepare / Update Server",
                Detail = "Downloads SteamCMD (if needed) and installs / validates the dedicated server.",
                ActionHint = "InstallOrUpdateServerCommand",
                ActionLabel = "Prepare / Update Server",
            },
            new RecoveryStep
            {
                Order = 2,
                Title = "Or point at an existing install",
                Detail =
                    "If you already have an Abiotic Factor dedicated server somewhere else, " +
                    "set the Server Folder field on the Server tab to that path.",
            },
        ],
    };

    /// <summary>
    /// SteamCMD reports a self-update failure or steam.dll corruption — usually
    /// caused by syncing tools/ to a OneDrive/Dropbox path. We have a clean reset
    /// path that the App should kick off automatically.
    /// </summary>
    public static readonly RecoveryFlow BrokenSteamCmd = new()
    {
        Id = "BROKEN_STEAMCMD",
        Title = "SteamCMD failed to self-update",
        Summary =
            "SteamCMD reported a 'Failed to load steam.dll' or a self-update error. " +
            "This usually happens when SteamCMD lives in a synced folder " +
            "(OneDrive/Dropbox/Google Drive) — files get locked mid-write. " +
            "Facility Overseer already redirects tools/ off OneDrive automatically; " +
            "the fix is a clean reinstall of SteamCMD.",
        TriggerTag = "steamcmd.broken",
        Steps =
        [
            new RecoveryStep
            {
                Order = 1,
                Title = "Run Prepare / Update Server",
                Detail =
                    "The install pipeline detects 'Failed to load steam.dll' / 'reverting' " +
                    "and does one clean SteamCMD reinstall before retrying. Run it and watch " +
                    "the busy strip.",
                ActionHint = "InstallOrUpdateServerCommand",
                ActionLabel = "Prepare / Update Server",
            },
            new RecoveryStep
            {
                Order = 2,
                Title = "If it still fails, check the report",
                Detail =
                    "A steamcmd-report-*.txt is written under <DataRoot>/logs/ with the " +
                    "actionable error and next steps.",
                ActionHint = "OpenLogsFolderCommand",
                ActionLabel = "Open logs",
            },
        ],
    };

    public static readonly IReadOnlyList<RecoveryFlow> All =
    [
        CorruptWorld,
        PortConflict,
        MissingExecutable,
        BrokenSteamCmd,
    ];

    /// <summary>Convenience lookup by trigger tag, e.g. from <c>ServerHealthSignals.BlockingReason</c>.</summary>
    public static RecoveryFlow? ForTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag)
            ? null
            : All.FirstOrDefault(f =>
                string.Equals(f.TriggerTag, tag, StringComparison.OrdinalIgnoreCase));
}
