using System.Collections.ObjectModel;
using AbioticServerManager.Core.Schema;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AbioticServerManager.App.ViewModels;

/// <summary>
/// Per-world sandbox editor state. Settings are bucketed onto the World / Player / Enemy
/// / Advanced vertical tabs; unknown keys always land in Advanced and are never dropped.
/// </summary>
public sealed partial class SandboxSettingsViewModel : ObservableObject
{
    private readonly ISandboxSettingsService _service;
    private SandboxSettingsDocument? _document;

    public SandboxSettingsViewModel(ISandboxSettingsService service) => _service = service;

    public ObservableCollection<SettingViewModel> WorldSettings { get; } = [];
    public ObservableCollection<SettingViewModel> PlayerSettings { get; } = [];
    public ObservableCollection<SettingViewModel> EnemySettings { get; } = [];
    public ObservableCollection<SettingViewModel> AdvancedSettings { get; } = [];

    /// <summary>
    /// True when there are any uncategorised/unknown settings to show on the
    /// Advanced tab. Bound to the tab's Visibility so an empty Advanced tab is
    /// HIDDEN, not removed — the catch-all must still appear the moment a
    /// future game update introduces unknown keys.
    /// </summary>
    public bool HasAdvancedSettings => AdvancedSettings.Count > 0;

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

    public void LoadFrom(SandboxSettingsDocument document)
    {
        _document = document;
        FilePath = document.FilePath;

        WorldSettings.Clear();
        PlayerSettings.Clear();
        EnemySettings.Clear();
        AdvancedSettings.Clear();

        var buckets = new[] { WorldSettings, PlayerSettings, EnemySettings, AdvancedSettings };
        var staging = buckets.ToDictionary(b => b, _ => new List<SettingViewModel>());

        foreach (var descriptor in document.Settings)
        {
            var svm = new SettingViewModel(descriptor, ApplyEdit);
            staging[BucketFor(descriptor.Category)].Add(svm);
        }

        foreach (var (bucket, items) in staging)
        {
            // Sliders/numbers first, dropdowns/text in the middle, checkboxes last.
            foreach (var svm in items
                .OrderBy(s => ControlRank(s.ControlType))
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                bucket.Add(svm);
            }
        }

        IsLoaded = true;
        IsDirty = false;
        OnPropertyChanged(nameof(HasAdvancedSettings));
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
    /// setting that belongs to a different vertical tab. Called when the tab changes.
    /// </summary>
    public void ClearSelection() => SelectedSetting = null;

    /// <summary>
    /// Resets only the settings in the given tab/bucket back to their metadata defaults.
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

    private ObservableCollection<SettingViewModel> BucketFor(string category) =>
        category.ToLowerInvariant() switch
        {
            "world" => WorldSettings,
            "player" or "survival" => PlayerSettings,
            "enemy" => EnemySettings,
            _ => AdvancedSettings,
        };

    private static int ControlRank(Core.Schema.SettingControlType type) => type switch
    {
        Core.Schema.SettingControlType.Slider => 0,
        Core.Schema.SettingControlType.Number => 1,
        Core.Schema.SettingControlType.Dropdown => 2,
        Core.Schema.SettingControlType.Text => 3,
        Core.Schema.SettingControlType.Toggle => 4,
        _ => 5,
    };
}
