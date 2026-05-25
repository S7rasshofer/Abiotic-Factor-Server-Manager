namespace AbioticServerManager.Core.Runtime;

public enum PlayerRosterEventKind
{
    ConnectionAccepted,
    LoginRequested,
    JoinSucceeded,
    EnteredFacility,
    PlayerCountChanged,
    Disconnected,
    ServerStopped,

    /// <summary>An in-game chat message (carries <see cref="PlayerRosterEvent.Message"/>).</summary>
    Chat,
}

/// <summary>
/// One parsed signal from the dedicated server log. Fields are nullable because
/// different log lines carry different facts (a PlayerCount line has no name; a
/// connection-accepted line has only an address).
/// </summary>
public sealed record PlayerRosterEvent(
    DateTimeOffset Timestamp,
    PlayerRosterEventKind Kind,
    string? DisplayName,
    string? PrimaryId,
    string? SteamId64,
    string? Platform,
    string? RemoteAddress,
    int? PlayerCount,
    string RawLine)
{
    /// <summary>Chat message body - populated only for <see cref="PlayerRosterEventKind.Chat"/>.</summary>
    public string? Message { get; init; }

    public string KindText => Kind switch
    {
        PlayerRosterEventKind.ConnectionAccepted => "connection accepted",
        PlayerRosterEventKind.LoginRequested => "login requested",
        PlayerRosterEventKind.JoinSucceeded => "joined",
        PlayerRosterEventKind.EnteredFacility => "entered the facility",
        PlayerRosterEventKind.PlayerCountChanged => "player count changed",
        PlayerRosterEventKind.Disconnected => "disconnected",
        PlayerRosterEventKind.ServerStopped => "server stopped",
        PlayerRosterEventKind.Chat => "chat",
        _ => Kind.ToString(),
    };
}

/// <summary>
/// A durable per-player record. Mutable POCO so the persistence layer and the
/// MVVM layer can each own their concerns (mirrors <see cref="Models.ServerInstance"/>).
/// </summary>
public sealed class PlayerRosterEntry
{
    /// <summary>Stable identity key: SteamID64 &gt; connect id &gt; lowercased name.</summary>
    public string Key { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string? PrimaryId { get; set; }
    public string? SteamId64 { get; set; }
    public string? Platform { get; set; }

    /// <summary>Last remote endpoint - diagnostic only, never identity.</summary>
    public string? RemoteAddress { get; set; }

    public bool IsOnline { get; set; }
    public DateTimeOffset? CurrentSessionStartedAt { get; set; }
    public DateTimeOffset? FirstSeenAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string LastActivity { get; set; } = "";
    public int TotalSessions { get; set; }

    public string StatusText => IsOnline ? "online" : "offline";

    public string IdText =>
        !string.IsNullOrWhiteSpace(SteamId64) ? SteamId64! :
        !string.IsNullOrWhiteSpace(PrimaryId) ? PrimaryId! :
        "(name only)";

    /// <summary>
    /// User-friendly platform string for the roster column. The raw
    /// <see cref="Platform"/> token captured from the log is usually the
    /// generic "EOSPlus" wrapper that Abiotic Factor's EOS Plus integration
    /// reports - not useful for distinguishing PC vs console at a glance.
    /// This derived view leans on what we DO know:
    /// <list type="bullet">
    /// <item>SteamID64 (starts with the well-known "7656" Steam prefix) =&gt; PC (Steam).</item>
    /// <item>Recognisable PSN / Xbox / Epic tokens =&gt; that platform.</item>
    /// <item>No SteamID64 but a captured EOS connect id =&gt; Console (best guess - the player is not on Steam).</item>
    /// <item>Nothing useful =&gt; "-".</item>
    /// </list>
    /// Heuristic only - moderation operations still go through the captured id.
    /// </summary>
    public string PlatformDisplay
    {
        get
        {
            var hasSteamId = !string.IsNullOrWhiteSpace(SteamId64)
                && SteamId64!.StartsWith("7656", StringComparison.Ordinal);
            if (hasSteamId)
            {
                return "PC (Steam)";
            }

            var token = Platform?.Trim() ?? string.Empty;
            if (token.Length > 0)
            {
                if (token.Contains("psn", StringComparison.OrdinalIgnoreCase)
                    || token.Contains("playstation", StringComparison.OrdinalIgnoreCase))
                {
                    return "PlayStation";
                }
                if (token.Contains("xbl", StringComparison.OrdinalIgnoreCase)
                    || token.Contains("xbox", StringComparison.OrdinalIgnoreCase))
                {
                    return "Xbox";
                }
                if (token.Contains("epic", StringComparison.OrdinalIgnoreCase))
                {
                    return "PC (Epic)";
                }
                if (token.Contains("steam", StringComparison.OrdinalIgnoreCase))
                {
                    return "PC (Steam)";
                }
                // Generic EOS Plus wrapper - fall through to id-based heuristic.
                if (!token.Equals("EOSPlus", StringComparison.OrdinalIgnoreCase)
                    && !token.Equals("EOS", StringComparison.OrdinalIgnoreCase))
                {
                    return token;
                }
            }

            // No SteamID64 captured. If we have an EOS connect id at all,
            // the player isn't on Steam, so "Console" is the honest
            // best guess (PSN / Xbox / mobile - we cannot tell which from
            // the raw EOS Plus envelope alone).
            return !string.IsNullOrWhiteSpace(PrimaryId) ? "Console" : "-";
        }
    }

    public string CurrentSessionText
    {
        get
        {
            if (!IsOnline || CurrentSessionStartedAt is not { } start)
            {
                return "-";
            }

            var d = DateTimeOffset.Now - start;
            if (d < TimeSpan.Zero)
            {
                d = TimeSpan.Zero;
            }

            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}h {d.Minutes}m"
                : d.TotalMinutes >= 1
                    ? $"{d.Minutes}m {d.Seconds}s"
                    : $"{d.Seconds}s";
        }
    }

    public string LastSeenText => LastSeenAt is { } t
        ? t.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "-";

    public PlayerRosterEntry Clone() => (PlayerRosterEntry)MemberwiseClone();
}
