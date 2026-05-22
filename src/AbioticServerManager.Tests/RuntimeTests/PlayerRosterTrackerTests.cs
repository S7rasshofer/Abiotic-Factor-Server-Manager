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

        // No UNetConnection::Close line arrives — only the count dropping.
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

        // A close line whose hex doesn't match — would leave the player stuck.
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
}
