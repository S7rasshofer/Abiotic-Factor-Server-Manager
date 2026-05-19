using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Diagnostics;

/// <summary>
/// Splits server health into independent signals. The plan is emphatic that a single
/// "online" dot is misleading: process, config, local query and external reachability
/// are reported separately and honestly (Unknown is a valid, useful answer).
/// </summary>
public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticMessage>> ValidateConfigAsync(
        ServerInstance instance,
        IReadOnlyList<ServerInstance> otherInstances,
        CancellationToken ct = default);

    Task<QueryCheckResult> CheckLocalQueryAsync(ServerInstance instance, CancellationToken ct = default);

    Task<ExternalVisibilityResult> CheckExternalVisibilityAsync(
        ServerInstance instance,
        CancellationToken ct = default);

    Task<VersionCheckResult> CheckVersionAsync(ServerInstance instance, CancellationToken ct = default);
}
