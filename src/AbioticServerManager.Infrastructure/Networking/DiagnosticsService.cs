using System.Net.Http;
using System.Net.NetworkInformation;
using AbioticServerManager.Core.Config;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Infrastructure.Networking;

using SysProcess = System.Diagnostics.Process;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private static readonly HttpClient PublicIpHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly string[] ServerProcessNames =
        ["AbioticFactorServer-Win64-Shipping", "AbioticFactorServer", "AbioticFactor"];

    private readonly IConfigValidator _validator;
    private readonly A2SQueryClient _a2s;

    public DiagnosticsService(IConfigValidator validator, A2SQueryClient a2s)
    {
        _validator = validator;
        _a2s = a2s;
    }

    public Task<IReadOnlyList<DiagnosticMessage>> ValidateConfigAsync(
        ServerInstance instance,
        IReadOnlyList<ServerInstance> otherInstances,
        CancellationToken ct = default) =>
        Task.FromResult(_validator.Validate(instance, otherInstances));

    public Task<QueryCheckResult> CheckLocalQueryAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        try
        {
            var udpPorts = IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveUdpListeners()
                .Select(ep => ep.Port)
                .ToHashSet();

            var gameBound = udpPorts.Contains(instance.GamePort);
            var queryBound = udpPorts.Contains(instance.QueryPort);
            var serverRunning = IsServerProcessRunning();

            var detail =
                $"Server process: {(serverRunning ? "running" : "not detected")}. " +
                $"UDP game {instance.GamePort}: {(gameBound ? "bound" : "not bound")}. " +
                $"UDP query {instance.QueryPort}: {(queryBound ? "bound" : "not bound")}. " +
                "The Network tab's Check Setup shows which process owns each port.";

            if (queryBound && gameBound)
            {
                return Task.FromResult(new QueryCheckResult
                {
                    Status = CheckStatus.Pass,
                    Detail = detail,
                });
            }

            if (!serverRunning)
            {
                return Task.FromResult(new QueryCheckResult
                {
                    Status = CheckStatus.Unknown,
                    Detail = detail + " The server does not appear to be running yet.",
                });
            }

            return Task.FromResult(new QueryCheckResult
            {
                Status = CheckStatus.Fail,
                Detail = detail + " The server is running but one or both UDP ports are " +
                         "not bound - it may still be starting, or another process took the port.",
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new QueryCheckResult
            {
                Status = CheckStatus.Unknown,
                Detail = $"Could not inspect local UDP listeners: {ex.Message}",
            });
        }
    }

    public async Task<ExternalVisibilityResult> CheckExternalVisibilityAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        var guidance = new List<string>
        {
            $"Forward UDP port {instance.GamePort} (game) on your router to this machine.",
            $"Forward UDP port {instance.QueryPort} (query) on your router to this machine.",
            "Allow the game and query UDP ports plus the server executable in Windows Firewall.",
            "Best proof: ask someone OUTSIDE your network to find the server in the browser " +
            "or connect directly.",
            "If the server appears in the browser but they cannot connect, the game UDP port " +
            "is likely blocked.",
            "If the server does not appear at all, the query UDP port is likely blocked.",
            instance.LanOnly
                ? "LAN-only mode is ON - this server is not expected to be visible on the internet."
                : "LAN-only mode is OFF - the server may be visible publicly if ports are forwarded.",
        };

        if (instance.GamePort == instance.QueryPort)
        {
            guidance.Add("Game port and query port are identical; set them to different values.");
        }

        if (instance.LanOnly)
        {
            return new ExternalVisibilityResult
            {
                Status = CheckStatus.Unknown,
                Detail = "LAN-only mode is on, so the server is not expected to be reachable " +
                         "from the internet. Turn LAN Only off to host publicly.",
                Guidance = guidance,
            };
        }

        var publicIp = await TryGetPublicIpAsync(ct).ConfigureAwait(false);
        if (publicIp is null)
        {
            return new ExternalVisibilityResult
            {
                Status = CheckStatus.Unknown,
                Detail = "Could not determine this network's public IP, so external " +
                         "reachability cannot be confirmed. Follow the guidance below.",
                Guidance = guidance,
            };
        }

        var scope = IpAddressClassifier.Classify(publicIp);
        if (IpAddressClassifier.IsPrivateOrCarrierGrade(scope))
        {
            // The internet-facing address is itself private/CGNAT: a strong, evidence-based
            // double-NAT signal. Router port forwarding usually cannot help here.
            guidance.Insert(0,
                $"Detected internet-facing IP {publicIp} is {IpAddressClassifier.Describe(scope)}. " +
                "Ask your ISP for a public IPv4, or bridge the upstream router.");
            return new ExternalVisibilityResult
            {
                Status = CheckStatus.Fail,
                Detail = $"This network appears to be behind CGNAT / double NAT " +
                         $"(public IP {publicIp} is {IpAddressClassifier.Describe(scope)}). " +
                         "Port forwarding on your router will likely NOT make the server " +
                         "reachable from the internet, even with correct firewall rules.",
                Guidance = guidance,
            };
        }

        var responded = await _a2s
            .IsRespondingAsync(publicIp, instance.QueryPort, TimeSpan.FromSeconds(4), ct)
            .ConfigureAwait(false);

        if (responded)
        {
            return new ExternalVisibilityResult
            {
                Status = CheckStatus.Pass,
                Detail = $"The server answered a Steam query on {publicIp}:{instance.QueryPort}. " +
                         "That is a strong signal it is reachable from outside this machine.",
                Guidance = guidance,
            };
        }

        // A failed query from the same network is NOT proof of failure: many routers
        // block hairpin NAT (querying your own public IP from inside). Honest = Unknown.
        return new ExternalVisibilityResult
        {
            Status = CheckStatus.Unknown,
            Detail = $"No Steam query reply from {publicIp}:{instance.QueryPort}. This is a " +
                     "signal, not proof: many routers block querying your own public IP " +
                     "from inside (hairpin NAT). Verify from an outside network after " +
                     "forwarding the ports.",
            Guidance = guidance,
        };
    }

    private static bool IsServerProcessRunning()
    {
        try
        {
            foreach (var name in ServerProcessNames)
            {
                var found = SysProcess.GetProcessesByName(name);
                try
                {
                    if (found.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (var p in found)
                    {
                        p.Dispose();
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryGetPublicIpAsync(CancellationToken ct)
    {
        try
        {
            var ip = (await PublicIpHttp.GetStringAsync("https://api.ipify.org", ct)
                .ConfigureAwait(false)).Trim();
            return System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    public Task<VersionCheckResult> CheckVersionAsync(
        ServerInstance instance,
        CancellationToken ct = default) =>
        Task.FromResult(new VersionCheckResult
        {
            Status = CheckStatus.Unknown,
            Detail = "Run a SteamCMD validate/update to ensure the server is current. " +
                     "Automatic version comparison is not yet available.",
        });
}
