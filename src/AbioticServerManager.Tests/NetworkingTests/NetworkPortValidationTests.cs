using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class NetworkPortValidationTests
{
    private static bool Has(PortValidationResult r, string code) =>
        r.Messages.Any(m => m.Code == code);

    [Fact]
    public void Default_ports_are_valid()
    {
        var r = NetworkPortValidation.Validate(7777, 27015);

        Assert.True(r.CanCreateRules);
        Assert.DoesNotContain(r.Messages, m => m.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Custom_distinct_high_ports_are_valid()
    {
        var r = NetworkPortValidation.Validate(28000, 28001);

        Assert.True(r.CanCreateRules);
        Assert.Empty(r.Messages);
    }

    [Fact]
    public void Identical_game_and_query_ports_fail()
    {
        var r = NetworkPortValidation.Validate(7777, 7777);

        Assert.False(r.CanCreateRules);
        Assert.True(Has(r, "PORT_CONFLICT"));
    }

    [Theory]
    [InlineData(0, 27015)]
    [InlineData(-1, 27015)]
    [InlineData(70000, 27015)]
    [InlineData(7777, 99999)]
    public void Out_of_range_ports_fail(int game, int query)
    {
        var r = NetworkPortValidation.Validate(game, query);

        Assert.False(r.CanCreateRules);
        Assert.Contains(r.Messages, m => m.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Privileged_port_warns_but_does_not_block()
    {
        var r = NetworkPortValidation.Validate(443, 27015);

        // 443 is a well-known service port, so it should be specifically flagged.
        Assert.True(Has(r, "PORT_WELL_KNOWN"));
        // It is still a valid, distinct port number, so rule creation is not blocked
        // on the validity rule itself.
        Assert.True(r.CanCreateRules);
    }

    [Fact]
    public void Low_privileged_port_is_flagged()
    {
        var r = NetworkPortValidation.Validate(900, 27015);

        Assert.True(Has(r, "PORT_PRIVILEGED"));
    }
}
