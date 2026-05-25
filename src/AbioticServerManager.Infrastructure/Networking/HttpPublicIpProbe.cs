using AbioticServerManager.Core.Networking;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Networking;

/// <summary>
/// Resolves the host's public IPv4 by calling a small plaintext endpoint that
/// echoes the caller's address. Cached per-instance so repeated UI refreshes
/// don't hammer the endpoint. Never throws - public IP is best-effort context,
/// not a critical path.
/// </summary>
public sealed class HttpPublicIpProbe : IPublicIpProbe, IDisposable
{
    // Plaintext, no JSON, no tracking, no rate limit on light reasonable use.
    private static readonly Uri Endpoint = new("https://api.ipify.org");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly ILogger<HttpPublicIpProbe> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public HttpPublicIpProbe(ILogger<HttpPublicIpProbe> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "FacilityOverseer/1.0 (+public-ip-probe; no tracking)");
    }

    public async Task<string?> ProbeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            using var response = await _http
                .GetAsync(Endpoint, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Public IP probe returned HTTP {Status}",
                    (int)response.StatusCode);
                return null;
            }

            var body = await response.Content
                .ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            _cached = PublicIpParsing.TryParseIpv4(body);
            return _cached;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogDebug(ex, "Public IP probe failed");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
