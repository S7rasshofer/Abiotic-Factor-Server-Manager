namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Probes a small, well-known public endpoint to discover this host's external
/// IPv4 address. Pure interface so Core stays IO-free; the HTTP implementation
/// lives in Infrastructure.
/// </summary>
public interface IPublicIpProbe
{
    /// <summary>
    /// Returns the host's public IPv4 as a string (e.g. "203.0.113.42"), or
    /// null on any failure (timeout, network down, parse mismatch). Never
    /// throws — public IP is best-effort UX, not a critical path.
    /// </summary>
    Task<string?> ProbeAsync(CancellationToken ct = default);
}

/// <summary>
/// Pure helpers for parsing/validating a plaintext IPv4 string returned by a
/// public-IP endpoint. Stays in Core so the parsing rules are unit-testable
/// without HTTP.
/// </summary>
public static class PublicIpParsing
{
    /// <summary>
    /// Trims whitespace, rejects anything that doesn't look like a dotted-quad
    /// IPv4 in 0–255 octets, and returns the canonical string or null.
    /// </summary>
    public static string? TryParseIpv4(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        var canonical = new string[4];
        for (var i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out var octet))
            {
                return null;
            }

            canonical[i] = octet.ToString();
        }

        return string.Join('.', canonical);
    }
}
