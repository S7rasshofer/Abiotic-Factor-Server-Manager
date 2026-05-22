using System.Collections.ObjectModel;
using AbioticServerManager.Core.Schema;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AbioticServerManager.App.ViewModels;

/// <summary>
/// Per-world sandbox editor state. Settings are grouped onto dynamically
/// discovered category tabs — a new category in <c>SandboxSettings.ini</c>
/// produces a new tab automatically, and unknown/uncategorised keys land on
/// the "Advanced" category and are never dropped.
/// </summary>
public sealed partial class SandboxSettingsViewModel : ObservableObject
{
    /// <summary>Display name for the uncategorised / unknown-key catch-all category.</summary>
    public const string AdvancedCategoryName = "Advanced";

    private readonly ISandboxSettingsService _service;
    private SandboxSettingsDocument? _document;

    public SandboxSettingsViewModel(ISandboxSettingsService service) => _service = service;

    /// <summary>
    /// One entry per discovered sandbox category, ordered World / Player /
    /// Survival / Enemy / (others A–Z) / Advanced. Bound directly as the
    /// Settings tab's sub-tab source.
    /// </summary>
    public ObservableCollection<SandboxCategoryViewModel> Categories { get; } = [];

    /// <summary>True once any categories exist (i.e. a sandbox file is loaded).</summary>
    public bool HasCategories => Categories.Count > 0;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _statusText = "No sandbox file loaded.";

    [ObservableProperty]
    private SettingViewModel? _selectedSetting;

    public SandboxSettingsDocument? Document => _document;

    // Keep the category facades' forwarded bindings (IsLoaded / SelectedSetting)
    // refreshed without a per-category event subscription.
    partial void OnIsLoadedChanged(bool value) =>
        NotifyCategories(nameof(SandboxCategoryViewModel.IsLoaded));

    partial void OnSelectedSettingChanged(SettingViewModel? value) =>
        NotifyCategories(nameof(SandboxCategoryViewModel.SelectedSetting));

    private void NotifyCategories(string propertyName)
    {
        foreach (var category in Categories)
        {
            category.NotifyForwardedChanged(propertyName);
        }
    }

    public void LoadFrom(SandboxSettingsDocument document)
    {
        _document = document;
        FilePath = document.FilePath;

        Categories.Clear();

        // Group by the setting's actual category. A category we have never seen
        // before still gets its own tab — no code change required.
        var grouped = document.Settings
            .Select(d => new SettingViewModel(d, ApplyEdit))
            .GroupBy(svm => NormalizeCategory(svm.Descriptor.Category))
            .OrderBy(g => CategoryRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var category = new SandboxCategoryViewModel(this, group.Key, EmptyHintFor(group.Key));

            // Sliders/numbers first, dropdowns/text in the middle, checkboxes last.
            foreach (var svm in group
                .OrderBy(s => ControlRank(s.ControlType))
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                category.Settings.Add(svm);
            }

            Categories.Add(category);
        }

        IsLoaded = true;
        IsDirty = false;
        OnPropertyChanged(nameof(HasCategories));

        var unknown = document.Settings.Count(s => !s.IsKnown);
        StatusText = $"{document.Settings.Count} settings loaded" +
                     (unknown > 0 ? $" ({unknown} unknown, preserved)" : "") +
                     $" — {document.FilePath}";
    }

    public async Task SaveAsync()
    {
        if (_document is null)
        {
            return;
        }

        await _service.SaveAsync(_document);
        IsDirty = false;
        StatusText = $"Saved {_document.FilePath}";
    }

    private void ApplyEdit(SettingViewModel setting, string value)
    {
        if (_document is null)
        {
            return;
        }

        _service.Set(_document, setting.Descriptor, value);
        SelectedSetting = setting;
        IsDirty = true;
        StatusText = $"Edited {setting.Key} (unsaved)";
    }

    [RelayCommand]
    private void Select(SettingViewModel? setting)
    {
        if (setting is not null)
        {
            SelectedSetting = setting;
        }
    }

    /// <summary>
    /// Clears the context/help selection so the Setting Details panel does not show a
    /// setting that belongs to a different category tab. Called when the tab changes.
    /// </summary>
    public void ClearSelection() => SelectedSetting = null;

    /// <summary>
    /// Resets only the settings in the given category back to their metadata defaults.
    /// Unknown settings (no default) are left untouched and preserved.
    /// </summary>
    [RelayCommand]
    private void ResetBucket(System.Collections.IEnumerable? items)
    {
        if (items is null)
        {
            return;
        }

        var resettable = items.OfType<SettingViewModel>().Where(s => s.HasDefault).ToList();
        if (resettable.Count == 0)
        {
            StatusText = "Nothing to reset on this tab (no known defaults).";
            return;
        }

        foreach (var setting in resettable)
        {
            setting.ResetToDefault();
        }

        StatusText = $"Reset {resettable.Count} setting(s) on this tab to defaults (unsaved).";
    }

    /// <summary>Empty category / whitespace collapses to the Advanced catch-all.</summary>
    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return AdvancedCategoryName;
        }

        var trimmed = category.Trim();
        // Title-case a single lowercase word for a tidy tab header.
        return trimmed.Length > 1 && char.IsLower(trimmed[0])
            ? char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
            : trimmed;
    }

    private static int CategoryRank(string category) => category.ToLowerInvariant() switch
    {
        "world" => 0,
        "player" => 1,
        "survival" => 2,
        "enemy" => 3,
        "advanced" => 99,        // catch-all always last
        _ => 50,                 // any newly-discovered category, alphabetised within this rank
    };

    private static string EmptyHintFor(string category) =>
        category.Equals(AdvancedCategoryName, StringComparison.OrdinalIgnoreCase)
            ? "Unknown and uncategorised settings appear here. Anything Facility Overseer " +
              "does not recognise is preserved exactly and editable as raw text."
            : $"{category} settings appear here automatically once the server is installed.";

    private static int ControlRank(SettingControlType type) => type switch
    {
        SettingControlType.Slider => 0,
        SettingControlType.Number => 1,
        SettingControlType.Dropdown => 2,
        SettingControlType.Text => 3,
        SettingControlType.Toggle => 4,
        _ => 5,
    };
}
