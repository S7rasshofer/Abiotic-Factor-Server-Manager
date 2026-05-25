namespace AbioticServerManager.Core.Schema;

public enum SettingValueType
{
    Boolean,
    Number,
    Enum,
    Text,
}

/// <summary>The WPF control the dynamic renderer should use for a setting.</summary>
public enum SettingControlType
{
    Toggle,
    Slider,
    Number,
    Dropdown,
    Text,
}

/// <summary>
/// Optional, enrichment-only description of a known setting. The plan is explicit that
/// this is an overlay, never the source of truth: live INI values always win and an
/// absent entry must still render via inference.
/// </summary>
public sealed record SettingMetadata
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required SettingValueType Type { get; init; }

    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Default { get; init; }
    public string? Description { get; init; }
    public string? Warning { get; init; }

    /// <summary>Canonical values written to the INI (loss-less). e.g. "0", "1", "2".</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Human-readable label per option, index-aligned with <see cref="Options"/>. Lets the
    /// dropdown show "Apocalyptic" while still writing "3", instead of dumping the
    /// "1=Normal, 2=Hard..." legend into the description.
    /// </summary>
    public IReadOnlyList<string> OptionLabels { get; init; } = [];
}
