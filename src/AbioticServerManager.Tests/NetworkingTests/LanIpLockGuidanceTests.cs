using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class LanIpLockGuidanceTests
{
    [Fact]
    public void Empty_ip_yields_a_helpful_blocker_message()
    {
        var text = LanIpLockGuidance.Compose(new LanIpLockContext { Ipv4 = "" });
        Assert.Contains("No LAN IPv4 detected", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reserved IP", text);
    }

    [Fact]
    public void Minimal_context_includes_ip_and_concept()
    {
        var text = LanIpLockGuidance.Compose(new LanIpLockContext { Ipv4 = "192.168.1.42" });

        Assert.Contains("DHCP reservation", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("192.168.1.42", text);
        Assert.Contains("Reserved IP:", text);
        Assert.Contains("MAC address:", text);
    }

    [Fact]
    public void Full_context_pastes_every_value()
    {
        var ctx = new LanIpLockContext
        {
            Ipv4 = "10.0.0.5",
            Hostname = "TORCHWOOD-PC",
            MacAddress = "AB:CD:EF:12:34:56",
            Gateway = "10.0.0.1",
            AdapterDescription = "Intel Ethernet I225-V",
        };

        var text = LanIpLockGuidance.Compose(ctx);

        Assert.Contains("10.0.0.5", text);
        Assert.Contains("TORCHWOOD-PC", text);
        Assert.Contains("AB:CD:EF:12:34:56", text);
        Assert.Contains("http://10.0.0.1/", text);
        Assert.Contains("Intel Ethernet I225-V", text);
    }

    [Fact]
    public void Includes_a_handful_of_router_brand_hints()
    {
        // Generic guidance must not assume one router brand — names differ widely.
        var text = LanIpLockGuidance.Compose(new LanIpLockContext { Ipv4 = "192.168.1.42" });

        Assert.Contains("UniFi", text);
        Assert.Contains("ASUS", text);
        Assert.Contains("Netgear", text);
        Assert.Contains("TP-Link", text);
    }

    [Fact]
    public void Missing_mac_shows_a_clear_fallback_hint()
    {
        var text = LanIpLockGuidance.Compose(new LanIpLockContext
        {
            Ipv4 = "192.168.1.42",
            MacAddress = "",
        });

        Assert.Contains("MAC address:", text);
        Assert.Contains("not detected", text, StringComparison.OrdinalIgnoreCase);
    }
}
