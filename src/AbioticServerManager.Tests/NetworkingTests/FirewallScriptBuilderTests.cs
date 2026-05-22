using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class FirewallScriptBuilderTests
{
    private static ServerInstance World(int game = 7777, int query = 27015) => new()
    {
        Id = "world0001",
        DisplayName = "Cascade",
        GamePort = game,
        QueryPort = query,
    };

    private static string Create(ServerInstance w, string? exe = @"C:\AF\AbioticFactorServer-Win64-Shipping.exe") =>
        FirewallScriptBuilder.BuildEnsureRulesScript(w, exe, @"C:\Temp\result.json");

    [Fact]
    public void Display_names_match_product_spec()
    {
        Assert.Equal(
            "Facility Overseer - Cascade - Abiotic Factor Game UDP 7777",
            FirewallScriptBuilder.RuleDisplayName(FirewallRuleRole.Game, "Cascade", 7777));
        Assert.Equal(
            "Facility Overseer - Cascade - Abiotic Factor Query UDP 27015",
            FirewallScriptBuilder.RuleDisplayName(FirewallRuleRole.Query, "Cascade", 27015));
        Assert.Equal(
            "Facility Overseer - Cascade - Abiotic Factor Server executable",
            FirewallScriptBuilder.RuleDisplayName(FirewallRuleRole.Program, "Cascade", 0));
    }

    [Fact]
    public void Create_script_uses_udp_profile_any_group_and_enabled()
    {
        var script = Create(World());

        Assert.Contains("-Protocol UDP", script);
        Assert.Contains("-Profile Any", script);
        Assert.Contains("$group = 'Facility Overseer'", script);
        Assert.Contains("-Group $group", script);
        Assert.Contains("-Enabled True", script);
        Assert.Contains("-Direction Inbound", script);
        Assert.Contains("-Action Allow", script);
    }

    [Fact]
    public void Create_script_uses_selected_custom_ports()
    {
        var script = Create(World(28010, 28011));

        Assert.Contains("Facility Overseer - Cascade - Abiotic Factor Game UDP 28010", script);
        Assert.Contains("Facility Overseer - Cascade - Abiotic Factor Query UDP 28011", script);
        Assert.DoesNotContain("UDP 7777", script);
        Assert.DoesNotContain("UDP 27015", script);
    }

    [Fact]
    public void Create_script_is_idempotent_remove_and_recreate_for_selected_world_only()
    {
        var script = Create(World());

        // Repair first finds only app-managed rules for this world/purpose, removes
        // those, and then recreates the three required rules.
        Assert.Contains("Get-NetFirewallRule -Group $group", script);
        Assert.Contains("Is-ManagedForPurpose $_ $purpose $legacyMarker", script);
        Assert.Contains("WorldId=$worldId", script);
        Assert.Contains("Purpose=$purpose", script);
        Assert.Contains("Remove-ManagedRules $purpose $legacyMarker", script);
        Assert.Contains("New-NetFirewallRule", script);
        Assert.DoesNotContain("Set-NetFirewallRule -Name $primary.Name", script);
    }

    [Fact]
    public void Create_script_uses_separate_port_and_program_rule_parameter_sets()
    {
        var script = Create(World());

        Assert.Contains("-Protocol UDP", script);
        Assert.Contains("-LocalPort $port", script);
        Assert.Contains("-Program $programPath", script);

        var programFunctionStart = script.IndexOf("function Repair-ProgramRule", StringComparison.Ordinal);
        var writeResultStart = script.IndexOf("function Write-Result", StringComparison.Ordinal);
        var programFunction = script[programFunctionStart..writeResultStart];

        Assert.Contains("-Program $programPath", programFunction);
        Assert.DoesNotContain("-LocalPort", programFunction);
    }

    [Fact]
    public void Marker_is_per_world_and_per_role()
    {
        var a = FirewallScriptBuilder.Marker("world0001", FirewallRuleRole.Game);
        var b = FirewallScriptBuilder.Marker("world0001", FirewallRuleRole.Query);
        var c = FirewallScriptBuilder.Marker("world0002", FirewallRuleRole.Game);

        Assert.Equal("FOID=world0001;ROLE=game", a);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Descriptions_use_required_world_and_purpose_metadata()
    {
        Assert.Equal(
            "Managed by Facility Overseer. WorldId=world0001; Purpose=GamePort",
            FirewallScriptBuilder.RuleDescription("world0001", FirewallRuleRole.Game));
        Assert.Equal(
            "Managed by Facility Overseer. WorldId=world0001; Purpose=ServerExecutable",
            FirewallScriptBuilder.RuleDescription("world0001", FirewallRuleRole.Program));
    }

    [Fact]
    public void Create_script_skips_program_rule_when_exe_missing()
    {
        var script = Create(World(), exe: null);

        // Both UDP port rules must still be emitted — a missing executable must
        // never block them (the "no firewall rules appear" bug).
        Assert.Contains("Facility Overseer - Cascade - Abiotic Factor Game UDP 7777", script);
        Assert.Contains("Facility Overseer - Cascade - Abiotic Factor Query UDP 27015", script);

        // The program-rule call must be guarded so a null path skips cleanly
        // instead of throwing and failing the whole repair.
        Assert.Contains("if ($programPath -and -not [string]::IsNullOrWhiteSpace", script);
        Assert.Contains("program rule skipped", script);
    }

    [Fact]
    public void Create_script_with_exe_present_invokes_the_program_rule()
    {
        var script = Create(World()); // default exe path

        Assert.Contains("Repair-ProgramRule", script);
        Assert.Contains("Facility Overseer - Cascade - Abiotic Factor Server executable", script);
    }

    [Fact]
    public void Create_script_always_writes_outcome_file()
    {
        var script = Create(World());

        Assert.Contains("function Write-Result", script);
        Assert.Contains(@"C:\Temp\result.json", script);
        Assert.Contains("Write-Result $false", script);
        Assert.Contains("Write-Result $ok", script);
    }

    [Fact]
    public void Inspection_script_checks_udp_profile_and_group()
    {
        var script = FirewallScriptBuilder.BuildInspectionScript(World(), null);

        Assert.Contains("$group = 'Facility Overseer'", script);
        Assert.Contains("Get-NetFirewallRule -Group $group", script);
        Assert.Contains("'UDP'", script);
        Assert.Contains("expected Any", script);
        Assert.Contains("Get-NetUDPEndpoint", script);
        Assert.Contains("netstat -ano -p udp", script);
        Assert.Contains("AbioticFactor*", script);
        Assert.Contains("Similar non-managed inbound allow rule", script);
    }

    [Fact]
    public void Inspection_script_parenthesizes_function_calls_in_boolean_expressions()
    {
        var script = FirewallScriptBuilder.BuildInspectionScript(World(), null);

        Assert.Contains(
            "if ([string]$rule.Group -eq $group -and (Contains-Text $rule.Description $worldId))",
            script);
        Assert.DoesNotContain("-and Contains-Text", script);
    }

    [Fact]
    public void Inspection_script_does_not_open_anything()
    {
        var script = FirewallScriptBuilder.BuildInspectionScript(World(), null);

        Assert.DoesNotContain("New-NetFirewallRule", script);
        Assert.DoesNotContain("Set-NetFirewallRule", script);
        Assert.DoesNotContain("Remove-NetFirewallRule", script);
    }
}
