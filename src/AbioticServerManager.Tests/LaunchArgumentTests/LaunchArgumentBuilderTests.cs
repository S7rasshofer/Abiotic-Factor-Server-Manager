using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.LaunchArgumentTests;

public class LaunchArgumentBuilderTests
{
    private static readonly LaunchArgumentBuilder Builder = new();

    private static ServerInstance Sample() => new()
    {
        SteamServerName = "My Server",
        WorldSaveName = "Cascade",
        MaxPlayers = 6,
        GamePort = 7777,
        QueryPort = 27015,
    };

    [Fact]
    public void Builds_required_arguments()
    {
        var args = Builder.BuildArguments(Sample());

        Assert.Contains("-SteamServerName=\"My Server\"", args);
        Assert.Contains("-WorldSaveName=Cascade", args);
        Assert.Contains("-MaxServerPlayers=6", args);
        Assert.Contains("-PORT=7777", args);
        Assert.Contains("-QUERYPORT=27015", args);
    }

    [Fact]
    public void Omits_empty_optional_arguments()
    {
        var args = Builder.BuildArguments(Sample());

        Assert.DoesNotContain(args, a => a.StartsWith("-ServerPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("-AdminPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("-MultiHome", StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_flags_only_when_true()
    {
        var instance = Sample();
        instance.LanOnly = true;
        instance.UseLocalIps = true;

        var args = Builder.BuildArguments(instance);

        Assert.Contains("-LANOnly", args);
        Assert.Contains("-UseLocalIPs", args);
        Assert.DoesNotContain("-PlatformLimited", args);
    }

    [Theory]
    [InlineData(PlatformAccessMode.All, null)]
    [InlineData(PlatformAccessMode.PcOnly, "-PlatformLimited=PC")]
    [InlineData(PlatformAccessMode.PlaystationOnly, "-PlatformLimited=Playstation")]
    [InlineData(PlatformAccessMode.XboxOnly, "-PlatformLimited=Xbox")]
    public void Emits_platform_access_filter_when_restricted(
        PlatformAccessMode mode,
        string? expected)
    {
        var instance = Sample();
        instance.PlatformAccessMode = mode;

        var args = Builder.BuildArguments(instance);

        if (expected is null)
        {
            Assert.DoesNotContain(args, a => a.StartsWith("-PlatformLimited", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(expected, args);
        }
    }

    [Fact]
    public void Masks_passwords_in_diagnostic_output()
    {
        var instance = Sample();
        instance.ServerPassword = "hunter2";
        instance.AdminPassword = "rootpw";

        var masked = Builder.BuildMaskedCommandLine(instance);

        Assert.DoesNotContain("hunter2", masked);
        Assert.DoesNotContain("rootpw", masked);
        Assert.Contains("********", masked);
    }

    [Fact]
    public void Passwords_present_in_real_arguments()
    {
        var instance = Sample();
        instance.ServerPassword = "hunter2";

        var args = Builder.BuildArguments(instance);

        Assert.Contains("-ServerPassword=hunter2", args);
    }

    [Fact]
    public void Preserves_additional_launch_arguments()
    {
        var instance = Sample();
        instance.AdditionalLaunchArguments.Add("-CustomUnknownFlag");

        var args = Builder.BuildArguments(instance);

        Assert.Contains("-CustomUnknownFlag", args);
    }

    [Fact]
    public void Emits_saved_config_paths_relative_to_saved_root()
    {
        var installPath = @"C:\Servers\Abiotic";
        var instance = Sample();
        instance.InstallPath = installPath;
        instance.SandboxIniPath = Path.Combine(
            installPath,
            "AbioticFactor",
            "Saved",
            "SaveGames",
            "Server",
            "Worlds",
            "Cascade",
            "SandboxSettings.ini");
        instance.AdminIniPath = Path.Combine(
            installPath,
            "AbioticFactor",
            "Saved",
            "SaveGames",
            "Server",
            "Admin.ini");

        var args = Builder.BuildArguments(instance);

        Assert.Contains("-SandboxIniPath=SaveGames/Server/Worlds/Cascade/SandboxSettings.ini", args);
        Assert.Contains("-AdminIniPath=SaveGames/Server/Admin.ini", args);
    }

    [Fact]
    public void Emits_staged_sandbox_path_relative_to_saved_root()
    {
        var installPath = @"C:\Servers\Abiotic";
        var instance = Sample();
        instance.Id = "abc123";
        instance.InstallPath = installPath;
        instance.SandboxIniPath = WorldSaveLayout.StagedSandboxPath(instance);

        var args = Builder.BuildArguments(instance);

        Assert.Contains("-WorldSaveName=Cascade", args);
        Assert.Contains(
            "-SandboxIniPath=Config/FacilityOverseer/abc123/SandboxSettings.ini",
            args);
    }

    [Fact]
    public void Throws_when_asked_to_emit_an_absolute_path_outside_the_server_saved_root()
    {
        // AF's -SandboxIniPath / -AdminIniPath are hard-prefixed with
        // "../../../AbioticFactor/Saved/" by the dedicated server, so an
        // absolute Windows path produces a malformed lookup and AF silently
        // loads defaults instead. Previously this code path emitted the
        // absolute path and accepted that silent fall-through; the new
        // contract is loud failure so the launch orchestrator MUST stage
        // the durable copy into the install Saved tree first.
        var instance = Sample();
        instance.InstallPath = @"C:\Servers\Abiotic";
        instance.SandboxIniPath = @"C:\Data\worlds\w1\config\SandboxSettings.ini";
        instance.AdminIniPath = @"C:\Data\worlds\w1\config\Admin.ini";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Builder.BuildArguments(instance));

        Assert.Contains("Stage the file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Throws_when_install_path_is_missing_so_the_relative_anchor_is_unknown()
    {
        // No install path = no <install>/AbioticFactor/Saved/ to express the
        // path relative to. Refuse rather than emit a path AF cannot load.
        var instance = Sample();
        instance.InstallPath = "";
        instance.SandboxIniPath = @"C:\Data\worlds\w1\config\SandboxSettings.ini";

        Assert.Throws<InvalidOperationException>(() => Builder.BuildArguments(instance));
    }
}
