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
    private readonly List<PlayerRosterEvent> _chat = [];
    private readonly int _historyLimit;
    private string? _pendingRemoteAddress;

    // Number of consecutive A2S polls that reported fewer players than the
    // roster believed were online. Eviction is gated behind a value of >= 2
    // so a single dropped UDP packet (or a poll racing a connection accept)
    // cannot flicker a real player offline.
    private int _consecutiveLowMismatchCount;

    public PlayerRosterTracker(int historyLimit = 500) =>
        _historyLimit = Math.Max(1, historyLimit);

    public int? ServerPlayerCount { get; private set; }

    /// <summary>Set when the server-reported count disagrees with the online roster.</summary>
    public string? CountWarning { get; private set; }

    public bool HasSeenActivity => _history.Count > 0 || _byKey.Count > 0;

    public IReadOnlyList<PlayerRosterEvent> History => _history;

    /// <summary>In-game chat messages, newest first. Kept separate from
    /// <see cref="History"/> so chat volume never evicts roster lifecycle events.</summary>
    public IReadOnlyList<PlayerRosterEvent> Chat => _chat;

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
                // Authoritative corrective: the server reporting zero players
                // means nobody is online, full stop. This closes any session
                // whose disconnect line we failed to match - the "stuck online
                // for an hour" bug - without waiting for server shutdown.
                if (evt.PlayerCount == 0)
                {
                    foreach (var e in _byKey.Values.Where(e => e.IsOnline))
                    {
                        e.IsOnline = false;
                        e.CurrentSessionStartedAt = null;
                        e.LastSeenAt = evt.Timestamp;
                        e.LastActivity = "disconnected";
                    }
                }

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

            case PlayerRosterEventKind.Chat:
                ApplyChat(evt);
                return evt; // chat lives in its own list, not roster History
        }

        _history.Insert(0, evt);
        if (_history.Count > _historyLimit)
        {
            _history.RemoveRange(_historyLimit, _history.Count - _historyLimit);
        }

        return evt;
    }

    private void ApplyChat(PlayerRosterEvent evt)
    {
        _chat.Insert(0, evt);
        if (_chat.Count > _historyLimit)
        {
            _chat.RemoveRange(_historyLimit, _chat.Count - _historyLimit);
        }

        // A chat message proves the player is still around - refresh last-seen.
        if (!string.IsNullOrWhiteSpace(evt.DisplayName) &&
            Resolve(null, null, evt.DisplayName, create: false) is { } entry)
        {
            entry.LastSeenAt = evt.Timestamp;
            entry.LastActivity = "chatted";
        }
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

    /// <summary>
    /// Reconciles the roster's online set against an externally-observed live
    /// player count (e.g. an A2S query against the running server's query
    /// port). The corroborator is the ground truth for "how many clients are
    /// actually connected" - the log-line-driven roster can over-count when
    /// a disconnect signal is missed (player crash, ungraceful network drop,
    /// unmatched UNetConnection close hex).
    /// </summary>
    /// <remarks>
    /// Eviction is debounced: a single low reading is ignored (a transient
    /// UDP blip should not flicker a real player offline), two consecutive
    /// low readings trigger eviction of the oldest-<see cref="PlayerRosterEntry.LastSeenAt"/>
    /// entries until the roster matches the live count. A matching or higher
    /// reading resets the debounce counter.
    ///
    /// Returns the display names of any entries that were evicted - empty
    /// when nothing changed. The caller is responsible for republishing the
    /// roster snapshot (e.g. <c>RefreshRoster()</c>) when the returned list
    /// is non-empty.
    /// </remarks>
    /// <param name="liveCount">Player count reported by the corroborator. Negative values are ignored.</param>
    /// <param name="now">Timestamp used for the evicted entries' <see cref="PlayerRosterEntry.LastSeenAt"/>.</param>
    public IReadOnlyList<string> ReconcileWithLiveCount(int liveCount, DateTimeOffset now)
    {
        if (liveCount < 0)
        {
            return [];
        }

        ServerPlayerCount = liveCount;

        var onlineCount = OnlineCount;
        if (liveCount >= onlineCount)
        {
            // Roster matches or under-reports. Either nothing to do, or we
            // missed a join - never fabricate a row from a count alone.
            _consecutiveLowMismatchCount = 0;
            RecomputeCountWarning();
            return [];
        }

        // Live count is below the roster's online set - presumed missed
        // disconnect. Wait for a second confirming poll before evicting.
        _consecutiveLowMismatchCount++;
        if (_consecutiveLowMismatchCount < 2)
        {
            RecomputeCountWarning();
            return [];
        }

        var evictCount = onlineCount - liveCount;
        var victims = _byKey.Values
            .Where(e => e.IsOnline)
            .OrderBy(e => e.LastSeenAt ?? DateTimeOffset.MinValue)
            .Take(evictCount)
            .ToList();

        var names = new List<string>(victims.Count);
        foreach (var victim in victims)
        {
            victim.IsOnline = false;
            victim.CurrentSessionStartedAt = null;
            victim.LastSeenAt = now;
            victim.LastActivity = "disconnected (A2S reconcile)";
            names.Add(victim.DisplayName);
        }

        _consecutiveLowMismatchCount = 0;
        RecomputeCountWarning();
        return names;
    }
}
