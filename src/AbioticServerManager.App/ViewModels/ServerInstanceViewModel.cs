using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.Networking;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AbioticServerManager.App.ViewModels;

public sealed record PlatformAccessOption(PlatformAccessMode Mode, string Label);

/// <summary>
/// Editable wrapper around a <see cref="ServerInstance"/>. The plain model stays
/// serialisation-friendly; this view model adds change notification and live status.
/// </summary>
public sealed partial class ServerInstanceViewModel : ObservableObject
{
    // Vertical tab order: 0 Server, 1 Network, 2 Game Settings, 3 Backups,
    // 4 Logs & Status. (Admin is now a sub-tab inside Logs & Status.)
    public const int GameSettingsTabIndex = 2;
    public const int LogsStatusTabIndex = 4;

    private readonly PlayerActivityTracker _playerActivity = new();
    private readonly PlayerRosterTracker _roster = new();
    private readonly ServerHealthTracker _health = new();
    private readonly StartupSequenceTracker _startup = new();

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

    // Switching vertical tabs clears any stale Setting Details selection and
    // re-expands the rail (the user just navigated, so show the labels).
    partial void OnSelectedVerticalTabIndexChanged(int value)
    {
        Sandbox?.ClearSelection();
        IsMainTabRailCondensed = false;
    }

    /// <summary>
    /// Whether the main vertical tab rail is condensed to icons only. Set true
    /// when the user clicks into a tab's content (more room to work), false when
    /// they click a rail tab so the labels are readable while navigating.
    /// </summary>
    [ObservableProperty]
    private bool _isMainTabRailCondensed;

    public bool IsRunning => IsRunningState;

    public string TabHeader => $"{DisplayName}  {(IsRunningState ? "*" : "o")}";

    public ObservableCollection<DiagnosticMessage> Diagnostics { get; } = [];

    // ---- Sec 4.3 Recommended Actions / Sec 4.5 World Integrity (Phase 2 guidance) ----

    /// <summary>Sec 4.3: ranked next-step suggestions for this world.</summary>
    public ObservableCollection<RecommendedAction> RecommendedActions { get; } = [];

    [ObservableProperty]
    private bool _hasRecommendedActions;

    /// <summary>Sec 4.5: pre-start integrity findings (blockers / warnings / info).</summary>
    public ObservableCollection<WorldIntegrityFinding> IntegrityFindings { get; } = [];

    [ObservableProperty]
    private bool _hasIntegrityFindings;

    /// <summary>
    /// Unified Logs-tab notification strip. Combines the recommended-actions
    /// summary, the integrity-findings summary, and each individual
    /// diagnostic into one ordered collection so the WrapPanel can flow
    /// every chip on a single line (wrapping when full). The list is
    /// rebuilt by <see cref="RebuildNotificationChips"/> whenever any of
    /// the three source collections change.
    /// </summary>
    public ObservableCollection<object> NotificationChips { get; } = [];

    /// <summary>
    /// Rebuilds <see cref="NotificationChips"/> from the current state of
    /// <see cref="HasRecommendedActions"/>, <see cref="HasIntegrityFindings"/>,
    /// and <see cref="Diagnostics"/>. Cheap (at most a few items), so we
    /// just clear and re-add rather than diffing.
    /// </summary>
    public void RebuildNotificationChips()
    {
        NotificationChips.Clear();
        if (HasRecommendedActions)
        {
            NotificationChips.Add(new RecommendedActionsChip(this));
        }
        if (HasIntegrityFindings)
        {
            NotificationChips.Add(new IntegrityFindingsChip(this));
        }
        foreach (var diag in Diagnostics)
        {
            NotificationChips.Add(diag);
        }
    }

    [ObservableProperty]
    private string _integritySummary = "World integrity has not been checked yet.";

    /// <summary>Sec 4.5: false when an integrity blocker is present - drives the pre-start gate.</summary>
    [ObservableProperty]
    private bool _isWorldLaunchable = true;

