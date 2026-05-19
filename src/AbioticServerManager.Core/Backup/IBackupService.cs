using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Backup;

/// <summary>
/// Captures and restores per-world backups (world save folder, SandboxSettings.ini,
/// Admins.ini, and the instance profile) under
/// <c>&lt;DataRoot&gt;/backups/&lt;instance-id&gt;/&lt;timestamp&gt;/</c>.
/// Backups always copy, never move, and a restore first snapshots current state so a
/// mistaken restore is itself recoverable. "Discover it, do not hardcode it."
/// </summary>
public interface IBackupService
{
    /// <summary>Absolute path of the folder holding every backup for one instance.</summary>
    string GetInstanceBackupRoot(ServerInstance instance);

    Task<BackupResult> CreateBackupAsync(
        ServerInstance instance,
        string reason,
        CancellationToken ct = default);

    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(
        ServerInstance instance,
        CancellationToken ct = default);

    Task<BackupResult> RestoreBackupAsync(
        ServerInstance instance,
        BackupEntry backup,
        CancellationToken ct = default);
}
