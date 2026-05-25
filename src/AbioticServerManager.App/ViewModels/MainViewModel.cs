using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using AbioticServerManager.App.Services;
using AbioticServerManager.App.Views;
using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Migration;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Networking;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.Networking;
using AbioticServerManager.Infrastructure.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IInstanceStore _store;
    private readonly IPlayerRosterStore _rosterStore;
    private readonly IInternalIpSnapshotStore _ipSnapshots;
    private readonly IPublicIpProbe _publicIpProbe;
    private readonly IResetManagedDataService _resetManagedData;
    private readonly IWorldIdentityMigrationService _worldIdentityMigration;
    private readonly ILegacyMigrationService _migration;
    private readonly IDiagnosticsService _diagnostics;
    private readonly INetworkSetupService _networkSetup;
    private readonly A2SQueryClient _a2s;
    private readonly ISteamCmdService _steamCmd;
    private readonly IServerProcessService _processes;
    private readonly ISandboxSettingsService _sandbox;
    private readonly ISandboxRuntimeStagingService _sandboxStaging;
    private readonly IBackupService _backups;
    private readonly IAdminListService _adminList;
    private readonly IPlayerBanService _bans;
    private readonly IServerInstallStateService _serverInstallState;
    private readonly IWorldIntegrityInspector _worldIntegrity;
    private readonly IAppPaths _paths;
    private readonly ILogger<MainViewModel> _logger;
    private bool _installPromptShown;
    private bool _suppressAutoLoadForSelection;
    private ServerInstanceViewModel? _observedSelectedWorld;

    // Serializes instance-store writes so overlapping saves (e.g. rapid tab
    // switching firing AutoLoadSandboxAsync) cannot interleave on the JSON file.
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    // Per-world follower of AbioticFactor.log (the rich LogNet source the
    // captured stdout does not carry). Keyed by world id.
    private readonly Dictionary<string, AbioticServerLogTail> _logTails = new();

    // Per-world A2S corroboration loop: while the server is running, periodically
    // queries 127.0.0.1:QueryPort. A reply is empirical proof the server is up
    // (it's the same signal Steam's master server uses), and promotes Health to
    // Online if log heuristics false-positived Blocked. Keyed by world id.
    private readonly Dictionary<string, CancellationTokenSource> _a2sCorroborators = new();

    public MainViewModel(
        IInstanceStore store,
        IPlayerRosterStore rosterStore,
        IInternalIpSnapshotStore ipSnapshots,
        IPublicIpProbe publicIpProbe,
        IResetManagedDataService resetManagedData,
        IWorldIdentityMigrationService worldIdentityMigration,
        ILegacyMigrationService migration,
        IDiagnosticsService diagnostics,
        INetworkSetupService networkSetup,
        A2SQueryClient a2s,
        ISteamCmdService steamCmd,
        IServerProcessService processes,
        ISandboxSettingsService sandbox,
        ISandboxRuntimeStagingService sandboxStaging,
        IBackupService backups,
        IAdminListService adminList,
        IPlayerBanService bans,
        IServerInstallStateService serverInstallState,
        IWorldIntegrityInspector worldIntegrity,
        IAppPaths paths,
        ILogger<MainViewModel> logger)
    {
        _store = store;
        _rosterStore = rosterStore;
        _ipSnapshots = ipSnapshots;
        _publicIpProbe = publicIpProbe;
        _resetManagedData = resetManagedData;
        _worldIdentityMigration = worldIdentityMigration;
        _migration = migration;
        _diagnostics = diagnostics;
        _networkSetup = networkSetup;
        _a2s = a2s;
        _steamCmd = steamCmd;
        _processes = processes;
        _sandbox = sandbox;
        _sandboxStaging = sandboxStaging;
        _backups = backups;
        _adminList = adminList;
        _bans = bans;
        _serverInstallState = serverInstallState;
        _worldIntegrity = worldIntegrity;
        _paths = paths;
        _logger = logger;

        _processes.LogReceived += OnLogReceived;
        _processes.RuntimeChanged += OnRuntimeChanged;
        Worlds.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWorlds));
    }

    public ObservableCollection<ServerInstanceViewModel> Worlds { get; } = [];

    /// <summary>True once at least one world exists. Drives the first-run
    /// clean-slate empty state versus the horizontal world tab strip.</summary>
    public bool HasWorlds => Worlds.Count > 0;

    /// <summary>
    /// Whether the Create / Clone / Delete / Save cluster on the title card is
    /// shown. Expanded on a clean slate (no worlds); slides away once the first
    /// world exists. The user toggles it back by clicking the title.
    /// </summary>
    [ObservableProperty]
    private bool _areWorldActionsExpanded;

    [RelayCommand]
    private void ToggleWorldActions() =>
        AreWorldActionsExpanded = !AreWorldActionsExpanded;

    /// <summary>
    /// True once the shared dedicated server is installed and launchable. Drives
    /// the first-run flow - the server is prepared before any world is created.
    /// </summary>
    [ObservableProperty]
    private bool _isServerPrepared;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckNetworkSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFirewallRulesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyRouterChecklistCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNetworkDiagnosticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSandboxCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetServerTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackupNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenWorldFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFreshWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAdminCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAdminFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckExternalVisibilityCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallOrUpdateServerCommand))]
    private ServerInstanceViewModel? _selectedWorld;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckNetworkSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFirewallRulesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyRouterChecklistCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyNetworkDiagnosticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallOrUpdateServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFreshWorldCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _headerInfoText = AppVersionText() + "  |  No world selected";

    [ObservableProperty]
    private string _busyStatus = "";

    [ObservableProperty]
    private double? _busyProgress;

    [ObservableProperty]
    private string _busyTitle = "Working...";

    [ObservableProperty]
    private string _busyDetail = "";

    [ObservableProperty]
    private string _busyPercentText = "";

    [ObservableProperty]
    private bool _busyIsIndeterminate = true;

    private static string PhaseTitle(InstallPhase phase) => phase switch
    {
        InstallPhase.DownloadingSteamCmd => "Downloading SteamCMD",
        InstallPhase.ExtractingSteamCmd => "Extracting SteamCMD",
        InstallPhase.UpdatingSteamCmd => "Updating SteamCMD",
        InstallPhase.InstallingServer => "Installing / updating dedicated server",
        InstallPhase.ValidatingServer => "Validating dedicated server files",
        InstallPhase.Completed => "Finishing up",
        InstallPhase.Failed => "Setup failed",
        _ => "Working...",
    };

    private void ApplyBusyProgress(InstallProgress p)
    {
        BusyTitle = PhaseTitle(p.Phase);
        BusyStatus = p.Status;
        BusyDetail = p.OutputLine ?? "";
        if (p.PercentComplete is { } pct)
        {
            var clamped = Math.Clamp(pct, 0, 100);
            BusyProgress = clamped;
            BusyPercentText = $"{clamped:0}%";
            BusyIsIndeterminate = false;
        }
        else
        {
            BusyProgress = 0;
            BusyPercentText = "";
            BusyIsIndeterminate = true;
        }
    }

    private void ResetBusyProgress()
    {
        BusyStatus = "";
        BusyProgress = null;
        BusyTitle = "Working...";
        BusyDetail = "";
        BusyPercentText = "";
        BusyIsIndeterminate = true;
    }

    /// <summary>
    /// Empty when the LAN IPv4 has not changed since the last launch (or this is
    /// the first run). Populated with a single warning sentence when it HAS
    /// changed - port forwarding may need attention.
    /// </summary>
    [ObservableProperty]
    private string _internalIpChangeBannerText = "";

    public bool HasInternalIpChangeBanner => !string.IsNullOrEmpty(InternalIpChangeBannerText);

    partial void OnInternalIpChangeBannerTextChanged(string value) =>
        OnPropertyChanged(nameof(HasInternalIpChangeBanner));

    [RelayCommand]
    private void DismissInternalIpChangeBanner() => InternalIpChangeBannerText = "";

    /// <summary>This PC's current best LAN IPv4 (the address friends on the LAN reach).</summary>
    [ObservableProperty]
    private string _lanIpv4 = "-";

    /// <summary>This PC's public IPv4 (what friends on the internet reach via port forwarding).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublicIpv4Display))]
    private string _publicIpv4 = "checking...";

    /// <summary>
    /// Whether the public IP is revealed. Hidden by default - it is moderately
    /// sensitive - and toggled from a right-click "Show / Hide" menu.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublicIpv4Display))]
    [NotifyPropertyChangedFor(nameof(PublicIpToggleLabel))]
    private bool _isPublicIpVisible;

    /// <summary>The public IP for display - the address when revealed, a mask otherwise.</summary>
    public string PublicIpv4Display => IsPublicIpVisible ? PublicIpv4 : "********";

    /// <summary>Right-click menu label - flips between Show and Hide.</summary>
    public string PublicIpToggleLabel =>
        IsPublicIpVisible ? "Hide public IP" : "Show public IP";

    [RelayCommand]
    private void TogglePublicIpVisibility() => IsPublicIpVisible = !IsPublicIpVisible;

    /// <summary>
    /// Sec 3.1b: composes router-agnostic "lock this IP" guidance, copies it to
    /// the clipboard, and shows it in a dismissible dialog so the user can
    /// follow along while opening the router admin page.
    /// </summary>
    [RelayCommand]
    private void ShowLanIpLockGuidance()
    {
        var ctx = new LanIpLockContext
        {
            Ipv4 = string.IsNullOrWhiteSpace(LanIpv4) || LanIpv4 == "-" ? "" : LanIpv4,
            Hostname = Environment.MachineName,
            MacAddress = "",  // Ipv4Candidate doesn't carry MAC; we surface a fallback hint instead.
            Gateway = _bestLanCandidate?.GatewayAddress ?? "",
            AdapterDescription = _bestLanCandidate?.InterfaceDescription ?? "",
        };

        var text = LanIpLockGuidance.Compose(ctx);

        var copied = ClipboardHelper.TryCopy(text);
        if (!copied)
        {
            _logger.LogDebug("Could not copy lock-IP guidance to clipboard");
        }

        MessageBox.Show(
            text + (copied
                ? "\n\n(Copied to clipboard.)"
                : "\n\n(Could not copy to clipboard - another app may be holding it. Select and copy manually if needed.)"),
            "Facility Overseer - Lock LAN IP",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task RefreshPublicIpAsync()
    {
        PublicIpv4 = "checking...";
        try
        {
            var probed = await _publicIpProbe.ProbeAsync();
            PublicIpv4 = string.IsNullOrEmpty(probed) ? "unavailable" : probed;
        }
        catch
        {
            PublicIpv4 = "unavailable";
        }
    }

    /// <summary>
    /// Erases everything Facility Overseer manages - both the durable
    /// <c>DataRoot</c> tree AND the volatile SteamCMD/server tree - in one
    /// confirmed action. The data-root choice is preserved so the user keeps
    /// the location they picked on first run.
    /// </summary>
    [RelayCommand]
    private async Task ResetManagedDataAsync()
    {
        if (Worlds.Any(w => w.IsRunningState))
        {
            MessageBox.Show(
                "Stop all running worlds before resetting managed data.",
                "Facility Overseer - Reset Managed Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            "This deletes EVERYTHING Facility Overseer manages:\n" +
            "  - All world profiles, sandbox settings, admins, bans, roster\n" +
            "  - Backups\n" +
            "  - SteamCMD and the dedicated server install\n" +
            "  - Logs\n\n" +
            $"DataRoot:     {_paths.DataRoot}\n" +
            "Anything outside these managed folders is left alone.\n\n" +
            "Your data-root choice (where the new clean state will live) is " +
            "preserved.\n\nProceed?",
            "Facility Overseer - Reset Everything Managed",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _resetManagedData.ResetAsync();

            // Clear in-memory world state so the UI matches disk after reset.
            Worlds.Clear();
            SelectedWorld = null;
            AreWorldActionsExpanded = true;

            var summary = result.Success
                ? $"Removed {result.RemovedPaths.Count} item(s)."
                : $"Removed {result.RemovedPaths.Count} item(s); " +
                  $"{result.FailedPaths.Count} could not be removed (locked or in use).";

            MessageBox.Show(
                summary + "\n\nReport saved to:\n" + result.ReportPath +
                "\n\nRestart Facility Overseer to start fresh.",
                "Facility Overseer - Reset Managed Data",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset managed data failed");
            MessageBox.Show(
                "The reset could not complete:\n\n" + ex.Message,
                "Facility Overseer - Reset Managed Data",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public async Task InitializeAsync()
    {
        await MaybeMigrateLegacyDataAsync();
        await DetectInternalIpChangeAsync();
        _ = ProbePublicIpInBackgroundAsync(); // best-effort; do not block startup

        var loaded = await _store.LoadAsync();
        var migratedAny = false;
        foreach (var model in loaded)
        {
            // Sec 2.1: ensure per-world INIs are under <DataRoot>/worlds/<id>/config/
            // before any downstream service (sandbox load, admin list, launch args)
            // reads from instance.SandboxIniPath / AdminIniPath.
            var migration = await _worldIdentityMigration.MigrateIfNeededAsync(model);
            if (migration.HadWork)
            {
                migratedAny = true;
                _logger.LogInformation(
                    "Sec 2.1 migration for world {Id}: copied {Count} file(s)",
                    model.Id,
                    migration.CopiedDescriptions.Count);
            }

            Worlds.Add(CreateWorldVm(model));
        }

        if (migratedAny)
        {
            // Persist updated SandboxIniPath / AdminIniPath so the new layout
            // sticks across launches.
            try
            {
                await _store.SaveAsync([.. loaded]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist Sec 2.1 migration path updates");
            }
        }

        // First launch is a deliberate clean slate (UI Tweaks A6): no auto-created
        // world and no world tabs - just the title card and an empty-state prompt.
        // The Create / Clone / Delete / Save cluster stays open until the user
        // makes their first world.
        AreWorldActionsExpanded = Worlds.Count == 0;

        // The dedicated server is a shared, world-independent tool. Evaluate it up
        // front so the UI leads with server preparation - the server is downloaded
        // before any world exists (UI Tweaks F6).
        RefreshServerInstallState();

        if (Worlds.Count == 0)
        {
            // Clean slate: still prompt to prepare the server (server before world).
            await MaybePromptInstallAsync();
            return;
        }

        SelectedWorld = Worlds.FirstOrDefault();
        foreach (var world in Worlds)
        {
            world.IsRunningState = _processes.IsRunning(world.Id);
            RefreshInstallStatus(world);
            await SeedRosterAsync(world);
        }

        await MaybePromptInstallAsync();
    }

    private async Task MaybeMigrateLegacyDataAsync()
    {
        try
        {
            if (!_migration.ShouldOfferMigration(out var findings))
            {
                return;
            }

            var dialog = new LegacyDataImportDialog(findings)
            {
                Owner = Application.Current?.MainWindow,
            };

            dialog.ShowDialog();
            switch (dialog.Choice)
            {
                case LegacyImportChoice.Import:
                    await ImportLegacyDataAsync(findings);
                    break;

                case LegacyImportChoice.StartFresh:
                    await _migration.MarkMigrationDeclinedAsync();
                    break;

                // Dismissed: leave the marker absent so the user is asked again
                // next launch (a misclick on the close button shouldn't lock the
                // decision in either direction).
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy migration check failed");
        }
    }

    private async Task ImportLegacyDataAsync(IReadOnlyList<LegacyFinding> findings)
    {
        var result = await _migration.MigrateAsync(findings);
        var rebased = 0;
        if (result.ImportedConfig)
        {
            rebased = await RebaseImportedInstancesAsync();
        }

        var summary = result.ImportedConfig
            ? rebased > 0
                ? $"Previous world profiles were imported. {rebased} profile(s) had stale paths " +
                  "from the old layout - those have been reset so the worlds resolve under the " +
                  "current data folder."
                : "Previous world profiles were imported."
            : "No profiles were imported (current data folder already has its own).";

        MessageBox.Show(
            summary + "\n\nA migration report was saved to:\n" + result.ReportPath,
            "Facility Overseer - Import Previous Data");
    }

    /// <summary>
    /// After legacy <c>instances.json</c> has been copied in, walk the freshly
    /// imported profiles and reset any install/sandbox/admin/world paths that
    /// no longer exist on disk. Returns the number of profiles that changed
    /// so the import confirmation can be honest about it.
    /// </summary>
    private async Task<int> RebaseImportedInstancesAsync()
    {
        var loaded = await _store.LoadAsync();
        if (loaded.Count == 0)
        {
            return 0;
        }

        var rebased = new List<ServerInstance>(loaded.Count);
        var changed = 0;
        foreach (var instance in loaded)
        {
            var clean = LegacyPathRebase.ScrubStalePaths(
                instance,
                _paths.DefaultServerInstallDirectory,
                Directory.Exists,
                File.Exists);

            if (HasPathDiff(instance, clean))
            {
                changed++;
                rebased.Add(clean);
            }
            else
            {
                rebased.Add(instance);
            }
        }

        if (changed > 0)
        {
            await _store.SaveAsync(rebased);
        }

        return changed;
    }

    private static bool HasPathDiff(ServerInstance a, ServerInstance b) =>
        !string.Equals(a.InstallPath, b.InstallPath, StringComparison.Ordinal) ||
        !string.Equals(a.WorldPath, b.WorldPath, StringComparison.Ordinal) ||
        !string.Equals(a.SandboxIniPath, b.SandboxIniPath, StringComparison.Ordinal) ||
        !string.Equals(a.AdminIniPath, b.AdminIniPath, StringComparison.Ordinal);

    /// <summary>
    /// Sec 3.1b cache of the best LAN candidate's full detail (address, gateway,
    /// adapter description) so the "Lock this IP" command can compose the
    /// guidance without re-probing.
    /// </summary>
    private Ipv4Candidate? _bestLanCandidate;

    private async Task DetectInternalIpChangeAsync()
    {
        try
        {
            var selection = _networkSetup.DetectLanIpv4();
            var current = selection.Best;
            LanIpv4 = string.IsNullOrEmpty(current) ? "-" : current;
            _bestLanCandidate = selection.UsableCandidates.FirstOrDefault();

            var lastSeen = await _ipSnapshots.LoadAsync();
            var change = InternalIpChangeTracker.Detect(lastSeen, current);

            InternalIpChangeBannerText = change switch
            {
                InternalIpChange.Changed =>
                    $"Your LAN IPv4 changed from {lastSeen!.Ipv4} to {current}. If you " +
                    "have router port forwarding pinned to the old address, friends may " +
                    "no longer be able to join. Re-check the Network tab.",
                InternalIpChange.Lost when lastSeen is not null =>
                    $"No LAN IPv4 detected (last seen {lastSeen.Ipv4}). Reconnect to your " +
                    "network and re-check the Network tab.",
                _ => "",
            };

            // Only refresh the snapshot when we actually have a current IP - never
            // clobber a known-good last-seen with a transient nothing.
            var snapshot = InternalIpChangeTracker.SnapshotFor(current, DateTimeOffset.UtcNow);
            if (snapshot is not null)
            {
                await _ipSnapshots.SaveAsync(snapshot);
            }
        }
        catch (Exception ex)
        {
            // Detection is best-effort UX; never block startup on it.
            _logger.LogWarning(ex, "Internal IP change detection failed");
        }
    }

    private async Task ProbePublicIpInBackgroundAsync()
    {
        try
        {
            var probed = await _publicIpProbe.ProbeAsync();
            PublicIpv4 = string.IsNullOrEmpty(probed) ? "unavailable" : probed;
        }
        catch (Exception ex)
        {
            PublicIpv4 = "unavailable";
            _logger.LogDebug(ex, "Initial public-IP probe failed (best-effort)");
        }
    }

    private async Task SeedRosterAsync(ServerInstanceViewModel world)
    {
        try
        {
            var known = await _rosterStore.LoadAsync(world.Id);
            if (known.Count > 0)
            {
                world.SeedRoster(known);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not seed roster for world {Id}", world.Id);
        }
    }

    private void ScheduleRosterSave(ServerInstanceViewModel world)
    {
        var id = world.Id;
        var snapshot = world.ExportRoster();
        _ = Task.Run(async () =>
        {
            try
            {
                await _rosterStore.SaveAsync(id, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Roster save failed for world {Id}", id);
            }
        });
    }

    private void StartLogTail(ServerInstanceViewModel world)
    {
        StopLogTail(world.Id);

        var installPath = world.Model.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return;
        }

        try
        {
            _logTails[world.Id] = AbioticServerLogTail.Start(
                world.Id,
                installPath,
                lines => Dispatcher().BeginInvoke(() => OnTailLines(world, lines)),
                _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start AF log tail for {Id}", world.Id);
        }
    }

    private void StopLogTail(string instanceId)
    {
        if (_logTails.Remove(instanceId, out var tail))
        {
            tail.Dispose();
        }
    }

    /// <summary>
    /// Periodically polls A2S against 127.0.0.1:QueryPort for the entire
    /// lifetime of the running server. Has two roles:
    /// <list type="bullet">
    /// <item><b>Pre-Online:</b> a successful reply is direct evidence the
    /// server is up, even if the log heuristics false-positived Blocked.
    /// Promotes Health to Online via <c>ConfirmOnlineFromCorroboration</c>
    /// and clears any stuck startup-phase failures.</item>
    /// <item><b>Post-Online:</b> the parsed <c>PlayerCount</c> field is the
    /// ground truth for "how many clients are actually connected". Hands it
    /// to <c>PlayerRosterTracker.ReconcileWithLiveCount</c> so a missed
    /// disconnect log line cannot leave a phantom row stuck "online".</item>
    /// </list>
    /// Cadence is 5 s pre-Online (to settle the green light quickly) and
    /// 60 s post-Online (low ambient cost; the debounced reconcile in the
    /// tracker tolerates the wider window). A failed query is a silent
    /// no-op for the roster - the debouncers handle the transient case.
    /// </summary>
    private void StartA2SCorroborator(ServerInstanceViewModel world)
    {
        StopA2SCorroborator(world.Id);

        var cts = new CancellationTokenSource();
        _a2sCorroborators[world.Id] = cts;

        var port = world.QueryPort;
        if (port is <= 0 or > 65535)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Initial settle delay: avoid hammering the port while the
                // server is still binding sockets in the first second.
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);

                while (!cts.IsCancellationRequested)
                {
                    A2SInfoSnapshot? snapshot;
                    try
                    {
                        snapshot = await _a2s
                            .QueryInfoAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "A2S corroboration probe threw for {Id}", world.Id);
                        snapshot = null;
                    }

                    if (snapshot is not null)
                    {
                        var snap = snapshot;
                        _ = Dispatcher().BeginInvoke(() =>
                        {
                            // Promotion to Online (no-op if already Online).
                            world.ConfirmOnlineFromCorroboration(
                                $"A2S query on 127.0.0.1:{port} replied - the server is reachable.");

                            // Roster reconcile (debounced; no-op if counts match).
                            world.RecordA2SInfo(snap);
                        });
                    }
                    else
                    {
                        _ = Dispatcher().BeginInvoke(() => world.RecordA2SFailure());
                    }

                    // Slow cadence once Online (ambient verification); faster
                    // while still climbing toward Online so the green light
                    // doesn't lag behind reality.
                    var delay = world.Health == ServerHealth.Online
                        ? TimeSpan.FromSeconds(60)
                        : TimeSpan.FromSeconds(5);
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal teardown.
            }
        }, cts.Token);
    }

    private void StopA2SCorroborator(string instanceId)
    {
        if (_a2sCorroborators.Remove(instanceId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed during a previous teardown.
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    private void OnTailLines(ServerInstanceViewModel world, IReadOnlyList<ServerLogLine> lines)
    {
        // Process the whole tick's batch, then refresh the roster UI ONCE.
        // Refreshing per line saturated the UI thread on a join burst.
        foreach (var line in lines)
        {
            world.ApplyHealth(line);
        }

        if (world.ApplyRosterActivityBatch(lines))
        {
            ScheduleRosterSave(world);
        }
    }

    /// <summary>
    /// First-run guidance: if the selected world has no prepared server, invite the
    /// user to prepare the app-managed server payload now. Shown once per session.
    /// </summary>
    private async Task MaybePromptInstallAsync()
    {
        if (_installPromptShown)
        {
            return;
        }

        _installPromptShown = true;

        // The dedicated server is world-independent - evaluate the shared managed
        // install so this prompt can lead the first run before any world exists.
        var state = RefreshServerInstallState();
        if (state.IsLaunchable)
        {
            return;
        }

        var choice = MessageBox.Show(
            "The Abiotic Factor dedicated server is not prepared yet.\n\n" +
            "Facility Overseer can download and manage the server files inside its own data folder:\n\n" +
            _paths.ManagedServerDirectory + "\n\n" +
            "This also fetches SteamCMD automatically - no Steam login required.\n\n" +
            "Prepare the server now?",
            "Facility Overseer - Welcome",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (choice == MessageBoxResult.Yes)
        {
            await InstallOrUpdateServer();
        }
    }

    private ServerInstanceViewModel CreateWorldVm(ServerInstance model)
    {
        var vm = new ServerInstanceViewModel(model)
        {
            Sandbox = new SandboxSettingsViewModel(_sandbox),
        };

        // B1: when the world reaches Online for the first time per process,
        // auto-refresh the Network panel so stale "server stopped" rows
        // flip to live "bound + answering" rows without the user clicking
        // Check Setup again.
        vm.PropertyChanged += OnWorldPropertyChanged;
        return vm;
    }

    private void OnWorldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ServerInstanceViewModel world)
        {
            return;
        }

        if (e.PropertyName != nameof(ServerInstanceViewModel.Health))
        {
            return;
        }

        // B3: any Health change re-runs guidance so the red Recovery Flow
        // panel clears as soon as the world recovers (A2S corroboration,
        // lobby code, or a later readiness signal flipping Blocked -> Online).
        // RefreshGuidance is also what re-evaluates RecommendedActions, so
        // a stale "HANDLE_BLOCKED" prompt clears at the same time.
        RefreshGuidance(world);

        if (world.Health == ServerHealth.Online)
        {
            MaybeAutoRefreshNetworkChecks(world);
        }
    }

    // World ids whose Network panel has already been auto-refreshed for the
    // current Online window. Cleared in OnRuntimeChanged when the world stops,
    // so a restart re-arms the refresh.
    private readonly HashSet<string> _networkAutoRefreshDone = new();

    private void MaybeAutoRefreshNetworkChecks(ServerInstanceViewModel world)
    {
        if (!_networkAutoRefreshDone.Add(world.Id))
        {
            return; // already refreshed once this Online window
        }

        if (world.IsNetworkChecking)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Small settle delay so the port-binding/process-check rows
                // see the final state and don't catch the server mid-bind.
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                var status = await _networkSetup.InspectAsync(world.Model).ConfigureAwait(false);
                _ = Dispatcher().BeginInvoke(() => ApplyNetworkSetupStatus(world, status));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Auto network re-check failed for {Id}", world.Id);
            }
        });
    }

    private ServerInstance CreateDefaultInstance(string name) => new()
    {
        DisplayName = name,
        SteamServerName = name,
        WorldSaveName = name,
        InstallPath = _paths.DefaultServerInstallDirectory,
    };

    [RelayCommand]
    private async Task CreateWorld()
    {
        // UI Tweaks A7: prompt for a name and difficulty before creating.
        var dialog = new CreateWorldDialog($"World {Worlds.Count + 1}")
        {
            Owner = Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var world = await AddWorldAsync(dialog.WorldName);
        await ApplyCreateWorldDifficultyAsync(world, dialog.GameDifficulty);
    }

    /// <summary>
    /// Writes the difficulty chosen in the Create World dialog into the new
    /// world's freshly-staged SandboxSettings.ini - the GameDifficulty key in
    /// the World category. No-op if the sandbox exposes no such key.
    /// </summary>
    private static async Task ApplyCreateWorldDifficultyAsync(
        ServerInstanceViewModel world, string difficultyValue)
    {
        if (world.Sandbox is not { } sandbox)
        {
            return;
        }

        var difficulty = sandbox.Categories
            .SelectMany(c => c.Settings)
            .FirstOrDefault(s =>
                string.Equals(s.Key, "GameDifficulty", StringComparison.OrdinalIgnoreCase));
        if (difficulty is null)
        {
            return;
        }

        difficulty.StringValue = difficultyValue;
        await sandbox.SaveAsync();
    }

    /// <summary>
    /// Shared world-creation path used by the Create World button and by first-run
    /// bootstrap. Adds the profile, selects it without a redundant auto-load,
    /// persists, offers the install prompt, then stages the sandbox config.
    /// </summary>
    private async Task<ServerInstanceViewModel> AddWorldAsync(string name)
    {
        var vm = CreateWorldVm(CreateDefaultInstance(name));
        Worlds.Add(vm);
        _suppressAutoLoadForSelection = true;
        try
        {
            SelectedWorld = vm;
        }
        finally
        {
            _suppressAutoLoadForSelection = false;
        }

        await SaveAsync();
        await SeedRosterAsync(vm);
        await MaybePromptInstallAsync();
        await AutoLoadSandboxAsync(vm);

        // UI Tweaks A3: once a world exists, tuck the title-card action cluster
        // away. Clicking the FACILITY OVERSEER title brings it back.
        AreWorldActionsExpanded = false;
        return vm;
    }

    private bool HasSelection() => SelectedWorld is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CloneWorld()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var clone = SelectedWorld.Model.Clone();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.DisplayName = SelectedWorld.DisplayName + " (copy)";
        var vm = CreateWorldVm(clone);
        Worlds.Add(vm);
        SelectedWorld = vm;
        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteWorld()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        if (_processes.IsRunning(SelectedWorld.Id))
        {
            MessageBox.Show("Stop the world before deleting it.", "Facility Overseer");
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete world \"{SelectedWorld.DisplayName}\"? This removes the profile only, not the save files.",
            "Facility Overseer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var toRemove = SelectedWorld;
        await AutoBackupAsync(toRemove, "before-delete");
        Worlds.Remove(toRemove);
        SelectedWorld = Worlds.FirstOrDefault();

        // UI Tweaks A6: deleting the last world returns to the clean slate, so
        // re-open the title-card action cluster.
        if (Worlds.Count == 0)
        {
            AreWorldActionsExpanded = true;
        }

        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ResetServerTab()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        if (MessageBox.Show(
                "Reset all Server tab settings (name, passwords, ports, limits, install path) " +
                "for this world back to defaults? The world's display name and sandbox files are kept.",
                "Facility Overseer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var d = new ServerInstance();
        SelectedWorld.SteamServerName = d.SteamServerName;
        SelectedWorld.WorldSaveName = d.WorldSaveName;
        SelectedWorld.ServerPassword = d.ServerPassword;
        SelectedWorld.AdminPassword = d.AdminPassword;
        SelectedWorld.MaxPlayers = d.MaxPlayers;
        SelectedWorld.GamePort = d.GamePort;
        SelectedWorld.QueryPort = d.QueryPort;
        SelectedWorld.LanOnly = d.LanOnly;
        SelectedWorld.UseLocalIps = d.UseLocalIps;
        SelectedWorld.PlatformAccessMode = d.PlatformAccessMode;
        SelectedWorld.MultiHomeAddress = d.MultiHomeAddress;
        SelectedWorld.InstallPath = _paths.DefaultServerInstallDirectory;
        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ValidateConfig()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var others = Worlds.Where(w => w.Id != SelectedWorld.Id).Select(w => w.Model).ToList();
        var results = await _diagnostics.ValidateConfigAsync(SelectedWorld.Model, others);

        SelectedWorld.Diagnostics.Clear();
        foreach (var r in results)
        {
            SelectedWorld.Diagnostics.Add(r);
        }
        SelectedWorld.RebuildNotificationChips();

        await SaveAsync();
    }

    private bool CanCheckVisibility() =>
        SelectedWorld is not null && !SelectedWorld.IsVisibilityChecking;

    [RelayCommand(CanExecute = nameof(CanCheckVisibility))]
    private async Task CheckExternalVisibility()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        world.IsVisibilityChecking = true;
        CheckExternalVisibilityCommand.NotifyCanExecuteChanged();
        world.ExternalVisibilityText = "Checking external reachability (Steam query)...";
        try
        {
            var result = await _diagnostics.CheckExternalVisibilityAsync(world.Model);
            world.ExternalVisibilityStatus = result.Status;
            world.ExternalVisibilityText = result.Detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External visibility check failed for {Id}", world.Id);
            world.ExternalVisibilityStatus = CheckStatus.Unknown;
            world.ExternalVisibilityText = "Visibility check failed: " + ex.Message;
        }
        finally
        {
            world.IsVisibilityChecking = false;
            CheckExternalVisibilityCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanUseNetworkSetup() => SelectedWorld is not null && !IsBusy;

    private bool CanRepairFirewallRules() =>
        SelectedWorld is not null &&
        !IsBusy &&
        NetworkPortValidation.Validate(
            SelectedWorld.GamePort,
            SelectedWorld.QueryPort).CanCreateRules;

    [RelayCommand(CanExecute = nameof(CanUseNetworkSetup))]
    private async Task CheckNetworkSetup()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        world.IsNetworkChecking = true;
        world.NetworkStatusText = "Checking Windows Firewall and local network addresses...";

        try
        {
            var status = await _networkSetup.InspectAsync(world.Model);
            ApplyNetworkSetupStatus(world, status);
            await SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network setup check failed");
            world.NetworkStatusText = "Network setup check failed: " + ex.Message;
        }
        finally
        {
            world.IsNetworkChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRepairFirewallRules))]
    private async Task CreateFirewallRules()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            "Facility Overseer will ask Windows for administrator permission and create inbound " +
            "allow rules for this world's UDP game port, UDP query port, and dedicated server " +
            "executable. Router settings will not be changed.\n\nContinue?",
            "Facility Overseer - Windows Firewall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var world = SelectedWorld;
        IsBusy = true;
        BusyStatus = "Waiting for Windows Firewall permission...";
        BusyProgress = null;

        try
        {
            var result = await _networkSetup.EnsureFirewallRulesAsync(world.Model);

            if (result.Success)
            {
                world.Model.Network.LastFirewallRepairAtUtc = DateTimeOffset.UtcNow;
                MessageBox.Show(
                    result.Message + "\n\nExternal router reachability is still unknown. " +
                    "Complete the router checklist and ask someone outside your network to test joining.",
                    "Facility Overseer - Windows Firewall",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (result.NeedsAdmin)
            {
                MessageBox.Show(
                    result.Message,
                    "Facility Overseer - Administrator Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    result.Message,
                    "Facility Overseer - Windows Firewall",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            var status = await _networkSetup.InspectAsync(world.Model);
            ApplyNetworkSetupStatus(world, status);
            await SaveAsync();
        }
        finally
        {
            IsBusy = false;
            BusyStatus = "";
            BusyProgress = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseNetworkSetup))]
    private async Task CopyRouterChecklist()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        world.IsNetworkChecking = true;
        try
        {
            var status = await _networkSetup.InspectAsync(world.Model);
            ApplyNetworkSetupStatus(world, status);

            IReadOnlyList<string> lines = [.. world.RouterChecklist];
            var copied = ClipboardHelper.TryCopy(string.Join(Environment.NewLine, lines));

            var target = status.SuggestedRouterTarget ?? "the current LAN IPv4";
            MessageBox.Show(
                copied
                    ? $"Router checklist copied for {target}. Reserve this IP in DHCP."
                    : $"Could not copy the router checklist (clipboard busy). Inspection completed for {target}.",
                "Facility Overseer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy router checklist");
            MessageBox.Show(
                "Could not copy the router checklist:\n\n" + ex.Message,
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            world.IsNetworkChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseNetworkSetup))]
    private async Task CopyNetworkDiagnostics()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        world.IsNetworkChecking = true;
        try
        {
            var status = await _networkSetup.InspectAsync(world.Model);
            ApplyNetworkSetupStatus(world, status);
            await SaveAsync();

            var copied = ClipboardHelper.TryCopy(BuildNetworkDiagnosticsText(world, status));
            MessageBox.Show(
                copied
                    ? "Network diagnostics copied to the clipboard."
                    : "Could not copy network diagnostics (clipboard busy). Diagnostics ran successfully.",
                "Facility Overseer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy network diagnostics");
            MessageBox.Show(
                "Could not copy network diagnostics:\n\n" + ex.Message,
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            world.IsNetworkChecking = false;
        }
    }

    private void ApplyNetworkSetupStatus(
        ServerInstanceViewModel world,
        NetworkSetupStatus status)
    {
        world.NetworkChecks.Clear();
        foreach (var check in status.Checks)
        {
            world.NetworkChecks.Add(check);
        }

        world.RouterChecklist.Clear();
        foreach (var step in status.RouterChecklist)
        {
            world.RouterChecklist.Add(step);
        }

        world.LocalIpv4Addresses.Clear();
        foreach (var address in status.LocalIpv4CandidateDetails.Count > 0
                     ? status.LocalIpv4CandidateDetails
                     : status.LocalIpv4Addresses)
        {
            world.LocalIpv4Addresses.Add(address);
        }

        world.RouterTargetText = status.SuggestedRouterTarget is { Length: > 0 } target
            ? target
            : "No LAN IPv4 address detected";
        world.ServerExecutableText = status.ServerExecutablePath ?? "Dedicated server executable not found";
        world.LastFirewallRepairText = status.LastFirewallRepairAtUtc is { } repairedAt
            ? $"Last repaired {FormatUtc(repairedAt)}"
            : "No firewall repair has been run for this world yet.";

        world.FirewallRulesConfigured = status.AreFirewallRulesConfigured;
        world.FirewallSummaryText = status.AreFirewallRulesConfigured
            ? "All required Facility Overseer firewall rules exist and verified correctly."
            : "One or more required Facility Overseer firewall rules are missing or incorrect. " +
              "Use Create / Repair Windows Firewall Rules.";

        var notes = new List<string>();

        var portErrors = status.PortValidationMessages
            .Where(m => m.Severity == DiagnosticSeverity.Error)
            .Select(m => m.Message)
            .ToList();
        if (portErrors.Count > 0)
        {
            notes.Add("Invalid ports: " + string.Join(" ", portErrors));
        }

        var portWarnings = status.PortValidationMessages
            .Where(m => m.Severity == DiagnosticSeverity.Warning)
            .Select(m => m.Title)
            .ToList();
        if (portWarnings.Count > 0)
        {
            notes.Add("Warnings: " + string.Join("; ", portWarnings) + ".");
        }

        if (status.MultipleIpWarning is { Length: > 0 } ipWarn)
        {
            notes.Add(ipWarn);
        }

        var drift = status.Checks.FirstOrDefault(c => c.Id == "lan.routerTargetDrift");
        if (drift?.Status == NetworkCheckStatus.Warn)
        {
            notes.Add(drift.Summary);
        }

        if (status.Environment is { } env)
        {
            notes.Add(
                $"Elevated: {(env.IsElevated ? "yes" : "no")}. " +
                $"Network profile: {env.NetworkProfile}. " +
                $"Server process: {(env.ServerProcessRunning ? "running" : "stopped")}.");
        }

        if (status.InspectionError is { Length: > 0 } error)
        {
            notes.Add("Firewall inspection was incomplete: " + ShortStatus(error));
        }

        world.NetworkStatusText = notes.Count > 0
            ? string.Join("  ", notes)
            : "Network setup check complete. Router forwarding status is unknown until you " +
              "test from outside your network.";

        // Sec 4.3 / Sec 4.7: network state just changed - refresh guidance + score.
        ApplyNetworkConfidence(world, status);
        ApplyReachabilityVerdict(world, status);
        RefreshGuidance(world);
    }

    /// <summary>Phase B2: top-of-panel rollup that answers "can players join right now?".</summary>
    private static void ApplyReachabilityVerdict(
        ServerInstanceViewModel world,
        NetworkSetupStatus status)
    {
        var processRunning = status.Environment?.ServerProcessRunning ?? world.IsRunningState;
        var gameBound = status.PortBindings.Any(p => p.Port == world.GamePort && p.IsListening);
        var queryBound = status.PortBindings.Any(p => p.Port == world.QueryPort && p.IsListening);

        // A live Health == Online is direct corroboration (A2S, lobby code, or
        // a clean readiness signal already promoted it). Feed it back in so the
        // verdict matches the world dot.
        var a2sResponded = world.Health == ServerHealth.Online;
        var lobby = world.LobbyCode is { Length: > 0 };

        var verdict = NetworkVerdictRules.Evaluate(new NetworkVerdictInputs
        {
            ServerProcessRunning = processRunning,
            A2SLocalResponded = a2sResponded,
            GamePortBound = gameBound,
            QueryPortBound = queryBound,
            LobbyCodePublished = lobby,
            IsLanOnly = world.LanOnly,
        });

        world.ReachabilityStatus = verdict.Status;
        world.ReachabilityHeadline = verdict.Headline;
        world.ReachabilityDetail = verdict.Detail;
    }

    /// <summary>Sec 4.7: scores how ready this world's network setup is to host.</summary>
    private void ApplyNetworkConfidence(ServerInstanceViewModel world, NetworkSetupStatus status)
    {
        var gamePortBound = status.PortBindings.Any(p => p.Port == world.GamePort && p.IsListening);
        var publicScope = IpAddressClassifier.Classify(PublicIpv4);

        var result = NetworkConfidenceScoring.Score(new NetworkConfidenceInputs
        {
            HasLanIpv4 = LanIpv4 is { Length: > 0 } lan && lan != "-",
            FirewallRulesConfigured = status.AreFirewallRulesConfigured,
            A2SLocalResponded = gamePortBound,
            HasPublicIpv4 = publicScope is Ipv4Scope.Public or Ipv4Scope.CarrierGradeNat,
            LooksLikeCgnat = publicScope == Ipv4Scope.CarrierGradeNat,
            IsLanOnly = world.LanOnly,
        });

        world.NetworkConfidenceSummary = $"{result.Score} / 100  -  {result.Band}";
        world.NetworkConfidenceStrengths.Clear();
        foreach (var strength in result.Strengths)
        {
            world.NetworkConfidenceStrengths.Add(strength);
        }

        world.NetworkConfidenceLifts.Clear();
        foreach (var lift in result.Lifts)
        {
            world.NetworkConfidenceLifts.Add(lift);
        }

        world.HasNetworkConfidence = true;
    }

    private static string FormatUtc(DateTimeOffset? timestamp) =>
        timestamp is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'") : "at an unknown time";

    private static string ShortStatus(string text)
    {
        var singleLine = string.Join(
            " ",
            text.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        const int max = 220;
        return singleLine.Length <= max ? singleLine : singleLine[..max] + "...";
    }

    private static string BuildNetworkDiagnosticsText(
        ServerInstanceViewModel world,
        NetworkSetupStatus status)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "unknown";
        var env = status.Environment;
        var lines = new List<string>
        {
            "Facility Overseer Network Diagnostics",
            $"App version: {version}",
            $"World: {world.DisplayName} / {world.Id}",
            $"Game UDP: {world.GamePort}",
            $"Query UDP: {world.QueryPort}",
            $"Server exe: {status.ServerExecutablePath ?? "not found"}",
            "Current LAN IPv4 candidates:",
        };

        var candidates = status.LocalIpv4CandidateDetails.Count > 0
            ? status.LocalIpv4CandidateDetails
            : status.LocalIpv4Addresses;
        if (candidates.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(candidates.Select(c => "- " + c));
        }

        lines.Add("Firewall rules:");
        lines.Add("- Game: " + CheckSnapshot(status, "firewall.game"));
        lines.Add("- Query: " + CheckSnapshot(status, "firewall.query"));
        lines.Add("- Exe: " + CheckSnapshot(status, "firewall.exe"));
        lines.Add("Server process:");
        lines.Add($"- Running: {(env?.ServerProcessRunning == true ? "yes" : "no")}");

        var endpointDetails = status.Checks
            .Where(c => c.Id is "endpoint.game" or "endpoint.query")
            .Select(c => $"{c.Label}: {c.Summary} {c.Details}".Trim())
            .ToList();
        lines.Add("- UDP endpoints: " +
                  (endpointDetails.Count == 0 ? "not inspected" : string.Join("; ", endpointDetails)));
        lines.Add($"Network profile: {env?.NetworkProfile ?? "Unknown"}");
        lines.Add($"Elevation: {(env?.IsElevated == true ? "yes" : "no")}");
        lines.Add("External reachability: Unknown, requires outside-network test");
        if (status.LogPath is { Length: > 0 } logPath)
        {
            lines.Add($"App log: {logPath}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CheckSnapshot(NetworkSetupStatus status, string id)
    {
        var check = status.Checks.FirstOrDefault(c => c.Id == id);
        if (check is null)
        {
            return "unknown";
        }

        var detail = string.IsNullOrWhiteSpace(check.Details) ? "" : " " + check.Details;
        return $"{check.StatusLabel}: {check.Summary}{detail}";
    }

    /// <summary>
    /// Silently resolves and loads the sandbox file for a world (no prompts). Called when
    /// a world is selected and after an install, so the World/Player/Enemy tabs are
    /// populated without the user clicking anything.
    /// </summary>
    private async Task AutoLoadSandboxAsync(ServerInstanceViewModel world)
    {
        if (world.Sandbox is null)
        {
            return;
        }

        try
        {
            // Never touch a world folder while its server is running - the game
            // owns the save then, and a transient "looks config-only" state must
            // not get quarantined out from under a live server.
            if (!_processes.IsRunning(world.Id))
            {
                CleanupOrphanWorldFolder(world.Model);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to clean partial world folder for {Id}", world.Id);
            world.Sandbox.StatusText = "Could not clean up the partial world folder: " + ex.Message;
            return;
        }

        var resolution = WorldSaveLayout.Resolve(world.Model);
        if (resolution.RealSaveExists &&
            !string.Equals(
                world.Model.WorldPath,
                resolution.WorldFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            world.Model.WorldPath = resolution.WorldFolder;
        }

        var path = resolution.ExistingSandboxPath;
        var createdFromDefaultTemplate = false;
        if (path is null)
        {
            try
            {
                var result = await EnsureDefaultSandboxAsync(world.Model, resolution);
                path = result.Path;
                createdFromDefaultTemplate = result.CreatedFromDefaultTemplate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to create default sandbox settings for {Id}", world.Id);
                world.Sandbox.StatusText =
                    "No SandboxSettings.ini found yet, and the default template could not be created: " +
                    ex.Message;
                return;
            }
        }

        if (!string.Equals(world.Model.SandboxIniPath, path, StringComparison.OrdinalIgnoreCase))
        {
            world.Model.SandboxIniPath = path;
            await SaveAsync();
        }

        try
        {
            var doc = await _sandbox.LoadAsync(path);
            world.Sandbox.LoadFrom(doc);
            if (createdFromDefaultTemplate)
            {
                world.Sandbox.StatusText =
                    $"Loaded default SandboxSettings.ini template - {path}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-load sandbox settings from {Path}", path);
            world.Sandbox.StatusText = "Could not load sandbox settings: " + ex.Message;
        }
    }

    /// <summary>
    /// Runs <see cref="AutoLoadSandboxAsync"/> without awaiting (event handlers
    /// cannot await) but still logs any escaped exception instead of leaving it
    /// as an unobserved task fault.
    /// </summary>
    private void FireAndForgetAutoLoad(ServerInstanceViewModel world) =>
        _ = AutoLoadSandboxAsync(world).ContinueWith(
            t => _logger.LogError(
                t.Exception, "Auto-load sandbox failed for {Id}", world.Id),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    partial void OnSelectedWorldChanging(ServerInstanceViewModel? value)
    {
        if (_observedSelectedWorld is not null)
        {
            _observedSelectedWorld.PropertyChanged -= OnSelectedWorldPropertyChanged;
            _observedSelectedWorld = null;
        }
    }

    partial void OnSelectedWorldChanged(ServerInstanceViewModel? value)
    {
        if (value is not null)
        {
            _observedSelectedWorld = value;
            value.PropertyChanged += OnSelectedWorldPropertyChanged;
            RefreshInstallStatus(value);
            if (!_suppressAutoLoadForSelection)
            {
                FireAndForgetAutoLoad(value);
            }

            _ = RefreshBackupsAsync(value);
            RefreshAdmins(value);
            FireAndForgetAutoInspectNetwork(value);
        }
    }

    /// <summary>
    /// Sec 4.3 auto-probe: when a world becomes active we silently inspect the
    /// network so the recommended-actions list reflects real firewall state,
    /// not the default-false placeholder. Once-per-session per world (guarded
    /// by <see cref="ServerInstanceViewModel.FirewallRulesConfigured"/> being
    /// null) so tab switches do not re-shell out to netsh.
    /// </summary>
    private void FireAndForgetAutoInspectNetwork(ServerInstanceViewModel world)
    {
        if (world.FirewallRulesConfigured is not null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var status = await _networkSetup.InspectAsync(world.Model);
                await Application.Current.Dispatcher.InvokeAsync(
                    () => ApplyNetworkSetupStatus(world, status));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Background network inspect failed for world {Id}",
                    world.Id);
            }
        });
    }

    private void OnSelectedWorldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServerInstanceViewModel.InstallPath))
        {
            RefreshInstallStatus(SelectedWorld);
            StartServerCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(ServerInstanceViewModel.GamePort)
            or nameof(ServerInstanceViewModel.QueryPort))
        {
            CreateFirewallRulesCommand.NotifyCanExecuteChanged();
        }

        // Sec 4.3: health drives recommended actions (e.g. "restart after crash").
        if (e.PropertyName is nameof(ServerInstanceViewModel.Health))
        {
            RefreshGuidance(SelectedWorld);
        }
    }

    // Sec 4.3: a new LAN IPv4 changes the "connect to network" recommendation.
    partial void OnLanIpv4Changed(string value) => RefreshGuidance(SelectedWorld);

    // ===================== Admin list (plan Sec 6.3) =====================

    private void RefreshAdmins(ServerInstanceViewModel world)
    {
        try
        {
            var path = _adminList.ResolveAdminIniPath(world.Model);
            if (!string.IsNullOrWhiteSpace(path) &&
                !string.Equals(world.Model.AdminIniPath, path, StringComparison.OrdinalIgnoreCase))
            {
                world.AdminIniPath = path;
            }

            var moderators = _adminList.Load(path);
            world.Admins.Clear();
            foreach (var id in moderators)
            {
                world.Admins.Add(id);
            }

            // Sec 3.2/Sec 3.3 - push the moderator + banned id sets into the world VM so
            // the roster gets its admin marker and banned ids never leak into
            // the live roster collection.
            var bans = _bans.ListBans(world.Model);
            world.UpdateModerationLists(moderators, bans);

            world.AdminStatusText = world.Admins.Count == 0
                ? "No admins yet. Add a player's SteamID64 (17 digits) below."
                : $"{world.Admins.Count} admin(s). Saved to {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load admin list for {Id}", world.Id);
            world.AdminStatusText = "Could not load the admin list: " + ex.Message;
        }
    }

    private void SaveAdmins(ServerInstanceViewModel world)
    {
        try
        {
            var path = _adminList.ResolveAdminIniPath(world.Model);
            world.AdminIniPath = path;
            _adminList.Save(path, [.. world.Admins]);
            world.AdminStatusText = $"{world.Admins.Count} admin(s). Saved to {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save admin list for {Id}", world.Id);
            MessageBox.Show("Could not save the admin list:\n\n" + ex.Message, "Facility Overseer");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AddAdmin()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var id = (SelectedWorld.NewAdminId ?? "").Trim();
        if (!IAdminListService.IsValidSteamId(id))
        {
            MessageBox.Show(
                "That doesn't look like a SteamID64. It should be 17 digits starting with 7 " +
                "(e.g. 76561198000000000).",
                "Facility Overseer - Admins");
            return;
        }

        if (SelectedWorld.Admins.Contains(id))
        {
            SelectedWorld.AdminStatusText = "That admin is already on the list.";
            return;
        }

        SelectedWorld.Admins.Add(id);
        SelectedWorld.NewAdminId = "";
        SaveAdmins(SelectedWorld);
        await SaveAsync();
    }

    [RelayCommand]
    private async Task RemoveAdmin(string? id)
    {
        if (SelectedWorld is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        SelectedWorld.Admins.Remove(id);
        SaveAdmins(SelectedWorld);
        await SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenAdminFolder()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var path = _adminList.ResolveAdminIniPath(SelectedWorld.Model);
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            MessageBox.Show(
                "No admin file location yet. Install the server or set an install path first.",
                "Facility Overseer");
            return;
        }

        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveSandbox()
    {
        if (SelectedWorld?.Sandbox is { IsLoaded: true } sandbox)
        {
            await AutoBackupAsync(SelectedWorld, "before-config-save");
            try
            {
                await sandbox.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sandbox settings");
                MessageBox.Show("Could not save sandbox settings:\n\n" + ex.Message, "Facility Overseer");
                return;
            }

            // If the server for this world is currently running, also push the
            // freshly-saved durable INI into the staged runtime location so a
            // world restart picks it up without the user having to re-save
            // after stopping. AF cannot hot-reload SandboxSettings.ini, so a
            // restart is still required - hence the explicit "restart to
            // apply in-game" toast rather than a "Saved" one.
            if (SelectedWorld.IsRunningState)
            {
                try
                {
                    var paths = SandboxLaunchPaths.For(SelectedWorld.Model);
                    await _sandboxStaging.PushDurableToStagedAsync(paths);
                    ShowCopyToast("Saved. Restart the world to apply in-game.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not push sandbox to staged runtime copy");
                }
            }
            else
            {
                ShowCopyToast("Saved SandboxSettings.ini");
            }
        }
    }

    // ===================== Backup & Restore (plan Sec 14) =====================

    private bool CanBackup() => SelectedWorld is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupNow()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        world.BackupStatusText = "Creating backup...";
        var result = await _backups.CreateBackupAsync(world.Model, "manual");
        world.BackupStatusText = result.Message;
        await RefreshBackupsAsync(world);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Facility Overseer - Backup");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RestoreBackup()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        if (world.SelectedBackup is null)
        {
            MessageBox.Show("Select a backup from the list first.", "Facility Overseer - Restore");
            return;
        }

        if (_processes.IsRunning(world.Id))
        {
            MessageBox.Show("Stop the world before restoring a backup.", "Facility Overseer");
            return;
        }

        var backup = world.SelectedBackup;
        if (MessageBox.Show(
                $"Restore the backup from {backup.CreatedAt:g} ({backup.Reason})?\n\n" +
                "This overwrites the current world save and config. A 'pre-restore' safety " +
                "backup of the current state is taken automatically first.",
                "Facility Overseer - Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        BusyStatus = "Restoring backup...";
        BusyProgress = null;
        try
        {
            var result = await _backups.RestoreBackupAsync(world.Model, backup);
            world.BackupStatusText = result.Message;
            await RefreshBackupsAsync(world);
            if (world.Sandbox is not null)
            {
                await AutoLoadSandboxAsync(world);
            }

            MessageBox.Show(result.Message, "Facility Overseer - Restore");
        }
        finally
        {
            IsBusy = false;
            BusyStatus = "";
            BusyProgress = null;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task OpenWorldFolder()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        var path = ResolveWorldFolderPath(world.Model);
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(
                "Set an install path before opening the world folder.",
                "Facility Overseer");
            return;
        }

        if (!Directory.Exists(path))
        {
            MessageBox.Show(
                "The world save folder does not exist yet. Start the server once to let " +
                "Abiotic Factor create the real save folder.\n\n" + path,
                "Facility Overseer - Open World Folder");
            return;
        }

        // Always open an existing folder - even a partial/corrupt one - so the
        // user can inspect or recover it. Only persist it as the world path when
        // it is a genuine save.
        if (WorldSaveLayout.IsRealWorldSaveFolder(path) &&
            !string.Equals(world.Model.WorldPath, path, StringComparison.OrdinalIgnoreCase))
        {
            world.Model.WorldPath = path;
            await SaveAsync();
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private bool CanCreateFreshWorld() =>
        SelectedWorld is not null && !IsBusy;

    /// <summary>
    /// Corrupt-world recovery: never deletes. Stops the server, moves the
    /// existing world folder to a timestamped quarantine, and lets the game
    /// regenerate a fresh save on the next Start. Staged SandboxSettings.ini
    /// lives outside the world folder, so settings are preserved automatically.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateFreshWorld))]
    private async Task CreateFreshWorld()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        var folder = ResolveWorldFolderPath(world.Model);
        var hasFolder = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);

        var confirm = MessageBox.Show(
            "Create a fresh world for \"" + world.DisplayName + "\"?\n\n" +
            (hasFolder
                ? "The current world save will be MOVED to a timestamped quarantine " +
                  "folder (never deleted) so you can recover it:\n\n" + folder
                : "No existing world save folder was found; a fresh one will be " +
                  "created on the next Start.") +
            "\n\nYour sandbox settings are kept. The server will be stopped and you " +
            "can Start again when ready. Continue?",
            "Facility Overseer - Create Fresh World",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_processes.IsRunning(world.Id))
            {
                await _processes.StopAsync(world.Model);
            }

            if (hasFolder && WorldSaveLayout.IsRealWorldSaveFolder(folder))
            {
                await AutoBackupAsync(world, "before-create-fresh-world");
            }

            if (hasFolder)
            {
                var quarantine = UniqueQuarantinePath(folder);
                Directory.Move(folder, quarantine);
                if (string.Equals(world.Model.WorldPath, folder, StringComparison.OrdinalIgnoreCase))
                {
                    world.Model.WorldPath = "";
                }

                await SaveAsync();
                MessageBox.Show(
                    "The world save was quarantined to:\n\n" + quarantine +
                    "\n\nPress Start to generate a fresh world.",
                    "Facility Overseer - Create Fresh World");
            }
            else
            {
                MessageBox.Show(
                    "No existing world folder to quarantine. Press Start to generate " +
                    "a fresh world.",
                    "Facility Overseer - Create Fresh World");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Create Fresh World failed for {Id}", world.Id);
            MessageBox.Show(
                "Could not create a fresh world:\n\n" + ex.Message,
                "Facility Overseer");
        }
    }

    /// <summary>
    /// Opens the Player Detail tab for the double-clicked roster row. The
    /// double-click has already set <c>SelectedRosterPlayer</c>, so no
    /// parameter is needed.
    /// </summary>
    [RelayCommand]
    private void ShowPlayerDetail()
    {
        var world = SelectedWorld;
        if (world?.SelectedRosterPlayer is { } row)
        {
            world.ShowPlayerDetail(row);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task BanSelectedPlayer()
    {
        var world = SelectedWorld;
        var row = world?.SelectedRosterPlayer;
        if (world is null || row is null)
        {
            MessageBox.Show(
                "Select a player in the roster first.", "Facility Overseer - Ban");
            return;
        }

        await BanRowAsync(world, row);
    }

    // D12: right-click moderation on a roster row. Promote / demote / ban all
    // operate on the row's captured id, never on the visual decoration.
    [RelayCommand]
    private async Task BanPlayer(RosterRowViewModel? row)
    {
        if (SelectedWorld is { } world && row is not null)
        {
            await BanRowAsync(world, row);
        }
    }

    [RelayCommand]
    private async Task PromotePlayer(RosterRowViewModel? row)
    {
        if (SelectedWorld is not { } world || row is null)
        {
            return;
        }

        var id = row.SteamId64 ?? "";
        if (!IAdminListService.IsValidSteamId(id))
        {
            MessageBox.Show(
                $"\"{row.DisplayName}\" has no SteamID64 captured yet, so they cannot be " +
                "made a moderator. The SteamID64 is captured once they connect.",
                "Facility Overseer - Promote");
            return;
        }

        if (world.Admins.Contains(id))
        {
            return;
        }

        world.Admins.Add(id);
        SaveAdmins(world);
        await SaveAsync();
        RefreshAdmins(world);
    }

    [RelayCommand]
    private async Task DemotePlayer(RosterRowViewModel? row)
    {
        if (SelectedWorld is not { } world || row is null)
        {
            return;
        }

        var id = row.SteamId64 ?? "";
        if (!world.Admins.Contains(id))
        {
            return;
        }

        world.Admins.Remove(id);
        SaveAdmins(world);
        await SaveAsync();
        RefreshAdmins(world);
    }

    private async Task BanRowAsync(ServerInstanceViewModel world, RosterRowViewModel row)
    {
        // Sec 3.3: the row VM is decoration only - ban operates on the underlying
        // captured id, never on the visual.
        var player = row.Entry;
        var id = !string.IsNullOrWhiteSpace(player.SteamId64)
            ? player.SteamId64!
            : player.PrimaryId ?? "";

        if (MessageBox.Show(
                $"Ban \"{player.DisplayName}\" ({(id.Length > 0 ? id : "no captured ID")})?\n\n" +
                "This adds them to Admin.ini [BannedPlayers]. Abiotic Factor reads that " +
                "file on start, so a restart is needed to enforce it on a connected player.",
                "Facility Overseer - Ban Player",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _bans.Ban(world.Model, id, player.DisplayName);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Facility Overseer - Ban", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Sec 3.2: surface the new ban on the Banished page + filter the roster.
        RefreshAdmins(world);

        if (_processes.IsRunning(world.Id) &&
            MessageBox.Show(
                result.Message + "\n\nRestart the server now to disconnect and enforce " +
                "the ban?",
                "Facility Overseer - Ban Player",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await _processes.RestartAsync(world.Model);
        }
        else
        {
            MessageBox.Show(result.Message, "Facility Overseer - Ban");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void UnbanSelectedPlayer()
    {
        var world = SelectedWorld;
        var row = world?.SelectedRosterPlayer;
        if (world is null || row is null)
        {
            MessageBox.Show(
                "Select a player in the roster first.", "Facility Overseer - Unban");
            return;
        }

        var player = row.Entry;
        var id = !string.IsNullOrWhiteSpace(player.SteamId64)
            ? player.SteamId64!
            : player.PrimaryId ?? "";

        var result = _bans.Unban(world.Model, id);
        if (result.Success)
        {
            // Sec 3.2: re-derive the banned set so the Banished page row drops.
            RefreshAdmins(world);
        }
        MessageBox.Show(result.Message, "Facility Overseer - Unban");
    }

    /// <summary>
    /// Sec 3.2: lift a ban from the Banished page directly. Operates on the
    /// underlying id row, not on the roster (banned ids are deliberately
    /// hidden from the roster).
    /// </summary>
    [RelayCommand]
    private void UnbanFromBanished(string? id)
    {
        var world = SelectedWorld;
        if (world is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var result = _bans.Unban(world.Model, id);
        if (result.Success)
        {
            RefreshAdmins(world);
        }

        MessageBox.Show(result.Message, "Facility Overseer - Unban");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenBackupsFolder()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var dir = _backups.GetInstanceBackupRoot(SelectedWorld.Model);
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
    }

    private static string ResolveWorldFolderPath(ServerInstance instance)
    {
        // Prefer the world's actual save folder (tolerant of the game's own
        // name casing) so Open World Folder works even when the on-disk folder
        // differs from the predicted name; fall back to the expected location.
        var real = WorldSaveLayout.FindExistingRealWorldFolder(instance);
        return string.IsNullOrEmpty(real)
            ? WorldSaveLayout.ExpectedWorldFolder(instance)
            : real;
    }

    private async Task RefreshBackupsAsync(ServerInstanceViewModel world)
    {
        try
        {
            var list = await _backups.ListBackupsAsync(world.Model);
            world.Backups.Clear();
            foreach (var entry in list)
            {
                world.Backups.Add(entry);
            }

            if (list.Count > 0 && world.BackupStatusText.StartsWith("No backups", StringComparison.Ordinal))
            {
                world.BackupStatusText = $"{list.Count} backup(s) available.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups for {Id}", world.Id);
        }
    }

    /// <summary>
    /// Best-effort safety backup taken before a destructive action (config save, update,
    /// delete). A failure is logged and surfaced but never silently blocks the action the
    /// user explicitly asked for.
    /// </summary>
    private async Task AutoBackupAsync(ServerInstanceViewModel world, string reason)
    {
        try
        {
            var result = await _backups.CreateBackupAsync(world.Model, reason);
            world.BackupStatusText = result.Message;
            await RefreshBackupsAsync(world);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-backup ({Reason}) failed for {Id}", reason, world.Id);
        }
    }

    private async Task<(string Path, bool CreatedFromDefaultTemplate)> EnsureDefaultSandboxAsync(
        ServerInstance instance,
        SandboxResolution resolution)
    {
        // Sec 2.1: write the default sandbox to the canonical durable location
        // (<DataRoot>/worlds/<id>/config/SandboxSettings.ini) directly. The old
        // path went to the staged location inside the server install and
        // relied on the next launch's WorldIdentityMigrationService to move
        // it - which left a window where the integrity check pointed at a
        // canonical path that did not exist yet, surfacing a false
        // "Sandbox settings file missing" warning on a freshly-created world.
        var worldId = string.IsNullOrWhiteSpace(instance.Id) ? "default" : instance.Id;
        var path = _paths.WorldSandboxIniPath(worldId);

        _paths.EnsureWorldCreated(worldId);

        if (!File.Exists(path))
        {
            if (resolution.MigrationSource is { } source &&
                File.Exists(source) &&
                !string.Equals(source, path, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, path);
                return (path, false);
            }

            var template = await DefaultSandboxSettings.LoadTemplateAsync();
            await File.WriteAllTextAsync(path, template);
            return (path, true);
        }

        return (path, false);
    }

    private static void CleanupOrphanWorldFolder(ServerInstance instance)
    {
        var worldFolder = WorldSaveLayout.ExpectedWorldFolder(instance);
        if (!WorldSaveLayout.IsOrphanConfigOnlyWorldFolder(worldFolder))
        {
            return;
        }

        var stagedPath = WorldSaveLayout.StagedSandboxPath(instance);
        var worldSandbox = Path.Combine(worldFolder, DefaultSandboxSettings.FileName);
        if (!string.IsNullOrWhiteSpace(stagedPath) &&
            File.Exists(worldSandbox) &&
            !File.Exists(stagedPath))
        {
            var stagedDirectory = Path.GetDirectoryName(stagedPath);
            if (!string.IsNullOrWhiteSpace(stagedDirectory))
            {
                Directory.CreateDirectory(stagedDirectory);
            }

            File.Move(worldSandbox, stagedPath);
            instance.SandboxIniPath = stagedPath;
        }

        if (!Directory.EnumerateFileSystemEntries(worldFolder).Any())
        {
            Directory.Delete(worldFolder);
            if (string.Equals(instance.WorldPath, worldFolder, StringComparison.OrdinalIgnoreCase))
            {
                instance.WorldPath = "";
            }

            return;
        }

        var quarantinePath = UniqueQuarantinePath(worldFolder);
        Directory.Move(worldFolder, quarantinePath);
        if (string.Equals(instance.WorldPath, worldFolder, StringComparison.OrdinalIgnoreCase))
        {
            instance.WorldPath = "";
        }
    }

    private static string UniqueQuarantinePath(string worldFolder)
    {
        var parent = Path.GetDirectoryName(worldFolder) ?? "";
        var name = Path.GetFileName(worldFolder);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
        var basePath = Path.Combine(parent, $"{name}.partial-{stamp}");
        var path = basePath;
        var suffix = 1;
        while (Directory.Exists(path) || File.Exists(path))
        {
            path = $"{basePath}-{suffix++}";
        }

        return path;
    }

    private static string OutputTail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        var trimmed = output.Trim();
        const int max = 800;
        var tail = trimmed.Length > max ? "..." + trimmed[^max..] : trimmed;
        return "\n\nSteamCMD output:\n" + tail;
    }

    private void RefreshInstallStatus(ServerInstanceViewModel? world)
    {
        if (world is null)
        {
            return;
        }

        var state = _serverInstallState.Evaluate(world.Model);

        // While running, the health tracker owns StatusText (Starting/Online/
        // Blocked/Crashed). Only set it from install state when stopped.
        if (!world.IsRunningState)
        {
            world.StatusText = state.IsLaunchable ? "Stopped" : state.ValidationMessage;
        }

        RefreshHeaderInfo(state);
        RefreshGuidance(world, state);
    }

    /// <summary>
    /// Evaluates the shared managed dedicated-server install - world-independent,
    /// because the server is one disposable tool, not part of any world - and
    /// refreshes the header plus <see cref="IsServerPrepared"/>. Lets the server
    /// be prepared before the first world is created.
    /// </summary>
    private ServerInstallState RefreshServerInstallState()
    {
        var state = _serverInstallState.Evaluate(
            new ServerInstance { InstallPath = _paths.ManagedServerDirectory });
        IsServerPrepared = state.IsLaunchable;
        RefreshHeaderInfo(state);
        return state;
    }

    /// <summary>
    /// Sec 4.3 / Sec 4.5: re-runs the world-integrity inspection and rebuilds the
    /// ranked recommended-actions list for one world. Cheap enough (a few
    /// File.Exists probes) to hang off the central RefreshInstallStatus hub.
    /// </summary>
    private void RefreshGuidance(ServerInstanceViewModel world, ServerInstallState installState)
    {
        // Sec 4.5 - integrity findings + the pre-start launch gate.
        ApplyIntegrityReport(world, _worldIntegrity.Inspect(world.Model));

        // Sec 4.3 - ranked next-step suggestions for the current state.
        var actions = RecommendedActions.Build(new RecommendedActionInputs
        {
            InstallKind = installState.Kind,
            Health = world.Health,
            FirewallRulesConfigured = world.FirewallRulesConfigured,
            HasLanIpv4 = LanIpv4 is { Length: > 0 } ip && ip != "-",
            IsLanOnly = world.LanOnly,
            WorldsExist = Worlds.Count > 0,
            HasBlockerFindings = !world.IsWorldLaunchable,
        });

        world.RecommendedActions.Clear();
        foreach (var action in actions)
        {
            world.RecommendedActions.Add(action);
        }

        world.HasRecommendedActions = world.RecommendedActions.Count > 0;
        world.RebuildNotificationChips();

        // Sec 4.2 - surface a guided recovery flow when the world is Blocked.
        var recoveryTag = world.Health == ServerHealth.Blocked ? world.HealthBlockingTag : null;
        ApplyRecoveryFlow(world, RecoveryFlows.ForTag(recoveryTag));
    }

    /// <summary>Refreshes guidance for a world, evaluating its install state first.</summary>
    private void RefreshGuidance(ServerInstanceViewModel? world)
    {
        if (world is not null)
        {
            RefreshGuidance(world, _serverInstallState.Evaluate(world.Model));
        }
    }

    /// <summary>Sec 4.5: pushes a world-integrity report onto the world view model.</summary>
    private static void ApplyIntegrityReport(ServerInstanceViewModel world, WorldIntegrityReport report)
    {
        // Info-severity findings (e.g. "Admin / ban list not created yet") are
        // not launch impediments and belong on their owning tab, not on the
        // integrity chip. Filter them out of the UI surface; the validator's
        // full report is still emitted to logs/audit for diagnostics.
        world.IntegrityFindings.Clear();
        foreach (var finding in report.Findings)
        {
            if (finding.Severity == WorldIntegritySeverity.Info)
            {
                continue;
            }

            world.IntegrityFindings.Add(finding);
        }

        world.HasIntegrityFindings = world.IntegrityFindings.Count > 0;
        world.IsWorldLaunchable = report.IsLaunchable;
        world.RebuildNotificationChips();

        var blockers = report.Findings.Count(f => f.Severity == WorldIntegritySeverity.Blocker);
        var warnings = report.Findings.Count(f => f.Severity == WorldIntegritySeverity.Warning);
        world.IntegritySummary = !report.IsLaunchable
            ? $"World cannot start - {blockers} blocker(s) must be fixed."
            : warnings > 0
                ? $"World is launchable - {warnings} warning(s) to review."
                : "World integrity checks passed.";
    }

    /// <summary>Sec 4.2: pushes the matching guided recovery flow onto the world, or clears it.</summary>
    private static void ApplyRecoveryFlow(ServerInstanceViewModel world, RecoveryFlow? flow)
    {
        world.RecoverySteps.Clear();
        if (flow is null)
        {
            world.HasRecoveryFlow = false;
            return;
        }

        world.RecoveryFlowTitle = flow.Title;
        world.RecoveryFlowSummary = flow.Summary;
        foreach (var step in flow.Steps)
        {
            world.RecoverySteps.Add(step);
        }

        world.HasRecoveryFlow = true;
    }

    /// <summary>
    /// Opens the floating "Recommended next steps" window for the given world.
    /// The Logs &amp; Status tab shows only a chip; the long action list lives
    /// in this non-modal popup so it does not crowd the log surface.
    /// </summary>
    [RelayCommand]
    private void OpenRecommendedActionsWindow(ServerInstanceViewModel? world)
    {
        if (world is null)
        {
            return;
        }

        var popup = new RecommendedActionsWindow(world)
        {
            Owner = Application.Current?.MainWindow,
        };
        popup.Show();
    }

    /// <summary>
    /// Opens the floating world-integrity window for the given world.
    /// Parallels <see cref="OpenRecommendedActionsWindow"/>.
    /// </summary>
    [RelayCommand]
    private void OpenIntegrityWindow(ServerInstanceViewModel? world)
    {
        if (world is null)
        {
            return;
        }

        var popup = new WorldIntegrityWindow(world)
        {
            Owner = Application.Current?.MainWindow,
        };
        popup.Show();
    }

    /// <summary>
    /// Sec 4.3: runs the App command behind a recommended action. The action
    /// carries a <c>CommandHint</c> (a command name) so the panel stays
    /// data-driven - no per-action button wiring in XAML.
    /// </summary>
    [RelayCommand]
    private async Task RunRecommendedAction(RecommendedAction? action)
    {
        await DispatchCommandHintAsync(action?.CommandHint);
    }

    /// <summary>
    /// Sec 4.5: runs the App command attached to a world-integrity finding.
    /// Lets the user fix a warning (e.g. "Sandbox settings file missing")
    /// inline from the World integrity popup instead of hunting for the
    /// matching tab and command.
    /// </summary>
    [RelayCommand]
    private async Task RunIntegrityAction(WorldIntegrityFinding? finding)
    {
        await DispatchCommandHintAsync(finding?.CommandHint);
    }

    /// <summary>
    /// Single dispatch table for command-hint strings carried by recommended
    /// actions and integrity findings. New hints land here once, not in each
    /// caller.
    /// </summary>
    private async Task DispatchCommandHintAsync(string? commandHint)
    {
        switch (commandHint)
        {
            case "InstallOrUpdateServerCommand":
                if (InstallOrUpdateServerCommand.CanExecute(null))
                {
                    await InstallOrUpdateServer();
                }

                break;
            case "CreateFirewallRulesCommand":
                if (CreateFirewallRulesCommand.CanExecute(null))
                {
                    await CreateFirewallRules();
                }

                break;
            case "CreateWorldCommand":
                await CreateWorld();
                break;
            case "RestartServerCommand":
                if (RestartServerCommand.CanExecute(null))
                {
                    await RestartServer();
                }

                break;
            case "SaveSandboxCommand":
                if (SaveSandboxCommand.CanExecute(null))
                {
                    await SaveSandbox();
                }

                break;
        }
    }

    /// <summary>
    /// Sec 4.2: runs the App command behind a recovery-flow step. The step carries
    /// an <c>ActionHint</c> command name so the flow stays data-driven.
    /// </summary>
    [RelayCommand]
    private void RunRecoveryStep(RecoveryStep? step)
    {
        IRelayCommand? command = step?.ActionHint switch
        {
            "StopServerCommand" => StopServerCommand,
            "RestartServerCommand" => RestartServerCommand,
            "CreateFreshWorldCommand" => CreateFreshWorldCommand,
            "OpenWorldFolderCommand" => OpenWorldFolderCommand,
            "OpenBackupsFolderCommand" => OpenBackupsFolderCommand,
            "InstallOrUpdateServerCommand" => InstallOrUpdateServerCommand,
            _ => null,
        };

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    // ---- Header chip copy commands (LAN / PUB / LOBBY) ----
    //
    // All three go through ClipboardHelper.TryCopy so a transient COMException
    // (another process holding the clipboard - a routine Windows race) does
    // not silently fail. Each fires ShowCopyToast on success/failure so the
    // user sees a brief confirmation next to the chips instead of having to
    // paste somewhere to find out whether the click registered.

    /// <summary>
    /// Brief transient feedback text shown alongside the header chip strip
    /// when one of the copy commands fires. Cleared automatically by
    /// <see cref="_copyToastTimer"/> after ~1.5 s.
    /// </summary>
    [ObservableProperty]
    private string _lastCopyToast = "";

    /// <summary>True when <see cref="LastCopyToast"/> represents a failure (drives warn-coloured text).</summary>
    [ObservableProperty]
    private bool _isCopyToastError;

    private System.Windows.Threading.DispatcherTimer? _copyToastTimer;

    /// <summary>Sets the chip-strip toast text and starts the auto-clear timer.</summary>
    private void ShowCopyToast(string message, bool isError = false)
    {
        LastCopyToast = message;
        IsCopyToastError = isError;
        _copyToastTimer ??= CreateCopyToastTimer();
        _copyToastTimer.Stop();
        _copyToastTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateCopyToastTimer()
    {
        var t = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        t.Tick += (_, _) =>
        {
            t.Stop();
            LastCopyToast = "";
            IsCopyToastError = false;
        };
        return t;
    }

    /// <summary>Sec 4.9: copies the selected world's lobby code to the clipboard.</summary>
    [RelayCommand]
    private void CopyLobbyCode()
    {
        var code = SelectedWorld?.LobbyCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (ClipboardHelper.TryCopy(code))
        {
            ShowCopyToast($"Copied lobby code {code}");
        }
        else
        {
            ShowCopyToast("Copy failed - clipboard busy", isError: true);
            _logger.LogDebug("Could not copy lobby code to clipboard");
        }
    }

    /// <summary>Copies this PC's LAN IPv4 to the clipboard from the compact header chip.</summary>
    [RelayCommand]
    private void CopyLanIp()
    {
        if (string.IsNullOrWhiteSpace(LanIpv4) || LanIpv4 == "-")
        {
            return;
        }

        if (ClipboardHelper.TryCopy(LanIpv4))
        {
            ShowCopyToast($"Copied LAN IP {LanIpv4}");
        }
        else
        {
            ShowCopyToast("Copy failed - clipboard busy", isError: true);
            _logger.LogDebug("Could not copy LAN IP to clipboard");
        }
    }

    /// <summary>
    /// Copies the public IPv4 to the clipboard. Only copies when the value is
    /// currently revealed - copying a masked / "unavailable" placeholder is a
    /// foot-gun, so the chip's click no-ops until the user reveals.
    /// </summary>
    [RelayCommand]
    private void CopyPublicIp()
    {
        if (!IsPublicIpVisible)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(PublicIpv4) ||
            PublicIpv4 is "checking..." or "unavailable")
        {
            return;
        }

        if (ClipboardHelper.TryCopy(PublicIpv4))
        {
            ShowCopyToast($"Copied public IP {PublicIpv4}");
        }
        else
        {
            ShowCopyToast("Copy failed - clipboard busy", isError: true);
            _logger.LogDebug("Could not copy public IP to clipboard");
        }
    }

    private void RefreshHeaderInfo(ServerInstallState state)
    {
        var server = state.Kind switch
        {
            ServerInstallKind.SteamCmdManaged => state.BuildId is { Length: > 0 } b
                ? $"Server build {b}  |  SteamCMD managed"
                : "Server installed  |  SteamCMD managed",
            ServerInstallKind.DetectedUnmanaged => "Server detected  |  update status unknown",
            ServerInstallKind.Missing or ServerInstallKind.EmptyFolder => "Server not installed",
            ServerInstallKind.InvalidFolder => "Server folder invalid",
            _ => state.ValidationMessage,
        };

        HeaderInfoText = $"{AppVersionText()}  |  {server}";
    }

    private static string AppVersionText()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly()
                  ?? System.Reflection.Assembly.GetExecutingAssembly();
        var info = asm
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = !string.IsNullOrWhiteSpace(info)
            ? info!.Split('+')[0]
            : asm.GetName().Version?.ToString(3) ?? "dev";
        return $"Facility Overseer v{version}";
    }

    private bool HasLaunchableInstall(ServerInstanceViewModel world) =>
        _serverInstallState.Evaluate(world.Model).IsLaunchable;

    private bool CanStart() =>
        SelectedWorld is not null &&
        !IsBusy &&
        !SelectedWorld.IsRunningState &&
        !Worlds.Any(w => w.IsRunningState) &&
        HasLaunchableInstall(SelectedWorld);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartServer()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var installState = _serverInstallState.Evaluate(SelectedWorld.Model);
        if (!installState.IsLaunchable)
        {
            RefreshInstallStatus(SelectedWorld);
            MessageBox.Show(installState.ValidationMessage, "Facility Overseer");
            return;
        }

        if (!await PrepareWorldForStartAsync(SelectedWorld))
        {
            return;
        }

        // Sec 4.5 - pre-start world-integrity gate: surface blockers up front
        // instead of letting them fail mid-startup as raw log errors.
        var integrity = _worldIntegrity.Inspect(SelectedWorld.Model);
        ApplyIntegrityReport(SelectedWorld, integrity);
        if (!integrity.IsLaunchable)
        {
            SelectedWorld.SelectedVerticalTabIndex = ServerInstanceViewModel.LogsStatusTabIndex;
            SelectedWorld.LogsSubTabIndex = 0;
            MessageBox.Show(
                "This world has integrity problems that must be fixed before it can start:\n\n"
                + string.Join(
                    Environment.NewLine,
                    integrity.Findings
                        .Where(f => f.Severity == WorldIntegritySeverity.Blocker)
                        .Select(f => "- " + f.Title)),
                "Facility Overseer");
            return;
        }

        await ValidateConfig();
        if (SelectedWorld.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            MessageBox.Show(
                "Fix the configuration errors on the Logs & Status tab before starting.",
                "Facility Overseer");
            return;
        }

        var alreadyRunning = Worlds.FirstOrDefault(w => w.Id != SelectedWorld.Id && w.IsRunningState);
        if (alreadyRunning is not null)
        {
            MessageBox.Show(
                $"Stop \"{alreadyRunning.DisplayName}\" before starting another world.",
                "Facility Overseer");
            return;
        }

        var result = await _processes.StartAsync(SelectedWorld.Model);
        if (!result.Started)
        {
            MessageBox.Show(result.ErrorMessage ?? "The server failed to start.", "Facility Overseer");
            return;
        }

        SelectedWorld.SelectedVerticalTabIndex = ServerInstanceViewModel.LogsStatusTabIndex;
    }

    private async Task<bool> PrepareWorldForStartAsync(ServerInstanceViewModel world)
    {
        try
        {
            CleanupOrphanWorldFolder(world.Model);
            await AutoLoadSandboxAsync(world);
            await SaveAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not prepare world {Id} before start", world.Id);
            MessageBox.Show(
                "Could not prepare the world before starting:\n\n" + ex.Message,
                "Facility Overseer");
            return false;
        }
    }

    private bool CanStop() => SelectedWorld is not null && SelectedWorld.IsRunningState;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopServer()
    {
        if (SelectedWorld is not null)
        {
            await _processes.StopAsync(SelectedWorld.Model);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task RestartServer()
    {
        if (SelectedWorld is not null)
        {
            await _processes.RestartAsync(SelectedWorld.Model);
        }
    }

    // Server preparation installs the shared managed server and is world-
    // independent, so it does not require a selected world.
    private bool CanPrepareServer() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPrepareServer))]
    private async Task InstallOrUpdateServer()
    {
        if (IsBusy)
        {
            return;
        }

        var installPath = _paths.ManagedServerDirectory;

        // The server installs to the shared managed directory regardless of any
        // world. When a world is selected, point it at this install too.
        if (SelectedWorld is not null)
        {
            SelectedWorld.InstallPath = installPath;
            await SaveAsync();
        }

        IsBusy = true;
        BusyTitle = "Preparing server";
        BusyIsIndeterminate = true;
        try
        {
            var progress = new Progress<InstallProgress>(ApplyBusyProgress);

            if (!await _steamCmd.IsSteamCmdInstalledAsync())
            {
                var steamResult = await _steamCmd.InstallSteamCmdAsync(progress);
                if (!steamResult.Success)
                {
                    ShowSetupFailure("SteamCMD setup failed", steamResult);
                    return;
                }
            }

            // Updating over an existing install can overwrite files; snapshot first.
            // A fresh install into an empty/new folder has nothing worth backing up.
            if (SelectedWorld is not null &&
                Directory.Exists(installPath) &&
                Directory.EnumerateFileSystemEntries(installPath).Any())
            {
                BusyTitle = "Backing up before update";
                BusyStatus = "Backing up before update...";
                BusyIsIndeterminate = true;
                await AutoBackupAsync(SelectedWorld, "before-update");
            }

            var result = await _steamCmd.InstallOrUpdateServerAsync(installPath, progress);
            if (result.Success)
            {
                MessageBox.Show(
                    $"Dedicated server files are up to date.\n\nInstalled to:\n{installPath}",
                    "Facility Overseer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ShowSetupFailure("Server update failed", result);
            }

            await SaveAsync();

            if (result.Success && SelectedWorld is not null)
            {
                // The server's first run generates SandboxSettings.ini; pick it up now.
                RefreshInstallStatus(SelectedWorld);
                StartServerCommand.NotifyCanExecuteChanged();
                await AutoLoadSandboxAsync(SelectedWorld);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install/update failed");
            MessageBox.Show(
                "Install/update failed:\n\n" + ex.Message,
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            ResetBusyProgress();
            RefreshInstallStatus(SelectedWorld);
            RefreshServerInstallState();
        }
    }

    /// <summary>
    /// Presents a setup failure with the actionable remediation first and the raw
    /// SteamCMD tail clearly secondary, instead of a wall of console output.
    /// </summary>
    private static void ShowSetupFailure(string title, SteamCmdResult result)
    {
        var help = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"SteamCMD exited with code {result.ExitCode}."
            : result.ErrorMessage;

        if (string.IsNullOrWhiteSpace(result.LogPath))
        {
            MessageBox.Show(
                $"{title}.\n\n{help}{OutputTail(result.Output)}",
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var choice = MessageBox.Show(
            $"{title}.\n\n{help}\n\nA full troubleshooting log was saved to:\n" +
            $"{result.LogPath}\n\nOpen it now?",
            "Facility Overseer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);

        if (choice == MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = result.LogPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Could not open the log automatically. Open it manually from:\n" +
                    result.LogPath,
                    "Facility Overseer");
            }
        }
    }

    [RelayCommand]
    private async Task Save() => await SaveAsync();

    private async Task SaveAsync()
    {
        // Snapshot on the calling (UI) thread before awaiting, exactly as the
        // original .ToList() did, so the ObservableCollection is never enumerated
        // off-thread; the lock then serializes the actual file write.
        var snapshot = Worlds.Select(w => w.Model).ToList();
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save instances");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void OnLogReceived(object? sender, ServerLogLine line)
    {
        Dispatcher().Invoke(() =>
        {
            var world = Worlds.FirstOrDefault(w => w.Id == line.InstanceId);
            if (world is null)
            {
                return;
            }

            world.LogLines.Add(ServerLogEntry.From(line));
            while (world.LogLines.Count > 1000)
            {
                world.LogLines.RemoveAt(0);
            }

            world.ApplyPlayerActivity(line);
            // Health also reads stdout for the app's synthetic exit/crash lines;
            // the roster is driven exclusively by the AF log-file tail (below) so
            // Recent Activity is never double-counted.
            world.ApplyHealth(line);
        });
    }

    private void OnRuntimeChanged(object? sender, string instanceId)
    {
        Dispatcher().Invoke(() =>
        {
            var world = Worlds.FirstOrDefault(w => w.Id == instanceId);
            if (world is null)
            {
                return;
            }

            var wasTailing = _logTails.ContainsKey(instanceId);
            world.IsRunningState = _processes.IsRunning(instanceId);
            if (world.IsRunningState)
            {
                if (!wasTailing)
                {
                    world.OnServerStarted();
                    StartLogTail(world);
                    StartA2SCorroborator(world);
                }
            }
            else
            {
                StopLogTail(instanceId);
                StopA2SCorroborator(instanceId);
                _networkAutoRefreshDone.Remove(instanceId);
                world.OnServerStopped();
                world.MarkServerStopped();
                ScheduleRosterSave(world);
            }

            RefreshInstallStatus(world);
            StartServerCommand.NotifyCanExecuteChanged();
            StopServerCommand.NotifyCanExecuteChanged();
            RestartServerCommand.NotifyCanExecuteChanged();
            FireAndForgetAutoLoad(world);
        });
    }

    private static System.Windows.Threading.Dispatcher Dispatcher() =>
        Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
}
