namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Inputs to <see cref="NetworkConfidenceScoring"/>. All facts the App layer
/// has collected from <see cref="INetworkSetupService"/> + the diagnostics
/// service, reduced to value-typed flags so the scoring is unit-testable
/// without IO.
/// </summary>
public sealed record NetworkConfidenceInputs
{
    /// <summary>True when the LAN IPv4 was detected (any non-null Best).</summary>
    public bool HasLanIpv4 { get; init; }

    /// <summary>True when both UDP rules (game + query) are present in Windows Firewall.</summary>
    public bool FirewallRulesConfigured { get; init; }

    /// <summary>True when the local A2S query responded.</summary>
    public bool A2SLocalResponded { get; init; }

    /// <summary>True when a public IPv4 was discovered (probe succeeded).</summary>
    public bool HasPublicIpv4 { get; init; }

    /// <summary>
    /// True when the host's WAN address looks like CGNAT space (100.64.0.0/10).
    /// CGNAT means the user cannot host without help from their ISP regardless
    /// of router config, so it suppresses the score.
    /// </summary>
    public bool LooksLikeCgnat { get; init; }

    /// <summary>True when the host is on a LAN-only world (LANOnly flag set).</summary>
    public bool IsLanOnly { get; init; }
}

/// <summary>
/// A score plus the reasons that pushed it up or down. UI surfaces the lift
/// list as actionable "do this to improve" hints.
/// </summary>
public sealed record NetworkConfidenceResult
{
    public required int Score { get; init; }              // 0..100
    public required string Band { get; init; }            // None / Low / OK / Good / Great
    public required IReadOnlyList<string> Strengths { get; init; }
    public required IReadOnlyList<string> Lifts { get; init; } // what would raise the score
}

public static class NetworkConfidenceScoring
{
    public static NetworkConfidenceResult Score(NetworkConfidenceInputs inputs)
    {
        var strengths = new List<string>();
        var lifts = new List<string>();
        int score = 0;

        if (inputs.HasLanIpv4)
        {
            score += 20;
            strengths.Add("LAN IPv4 detected");
        }
        else
        {
            lifts.Add("Connect this PC to your network (Ethernet or Wi-Fi).");
        }

        if (inputs.FirewallRulesConfigured)
        {
            score += 25;
            strengths.Add("Windows Firewall rules configured");
        }
        else
        {
            lifts.Add("Create Windows Firewall rules on the Network tab (one click).");
        }

        if (inputs.A2SLocalResponded)
        {
            score += 25;
            strengths.Add("Local A2S query responded — game/query ports are bound");
        }
        else
        {
            lifts.Add("Start the server so the game/query ports bind and A2S responds.");
        }

        // LAN-only worlds intentionally do not need the internet; cap there and
        // call out the LAN-only mode so the score isn't misread as "broken".
        if (inputs.IsLanOnly)
        {
            return new NetworkConfidenceResult
            {
                Score = score,
                Band = Bandify(score),
                Strengths = [.. strengths, "LAN-only mode: public reachability not required"],
                Lifts = lifts,
            };
        }

        if (inputs.HasPublicIpv4)
        {
            score += 20;
            strengths.Add("Public IPv4 discovered");
        }
        else
        {
            lifts.Add("Public IP could not be detected — check your internet connection.");
        }

        if (inputs.LooksLikeCgnat)
        {
            // CGNAT alone caps you at "Low" — the only fix is contacting the ISP.
            score = Math.Min(score, 35);
            lifts.Add(
                "Your public IP looks like Carrier-Grade NAT (100.64.0.0/10). Friends " +
                "on the internet cannot reach this PC even with port forwarding; ask " +
                "your ISP for a real public IP.");
        }
        else if (inputs.HasPublicIpv4)
        {
            score += 10;
            strengths.Add("Public IP appears reachable (not CGNAT)");
        }

        score = Math.Clamp(score, 0, 100);

        if (lifts.Count == 0)
        {
            lifts.Add("Everything is configured. Verify friends can actually connect.");
        }

        return new NetworkConfidenceResult
        {
            Score = score,
            Band = Bandify(score),
            Strengths = strengths,
            Lifts = lifts,
        };
    }

    private static string Bandify(int score) => score switch
    {
        >= 90 => "Great",
        >= 70 => "Good",
        >= 40 => "OK",
        >= 10 => "Low",
        _ => "None",
    };
}
