using System.Net;
using System.Net.Sockets;

namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Where an IPv4 address sits in the address space. Used both to pick a sane LAN
/// address and to warn about CGNAT / double NAT when port forwarding cannot work.
/// </summary>
public enum Ipv4Scope
{
    /// <summary>Not a parseable IPv4 address.</summary>
    Invalid,

    /// <summary>127.0.0.0/8 loopback - never a LAN address.</summary>
    Loopback,

    /// <summary>169.254.0.0/16 APIPA link-local - means "no DHCP", unusable.</summary>
    LinkLocalApipa,

    /// <summary>RFC1918 private (10/8, 172.16/12, 192.168/16) - a normal LAN address.</summary>
    PrivateRfc1918,

    /// <summary>100.64.0.0/10 carrier-grade NAT - a strong CGNAT / double-NAT signal.</summary>
    CarrierGradeNat,

    /// <summary>A globally routable public address.</summary>
    Public,
}

/// <summary>
/// Pure (no-IO) classification of IPv4 addresses. Kept in Core so the CGNAT and
/// LAN-selection rules are unit-testable without touching real adapters.
/// </summary>
public static class IpAddressClassifier
{
    public static Ipv4Scope Classify(string? address) =>
        IPAddress.TryParse(address, out var parsed) ? Classify(parsed) : Ipv4Scope.Invalid;

    public static Ipv4Scope Classify(IPAddress? address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return Ipv4Scope.Invalid;
        }

        var b = address.GetAddressBytes();

        if (b[0] == 127)
        {
            return Ipv4Scope.Loopback;
        }

        if (b[0] == 169 && b[1] == 254)
        {
            return Ipv4Scope.LinkLocalApipa;
        }

        // 100.64.0.0/10 - carrier-grade NAT (RFC 6598).
        if (b[0] == 100 && b[1] is >= 64 and <= 127)
        {
            return Ipv4Scope.CarrierGradeNat;
        }

        if (b[0] == 10 ||
            (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168))
        {
            return Ipv4Scope.PrivateRfc1918;
        }

        return Ipv4Scope.Public;
    }

    /// <summary>True for ranges where a router port-forward typically cannot expose a host.</summary>
    public static bool IsPrivateOrCarrierGrade(Ipv4Scope scope) =>
        scope is Ipv4Scope.PrivateRfc1918 or Ipv4Scope.CarrierGradeNat;

    /// <summary>True for an address that can legitimately be a LAN target.</summary>
    public static bool IsUsableLanScope(Ipv4Scope scope) =>
        scope is Ipv4Scope.PrivateRfc1918 or Ipv4Scope.CarrierGradeNat or Ipv4Scope.Public;

    public static string Describe(Ipv4Scope scope) => scope switch
    {
        Ipv4Scope.Loopback => "loopback (127.x)",
        Ipv4Scope.LinkLocalApipa => "APIPA link-local (169.254.x - no DHCP lease)",
        Ipv4Scope.PrivateRfc1918 => "private LAN (RFC1918)",
        Ipv4Scope.CarrierGradeNat => "carrier-grade NAT (100.64.0.0/10)",
        Ipv4Scope.Public => "public",
        _ => "invalid",
    };
}
