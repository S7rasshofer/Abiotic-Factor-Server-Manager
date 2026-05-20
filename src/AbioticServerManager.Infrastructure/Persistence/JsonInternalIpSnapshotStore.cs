using System.Text.Json;
using AbioticServerManager.Core.Networking;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Persists <see cref="InternalIpSnapshot"/> to
/// <c>&lt;DataRoot&gt;/config/last-internal-ip.json</c>. Write is atomic
/// (temp + replace); a corrupt or missing file simply yields a null snapshot
/// so the next launch is treated as FirstRun rather than failing.
/// </summary>
public sealed class JsonInternalIpSnapshotStore : IInternalIpSnapshotStore
{
    private const string FileName = "last-internal-ip.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonInternalIpSnapshotStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonInternalIpSnapshotStore(
        IAppPaths paths,
        ILogger<JsonInternalIpSnapshotStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private string FilePath => Path.Combine(_paths.ConfigDirectory, FileName);

    public async Task<InternalIpSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = FilePath;
            if (!File.Exists(file))
            {
                return null;
            }

            await using var stream = File.OpenRead(file);
            return await JsonSerializer
                .DeserializeAsync<InternalIpSnapshot>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "last-internal-ip.json could not be read; treating as first run");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(InternalIpSnapshot snapshot, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var file = FilePath;
            var temp = file + ".tmp";

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, snapshot, SerializerOptions, ct)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort. A failed save just means next launch treats the
            // current state as FirstRun. We never lose user data here.
            _logger.LogWarning(ex, "last-internal-ip.json could not be written");
        }
        finally
        {
            _gate.Release();
        }
    }
}
