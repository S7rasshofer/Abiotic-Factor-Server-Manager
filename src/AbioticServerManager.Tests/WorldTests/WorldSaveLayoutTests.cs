using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Tests.WorldTests;

public sealed class WorldSaveLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fo-world-layout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Staged_sandbox_path_lives_under_saved_config_not_worlds()
    {
        var instance = Instance();

        var path = WorldSaveLayout.StagedSandboxPath(instance);

        Assert.Equal(
            Path.Combine(
                _root,
                "AbioticFactor",
                "Saved",
                "Config",
                "FacilityOverseer",
                instance.Id,
                DefaultSandboxSettings.FileName),
            path);
        Assert.DoesNotContain(
            Path.Combine("SaveGames", "Server", "Worlds"),
            path);
    }

    [Fact]
    public void Expected_world_folder_does_not_create_folder()
    {
        var instance = Instance();

        var path = WorldSaveLayout.ExpectedWorldFolder(instance);

        Assert.EndsWith(Path.Combine("Worlds", "Cascade"), path);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Sandbox_only_world_folder_is_orphan_not_real_save()
    {
        var instance = Instance();
        var folder = WorldSaveLayout.ExpectedWorldFolder(instance);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, DefaultSandboxSettings.FileName),
            "[SandboxSettings]\nGameDifficulty=1\n");

        Assert.True(WorldSaveLayout.IsOrphanConfigOnlyWorldFolder(folder));
        Assert.False(WorldSaveLayout.IsRealWorldSaveFolder(folder));
    }

    [Fact]
    public void Sav_file_marks_world_folder_as_real_save()
    {
        var instance = Instance();
        var folder = WorldSaveLayout.ExpectedWorldFolder(instance);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "WorldSave_MetaData.sav"), "");

        Assert.True(WorldSaveLayout.IsRealWorldSaveFolder(folder));
        Assert.False(WorldSaveLayout.IsOrphanConfigOnlyWorldFolder(folder));
    }

    [Fact]
    public void Player_data_folder_marks_world_folder_as_real_save()
    {
        var instance = Instance();
        var folder = WorldSaveLayout.ExpectedWorldFolder(instance);
        Directory.CreateDirectory(Path.Combine(folder, "PlayerData"));

        Assert.True(WorldSaveLayout.IsRealWorldSaveFolder(folder));
        Assert.False(WorldSaveLayout.IsOrphanConfigOnlyWorldFolder(folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServerInstance Instance() => new()
    {
        Id = "abc123",
        DisplayName = "Cascade",
        WorldSaveName = "Cascade",
        InstallPath = _root,
    };
}
