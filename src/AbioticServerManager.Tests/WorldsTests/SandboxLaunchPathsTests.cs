using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.WorldsTests;

/// <summary>
/// Pure tests for <see cref="SandboxLaunchPaths"/>: the path math that drives
/// the stage-then-relative launch-arg pipeline. The companion IO is tested
/// separately in SandboxRuntimeStagingServiceTests.
/// </summary>
public class SandboxLaunchPathsTests
{
    private const string Install = @"C:\Servers\Abiotic";

    private static ServerInstance Sample(string id = "abc123") => new()
    {
        Id = id,
        DisplayName = "Cascade",
        WorldSaveName = "Cascade",
        InstallPath = Install,
        SandboxIniPath = @"C:\Data\worlds\abc123\config\SandboxSettings.ini",
        AdminIniPath = @"C:\Data\worlds\abc123\config\Admin.ini",
    };

    [Fact]
    public void Staged_paths_live_under_install_saved_config_facility_overseer()
    {
        var paths = SandboxLaunchPaths.For(Sample());

        Assert.Equal(
            Path.Combine(Install, "AbioticFactor", "Saved", "Config", "FacilityOverseer", "abc123", "SandboxSettings.ini"),
            paths.StagedSandboxPath);
        Assert.Equal(
            Path.Combine(Install, "AbioticFactor", "Saved", "Config", "FacilityOverseer", "abc123", "Admin.ini"),
            paths.StagedAdminPath);
        Assert.Equal(
            Path.Combine(Install, "AbioticFactor", "Saved", "Config", "FacilityOverseer", "abc123"),
            paths.StagedFolder);
    }

    [Fact]
    public void Relative_args_are_forward_slashed_and_relative_to_saved()
    {
        // AF prefixes the value with ../../../AbioticFactor/Saved/ so it has
        // to be exactly the path AF will resolve to the staged file.
        var paths = SandboxLaunchPaths.For(Sample());

        Assert.Equal("Config/FacilityOverseer/abc123/SandboxSettings.ini", paths.RelativeSandboxArg);
        Assert.Equal("Config/FacilityOverseer/abc123/Admin.ini", paths.RelativeAdminArg);
    }

    [Fact]
    public void Durable_paths_pass_through_unchanged()
    {
        var paths = SandboxLaunchPaths.For(Sample());

        Assert.Equal(@"C:\Data\worlds\abc123\config\SandboxSettings.ini", paths.DurableSandboxPath);
        Assert.Equal(@"C:\Data\worlds\abc123\config\Admin.ini", paths.DurableAdminPath);
    }

    [Fact]
    public void Throws_when_install_path_is_missing()
    {
        var instance = Sample();
        instance.InstallPath = "";

        Assert.Throws<InvalidOperationException>(() => SandboxLaunchPaths.For(instance));
    }
}
