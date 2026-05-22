using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.FileSystem;

/// <summary>
/// Implements <see cref="IResetManagedDataService"/>. Only operates inside the
/// active <c>DataRoot</c> and <c>VolatileRoot</c> — never an arbitrary path.
/// Writes a per-reset report to the in-place logs directory so the user can
/// review what was removed. The data-root choice pointer is preserved so the
/// user keeps the location they picked on first run.
/// </summary>
public sealed class ResetManagedDataService : IResetManagedDataService
{
    private readonly AppPaths _paths;
    private readonly ILogger<ResetManagedDataService> _logger;

    public ResetManagedDataService(IAppPaths paths, ILogger<ResetManagedDataService> logger)
    {
        // The IAppPaths abstraction exposes DataRoot but not VolatileRoot; cast to
        // the concrete to reach the latter. Acceptable because reset is a single
        // implementation tied to this layout.
        _paths = paths as AppPaths
            ?? throw new InvalidOperationException(
                "ResetManagedDataService requires the concrete AppPaths to access VolatileRoot.");
        _logger = logger;
    }

    public Task<ResetManagedDataResult> ResetAsync(CancellationToken ct = default)
    {
        var removed = new List<string>();
        var failed = new List<string>();

        // Capture the report path BEFORE clearing so we can still write it after
        // the logs directory itself is gone (we recreate it).
        var reportPath = Path.Combine(
            _paths.LogsDirectory,
            $"reset-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");

        // DataRoot first (durable side): clear children but keep the root itself
        // so logs/ can be recreated for the report.
        ClearChildren(_paths.DataRoot, removed, failed, ct);

        // VolatileRoot may equal DataRoot (non-synced location) — only walk it
        // separately when it's a different path.
        if (!string.Equals(
                Path.GetFullPath(_paths.DataRoot),
                Path.GetFullPath(_paths.VolatileRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            ClearChildren(_paths.VolatileRoot, removed, failed, ct);
        }

        // Recreate canonical layout so the next launch starts cleanly.
        _paths.EnsureCreated();
        TryWriteReport(reportPath, removed, failed);

        return Task.FromResult(new ResetManagedDataResult
        {
            Success = failed.Count == 0,
            ReportPath = reportPath,
            RemovedPaths = removed,
            FailedPaths = failed,
        });
    }

    private void ClearChildren(string root, List<string> removed, List<string> failed, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var child in Directory.EnumerateFileSystemEntries(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.Exists(child))
                    {
                        Directory.Delete(child, recursive: true);
                    }
                    else
                    {
                        File.Delete(child);
                    }

                    removed.Add(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not remove {Path} during reset", child);
                    failed.Add(child + " — " + ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate {Path} during reset", root);
            failed.Add(root + " — " + ex.Message);
        }
    }

    private void TryWriteReport(string reportPath, List<string> removed, List<string> failed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            using var writer = new StreamWriter(reportPath);
            writer.WriteLine($"Facility Overseer — Reset Managed Data Report");
            writer.WriteLine($"Generated: {DateTimeOffset.UtcNow:O}");
            writer.WriteLine($"DataRoot:     {_paths.DataRoot}");
            writer.WriteLine($"VolatileRoot: {_paths.VolatileRoot}");
            writer.WriteLine();
            writer.WriteLine($"Removed ({removed.Count}):");
            foreach (var path in removed)
            {
                writer.WriteLine("  " + path);
            }

            if (failed.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine($"Failed ({failed.Count}):");
                foreach (var path in failed)
                {
                    writer.WriteLine("  " + path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write reset report to {Path}", reportPath);
        }
    }
}
