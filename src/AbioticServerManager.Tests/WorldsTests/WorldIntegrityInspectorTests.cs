using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.FileSystem;

namespace AbioticServerManager.Tests.WorldsTests;

/// <summary>
/// §4.5: verifies the inspector maps a world's on-disk state into the inputs
/// the pure <see cref="WorldIntegrityValidator"/> expects. The verdict logic
/// itself is covered by <see cref="WorldIntegrityValidatorTests"/>.
/// </summary>
public sealed class WorldIntegrityInspectorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fo-integrity-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public WorldIntegrityInspectorTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Stubs install-state so a test controls only whether the exe is found.</summary>
    private sealed class FakeInstallState(string? executablePath) : IServerInstallStateService
    {
        public ServerInstallState Evaluate(ServerInstance instance) => Build();

        public ServerInstallState Evaluate(string? installPath) => Build();

        private ServerInstallState Build() => new()
        {
            Kind = executablePath is { Length: > 0 }
                ? ServerInstallKind.SteamCmdManaged
                : ServerInstallKind.Missing,
            DataRoot = "",
            ServerInstallPath = "",
            ExecutablePath = executablePath,
            InstallSource = "test",
            ValidationMessage = "",
        };
    }

    private WorldIntegrityInspector Inspector(bool executableFound) =>
        new(_paths, new FakeInstallState(executableFound ? @"C:\fake\server.exe" : null));

    [Fact]
    public void Healthy_world_under_data_root_is_launchable_with_no_findings()
    {
        const string id = "world-healthy";
        _paths.EnsureWorldCreated(id);
        File.WriteAllText(_paths.WorldSandboxIniPath(id), "[/Script/AbioticFactor]\nGameDifficulty=1");
        File.WriteAllText(_paths.WorldAdminIniPath(id), "[Moderators]\n");

        var instance = new ServerInstance
        {
            Id = id,
            InstallPath = _paths.ManagedServerDirectory,
            SandboxIniPath = _paths.WorldSandboxIniPath(id),
            AdminIniPath = _paths.WorldAdminIniPath(id),
        };

        var report = Inspector(executableFound: true).Inspect(instance);

        Assert.True(report.IsLaunchable);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Missing_executable_is_a_blocker()
    {
        const string id = "world-no-exe";
        _paths.EnsureWorldCreated(id);
        File.WriteAllText(_paths.WorldSandboxIniPath(id), "GameDifficulty=1");
        File.WriteAllText(_paths.WorldAdminIniPath(id), "[Moderators]\n");

        var instance = new ServerInstance
        {
            Id = id,
            InstallPath = _paths.ManagedServerDirectory,
            SandboxIniPath = _paths.WorldSandboxIniPath(id),
            AdminIniPath = _paths.WorldAdminIniPath(id),
        };

        var report = Inspector(executableFound: false).Inspect(instance);

        Assert.False(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "EXE_MISSING" && f.Severity == WorldIntegritySeverity.Blocker);
    }

    [Fact]
    public void Sandbox_inside_server_install_is_warned_as_not_under_data_root()
    {
        const string id = "world-legacy";
        var installDir = Path.Combine(_root, "servers", "abiotic-factor-dedicated");
        var legacySandbox = Path.Combine(
            installDir, "AbioticFactor", "Saved", "Config", "FacilityOverseer", id,
            "SandboxSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySandbox)!);
        File.WriteAllText(legacySandbox, "GameDifficulty=2");

        var instance = new ServerInstance
        {
            Id = id,
            InstallPath = installDir,
            SandboxIniPath = legacySandbox,
        };

        var report = Inspector(executableFound: true).Inspect(instance);

        Assert.Contains(report.Findings, f =>
            f.Id == "SANDBOX_NOT_UNDER_DATAROOT" && f.Severity == WorldIntegritySeverity.Warning);
        // The legacy file still exists, so it must not also be reported missing.
        Assert.DoesNotContain(report.Findings, f => f.Id == "SANDBOX_INI_MISSING");
    }

    [Fact]
    public void Unset_config_paths_fall_back_to_the_canonical_data_root_location()
    {
        const string id = "world-fresh";
        var instance = new ServerInstance
        {
            Id = id,
            InstallPath = _paths.ManagedServerDirectory,
            SandboxIniPath = "",
            AdminIniPath = "",
        };

        var report = Inspector(executableFound: true).Inspect(instance);

        // Empty paths must NOT trip the "path unset" blocker — IAppPaths always
        // resolves a canonical path; the file simply does not exist yet.
        Assert.DoesNotContain(report.Findings, f => f.Id == "SANDBOX_PATH_UNSET");
        Assert.Contains(report.Findings, f =>
            f.Id == "SANDBOX_INI_MISSING" && f.Severity == WorldIntegritySeverity.Warning);
        Assert.True(report.IsLaunchable);
    }
}
