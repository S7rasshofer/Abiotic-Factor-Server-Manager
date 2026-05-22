namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// One chat line as shown on the player-detail tab. <see cref="IsFromPlayer"/>
/// flags the lines authored by the player the tab is focused on, so the UI can
/// emphasise their messages while still showing the surrounding conversation.
/// </summary>
public sealed record PlayerChatLine(
    DateTimeOffset Timestamp,
    string Name,
    string Message,
    bool IsFromPlayer)
{
    public string TimeText => Timestamp.LocalDateTime.ToString("HH:mm:ss");
}

/// <summary>The per-player detail snapshot: lifecycle activity + chat in context.</summary>
public sealed record PlayerDetailReport
{
    public required string DisplayName { get; init; }

    /// <summary>Roster lifecycle events naming this player, newest first.</summary>
    public required IReadOnlyList<PlayerRosterEvent> Activity { get; init; }

    /// <summary>
    /// The whole chat conversation (oldest first) so the player's lines read in
    /// context; <see cref="PlayerChatLine.IsFromPlayer"/> marks their own messages.
    /// </summary>
    public required IReadOnlyList<PlayerChatLine> Chat { get; init; }

    public bool HasChat => Chat.Count > 0;
    public bool HasActivity => Activity.Count > 0;
}

/// <summary>
/// Pure builder for the player-detail tab. Filters the roster history + chat
/// log for one player. Kept in Core so the filtering is unit-testable without
/// the UI.
/// </summary>
public static class PlayerDetailBuilder
{
    public static PlayerDetailReport Build(
        string displayName,
        IReadOnlyList<PlayerRosterEvent> history,
        IReadOnlyList<PlayerRosterEvent> chat)
    {
        // Lifecycle activity: roster events that name this player (newest first,
        // matching how History is stored).
        var activity = history
            .Where(e => NameMatches(e.DisplayName, displayName))
            .ToList();

        // Chat: show the entire conversation (oldest first for readability) so
        // the player's lines have context; flag the player's own messages.
        var chatLines = chat
            .OrderBy(c => c.Timestamp)
            .Select(c => new PlayerChatLine(
                c.Timestamp,
                c.DisplayName ?? "",
                c.Message ?? "",
                NameMatches(c.DisplayName, displayName)))
            .ToList();

        return new PlayerDetailReport
        {
            DisplayName = displayName,
            Activity = activity,
            Chat = chatLines,
        };
    }

    private static bool NameMatches(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) &&
        !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
