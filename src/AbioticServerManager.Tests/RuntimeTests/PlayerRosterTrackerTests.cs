using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class PlayerRosterTrackerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-19T03:17:00Z");

    private static ServerLogLine Log(string text, int offsetSeconds = 0) =>
        new("world", T0.AddSeconds(offsetSeconds), text, IsError: false);

    // ---- Parser: real Abiotic Factor lines from the observed TorchWood log ----

    [Fact]
    public void Parses_login_request_with_steamid_and_platform()
    {
        var e = PlayerRosterParser.TryParse(Log(
            "LogNet: Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_|abc " +
            "userId: 0002abc platform: EOSPlus"));

        Assert.NotNull(e);
        Assert.Equal(PlayerRosterEventKind.LoginRequested, e!.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
        Assert.Equal("76561198104903704", e.SteamId64);
        Assert.Equal("EOSPlus", e.Platform);
    }

    [Fact]
    public void Parses_join_succeeded()
    {
        var e = PlayerRosterParser.TryParse(Log("Join succeeded: S7razzy"));
        Assert.Equal(PlayerRosterEventKind.JoinSucceeded, e!.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
    }

    [Fact]
    public void Parses_entered_facility_chat_line()
    {
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  S7razzy has entered the facility."));
        Assert.Equal(PlayerRosterEventKind.EnteredFacility, e!.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
    }

    [Fact]
    public void Parses_player_count_attribute()
    {
        var e = PlayerRosterParser.TryParse(Log(
            "EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)"));
        Assert.Equal(PlayerRosterEventKind.PlayerCountChanged, e!.Kind);
        Assert.Equal(1, e.PlayerCount);
    }

    [Fact]
    public void Parses_accepted_connection_remote_address()
    {
        var e = PlayerRosterParser.TryParse(Log(
            "LogNet: NotifyAcceptedConnection: Server accepting post-challenge " +
            "connection from: 192.168.254.3:54007"));
        Assert.Equal(PlayerRosterEventKind.ConnectionAccepted, e!.Kind);
        Assert.Equal("192.168.254.3:54007", e.RemoteAddress);
    }

    [Fact]
    public void Generic_join_leave_still_parsed_as_fallback()
    {
        Assert.Equal(
            PlayerRosterEventKind.JoinSucceeded,
            PlayerRosterParser.TryParse(Log("Player \"Alice\" joined"))!.Kind);
        Assert.Equal(
            PlayerRosterEventKind.Disconnected,
            PlayerRosterParser.TryParse(Log("Alice left the server"))!.Kind);
    }

    // ---- Tracker behaviour ----

    [Fact]
    public void Full_torchwood_join_sequence_yields_one_online_player()
    {
        var t = new PlayerRosterTracker();

        t.Apply(Log("NotifyAcceptedConnection from: 192.168.254.3:54007", 0));
        t.Apply(Log("Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_| " +
                    "platform: EOSPlus", 1));
        t.Apply(Log("Join succeeded: S7razzy", 2));
        t.Apply(Log("CHAT LOG:  S7razzy has entered the facility.", 3));
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)", 4));

        var entry = Assert.Single(t.Entries);
        Assert.True(entry.IsOnline);
        Assert.Equal("S7razzy", entry.DisplayName);
        Assert.Equal("76561198104903704", entry.SteamId64);
        Assert.Equal("EOSPlus", entry.Platform);
        Assert.Equal("192.168.254.3:54007", entry.RemoteAddress);
        Assert.Equal(1, entry.TotalSessions);
        Assert.Equal(1, t.OnlineCount);
        Assert.Equal(1, t.ServerPlayerCount);
        Assert.Null(t.CountWarning);
    }

    [Fact]
    public void Entered_facility_does_not_open_a_second_session()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("Join succeeded: S7razzy", 0));
        t.Apply(Log("CHAT LOG:  S7razzy has entered the facility.", 1));

        Assert.Equal(1, t.Entries[0].TotalSessions);
    }

    [Fact]
    public void Player_count_mismatch_raises_warning()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (2)"));

        Assert.NotNull(t.CountWarning);
        Assert.Contains("2", t.CountWarning!);
    }

    [Fact]
    public void Server_stop_marks_everyone_offline()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("Join succeeded: S7razzy", 0));
        Assert.Equal(1, t.OnlineCount);

        t.Apply(Log("[server stopped]", 5));

        Assert.Equal(0, t.OnlineCount);
        Assert.False(t.Entries[0].IsOnline);
    }

    [Fact]
    public void Durable_export_then_seed_keeps_known_player_offline()
    {
        var t1 = new PlayerRosterTracker();
        t1.Apply(Log("Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_| " +
                     "platform: EOSPlus", 0));
        t1.Apply(Log("Join succeeded: S7razzy", 1));

        var durable = t1.ExportDurable();
        Assert.All(durable, e => Assert.False(e.IsOnline));

        var t2 = new PlayerRosterTracker();
        t2.SeedKnown(durable);

        var seeded = Assert.Single(t2.Entries);
        Assert.Equal("S7razzy", seeded.DisplayName);
        Assert.Equal("76561198104903704", seeded.SteamId64);
        Assert.False(seeded.IsOnline);

        // A reconnect updates the SAME player, not a duplicate.
        t2.Apply(Log("Join succeeded: S7razzy", 100));
        Assert.Single(t2.Entries);
        Assert.True(t2.Entries[0].IsOnline);
    }

    [Fact]
    public void Unknown_line_is_ignored()
    {
        var t = new PlayerRosterTracker();
        Assert.Null(t.Apply(Log("LogMemory: tick 1234 complete")));
        Assert.Empty(t.Entries);
    }

    // ---- Verbatim lines from the real TorchWood AbioticFactor.log ----

    private const string RealLogin =
        "[2026.05.19-19.37.04:771][118]LogNet: Login request: ?Name=S7razzy??" +
        "ConnectID=76561198104903704_+_|00024d0a26974b4cb944a8184dcd18fe userId: " +
        "EOSPlus:UNKNOWN [0x1100001089F0018]_+_|00024d0a26974b4cb944a8184dcd18fe " +
        "platform: EOSPlus";

    private const string RealAccepted =
        "[2026.05.19-19.37.04:726][117]LogNet: NotifyAcceptedConnection: Name: Facility, " +
        "TimeStamp: 05/19/26 15:37:04, [UNetConnection] RemoteAddr: 192.168.254.3:62418, " +
        "Name: IpConnection_2147481747, Driver: Name:GameNetDriver";

    private const string RealClose =
        "[2026.05.19-19.42.04:970][138]LogNet: UNetConnection::Close: [UNetConnection] " +
        "RemoteAddr: 192.168.254.3:62418, Name: IpConnection_2147481747, Driver: " +
        "Name:GameNetDriver, IsServer: YES, UniqueId: EOSPlus:UNKNOWN " +
        "[0x1100001089F0018]_+_|00024d0a26974b4cb944a8184dcd18fe, Channels: 116, Time: x";

    [Fact]
    public void Real_login_line_yields_steamid_platform_and_full_connectid()
    {
        var e = PlayerRosterParser.TryParse(Log(RealLogin))!;

        Assert.Equal(PlayerRosterEventKind.LoginRequested, e.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
        Assert.Equal("76561198104903704", e.SteamId64);
        Assert.Equal("EOSPlus", e.Platform);
        Assert.Contains("00024d0a26974b4cb944a8184dcd18fe", e.PrimaryId!);
    }

    [Fact]
    public void Real_non_steam_login_keeps_connectid_without_steamid()
    {
        var e = PlayerRosterParser.TryParse(Log(
            "LogNet: Login request: ?Name=ArkhamDante05??ConnectID=2535457262544294_+_|" +
            "00021a55baf7462cabc8262fca8b6249 userId: EOSPlus:INVALID[0x9] platform: EOSPlus"))!;

        Assert.Equal("ArkhamDante05", e.DisplayName);
        Assert.Null(e.SteamId64);
        Assert.Contains("00021a55baf7462cabc8262fca8b6249", e.PrimaryId!);
        Assert.Equal("EOSPlus", e.Platform);
    }

    [Fact]
    public void Real_accepted_connection_captures_remote_address()
    {
        var e = PlayerRosterParser.TryParse(Log(RealAccepted))!;
        Assert.Equal(PlayerRosterEventKind.ConnectionAccepted, e.Kind);
        Assert.Equal("192.168.254.3:62418", e.RemoteAddress);
    }

    [Fact]
    public void Real_close_line_marks_the_right_player_offline()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log(RealAccepted, 0));
        t.Apply(Log(RealLogin, 1));
        t.Apply(Log("LogNet: Join succeeded: S7razzy", 2));

        var entry = Assert.Single(t.Entries);
        Assert.True(entry.IsOnline);
        Assert.Equal("76561198104903704", entry.SteamId64);
        Assert.Equal("EOSPlus", entry.Platform);
        Assert.Equal("192.168.254.3:62418", entry.RemoteAddress);

        var dc = t.Apply(Log(RealClose, 300));
        Assert.Equal(PlayerRosterEventKind.Disconnected, dc!.Kind);
        Assert.False(t.Entries[0].IsOnline);
        Assert.Equal(0, t.OnlineCount);
    }

    [Fact]
    public void Close_for_unknown_player_is_a_safe_no_op()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Someone", 0));
        t.Apply(Log(RealClose, 1)); // hex not associated with anyone
        Assert.True(t.Entries[0].IsOnline);
    }

    // ---- PlayerCount=0 corrective (the "stuck online for an hour" bug) ----

    [Fact]
    public void Player_count_zero_offlines_everyone_even_without_a_disconnect_line()
    {
        // Scenario from the field: a player hopped on briefly, the disconnect
        // line was never matched, and they showed online until server stop.
        // The session-browser PlayerCount dropping to 0 must close the session.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: S7razzy", 0));
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)", 1));
        Assert.Equal(1, t.OnlineCount);

        // No UNetConnection::Close line arrives - only the count dropping.
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (0)", 30));

        Assert.Equal(0, t.OnlineCount);
        Assert.False(t.Entries[0].IsOnline);
        Assert.Null(t.Entries[0].CurrentSessionStartedAt);
        Assert.Null(t.CountWarning);
    }

    [Fact]
    public void Player_count_zero_corrects_a_missed_disconnect_hex_mismatch()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log(RealAccepted, 0));
        t.Apply(Log(RealLogin, 1));
        t.Apply(Log("LogNet: Join succeeded: S7razzy", 2));
        Assert.True(t.Entries[0].IsOnline);

        // A close line whose hex doesn't match - would leave the player stuck.
        t.Apply(Log("LogNet: UNetConnection::Close: ... _+_|deadbeefdeadbeef", 10));
        Assert.True(t.Entries[0].IsOnline); // still stuck at this point

        // The count going to 0 is the authoritative corrective.
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (0)", 11));
        Assert.False(t.Entries[0].IsOnline);
        Assert.Equal(0, t.OnlineCount);
    }

    [Fact]
    public void Player_count_zero_does_not_resurrect_or_duplicate_players()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: S7razzy", 0));
        t.Apply(Log("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (0)", 5));

        Assert.Single(t.Entries);
        Assert.False(t.Entries[0].IsOnline);
        Assert.Equal("disconnected", t.Entries[0].LastActivity);
    }

    // ---- "has left the facility" chat-style departure ----

    [Fact]
    public void Parses_left_facility_chat_line_as_disconnect()
    {
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  S7razzy has left the facility."));
        Assert.NotNull(e);
        Assert.Equal(PlayerRosterEventKind.Disconnected, e!.Kind);
        Assert.Equal("S7razzy", e.DisplayName);
    }

    [Fact]
    public void Left_facility_must_win_over_generic_chat_regex()
    {
        // The colon-less "X has left the facility" line could otherwise be
        // misread as a chat message body. Make sure the dedicated regex
        // claims it first - same ordering rule as EnteredFacility.
        var e = PlayerRosterParser.TryParse(Log("CHAT LOG:  S7razzy has left the facility."));
        Assert.NotEqual(PlayerRosterEventKind.Chat, e!.Kind);
    }

    [Fact]
    public void Left_facility_marks_a_steam_logged_in_player_offline_by_name()
    {
        // This is the user-facing fix: the player logs in via Steam (so the
        // tracker keys them by SteamID64) and then quits cleanly. AF logs
        // "has left the facility" with only the display name - no hex - and
        // the old roster parser had no signal for it, leaving the row stuck
        // on "online" until server shutdown.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: NotifyAcceptedConnection: ... RemoteAddr: 10.0.0.1:55555 ...", 0));
        t.Apply(Log(
            "LogNet: Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_|abc " +
            "userId: 0002abc platform: EOSPlus", 1));
        t.Apply(Log("LogNet: Join succeeded: S7razzy", 2));
        Assert.True(t.Entries[0].IsOnline);

        t.Apply(Log("CHAT LOG:  S7razzy has left the facility.", 300));

        Assert.False(t.Entries[0].IsOnline);
        Assert.Equal(0, t.OnlineCount);
        Assert.Equal("disconnected", t.Entries[0].LastActivity);
    }

    [Fact]
    public void Left_facility_for_unknown_player_is_a_safe_no_op()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Someone", 0));
        Assert.True(t.Entries[0].IsOnline);

        t.Apply(Log("CHAT LOG:  GhostPlayer has left the facility.", 1));

        // Existing player must NOT be affected by an unrelated leave line.
        Assert.True(t.Entries[0].IsOnline);
    }

    // ---- A2S corroboration reconciliation (live count cross-check) ----

    [Fact]
    public void Reconcile_with_equal_live_count_is_a_noop()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));
        t.Apply(Log("LogNet: Join succeeded: Bob", 1));
        Assert.Equal(2, t.OnlineCount);

        var evicted = t.ReconcileWithLiveCount(2, T0.AddSeconds(60));

        Assert.Empty(evicted);
        Assert.Equal(2, t.OnlineCount);
        Assert.Null(t.CountWarning);
    }

    [Fact]
    public void Reconcile_with_higher_live_count_does_not_fabricate_rows()
    {
        // A2S says 2 but we only know about 1. Never invent a roster row
        // from a count - we'd have no name, no id, nothing to display.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));

        var evicted = t.ReconcileWithLiveCount(2, T0.AddSeconds(60));

        Assert.Empty(evicted);
        Assert.Single(t.Entries);
        Assert.Equal(1, t.OnlineCount);
    }

    [Fact]
    public void Reconcile_with_lower_live_count_debounces_first_poll()
    {
        // A single low reading must not evict anyone - a dropped UDP packet
        // or a race against a connection accept should not flicker a real
        // player offline. The debounce gate is exactly the "stuck online
        // for an hour" prevention the user asked for, minus the false
        // positives a one-shot evict would cause.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));
        t.Apply(Log("LogNet: Join succeeded: Bob", 1));

        var evicted = t.ReconcileWithLiveCount(1, T0.AddSeconds(60));

        Assert.Empty(evicted);
        Assert.Equal(2, t.OnlineCount);
        Assert.NotNull(t.CountWarning); // mismatch is surfaced even when not evicting
    }

    [Fact]
    public void Reconcile_with_two_consecutive_low_readings_evicts_oldest_first()
    {
        // Alice last seen at T0+0s, Bob at T0+30s. A2S says 1 - the missing
        // player is the one whose log activity went stale first.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));
        t.Apply(Log("LogNet: Join succeeded: Bob", 30));

        t.ReconcileWithLiveCount(1, T0.AddSeconds(60));
        var evicted = t.ReconcileWithLiveCount(1, T0.AddSeconds(120));

        var name = Assert.Single(evicted);
        Assert.Equal("Alice", name);
        Assert.False(t.Entries.Single(e => e.DisplayName == "Alice").IsOnline);
        Assert.True(t.Entries.Single(e => e.DisplayName == "Bob").IsOnline);
        Assert.Equal(1, t.OnlineCount);
    }

    [Fact]
    public void Reconcile_resets_debounce_counter_when_match_arrives()
    {
        // A mismatch then a clean reading must restart the debouncer - a
        // subsequent single mismatch cannot inherit the prior count.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));
        t.Apply(Log("LogNet: Join succeeded: Bob", 1));

        t.ReconcileWithLiveCount(1, T0.AddSeconds(60));   // mismatch #1
        t.ReconcileWithLiveCount(2, T0.AddSeconds(120));  // match - resets
        var evicted =
            t.ReconcileWithLiveCount(1, T0.AddSeconds(180)); // new mismatch #1 only

        Assert.Empty(evicted);
        Assert.Equal(2, t.OnlineCount);
    }

    [Fact]
    public void Reconcile_with_zero_evicts_all_after_debounce()
    {
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));
        t.Apply(Log("LogNet: Join succeeded: Bob", 1));

        t.ReconcileWithLiveCount(0, T0.AddSeconds(60));
        var evicted = t.ReconcileWithLiveCount(0, T0.AddSeconds(120));

        Assert.Equal(2, evicted.Count);
        Assert.Equal(0, t.OnlineCount);
    }

    [Fact]
    public void Reconcile_ignores_negative_live_count()
    {
        // A2S parser failure / corrupt payload would never report negative
        // counts, but a defensive guard keeps the algorithm honest.
        var t = new PlayerRosterTracker();
        t.Apply(Log("LogNet: Join succeeded: Alice", 0));

        var evicted = t.ReconcileWithLiveCount(-1, T0.AddSeconds(60));

        Assert.Empty(evicted);
        Assert.Equal(1, t.OnlineCount);
    }
}
