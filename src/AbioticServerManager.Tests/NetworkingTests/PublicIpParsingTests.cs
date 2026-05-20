using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class PublicIpParsingTests
{
    [Theory]
    [InlineData("203.0.113.42", "203.0.113.42")]
    [InlineData("  192.168.1.1\n", "192.168.1.1")]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("0.0.0.0", "0.0.0.0")]
    [InlineData("255.255.255.255", "255.255.255.255")]
    public void Accepts_valid_ipv4(string input, string expected) =>
        Assert.Equal(expected, PublicIpParsing.TryParseIpv4(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("203.0.113")]            // too few octets
    [InlineData("203.0.113.42.99")]      // too many octets
    [InlineData("203.0.113.256")]        // octet > 255
    [InlineData("203.0.-1.42")]          // negative octet
    [InlineData("203.0.x.42")]           // non-numeric
    [InlineData("<html>error</html>")]   // HTML response
    [InlineData("203.0.113.42 extra")]   // trailing garbage
    public void Rejects_invalid_or_garbage(string? input) =>
        Assert.Null(PublicIpParsing.TryParseIpv4(input));
}
