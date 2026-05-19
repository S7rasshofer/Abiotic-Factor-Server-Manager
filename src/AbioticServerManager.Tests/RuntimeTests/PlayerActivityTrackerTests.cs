using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class PlayerActivityTrackerTests
{
    [Fact]
    public void Tracks_join_and_leave_events()
    {
        var tracker = new PlayerActivityTracker();

        tracker.Apply(Log("Player \"Alice\" joined"));
        tracker.Apply(Log("Alice left the server"));

        Assert.Empty(tracker.ActivePlayers);
        Assert.Equal(2, tracker.History.Count);
        Assert.Equal(PlayerActivityKind.Left, tracker.History[0].Kind);
    }

    [Fact]
    public void Duplicate_join_does_not_duplicate_active_player()
    {
        var tracker = new PlayerActivityTracker();

        tracker.Apply(Log("Player Bob joined"));
        tracker.Apply(Log("Bob connected"));

        Assert.Single(tracker.ActivePlayers);
        Assert.Equal("Bob", tracker.ActivePlayers[0]);
    }

    [Fact]
    public void Unknown_line_is_ignored()
    {
        var tracker = new PlayerActivityTracker();

        var result = tracker.Apply(Log("LogOnline: server tick complete"));

        Assert.Null(result);
        Assert.Empty(tracker.ActivePlayers);
        Assert.Empty(tracker.History);
    }

    [Fact]
    public void Pairs_join_and_leave_into_a_session_with_duration()
    {
        var tracker = new PlayerActivityTracker();
        var start = DateTimeOffset.Parse("2026-05-18T12:00:00Z");

        tracker.Apply(new ServerLogLine("w", start, "Player \"Alice\" joined", false));

        Assert.Single(tracker.Sessions);
        Assert.True(tracker.Sessions[0].IsActive);

        tracker.Apply(new ServerLogLine("w", start.AddMinutes(90), "Alice left the server", false));

        var session = Assert.Single(tracker.Sessions);
        Assert.False(session.IsActive);
        Assert.Equal(TimeSpan.FromMinutes(90), session.Duration);
        Assert.Equal("1h 30m", session.DurationText);
        Assert.Equal("ended", session.StatusText);
    }

    private static ServerLogLine Log(string text) =>
        new("world", DateTimeOffset.Parse("2026-05-18T12:00:00Z"), text, IsError: false);
}
