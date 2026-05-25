using System.Collections.Concurrent;
using System.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Worlds;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Process;

using SysProcess = System.Diagnostics.Process;

public sealed class ServerProcessService : IServerProcessService, IDisposable
{
    private sealed class RunningServer
    {
        public required SysProcess Process { get; init; }
        public Win32JobObject? Job { get; init; }
        public bool StopRequested { get; set; }
        public bool Finalized { get; set; }

        /// <summary>
        /// Sandbox staging context captured at launch. Used to sync any
        /// AF-modified runtime INIs back to the durable copy after the
        /// process tree fully exits.
        /// </summary>
        public SandboxLaunchPaths? LaunchPaths { get; init; }
        public SandboxStageResult? StageResult { get; init; }
        public ServerInstance? StoppedInstance { get; init; }

        /// <summary>
        /// True while any process in the tree is alive. Uses the job (so a detached
        /// child the launcher spawned still counts) and falls back to the launcher
        /// process handle if the job query is unavailable.
        /// </summary>
        public bool IsAlive()
        {
            if (Job is not null)
            {
                var active = Job.ActiveProcessCount();
                if (active >= 0)
                {
                    return active > 0;
                }
            }

            try
            {
                return !Process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    private readonly ConcurrentDictionary<string, RunningServer> _running = new();
    private readonly ILaunchArgumentBuilder _argumentBuilder;
    private readonly IServerExecutableLocator _locator;
    private readonly ISandboxRuntimeStagingService? _staging;
    private readonly ILogger<ServerProcessService> _logger;
    private readonly TimeSpan _gracefulStopTimeout;

    public ServerProcessService(
        ILaunchArgumentBuilder argumentBuilder,
        IServerExecutableLocator locator,
        ILogger<ServerProcessService> logger,
        TimeSpan? gracefulStopTimeout = null,
        ISandboxRuntimeStagingService? staging = null)
    {
        _argumentBuilder = argumentBuilder;
        _locator = locator;
        _staging = staging;
        _logger = logger;
        _gracefulStopTimeout = gracefulStopTimeout ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler<ServerLogLine>? LogReceived;

    public event EventHandler<string>? RuntimeChanged;

    public bool IsRunning(string instanceId) =>
        _running.TryGetValue(instanceId, out var s) && s.IsAlive();

    public async Task<ServerStartResult> StartAsync(ServerInstance instance, CancellationToken ct = default)
    {
        if (IsRunning(instance.Id))
        {
            return ServerStartResult.Fail("This world is already running.");
        }

        var exe = _locator.Locate(instance.InstallPath);
        if (exe is null)
        {
            return ServerStartResult.Fail(
                "Could not find the dedicated server executable. Install or update the server first.");
        }

        // Stage durable Sandbox/Admin INIs into the install's Saved tree so AF
        // can find them via the relative path it actually accepts. Without
        // this, AF would silently fall back to default settings - the user's
        // saved tuning would never reach the game.
        SandboxLaunchPaths? launchPaths = null;
        SandboxStageResult? stageResult = null;
        ServerInstance launchInstance = instance;
        if (_staging is not null && !string.IsNullOrWhiteSpace(instance.InstallPath))
        {
            try
            {
                launchPaths = SandboxLaunchPaths.For(instance);
                stageResult = await _staging.StageAsync(launchPaths, ct).ConfigureAwait(false);
                if (!stageResult.Success)
                {
                    return ServerStartResult.Fail(
                        stageResult.FailureReason
                        ?? "Could not stage world settings into the server install.");
                }

                // Hand the arg builder a clone that points at the staged copies
                // so ResolveIniArg can express them as relative-to-Saved.
                launchInstance = instance.Clone();
                launchInstance.SandboxIniPath = launchPaths.StagedSandboxPath;
                launchInstance.AdminIniPath = launchPaths.StagedAdminPath;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Sandbox staging threw for world {World}", instance.DisplayName);
                return ServerStartResult.Fail(
                    "Could not stage world settings into the server install: " + ex.Message);
            }
        }

        string arguments;
        string maskedCommandLine;
        try
        {
            arguments = string.Join(' ', _argumentBuilder.BuildArguments(launchInstance));
            maskedCommandLine = _argumentBuilder.BuildMaskedCommandLine(launchInstance);
        }
        catch (InvalidOperationException ex)
        {
            // ResolveIniArg refused to emit an unsafe absolute path; this can
            // only happen when staging is disabled or InstallPath is missing.
            _logger.LogError(ex, "Could not build launch arguments for {World}", instance.DisplayName);
            return ServerStartResult.Fail(ex.Message);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? instance.InstallPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var process = new SysProcess { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                return ServerStartResult.Fail("The server process failed to start.");
            }

            // Assign to the job immediately so children spawned by the launcher
            // (the real dedicated server) are captured too.
            var job = Win32JobObject.TryCreate();
            if (job is not null && !job.Assign(process))
            {
                _logger.LogWarning(
                    "Could not place world {World} in a job object; only the launcher " +
                    "process will be tracked.",
                    instance.DisplayName);
                job.Dispose();
                job = null;
            }

            var state = new RunningServer
            {
                Process = process,
                Job = job,
                LaunchPaths = launchPaths,
                StageResult = stageResult,
                StoppedInstance = instance,
            };
            process.OutputDataReceived += (_, e) => Emit(instance.Id, e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => Emit(instance.Id, e.Data, isError: true);
            process.Exited += (_, _) => OnLauncherExited(instance.Id, state);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _running[instance.Id] = state;

            _logger.LogInformation(
                "Started world {World} (pid {Pid}) with args: {Args}",
                instance.DisplayName,
                process.Id,
                maskedCommandLine);

            RuntimeChanged?.Invoke(this, instance.Id);
            return ServerStartResult.Ok(process.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start world {World}", instance.DisplayName);
            return ServerStartResult.Fail(ex.Message);
        }
    }

    public async Task StopAsync(ServerInstance instance, CancellationToken ct = default)
    {
        if (!_running.TryGetValue(instance.Id, out var state))
        {
            return;
        }

        state.StopRequested = true;
        try
        {
            // Best-effort graceful shutdown: ask the server to exit, then wait for
            // the whole tree (launcher + real server) to drain.
            await TryGracefulStopAsync(state, instance, ct).ConfigureAwait(false);

            if (state.IsAlive())
            {
                _logger.LogWarning(
                    "World {World} did not stop gracefully; terminating the process tree",
                    instance.DisplayName);

                if (state.Job is not null && state.Job.Terminate())
                {
                    await WaitWhileAliveAsync(state, TimeSpan.FromSeconds(15), ct)
                        .ConfigureAwait(false);
                }

                if (state.IsAlive() && !state.Process.HasExited)
                {
                    // No job (or job terminate failed): fall back to killing the
                    // launcher's process tree directly.
                    state.Process.Kill(entireProcessTree: true);
                    await state.Process
                        .WaitForExitAsync(ct)
                        .WaitAsync(TimeSpan.FromSeconds(15), ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping world {World}", instance.DisplayName);
        }
        finally
        {
            Finalize(instance.Id, state);
        }
    }

    public async Task<ServerStartResult> RestartAsync(ServerInstance instance, CancellationToken ct = default)
    {
        await StopAsync(instance, ct).ConfigureAwait(false);
        return await StartAsync(instance, ct).ConfigureAwait(false);
    }

    private async Task TryGracefulStopAsync(
        RunningServer state,
        ServerInstance instance,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _gracefulStopTimeout;

        try
        {
            if (!state.Process.HasExited &&
                state.Process.StartInfo.RedirectStandardInput)
            {
                _logger.LogInformation("Sending exit command to world {World}", instance.DisplayName);

                // Time-box the write itself so a server that never reads stdin
                // cannot make Stop hang forever.
                using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                writeCts.CancelAfter(TimeSpan.FromSeconds(2));
                await state.Process.StandardInput
                    .WriteLineAsync("exit".AsMemory(), writeCts.Token)
                    .ConfigureAwait(false);
                await state.Process.StandardInput.FlushAsync(writeCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException)
        {
            _logger.LogWarning("Could not send exit command to world {World}", instance.DisplayName);
        }

        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await WaitWhileAliveAsync(state, remaining, ct).ConfigureAwait(false);
        }
    }

    private static async Task WaitWhileAliveAsync(
        RunningServer state,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!state.IsAlive())
            {
                return;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }
    }

    private void Emit(string instanceId, string? line, bool isError)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        LogReceived?.Invoke(this, ServerLogLine.FromProcessOutput(
            instanceId,
            DateTimeOffset.Now,
            line,
            isError));
    }

    /// <summary>
    /// The launcher process exited. If the job still has live processes the real
    /// server detached and is still running, so keep tracking until it drains;
    /// otherwise finalize (clean stop or crash).
    /// </summary>
    private void OnLauncherExited(string instanceId, RunningServer state)
    {
        if (state.StopRequested)
        {
            return; // StopAsync owns finalization.
        }

        if (state.Job is not null && state.Job.ActiveProcessCount() > 0)
        {
            _ = MonitorDetachedServerAsync(instanceId, state);
            return;
        }

        var exitCode = SafeExitCode(state.Process);
        if (exitCode != 0)
        {
            _logger.LogWarning(
                "World {Instance} exited unexpectedly with code {Code} (possible crash)",
                instanceId,
                exitCode);
            Emit(instanceId, $"[server exited unexpectedly with code {exitCode}]", isError: true);
        }

        Finalize(instanceId, state);
    }

    private async Task MonitorDetachedServerAsync(string instanceId, RunningServer state)
    {
        try
        {
            while (!state.StopRequested && state.IsAlive())
            {
                await Task.Delay(1500).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort; fall through to finalize.
        }

        if (!state.StopRequested)
        {
            Emit(instanceId, "[server stopped]", isError: false);
            Finalize(instanceId, state);
        }
    }

    private void Finalize(string instanceId, RunningServer state)
    {
        lock (state)
        {
            if (state.Finalized)
            {
                return;
            }

            state.Finalized = true;
        }

        _running.TryRemove(instanceId, out _);
        state.Job?.Dispose();

        // Sync any AF-modified runtime INIs back to the durable copy. Fire
        // and forget - this should not block the UI thread that raised the
        // RuntimeChanged event, and any failure here is logged inside the
        // staging service rather than surfaced as an error.
        if (_staging is not null && state.LaunchPaths is not null && state.StageResult is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _staging
                        .SyncBackAsync(state.LaunchPaths, state.StageResult)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Sandbox sync-back failed for {InstanceId}", instanceId);
                }
            });
        }

        RuntimeChanged?.Invoke(this, instanceId);
    }

    private static int SafeExitCode(SysProcess process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    public void Dispose()
    {
        foreach (var state in _running.Values)
        {
            try
            {
                state.Job?.Terminate();
                if (!state.Process.HasExited)
                {
                    state.Process.Kill(entireProcessTree: true);
                }

                state.Process.Dispose();
                state.Job?.Dispose();
            }
            catch
            {
                // Best effort on shutdown.
            }
        }

        _running.Clear();
    }
}
