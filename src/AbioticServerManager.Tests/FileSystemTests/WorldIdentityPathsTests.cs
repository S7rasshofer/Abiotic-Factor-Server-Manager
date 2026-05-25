using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.FileSystemTests;

/// <summary>
/// Sec 2.1 acceptance tests: per-world config lives under
/// <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/config/</c>, a server install wipe
/// does not touch it, and the legacy -> new copy is idempotent.
/// </summary>
public class WorldIdentityPathsTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly WorldIdentityMigrationService _service;

    public WorldIdentityPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fo-world-identity-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _service = new WorldIdentityMigrationService(
            _paths, NullLogger<WorldIdentityMigrationService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Per_world_paths_live_under_data_root()
    {
        const string id = "world-1";

        Assert.Equal(
            Path.Combine(_root, "worlds", id, "config", "SandboxSettings.ini"),
            _paths.WorldSandboxIniPath(id));

        Assert.Equal(
            Path.Combine(_root, "worlds", id, "config", "Admin.ini"),
            _paths.WorldAdminIniPath(id));

        Assert.Equal(
            Path.Combine(_root, "worlds", id, "config"),
            _paths.WorldConfigDirectory(id));
    }

    [Fact]
    public void EnsureWorldCreated_makes_durable_subfolders_only()
    {
        const string id = "world-2";
        _paths.EnsureWorldCreated(id);

        Assert.True(Directory.Exists(_paths.WorldConfigDirectory(id)));
        Assert.True(Directory.Exists(_paths.WorldSavesDirectory(id)));
        Assert.True(Directory.Exists(_paths.WorldRosterDirectory(id)));
        Assert.True(Directory.Exists(_paths.WorldRuntimeDirectory(id)));

        // Nothing in the server install was created.
        Assert.False(Directory.Exists(Path.Combine(_root, "servers", "abiotic-factor-dedicated", "AbioticFactor")));
    }

    [Fact]
    public async Task Migration_copies_legacy_files_to_new_location_and_leaves_originals()
    {
        const string id = "world-migrate";

        // Simulate the legacy in-server-install layout.
        var legacyInstall = Path.Combine(_root, "servers", "abiotic-factor-dedicated");
        var legacySandbox = Path.Combine(
            legacyInstall,
            "AbioticFactor", "Saved", "Config", "FacilityOverseer", id,
            "SandboxSettings.ini");
        var legacyAdmin = Path.Combine(
            legacyInstall,
            "AbioticFactor", "Saved", "SaveGames", "Server",
            "Admin.ini");

        Directory.CreateDirectory(Path.GetDirectoryName(legacySandbox)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyAdmin)!);
        await File.WriteAllTextAsync(legacySandbox, "ZombieDamageMultiplier=2.5");
        await File.WriteAllTextAsync(legacyAdmin, "[Moderators]\nModerator=76561198000000001");

        var instance = new ServerInstance
        {
            Id = id,
            DisplayName = "MigrateMe",
            InstallPath = legacyInstall,
            SandboxIniPath = legacySandbox,
            AdminIniPath = legacyAdmin,
        };

        var result = await _service.MigrateIfNeededAsync(instance);

        Assert.True(result.HadWork);
        Assert.Equal(2, result.CopiedDescriptions.Count);

        // New location has the content.
        Assert.True(File.Exists(_paths.WorldSandboxIniPath(id)));
        Assert.True(File.Exists(_paths.WorldAdminIniPath(id)));
        Assert.Contains("ZombieDamageMultiplier", await File.ReadAllTextAsync(_paths.WorldSandboxIniPath(id)));
        Assert.Contains("Moderators", await File.ReadAllTextAsync(_paths.WorldAdminIniPath(id)));

        // ServerInstance now points at the new in-DataRoot paths.
        Assert.Equal(_paths.WorldSandboxIniPath(id), instance.SandboxIniPath);
        Assert.Equal(_paths.WorldAdminIniPath(id), instance.AdminIniPath);

        // Legacy files are left in place (copy-not-delete).
        Assert.True(File.Exists(legacySandbox));
        Assert.True(File.Exists(legacyAdmin));
    }

    [Fact]
    public async Task Migration_is_idempotent_when_target_already_exists()
    {
        const string id = "world-idempotent";

        // Set up legacy + target. Target is intentionally NEWER so the planner
        // treats it as already migrated.
        var legacyInstall = Path.Combine(_root, "servers", "abiotic-factor-dedicated");
        var legacySandbox = Path.Combine(
            legacyInstall,
            "AbioticFactor", "Saved", "Config", "FacilityOverseer", id,
            "SandboxSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySandbox)!);
        await File.WriteAllTextAsync(legacySandbox, "OldValue=1");
        File.SetLastWriteTimeUtc(legacySandbox, DateTime.UtcNow.AddDays(-2));

        _paths.EnsureWorldCreated(id);
        var targetSandbox = _paths.WorldSandboxIniPath(id);
        await File.WriteAllTextAsync(targetSandbox, "NewValue=2");
        File.SetLastWriteTimeUtc(targetSandbox, DateTime.UtcNow);

        var instance = new ServerInstance
        {
            Id = id,
            InstallPath = legacyInstall,
            SandboxIniPath = legacySandbox,
        };

        var result = await _service.MigrateIfNeededAsync(instance);

        Assert.False(result.HadWork);
        // Target's content is unchanged - we did NOT overwrite a newer in-DataRoot file.
        Assert.Equal("NewValue=2", await File.ReadAllTextAsync(targetSandbox));
        // The path field still points at the canonical new location.
        Assert.Equal(targetSandbox, instance.SandboxIniPath);
    }

    [Fact]
    public async Task Server_install_wipe_does_not_touch_worlds_directory()
    {
        const string id = "world-survives";
        _paths.EnsureWorldCreated(id);

        var sandbox = _paths.WorldSandboxIniPath(id);
        await File.WriteAllTextAsync(sandbox, "ImportantTuning=42");

        // Simulate a SteamCMD wipe: blow away <VolatileRoot>/servers/.
        var serversDir = Path.Combine(_paths.VolatileRoot, "servers");
        Directory.CreateDirectory(Path.Combine(serversDir, "abiotic-factor-dedicated", "AbioticFactor"));
        Directory.Delete(serversDir, recursive: true);

        Assert.True(File.Exists(sandbox), "Per-world sandbox MUST survive a server install wipe");
        Assert.Equal("ImportantTuning=42", await File.ReadAllTextAsync(sandbox));
    }

    [Fact]
    public async Task Fresh_world_with_no_legacy_just_sets_canonical_paths()
    {
        const string id = "world-fresh";
        var instance = new ServerInstance
        {
            Id = id,
            DisplayName = "BrandNew",
            InstallPath = "",
            SandboxIniPath = "",
            AdminIniPath = "",
        };

        var result = await _service.MigrateIfNeededAsync(instance);

        Assert.False(result.HadWork);
        Assert.Equal(_paths.WorldSandboxIniPath(id), instance.SandboxIniPath);
        Assert.Equal(_paths.WorldAdminIniPath(id), instance.AdminIniPath);
        Assert.True(Directory.Exists(_paths.WorldConfigDirectory(id)));
    }

    [Fact]
    public async Task Already_in_new_layout_is_treated_as_no_work()
    {
        const string id = "world-already-new";
        _paths.EnsureWorldCreated(id);
        var canonical = _paths.WorldAdminIniPath(id);
        await File.WriteAllTextAsync(canonical, "[Moderators]\n");

        var instance = new ServerInstance
        {
            Id = id,
            AdminIniPath = canonical,
            SandboxIniPath = _paths.WorldSandboxIniPath(id),
        };

        var result = await _service.MigrateIfNeededAsync(instance);

        Assert.False(result.HadWork);
        Assert.Equal(canonical, instance.AdminIniPath);
    }
}