    /// <summary>
    /// Sec 4.3: last-known Windows Firewall state, cached from the most recent
    /// network inspection so the recommended-actions builder need not re-probe.
    /// <c>null</c> until the first inspection completes - keeps the false
    /// "Create firewall rules" prompt from firing before we have actually
    /// checked the rules.
    /// </summary>
    [ObservableProperty]
    private bool? _firewallRulesConfigured;

    // ---- Sec 4.6 Startup sequence timeline ----

    /// <summary>The 7-phase startup timeline (process -> net -> world -> session -> ready).</summary>
    public ObservableCollection<StartupPhaseEntry> StartupPhases { get; } = [];

    [ObservableProperty]
    private bool _hasStartupSequence;

    [ObservableProperty]
    private string _startupSummary = "";

    /// <summary>
    /// Sec 4.9: the in-game "lobby code" players can use to join directly,
    /// captured from the dedicated server's EOS session log. Empty when the
    /// server is not running or the code has not been published yet.
    /// </summary>
    [ObservableProperty]
    private string _lobbyCode = "";

    // ---- Sec 4.2 Guided recovery flow ----

    [ObservableProperty]
    private bool _hasRecoveryFlow;

    [ObservableProperty]
    private string _recoveryFlowTitle = "";

    [ObservableProperty]
    private string _recoveryFlowSummary = "";

    /// <summary>Ordered steps of the active recovery flow, with optional action commands.</summary>
    public ObservableCollection<RecoveryStep> RecoverySteps { get; } = [];

    public ObservableCollection<ServerLogEntry> LogLines { get; } = [];

    public ObservableCollection<string> ActivePlayers { get; } = [];

    public ObservableCollection<PlayerActivityEvent> PlayerActivityHistory { get; } = [];

    public ObservableCollection<PlayerSession> PlayerSessions { get; } = [];

    /// <summary>
    /// Durable roster (online first), parsed from real Abiotic Factor logs.
    /// Sec 3.2: banned ids are filtered out of this collection - they appear on
    /// the Banished sub-tab only.
    /// Sec 3.3: each row exposes a derived <c>IsAdmin</c> resolved against the
    /// world's moderator list at refresh time.
    /// </summary>
    public ObservableCollection<RosterRowViewModel> Roster { get; } = [];

    /// <summary>Sec 3.2 banished-players page rows (id / last-known name / source / notes).</summary>
    public ObservableCollection<BannedPlayerRow> BannedPlayers { get; } = [];

    // --- Player Detail tab: populated on double-clicking a roster row ---

    /// <summary>Lifecycle events for the player shown on the Player Detail tab.</summary>
    public ObservableCollection<PlayerRosterEvent> SelectedPlayerActivity { get; } = [];

    /// <summary>Chat conversation shown on the Player Detail tab (their lines flagged).</summary>
    public ObservableCollection<PlayerChatLine> SelectedPlayerChat { get; } = [];

    [ObservableProperty]
    private string _playerDetailHeader = "Double-click a player in the list to see their activity and chat.";

    [ObservableProperty]
    private bool _hasPlayerDetail;

    /// <summary>Inner Logs &amp; Status sub-tab: 0 = Log, 1 = Players, 2 = Player Detail.</summary>
    [ObservableProperty]
    private int _logsSubTabIndex;

    public const int PlayerDetailSubTabIndex = 2;

    /// <summary>Display name of the player currently shown on the detail tab (for live refresh).</summary>
    private string? _detailPlayerName;

    public ObservableCollection<NetworkCheckResult> NetworkChecks { get; } = [];

    // ---- Phase B: pinned Reachability verdict ----

    /// <summary>Top-of-panel rollup status (Stopped / Reachable / BindingOrWarming / Unreachable).</summary>
    [ObservableProperty]
    private NetworkVerdictStatus _reachabilityStatus = NetworkVerdictStatus.Stopped;

