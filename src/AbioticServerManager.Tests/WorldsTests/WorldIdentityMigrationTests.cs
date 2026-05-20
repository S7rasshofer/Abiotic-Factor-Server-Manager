using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.WorldsTests;

public class WorldIdentityMigrationTests
{
    private static readonly DateTimeOffset Then = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Then.AddDays(1);

    [Fact]
    public void None_when_legacy_missing()
    {
        var action = WorldIdentityMigration.PlanAction(
            legacyExists: false,
            targetExists: false);

        Assert.Equal(WorldIdentityMigrationAction.None, action);
    }

    [Fact]
    public void Copy_when_only_legacy_exists()
    {
        var action = WorldIdentityMigration.PlanAction(
            legacyExists: true,
            targetExists: false,
            legacyWriteTime: Then);

        Assert.Equal(WorldIdentityMigrationAction.CopyLegacyToTarget, action);
    }

    [Fact]
    public void Already_migrated_when_both_exist_and_target_newer()
    {
        var action = WorldIdentityMigration.PlanAction(
            legacyExists: true,
            targetExists: true,
            legacyWriteTime: Then,
            targetWriteTime: Later);

        Assert.Equal(WorldIdentityMigrationAction.AlreadyMigrated, action);
    }

    [Fact]
    public void Already_migrated_when_both_exist_and_target_same_age()
    {
        var action = WorldIdentityMigration.PlanAction(
            legacyExists: true,
            targetExists: true,
            legacyWriteTime: Then,
            targetWriteTime: Then);

        Assert.Equal(WorldIdentityMigrationAction.AlreadyMigrated, action);
    }

    [Fact]
    public void Plan_returns_copy_step_for_legacy_only_files()
    {
        var legacySandbox = @"C:\install\AbioticFactor\Saved\Config\FacilityOverseer\W1\SandboxSettings.ini";
        var legacyAdmin = @"C:\install\AbioticFactor\Saved\SaveGames\Server\Admin.ini";
        var targetSandbox = @"D:\Data\worlds\W1\config\SandboxSettings.ini";
        var targetAdmin = @"D:\Data\worlds\W1\config\Admin.ini";

        var fileExists = (string p) => p == legacySandbox || p == legacyAdmin;
        var writeTime = (string p) => (DateTimeOffset?)Then;

        var plan = WorldIdentityMigration.Plan(
            legacySandbox, legacyAdmin, targetSandbox, targetAdmin,
            fileExists, writeTime);

        Assert.True(plan.HasWork);
        Assert.Equal(WorldIdentityMigrationAction.CopyLegacyToTarget, plan.Sandbox.Action);
        Assert.Equal(WorldIdentityMigrationAction.CopyLegacyToTarget, plan.Admin.Action);
        Assert.Equal(targetSandbox, plan.Sandbox.TargetPath);
        Assert.Equal(legacyAdmin, plan.Admin.LegacyPath);
    }

    [Fact]
    public void Plan_returns_no_work_when_targets_already_exist()
    {
        var legacySandbox = @"C:\install\old\SandboxSettings.ini";
        var legacyAdmin = @"C:\install\old\Admin.ini";
        var targetSandbox = @"D:\Data\worlds\W1\config\SandboxSettings.ini";
        var targetAdmin = @"D:\Data\worlds\W1\config\Admin.ini";

        // Everything exists; target is newer than legacy.
        var fileExists = (string _) => true;
        var writeTime = (string p) =>
            (DateTimeOffset?)(p.StartsWith("D:") ? Later : Then);

        var plan = WorldIdentityMigration.Plan(
            legacySandbox, legacyAdmin, targetSandbox, targetAdmin,
            fileExists, writeTime);

        Assert.False(plan.HasWork);
        Assert.Equal(WorldIdentityMigrationAction.AlreadyMigrated, plan.Sandbox.Action);
        Assert.Equal(WorldIdentityMigrationAction.AlreadyMigrated, plan.Admin.Action);
    }

    [Fact]
    public void Plan_skips_when_legacy_path_is_blank()
    {
        // Fresh world that has never had files in the old layout: nothing to do.
        var plan = WorldIdentityMigration.Plan(
            legacySandboxPath: null,
            legacyAdminPath: "",
            targetSandboxPath: @"D:\Data\worlds\W1\config\SandboxSettings.ini",
            targetAdminPath: @"D:\Data\worlds\W1\config\Admin.ini",
            fileExists: _ => false,
            writeTime: _ => null);

        Assert.False(plan.HasWork);
        Assert.Equal(WorldIdentityMigrationAction.None, plan.Sandbox.Action);
        Assert.Equal(WorldIdentityMigrationAction.None, plan.Admin.Action);
    }

    [Fact]
    public void Idempotent_when_called_twice_after_migration()
    {
        var legacy = @"C:\install\old\Admin.ini";
        var target = @"D:\Data\worlds\W1\config\Admin.ini";

        // First call: only legacy exists → would copy.
        var existsBefore = (string p) => p == legacy;
        var planBefore = WorldIdentityMigration.Plan(
            legacy, legacy, target, target, existsBefore, _ => Then);
        Assert.True(planBefore.HasWork);

        // Second call after a hypothetical copy: both exist, target is newer.
        var existsAfter = (string _) => true;
        var writeAfter = (string p) =>
            (DateTimeOffset?)(p == target ? Later : Then);
        var planAfter = WorldIdentityMigration.Plan(
            legacy, legacy, target, target, existsAfter, writeAfter);

        Assert.False(planAfter.HasWork);
    }
}
