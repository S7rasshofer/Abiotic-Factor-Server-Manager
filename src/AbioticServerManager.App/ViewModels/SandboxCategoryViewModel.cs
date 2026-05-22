using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbioticServerManager.App.ViewModels;

/// <summary>
/// One dynamically-discovered sandbox category — rendered as a sub-tab under
/// the Settings tab. A new category in a future game update therefore produces
/// a new tab with zero code changes ("discover, don't hardcode").
///
/// Holds its own <see cref="Settings"/> list and forwards the shared editor
/// members (<see cref="IsLoaded"/> / <see cref="SelectedSetting"/> /
/// <see cref="SelectCommand"/> / <see cref="ResetBucketCommand"/>) to the parent
/// <see cref="SandboxSettingsViewModel"/>, so <c>SandboxCategoryPanel</c> can
/// bind to a category VM directly and stay unaware of the parent.
/// </summary>
public sealed class SandboxCategoryViewModel : ObservableObject
{
    private readonly SandboxSettingsViewModel _parent;

    public SandboxCategoryViewModel(SandboxSettingsViewModel parent, string name, string emptyHint)
    {
        _parent = parent;
        Name = name;
        EmptyHint = emptyHint;
    }

    /// <summary>Display name = the discovered category (e.g. "World", "Enemy", "Advanced").</summary>
    public string Name { get; }

    /// <summary>Hint shown when no sandbox file is loaded yet.</summary>
    public string EmptyHint { get; }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];

    // --- Facade members SandboxCategoryPanel binds against (forwarded to parent) ---

    public bool IsLoaded => _parent.IsLoaded;

    public SettingViewModel? SelectedSetting => _parent.SelectedSetting;

    public ICommand SelectCommand => _parent.SelectCommand;

    public ICommand ResetBucketCommand => _parent.ResetBucketCommand;

    /// <summary>
    /// Called by the parent when a forwarded property changes, so the panel's
    /// bindings refresh. Avoids a per-category PropertyChanged subscription
    /// (and the leak that would come with rebuilding categories on every load).
    /// </summary>
    public void NotifyForwardedChanged(string propertyName) => OnPropertyChanged(propertyName);
}
