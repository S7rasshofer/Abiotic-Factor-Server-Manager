using AbioticServerManager.Core.Schema;

namespace AbioticServerManager.Tests.SchemaTests;

public class SandboxCategoryHeadingTests
{
    [Theory]
    [InlineData("; === WORLD ===", "World")]
    [InlineData("; === ENEMY ===", "Enemy")]
    [InlineData("; === PLAYER ===", "Player")]
    [InlineData(";=== Items ===", "Items")]
    [InlineData("# --- Survival ---", "Survival")]
    [InlineData(";  ====  Game Difficulty  ====", "Game Difficulty")]
    public void Parses_banner_headings(string raw, string expected)
    {
        Assert.True(SandboxCategoryHeading.TryParse(raw, out var category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("; Generated sandbox settings")]
    [InlineData("; future key the app has never heard of")]
    [InlineData("EnemySpawnRate=1.0")]
    [InlineData("; ===")]
    [InlineData("")]
    public void Ignores_non_heading_comments(string raw) =>
        Assert.False(SandboxCategoryHeading.TryParse(raw, out _));
}
