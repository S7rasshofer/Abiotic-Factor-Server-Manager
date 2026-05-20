using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbioticServerManager.App.ViewModels;

public sealed record PlatformAccessOption(PlatformAccessMode Mode, string Label);

/// <summary>
/// Editable wrapper around a <see cref="ServerInstance"/>. The plain model stays
/// serialisation-friendly; this view model adds change notification and live status.
/// </summary>
public sealed partial class ServerInstanceViewModel : ObservableObject
{
    public const int LogsStatusTabIndex = 7;

    private readonly PlayerActivityTracker _playerActivity = new();
    private readonly PlayerRosterTracker _roster = new();
    private readonly ServerHealthTracker _health = new();

    public ServerInstanceViewModel(ServerInstance model)
    {
        Model = model;
    }

    public IReadOnlyList<PlatformAccessOption> PlatformAccessOptions { get; } =
    [
        new(PlatformAccessMode.All, "All platforms / Crossplay"),
        new(PlatformAccessMode.PcOnly, "PC / Steam only"),
        new(PlatformAccessMode.PlaystationOnly, "PlayStation only"),
        new(PlatformAccessMode.XboxOnly, "Xbox + Windows Store only"),
    ];

    public ServerInstance Model { get; }

    public string Id => Model.Id;

    /// <summary>
    /// The single user-facing server name. Drives the tab label, the browser name
    /// (-SteamServerName) and the save folder (-WorldSaveName, sanitized) so non-technical
    /// users only ever name their server once.
    /// </summary>
    public string Name
    {
        get => Model.DisplayName;
        set
        {
            var v = value ?? string.Empty;
            if (string.Equals(v, Model.DisplayName, StringComparison.Ordinal))
            {
                return;
            }

            Model.DisplayName = v;
            Model.SteamServerName = v;
            Model.WorldSaveName = SanitizeSaveName(v);

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(SteamServerName));
            OnPropertyChanged(nameof(WorldSaveName));
            OnPropertyChanged(nameof(TabHeader));
        }
    }

    private static string SanitizeSaveName(string name)
    {
        var cleaned = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);
        return cleaned.Length == 0 ? "World" : cleaned;
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { if (SetModel(Model.DisplayName, value, v => Model.DisplayName = v)) OnPropertyChanged(nameof(TabHeader)); }
    }

    public string SteamServerName
    {
        get => Model.SteamServerName;
        set => SetModel(Model.SteamServerName, value, v => Model.SteamServerName = v);
    }

    public string WorldSaveName
    {
        get => Model.WorldSaveName;
        set => SetModel(Model.WorldSaveName, value, v => Model.WorldSaveName = v);
    }

    public string ServerPassword
    {
        get => Model.ServerPassword;
        set => SetModel(Model.ServerPassword, value, v => Model.ServerPassword = v);
    }

    public string AdminPassword
    {
        get => Model.AdminPassword;
        set => SetModel(Model.AdminPassword, value, v => Model.AdminPassword = v);
    }

    public int MaxPlayers
    {
        get => Model.MaxPlayers;
        set => SetModel(Model.MaxPlayers, value, v => Model.MaxPlayers = v);
    }

    public int GamePort
    {
        get => Model.GamePort;
        set => SetModel(Model.GamePort, value, v => Model.GamePort = v);
    }

    public int QueryPort
    {
        get => Model.QueryPort;
        set => SetModel(Model.QueryPort, value, v => Model.QueryPort = v);
    }

    public bool LanOnly
    {
        get => Model.LanOnly;
        set => SetModel(Model.LanOnly, value, v => Model.LanOnly = v);
    }

    public bool UseLocalIps
    {
        get => Model.UseLocalIps;
        set => SetModel(Model.UseLocalIps, value, v => Model.UseLocalIps = v);
    }

    public PlatformAccessMode PlatformAccessMode
    {
        get => Model.PlatformAccessMode;
        set => SetModel(Model.PlatformAccessMode, value, v => Model.PlatformAccessMode = v);
    }

    public string? MultiHomeAddress
    {
        get => Model.MultiHomeAddress;
        set => SetModel(Model.MultiHomeAddress, value, v => Model.MultiHomeAddress = v);
    }

    public string InstallPath
    {
        get => Model.InstallPath;
        set => SetModel(Model.InstallPath, value, v => Model.InstallPath = v);
    }

    public string AdminIniPath
    {
        get => Model.AdminIniPath;
        set => SetModel(Model.AdminIniPath, value, v => Model.AdminIniPath = v);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabHeader))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private string _statusText = "Stopped";

    [ObservableProperty]
    private bool _isRunningState;

    [ObservableProperty]
    private int _selectedVerticalTabIndex;

    // Switching vertical tabs must not leave a stale Setting Details selection from
    // another tab on screen.
    partial void OnSelectedVerticalTabIndexChanged(int value) => Sandbox?.ClearSelection();

    public bool IsRunning => IsRunningState;

    public string TabHeader => $"{DisplayName}  {(IsRunningState ? "●" : "○")}";

    public ObservableCollection<DiagnosticMessage> Diagnostics { get; } = [];

    public ObservableCollection<ServerLogEntry> LogLines { get; } = [];

    public ObservableCollection<string> ActivePlayers { get; } = [];

    public ObservableCollection<PlayerActivityEvent> PlayerActivityHistory { get; } = [];

    public ObservableCollection<PlayerSession> PlayerSessions { get; } = [];

    /// <summary>
    /// Durable roster (online first), parsed from real Abiotic Factor logs.
    /// §3.2: banned ids are filtered out of this collection — they appear on
    /// the Banished sub-tab only.
    /// §3.3: each row exposes a derived <c>IsAdmin</c> resolved against the
    /// world's moderator list at refresh time.
    /// </summary>
    public ObservableCollection<RosterRowViewModel> Roster { get; } = [];

    /// <summary>§3.2 banished-players page rows (id / last-known name / source / notes).</summary>
    public ObservableCollection<BannedPlayerRow> BannedPlayers { get; } = [];

    public ObservableCollection<PlayerRosterEvent> RosterActivity { get; } = [];

    public ObservableCollection<NetworkCheckResult> NetworkChecks { get; } = [];

    public ObservableCollection<string> RouterChecklist { get; } = [];

    public ObservableCollection<string> LocalIpv4Addresses { get; } = [];

    public ObservableCollection<string> Admins { get; } = [];

    [ObservableProperty]
    private string _newAdminId = "";

    [ObservableProperty]
    private string _adminStatusText =
        "Add the SteamID64 of each player who should have admin rights.";

    public ObservableCollection<BackupEntry> Backups { get; } = [];

    [ObservableProperty]
    private BackupEntry? _selectedBackup;

    [ObservableProperty]
    private string _backupStatusText = "No backups yet for this world.";

    [ObservableProperty]
    private bool _isNetworkChecking;

    [ObservableProperty]
    private string _networkStatusText = "Network setup has not been checked yet.";

    [ObservableProperty]
    private string _firewallSummaryText = "Check setup to verify Windows Firewall rules.";

    [ObservableProperty]
    private string _routerTargetText = "Check setup to detect this PC's LAN IP.";

    [ObservableProperty]
    private string _serverExecutableText = "Check setup to locate the dedicated server executable.";

    [ObservableProperty]
    private string _lastRouterChecklistText = "No router checklist copied for this world yet.";

    [ObservableProperty]
    private string _lastFirewallRepairText = "No firewall repair has been run for this world yet.";

    [ObservableProperty]
    private string _playerActivityStatusText = "Player activity has not been detected in the logs yet.";

    [ObservableProperty]
    private string _playersOnlineText = "No players have connected yet.";

    [ObservableProperty]
    private RosterRowViewModel? _selectedRosterPlayer;

    /// <summary>
    /// Snapshot of the world's moderator SteamID64 set, refreshed by the shell
    /// whenever Admin.ini changes. Drives the §3.3 admin marker on roster rows.
    /// </summary>
    public IReadOnlyList<string> ModeratorIds { get; private set; } = [];

    /// <summary>
    /// Snapshot of the world's banned SteamID64 set, refreshed by the shell
    /// whenever Admin.ini changes. Drives §3.2 (filter banned from roster +
    /// surface them on the Banished page) and the Admin-tab badge count.
    /// </summary>
    public IReadOnlyList<string> BannedIds { get; private set; } = [];

    /// <summary>Count surfaced as a small badge on the Admin tab header.</summary>
    [ObservableProperty]
    private int _bannedCount;

    public void UpdateModerationLists(
        IReadOnlyList<string> moderatorIds,
        IReadOnlyList<string> bannedIds)
    {
        ModeratorIds = moderatorIds;
        BannedIds = bannedIds;
        BannedCount = bannedIds.Count;
        RefreshRoster();
    }

    /// <summary>Honest runtime health detail (process vs actually online vs blocked).</summary>
    [ObservableProperty]
    private string _healthDetail = "Server is stopped.";

    public string HealthStatusText => _health.StatusText;

    /// <summary>
    /// Honest, single-source-of-truth health value the world status dot binds to.
    /// Do not bind a dot to <see cref="IsRunningState"/> — process presence is
    /// not health (a corrupt world is briefly running but Blocked).
    /// </summary>
    public ServerHealth Health => _health.Health;

    public void OnServerStarted()
    {
        _health.OnProcessStarted();
        PushHealth();
    }

    public void OnServerStopped()
    {
        _health.OnProcessExited(unexpected: false);
        PushHealth();
    }

    public void ApplyHealth(ServerLogLine line)
    {
        if (_health.Apply(line))
        {
            PushHealth();
        }
    }

    private void PushHealth()
    {
        HealthDetail = _health.Reason;
        OnPropertyChanged(nameof(HealthStatusText));
        OnPropertyChanged(nameof(Health));
        if (IsRunningState || _health.StatusText is "Blocked" or "Crashed")
        {
            StatusText = _health.StatusText;
        }
    }

    [ObservableProperty]
    private CheckStatus _externalVisibilityStatus = CheckStatus.Unknown;

    [ObservableProperty]
    private string _externalVisibilityText =
        "Not checked. Click “Check Visibility” (the server must be running and ports forwarded).";

    [ObservableProperty]
    private bool _isVisibilityChecking;

    /// <summary>Per-world dynamic sandbox settings editor state. Assigned by the shell.</summary>
    public SandboxSettingsViewModel? Sandbox
    {
        get;
        set { if (!ReferenceEquals(field, value)) { field = value; OnPropertyChanged(); } }
    }

    private bool SetModel<T>(
        T current,
        T value,
        Action<T> setter,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        setter(value);
        OnPropertyChanged(propertyName);
        return true;
    }

    public void ApplyPlayerActivity(ServerLogLine line)
    {
        if (_playerActivity.Apply(line) is null)
        {
            return;
        }

        ActivePlayers.Clear();
        foreach (var player in _playerActivity.ActivePlayers)
        {
            ActivePlayers.Add(player);
        }

        PlayerActivityHistory.Clear();
        foreach (var entry in _playerActivity.History)
        {
            PlayerActivityHistory.Add(entry);
        }

        PlayerSessions.Clear();
        foreach (var session in _playerActivity.Sessions)
        {
            PlayerSessions.Add(session);
        }

        PlayerActivityStatusText = ActivePlayers.Count == 0
            ? "No active players detected."
            : $"{ActivePlayers.Count} active player(s) detected.";
    }

    /// <summary>Loads previously known players (offline) before any live events.</summary>
    public void SeedRoster(IReadOnlyList<PlayerRosterEntry> known)
    {
        _roster.SeedKnown(known);
        RefreshRoster();
    }

    /// <summary>Durable roster facts for persistence.</summary>
    public IReadOnlyList<PlayerRosterEntry> ExportRoster() => _roster.ExportDurable();

    /// <summary>
    /// Feeds one log line to the roster. Returns true when a durable change
    /// happened (so the shell can persist it), false otherwise.
    /// </summary>
    public bool ApplyRosterActivity(ServerLogLine line)
    {
        var evt = _roster.Apply(line);
        if (evt is null)
        {
            return false;
        }

        RefreshRoster();
        return evt.Kind is PlayerRosterEventKind.LoginRequested
            or PlayerRosterEventKind.JoinSucceeded
            or PlayerRosterEventKind.EnteredFacility
            or PlayerRosterEventKind.Disconnected
            or PlayerRosterEventKind.ServerStopped;
    }

    /// <summary>Server process ended: close all sessions, mark everyone offline.</summary>
    public void MarkServerStopped()
    {
        _roster.Apply(new ServerLogLine(Id, DateTimeOffset.Now, "[server stopped]", false));
        RefreshRoster();
    }

    private void RefreshRoster()
    {
        // Entries are fresh clones each refresh, so preserve the user's selection
        // by stable key (otherwise selecting a player to ban would clear on the
        // next log event).
        var selectedKey = SelectedRosterPlayer?.Key;

        // §3.2: live roster excludes banned ids; they appear only on the
        // Banished sub-tab (Banished page rows below).
        var entries = _roster.Entries;
        var visible = RosterPresentation.FilterActive(entries, BannedIds);

        Roster.Clear();
        foreach (var entry in visible)
        {
            // §3.3: derived IsAdmin per row; decoration only.
            var isAdmin = RosterPresentation.IsAdmin(entry, ModeratorIds);
            Roster.Add(new RosterRowViewModel(entry, isAdmin));
        }

        if (selectedKey is { Length: > 0 })
        {
            SelectedRosterPlayer = Roster.FirstOrDefault(e => e.Key == selectedKey);
        }

        RosterActivity.Clear();
        foreach (var evt in _roster.History)
        {
            RosterActivity.Add(evt);
        }

        // §3.2 banished-players page rows (built from the sectioned ids,
        // joined with the full roster for last-known display name).
        BannedPlayers.Clear();
        foreach (var row in RosterPresentation.BuildBannedRows(BannedIds, entries))
        {
            BannedPlayers.Add(row);
        }

        var header = $"Players Online: {_roster.OnlineCount}/{MaxPlayers}";
        if (_roster.ServerPlayerCount is { } count)
        {
            header += $"  ·  Server count: {count}";
        }

        if (_roster.CountWarning is { Length: > 0 } warning)
        {
            header += $"  ·  {warning}";
        }
        else if (!_roster.HasSeenActivity)
        {
            header = "No players have connected yet.";
        }

        PlayersOnlineText = header;
    }
}
