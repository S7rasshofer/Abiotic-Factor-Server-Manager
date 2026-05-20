using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class HealthIndicatorTests
{
    [Theory]
    [InlineData(ServerHealth.Stopped, HealthIndicator.Grey)]
    [InlineData(ServerHealth.Starting, HealthIndicator.Yellow)]
    [InlineData(ServerHealth.Online, HealthIndicator.Green)]
    [InlineData(ServerHealth.Blocked, HealthIndicator.Red)]
    [InlineData(ServerHealth.Crashed, HealthIndicator.Red)]
    public void Maps_health_to_semantic_color(ServerHealth health, HealthIndicator expected) =>
        Assert.Equal(expected, HealthIndicators.For(health));

    [Fact]
    public void Every_health_value_is_explicitly_mapped()
    {
        // Catches the case where a new ServerHealth enum value is added but the
        // indicator switch isn't updated — the default fallback to Grey would
        // otherwise silently misrepresent the new state.
        foreach (ServerHealth health in Enum.GetValues<ServerHealth>())
        {
            var indicator = HealthIndicators.For(health);
            if (health is ServerHealth.Stopped)
            {
                Assert.Equal(HealthIndicator.Grey, indicator);
            }
            else
            {
                Assert.NotEqual(HealthIndicator.Grey, indicator);
            }
        }
    }

    [Fact]
    public void Blocked_is_red_not_green()
    {
        // The original bug we are correcting: a briefly-running corrupt world
        // (process up, Health = Blocked) used to render a green dot because the
        // dot was bound to IsRunningState. Pin the correct mapping forever.
        Assert.Equal(HealthIndicator.Red, HealthIndicators.For(ServerHealth.Blocked));
        Assert.NotEqual(HealthIndicator.Green, HealthIndicators.For(ServerHealth.Blocked));
    }
}
