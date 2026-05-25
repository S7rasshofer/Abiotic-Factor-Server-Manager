using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Core.Worlds;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.FileSystem;

/// <summary>
/// Concrete Sec 2.1 migration runner. Pure planning lives in
/// <see cref="WorldIdentityMigration"/>; this class adds the IO (copy + log).
/// Idempotent: re-running is a no-op once the new files exist and are at
/// least as new as the legacy ones.
/// </summary>
public sealed class WorldIdentityMigrationService : IWorldIdentityMigrationService
{
    private readonly IAppPaths _paths;
    private readonly ILogger<WorldIdentityMigrationService> _logger;

    public WorldIdentityMigrationService(
        IAppPaths paths,
        ILogger<WorldIdentityMigrationService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public Task<WorldIdentityMigrationResult> MigrateIfNeededAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instance.Id))
        {
            return Task.FromResult(Empty());
        }

        var targetSandbox = _paths.WorldSandboxIniPath(instance.Id);
        var targetAdmin = _paths.WorldAdminIniPath(instance.Id);
        var legacySandbox = ResolveLegacySandboxPath(instance);
        var legacyAdmin = ResolveLegacyAdminPath(instance);

        var plan = WorldIdentityMigration.Plan(
            legacySandboxPath: legacySandbox,
            legacyAdminPath: legacyAdmin,
            targetSandboxPath: targetSandbox,
            targetAdminPath: targetAdmin,
            fileExists: File.Exists,
            writeTime: SafeWriteTime);

        // ALWAYS point the model at the canonical in-DataRoot paths so launch
        // args + downstream services use them, even when nothing physical was
        // copied (fresh world, or migration ran on a previous launch).
        instance.SandboxIniPath = targetSandbox;
        instance.AdminIniPath = targetAdmin;

        if (!plan.HasWork)
        {
            // Ensure the world's config directory exists so writes do not need
            // a CreateDirectory hop later.
            try
            {
                if (_paths is AppPaths concrete)
                {
                    concrete.EnsureWorldCreated(instance.Id);
                }
                else
                {
                    Directory.CreateDirectory(_paths.WorldConfigDirectory(instance.Id));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not create world config dir for {Id}", instance.Id);
            }

            return Task.FromResult(new WorldIdentityMigrationResult
            {
                HadWork = false,
                CopiedDescriptions = [],
                FailedDescriptions = [],
                ReportPath = null,
            });
        }

        var copied = new List<string>();
        var failed = new List<string>();

        try
        {
            if (_paths is AppPaths concrete)
            {
                concrete.EnsureWorldCreated(instance.Id);
            }
            else
            {
                Directory.CreateDirectory(_paths.WorldConfigDirectory(instance.Id));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not create world directory for {Id}", instance.Id);
        }

        TryCopy(plan.Sandbox, copied, failed);
        TryCopy(plan.Admin, copied, failed);

        var reportPath = TryWriteReport(instance.Id, plan, copied, failed);

        return Task.FromResult(new WorldIdentityMigrationResult
        {
            HadWork = true,
            CopiedDescriptions = copied,
            FailedDescriptions = failed,
            ReportPath = reportPath,
        });
    }

    private static WorldIdentityMigrationResult Empty() => new()
    {
        HadWork = false,
        CopiedDescriptions = [],
        FailedDescriptions = [],
        ReportPath = null,
    };

    private static string? ResolveLegacySandboxPath(ServerInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.SandboxIniPath))
        {
            return null;
        }

        // The new in-DataRoot path is the migration TARGET, not the source.
        // Recognise an already-migrated value (or an empty placeholder) as "no legacy".
        var looksLikeNewLayout =
            instance.SandboxIniPath.Replace('\\', '/')
                .Contains("/worlds/" + instance.Id + "/config/", StringComparison.OrdinalIgnoreCase);

        return looksLikeNewLayout ? null : instance.SandboxIniPath;
    }

    private static string? ResolveLegacyAdminPath(ServerInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.AdminIniPath))
        {
            // Fall back to the common server-install location so existing worlds
            // migrate even when only the install was tracked.
            if (!string.IsNullOrWhiteSpace(instance.InstallPath))
            {
                return Path.Combine(
                    instance.InstallPath,
                    "AbioticFactor",
                    "Saved",
                    "SaveGames",
                    "Server",
                    "Admin.ini");
            }

            return null;
        }

        var looksLikeNewLayout =
            instance.AdminIniPath.Replace('\\', '/')
                .Contains("/worlds/" + instance.Id + "/config/", StringComparison.OrdinalIgnoreCase);

        return looksLikeNewLayout ? null : instance.AdminIniPath;
    }

    private void TryCopy(WorldIdentityMigrationStep step, List<string> copied, List<string> failed)
    {
        if (step.Action != WorldIdentityMigrationAction.CopyLegacyToTarget)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(step.LegacyPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(step.TargetPath)!);
            File.Copy(step.LegacyPath, step.TargetPath, overwrite: false);
            copied.Add($"{step.Description}: {step.LegacyPath} -> {step.TargetPath}");
            _logger.LogInformation(
                "Sec 2.1 migration: copied {Description} from {Legacy} to {Target}",
                step.Description, step.LegacyPath, step.TargetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Sec 2.1 migration: copy failed for {Description}", step.Description);
            failed.Add($"{step.Description}: {ex.Message}");
        }
    }

    private string? TryWriteReport(
        string worldId,
        WorldIdentityMigrationPlan plan,
        List<string> copied,
        List<string> failed)
    {
        try
        {
            var dir = _paths.WorldDirectory(worldId);
            Directory.CreateDirectory(dir);
            var reportPath = Path.Combine(
                dir,
                $"migration-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");

            using var writer = new StreamWriter(reportPath);
            writer.WriteLine("Facility Overseer - Sec 2.1 World Identity Migration");
            writer.WriteLine($"Generated: {DateTimeOffset.UtcNow:O}");
            writer.WriteLine($"World id:  {worldId}");
            writer.WriteLine();
            writer.WriteLine("Plan:");
            writer.WriteLine($"  Sandbox: {plan.Sandbox.Action}  legacy={plan.Sandbox.LegacyPath}  target={plan.Sandbox.TargetPath}");
            writer.WriteLine($"  Admin:   {plan.Admin.Action}  legacy={plan.Admin.LegacyPath}  target={plan.Admin.TargetPath}");
            writer.WriteLine();
            writer.WriteLine($"Copied ({copied.Count}):");
            foreach (var line in copied)
            {
                writer.WriteLine("  " + line);
            }

            if (failed.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine($"Failed ({failed.Count}):");
                foreach (var line in failed)
                {
                    writer.WriteLine("  " + line);
                }
            }

            return reportPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write Sec 2.1 migration report for {Id}", worldId);
            return null;
        }
    }

    private static DateTimeOffset? SafeWriteTime(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
