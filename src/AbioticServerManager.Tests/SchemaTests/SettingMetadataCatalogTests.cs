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

    /// <summary>Every slider-backed (number) setting in the built-in catalog.</summary>
    private static readonly string[] NumberSettingKeys =
    [
        "DayNightCycleSpeedMultiplier", "SinkRefillRate", "FoodSpoilSpeedMultiplier",
        "RefrigerationEffectivenessMultiplier", "StructuralSupportLimit", "EnemySpawnRate",
        "EnemyHealthMultiplier", "EnemyPlayerDamageMultiplier", "EnemyDeployableDamageMultiplier",
        "DetectionSpeedMultiplier", "DamageToAlliesMultiplier", "HungerSpeedMultiplier",
        "ThirstSpeedMultiplier", "FatigueSpeedMultiplier", "ContinenceSpeedMultiplier",
        "BonusPerkPoints", "PlayerXPGainMultiplier", "ItemStackSizeMultiplier",
        "ItemWeightMultiplier", "ItemDurabilityMultiplier", "DurabilityLossOnDeathMultiplier",
        "BaseInventorySize",
    ];

    [Fact]
    public void Every_number_setting_declares_min_max_and_step()
    {
        // A missing max is the bug that made one slider runaway-sensitive: with
        // no metadata max the UI infers one from the value itself, so the track
        // grows every time the value is pushed up.
        var catalog = new SettingMetadataCatalog();

        foreach (var key in NumberSettingKeys)
        {
            var m = catalog.Find("SandboxSettings", key);
            Assert.NotNull(m);
            Assert.Equal(SettingValueType.Number, m!.Type);
            Assert.True(m.Min is not null, $"{key} is missing a min.");
            Assert.True(m.Max is not null, $"{key} is missing a max.");
            Assert.True(m.Step is not null, $"{key} is missing a step.");
            Assert.True(m.Step > 0, $"{key} has a non-positive step.");
            Assert.True(m.Min < m.Max, $"{key} has min >= max.");
        }
    }

    [Theory]
    [InlineData("DayNightCycleSpeedMultiplier")]
    [InlineData("StructuralSupportLimit")]
    [InlineData("EnemyHealthMultiplier")]
    [InlineData("EnemyPlayerDamageMultiplier")]
    [InlineData("BonusPerkPoints")]
    [InlineData("BaseInventorySize")]
    [InlineData("DamageToAlliesMultiplier")]
    [InlineData("DurabilityLossOnDeathMultiplier")]
    public void Number_setting_default_sits_on_the_snap_grid(string key)
    {
        // With slider tick-snapping enabled, both the default and the max must
        // be an exact number of steps from the min — otherwise the user can
        // never land the slider on the default value.
        var m = new SettingMetadataCatalog().Find("SandboxSettings", key)!;
        var min = m.Min!.Value;
        var max = m.Max!.Value;
        var step = m.Step!.Value;

        Assert.True(double.TryParse(
            m.Default,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var def),
            $"{key} default '{m.Default}' is not numeric.");

        Assert.InRange(def, min, max);
        AssertOnGrid(def - min, step, $"{key} default {def} is off the step grid");
        AssertOnGrid(max - min, step, $"{key} max {max} is off the step grid");
    }

    [Fact]
    public void Structural_support_limit_is_bounded()
    {
        // Regression: this slider used to ship without a max.
        var m = new SettingMetadataCatalog().Find("SandboxSettings", "StructuralSupportLimit");
        Assert.NotNull(m);
        Assert.NotNull(m!.Max);
        Assert.True(m.Max <= 100, "Structural Support Limit max should be a sane bound.");
    }

    [Theory]
    [InlineData("StructuralSupportLimit")]
    [InlineData("BonusPerkPoints")]
    [InlineData("BaseInventorySize")]
    public void Whole_number_settings_have_an_integer_step(string key)
    {
        var m = new SettingMetadataCatalog().Find("SandboxSettings", key)!;
        Assert.NotNull(m.Step);
        Assert.True(m.Step!.Value >= 1.0 && m.Step.Value % 1.0 == 0.0,
            $"{key} should have a whole-number step so its slider snaps to integers.");
    }

    private static void AssertOnGrid(double span, double step, string because)
    {
        var steps = span / step;
        Assert.True(Math.Abs(steps - Math.Round(steps)) < 0.001, because);
    }
}
