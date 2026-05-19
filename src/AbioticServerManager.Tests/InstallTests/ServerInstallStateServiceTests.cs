using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Infrastructure.FileSystem;
using AbioticServerManager.Infrastructure.Install;

namespace AbioticServerManager.Tests.InstallTests;

public sealed class ServerInstallStateServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fo-install-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly ServerInstallStateService _service;

    public ServerInstallStateServiceTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _service = new ServerInstallStateService(_paths, new ServerExecutableLocator());
    }

    [Fact]
    public void Missing_managed_server_folder_is_not_launchable()
    {
        var state = _service.Evaluate(_paths.ManagedServerDirectory);

        Assert.Equal(ServerInstallKind.Missing, state.Kind);
        Assert.False(state.IsLaunchable);
        Assert.Equal(_paths.ManagedServerDirectory, state.ServerInstallPath);
    }

    [Fact]
    public void Empty_server_folder_is_not_launchable()
    {
        Directory.CreateDirectory(_paths.ManagedServerDirectory);

        var state = _service.Evaluate(_paths.ManagedServerDirectory);

        Assert.Equal(ServerInstallKind.EmptyFolder, state.Kind);
        Assert.False(state.IsLaunchable);
    }

    [Fact]
    public void Folder_without_server_executable_is_invalid()
    {
        Directory.CreateDirectory(_paths.ManagedServerDirectory);
        File.WriteAllText(Path.Combine(_paths.ManagedServerDirectory, "readme.txt"), "not a server");

        var state = _service.Evaluate(_paths.ManagedServerDirectory);

        Assert.Equal(ServerInstallKind.InvalidFolder, state.Kind);
        Assert.False(state.IsLaunchable);
    }

    [Fact]
    public void Server_executable_without_manifest_is_detected_unmanaged_and_launchable()
    {
        var exe = CreateServerExecutable(_paths.ManagedServerDirectory);

        var state = _service.Evaluate(_paths.ManagedServerDirectory);

        Assert.Equal(ServerInstallKind.DetectedUnmanaged, state.Kind);
        Assert.True(state.IsLaunchable);
        Assert.Equal(exe, state.ExecutablePath);
        Assert.Null(state.ManifestPath);
    }

    [Fact]
    public void Server_executable_with_manifest_is_steamcmd_managed_and_launchable()
    {
        var exe = CreateServerExecutable(_paths.ManagedServerDirectory);
        var manifest = CreateManifest(_paths.ManagedServerDirectory, "23174893");

        var state = _service.Evaluate(_paths.ManagedServerDirectory);

        Assert.Equal(ServerInstallKind.SteamCmdManaged, state.Kind);
        Assert.True(state.IsLaunchable);
        Assert.Equal(exe, state.ExecutablePath);
        Assert.Equal(manifest, state.ManifestPath);
        Assert.Equal("23174893", state.BuildId);
    }

    [Fact]
    public void Start_gate_is_closed_when_install_is_missing_and_open_when_ready()
    {
        var missing = _service.Evaluate(_paths.ManagedServerDirectory);
        Assert.False(missing.IsLaunchable);

        CreateServerExecutable(_paths.ManagedServerDirectory);
        CreateManifest(_paths.ManagedServerDirectory, "23174893");

        var ready = _service.Evaluate(_paths.ManagedServerDirectory);
        Assert.True(ready.IsLaunchable);
    }

    private static string CreateServerExecutable(string installRoot)
    {
        var dir = Path.Combine(installRoot, "AbioticFactor", "Binaries", "Win64");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "AbioticFactorServer-Win64-Shipping.exe");
        File.WriteAllText(exe, "");
        return exe;
    }

    private static string CreateManifest(string installRoot, string buildId)
    {
        var dir = Path.Combine(installRoot, "steamapps");
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(dir, $"appmanifest_{ISteamCmdService.AbioticFactorDedicatedAppId}.acf");
        File.WriteAllText(
            manifest,
            $$"""
            "AppState"
            {
                "appid" "{{ISteamCmdService.AbioticFactorDedicatedAppId}}"
                "buildid" "{{buildId}}"
            }
            """);
        return manifest;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
