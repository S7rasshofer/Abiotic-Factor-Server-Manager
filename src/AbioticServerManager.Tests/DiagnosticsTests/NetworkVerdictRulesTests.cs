using AbioticServerManager.Core.Diagnostics;

namespace AbioticServerManager.Tests.DiagnosticsTests;

public class NetworkVerdictRulesTests
{
    private static NetworkVerdictInputs Inputs(
        bool processRunning = true,
        bool a2sResponded = false,
        bool gameBound = false,
        bool queryBound = false,
        bool lobbyCode = false,
        bool isLanOnly = false) =>
        new()
        {
            ServerProcessRunning = processRunning,
            A2SLocalResponded = a2sResponded,
            GamePortBound = gameBound,
            QueryPortBound = queryBound,
            LobbyCodePublished = lobbyCode,
            IsLanOnly = isLanOnly,
        };

    [Fact]
    public void Stopped_when_process_not_running()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(processRunning: false));
        Assert.Equal(NetworkVerdictStatus.Stopped, result.Status);
        Assert.Contains("stopped", result.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reachable_when_a2s_responded()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(a2sResponded: true));
        Assert.Equal(NetworkVerdictStatus.Reachable, result.Status);
        Assert.Contains("A2S", result.Detail);
    }

    [Fact]
    public void Reachable_when_lobby_code_published_even_without_a2s_reply()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(lobbyCode: true));
        Assert.Equal(NetworkVerdictStatus.Reachable, result.Status);
        Assert.Contains("lobby code", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reachable_headline_is_lan_specific_for_lan_only_world()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(a2sResponded: true, isLanOnly: true));
        Assert.Equal(NetworkVerdictStatus.Reachable, result.Status);
        Assert.Contains("LAN", result.Headline);
    }

    [Fact]
    public void BindingOrWarming_when_ports_bound_but_no_reply_yet()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(gameBound: true, queryBound: true));
        Assert.Equal(NetworkVerdictStatus.BindingOrWarming, result.Status);
        Assert.Contains("warming", result.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unreachable_when_running_but_neither_port_bound()
    {
        var result = NetworkVerdictRules.Evaluate(Inputs(gameBound: false, queryBound: false));
        Assert.Equal(NetworkVerdictStatus.Unreachable, result.Status);
        Assert.Contains("Neither", result.Detail);
    }

    [Fact]
    public void Unreachable_call_out_specific_missing_port()
    {
        var gameMissing = NetworkVerdictRules.Evaluate(Inputs(gameBound: false, queryBound: true));
        Assert.Equal(NetworkVerdictStatus.Unreachable, gameMissing.Status);
        Assert.Contains("game UDP port is not bound", gameMissing.Detail);

        var queryMissing = NetworkVerdictRules.Evaluate(Inputs(gameBound: true, queryBound: false));
        Assert.Equal(NetworkVerdictStatus.Unreachable, queryMissing.Status);
        Assert.Contains("query UDP port is not bound", queryMissing.Detail);
    }

    [Fact]
    public void Reachable_takes_priority_over_unreachable_when_a2s_replies_without_port_inspection_data()
    {
        // The local A2S probe runs independently of the firewall inspection
        // (which is what produces the PortBindings list). If A2S responded
        // but the PortBindings list was empty (e.g. inspection failed), we
        // still trust the A2S reply - it's empirical reachability.
        var result = NetworkVerdictRules.Evaluate(
            Inputs(a2sResponded: true, gameBound: false, queryBound: false));
        Assert.Equal(NetworkVerdictStatus.Reachable, result.Status);
    }
}
