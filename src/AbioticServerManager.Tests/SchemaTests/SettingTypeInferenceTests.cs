using AbioticServerManager.Core.Schema;

namespace AbioticServerManager.Tests.SchemaTests;

public class SettingTypeInferenceTests
{
    [Theory]
    [InlineData("True", SettingValueType.Boolean)]
    [InlineData("false", SettingValueType.Boolean)]
    [InlineData("FALSE", SettingValueType.Boolean)]
    [InlineData("42", SettingValueType.Number)]
    [InlineData("-7", SettingValueType.Number)]
    [InlineData("1.5", SettingValueType.Number)]
    [InlineData("0.0", SettingValueType.Number)]
    [InlineData("Normal", SettingValueType.Text)]
    [InlineData("", SettingValueType.Text)]
    [InlineData("C:\\path\\thing", SettingValueType.Text)]
    public void Infers_expected_type(string value, SettingValueType expected) =>
        Assert.Equal(expected, SettingTypeInference.Infer(value));

    [Fact]
    public void Unknown_numeric_descriptor_still_renders_as_slider()
    {
        // Even without metadata bounds, numeric settings get a slider; the view model
        // infers a sensible range from the value itself.
        var d = new SettingDescriptor { Section = "SandboxSettings", Key = "X", Value = "5" };
        Assert.Equal(SettingControlType.Slider, d.ControlType);
        Assert.Equal(SettingSource.InferredFallback, d.Source);
    }

    [Fact]
    public void Known_ranged_numeric_descriptor_renders_as_slider()
    {
        var d = new SettingDescriptor
        {
            Section = "SandboxSettings",
            Key = "EnemySpawnRate",
            Value = "1.0",
            Metadata = new SettingMetadata
            {
                Section = "SandboxSettings",
                Key = "EnemySpawnRate",
                DisplayName = "Enemy Spawn Rate",
                Category = "Enemy",
                Type = SettingValueType.Number,
                Min = 0,
                Max = 3,
            },
        };

        Assert.Equal(SettingControlType.Slider, d.ControlType);
        Assert.Equal(SettingSource.BuiltInMetadata, d.Source);
    }
}
