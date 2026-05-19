using System.Text;
using AbioticServerManager.Core.Migration;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Migration;

/// <summary>
/// Conservative legacy migration: never deletes or moves the user's old data.
/// It only copies the small config (instances/settings) into a fresh data root
/// and always writes a human-readable report. Large server payloads are listed
/// for manual adoption via the Server Folder field rather than bulk-copied.
/// </summary>
public sealed class LegacyMigrationService : ILegacyMigrationService
{
    private const string MarkerName = ".legacy-migration-done";

    private readonly IAppPaths _paths;
    private readonly ILogger<LegacyMigrationService> _logger;

    public LegacyMigrationService(IAppPaths paths, ILogger<LegacyMigrationService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public bool ShouldOfferMigration(out IReadOnlyList<LegacyFinding> findings)
    {
        findings = Detect();

        if (File.Exists(MarkerPath) || File.Exists(_paths.InstancesFile))
        {
            // Already migrated, or the current root already has profiles: do not
            // offer (and never overwrite existing config).
            return false;
        }

        return findings.Any(f => f.HasInstances);
    }

    public async Task<LegacyMigrationResult> MigrateAsync(
        IReadOnlyList<LegacyFinding> findings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.ConfigDirectory);
        Directory.CreateDirectory(_paths.LogsDirectory);

        var report = new StringBuilder();
        report.AppendLine("Facility Overseer - legacy data migration report");
        report.AppendLine("================================================");
        report.AppendLine($"When        : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Data root   : {_paths.DataRoot}");
        report.AppendLine("Policy      : copy small config only; never move or delete source.");
        report.AppendLine();

        var importedConfig = false;

        foreach (var f in findings)
        {
            report.AppendLine($"Found legacy root: {f.Root}");
            report.AppendLine(
                $"  instances={f.HasInstances} settings={f.HasSettings} " +
                $"logs={f.HasLogs} backups={f.HasBackups} server={f.HasServer}");

            // Only import config when the current root has none (no overwrite).
            if (!importedConfig && f.HasInstances && !File.Exists(_paths.InstancesFile))
            {
                importedConfig |= TryCopy(
                    FindFile(f.Root, "instances.json"), _paths.InstancesFile, report);
                TryCopy(FindFile(f.Root, "settings.json"), _paths.SettingsFile, report);
            }

            if (f.HasServer)
            {
                report.AppendLine(
                    "  NOTE: server files were NOT copied (large). Point the Server " +
                    "Folder field at this path to adopt them.");
            }

            report.AppendLine();
        }

        report.AppendLine(
            "Old folders were left exactly as they were. You can delete them yourself " +
            "once you have confirmed everything works.");

        var reportPath = Path.Combine(
            _paths.LogsDirectory,
            $"migration-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        try
        {
            await File.WriteAllTextAsync(reportPath, report.ToString(), ct)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.Now.ToString("o"), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not finish writing the migration report/marker");
        }

        return new LegacyMigrationResult
        {
            ImportedConfig = importedConfig,
            ReportPath = reportPath,
            Findings = findings,
        };
    }

    private string MarkerPath => Path.Combine(_paths.ConfigDirectory, MarkerName);

    private IReadOnlyList<LegacyFinding> Detect()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var currentRoots = new[]
        {
            Norm(_paths.DataRoot),
            Norm(Path.GetDirectoryName(_paths.SteamCmdDirectory) ?? ""), // VolatileRoot/tools -> root-ish
        };

        var results = new List<LegacyFinding>();
        foreach (var candidate in LegacyDataLocations.Candidates(roaming, local))
        {
            try
            {
                if (!Directory.Exists(candidate) ||
                    currentRoots.Contains(Norm(candidate), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var finding = new LegacyFinding
                {
                    Root = candidate,
                    HasInstances = FindFile(candidate, "instances.json") is not null,
                    HasSettings = FindFile(candidate, "settings.json") is not null,
                    HasLogs = Directory.Exists(Path.Combine(candidate, "logs")),
                    HasBackups = Directory.Exists(Path.Combine(candidate, "backups")),
                    HasServer = HasServerExecutable(candidate),
                };

                if (finding.HasAnything)
                {
                    results.Add(finding);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not inspect legacy root {Root}", candidate);
            }
        }

        return results;
    }

    private static string Norm(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? ""
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? FindFile(string root, string name)
    {
        var atRoot = Path.Combine(root, name);
        if (File.Exists(atRoot))
        {
            return atRoot;
        }

        var atConfig = Path.Combine(root, "config", name);
        return File.Exists(atConfig) ? atConfig : null;
    }

    private static bool HasServerExecutable(string root)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, "AbioticFactorServer*.exe", SearchOption.AllDirectories)
                .Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool TryCopy(string? source, string destination, StringBuilder report)
    {
        if (source is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            report.AppendLine($"  COPIED {source} -> {destination}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.AppendLine($"  COPY FAILED {source}: {ex.Message}");
            _logger.LogWarning(ex, "Legacy copy failed for {Source}", source);
            return false;
        }
    }
}
