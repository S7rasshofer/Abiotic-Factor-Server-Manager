using System.Net;
using System.Net.Sockets;
using System.Text;
using AbioticServerManager.Infrastructure.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class A2SQueryClientTests
{
    private readonly A2SQueryClient _client = new();

    /// <summary>
    /// Builds a well-formed A2S_INFO reply (header + 0x49 + protocol + four
    /// C-strings + AppID + Players + MaxPlayers). Mirrors Valve's wire
    /// layout exactly so the parser tests stand in for "what the real
    /// dedicated server would send".
    /// </summary>
    private static byte[] BuildInfoPacket(
        string name = "Facility",
        string map = "Worldmap",
        string folder = "AbioticFactor",
        string game = "Abiotic Factor",
        ushort appId = 0,
        byte players = 0,
        byte maxPlayers = 6)
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xFF, 0xFF, 0xFF], 0, 4); // header
        ms.WriteByte(0x49);                       // info reply type
        ms.WriteByte(0x11);                       // protocol byte
        WriteCString(ms, name);
        WriteCString(ms, map);
        WriteCString(ms, folder);
        WriteCString(ms, game);
        ms.WriteByte((byte)(appId & 0xFF));
        ms.WriteByte((byte)((appId >> 8) & 0xFF));
        ms.WriteByte(players);
        ms.WriteByte(maxPlayers);
        return ms.ToArray();
    }

    private static void WriteCString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    [Fact]
    public async Task Returns_true_when_server_sends_info_reply()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serve = Task.Run(async () =>
        {
            var req = await server.ReceiveAsync();
            byte[] reply = [0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11, (byte)'O', (byte)'K', 0x00];
            await server.SendAsync(reply, reply.Length, req.RemoteEndPoint);
        });

        var ok = await _client.IsRespondingAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));

        Assert.True(ok);
        await serve;
    }

    [Fact]
    public async Task Answers_challenge_then_succeeds()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serve = Task.Run(async () =>
        {
            var first = await server.ReceiveAsync();
            byte[] challenge = [0xFF, 0xFF, 0xFF, 0xFF, 0x41, 0x01, 0x02, 0x03, 0x04];
            await server.SendAsync(challenge, challenge.Length, first.RemoteEndPoint);

            var second = await server.ReceiveAsync();
            // The retry must echo the 4 challenge bytes at the end.
            Assert.Equal([0x01, 0x02, 0x03, 0x04], second.Buffer[^4..]);
            byte[] info = [0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11];
            await server.SendAsync(info, info.Length, second.RemoteEndPoint);
        });

        var ok = await _client.IsRespondingAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));

        Assert.True(ok);
        await serve;
    }

    [Fact]
    public async Task Returns_false_when_nothing_is_listening()
    {
        // Bind then release a port so it is almost certainly closed.
        var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var deadPort = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose();

        var ok = await _client.IsRespondingAsync("127.0.0.1", deadPort, TimeSpan.FromMilliseconds(600));

        Assert.False(ok);
    }

    // ---- A2S_INFO parser (TryParseInfo) ----

    [Fact]
    public async Task Query_info_returns_parsed_snapshot_on_valid_reply()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serve = Task.Run(async () =>
        {
            var req = await server.ReceiveAsync();
            var reply = BuildInfoPacket(name: "Test World", players: 3, maxPlayers: 6);
            await server.SendAsync(reply, reply.Length, req.RemoteEndPoint);
        });

        var snap = await _client.QueryInfoAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));

        Assert.NotNull(snap);
        Assert.Equal("Test World", snap!.ServerName);
        Assert.Equal(3, snap.PlayerCount);
        Assert.Equal(6, snap.MaxPlayers);
        await serve;
    }

    [Fact]
    public async Task Query_info_answers_challenge_then_parses_second_reply()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serve = Task.Run(async () =>
        {
            var first = await server.ReceiveAsync();
            byte[] challenge = [0xFF, 0xFF, 0xFF, 0xFF, 0x41, 0x0A, 0x0B, 0x0C, 0x0D];
            await server.SendAsync(challenge, challenge.Length, first.RemoteEndPoint);

            var second = await server.ReceiveAsync();
            Assert.Equal([0x0A, 0x0B, 0x0C, 0x0D], second.Buffer[^4..]);
            var reply = BuildInfoPacket(players: 2, maxPlayers: 6);
            await server.SendAsync(reply, reply.Length, second.RemoteEndPoint);
        });

        var snap = await _client.QueryInfoAsync("127.0.0.1", port, TimeSpan.FromSeconds(3));

        Assert.NotNull(snap);
        Assert.Equal(2, snap!.PlayerCount);
        await serve;
    }

    [Fact]
    public void Parse_info_returns_null_on_truncated_payload()
    {
        // Valid header + 0x49 but the payload cuts off mid-string. The
        // parser must refuse rather than fabricate a snapshot from garbage.
        byte[] truncated = [0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11, (byte)'F', (byte)'O', (byte)'O'];

        var snap = A2SQueryClient.TryParseInfo(truncated, DateTimeOffset.UtcNow);

        Assert.Null(snap);
    }

    [Fact]
    public void Parse_info_returns_null_when_type_byte_is_wrong()
    {
        // Build a well-formed info packet but flip the type byte to a
        // challenge marker - the parser belongs to the info-reply path
        // only and must not try to parse a challenge as info.
        var packet = BuildInfoPacket();
        packet[4] = 0x41;

        var snap = A2SQueryClient.TryParseInfo(packet, DateTimeOffset.UtcNow);

        Assert.Null(snap);
    }

    [Fact]
    public async Task Query_info_returns_null_when_nothing_is_listening()
    {
        var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var deadPort = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose();

        var snap = await _client.QueryInfoAsync("127.0.0.1", deadPort, TimeSpan.FromMilliseconds(600));

        Assert.Null(snap);
    }
}
