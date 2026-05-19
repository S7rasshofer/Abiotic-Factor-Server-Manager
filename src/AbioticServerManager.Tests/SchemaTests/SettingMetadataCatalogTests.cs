using AbioticServerManager.Core.Schema;

namespace AbioticServerManager.Tests.SchemaTests;

public class SettingMetadataCatalogTests
{
    [Fact]
    public void Loads_built_in_metadata()
    {
        var catalog = new SettingMetadataCatalog();

        var enemy = catalog.Find("SandboxSettings", "EnemySpawnRate");

        Assert.NotNull(enemy);
        Assert.Equal("Enemy Spawn Rate", enemy!.DisplayName);
        Assert.Equal(SettingValueType.Number, enemy.Type);
        Assert.Equal(0.5, enemy.Min);
        Assert.Equal(3.0, enemy.Max);
    }

    [Fact]
    public void Enum_settings_carry_labels_and_clean_descriptions()
    {
        var catalog = new SettingMetadataCatalog();

        var difficulty = catalog.Find("SandboxSettings", "GameDifficulty");

        Assert.NotNull(difficulty);
        Assert.Equal(SettingValueType.Enum, difficulty!.Type);
        // Canonical values still written loss-lessly.
        Assert.Equal(["1", "2", "3"], difficulty.Options);
        // Human labels index-aligned with the values.
        Assert.Equal(["Normal", "Hard", "Apocalyptic"], difficulty.OptionLabels);
        // The "1=Normal, 2=Hard…" legend must NOT live in the description anymore.
        Assert.DoesNotContain("=", difficulty.Description ?? "");
        Assert.DoesNotContain("Values", difficulty.Description ?? "");
    }

    [Fact]
    public void Every_enum_with_options_has_matching_labels()
    {
        var catalog = new SettingMetadataCatalog();

        foreach (var key in new[]
                 {
                     "GameDifficulty", "DayNightCycleState", "WeatherFrequency",
                     "BridgeSupports", "EnemyAccuracy", "DeathPenalties",
                     "FirstTimeStartingWeapon",
                 })
        {
            var m = catalog.Find("SandboxSettings", key);
            Assert.NotNull(m);
            Assert.Equal(m!.Options.Count, m.OptionLabels.Count);
            Assert.DoesNotContain("Values", m.Description ?? "");
        }
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var catalog = new SettingMetadataCatalog();
        Assert.NotNull(catalog.Find("sandboxsettings", "hardcoremode"));
    }

    [Fact]
    public void Unknown_key_returns_null()
    {
        var catalog = new SettingMetadataCatalog();
        Assert.Null(catalog.Find("SandboxSettings", "TotallyMadeUpFutureKey"));
    }

    [Fact]
    public void Categories_are_ordered()
    {
        var catalog = new SettingMetadataCatalog();
        var cats = catalog.Categories;

        Assert.Contains("World", cats);
        Assert.Contains("Enemy", cats);
        Assert.True(cats.ToList().IndexOf("World") < cats.ToList().IndexOf("Enemy"));
    }

    [Fact]
    public void Override_file_layers_on_top_of_built_in()
    {
        var overridePath = Path.Combine(Path.GetTempPath(), $"fo-md-{Guid.NewGuid():N}.json");
        File.WriteAllText(overridePath, """
        {
          "SandboxSettings": {
            "EnemySpawnRate": { "displayName": "Custom Spawn", "category": "Enemy", "type": "number" },
            "BrandNewFutureKey": { "displayName": "Future Thing", "category": "World", "type": "text" }
          }
        }
        """);

        try
        {
            var catalog = new SettingMetadataCatalog(overridePath);

            Assert.Equal("Custom Spawn", catalog.Find("SandboxSettings", "EnemySpawnRate")!.DisplayName);
            Assert.NotNull(catalog.Find("SandboxSettings", "BrandNewFutureKey"));
        }
        finally
        {
            File.Delete(overridePath);
        }
    }

    [Fact]
    public void Broken_override_file_does_not_break_catalog()
    {
        var overridePath = Path.Combine(Path.GetTempPath(), $"fo-md-{Guid.NewGuid():N}.json");
        File.WriteAllText(overridePath, "{ not valid json");

        try
        {
            var catalog = new SettingMetadataCatalog(overridePath);
            Assert.NotNull(catalog.Find("SandboxSettings", "EnemySpawnRate"));
        }
        finally
        {
            File.Delete(overridePath);
        }
    }
}
