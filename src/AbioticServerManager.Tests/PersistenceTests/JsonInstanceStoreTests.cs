using AbioticServerManager.Core.Models;
using AbioticServerManager.Infrastructure.FileSystem;
using AbioticServerManager.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.PersistenceTests;

public class JsonInstanceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly JsonInstanceStore _store;
    private readonly AppPaths _paths;

    public JsonInstanceStoreTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new JsonInstanceStore(_paths, NullLogger<JsonInstanceStore>.Instance);
    }

    [Fact]
    public async Task Missing_file_returns_empty_list()
    {
        var result = await _store.LoadAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task Round_trips_instances()
    {
        var instances = new List<ServerInstance>
        {
            new() { DisplayName = "Cascade", GamePort = 7777 },
            new() { DisplayName = "Lab Rats", GamePort = 7779 },
        };

        await _store.SaveAsync(instances);
        var loaded = await _store.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Cascade", loaded[0].DisplayName);
        Assert.Equal(7779, loaded[1].GamePort);
    }

    [Fact]
    public async Task Corrupt_file_is_quarantined_and_returns_empty()
    {
        await File.WriteAllTextAsync(_paths.InstancesFile, "{ this is not valid json ]");

        var result = await _store.LoadAsync();

        Assert.Empty(result);
        Assert.False(File.Exists(_paths.InstancesFile));
        Assert.NotEmpty(Directory.GetFiles(
            Path.GetDirectoryName(_paths.InstancesFile)!,
            "instances.json.corrupt-*"));
    }

    [Fact]
    public async Task Legacy_platform_limited_field_is_ignored()
    {
        await File.WriteAllTextAsync(
            _paths.InstancesFile,
            """
            [
              {
                "displayName": "Legacy",
                "steamServerName": "Legacy",
                "worldSaveName": "Legacy",
                "platformLimited": true
              }
            ]
            """);

        var loaded = await _store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal(PlatformAccessMode.All, loaded[0].PlatformAccessMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
