namespace AbioticServerManager.Core.Services;

/// <summary>
/// Outcome of a <see cref="IResetManagedDataService"/> run. The report log lists
/// every path that was touched so the user can verify and recover if needed.
/// </summary>
public sealed record ResetManagedDataResult
{
    public required bool Success { get; init; }
    public required string ReportPath { get; init; }
    public required IReadOnlyList<string> RemovedPaths { get; init; }
    public required IReadOnlyList<string> FailedPaths { get; init; }
}

/// <summary>
/// Erases the data Facility Overseer manages — both the durable
/// <c>DataRoot</c> (config, world profiles, logs, backups, players) and the
/// volatile root (SteamCMD + dedicated server install). Quarantine-friendly:
/// the data-root choice pointer at <c>%LOCALAPPDATA%\FacilityOverseer\data-root.txt</c>
/// is left alone so the user keeps their stored preference. Anything outside
/// the managed roots is never touched.
/// </summary>
public interface IResetManagedDataService
{
    Task<ResetManagedDataResult> ResetAsync(CancellationToken ct = default);
}
