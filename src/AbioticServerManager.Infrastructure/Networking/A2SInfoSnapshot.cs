namespace AbioticServerManager.Infrastructure.Networking;

/// <summary>
/// Parsed fields from a Source/Steam A2S_INFO reply. Only the fields we use
/// to corroborate the live roster are surfaced; the rest of the payload
/// (map, folder, game, environment, VAC, version, EDF extras) is parsed
/// past but discarded.
/// </summary>
public sealed record A2SInfoSnapshot(
    string ServerName,
    int PlayerCount,
    int MaxPlayers,
    DateTimeOffset QueriedAt);
