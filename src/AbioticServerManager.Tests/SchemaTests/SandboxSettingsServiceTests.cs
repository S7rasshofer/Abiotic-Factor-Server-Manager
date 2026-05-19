using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Infrastructure.Persistence;

namespace AbioticServerManager.Tests.SchemaTests;

public class SandboxSettingsServiceTests : IDisposable
{
    private sealed class FakeSchemaCache : ISchemaCache
    {
        public List<(string, string)> Recorded { get; } = [];
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public bool HasSeen(string section, string key) => false;
        public Task RecordAsync(IEnumerable<(string Section, string Key)> keys, CancellationToken ct = default)
        {
            Recorded.AddRange(keys);
            return Task.CompletedTask;
        }
    }

    private const string SampleIni =
        "; Generated sandbox settings\n" +
        "[SandboxSettings]\n" +
        "EnemySpawnRate=1.0\n" +
        "HardcoreMode=False\n" +
        "; future key the app has never heard of\n" +
        "MysteryFutureKey=banana\n";

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"fo-sbx-{Guid.NewGuid():N}.ini");
    private readonly SandboxSettingsService _service =
        new(new SettingMetadataCatalog(), new FakeSchemaCache());

    [Fact]
    public async Task Loads_and_merges_metadata()
    {
        await File.WriteAllTextAsync(_file, SampleIni);
        var doc = await _service.LoadAsync(_file);

        var spawn = doc.Settings.Single(s => s.Key == "EnemySpawnRate");
        Assert.True(spawn.IsKnown);
        Assert.Equal("Enemy", spawn.Category);
        Assert.Equal(SettingControlType.Slider, spawn.ControlType);

        var mystery = doc.Settings.Single(s => s.Key == "MysteryFutureKey");
        Assert.False(mystery.IsKnown);
        Assert.Equal("Advanced", mystery.Category);
        Assert.Equal(SettingSource.InferredFallback, mystery.Source);
    }

    [Fact]
    public async Task Edit_then_save_preserves_comments_and_unknown_keys()
    {
        await File.WriteAllTextAsync(_file, SampleIni);
        var doc = await _service.LoadAsync(_file);

        var hardcore = doc.Settings.Single(s => s.Key == "HardcoreMode");
        _service.Set(doc, hardcore, "True");
        await _service.SaveAsync(doc);

        var written = await File.ReadAllTextAsync(_file);
        Assert.Contains("HardcoreMode=True", written);
        Assert.Contains("; Generated sandbox settings", written);
        Assert.Contains("; future key the app has never heard of", written);
        Assert.Contains("MysteryFutureKey=banana", written);
        Assert.Equal("True", hardcore.Value);
    }

    [Fact]
    public async Task Missing_file_loads_as_empty_document()
    {
        var doc = await _service.LoadAsync(_file);
        Assert.Empty(doc.Settings);
    }

    [Fact]
    public async Task Grouping_orders_categories()
    {
        await File.WriteAllTextAsync(_file, SampleIni);
        var doc = await _service.LoadAsync(_file);

        var groups = doc.GroupByCategory(["World", "Player", "Enemy", "Survival", "Items", "Advanced"]);
        var names = groups.Select(g => g.Category).ToList();

        Assert.Equal(names.OrderBy(n => names.IndexOf(n)), names);
        Assert.Contains("Advanced", names);
    }

    private const string MultiSectionIni =
        "[World]\n" +
        "DayNightCycleState=Normal\n" +
        "SomeUnknownWorldKnob=2\n" +
        "[Player]\n" +
        "HardcoreMode=False\n" +
        "UnknownPlayerThing=banana\n" +
        "[Enemy]\n" +
        "EnemySpawnRate=1.0\n" +
        "CustomEnemyFlag=True\n";

    [Fact]
    public async Task Real_world_multi_section_file_buckets_by_section()
    {
        await File.WriteAllTextAsync(_file, MultiSectionIni);
        var doc = await _service.LoadAsync(_file);

        // Known keys: metadata still resolves even though the section is [Player]/[Enemy]
        // rather than the catalog's [SandboxSettings] (key-only fallback).
        Assert.Equal("Enemy", doc.Settings.Single(s => s.Key == "EnemySpawnRate").Category);
        Assert.Equal("World", doc.Settings.Single(s => s.Key == "HardcoreMode").Category);

        // Unknown keys bucket by their INI section, NOT dumped into Advanced.
        Assert.Equal("World", doc.Settings.Single(s => s.Key == "SomeUnknownWorldKnob").Category);
        Assert.Equal("Player", doc.Settings.Single(s => s.Key == "UnknownPlayerThing").Category);
        Assert.Equal("Enemy", doc.Settings.Single(s => s.Key == "CustomEnemyFlag").Category);
    }

    [Fact]
    public async Task Lowercase_section_names_still_bucket_correctly()
    {
        await File.WriteAllTextAsync(_file, "[player]\nMyCustomKnob=3\n");
        var doc = await _service.LoadAsync(_file);

        Assert.Equal("Player", doc.Settings.Single(s => s.Key == "MyCustomKnob").Category);
    }

    // Mirrors the real Abiotic Factor SandboxSettings.ini: one [SandboxSettings] section
    // with category banner comments.
    private const string RealAbioticIni =
        "[SandboxSettings]\n" +
        "; === WORLD ===\n" +
        "GameDifficulty=1\n" +
        "HardcoreMode=False\n" +
        "LootRespawnEnabled=False\n" +
        "PowerSocketsOffAtNight=True\n" +
        "DayNightCycleState=0\n" +
        "DayNightCycleSpeedMultiplier=1.0\n" +
        "WeatherFrequency=3\n" +
        "SinkRefillRate=1.0\n" +
        "FoodSpoilSpeedMultiplier=1.0\n" +
        "RefrigerationEffectivenessMultiplier=1.0\n" +
        "StorageByTag=True\n" +
        "StructuralSupportLimit=5\n" +
        "BridgeSupports=2\n" +
        "HomeWorlds=True\n" +
        "InvisibleRadiation=False\n" +
        "TaintedSinkWater=False\n" +
        "RadiationDealsDamage=False\n" +
        "\n" +
        "; === ENEMY ===\n" +
        "EnemySpawnRate=1.0\n" +
        "EnemyHealthMultiplier=1.00\n" +
        "EnemyPlayerDamageMultiplier=1.00\n" +
        "EnemyDeployableDamageMultiplier=1.0\n" +
        "DetectionSpeedMultiplier=1.0\n" +
        "EnemyAccuracy=2\n" +
        "\n" +
        "; === PLAYER ===\n" +
        "DamageToAlliesMultiplier=0.50\n" +
        "HungerSpeedMultiplier=1.0\n" +
        "ThirstSpeedMultiplier=1.0\n" +
        "FatigueSpeedMultiplier=1.0\n" +
        "ContinenceSpeedMultiplier=1.0\n" +
        "BonusPerkPoints=0\n" +
        "PlayerXPGainMultiplier=1.0\n" +
        "ItemStackSizeMultiplier=1.0\n" +
        "ItemWeightMultiplier=1.0\n" +
        "ItemDurabilityMultiplier=1.0\n" +
        "DurabilityLossOnDeathMultiplier=0.10\n" +
        "ShowDeathMessages=True\n" +
        "AllowRecipeSharing=True\n" +
        "AllowPagers=True\n" +
        "AllowTransmog=True\n" +
        "DisableResearchMinigame=False\n" +
        "DeathPenalties=1\n" +
        "FirstTimeStartingWeapon=0\n" +
        "HostAccessPlayerCorpses=True\n" +
        "AllowCharacterReset=True\n" +
        "BaseInventorySize=12\n" +
        "PlayerFurnitureDestruction=False\n" +
        "AllowIronMode=True\n";

    [Fact]
    public async Task Buckets_by_banner_comment_grouping()
    {
        await File.WriteAllTextAsync(_file, RealAbioticIni);
        var doc = await _service.LoadAsync(_file);

        Assert.Equal("World", doc.Settings.Single(s => s.Key == "GameDifficulty").Category);
        Assert.Equal("World", doc.Settings.Single(s => s.Key == "HardcoreMode").Category);
        Assert.Equal("World", doc.Settings.Single(s => s.Key == "DayNightCycleState").Category);
        Assert.Equal("Enemy", doc.Settings.Single(s => s.Key == "EnemySpawnRate").Category);
        Assert.Equal("Enemy", doc.Settings.Single(s => s.Key == "EnemyAccuracy").Category);
        Assert.Equal("Player", doc.Settings.Single(s => s.Key == "DamageToAlliesMultiplier").Category);
        Assert.Equal("Player", doc.Settings.Single(s => s.Key == "HungerSpeedMultiplier").Category);
    }

    [Fact]
    public async Task Current_default_sandbox_settings_all_have_metadata()
    {
        await File.WriteAllTextAsync(_file, RealAbioticIni);
        var doc = await _service.LoadAsync(_file);

        Assert.All(doc.Settings, setting => Assert.True(setting.IsKnown, setting.Key));
    }

    [Fact]
    public async Task Embedded_default_sandbox_settings_loads_as_real_document()
    {
        var template = await DefaultSandboxSettings.LoadTemplateAsync();
        await File.WriteAllTextAsync(_file, template);

        var doc = await _service.LoadAsync(_file);

        Assert.NotEmpty(doc.Settings);
        Assert.Contains(doc.Settings, setting => setting.Key == "GameDifficulty");
        Assert.Contains(doc.Settings, setting => setting.Key == "EnemySpawnRate");
        Assert.Contains(doc.Settings, setting => setting.Key == "HungerSpeedMultiplier");
        Assert.All(doc.Settings, setting => Assert.True(setting.IsKnown, setting.Key));
    }

    [Fact]
    public async Task Banner_comments_survive_save()
    {
        await File.WriteAllTextAsync(_file, RealAbioticIni);
        var doc = await _service.LoadAsync(_file);

        _service.Set(doc, doc.Settings.Single(s => s.Key == "GameDifficulty"), "2");
        await _service.SaveAsync(doc);

        var written = await File.ReadAllTextAsync(_file);
        Assert.Contains("; === WORLD ===", written);
        Assert.Contains("; === ENEMY ===", written);
        Assert.Contains("; === PLAYER ===", written);
        Assert.Contains("GameDifficulty=2", written);
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}
