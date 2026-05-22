using System.Globalization;
using AbioticServerManager.Core.Schema;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbioticServerManager.App.ViewModels;

/// <summary>One dropdown choice: <see cref="Value"/> is written to the INI (loss-less),
/// <see cref="Label"/> is what the user reads.</summary>
public sealed record SettingOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Bindable wrapper around one <see cref="SettingDescriptor"/>. Every edit is pushed back
/// through <paramref name="apply"/> so the loss-less INI document stays the source of truth.
/// </summary>
public sealed partial class SettingViewModel : ObservableObject
{
    private readonly SettingDescriptor _descriptor;
    private readonly Action<SettingViewModel, string> _apply;

    public SettingViewModel(SettingDescriptor descriptor, Action<SettingViewModel, string> apply)
    {
        _descriptor = descriptor;
        _apply = apply;
        Options = descriptor.Metadata?.Options ?? [];

        var labels = descriptor.Metadata?.OptionLabels ?? [];
        OptionItems =
        [
            .. Options.Select((value, i) =>
                new SettingOption(value, i < labels.Count ? labels[i] : value)),
        ];
    }

    public SettingDescriptor Descriptor => _descriptor;

    public string Key => _descriptor.Key;
    public string Section => _descriptor.Section;
    public string DisplayName => _descriptor.DisplayName;
    public string Category => _descriptor.Category;
    public SettingControlType ControlType => _descriptor.ControlType;
    public bool IsKnown => _descriptor.IsKnown;
    public string? Description => _descriptor.Metadata?.Description;
    public string? Warning => _descriptor.Metadata?.Warning;
    public string? DefaultValue => _descriptor.Metadata?.Default;
    public IReadOnlyList<string> Options { get; }

    /// <summary>Dropdown choices with human labels; bound by the Dropdown template.</summary>
    public IReadOnlyList<SettingOption> OptionItems { get; }

    public string SourceText => IsKnown
        ? "Built-in metadata"
        : "Discovered in your live files (preserved)";

    public string? UnknownNotice => IsKnown ? null : SettingDescriptor.UnknownNotice;

    /// <summary>True when metadata supplies a default this setting can be reset to.</summary>
    public bool HasDefault => !string.IsNullOrEmpty(_descriptor.Metadata?.Default);

    /// <summary>Resets to the metadata default. No-op for unknown settings (nothing to reset to).</summary>
    public void ResetToDefault()
    {
        if (_descriptor.Metadata?.Default is { Length: > 0 } def)
        {
            Commit(def);
        }
    }

    private double CurrentNumber =>
        double.TryParse(_descriptor.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;

    private bool LooksIntegral =>
        !_descriptor.Value.Contains('.', StringComparison.Ordinal) &&
        long.TryParse(_descriptor.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    // Prefer real metadata bounds. Only when none are declared do we infer a sane range
    // from the value itself (and always keep the current value inside the slider track).
    public double Minimum =>
        _descriptor.Metadata?.Min ?? Math.Min(0, CurrentNumber);

    public double Maximum
    {
        get
        {
            if (_descriptor.Metadata?.Max is { } max)
            {
                return max;
            }

            var v = CurrentNumber;
            var inferred = LooksIntegral
                ? Math.Max(10, Math.Ceiling(v * 2))
                : Math.Max(2.0, Math.Ceiling(v * 2));
            return Math.Max(inferred, v);
        }
    }

    public double Step =>
        _descriptor.Metadata?.Step ?? (LooksIntegral ? 1 : 0.05);

    /// <summary>
    /// True when this setting is whole-number-valued (its step is a positive
    /// integer) — e.g. Base Inventory Size, Bonus Perk Points, Structural
    /// Support Limit. Drives integer snapping so the slider cannot produce a
    /// fractional value like "12.7 inventory slots".
    /// </summary>
    public bool IsInteger => Step >= 1.0 && Step % 1.0 == 0.0;

    /// <summary>Page-step for keyboard PageUp/PageDown on the slider.</summary>
    public double LargeStep => Step * 10.0;

    public string RangeText => _descriptor.Metadata is { Min: { } min, Max: { } max }
        ? $"{Format(min)} – {Format(max)}"
        : ControlType == SettingControlType.Slider
            ? $"{Format(Minimum)} – {Format(Maximum)} (inferred)"
            : "—";

    public string OptionsText => OptionItems.Count > 0
        ? string.Join(", ", OptionItems.Select(o =>
            o.Value == o.Label ? o.Value : $"{o.Label} ({o.Value})"))
        : "—";

    /// <summary>Value formatted for the context panel — shows the label for dropdowns.</summary>
    public string CurrentValueDisplay =>
        OptionItems.FirstOrDefault(o => o.Value == _descriptor.Value) is { } match
            ? $"{match.Label} ({match.Value})"
            : _descriptor.Value;

    public string StringValue
    {
        get => _descriptor.Value;
        set => Commit(value);
    }

    public bool BoolValue
    {
        get => _descriptor.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        set => Commit(value ? "True" : "False");
    }

    public double DoubleValue
    {
        get => double.TryParse(
            _descriptor.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var d) ? d : Minimum;
        // Whole-number settings never store a fractional value, even if the
        // slider's snapping leaves a rounding sliver.
        set => Commit(Format(IsInteger ? Math.Round(value) : value));
    }

    public string SelectedOption
    {
        get => _descriptor.Value;
        set => Commit(value);
    }

    private void Commit(string canonical)
    {
        if (string.Equals(canonical, _descriptor.Value, StringComparison.Ordinal))
        {
            return;
        }

        _apply(this, canonical);
        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(DoubleValue));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(CurrentValueDisplay));
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(LargeStep));
        OnPropertyChanged(nameof(IsInteger));
        OnPropertyChanged(nameof(RangeText));
    }

    private static string Format(double value) =>
        value == Math.Floor(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
}
