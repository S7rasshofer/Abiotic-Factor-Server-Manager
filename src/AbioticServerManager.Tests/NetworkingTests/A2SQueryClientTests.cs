using System.Net;
using System.Net.Sockets;
using AbioticServerManager.Infrastructure.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class A2SQueryClientTests
{
    private readonly A2SQueryClient _client = new();

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
}
