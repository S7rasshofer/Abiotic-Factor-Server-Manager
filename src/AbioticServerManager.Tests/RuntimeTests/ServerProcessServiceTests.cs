using System.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.RuntimeTests;

public class ServerProcessServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fo-process-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stop_sends_exit_before_kill()
    {
        Directory.CreateDirectory(_root);
        var marker = Path.Combine(_root, "exit.txt");
        var script = WriteScript(
            "exit-listener.ps1",
            $$"""
            $marker = '{{Ps(marker)}}'
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ($line -eq 'exit') {
                    Set-Content -LiteralPath $marker -Value 'exit'
                    exit 0
                }
            }
            """);

        var service = CreateService(script, TimeSpan.FromSeconds(2));
        var instance = Instance();

        var start = await service.StartAsync(instance);
        Assert.True(start.Started, start.ErrorMessage);

        await service.StopAsync(instance);

        Assert.False(service.IsRunning(instance.Id));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task Stop_kills_process_when_exit_is_ignored()
    {
        Directory.CreateDirectory(_root);
        var script = WriteScript(
            "ignore-exit.ps1",
            """
            Start-Sleep -Seconds 30
            """);

        var service = CreateService(script, TimeSpan.FromMilliseconds(200));
        var instance = Instance();

        var start = await service.StartAsync(instance);
        Assert.True(start.Started, start.ErrorMessage);

        var elapsed = Stopwatch.StartNew();
        await service.StopAsync(instance);
        elapsed.Stop();

        Assert.False(service.IsRunning(instance.Id));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Stop_kills_detached_child_after_launcher_exits()
    {
        Directory.CreateDirectory(_root);
        var pidFile = Path.Combine(_root, "child.pid");
        var ps = PowerShellExe();

        // Child: record its PID, then live for a long time.
        var childScript = WriteScript(
            "child.ps1",
            $$"""
            Set-Content -LiteralPath '{{Ps(pidFile)}}' -Value $PID
            Start-Sleep -Seconds 120
            """);

        // Launcher: spawn the child detached, then exit immediately. This is the
        // real-world pattern where Stop used to be a silent no-op.
        var launcher = WriteScript(
            "launcher.ps1",
            $$"""
            Start-Process -FilePath '{{Ps(ps)}}' `
                -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','{{Ps(childScript)}}'
            exit 0
            """);

        var service = CreateService(launcher, TimeSpan.FromMilliseconds(200));
        var instance = Instance();

        var start = await service.StartAsync(instance);
        Assert.True(start.Started, start.ErrorMessage);

        // Wait for the child to come up and the launcher to exit.
        var childPid = await WaitForPidAsync(pidFile);
        Assert.True(IsProcessAlive(childPid), "child should be running");

        // The launcher has exited but the job keeps the world "running".
        Assert.True(service.IsRunning(instance.Id));

        await service.StopAsync(instance);

        Assert.False(service.IsRunning(instance.Id));
        Assert.False(IsProcessAlive(childPid), "detached child must be killed by Stop");
    }

    private static async Task<int> WaitForPidAsync(string pidFile)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) &&
                int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out var pid))
            {
                return pid;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Child process never reported its PID.");
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            return !System.Diagnostics.Process.GetProcessById(pid).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServerProcessService CreateService(string script, TimeSpan gracefulTimeout) =>
        new(
            new FixedArgumentBuilder([
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Quote(script),
            ]),
            new FixedLocator(PowerShellExe()),
            NullLogger<ServerProcessService>.Instance,
            gracefulTimeout);

    private string WriteScript(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ServerInstance Instance() => new()
    {
        DisplayName = "Process Test",
        InstallPath = Environment.CurrentDirectory,
    };

    private static string PowerShellExe() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string Ps(string value) => value.Replace("'", "''");

    private sealed class FixedLocator(string exe) : IServerExecutableLocator
    {
        public string? Locate(string installPath) => exe;
    }

    private sealed class FixedArgumentBuilder(IReadOnlyList<string> args) : ILaunchArgumentBuilder
    {
        public IReadOnlyList<string> BuildArguments(ServerInstance instance) => args;

        public string BuildMaskedCommandLine(ServerInstance instance) => string.Join(' ', args);
    }
}
