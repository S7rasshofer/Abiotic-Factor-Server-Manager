namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Maintains a durable player roster from parsed log events. Identity is stable
/// (SteamID64 &gt; connect id &gt; name) so a reconnect updates the same player and
/// known players survive an app restart via <see cref="ExportDurable"/> /
/// <see cref="SeedKnown"/>.
/// </summary>
public sealed class PlayerRosterTracker
{
    private readonly Dictionary<string, PlayerRosterEntry> _byKey = new(StringComparer.Ordinal);
    private readonly List<PlayerRosterEvent> _history = [];
    private readonly int _historyLimit;
    private string? _pendingRemoteAddress;

    public PlayerRosterTracker(int historyLimit = 500) =>
        _historyLimit = Math.Max(1, historyLimit);

    public int? ServerPlayerCount { get; private set; }

    /// <summary>Set when the server-reported count disagrees with the online roster.</summary>
    public string? CountWarning { get; private set; }

    public bool HasSeenActivity => _history.Count > 0 || _byKey.Count > 0;

    public IReadOnlyList<PlayerRosterEvent> History => _history;

    public int OnlineCount => _byKey.Values.Count(e => e.IsOnline);

    /// <summary>Roster snapshot, online first then most-recently-seen.</summary>
    public IReadOnlyList<PlayerRosterEntry> Entries =>
        [.. _byKey.Values
            .OrderByDescending(e => e.IsOnline)
            .ThenByDescending(e => e.LastSeenAt ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Clone())];

    /// <summary>Loads previously known players as offline (call before live events).</summary>
    public void SeedKnown(IEnumerable<PlayerRosterEntry> known)
    {
        foreach (var k in known)
        {
            var entry = k.Clone();
            entry.IsOnline = false;
            entry.CurrentSessionStartedAt = null;
            if (string.IsNullOrEmpty(entry.Key))
            {
                entry.Key = KeyFor(entry.SteamId64, entry.PrimaryId, entry.DisplayName);
            }

            _byKey[entry.Key] = entry;
        }
    }

    /// <summary>Durable, non-volatile facts for persistence.</summary>
    public IReadOnlyList<PlayerRosterEntry> ExportDurable() =>
        [.. _byKey.Values.Select(e =>
        {
            var c = e.Clone();
            c.IsOnline = false;
            c.CurrentSessionStartedAt = null;
            return c;
        })];

    public PlayerRosterEvent? Apply(ServerLogLine line)
    {
        var evt = PlayerRosterParser.TryParse(line);
        if (evt is null)
        {
            return null;
        }

        switch (evt.Kind)
        {
            case PlayerRosterEventKind.ConnectionAccepted:
                _pendingRemoteAddress = evt.RemoteAddress;
                break;

            case PlayerRosterEventKind.LoginRequested:
                ApplyLogin(evt);
                break;

            case PlayerRosterEventKind.JoinSucceeded:
                MarkOnline(evt, "joined");
                break;

            case PlayerRosterEventKind.EnteredFacility:
                MarkOnline(evt, "entered the facility");
                break;

            case PlayerRosterEventKind.PlayerCountChanged:
                ServerPlayerCount = evt.PlayerCount;
                RecomputeCountWarning();
                break;

            case PlayerRosterEventKind.Disconnected:
                MarkOffline(evt);
                break;

            case PlayerRosterEventKind.ServerStopped:
                foreach (var e in _byKey.Values.Where(e => e.IsOnline))
                {
                    e.IsOnline = false;
                    e.CurrentSessionStartedAt = null;
                    e.LastSeenAt = evt.Timestamp;
                    e.LastActivity = "server stopped";
                }

                ServerPlayerCount = 0;
                CountWarning = null;
                break;
        }

        _history.Insert(0, evt);
        if (_history.Count > _historyLimit)
        {
            _history.RemoveRange(_historyLimit, _history.Count - _historyLimit);
        }

        return evt;
    }

