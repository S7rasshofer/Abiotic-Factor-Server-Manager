using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AbioticServerManager.Infrastructure.Networking;

/// <summary>
/// Minimal Source/Steam A2S_INFO client. A valid reply means the query port is
/// actually reachable and answering - the only honest basis for a green
/// "externally visible" light. Handles the modern challenge handshake.
/// <see cref="QueryInfoAsync"/> also parses the player-count fields so the
/// roster can be reconciled against the server's own view of who is
/// connected (corroborator for the log-line-driven tracker).
/// </summary>
public sealed class A2SQueryClient
{
    private static readonly byte[] InfoPayload =
        Encoding.ASCII.GetBytes("Source Engine Query\0");

    public async Task<bool> IsRespondingAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default) =>
        await GetInfoReplyAsync(host, port, timeout, ct).ConfigureAwait(false) is not null;

    /// <summary>
    /// Queries A2S_INFO and parses the player-count fields. Returns
    /// <c>null</c> on any failure (timeout, malformed reply, ICMP unreachable,
    /// challenge handshake that never settles into an info reply). The caller
    /// treats null as "no signal this poll" and leaves the roster alone -
    /// debounce in <c>PlayerRosterTracker</c> handles the rest.
    /// </summary>
    public async Task<A2SInfoSnapshot?> QueryInfoAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var reply = await GetInfoReplyAsync(host, port, timeout, ct).ConfigureAwait(false);
        return reply is null ? null : TryParseInfo(reply, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Drives the wire-level handshake and returns the verified info-reply
    /// bytes (header + 0x49 type byte + payload), or <c>null</c> on any
    /// failure. Both public entry points share this so the protocol logic
    /// is in exactly one place.
    /// </summary>
    private static async Task<byte[]?> GetInfoReplyAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
        {
            return null;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return null;
            }

            using var udp = new UdpClient(addresses[0].AddressFamily);
            udp.Connect(new IPEndPoint(addresses[0], port));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var reply = await SendAndReceiveAsync(udp, BuildRequest(null), cts.Token)
                .ConfigureAwait(false);
            if (reply is null || reply.Length < 5 || !HasQueryHeader(reply))
            {
                return null;
            }

            // 0x49 = info reply (reachable). 0x41 = challenge - answer it and retry.
            if (reply[4] == 0x49)
            {
                return reply;
            }

            if (reply[4] == 0x41 && reply.Length >= 9)
            {
                var challenge = reply[5..9];
                var second = await SendAndReceiveAsync(udp, BuildRequest(challenge), cts.Token)
                    .ConfigureAwait(false);
                if (second is { Length: >= 5 } && HasQueryHeader(second) && second[4] == 0x49)
                {
                    return second;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a verified A2S_INFO reply into the fields we care about. Layout
    /// (per Valve's wiki): 4-byte 0xFF header, 0x49 type, 1-byte protocol,
    /// then four null-terminated strings (Name, Map, Folder, Game), then a
    /// 2-byte AppID short, then Players + MaxPlayers bytes. Any later EDF
    /// fields are ignored. Returns null on a truncated / malformed payload.
    /// Internal so the test suite can pin the parser against golden packets
    /// without needing a UDP loopback.
    /// </summary>
    internal static A2SInfoSnapshot? TryParseInfo(byte[] reply, DateTimeOffset queriedAt)
    {
        // Minimum well-formed reply with all empty strings:
        //   4 header + 1 type + 1 protocol + 4 nulls + 2 short + 1 + 1 = 14
        if (reply.Length < 14 || !HasQueryHeader(reply) || reply[4] != 0x49)
        {
            return null;
        }

        var i = 5;            // past 4-byte 0xFF header + 0x49 type byte
        i++;                  // skip protocol byte
        if (!TryReadCString(reply, ref i, out var name)) return null;
        if (!TryReadCString(reply, ref i, out _)) return null;     // map
        if (!TryReadCString(reply, ref i, out _)) return null;     // folder
        if (!TryReadCString(reply, ref i, out _)) return null;     // game
        if (i + 4 > reply.Length) return null;                     // need short + Players + MaxPlayers
        i += 2;               // skip 2-byte AppID short
        var players = reply[i++];
        var maxPlayers = reply[i];

        return new A2SInfoSnapshot(name, players, maxPlayers, queriedAt);
    }

    /// <summary>Reads a null-terminated UTF-8 string starting at <paramref name="offset"/> and advances past the null.</summary>
    private static bool TryReadCString(byte[] data, ref int offset, out string value)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        if (offset >= data.Length)
        {
            value = string.Empty;
            return false;
        }

        value = Encoding.UTF8.GetString(data, start, offset - start);
        offset++; // skip null terminator
        return true;
    }

    private static async Task<byte[]?> SendAndReceiveAsync(
        UdpClient udp,
        byte[] request,
        CancellationToken ct)
    {
        await udp.SendAsync(request, ct).ConfigureAwait(false);
        var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
        return result.Buffer;
    }

    private static byte[] BuildRequest(byte[]? challenge)
    {
        var size = 5 + InfoPayload.Length + (challenge?.Length ?? 0);
        var packet = new byte[size];
        packet[0] = packet[1] = packet[2] = packet[3] = 0xFF;
        packet[4] = 0x54; // 'T' - A2S_INFO
        InfoPayload.CopyTo(packet, 5);
        challenge?.CopyTo(packet, 5 + InfoPayload.Length);
        return packet;
    }

    private static bool HasQueryHeader(byte[] data) =>
        data[0] == 0xFF && data[1] == 0xFF && data[2] == 0xFF && data[3] == 0xFF;
}