    /// <summary>One-line headline for the Reachability verdict banner.</summary>
    [ObservableProperty]
    private string _reachabilityHeadline = "Server is stopped.";

    /// <summary>Sub-line detail for the Reachability verdict banner.</summary>
    [ObservableProperty]
    private string _reachabilityDetail = "Start the world to check reachability.";

    // ---- Sec 4.7 Network confidence score ----

    [ObservableProperty]
    private bool _hasNetworkConfidence;

    /// <summary>Compact "76/100 - Good" summary shown next to the panel title.</summary>
    [ObservableProperty]
    private string _networkConfidenceSummary = "Run Check Setup to score this world's hosting readiness.";

    /// <summary>What is already working in this world's network setup.</summary>
    public ObservableCollection<string> NetworkConfidenceStrengths { get; } = [];

    /// <summary>Ranked "do this to raise the score" hints.</summary>
    public ObservableCollection<string> NetworkConfidenceLifts { get; } = [];

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
    private string _lastFirewallRepairText = "No firewall repair has been run for this world yet.";

    [ObservableProperty]
    private string _playerActivityStatusText = "Player activity has not been detected in the logs yet.";

    [ObservableProperty]
    private string _playersOnlineText = "No players have connected yet.";

    [ObservableProperty]
    private RosterRowViewModel? _selectedRosterPlayer;

    /// <summary>
    /// Snapshot of the world's moderator SteamID64 set, refreshed by the shell
    /// whenever Admin.ini changes. Drives the Sec 3.3 admin marker on roster rows.
    /// </summary>
    public IReadOnlyList<string> ModeratorIds { get; private set; } = [];

    /// <summary>
    /// Snapshot of the world's banned SteamID64 set, refreshed by the shell
    /// whenever Admin.ini changes. Drives Sec 3.2 (filter banned from roster +
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

    /// <summary>Sec 4.2: the active recovery-flow trigger tag while Blocked, else null.</summary>
    public string? HealthBlockingTag => _health.BlockingTag;

    /// <summary>
    /// Honest, single-source-of-truth health value the world status dot binds to.
    /// Do not bind a dot to <see cref="IsRunningState"/> - process presence is
    /// not health (a corrupt world is briefly running but Blocked).
    /// </summary>
    public ServerHealth Health => _health.Health;

    public void OnServerStarted()
    {
        _health.OnProcessStarted();
        _startup.OnProcessStarted();
        RefreshStartup();
        PushHealth();

        // Sec 4.9: a fresh session mints a new lobby code; clear the stale one
        // until the running server publishes the new code to its log.
        LobbyCode = "";
    }

    public void OnServerStopped()
    {
        _health.OnProcessExited(unexpected: false);
        _startup.OnServerStopped(unexpected: false);
        RefreshStartup();
        PushHealth();
        LobbyCode = "";
    }

    public void ApplyHealth(ServerLogLine line)
    {
        if (_health.Apply(line))
        {
            PushHealth();
        }

        // Sec 4.6: same log line advances the startup timeline.
        if (_startup.OnLogLine(line.Text))
        {
            RefreshStartup();
        }

        // Sec 4.9: capture the lobby code the server publishes to its EOS session.
        // A lobby code is direct proof the EOS session was created - if the log
        // heuristics decided the world was Blocked from earlier noise, the
        // published code overrides them.
        if (LobbyCodeParser.TryParse(line.Text) is { } code)
        {
            LobbyCode = code;
            ConfirmOnlineFromCorroboration(
                "EOS published a lobby code (the online session is live).");
        }
    }

