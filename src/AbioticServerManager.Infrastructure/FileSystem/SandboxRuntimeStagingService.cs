using System.Security.Cryptography;
using AbioticServerManager.Core.Worlds;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.FileSystem;

/// <summary>
/// Concrete implementation of <see cref="ISandboxRuntimeStagingService"/>.
/// Owns the filesystem IO; all pure path math lives in
/// <see cref="SandboxLaunchPaths"/>.
/// </summary>
public sealed class SandboxRuntimeStagingService : ISandboxRuntimeStagingService
{
    private readonly ILogger<SandboxRuntimeStagingService> _logger;

    public SandboxRuntimeStagingService(ILogger<SandboxRuntimeStagingService> logger) =>
        _logger = logger;

    public async Task<SandboxStageResult> StageAsync(
        SandboxLaunchPaths paths,
        CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(paths.StagedFolder))
            {
                Directory.CreateDirectory(paths.StagedFolder);
            }

            await CopyIfDurableExistsAsync(
                paths.DurableSandboxPath, paths.StagedSandboxPath, "SandboxSettings.ini", ct)
                .ConfigureAwait(false);

            await CopyIfDurableExistsAsync(
                paths.DurableAdminPath, paths.StagedAdminPath, "Admin.ini", ct)
                .ConfigureAwait(false);

            var sandboxHash = await TryHashAsync(paths.StagedSandboxPath, ct).ConfigureAwait(false);
            var adminHash = await TryHashAsync(paths.StagedAdminPath, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Staged world INIs into {Folder} (sandbox: {SandboxHas}, admin: {AdminHas}).",
                paths.StagedFolder,
                sandboxHash is null ? "absent" : "present",
                adminHash is null ? "absent" : "present");

            return SandboxStageResult.Ok(sandboxHash, adminHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "Failed to stage world INIs into {Folder}",
                paths.StagedFolder);
            return SandboxStageResult.Fail(
                "Could not stage world settings into the server install. " +
                $"Reason: {ex.Message}");
        }
    }

    public async Task SyncBackAsync(
        SandboxLaunchPaths paths,
        SandboxStageResult stageResult,
        CancellationToken ct = default)
    {
        if (!stageResult.Success)
        {
            // Nothing was staged - nothing to sync back.
            return;
        }

        await SyncOneAsync(
            paths.StagedSandboxPath,
            paths.DurableSandboxPath,
            stageResult.SandboxHashBeforeRun,
            "SandboxSettings.ini",
            ct).ConfigureAwait(false);

        await SyncOneAsync(
            paths.StagedAdminPath,
            paths.DurableAdminPath,
            stageResult.AdminHashBeforeRun,
            "Admin.ini",
            ct).ConfigureAwait(false);
    }

    public async Task PushDurableToStagedAsync(
        SandboxLaunchPaths paths,
        CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(paths.StagedFolder))
            {
                Directory.CreateDirectory(paths.StagedFolder);
            }

            await CopyIfDurableExistsAsync(
                paths.DurableSandboxPath, paths.StagedSandboxPath, "SandboxSettings.ini", ct)
                .ConfigureAwait(false);

            await CopyIfDurableExistsAsync(
                paths.DurableAdminPath, paths.StagedAdminPath, "Admin.ini", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not push durable INIs into staged folder {Folder}; the next world " +
                "restart will pick them up via the normal stage step.",
                paths.StagedFolder);
        }
    }

    private async Task SyncOneAsync(
        string stagedPath,
        string durablePath,
        string? hashBefore,
        string label,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagedPath) || string.IsNullOrWhiteSpace(durablePath))
        {
            return;
        }

        if (!File.Exists(stagedPath))
        {
            // AF never wrote one; nothing to sync.
            return;
        }

        var hashAfter = await TryHashAsync(stagedPath, ct).ConfigureAwait(false);
        if (hashAfter is null)
        {
            return; // hash failed; safest to skip rather than overwrite durable with unknown
        }

        if (string.Equals(hashBefore, hashAfter, StringComparison.Ordinal))
        {
            // Unchanged during the run - leave the durable copy alone.
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(durablePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(stagedPath, durablePath, overwrite: true);
            _logger.LogInformation(
                "Synced {Label} from runtime back to durable (AF modified it during the run).",
                label);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not sync {Label} from {Staged} back to {Durable}",
                label, stagedPath, durablePath);
        }
    }

    private static async Task CopyIfDurableExistsAsync(
        string durablePath,
        string stagedPath,
        string label,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(durablePath) || string.IsNullOrWhiteSpace(stagedPath))
        {
            return;
        }

        if (!File.Exists(durablePath))
        {
            // Fresh world / file not authored yet. AF will write defaults.
            return;
        }

        // Async copy so the launch path isn't synchronously blocked on a
        // potentially-slow drive (OneDrive offline files, network shares).
        await using var source = new FileStream(
            durablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using var dest = new FileStream(
            stagedPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await source.CopyToAsync(dest, ct).ConfigureAwait(false);
        _ = label; // logged at the StageAsync level once both copies finish
    }

    private static async Task<string?> TryHashAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexString(hashBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
