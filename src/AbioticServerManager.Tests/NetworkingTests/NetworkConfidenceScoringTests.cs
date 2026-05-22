using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class NetworkConfidenceScoringTests
{
    [Fact]
    public void Nothing_configured_scores_zero_and_lists_all_lifts()
    {
        var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs());

        Assert.Equal(0, r.Score);
        Assert.Equal("None", r.Band);
        Assert.Empty(r.Strengths);
        Assert.NotEmpty(r.Lifts);
        Assert.Contains(r.Lifts, l => l.Contains("network", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Lifts, l => l.Contains("firewall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lan_only_with_lan_ip_and_firewall_and_a2s_is_at_least_OK()
    {
        var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
        {
            HasLanIpv4 = true,
            FirewallRulesConfigured = true,
            A2SLocalResponded = true,
            IsLanOnly = true,
        });

        // 20 + 25 + 25 = 70 → Good. Public-IP factors are intentionally skipped.
        Assert.Equal(70, r.Score);
        Assert.Equal("Good", r.Band);
        Assert.Contains(r.Strengths, s => s.Contains("LAN-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fully_configured_internet_host_scores_100()
    {
        var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
        {
            HasLanIpv4 = true,
            FirewallRulesConfigured = true,
            A2SLocalResponded = true,
            HasPublicIpv4 = true,
            LooksLikeCgnat = false,
            IsLanOnly = false,
        });

        Assert.Equal(100, r.Score);
        Assert.Equal("Great", r.Band);
        Assert.Contains(r.Strengths, s => s.Contains("Public IP appears reachable"));
        // Even at 100 we surface a "verify friends can connect" lift so the
        // user doesn't read the score as proof.
        Assert.NotEmpty(r.Lifts);
    }

    [Fact]
    public void Cgnat_caps_score_at_low_band_no_matter_what()
    {
        var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
        {
            HasLanIpv4 = true,
            FirewallRulesConfigured = true,
            A2SLocalResponded = true,
            HasPublicIpv4 = true,
            LooksLikeCgnat = true,
        });

        Assert.True(r.Score <= 35,
            $"CGNAT must cap below the OK band; got {r.Score}");
        Assert.Contains(r.Lifts, l => l.Contains("Carrier-Grade NAT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Lifts, l => l.Contains("ISP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_is_always_in_0_to_100()
    {
        foreach (var hasLan in new[] { false, true })
        foreach (var hasFw in new[] { false, true })
        foreach (var hasA2s in new[] { false, true })
        foreach (var hasPub in new[] { false, true })
        foreach (var cgnat in new[] { false, true })
        foreach (var lan in new[] { false, true })
        {
            var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
            {
                HasLanIpv4 = hasLan,
                FirewallRulesConfigured = hasFw,
                A2SLocalResponded = hasA2s,
                HasPublicIpv4 = hasPub,
                LooksLikeCgnat = cgnat,
                IsLanOnly = lan,
            });

            Assert.InRange(r.Score, 0, 100);
            Assert.False(string.IsNullOrEmpty(r.Band));
        }
    }

    [Fact]
    public void Missing_public_ip_when_not_lan_only_adds_to_lifts_but_does_not_zero()
    {
        var r = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
        {
            HasLanIpv4 = true,
            FirewallRulesConfigured = true,
            A2SLocalResponded = true,
            HasPublicIpv4 = false,
            IsLanOnly = false,
        });

        Assert.Equal(70, r.Score);
        Assert.Contains(r.Lifts, l => l.Contains("Public IP", StringComparison.OrdinalIgnoreCase));
    }
}
