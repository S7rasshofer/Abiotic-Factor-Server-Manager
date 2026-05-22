using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class PlayerDetailBuilderTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-20T12:00:00Z");

    private static ServerLogLine Log(string text, int offsetSeconds = 0) =>
        new("world", T0.AddSeconds(offsetSeconds), text, IsError: false);

    // ---- Chat parsing ----

    [Fact]
    public void Parses_a_chat_message()
    {
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  S7razzy: gg everyone"));

        Assert.NotNull(e);
        Assert.Equal(PlayerRosterEventKind.Chat, e!.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
        Assert.Equal("gg everyone", e.Message);
    }

    [Fact]
    public void Entered_facility_line_is_not_misread_as_chat()
    {
        // The system "has entered the facility" line has no colon after the
        // name, so it must remain an EnteredFacility event, not Chat.
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  S7razzy has entered the facility."));
        Assert.Equal(PlayerRosterEventKind.EnteredFacility, e!.Kind);
    }

    [Fact]
    public void Chat_message_preserves_colons_in_the_body()
    {
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  Bob: meet me at 3:30 by the lab"));
        Assert.Equal(PlayerRosterEventKind.Chat, e!.Kind);
        Assert.Equal("Bob", e.DisplayName);
        Assert.Equal("meet me at 3:30 by the lab", e.Message);
    }

    [Fact]
    public void Chat_goes_to_its_own_list_not_roster_history()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("Join succeeded: S7razzy", 0));
        t.Apply(Log("CHAT LOG:  S7razzy: hello", 1));

        // Chat is tracked separately so it never evicts lifecycle events.
        Assert.Single(t.Chat);
        Assert.DoesNotContain(t.History, e => e.Kind == PlayerRosterEventKind.Chat);
    }

    // ---- PlayerDetailBuilder ----

    [Fact]
    public void Build_filters_activity_to_the_named_player()
    {
        var history = new[]
        {
            new PlayerRosterEvent(T0, PlayerRosterEventKind.JoinSucceeded, "S7razzy",
                null, null, null, null, null, "raw"),
            new PlayerRosterEvent(T0, PlayerRosterEventKind.JoinSucceeded, "OtherGuy",
                null, null, null, null, null, "raw"),
            new PlayerRosterEvent(T0, PlayerRosterEventKind.Disconnected, "S7razzy",
                null, null, null, null, null, "raw"),
        };

        var report = PlayerDetailBuilder.Build("S7razzy", history, []);

        Assert.Equal(2, report.Activity.Count);
        Assert.All(report.Activity, e =>
            Assert.Equal("S7razzy", e.DisplayName));
        Assert.True(report.HasActivity);
        Assert.False(report.HasChat);
    }

    [Fact]
    public void Build_shows_whole_conversation_and_flags_the_players_lines()
    {
        var chat = new[]
        {
            new PlayerRosterEvent(T0.AddSeconds(2), PlayerRosterEventKind.Chat, "OtherGuy",
                null, null, null, null, null, "raw") { Message = "hi" },
            new PlayerRosterEvent(T0.AddSeconds(1), PlayerRosterEventKind.Chat, "S7razzy",
                null, null, null, null, null, "raw") { Message = "hello" },
        };

        var report = PlayerDetailBuilder.Build("S7razzy", [], chat);

        // Whole conversation, oldest first.
        Assert.Equal(2, report.Chat.Count);
        Assert.Equal("hello", report.Chat[0].Message);
        Assert.Equal("hi", report.Chat[1].Message);
        // Only S7razzy's line is flagged as theirs.
        Assert.True(report.Chat[0].IsFromPlayer);
        Assert.False(report.Chat[1].IsFromPlayer);
    }

    [Fact]
    public void Build_name_match_is_case_insensitive()
    {
        var chat = new[]
        {
            new PlayerRosterEvent(T0, PlayerRosterEventKind.Chat, "S7Razzy",
                null, null, null, null, null, "raw") { Message = "yo" },
        };

        var report = PlayerDetailBuilder.Build("s7razzy", [], chat);
        Assert.True(report.Chat[0].IsFromPlayer);
    }

    [Fact]
    public void Build_with_no_data_yields_empty_report()
    {
        var report = PlayerDetailBuilder.Build("Nobody", [], []);
        Assert.False(report.HasActivity);
        Assert.False(report.HasChat);
        Assert.Empty(report.Activity);
        Assert.Empty(report.Chat);
    }
}
