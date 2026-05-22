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
    /// <summary>Chat message body — populated only for <see cref="PlayerRosterEventKind.Chat"/>.</summary>
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