    /// <summary>
    /// External proof the server is reachable (A2S reply, EOS lobby code, etc).
    /// Promotes Starting / Blocked to Online and clears the blocking tag. The
    /// log-line heuristics can lie via false-positive substring matches; direct
    /// reachability is the ground truth, so we let it override.
    /// </summary>
    public void ConfirmOnlineFromCorroboration(string reason)
    {
        if (_health.ConfirmOnlineFromCorroboration(reason))
        {
            PushHealth();
        }

        // Sec 4.6: direct proof the server is online also reconciles the
        // startup timeline. A transient blocking signal earlier in startup
        // (e.g. a stale "sandbox/admin settings path is invalid" log line)
        // would otherwise leave the phase strip and "Startup failed" header
        // pinned to red even after EOS publishes a lobby code, which is the
        // exact contradiction the user sees on the Logs tab.
        if (_startup.OnConfirmedOnline())
        {
            RefreshStartup();
        }
    }

    // ---- A2S corroboration of the live roster (player-count reconcile) ----

    /// <summary>
    /// Latest A2S_INFO query timestamp. Drives the "Live roster verified
    /// at HH:MM:SS" subtle line above the Players column header. Null while
    /// the loop has not produced a successful poll yet.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _lastA2SCheckAt;

    /// <summary>
    /// Whether the A2S corroboration loop's most recent polls succeeded.
    /// Goes false only after a short failure streak so a single dropped
    /// UDP packet does not flip the indicator pessimistic.
    /// </summary>
    [ObservableProperty]
    private bool _isA2SHealthy = true;

    /// <summary>
    /// Human-readable verified-status line for the Players tab. Empty until
    /// the first poll either succeeds (timestamp form) or fails enough to
    /// trip the unhealthy threshold (warning form).
    /// </summary>
    [ObservableProperty]
    private string _a2SVerifiedText = "";

    private int _a2sFailureStreak;

    /// <summary>
    /// Records a successful A2S info reply: drives the "Verified at ..."
    /// status line and hands the live player count to
    /// <see cref="PlayerRosterTracker.ReconcileWithLiveCount"/>. If the
    /// reconciler evicts any phantom rows, republish the roster snapshot
    /// so the Players list updates in place.
    /// </summary>
    public void RecordA2SInfo(A2SInfoSnapshot snapshot)
    {
        _a2sFailureStreak = 0;
        LastA2SCheckAt = snapshot.QueriedAt;
        IsA2SHealthy = true;
        A2SVerifiedText =
            $"Live roster verified at {snapshot.QueriedAt.ToLocalTime():HH:mm:ss}";

        var evicted = _roster.ReconcileWithLiveCount(snapshot.PlayerCount, DateTimeOffset.Now);
        if (evicted.Count > 0)
        {
            RefreshRoster();
        }
    }

    /// <summary>
    /// Records a failed A2S poll (timeout, malformed reply, port closed).
    /// A single failure is silent - the indicator only flips unhealthy after
    /// two consecutive misses, mirroring the eviction debounce so a UDP
    /// flicker does not redden the status line.
    /// </summary>
    public void RecordA2SFailure()
    {
        _a2sFailureStreak++;
        if (_a2sFailureStreak >= 2)
        {
            IsA2SHealthy = false;
            A2SVerifiedText = "Live verification unavailable (A2S not responding)";
        }
    }

