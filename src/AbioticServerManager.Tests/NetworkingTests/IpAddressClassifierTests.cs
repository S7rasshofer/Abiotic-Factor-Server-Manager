using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class IpAddressClassifierTests
{
    [Theory]
    [InlineData("127.0.0.1", Ipv4Scope.Loopback)]
    [InlineData("127.5.5.5", Ipv4Scope.Loopback)]
    [InlineData("169.254.10.20", Ipv4Scope.LinkLocalApipa)]
    [InlineData("10.0.0.5", Ipv4Scope.PrivateRfc1918)]
    [InlineData("172.16.4.9", Ipv4Scope.PrivateRfc1918)]
    [InlineData("172.31.255.254", Ipv4Scope.PrivateRfc1918)]
    [InlineData("192.168.1.100", Ipv4Scope.PrivateRfc1918)]
    [InlineData("100.64.0.1", Ipv4Scope.CarrierGradeNat)]
    [InlineData("100.127.255.255", Ipv4Scope.CarrierGradeNat)]
    [InlineData("8.8.8.8", Ipv4Scope.Public)]
    [InlineData("172.32.0.1", Ipv4Scope.Public)]
    [InlineData("100.128.0.1", Ipv4Scope.Public)]
    [InlineData("not-an-ip", Ipv4Scope.Invalid)]
    public void Classifies_ipv4_ranges(string ip, Ipv4Scope expected) =>
        Assert.Equal(expected, IpAddressClassifier.Classify(ip));

    [Fact]
    public void Cgnat_and_rfc1918_flagged_as_unforwardable()
    {
        Assert.True(IpAddressClassifier.IsPrivateOrCarrierGrade(Ipv4Scope.CarrierGradeNat));
        Assert.True(IpAddressClassifier.IsPrivateOrCarrierGrade(Ipv4Scope.PrivateRfc1918));
        Assert.False(IpAddressClassifier.IsPrivateOrCarrierGrade(Ipv4Scope.Public));
    }

    [Fact]
    public void Only_routable_scopes_are_usable_lan_targets()
    {
        Assert.True(IpAddressClassifier.IsUsableLanScope(Ipv4Scope.PrivateRfc1918));
        Assert.True(IpAddressClassifier.IsUsableLanScope(Ipv4Scope.Public));
        Assert.False(IpAddressClassifier.IsUsableLanScope(Ipv4Scope.Loopback));
        Assert.False(IpAddressClassifier.IsUsableLanScope(Ipv4Scope.LinkLocalApipa));
    }
}
