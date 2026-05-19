using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Persists world profiles to <c>instances.json</c>. Writes are atomic (temp file then
/// replace) and a corrupt file is quarantined rather than deleted so the user can recover.
/// </summary>
public sealed class JsonInstanceStore : IInstanceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonInstanceStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonInstanceStore(IAppPaths paths, ILogger<JsonInstanceStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServerInstance>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = _paths.InstancesFile;
            if (!File.Exists(file))
            {
                return [];
            }

            await using var stream = File.OpenRead(file);
            var instances = await JsonSerializer
                .DeserializeAsync<List<ServerInstance>>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);
            return instances ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            QuarantineCorruptFile(ex);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<ServerInstance> instances, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var file = _paths.InstancesFile;
            var temp = file + ".tmp";

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, instances, SerializerOptions, ct)
                    .ConfigureAwait(false);
            }

            if (File.Exists(file))
            {
                File.Replace(temp, file, null);
            }
            else
            {
                File.Move(temp, file);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void QuarantineCorruptFile(Exception ex)
    {
        try
        {
            var file = _paths.InstancesFile;
            if (File.Exists(file))
            {
                var quarantine = $"{file}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                File.Move(file, quarantine);
                _logger.LogError(
                    ex,
                    "instances.json could not be read and was moved to {Quarantine}",
                    quarantine);
            }
        }
        catch (Exception moveEx)
        {
            _logger.LogError(moveEx, "Failed to quarantine corrupt instances.json");
        }
    }
}