    private void ApplyLogin(PlayerRosterEvent evt)
    {
        var entry = Resolve(evt.SteamId64, evt.PrimaryId, evt.DisplayName, create: true)!;
        if (!string.IsNullOrWhiteSpace(evt.DisplayName))
        {
            entry.DisplayName = evt.DisplayName!;
        }

        entry.SteamId64 ??= evt.SteamId64;
        entry.PrimaryId ??= evt.PrimaryId;
        entry.Platform = evt.Platform ?? entry.Platform;
        entry.RemoteAddress = _pendingRemoteAddress ?? entry.RemoteAddress;
        entry.FirstSeenAt ??= evt.Timestamp;
        entry.LastSeenAt = evt.Timestamp;
        entry.LastActivity = "login requested";
        _pendingRemoteAddress = null;
        Rekey(entry);
    }

    private void MarkOnline(PlayerRosterEvent evt, string activity)
    {
        var entry = Resolve(evt.SteamId64, evt.PrimaryId, evt.DisplayName, create: true)!;
        if (!string.IsNullOrWhiteSpace(evt.DisplayName))
        {
            entry.DisplayName = evt.DisplayName!;
        }

        if (!entry.IsOnline)
        {
            entry.IsOnline = true;
            entry.CurrentSessionStartedAt = evt.Timestamp;
            entry.TotalSessions++;
        }

        entry.FirstSeenAt ??= evt.Timestamp;
        entry.LastSeenAt = evt.Timestamp;
        entry.LastActivity = activity;
        RecomputeCountWarning();
    }

    private void MarkOffline(PlayerRosterEvent evt)
    {
        // A real AF disconnect line has no name - only the connect-id hex (carried
        // in PrimaryId). Match the player whose ConnectID contains that hex.
        var entry = evt.PrimaryId is { Length: > 0 } hex
            ? _byKey.Values.FirstOrDefault(e =>
                  !string.IsNullOrEmpty(e.PrimaryId) &&
                  e.PrimaryId!.Contains(hex, StringComparison.OrdinalIgnoreCase))
            : null;

        entry ??= Resolve(evt.SteamId64, evt.PrimaryId, evt.DisplayName, create: false);
        if (entry is null)
        {
            return;
        }

        entry.IsOnline = false;
        entry.CurrentSessionStartedAt = null;
        entry.LastSeenAt = evt.Timestamp;
        entry.LastActivity = "disconnected";
        RecomputeCountWarning();
    }

    private PlayerRosterEntry? Resolve(
        string? steam, string? primaryId, string? name, bool create)
    {
        var key = KeyFor(steam, primaryId, name);

        if (_byKey.TryGetValue(key, out var direct))
        {
            return direct;
        }

        // Name-only events (Join/Entered) must find an entry an earlier login
        // created under a steam/connect-id key.
        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = _byKey.Values.FirstOrDefault(e =>
                string.Equals(e.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (!create)
        {
            return null;
        }

        var entry = new PlayerRosterEntry
        {
            Key = key,
            DisplayName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name!,
            SteamId64 = steam,
            PrimaryId = primaryId,
        };
        _byKey[key] = entry;
        return entry;
    }

    // If a name-only entry later gains a stable id, move it to the stable key.
    private void Rekey(PlayerRosterEntry entry)
    {
        var newKey = KeyFor(entry.SteamId64, entry.PrimaryId, entry.DisplayName);
        if (string.Equals(newKey, entry.Key, StringComparison.Ordinal))
        {
            return;
        }

        _byKey.Remove(entry.Key);
        entry.Key = newKey;
        _byKey[newKey] = entry;
    }

    private static string KeyFor(string? steam, string? primaryId, string? name)
    {
        if (!string.IsNullOrWhiteSpace(steam))
        {
            return "steam:" + steam;
        }

        if (!string.IsNullOrWhiteSpace(primaryId))
        {
            return "cid:" + primaryId;
        }

        return "name:" + (name ?? "").Trim().ToLowerInvariant();
    }

    private void RecomputeCountWarning() =>
        CountWarning = ServerPlayerCount is { } c && c != OnlineCount
            ? $"Server reports {c} player(s) but the roster shows {OnlineCount} online."
            : null;
}
