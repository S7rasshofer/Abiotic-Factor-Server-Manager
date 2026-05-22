using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Core.Admin;

/// <summary>
/// Source of a banned entry — surfaced on the Banished page so a user can tell
/// "I banned them from the app" from "this id was already in the file".
/// </summary>
public enum BanSource
{
    /// <summary>Banned via the app's Ban command (or otherwise tracked by the app).</summary>
    Manual,

    /// <summary>The id was in [BannedPlayers] when the app opened the file.</summary>
    RawIni,
}

/// <summary>
/// One row on the §3.2 Banished page. Pure record — IO sits in the VM that
/// composes the sectioned Admin.ini reader with the (already-IO) roster store.
/// </summary>
public sealed record BannedPlayerRow(
    string Id,
    string DisplayName,
    DateTimeOffset? DateBanned,
    BanSource Source,
    string Notes)
{
    public string DateBannedText => DateBanned is { } d
        ? d.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : string.Empty;

    public string SourceText => Source switch
    {
        BanSource.Manual => "manual",
        BanSource.RawIni => "raw INI",
        _ => Source.ToString(),
    };
}

/// <summary>
/// Pure presentation helpers for §3.2 (banned vs active roster) and §3.3
/// (admin marker derivation). No IO — the App layer feeds in already-loaded
/// roster entries, the [BannedPlayers] id list, and the [Moderators] id list.
/// </summary>
public static class RosterPresentation
{
    /// <summary>
    /// Returns the roster entries with banned ids filtered out. Match is by
    /// SteamID64 (banned ids only ever live there for real moderation), so a
    /// name-only roster row never gets accidentally hidden.
    /// </summary>
    public static IReadOnlyList<PlayerRosterEntry> FilterActive(
        IReadOnlyList<PlayerRosterEntry> roster,
        IReadOnlyCollection<string> bannedSteamIds)
    {
        if (bannedSteamIds.Count == 0)
        {
            return roster;
        }

        var lookup = new HashSet<string>(bannedSteamIds, StringComparer.OrdinalIgnoreCase);
        return roster
            .Where(r => string.IsNullOrWhiteSpace(r.SteamId64) || !lookup.Contains(r.SteamId64!))
            .ToList();
    }

    /// <summary>
    /// Builds the rows shown on §3.2. Last-known display name is filled in
    /// from the roster (by SteamID64) when available; date banned is unknown
    /// (best-effort placeholder for now); source is RawIni for every id read
    /// straight out of the sectioned file.
    /// </summary>
    public static IReadOnlyList<BannedPlayerRow> BuildBannedRows(
        IReadOnlyCollection<string> bannedSteamIds,
        IReadOnlyList<PlayerRosterEntry> roster)
    {
        if (bannedSteamIds.Count == 0)
        {
            return [];
        }

        var nameLookup = roster
            .Where(r => !string.IsNullOrWhiteSpace(r.SteamId64))
            .GroupBy(r => r.SteamId64!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.LastSeenAt ?? DateTimeOffset.MinValue).First().DisplayName,
                StringComparer.OrdinalIgnoreCase);

        return bannedSteamIds
            .Select(id =>
            {
                var name = nameLookup.TryGetValue(id, out var n) ? (n ?? string.Empty) : string.Empty;
                return new BannedPlayerRow(
                    Id: id,
                    DisplayName: name,
                    DateBanned: null,
                    Source: BanSource.RawIni,
                    Notes: string.Empty);
            })
            .ToList();
    }

    /// <summary>
    /// True when this roster row's captured SteamID64 matches one of the
    /// moderator ids. Decoration only — moderation operations still use the
    /// underlying id.
    /// </summary>
    public static bool IsAdmin(PlayerRosterEntry entry, IReadOnlyCollection<string> moderatorSteamIds)
    {
        if (moderatorSteamIds.Count == 0 || string.IsNullOrWhiteSpace(entry.SteamId64))
        {
            return false;
        }

        return moderatorSteamIds.Contains(entry.SteamId64!, StringComparer.OrdinalIgnoreCase);
    }
}
