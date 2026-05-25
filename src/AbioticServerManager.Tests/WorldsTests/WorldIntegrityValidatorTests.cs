using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.WorldsTests;

public class WorldIntegrityValidatorTests
{
    private static WorldIntegrityInputs Healthy() => new()
    {
        SandboxIniPath = @"C:\Data\worlds\w1\config\SandboxSettings.ini",
        SandboxIniExists = true,
        SandboxIniParses = true,
        AdminIniPath = @"C:\Data\worlds\w1\config\Admin.ini",
        AdminIniExists = true,
        WorldSaveFolderResolvable = true,
        SandboxUnderDataRoot = true,
        ServerExecutableFound = true,
    };

    [Fact]
    public void Healthy_inputs_have_no_findings_and_are_launchable()
    {
        var report = WorldIntegrityValidator.Validate(Healthy());

        Assert.Empty(report.Findings);
        Assert.True(report.IsLaunchable);
    }

    [Fact]
    public void Missing_executable_is_a_blocker()
    {
        var inputs = Healthy() with { ServerExecutableFound = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.False(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "EXE_MISSING" && f.Severity == WorldIntegritySeverity.Blocker);
    }

    [Fact]
    public void Unparseable_sandbox_ini_is_a_blocker()
    {
        var inputs = Healthy() with { SandboxIniParses = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.False(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "SANDBOX_INI_UNPARSEABLE" && f.Severity == WorldIntegritySeverity.Blocker);
    }

    [Fact]
    public void Missing_sandbox_ini_is_only_a_warning_so_first_run_can_proceed()
    {
        // On a fresh world, the sandbox INI is generated when the user saves
        // settings - missing on first launch must not block Start.
        var inputs = Healthy() with { SandboxIniExists = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.True(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "SANDBOX_INI_MISSING" && f.Severity == WorldIntegritySeverity.Warning);
    }

    [Fact]
    public void Sandbox_inside_server_install_warns_about_world_identity_migration()
    {
        var inputs = Healthy() with { SandboxUnderDataRoot = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.True(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "SANDBOX_NOT_UNDER_DATAROOT" &&
            f.Detail.Contains("SteamCMD validate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_admin_ini_is_only_info_not_a_blocker()
    {
        var inputs = Healthy() with { AdminIniExists = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.True(report.IsLaunchable);
        Assert.Contains(report.Findings, f =>
            f.Id == "ADMIN_INI_MISSING" && f.Severity == WorldIntegritySeverity.Info);
    }

    /// <summary>
    /// Sec 4.5: findings with a clear one-click fix expose the command hint
    /// so the popup can render a "Save Sandbox" / "Prepare server" button.
    /// </summary>
    [Fact]
    public void Missing_sandbox_finding_exposes_save_sandbox_command()
    {
        var inputs = Healthy() with { SandboxIniExists = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        var finding = report.Findings.Single(f => f.Id == "SANDBOX_INI_MISSING");
        Assert.Equal("SaveSandboxCommand", finding.CommandHint);
        Assert.Equal("Save Sandbox", finding.ActionLabel);
    }

    [Fact]
    public void Missing_executable_finding_exposes_install_command()
    {
        var inputs = Healthy() with { ServerExecutableFound = false };
        var report = WorldIntegrityValidator.Validate(inputs);

        var finding = report.Findings.Single(f => f.Id == "EXE_MISSING");
        Assert.Equal("InstallOrUpdateServerCommand", finding.CommandHint);
        Assert.Contains("Prepare", finding.ActionLabel);
    }

    [Fact]
    public void Multiple_blockers_are_all_reported()
    {
        var inputs = Healthy() with
        {
            ServerExecutableFound = false,
            SandboxIniPath = "",
            SandboxIniExists = false,
            SandboxIniParses = false,
        };
        var report = WorldIntegrityValidator.Validate(inputs);

        Assert.False(report.IsLaunchable);
        Assert.Contains(report.Findings, f => f.Id == "EXE_MISSING");
        Assert.Contains(report.Findings, f => f.Id == "SANDBOX_PATH_UNSET");
    }

    [Fact]
    public void Findings_have_stable_ids_for_dedupe()
    {
        var report1 = WorldIntegrityValidator.Validate(Healthy() with { ServerExecutableFound = false });
        var report2 = WorldIntegrityValidator.Validate(Healthy() with { ServerExecutableFound = false });

        Assert.Equal(
            report1.Findings.Select(f => f.Id).ToList(),
            report2.Findings.Select(f => f.Id).ToList());
    }
}
