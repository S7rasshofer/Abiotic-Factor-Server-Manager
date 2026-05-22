using AbioticServerManager.Core.Diagnostics;

namespace AbioticServerManager.Tests.DiagnosticsTests;

public class RecoveryFlowsTests
{
    [Fact]
    public void Catalog_contains_the_known_flows()
    {
        var ids = RecoveryFlows.All.Select(f => f.Id).ToList();

        Assert.Contains("CORRUPT_WORLD", ids);
        Assert.Contains("PORT_CONFLICT", ids);
        Assert.Contains("MISSING_EXECUTABLE", ids);
        Assert.Contains("BROKEN_STEAMCMD", ids);
    }

    [Fact]
    public void Every_flow_has_a_title_summary_and_at_least_one_step()
    {
        foreach (var flow in RecoveryFlows.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(flow.Title), $"Flow {flow.Id} missing title");
            Assert.False(string.IsNullOrWhiteSpace(flow.Summary), $"Flow {flow.Id} missing summary");
            Assert.False(string.IsNullOrWhiteSpace(flow.TriggerTag), $"Flow {flow.Id} missing trigger");
            Assert.NotEmpty(flow.Steps);
        }
    }

    [Fact]
    public void Steps_have_sequential_order_starting_at_one()
    {
        foreach (var flow in RecoveryFlows.All)
        {
            var expected = 1;
            foreach (var step in flow.Steps)
            {
                Assert.Equal(expected++, step.Order);
                Assert.False(string.IsNullOrWhiteSpace(step.Title));
                Assert.False(string.IsNullOrWhiteSpace(step.Detail));
            }
        }
    }

    [Fact]
    public void Steps_with_action_hints_also_carry_a_label()
    {
        foreach (var flow in RecoveryFlows.All)
        {
            foreach (var step in flow.Steps)
            {
                if (!string.IsNullOrEmpty(step.ActionHint))
                {
                    Assert.False(string.IsNullOrWhiteSpace(step.ActionLabel),
                        $"Flow {flow.Id} step {step.Order} has ActionHint but no ActionLabel");
                }
            }
        }
    }

    [Fact]
    public void Corrupt_world_flow_marks_the_create_fresh_step_as_destructive()
    {
        var step = RecoveryFlows.CorruptWorld.Steps
            .Single(s => s.ActionHint == "CreateFreshWorldCommand");

        Assert.True(step.IsDestructive,
            "Creating a fresh world quarantines the old save and must be marked destructive.");
    }

    [Fact]
    public void Corrupt_world_flow_recommends_backup_restore_before_destructive_step()
    {
        var steps = RecoveryFlows.CorruptWorld.Steps;
        var restore = steps.First(s => s.Detail.Contains("backup", StringComparison.OrdinalIgnoreCase));
        var fresh = steps.First(s => s.IsDestructive);

        Assert.True(restore.Order < fresh.Order,
            "Backup restore must be offered before the destructive create-fresh step.");
    }

    [Theory]
    [InlineData("world.corrupt", "CORRUPT_WORLD")]
    [InlineData("port.bind_fail", "PORT_CONFLICT")]
    [InlineData("exe.missing", "MISSING_EXECUTABLE")]
    [InlineData("steamcmd.broken", "BROKEN_STEAMCMD")]
    public void For_tag_returns_matching_flow(string tag, string expectedId)
    {
        var flow = RecoveryFlows.ForTag(tag);
        Assert.NotNull(flow);
        Assert.Equal(expectedId, flow!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonexistent.tag")]
    public void For_tag_returns_null_for_unknown_or_blank(string? tag)
    {
        Assert.Null(RecoveryFlows.ForTag(tag));
    }
}
