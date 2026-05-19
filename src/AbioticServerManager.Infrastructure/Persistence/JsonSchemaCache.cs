using System.Collections.Concurrent;
using System.Text.Json;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

public sealed class JsonSchemaCache : ISchemaCache
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonSchemaCache> _logger;
    private readonly ConcurrentDictionary<string, string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public JsonSchemaCache(IAppPaths paths, ILogger<JsonSchemaCache> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private static string Compose(string section, string key) => $"{section}.{key}";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var file = _paths.SchemaCacheFile;
            if (File.Exists(file))
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options);
                if (entries is not null)
                {
                    foreach (var (k, v) in entries)
                    {
                        _seen[k] = v;
                    }
                }
            }

            _loaded = true;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Schema cache could not be read; starting fresh");
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasSeen(string section, string key) => _seen.ContainsKey(Compose(section, key));

    public async Task RecordAsync(
        IEnumerable<(string Section, string Key)> keys,
        CancellationToken ct = default)
    {
        var changed = false;
        var stamp = DateTimeOffset.UtcNow.ToString("O");
        foreach (var (section, key) in keys)
        {
            if (_seen.TryAdd(Compose(section, key), stamp))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var temp = _paths.SchemaCacheFile + ".tmp";
            await File.WriteAllTextAsync(
                temp,
                JsonSerializer.Serialize(new SortedDictionary<string, string>(_seen), Options),
                ct).ConfigureAwait(false);

            if (File.Exists(_paths.SchemaCacheFile))
            {
                File.Replace(temp, _paths.SchemaCacheFile, null);
            }
            else
            {
                File.Move(temp, _paths.SchemaCacheFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist schema cache");
        }
        finally
        {
            _gate.Release();
        }
    }
}
