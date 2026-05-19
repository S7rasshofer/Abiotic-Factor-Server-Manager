using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class FirewallInspectionParserTests
{
    [Fact]
    public void Parses_roles_environment_and_ports()
    {
        const string json = """
        {
          "Roles": [
            {"Role":"game","Exists":true,"IsCorrect":true,"DisplayName":"Facility Overseer - Abiotic Factor Game UDP 7777","Problems":[]},
            {"Role":"query","Exists":true,"IsCorrect":false,"DisplayName":"x","Problems":["Protocol is 'TCP', expected UDP."]},
            {"Role":"program","Exists":false,"IsCorrect":false,"DisplayName":"","Problems":["No Facility Overseer rule found for this world."]}
          ],
          "Environment": {"IsElevated":true,"NetworkProfile":"Private","ServerProcessRunning":true,"ServerProcessNames":["AbioticFactorServer-Win64-Shipping"]},
          "Ports": [
            {"Port":7777,"Role":"game","IsListening":true,"OwningPids":[1234],"OwningProcesses":["AbioticFactorServer-Win64-Shipping"]},
            {"Port":27015,"Role":"query","IsListening":true,"OwningPids":[9],"OwningProcesses":["nginx"]}
          ]
        }
        """;

        var result = FirewallInspectionParser.ParseInspection(json);

        Assert.Equal(3, result.Roles.Count);
        Assert.True(result.Roles.Single(r => r.Role == FirewallRuleRole.Game).IsCorrect);
        Assert.False(result.Roles.Single(r => r.Role == FirewallRuleRole.Query).IsCorrect);
        Assert.Contains(
            "Protocol is 'TCP'",
            result.Roles.Single(r => r.Role == FirewallRuleRole.Query).Problems[0]);

        Assert.True(result.Environment.IsElevated);
        Assert.Equal("Private", result.Environment.NetworkProfile);
        Assert.True(result.Environment.ServerProcessRunning);

        var game = result.Ports.Single(p => p.Port == 7777);
        Assert.True(game.IsListening);
        Assert.False(game.ForeignOwner);

        var query = result.Ports.Single(p => p.Port == 27015);
        Assert.True(query.ForeignOwner); // nginx is not the dedicated server
    }

    [Fact]
    public void Tolerates_powershell_5_single_element_scalar_unwrap()
    {
        // Windows PowerShell 5.1 serializes a one-item array as a scalar.
        const string json = """
        {
          "Roles":[{"Role":"game","Exists":true,"IsCorrect":false,"DisplayName":"x","Problems":"Rule is disabled."}],
          "Environment":{"IsElevated":false,"NetworkProfile":"Public","ServerProcessRunning":true,"ServerProcessNames":"AbioticFactorServer-Win64-Shipping"},
          "Ports":[{"Port":7777,"Role":"game","IsListening":true,"OwningPids":4242,"OwningProcesses":"AbioticFactorServer-Win64-Shipping"}]
        }
        """;

        var result = FirewallInspectionParser.ParseInspection(json);

        Assert.Equal(["Rule is disabled."], result.Roles[0].Problems);
        Assert.Equal(["AbioticFactorServer-Win64-Shipping"], result.Environment.ServerProcessNames);
        Assert.Equal([4242], result.Ports[0].OwningPids);
        Assert.False(result.Ports[0].ForeignOwner);
    }

    [Fact]
    public void Empty_or_garbage_inspection_is_safe()
    {
        Assert.Empty(FirewallInspectionParser.ParseInspection("").Roles);
        Assert.Empty(FirewallInspectionParser.ParseInspection("   ").Ports);
    }

    [Fact]
    public void Parses_apply_outcome()
    {
        const string json = """
        {"Success":false,"RulesCreated":2,"RulesUpdated":0,"StaleRulesRemoved":1,
         "Errors":["Server executable not found"],"RawLog":"CREATE x\nREMOVE stale y"}
        """;

        var outcome = FirewallInspectionParser.ParseOutcome(json);

        Assert.False(outcome.Success);
        Assert.Equal(2, outcome.RulesCreated);
        Assert.Equal(1, outcome.StaleRulesRemoved);
        Assert.Single(outcome.Errors);
        Assert.Contains("CREATE x", outcome.RawLog);
    }

    [Fact]
    public void Missing_outcome_file_reports_failure()
    {
        var outcome = FirewallInspectionParser.ParseOutcome("");

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Errors);
    }

    [Theory]
    [InlineData("AbioticFactorServer-Win64-Shipping", true)]
    [InlineData("abioticfactor", true)]
    [InlineData("nginx", false)]
    [InlineData("steam", false)]
    public void Recognises_server_process(string name, bool expected) =>
        Assert.Equal(expected, FirewallInspectionParser.IsServerProcess(name));
}
