using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AbioticServerManager.Infrastructure.Networking;

/// <summary>
/// Minimal Source/Steam A2S_INFO client. A valid reply means the query port is
/// actually reachable and answering — the only honest basis for a green
/// "externally visible" light. Handles the modern challenge handshake.
/// </summary>
public sealed class A2SQueryClient
{
    private static readonly byte[] InfoPayload =
        Encoding.ASCII.GetBytes("Source Engine Query\0");

    public async Task<bool> IsRespondingAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
        {
            return false;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return false;
            }

            using var udp = new UdpClient(addresses[0].AddressFamily);
            udp.Connect(new IPEndPoint(addresses[0], port));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var reply = await SendAndReceiveAsync(udp, BuildRequest(null), cts.Token)
                .ConfigureAwait(false);
            if (reply is null || reply.Length < 5 || !HasQueryHeader(reply))
            {
                return false;
            }

            // 0x49 = info reply (reachable). 0x41 = challenge — answer it and retry.
            if (reply[4] == 0x49)
            {
                return true;
            }

            if (reply[4] == 0x41 && reply.Length >= 9)
            {
                var challenge = reply[5..9];
                var second = await SendAndReceiveAsync(udp, BuildRequest(challenge), cts.Token)
                    .ConfigureAwait(false);
                return second is { Length: >= 5 } && HasQueryHeader(second) && second[4] == 0x49;
            }

            return false;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
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
        packet[4] = 0x54; // 'T' — A2S_INFO
        InfoPayload.CopyTo(packet, 5);
        challenge?.CopyTo(packet, 5 + InfoPayload.Length);
        return packet;
    }

    private static bool HasQueryHeader(byte[] data) =>
        data[0] == 0xFF && data[1] == 0xFF && data[2] == 0xFF && data[3] == 0xFF;
}
