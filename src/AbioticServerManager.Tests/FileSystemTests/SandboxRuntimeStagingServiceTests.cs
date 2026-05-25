using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.FileSystemTests;

/// <summary>
/// IO-touching tests for <see cref="SandboxRuntimeStagingService"/>. Uses a
/// scratch temp directory rather than mocked filesystem so the SHA-256
/// hashing + change detection is exercised against real bytes.
/// </summary>
public class SandboxRuntimeStagingServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _installRoot;
    private readonly string _durableRoot;
    private readonly SandboxRuntimeStagingService _svc = new(NullLogger<SandboxRuntimeStagingService>.Instance);

    public SandboxRuntimeStagingServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FacilityOverseer.Tests", Guid.NewGuid().ToString("N"));
        _installRoot = Path.Combine(_root, "install");
        _durableRoot = Path.Combine(_root, "data", "worlds", "abc123", "config");
        Directory.CreateDirectory(_installRoot);
        Directory.CreateDirectory(_durableRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SandboxLaunchPaths Paths() => SandboxLaunchPaths.For(new ServerInstance
    {
        Id = "abc123",
        DisplayName = "Test",
        WorldSaveName = "Test",
        InstallPath = _installRoot,
        SandboxIniPath = Path.Combine(_durableRoot, "SandboxSettings.ini"),
        AdminIniPath = Path.Combine(_durableRoot, "Admin.ini"),
    });

    [Fact]
    public async Task Stage_copies_existing_durable_files_into_the_install_saved_tree()
    {
        var paths = Paths();
        await File.WriteAllTextAsync(paths.DurableSandboxPath, "ZombieDamage=2");
        await File.WriteAllTextAsync(paths.DurableAdminPath, "[Moderators]\n");

        var result = await _svc.StageAsync(paths);

        Assert.True(result.Success);
        Assert.True(File.Exists(paths.StagedSandboxPath));
        Assert.True(File.Exists(paths.StagedAdminPath));
        Assert.Equal("ZombieDamage=2", await File.ReadAllTextAsync(paths.StagedSandboxPath));
        Assert.NotNull(result.SandboxHashBeforeRun);
        Assert.NotNull(result.AdminHashBeforeRun);
    }

    [Fact]
    public async Task Stage_with_no_durable_files_is_a_clean_no_op()
    {
        // Fresh world: nothing to copy. AF will write its own defaults.
        var paths = Paths();

        var result = await _svc.StageAsync(paths);

        Assert.True(result.Success);
        Assert.False(File.Exists(paths.StagedSandboxPath));
        Assert.False(File.Exists(paths.StagedAdminPath));
        Assert.Null(result.SandboxHashBeforeRun);
        Assert.Null(result.AdminHashBeforeRun);
    }

    [Fact]
    public async Task Sync_back_copies_staged_to_durable_when_changed_during_run()
    {
        // Stage, then simulate AF modifying the runtime copy in-game.
        var paths = Paths();
        await File.WriteAllTextAsync(paths.DurableSandboxPath, "ZombieDamage=1");
        var stage = await _svc.StageAsync(paths);
        await File.WriteAllTextAsync(paths.StagedSandboxPath, "ZombieDamage=3");

        await _svc.SyncBackAsync(paths, stage);

        Assert.Equal("ZombieDamage=3", await File.ReadAllTextAsync(paths.DurableSandboxPath));
    }

    [Fact]
    public async Task Sync_back_leaves_durable_alone_when_nothing_changed_during_run()
    {
        var paths = Paths();
        await File.WriteAllTextAsync(paths.DurableSandboxPath, "ZombieDamage=1");
        var stage = await _svc.StageAsync(paths);
        var beforeWrite = File.GetLastWriteTimeUtc(paths.DurableSandboxPath);
        await Task.Delay(50); // ensure any inadvertent write would be detectable by mtime

        await _svc.SyncBackAsync(paths, stage);

        var afterWrite = File.GetLastWriteTimeUtc(paths.DurableSandboxPath);
        Assert.Equal(beforeWrite, afterWrite);
    }

    [Fact]
    public async Task Push_durable_to_staged_overwrites_the_runtime_copy()
    {
        // User saves a new value WHILE the server is running. The new value
        // must land in the staged file so the next world restart sees it.
        var paths = Paths();
        await File.WriteAllTextAsync(paths.DurableSandboxPath, "ZombieDamage=1");
        await _svc.StageAsync(paths);

        // Simulate the user saving a new value via the Sandbox tab.
        await File.WriteAllTextAsync(paths.DurableSandboxPath, "ZombieDamage=5");
        await _svc.PushDurableToStagedAsync(paths);

        Assert.Equal("ZombieDamage=5", await File.ReadAllTextAsync(paths.StagedSandboxPath));
    }
}
