namespace AbioticServerManager.Core.Services;

public sealed record LegacyFinding
{
    public required string Root { get; init; }
    public bool HasInstances { get; init; }
    public bool HasSettings { get; init; }
    public bool HasLogs { get; init; }
    public bool HasBackups { get; init; }
    public bool HasServer { get; init; }

    public bool HasAnything =>
        HasInstances || HasSettings || HasLogs || HasBackups || HasServer;
}

public sealed record LegacyMigrationResult
{
    public required bool ImportedConfig { get; init; }
    public required string ReportPath { get; init; }
    public IReadOnlyList<LegacyFinding> Findings { get; init; } = [];
}

/// <summary>
/// Conservative migration from old data roots. Never deletes or moves source
/// data; only COPIES small config into a fresh root and always writes a report.
/// Large server payloads are reported for manual adoption, not bulk-copied.
/// </summary>
public interface ILegacyMigrationService
{
    /// <summary>True only when there is something worth offering to import.</summary>
    bool ShouldOfferMigration(out IReadOnlyList<LegacyFinding> findings);

    Task<LegacyMigrationResult> MigrateAsync(
        IReadOnlyList<LegacyFinding> findings, CancellationToken ct = default);
}
