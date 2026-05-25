namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// Stages a world's durable Sandbox / Admin INIs into the runtime location
/// inside the server install where AF can find them via a path relative to
/// <c>&lt;install&gt;/AbioticFactor/Saved/</c>, and syncs in-game changes
/// back to the durable copy when the server exits.
/// </summary>
/// <remarks>
/// AF's <c>-SandboxIniPath</c> / <c>-AdminIniPath</c> are hard-prefixed with
/// <c>../../../AbioticFactor/Saved/</c> by the dedicated server; absolute
/// Windows paths produce a malformed lookup and a silent fall-through to
/// default settings. Staging is what keeps the user's saved settings the
/// settings AF actually loads.
/// </remarks>
public interface ISandboxRuntimeStagingService
{
    /// <summary>
    /// Copies durable Sandbox + Admin INIs into the runtime staged folder.
    /// If a durable file does not exist (fresh world), the corresponding
    /// staged file is left absent - AF will write its own defaults. Returns
    /// a snapshot of the staged files' SHA-256 hashes so <see cref="SyncBack"/>
    /// can tell which files AF actually modified during the run.
    /// </summary>
    Task<SandboxStageResult> StageAsync(SandboxLaunchPaths paths, CancellationToken ct = default);

    /// <summary>
    /// After the server process has exited, copies any staged files whose
    /// hash changed since <see cref="StageAsync"/> back to their durable
    /// location. Files unchanged during the run are skipped so we don't
    /// touch the durable copy's modified-time unnecessarily.
    /// </summary>
    Task SyncBackAsync(
        SandboxLaunchPaths paths,
        SandboxStageResult stageResult,
        CancellationToken ct = default);

    /// <summary>
    /// Pushes a freshly-saved durable file to its staged copy WHILE the
    /// server is running. Used by the "Save Sandbox" command so the next
    /// world restart picks up the user's edits without them needing to
    /// re-save after stopping the server.
    /// </summary>
    Task PushDurableToStagedAsync(SandboxLaunchPaths paths, CancellationToken ct = default);
}

/// <summary>
/// Outcome of <see cref="ISandboxRuntimeStagingService.StageAsync"/>. Carries
/// the pre-launch hashes of each staged file so the sync-back step can
/// detect which (if any) AF modified during the run.
/// </summary>
public sealed record SandboxStageResult(
    bool Success,
    string? FailureReason,
    string? SandboxHashBeforeRun,
    string? AdminHashBeforeRun)
{
    public static SandboxStageResult Ok(string? sandboxHash, string? adminHash) =>
        new(true, null, sandboxHash, adminHash);

    public static SandboxStageResult Fail(string reason) =>
        new(false, reason, null, null);
}
