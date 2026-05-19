using System.Text.Json;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Folder-based backup store. Each backup is a self-contained directory holding the
/// world save (copied, never moved), the sandbox and admin INI files, the instance
/// profile, and a manifest describing exactly what was captured. A restore takes a
/// <c>pre-restore</c> safety backup before overwriting anything.
/// </summary>
public sealed class FileBackupService : IBackupService
{
    private const string ManifestFileName = "manifest.json";
    private const string InstanceFileName = "instance.json";
    private const string SandboxFileName = "SandboxSettings.ini";
    private const string AdminFileName = "Admins.ini";
    private const string WorldFolderName = "world";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<FileBackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileBackupService(IAppPaths paths, ILogger<FileBackupService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string GetInstanceBackupRoot(ServerInstance instance) =>
        Path.Combine(_paths.BackupsRoot, SanitizeId(instance.Id));

    public async Task<BackupResult> CreateBackupAsync(
        ServerInstance instance,
        string reason,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => CreateBackupCore(instance, reason, ct), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for instance {Id}", instance.Id);
            return BackupResult.Fail("Backup failed: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ListBackupsCore(instance), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupResult> RestoreBackupAsync(
        ServerInstance instance,
        BackupEntry backup,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => RestoreBackupCore(instance, backup, ct), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed for instance {Id}", instance.Id);
            return BackupResult.Fail("Restore failed: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private BackupResult CreateBackupCore(
        ServerInstance instance,
        string reason,
        CancellationToken ct)
    {
        var root = GetInstanceBackupRoot(instance);
        Directory.CreateDirectory(root);

        var createdAt = DateTimeOffset.Now;
        var id = ReserveBackupFolder(root, createdAt, out var folder);

        var hadWorld = false;
        var hadSandbox = false;
        var hadAdmin = false;

        if (!string.IsNullOrWhiteSpace(instance.WorldPath) &&
            Directory.Exists(instance.WorldPath) &&
            DirectoryHasContent(instance.WorldPath))
        {
            CopyDirectory(instance.WorldPath, Path.Combine(folder, WorldFolderName), ct);
            hadWorld = true;
        }

        if (TryCopyFile(instance.SandboxIniPath, Path.Combine(folder, SandboxFileName)))
        {
            hadSandbox = true;
        }

        if (TryCopyFile(instance.AdminIniPath, Path.Combine(folder, AdminFileName)))
        {
            hadAdmin = true;
        }

        File.WriteAllText(
            Path.Combine(folder, InstanceFileName),
            JsonSerializer.Serialize(instance, JsonOptions));

        var manifest = new BackupManifest
        {
            Id = id,
            CreatedAt = createdAt,
            Reason = reason,
            IncludedWorldSave = hadWorld,
            IncludedSandboxIni = hadSandbox,
            IncludedAdminIni = hadAdmin,
            WorldPath = instance.WorldPath,
            SandboxIniPath = instance.SandboxIniPath,
            AdminIniPath = instance.AdminIniPath,
        };
        File.WriteAllText(
            Path.Combine(folder, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));

        var entry = ToEntry(manifest, folder);
        _logger.LogInformation(
            "Created {Reason} backup {Id} for instance {Instance}",
            reason,
            id,
            instance.Id);
        return BackupResult.Ok($"Backup created ({reason}).", entry);
    }

    private IReadOnlyList<BackupEntry> ListBackupsCore(ServerInstance instance)
    {
        var root = GetInstanceBackupRoot(instance);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var entries = new List<BackupEntry>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions);
                if (manifest is not null)
                {
                    entries.Add(ToEntry(manifest, folder));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable backup manifest {Path}", manifestPath);
            }
        }

        return [.. entries.OrderByDescending(e => e.CreatedAt)];
    }

    private BackupResult RestoreBackupCore(
        ServerInstance instance,
        BackupEntry backup,
        CancellationToken ct)
    {
        if (!Directory.Exists(backup.Path))
        {
            return BackupResult.Fail("That backup folder no longer exists.");
        }

        // Snapshot current state first so a mistaken restore is itself recoverable.
        var safety = CreateBackupCore(instance, "pre-restore", ct);
        if (!safety.Success)
        {
            return BackupResult.Fail(
                "Aborted: could not take a safety backup before restoring. " + safety.Message);
        }

        var restored = new List<string>();

        var worldBackup = Path.Combine(backup.Path, WorldFolderName);
        if (Directory.Exists(worldBackup) && !string.IsNullOrWhiteSpace(instance.WorldPath))
        {
            if (Directory.Exists(instance.WorldPath))
            {
                Directory.Delete(instance.WorldPath, recursive: true);
            }

            CopyDirectory(worldBackup, instance.WorldPath, ct);
            restored.Add("world save");
        }

        if (RestoreFile(Path.Combine(backup.Path, SandboxFileName), instance.SandboxIniPath))
        {
            restored.Add("SandboxSettings.ini");
        }

        if (RestoreFile(Path.Combine(backup.Path, AdminFileName), instance.AdminIniPath))
        {
            restored.Add("Admins.ini");
        }

        if (restored.Count == 0)
        {
            return BackupResult.Fail(
                "Nothing was restored. The backup had no matching target paths on this profile.");
        }

        var summary = string.Join(", ", restored);
        _logger.LogInformation(
            "Restored {Items} for instance {Instance} from backup {Backup}",
            summary,
            instance.Id,
            backup.Id);
        return BackupResult.Ok(
            $"Restored {summary}. A 'pre-restore' safety backup of the previous state was kept.");
    }

    private static string ReserveBackupFolder(
        string root,
        DateTimeOffset createdAt,
        out string folder)
    {
        var baseId = createdAt.ToString("yyyyMMdd-HHmmss");
        var id = baseId;
        var attempt = 1;
        folder = Path.Combine(root, id);
        while (Directory.Exists(folder))
        {
            id = $"{baseId}-{attempt++}";
            folder = Path.Combine(root, id);
        }

        Directory.CreateDirectory(folder);
        return id;
    }

    private static bool TryCopyFile(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return false;
        }

        File.Copy(source, destination, overwrite: true);
        return true;
    }

    private static bool RestoreFile(string backupFile, string? targetPath)
    {
        if (!File.Exists(backupFile) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Copy(backupFile, targetPath, overwrite: true);
        return true;
    }

    private static void CopyDirectory(string sourceDir, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destDir, relative);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool DirectoryHasContent(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Any();

    private static long DirectorySize(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // A vanished temp file should not break size reporting.
            }
        }

        return total;
    }

    private static BackupEntry ToEntry(BackupManifest manifest, string folder) => new()
    {
        Id = manifest.Id,
        Path = folder,
        CreatedAt = manifest.CreatedAt,
        Reason = manifest.Reason,
        SizeBytes = DirectorySize(folder),
        IncludedWorldSave = manifest.IncludedWorldSave,
        IncludedSandboxIni = manifest.IncludedSandboxIni,
        IncludedAdminIni = manifest.IncludedAdminIni,
    };

    private static string SanitizeId(string id)
    {
        var cleaned = new string([.. id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private sealed record BackupManifest
    {
        public required string Id { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required string Reason { get; init; }
        public bool IncludedWorldSave { get; init; }
        public bool IncludedSandboxIni { get; init; }
        public bool IncludedAdminIni { get; init; }
        public string WorldPath { get; init; } = "";
        public string SandboxIniPath { get; init; } = "";
        public string AdminIniPath { get; init; } = "";
    }
}
