using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class Ipv4SelectionTests
{
    private static Ipv4Candidate Candidate(
        string address,
        string name = "Ethernet",
        string description = "Realtek Gigabit",
        bool up = true,
        bool gateway = true,
        bool loopbackOrTunnel = false) => new()
        {
            Address = address,
            InterfaceName = name,
            InterfaceDescription = description,
            IsUp = up,
            HasDefaultGateway = gateway,
            IsLoopbackOrTunnel = loopbackOrTunnel,
        };

    [Fact]
    public void Ignores_loopback_and_apipa()
    {
        var result = Ipv4Selection.Select(
        [
            Candidate("127.0.0.1", "Loopback", "Software Loopback", loopbackOrTunnel: true),
            Candidate("169.254.3.4", "Ethernet 2", gateway: false),
            Candidate("192.168.1.50"),
        ]);

        Assert.Equal("192.168.1.50", result.Best);
        Assert.DoesNotContain("127.0.0.1", result.Usable);
        Assert.DoesNotContain("169.254.3.4", result.Usable);
    }

    [Fact]
    public void Ignores_disconnected_adapters()
    {
        var result = Ipv4Selection.Select(
        [
            Candidate("192.168.1.9", up: false),
            Candidate("192.168.1.10"),
        ]);

        Assert.Equal("192.168.1.10", result.Best);
        Assert.Single(result.Usable);
    }

    [Fact]
    public void Prefers_physical_adapter_over_virtual()
    {
        var result = Ipv4Selection.Select(
        [
            Candidate("172.20.5.1", "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter"),
            Candidate("192.168.0.20", "Wi-Fi", "Intel Wireless-AC"),
        ]);

        Assert.Equal("192.168.0.20", result.Best);
    }

    [Fact]
    public void Falls_back_to_virtual_when_it_is_the_only_candidate()
    {
        var result = Ipv4Selection.Select(
        [
            Candidate("172.20.5.1", "vEthernet (Default Switch)", "Hyper-V Virtual Adapter"),
        ]);

        Assert.Equal("172.20.5.1", result.Best);
    }

    [Fact]
    public void Warns_when_multiple_physical_lan_ips_exist()
    {
        var result = Ipv4Selection.Select(
        [
            Candidate("192.168.1.10", "Ethernet", "Realtek"),
            Candidate("192.168.5.10", "Wi-Fi", "Intel Wireless"),
        ]);

        Assert.True(result.HasAmbiguity);
        Assert.NotNull(result.Warning);
        Assert.Contains("192.168.1.10", result.Warning);
        Assert.Contains("192.168.5.10", result.Warning);
    }

    [Fact]
    public void No_candidates_yields_warning_and_no_best()
    {
        var result = Ipv4Selection.Select([]);

        Assert.Null(result.Best);
        Assert.NotNull(result.Warning);
    }
}
