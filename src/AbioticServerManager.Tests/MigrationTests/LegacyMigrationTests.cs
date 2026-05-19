using AbioticServerManager.Core.Migration;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Infrastructure.FileSystem;
using AbioticServerManager.Infrastructure.Migration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.MigrationTests;

public sealed class LegacyMigrationTests : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), "fo-migrate-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Candidate_locations_cover_the_documented_legacy_roots()
    {
        var c = LegacyDataLocations.Candidates(@"C:\Users\bob\AppData\Roaming",
                                               @"C:\Users\bob\AppData\Local");

        Assert.Contains(@"C:\Users\bob\AppData\Roaming\FacilityOverseer", c);
        Assert.Contains(@"C:\Users\bob\AppData\Local\FacilityOverseer", c);
        Assert.Contains(@"C:\Facility Overseer", c);
        Assert.Contains(@"C:\AbioticFactorServer", c);
    }

    [Fact]
    public async Task Migrate_copies_config_and_never_touches_the_source()
    {
        var oldRoot = Path.Combine(_tmp, "old");
        Directory.CreateDirectory(oldRoot);
        var oldInstances = Path.Combine(oldRoot, "instances.json");
        File.WriteAllText(oldInstances, "[{\"id\":\"abc\"}]");

        var dataRoot = Path.Combine(_tmp, "data");
        var paths = new AppPaths(dataRoot);
        var svc = new LegacyMigrationService(paths, NullLogger<LegacyMigrationService>.Instance);

        var findings = new List<LegacyFinding>
        {
            new() { Root = oldRoot, HasInstances = true },
        };

        var result = await svc.MigrateAsync(findings);

        Assert.True(result.ImportedConfig);
        Assert.True(File.Exists(paths.InstancesFile));               // copied in
        Assert.Equal(
            File.ReadAllText(oldInstances),
            File.ReadAllText(paths.InstancesFile));
        Assert.True(File.Exists(oldInstances));                      // source untouched
        Assert.True(File.Exists(result.ReportPath));                // report written
        Assert.Contains("never move or delete", File.ReadAllText(result.ReportPath));
    }

    [Fact]
    public async Task Migrate_does_not_overwrite_existing_config()
    {
        var oldRoot = Path.Combine(_tmp, "old");
        Directory.CreateDirectory(oldRoot);
        File.WriteAllText(Path.Combine(oldRoot, "instances.json"), "[\"OLD\"]");

        var dataRoot = Path.Combine(_tmp, "data");
        var paths = new AppPaths(dataRoot);
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.InstancesFile, "[\"CURRENT\"]");

        var svc = new LegacyMigrationService(paths, NullLogger<LegacyMigrationService>.Instance);
        var result = await svc.MigrateAsync(
            [new LegacyFinding { Root = oldRoot, HasInstances = true }]);

        Assert.False(result.ImportedConfig);
        Assert.Equal("[\"CURRENT\"]", File.ReadAllText(paths.InstancesFile));
    }

    [Fact]
    public void Does_not_offer_when_current_root_already_has_profiles()
    {
        var dataRoot = Path.Combine(_tmp, "data");
        var paths = new AppPaths(dataRoot);
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.InstancesFile, "[]");

        var svc = new LegacyMigrationService(paths, NullLogger<LegacyMigrationService>.Instance);

        Assert.False(svc.ShouldOfferMigration(out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }
}
