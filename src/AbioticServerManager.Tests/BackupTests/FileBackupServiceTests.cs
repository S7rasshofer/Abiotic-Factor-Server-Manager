using AbioticServerManager.Core.Models;
using AbioticServerManager.Infrastructure.FileSystem;
using AbioticServerManager.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.BackupTests;

public class FileBackupServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fo-bkp-" + Guid.NewGuid().ToString("N"));

    private readonly FileBackupService _service;
    private readonly string _gameDir;

    public FileBackupServiceTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _service = new FileBackupService(paths, NullLogger<FileBackupService>.Instance);
        _gameDir = Path.Combine(_root, "game");
    }

    private ServerInstance NewInstanceWithWorld()
    {
        var worldPath = Path.Combine(_gameDir, "Worlds", "Cascade");
        Directory.CreateDirectory(worldPath);
        File.WriteAllText(Path.Combine(worldPath, "world.db"), "save-bytes");
        Directory.CreateDirectory(Path.Combine(worldPath, "players"));
        File.WriteAllText(Path.Combine(worldPath, "players", "p1.dat"), "player-one");

        var sandboxPath = Path.Combine(_gameDir, "SandboxSettings.ini");
        File.WriteAllText(sandboxPath, "[SandboxSettings]\r\nEnemySpawnRate=1.0\r\n");

        var adminPath = Path.Combine(_gameDir, "Admins.ini");
        File.WriteAllText(adminPath, "76561198000000000\r\n");

        return new ServerInstance
        {
            DisplayName = "Cascade",
            WorldPath = worldPath,
            SandboxIniPath = sandboxPath,
            AdminIniPath = adminPath,
        };
    }

    [Fact]
    public async Task Create_captures_world_config_instance_and_manifest()
    {
        var instance = NewInstanceWithWorld();

        var result = await _service.CreateBackupAsync(instance, "manual");

        Assert.True(result.Success);
        var entry = Assert.IsType<Core.Backup.BackupEntry>(result.Entry);
        Assert.Equal("manual", entry.Reason);
        Assert.True(entry.IncludedWorldSave);
        Assert.True(entry.IncludedSandboxIni);
        Assert.True(entry.IncludedAdminIni);
        Assert.True(entry.SizeBytes > 0);

        Assert.Equal(
            "save-bytes",
            File.ReadAllText(Path.Combine(entry.Path, "world", "world.db")));
        Assert.Equal(
            "player-one",
            File.ReadAllText(Path.Combine(entry.Path, "world", "players", "p1.dat")));
        Assert.True(File.Exists(Path.Combine(entry.Path, "SandboxSettings.ini")));
        Assert.True(File.Exists(Path.Combine(entry.Path, "Admins.ini")));
        Assert.True(File.Exists(Path.Combine(entry.Path, "instance.json")));
        Assert.True(File.Exists(Path.Combine(entry.Path, "manifest.json")));
    }

    [Fact]
    public async Task Backup_without_world_folder_still_succeeds_config_only()
    {
        var instance = new ServerInstance { DisplayName = "NoWorld" };

        var result = await _service.CreateBackupAsync(instance, "manual");

        Assert.True(result.Success);
        Assert.False(result.Entry!.IncludedWorldSave);
        Assert.False(result.Entry.IncludedSandboxIni);
    }

    [Fact]
    public async Task List_returns_backups_newest_first()
    {
        var instance = NewInstanceWithWorld();

        var first = await _service.CreateBackupAsync(instance, "before-update");
        await Task.Delay(1100); // distinct second-resolution timestamp ids
        var second = await _service.CreateBackupAsync(instance, "manual");

        var list = await _service.ListBackupsAsync(instance);

        Assert.Equal(2, list.Count);
        Assert.Equal(second.Entry!.Id, list[0].Id);
        Assert.Equal(first.Entry!.Id, list[1].Id);
        Assert.Equal("manual", list[0].Reason);
    }

    [Fact]
    public async Task Restore_round_trips_world_and_config_and_keeps_safety_backup()
    {
        var instance = NewInstanceWithWorld();
        var backup = (await _service.CreateBackupAsync(instance, "manual")).Entry!;

        // Mutate live files after the backup.
        File.WriteAllText(Path.Combine(instance.WorldPath, "world.db"), "corrupted");
        File.Delete(Path.Combine(instance.WorldPath, "players", "p1.dat"));
        File.WriteAllText(instance.SandboxIniPath, "[SandboxSettings]\r\nEnemySpawnRate=9.0\r\n");

        var result = await _service.RestoreBackupAsync(instance, backup);

        Assert.True(result.Success);
        Assert.Equal("save-bytes", File.ReadAllText(Path.Combine(instance.WorldPath, "world.db")));
        Assert.Equal(
            "player-one",
            File.ReadAllText(Path.Combine(instance.WorldPath, "players", "p1.dat")));
        Assert.Contains("EnemySpawnRate=1.0", File.ReadAllText(instance.SandboxIniPath));

        var list = await _service.ListBackupsAsync(instance);
        Assert.Contains(list, e => e.Reason == "pre-restore");
    }

    [Fact]
    public async Task Restore_missing_backup_folder_fails_gracefully()
    {
        var instance = NewInstanceWithWorld();
        var backup = (await _service.CreateBackupAsync(instance, "manual")).Entry!;
        Directory.Delete(backup.Path, recursive: true);

        var result = await _service.RestoreBackupAsync(instance, backup);

        Assert.False(result.Success);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
