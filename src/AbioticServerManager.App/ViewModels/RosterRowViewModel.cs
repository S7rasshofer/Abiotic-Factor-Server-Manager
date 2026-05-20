using AbioticServerManager.Core.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbioticServerManager.App.ViewModels;

/// <summary>
/// Presentation wrapper around a <see cref="PlayerRosterEntry"/>. Adds the §3.3
/// derived <see cref="IsAdmin"/> flag (resolved at render time against the
/// world's moderator list) without polluting the Core model. The XAML binds
/// to this row's pass-through properties so the existing template barely
/// changes; the underlying entry is still used for ban/kick because moderation
/// must always operate on the captured id, never on the visual decoration.
/// </summary>
public sealed partial class RosterRowViewModel : ObservableObject
{
    public RosterRowViewModel(PlayerRosterEntry entry, bool isAdmin)
    {
        Entry = entry;
        _isAdmin = isAdmin;
    }

    public PlayerRosterEntry Entry { get; }

    /// <summary>Stable identity key. Used to preserve selection across refresh.</summary>
    public string Key => Entry.Key;

    public string DisplayName => Entry.DisplayName;
    public string IdText => Entry.IdText;
    public string StatusText => Entry.StatusText;
    public bool IsOnline => Entry.IsOnline;
    public string? Platform => Entry.Platform;
    public string? RemoteAddress => Entry.RemoteAddress;
    public string CurrentSessionText => Entry.CurrentSessionText;
    public string LastSeenText => Entry.LastSeenText;

    public string? SteamId64 => Entry.SteamId64;
    public string? PrimaryId => Entry.PrimaryId;

    [ObservableProperty]
    private bool _isAdmin;

    /// <summary>Superscript glyph rendered after the display name (§3.3).</summary>
    public static string AdminGlyph => "⁺"; // U+207A SUPERSCRIPT PLUS SIGN
}
