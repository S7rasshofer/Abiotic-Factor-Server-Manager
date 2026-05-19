namespace AbioticServerManager.Core.Install;

public enum InstallPhase
{
    Idle,
    DownloadingSteamCmd,
    ExtractingSteamCmd,
    UpdatingSteamCmd,
    InstallingServer,
    ValidatingServer,
    Completed,
    Failed,
}

/// <summary>A progress tick for the install/update pipeline. Surfaced to the install UI.</summary>
public sealed record InstallProgress
{
    public required InstallPhase Phase { get; init; }

    /// <summary>0-100 when known, null when the underlying tool reports no percentage.</summary>
    public double? PercentComplete { get; init; }

    /// <summary>A short human-readable status, e.g. "Downloading SteamCMD".</summary>
    public string Status { get; init; } = "";

    /// <summary>The most recent raw output line from the tool, if any.</summary>
    public string? OutputLine { get; init; }
}
