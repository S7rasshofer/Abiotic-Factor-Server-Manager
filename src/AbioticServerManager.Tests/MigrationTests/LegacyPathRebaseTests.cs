using AbioticServerManager.Core.Migration;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Tests.MigrationTests;

/// <summary>
/// Pinned behaviour for the post-import path scrub: stale absolute paths that
/// no longer exist on disk get reset so the rest of the app's canonical-fallback
/// logic resolves them under the current data root. Paths that still exist are
/// preserved (a user with an adopted external install must not have their
/// install path silently overwritten).
/// </summary>
public sealed class LegacyPathRebaseTests
{
    private const string DefaultInstall = @"D:\new-root\servers\abiotic-factor-dedicated";

    private static ServerInstance MakeInstance(
        string installPath = "",
        string worldPath = "",
        string sandboxIniPath = "",
        string adminIniPath = "") => new()
        {
            Id = "world-id",
            DisplayName = "Cascade",
            InstallPath = installPath,
            WorldPath = worldPath,
            SandboxIniPath = sandboxIniPath,
            AdminIniPath = adminIniPath,
        };

    [Fact]
    public void Stale_install_path_is_reset_to_the_current_default()
    {
        var stale = @"C:\Users\bob\AppData\Local\FacilityOverseer\servers\abiotic-factor-dedicated";
        var instance = MakeInstance(installPath: stale);

        var rebased = LegacyPathRebase.ScrubStalePaths(
            instance,
            DefaultInstall,
            directoryExists: _ => false,
            fileExists: _ => false);

        Assert.Equal(DefaultInstall, rebased.InstallPath);
    }

    [Fact]
    public void Existing_install_path_is_preserved()
    {
        var adopted = @"E:\Servers\AbioticFactor";
        var instance = MakeInstance(installPath: adopted);

        var rebased = LegacyPathRebase.ScrubStalePaths(
            instance,
            DefaultInstall,
            directoryExists: p => string.Equals(p, adopted, StringComparison.OrdinalIgnoreCase),
            fileExists: _ => false);

        Assert.Equal(adopted, rebased.InstallPath);
    }

    [Fact]
    public void Stale_sandbox_admin_world_paths_are_reset_to_empty()
    {
        var instance = MakeInstance(
            sandboxIniPath: @"C:\nope\SandboxSettings.ini",
            adminIniPath: @"C:\nope\Admin.ini",
            worldPath: @"C:\nope\Worlds\Cascade");

        var rebased = LegacyPathRebase.ScrubStalePaths(
            instance,
            DefaultInstall,
            directoryExists: _ => false,
            fileExists: _ => false);

        Assert.Equal("", rebased.SandboxIniPath);
        Assert.Equal("", rebased.AdminIniPath);
        Assert.Equal("", rebased.WorldPath);
    }

    [Fact]
    public void Empty_paths_are_left_empty_not_replaced()
    {
        var instance = MakeInstance();

        var rebased = LegacyPathRebase.ScrubStalePaths(
            instance,
            DefaultInstall,
            directoryExists: _ => false,
            fileExists: _ => false);

        // The scrub only acts on paths that are SET but stale. A fresh world
        // with empty paths must not have a default install path injected
        // behind the user's back.
        Assert.Equal("", rebased.InstallPath);
        Assert.Equal("", rebased.WorldPath);
        Assert.Equal("", rebased.SandboxIniPath);
        Assert.Equal("", rebased.AdminIniPath);
    }

    [Fact]
    public void Returns_a_clone_so_the_caller_can_diff()
    {
        var instance = MakeInstance(installPath: @"E:\Servers\AbioticFactor");

        var rebased = LegacyPathRebase.ScrubStalePaths(
            instance,
            DefaultInstall,
            directoryExists: _ => true,
            fileExists: _ => true);

        Assert.NotSame(instance, rebased);
    }
}
