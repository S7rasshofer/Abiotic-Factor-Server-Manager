using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.DiagnosticsTests;

public class RecommendedActionsTests
{
    private static RecommendedActionInputs Defaults() => new()
    {
        InstallKind = ServerInstallKind.SteamCmdManaged,
        Health = ServerHealth.Stopped,
        FirewallRulesConfigured = true,
        HasLanIpv4 = true,
        IsLanOnly = false,
        WorldsExist = true,
        HasBlockerFindings = false,
    };

    [Fact]
    public void Returns_at_most_three_actions()
    {
        var inputs = Defaults() with
        {
            InstallKind = ServerInstallKind.Missing,
            HasBlockerFindings = true,
            Health = ServerHealth.Crashed,
            FirewallRulesConfigured = false,
            HasLanIpv4 = false,
            WorldsExist = false,
        };

        var actions = RecommendedActions.Build(inputs);

        Assert.True(actions.Count <= RecommendedActions.MaxResults);
        Assert.Equal(3, actions.Count);
    }

    [Fact]
    public void Missing_install_recommends_prepare_server_at_high_priority()
    {
        var inputs = Defaults() with { InstallKind = ServerInstallKind.Missing };
        var actions = RecommendedActions.Build(inputs);

        var prepare = actions.SingleOrDefault(a => a.Id == "PREPARE_SERVER");
        Assert.NotNull(prepare);
        Assert.Equal(RecommendedActionPriority.High, prepare!.Priority);
        Assert.Equal("InstallOrUpdateServerCommand", prepare.CommandHint);
    }

    [Fact]
    public void Blocked_health_surfaces_handle_blocked_action()
    {
        var inputs = Defaults() with { Health = ServerHealth.Blocked };
        var actions = RecommendedActions.Build(inputs);

        Assert.Contains(actions, a => a.Id == "HANDLE_BLOCKED");
    }

    [Fact]
    public void Crashed_health_recommends_restart_with_command()
    {
        var inputs = Defaults() with { Health = ServerHealth.Crashed };
        var actions = RecommendedActions.Build(inputs);

        var restart = actions.Single(a => a.Id == "RESTART_AFTER_CRASH");
        Assert.Equal("RestartServerCommand", restart.CommandHint);
    }

    [Fact]
    public void Lan_only_world_does_not_demand_firewall_rules()
    {
        var inputs = Defaults() with
        {
            FirewallRulesConfigured = false,
            IsLanOnly = true,
        };

        var actions = RecommendedActions.Build(inputs);

        Assert.DoesNotContain(actions, a => a.Id == "CREATE_FIREWALL_RULES");
    }

    [Fact]
    public void Missing_firewall_on_internet_world_recommends_creation()
    {
        var inputs = Defaults() with { FirewallRulesConfigured = false };
        var actions = RecommendedActions.Build(inputs);

        var fw = actions.Single(a => a.Id == "CREATE_FIREWALL_RULES");
        Assert.Equal("CreateFirewallRulesCommand", fw.CommandHint);
    }

    [Fact]
    public void No_worlds_recommends_create_world_command()
    {
        var inputs = Defaults() with { WorldsExist = false };
        var actions = RecommendedActions.Build(inputs);

        Assert.Contains(actions, a =>
            a.Id == "CREATE_WORLD" && a.CommandHint == "CreateWorldCommand");
    }

    [Fact]
    public void Online_and_nothing_else_pending_suggests_verifying_friends_can_join()
    {
        var inputs = Defaults() with { Health = ServerHealth.Online };
        var actions = RecommendedActions.Build(inputs);

        Assert.Contains(actions, a => a.Id == "VERIFY_FRIENDS_CAN_JOIN");
    }

    [Fact]
    public void High_priority_items_sort_before_lower_priority()
    {
        var inputs = Defaults() with
        {
            InstallKind = ServerInstallKind.Missing,
            WorldsExist = false,
            HasBlockerFindings = false,
        };

        var actions = RecommendedActions.Build(inputs);

        // PREPARE_SERVER (High) must come before CREATE_WORLD (Medium).
        var iPrepare = actions.ToList().FindIndex(a => a.Id == "PREPARE_SERVER");
        var iWorld = actions.ToList().FindIndex(a => a.Id == "CREATE_WORLD");
        Assert.True(iPrepare >= 0);
        Assert.True(iWorld >= 0);
        Assert.True(iPrepare < iWorld);
    }

    [Fact]
    public void Healthy_steady_state_returns_minimal_or_no_actions()
    {
        var actions = RecommendedActions.Build(Defaults());

        // No worlds-running prompt, no blockers, no errors → either empty or
        // just informational. Either way, never more than MaxResults.
        Assert.True(actions.Count <= RecommendedActions.MaxResults);
    }
}
