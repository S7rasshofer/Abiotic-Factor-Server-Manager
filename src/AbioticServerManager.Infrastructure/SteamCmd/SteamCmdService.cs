using System.IO.Compression;
using System.Text;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Infrastructure.FileSystem;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.SteamCmd;

public sealed class SteamCmdService : ISteamCmdService
{
    private const string SteamCmdZipUrl =
        "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<SteamCmdService> _logger;

    public SteamCmdService(IAppPaths paths, ILogger<SteamCmdService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private string SteamCmdExe => Path.Combine(_paths.SteamCmdDirectory, "steamcmd.exe");

    public Task<bool> IsSteamCmdInstalledAsync(CancellationToken ct = default) =>
        Task.FromResult(File.Exists(SteamCmdExe));

    public async Task<SteamCmdResult> InstallSteamCmdAsync(
        IProgress<InstallProgress> progress,
        CancellationToken ct = default)
    {
        try
        {
            await ExtractFreshSteamCmdAsync(progress, ct).ConfigureAwait(false);
            if (!File.Exists(SteamCmdExe))
            {
                progress.Report(new InstallProgress
                {
                    Phase = InstallPhase.Failed,
                    Status = "SteamCMD setup failed",
                });
                return SteamCmdResult.Fail(-1, "", "steamcmd.exe was not found after extraction.");
            }

            progress.Report(new InstallProgress
            {
                Phase = InstallPhase.UpdatingSteamCmd,
                Status = "Updating SteamCMD (first run)",
            });

            // The first run bootstraps and self-updates SteamCMD. It commonly exits
            // non-zero (notably 7) then works next time, so the bootstrap exit code is
            // NOT a reliable success signal. Run it, retry once, then judge by whether
            // steamcmd.exe survived and did not hit the self-update death spiral.
            var bootstrap = await RunSteamCmdAsync(["+quit"], InstallPhase.UpdatingSteamCmd, progress, ct)
                .ConfigureAwait(false);
            if (!bootstrap.Success)
            {
                _logger.LogInformation(
                    "SteamCMD bootstrap exited with code {Code}; retrying once (expected on first run)",
                    bootstrap.ExitCode);
                bootstrap = await RunSteamCmdAsync(["+quit"], InstallPhase.UpdatingSteamCmd, progress, ct)
                    .ConfigureAwait(false);
            }

            // A broken steam.dll cannot be fixed by re-running +quit. Do exactly one
            // clean reinstall (fresh files) to clear a half-applied/locked update.
            if (!File.Exists(SteamCmdExe) ||
                SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(bootstrap.Output))
            {
                _logger.LogWarning(
                    "SteamCMD self-update failed; performing one clean reinstall");
                progress.Report(new InstallProgress
                {
                    Phase = InstallPhase.UpdatingSteamCmd,
                    Status = "Repairing SteamCMD (clean reinstall)",
                });
                await ExtractFreshSteamCmdAsync(progress, ct).ConfigureAwait(false);
                bootstrap = await RunSteamCmdAsync(
                        ["+quit"], InstallPhase.UpdatingSteamCmd, progress, ct)
                    .ConfigureAwait(false);
            }

            if (!File.Exists(SteamCmdExe) ||
                SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(bootstrap.Output))
            {
                progress.Report(new InstallProgress
                {
                    Phase = InstallPhase.Failed,
                    Status = "SteamCMD setup failed",
                });
                var logPath = WriteTroubleshootingReport(
                    "SteamCMD setup", bootstrap.ExitCode, bootstrap.Output);
                return SteamCmdResult.Fail(
                    bootstrap.ExitCode,
                    bootstrap.Output,
                    SteamCmdDiagnostics.SelfUpdateHelp) with
                { LogPath = logPath };
            }

            progress.Report(new InstallProgress
            {
                Phase = InstallPhase.Completed,
                Status = "SteamCMD ready",
            });

            // Success: the binary exists and bootstrapped. The real validation is the
            // subsequent app_update step.
            return SteamCmdResult.Ok(0, bootstrap.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamCMD installation failed");
            progress.Report(new InstallProgress { Phase = InstallPhase.Failed, Status = ex.Message });
            return SteamCmdResult.Fail(-1, "", ex.Message);
        }
    }

    /// <summary>
    /// Deletes any existing SteamCMD directory and lays down a clean copy. A
    /// half-applied/locked self-update leaves a broken steam.dll that only a
    /// fresh extract can clear.
    /// </summary>
    private async Task ExtractFreshSteamCmdAsync(
        IProgress<InstallProgress> progress,
        CancellationToken ct)
    {
        if (Directory.Exists(_paths.SteamCmdDirectory))
        {
            try
            {
                Directory.Delete(_paths.SteamCmdDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex, "Could not fully clear the SteamCMD directory before re-extract");
            }
        }

        Directory.CreateDirectory(_paths.SteamCmdDirectory);

        progress.Report(new InstallProgress
        {
            Phase = InstallPhase.DownloadingSteamCmd,
            Status = "Downloading SteamCMD",
        });

        var zipPath = Path.Combine(_paths.SteamCmdDirectory, "steamcmd.zip");
        await DownloadFileAsync(SteamCmdZipUrl, zipPath, progress, ct).ConfigureAwait(false);

        progress.Report(new InstallProgress
        {
            Phase = InstallPhase.ExtractingSteamCmd,
            Status = "Extracting SteamCMD",
        });

        ZipFile.ExtractToDirectory(zipPath, _paths.SteamCmdDirectory, overwriteFiles: true);
        File.Delete(zipPath);
    }

    public Task<SteamCmdResult> InstallOrUpdateServerAsync(
        string installPath,
        IProgress<InstallProgress> progress,
        CancellationToken ct = default) =>
        RunAppUpdateAsync(installPath, validate: true, InstallPhase.InstallingServer, progress, ct);

    public Task<SteamCmdResult> ValidateServerAsync(
        string installPath,
        IProgress<InstallProgress> progress,
        CancellationToken ct = default) =>
        RunAppUpdateAsync(installPath, validate: true, InstallPhase.ValidatingServer, progress, ct);

    private async Task<SteamCmdResult> RunAppUpdateAsync(
        string installPath,
        bool validate,
        InstallPhase phase,
        IProgress<InstallProgress> progress,
        CancellationToken ct)
    {
        if (!File.Exists(SteamCmdExe))
        {
            return SteamCmdResult.Fail(-1, "", "SteamCMD is not installed yet.");
        }

        Directory.CreateDirectory(installPath);

        var args = new List<string>
        {
            "+force_install_dir",
            installPath,
            "+login",
            "anonymous",
            "+app_update",
            ISteamCmdService.AbioticFactorDedicatedAppId,
        };

        if (validate)
        {
            args.Add("validate");
        }

        args.Add("+quit");

        progress.Report(new InstallProgress
        {
            Phase = phase,
            Status = phase == InstallPhase.ValidatingServer
                ? "Validating dedicated server files"
                : "Installing / updating dedicated server",
        });

        var result = await RunSteamCmdAsync(args, phase, progress, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            // If SteamCMD itself is broken (could not load steam.dll), retrying the
            // content request is pointless - repair SteamCMD first. Otherwise this is
            // just the common transient post-self-update hiccup; a single retry clears it.
            if (SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(result.Output))
            {
                _logger.LogWarning(
                    "SteamCMD broken during app_update; running a clean repair before retry");
                await InstallSteamCmdAsync(progress, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation(
                    "SteamCMD app_update exited with code {Code}; retrying once",
                    result.ExitCode);
            }

            result = await RunSteamCmdAsync(args, phase, progress, ct).ConfigureAwait(false);
        }

        if (!result.Success)
        {
            var logPath = WriteTroubleshootingReport(
                phase == InstallPhase.ValidatingServer ? "Server validate" : "Server update",
                result.ExitCode,
                result.Output);
            var help = SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(result.Output)
                ? SteamCmdDiagnostics.SelfUpdateHelp
                : result.ErrorMessage;
            result = result with { ErrorMessage = help, LogPath = logPath };
        }

        progress.Report(new InstallProgress
        {
            Phase = result.Success ? InstallPhase.Completed : InstallPhase.Failed,
            Status = result.Success ? "Server files up to date" : "Server update failed",
        });

        return result;
    }

    /// <summary>
    /// Writes one self-contained, human-readable troubleshooting report (env,
    /// resolved paths, sync detection, the captured SteamCMD output and SteamCMD's
    /// own bootstrap log) and returns its path. Best-effort: never throws.
    /// </summary>
    private string? WriteTroubleshootingReport(string operation, int exitCode, string output)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            var path = Path.Combine(
                _paths.LogsDirectory,
                $"steamcmd-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            var dataSynced = AppPaths.IsSyncedLocation(_paths.DataRoot);
            var steamSynced = AppPaths.IsSyncedLocation(_paths.SteamCmdDirectory);

            var sb = new StringBuilder();
            sb.AppendLine("Facility Overseer - SteamCMD troubleshooting report");
            sb.AppendLine("====================================================");
            sb.AppendLine($"When            : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"Operation       : {operation}");
            sb.AppendLine($"SteamCMD exit   : {exitCode}");
            sb.AppendLine($"Likely cause    : {SteamCmdDiagnostics.Summarize(output)}");
            sb.AppendLine();
            sb.AppendLine("Environment");
            sb.AppendLine("-----------");
            sb.AppendLine($"OS              : {Environment.OSVersion}");
            sb.AppendLine($"64-bit OS/proc  : {Environment.Is64BitOperatingSystem}/{Environment.Is64BitProcess}");
            sb.AppendLine($"Machine/User    : {Environment.MachineName}/{Environment.UserName}");
            sb.AppendLine();
            sb.AppendLine("Resolved paths");
            sb.AppendLine("--------------");
            sb.AppendLine($"DataRoot        : {_paths.DataRoot}  (synced: {dataSynced})");
            sb.AppendLine($"SteamCmd dir    : {_paths.SteamCmdDirectory}  (synced: {steamSynced})");
            sb.AppendLine($"Servers dir     : {_paths.ServersDirectory}");
            sb.AppendLine($"Logs dir        : {_paths.LogsDirectory}");
            sb.AppendLine();
            sb.AppendLine("Remediation");
            sb.AppendLine("-----------");
            sb.AppendLine(SteamCmdDiagnostics.SelfUpdateHelp);
            sb.AppendLine();
            sb.AppendLine("SteamCMD own bootstrap log (tail)");
            sb.AppendLine("---------------------------------");
            sb.AppendLine(ReadLogTail(
                Path.Combine(_paths.SteamCmdDirectory, "logs", "bootstrap_log.txt"), 8000));
            sb.AppendLine();
            sb.AppendLine("SteamCMD own stderr (tail)");
            sb.AppendLine("--------------------------");
            sb.AppendLine(ReadLogTail(
                Path.Combine(_paths.SteamCmdDirectory, "logs", "stderr.txt"), 4000));
            sb.AppendLine();
            sb.AppendLine("Captured SteamCMD console output");
            sb.AppendLine("--------------------------------");
            sb.AppendLine(string.IsNullOrWhiteSpace(output) ? "(none)" : output);

            File.WriteAllText(path, sb.ToString());
            _logger.LogError(
                "SteamCMD {Operation} failed (exit {Code}); troubleshooting report: {Path}",
                operation, exitCode, path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the SteamCMD troubleshooting report");
            return null;
        }
    }

    private static string ReadLogTail(string path, int maxChars)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "(not found: " + path + ")";
            }

            var text = File.ReadAllText(path);
            return text.Length > maxChars ? "...\n" + text[^maxChars..] : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(could not read: " + ex.Message + ")";
        }
    }

    private async Task<SteamCmdResult> RunSteamCmdAsync(
        IReadOnlyList<string> args,
        InstallPhase phase,
        IProgress<InstallProgress> progress,
        CancellationToken ct)
    {
        var output = new StringBuilder();

        void OnLine(string line)
        {
            output.AppendLine(line);
            progress.Report(new InstallProgress
            {
                Phase = phase,
                Status = phase.ToString(),
                OutputLine = line,
                PercentComplete = TryParseProgress(line),
            });
        }

        try
        {
            var result = await Cli.Wrap(SteamCmdExe)
                .WithArguments(args)
                .WithWorkingDirectory(_paths.SteamCmdDirectory)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(OnLine))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(OnLine))
                .ExecuteBufferedAsync(ct)
                .ConfigureAwait(false);

            return result.ExitCode == 0
                ? SteamCmdResult.Ok(result.ExitCode, output.ToString())
                : SteamCmdResult.Fail(
                    result.ExitCode,
                    output.ToString(),
                    $"SteamCMD exited with code {result.ExitCode}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamCMD execution failed");
            return SteamCmdResult.Fail(-1, output.ToString(), ex.Message);
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<InstallProgress> progress,
        CancellationToken ct)
    {
        using var response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            progress.Report(new InstallProgress
            {
                Phase = InstallPhase.DownloadingSteamCmd,
                Status = "Downloading SteamCMD",
                PercentComplete = total is > 0 ? received * 100.0 / total.Value : null,
            });
        }
    }

    private static double? TryParseProgress(string line)
    {
        // SteamCMD prints lines like: " Update state (0x61) downloading, progress: 42.13 (..)"
        var marker = line.IndexOf("progress:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var rest = line[(marker + "progress:".Length)..].TrimStart();
        var end = 0;
        while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '.'))
        {
            end++;
        }

        return double.TryParse(rest[..end], out var pct) ? pct : null;
    }
}
