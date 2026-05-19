using System.Text.RegularExpressions;

namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Parses real Abiotic Factor / Unreal / EOS dedicated-server log lines into
/// roster events. Generic "X joined / X left" text is still understood as a
/// fallback so older expectations and non-Steam phrasing keep working.
/// </summary>
public static class PlayerRosterParser
{
    // LogNet: Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_|<hex>
    //         userId: EOSPlus:UNKNOWN [0x...]_+_|<hex> platform: EOSPlus
    private static readonly Regex LoginRequest = new(
        @"Login request:\s*\??Name=(?<name>.+?)\?\?ConnectID=(?<cid>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // LogNet: UNetConnection::Close: ... UniqueId: EOSPlus:..[0x..]_+_|<connectIdHex>, Channels:..
    private static readonly Regex Disconnect = new(
        @"UNetConnection::Close:.*_\+_\|(?<cidhex>[0-9a-fA-F]{8,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlatformToken = new(
        @"platform:\s*(?<plat>[^\s,;|]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SteamId64 = new(
        @"(?<steam>7656\d{13})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Join succeeded: S7razzy
    private static readonly Regex JoinSucceeded = new(
        @"Join succeeded:\s*(?<name>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // CHAT LOG:  S7razzy has entered the facility.
    private static readonly Regex EnteredFacility = new(
        @"CHAT LOG:\s*(?<name>.+?)\s+has entered the facility",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)
    private static readonly Regex PlayerCount = new(
        @"\(PlayerCount\)\s*with value\s*\((?<n>\d+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // NotifyAcceptedConnection ... 192.168.254.3:54007
    private static readonly Regex AcceptedConnection = new(
        @"(?:NotifyAcceptedConnection|AddClientConnection|accepted connection).*?(?<addr>\d{1,3}(?:\.\d{1,3}){3}:\d{1,5})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ServerStoppedSignals =
    [
        "[server stopped]",
        "[server exited",
        "dedicated server will shut down",
        "LogExit: Exiting.",
    ];

    public static PlayerRosterEvent? TryParse(ServerLogLine line)
    {
        var text = line.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var ts = line.Timestamp;

        var login = LoginRequest.Match(text);
        if (login.Success)
        {
            var cid = login.Groups["cid"].Value.Trim();
            var steam = SteamId64.Match(cid) is { Success: true } sm
                ? sm.Groups["steam"].Value
                : SteamId64.Match(text) is { Success: true } sm2 ? sm2.Groups["steam"].Value : null;
            var plat = PlatformToken.Match(text) is { Success: true } pm
                ? pm.Groups["plat"].Value
                : null;
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.LoginRequested,
                CleanName(login.Groups["name"].Value),
                cid.Length > 0 ? cid : null,
                steam, plat, null, null, text);
        }

        var join = JoinSucceeded.Match(text);
        if (join.Success)
        {
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.JoinSucceeded,
                CleanName(join.Groups["name"].Value), null, null, null, null, null, text);
        }

        var entered = EnteredFacility.Match(text);
        if (entered.Success)
        {
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.EnteredFacility,
                CleanName(entered.Groups["name"].Value), null, null, null, null, null, text);
        }

        var count = PlayerCount.Match(text);
        if (count.Success && int.TryParse(count.Groups["n"].Value, out var n))
        {
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.PlayerCountChanged,
                null, null, null, null, null, n, text);
        }

        var accepted = AcceptedConnection.Match(text);
        if (accepted.Success)
        {
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.ConnectionAccepted,
                null, null, null, null, accepted.Groups["addr"].Value, null, text);
        }

        var dc = Disconnect.Match(text);
        if (dc.Success)
        {
            // PrimaryId carries the connect-id hex so the tracker can match the
            // exact player who left (the line has no display name).
            return new PlayerRosterEvent(
                ts, PlayerRosterEventKind.Disconnected,
                null, dc.Groups["cidhex"].Value, null, null, null, null, text);
        }

        foreach (var signal in ServerStoppedSignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return new PlayerRosterEvent(
                    ts, PlayerRosterEventKind.ServerStopped,
                    null, null, null, null, null, null, text);
            }
        }

        // Fallback: generic "X joined" / "X left" phrasing.
        var generic = PlayerActivityParser.TryParse(line);
        if (generic is not null)
        {
            return new PlayerRosterEvent(
                ts,
                generic.Kind == PlayerActivityKind.Joined
                    ? PlayerRosterEventKind.JoinSucceeded
                    : PlayerRosterEventKind.Disconnected,
                generic.PlayerName, null, null, null, null, null, text);
        }

        return null;
    }

    private static string CleanName(string value)
    {
        var cleaned = value.Trim().Trim('"', '\'', ':', '-', ' ', '?');
        return cleaned.Length > 64 ? cleaned[..64].Trim() : cleaned;
    }
}
