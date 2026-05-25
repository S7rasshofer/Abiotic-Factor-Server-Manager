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

    /// <summary>
    /// The import dialog shows "Worlds: A, B, C" so the user knows what's
    /// about to be brought in. The preview must read displayName from a
    /// camelCase legacy file without committing to the import.
    /// </summary>
    [Fact]
    public void PeekWorldNames_reads_camelCase_display_names()
    {
        Directory.CreateDirectory(_tmp);
        var legacy = Path.Combine(_tmp, "instances.json");
        File.WriteAllText(legacy, """
            [
              { "id": "a", "displayName": "Cascade" },
              { "id": "b", "displayName": "Reactor Annex" }
            ]
            """);

        var names = LegacyMigrationService.PeekWorldNames(legacy);

        Assert.Equal(new[] { "Cascade", "Reactor Annex" }, names);
        // Source untouched by the preview.
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void PeekWorldNames_tolerates_pascal_case_legacy_shape()
    {
        Directory.CreateDirectory(_tmp);
        var legacy = Path.Combine(_tmp, "instances.json");
        File.WriteAllText(legacy, """[ { "Id": "a", "DisplayName": "Old World" } ]""");

        var names = LegacyMigrationService.PeekWorldNames(legacy);

        Assert.Equal(new[] { "Old World" }, names);
    }

    [Fact]
    public void PeekWorldNames_returns_empty_on_missing_or_unparseable_file()
    {
        Directory.CreateDirectory(_tmp);
        var missing = Path.Combine(_tmp, "nope.json");
        Assert.Empty(LegacyMigrationService.PeekWorldNames(missing));
        Assert.Empty(LegacyMigrationService.PeekWorldNames(""));
    }

    /// <summary>
    /// "Start fresh" must write the same marker the import would write, so
    /// the dialog does not pop on the next launch. The marker lives at
    /// &lt;DataRoot&gt;/config/.legacy-migration-done.
    /// </summary>
    [Fact]
    public async Task MarkMigrationDeclined_writes_the_marker()
    {
        var dataRoot = Path.Combine(_tmp, "data");
        var paths = new AppPaths(dataRoot);
        var svc = new LegacyMigrationService(paths, NullLogger<LegacyMigrationService>.Instance);

        await svc.MarkMigrationDeclinedAsync();

        var marker = Path.Combine(paths.ConfigDirectory, ".legacy-migration-done");
        Assert.True(File.Exists(marker));
        Assert.StartsWith("declined:", File.ReadAllText(marker));
    }

    /// <summary>
    /// Defence-in-depth: even before reading the marker, ShouldOfferMigration
    /// gates on the marker file. Decline -> no offer on re-evaluation.
    /// </summary>
    [Fact]
    public async Task ShouldOfferMigration_is_false_after_decline()
    {
        var dataRoot = Path.Combine(_tmp, "data");
        var paths = new AppPaths(dataRoot);
        var svc = new LegacyMigrationService(paths, NullLogger<LegacyMigrationService>.Instance);

        await svc.MarkMigrationDeclinedAsync();

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
