using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Per-world roster at <c>&lt;DataRoot&gt;/players/&lt;worldId&gt;/roster.json</c>.
/// Only durable, non-secret facts are written; volatile online/session state is
/// stripped by <see cref="PlayerRosterTracker.ExportDurable"/> before saving.
/// Writes are atomic and a corrupt file is ignored, never throws.
/// </summary>
public sealed class JsonPlayerRosterStore : IPlayerRosterStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonPlayerRosterStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPlayerRosterStore(IAppPaths paths, ILogger<JsonPlayerRosterStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlayerRosterEntry>> LoadAsync(
        string worldId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(worldId))
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = RosterFile(worldId);
            if (!File.Exists(file))
            {
                return [];
            }

            await using var stream = File.OpenRead(file);
            var entries = await JsonSerializer
                .DeserializeAsync<List<PlayerRosterEntry>>(stream, Options, ct)
                .ConfigureAwait(false);
            return entries ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read roster for world {World}", worldId);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string worldId,
        IReadOnlyList<PlayerRosterEntry> entries,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(worldId))
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = RosterFile(worldId);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, entries, Options, ct)
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
            _logger.LogWarning(ex, "Could not save roster for world {World}", worldId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string RosterFile(string worldId)
    {
        var safe = new string([.. worldId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);
        if (safe.Length == 0)
        {
            safe = "default";
        }

        return Path.Combine(_paths.PlayersDirectory, safe, "roster.json");
    }
}
