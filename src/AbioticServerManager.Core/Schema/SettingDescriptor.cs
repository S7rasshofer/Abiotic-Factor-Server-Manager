namespace AbioticServerManager.Core.Schema;

public enum SettingSource
{
    /// <summary>Described by the built-in or override metadata catalog.</summary>
    BuiltInMetadata,

    /// <summary>Found only in the live file; type inferred, preserved verbatim.</summary>
    InferredFallback,
}

/// <summary>
/// One live setting merged with whatever metadata we have. This is the unit the dynamic
/// UI binds to. The live value is authoritative; metadata only enriches presentation.
/// </summary>
public sealed class SettingDescriptor
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Value { get; set; }
    public SettingMetadata? Metadata { get; init; }

    /// <summary>
    /// Category parsed from the live file's own banner comment (e.g. <c>; === WORLD ===</c>),
    /// or null when the setting is not under such a banner. Takes precedence over metadata
    /// so the tabs always mirror how the actual file is grouped.
    /// </summary>
    public string? CategoryHint { get; init; }

    public bool IsKnown => Metadata is not null;

    public SettingSource Source =>
        IsKnown ? SettingSource.BuiltInMetadata : SettingSource.InferredFallback;

    public SettingValueType ValueType =>
        Metadata?.Type ?? SettingTypeInference.Infer(Value);

    /// <summary>
    /// Generic container sections that carry no tab meaning. Real Abiotic Factor sandbox
    /// files split settings into named sections ([World], [Player], [Enemy], ...); when a
    /// setting is unknown we bucket it by its section so it lands on the right tab instead
    /// of dumping everything into Advanced.
    /// </summary>
    private static readonly HashSet<string> GenericSections =
        new(StringComparer.OrdinalIgnoreCase) { "", "SandboxSettings", "Sandbox" };

    public string Category =>
        CategoryHint
        ?? Metadata?.Category
        ?? (GenericSections.Contains(Section.Trim())
            ? "Advanced"
            : NormalizeSection(Section));

    private static string NormalizeSection(string section)
    {
        var s = section.Trim();
        return s.Length == 0 ? "Advanced" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public string DisplayName => Metadata?.DisplayName ?? Key;

    public SettingControlType ControlType => ValueType switch
    {
        SettingValueType.Boolean => SettingControlType.Toggle,
        SettingValueType.Enum => SettingControlType.Dropdown,
        // Every numeric value gets a slider. When metadata supplies min/max we use it;
        // otherwise the view model infers a sensible range (see SettingViewModel) rather
        // than hardcoding one blanket range for everything.
        SettingValueType.Number => SettingControlType.Slider,
        _ => SettingControlType.Text,
    };

    public const string UnknownNotice =
        "This setting was found in your live server files but is not known by this " +
        "version of Facility Overseer. It will be preserved and can be edited manually.";
}
