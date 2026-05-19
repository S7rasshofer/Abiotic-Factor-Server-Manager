using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class RouterChecklistBuilderTests
{
    [Fact]
    public void Uses_selected_ports_and_detected_lan_ip()
    {
        var lines = RouterChecklistBuilder.Build(7777, 27015, "192.168.1.42");
        var text = string.Join("\n", lines);

        Assert.Contains("Server PC LAN IPv4: 192.168.1.42", text);
        Assert.Contains("External UDP 7777 -> 192.168.1.42:7777", text);
        Assert.Contains("External UDP 27015 -> 192.168.1.42:27015", text);
    }

    [Fact]
    public void Uses_custom_ports()
    {
        var text = string.Join("\n", RouterChecklistBuilder.Build(28015, 28016, "10.0.0.5"));

        Assert.Contains("External UDP 28015 -> 10.0.0.5:28015", text);
        Assert.Contains("External UDP 28016 -> 10.0.0.5:28016", text);
        Assert.DoesNotContain("7777", text);
        Assert.DoesNotContain("27015", text);
    }

    [Fact]
    public void Includes_recommended_and_cgnat_guidance()
    {
        var text = string.Join("\n", RouterChecklistBuilder.Build(7777, 27015, "192.168.1.2"));

        Assert.Contains("Reserve 192.168.1.2", text);
        Assert.Contains("Use UDP, not TCP.", text);
        Assert.Contains("one-to-one", text);
        Assert.Contains("Do not forward unrelated ports", text);
        Assert.Contains("100.64.0.0/10", text);
    }

    [Fact]
    public void Includes_selected_world_and_joinability_guidance()
    {
        var text = string.Join("\n", RouterChecklistBuilder.Build(
            "Cascade",
            7777,
            27015,
            "192.168.1.2"));

        Assert.Contains("Selected world: Cascade", text);
        Assert.Contains("server browser", text);
        Assert.Contains("game UDP port may be blocked or forwarded to the wrong LAN IP", text);
        Assert.Contains("query UDP port may be blocked or forwarded to the wrong LAN IP", text);
    }

    [Fact]
    public void Warns_when_ports_are_identical()
    {
        var text = string.Join("\n", RouterChecklistBuilder.Build(7777, 7777, "192.168.1.2"));

        Assert.Contains("Game port and query port are the same", text);
    }

    [Fact]
    public void Handles_missing_lan_ip_without_crashing()
    {
        var text = string.Join("\n", RouterChecklistBuilder.Build(7777, 27015, null));

        Assert.Contains("run Check Setup", text);
        Assert.Contains("7777", text);
    }
}
