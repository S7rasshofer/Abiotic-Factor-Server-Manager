using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Services;

/// <summary>
/// Outcome of a §2.1 migration pass for one world. Lists what was copied so
/// the per-world migration log can record exactly which files moved location
/// (the original is always left in place — Reset Managed Data cleans up).
/// </summary>
public sealed record WorldIdentityMigrationResult
{
    public required bool HadWork { get; init; }
    public required IReadOnlyList<string> CopiedDescriptions { get; init; }
    public required IReadOnlyList<string> FailedDescriptions { get; init; }
    public required string? ReportPath { get; init; }
}

/// <summary>
/// Copies per-world <c>SandboxSettings.ini</c> + <c>Admin.ini</c> from their
/// legacy location inside the server install to the canonical
/// <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/config/</c> location. Updates the
/// <see cref="ServerInstance.SandboxIniPath"/> / <see cref="ServerInstance.AdminIniPath"/>
/// fields to the new canonical paths so downstream services pick them up.
/// COPY-NOT-DELETE: the original files are never moved or removed.
/// </summary>
public interface IWorldIdentityMigrationService
{
    Task<WorldIdentityMigrationResult> MigrateIfNeededAsync(
        ServerInstance instance,
        CancellationToken ct = default);
}