    /// <summary>Sec 4.6: republishes the startup timeline snapshot for the UI.</summary>
    private void RefreshStartup()
    {
        var snapshot = _startup.Snapshot;

        StartupPhases.Clear();
        foreach (var phase in snapshot.Phases)
        {
            StartupPhases.Add(phase);
        }

        var total = snapshot.Phases.Count;
        var done = snapshot.Phases.Count(p => p.Status == StartupPhaseStatus.Done);
        var failedPhase = snapshot.Phases.FirstOrDefault(p => p.Status == StartupPhaseStatus.Failed);

        HasStartupSequence = snapshot.IsRunning || failedPhase is not null;
        StartupSummary = failedPhase is not null
            ? $"Startup failed: {failedPhase.Detail}"
            : done == total
                ? $"Startup complete - all {total} phases in {snapshot.Elapsed.TotalSeconds:0.0}s."
                : $"Starting... {done} of {total} phases ({snapshot.Elapsed.TotalSeconds:0.0}s).";
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
        "Not checked. Click 'Check Visibility' (the server must be running and ports forwarded).";

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
    /// Feeds a whole tick's batch of log lines to the roster, then refreshes the
    /// UI ONCE. Returns true when a durable change happened (so the shell can
    /// persist it). Refreshing per line saturated the UI thread on a join burst.
    /// </summary>
    public bool ApplyRosterActivityBatch(IReadOnlyList<ServerLogLine> lines)
    {
        var persist = false;
        foreach (var line in lines)
        {
            var evt = _roster.Apply(line);
            if (evt is null)
            {
                continue;
            }

            persist |= evt.Kind is PlayerRosterEventKind.LoginRequested
                or PlayerRosterEventKind.JoinSucceeded
                or PlayerRosterEventKind.EnteredFacility
                or PlayerRosterEventKind.Disconnected
                or PlayerRosterEventKind.ServerStopped;
        }

        RefreshRoster();
        return persist;
    }

    /// <summary>
    /// Loads the Player Detail tab for the given roster row and switches to it.
    /// Called when the admin double-clicks a player.
    /// </summary>
    public void ShowPlayerDetail(RosterRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.DisplayName))
        {
            return;
        }

        _detailPlayerName = row.DisplayName;
        PopulatePlayerDetail(row.DisplayName);
        LogsSubTabIndex = PlayerDetailSubTabIndex;
    }

    private void PopulatePlayerDetail(string displayName)
    {
        var report = PlayerDetailBuilder.Build(displayName, _roster.History, _roster.Chat);

        SelectedPlayerActivity.Clear();
        foreach (var evt in report.Activity)
        {
            SelectedPlayerActivity.Add(evt);
        }

        SelectedPlayerChat.Clear();
        foreach (var chat in report.Chat)
        {
            SelectedPlayerChat.Add(chat);
        }

        PlayerDetailHeader =
            $"{report.DisplayName} - {report.Activity.Count} activity event(s), " +
            $"{report.Chat.Count(c => c.IsFromPlayer)} chat message(s)";
        HasPlayerDetail = true;
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

        // Sec 3.2: live roster excludes banned ids; they appear only on the
        // Banished sub-tab (Banished page rows below).
        var entries = _roster.Entries;
        var visible = RosterPresentation.FilterActive(entries, BannedIds);

        Roster.Clear();
        foreach (var entry in visible)
        {
            // Sec 3.3: derived IsAdmin per row; decoration only.
            var isAdmin = RosterPresentation.IsAdmin(entry, ModeratorIds);
            Roster.Add(new RosterRowViewModel(entry, isAdmin));
        }

        if (selectedKey is { Length: > 0 })
        {
            SelectedRosterPlayer = Roster.FirstOrDefault(e => e.Key == selectedKey);
        }

        // Keep an open Player Detail tab live as new events/chat arrive.
        if (_detailPlayerName is { Length: > 0 } detailName)
        {
            PopulatePlayerDetail(detailName);
        }

        // Sec 3.2 banished-players page rows (built from the sectioned ids,
        // joined with the full roster for last-known display name).
        BannedPlayers.Clear();
        foreach (var row in RosterPresentation.BuildBannedRows(BannedIds, entries))
        {
            BannedPlayers.Add(row);
        }

        var header = $"Players Online: {_roster.OnlineCount}/{MaxPlayers}";
        if (_roster.ServerPlayerCount is { } count)
        {
            header += $"  -  Server count: {count}";
        }

        if (_roster.CountWarning is { Length: > 0 } warning)
        {
            header += $"  -  {warning}";
        }
        else if (!_roster.HasSeenActivity)
        {
            header = "No players have connected yet.";
        }

        PlayersOnlineText = header;
    }
}
