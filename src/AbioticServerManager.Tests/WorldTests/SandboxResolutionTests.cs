using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.WorldTests;

public sealed class SandboxResolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fo-resolve-" + Guid.NewGuid().ToString("N"));

    private ServerInstance Instance() => new()
    {
        Id = "abc123",
        DisplayName = "Cascade",
        WorldSaveName = "Cascade",
        InstallPath = _root,
    };

    private string WorldsContainer() => Path.Combine(
        _root, "AbioticFactor", "Saved", "SaveGames", "Server", "Worlds");

    [Fact]
    public void No_save_yet_stages_config_and_does_not_touch_world_path()
    {
        var instance = Instance();

        var r = WorldSaveLayout.Resolve(instance);

        Assert.False(r.RealSaveExists);
        Assert.Null(r.ExistingSandboxPath);
        Assert.Equal(WorldSaveLayout.StagedSandboxPath(instance), r.DefaultSandboxTarget);
        Assert.DoesNotContain(
            Path.Combine("SaveGames", "Server", "Worlds"),
            r.DefaultSandboxTarget);
        Assert.Equal("", instance.WorldPath); // resolver must not mutate the instance
    }

    [Fact]
    public void Existing_staged_file_is_loaded_directly()
    {
        var instance = Instance();
        var staged = WorldSaveLayout.StagedSandboxPath(instance);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "[SandboxSettings]\n");

        var r = WorldSaveLayout.Resolve(instance);

        Assert.False(r.RealSaveExists);
        Assert.Equal(staged, r.ExistingSandboxPath);
    }

    [Fact]
    public void Real_save_present_migrates_staged_config_into_the_world()
    {
        var instance = Instance();
        var realFolder = Path.Combine(WorldsContainer(), "Cascade");
        Directory.CreateDirectory(realFolder);
        File.WriteAllText(Path.Combine(realFolder, "WorldSave_MetaData.sav"), "");

        var staged = WorldSaveLayout.StagedSandboxPath(instance);
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "[SandboxSettings]\n");

        var r = WorldSaveLayout.Resolve(instance);

        Assert.True(r.RealSaveExists);
        Assert.Equal(realFolder, r.WorldFolder);
        Assert.Equal(
            Path.Combine(realFolder, DefaultSandboxSettings.FileName),
            r.DefaultSandboxTarget);
        Assert.Equal(staged, r.MigrationSource);
        Assert.Null(r.ExistingSandboxPath); // world sandbox not written yet
    }

    [Fact]
    public void Real_save_with_existing_world_sandbox_is_loaded_in_place()
    {
        var instance = Instance();
        var realFolder = Path.Combine(WorldsContainer(), "Cascade");
        Directory.CreateDirectory(Path.Combine(realFolder, "PlayerData"));
        var worldSandbox = Path.Combine(realFolder, DefaultSandboxSettings.FileName);
        File.WriteAllText(worldSandbox, "[SandboxSettings]\n");

        var r = WorldSaveLayout.Resolve(instance);

        Assert.True(r.RealSaveExists);
        Assert.Equal(worldSandbox, r.ExistingSandboxPath);
    }

    [Fact]
    public void Finds_real_folder_when_game_used_different_casing()
    {
        var instance = Instance(); // WorldSaveName "Cascade"
        var actual = Path.Combine(WorldsContainer(), "CASCADE");
        Directory.CreateDirectory(Path.Combine(actual, "PlayerData"));

        var found = WorldSaveLayout.FindExistingRealWorldFolder(instance);

        // Windows is case-insensitive, so the returned casing is cosmetic - what
        // matters is that the real save is located (not silently missed).
        Assert.False(string.IsNullOrEmpty(found));
        Assert.Equal(
            Path.GetFullPath(actual),
            Path.GetFullPath(found),
            ignoreCase: true);
        Assert.True(WorldSaveLayout.Resolve(instance).RealSaveExists);
    }

    [Fact]
    public void Never_matches_a_different_worlds_save()
    {
        var instance = Instance(); // WorldSaveName "Cascade"
        var otherWorld = Path.Combine(WorldsContainer(), "Outpost");
        Directory.CreateDirectory(Path.Combine(otherWorld, "PlayerData"));

        var found = WorldSaveLayout.FindExistingRealWorldFolder(instance);

        Assert.Equal("", found);
        Assert.False(WorldSaveLayout.Resolve(instance).RealSaveExists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
