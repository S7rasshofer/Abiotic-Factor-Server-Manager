using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Networking;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IInstanceStore _store;
    private readonly IPlayerRosterStore _rosterStore;
    private readonly IDiagnosticsService _diagnostics;
    private readonly INetworkSetupService _networkSetup;
    private readonly ISteamCmdService _steamCmd;
    private readonly IServerProcessService _processes;
    private readonly ISandboxSettingsService _sandbox;
    private readonly IBackupService _backups;
    private readonly IAdminListService _adminList;
    private readonly IPlayerBanService _bans;
    private readonly IServerInstallStateService _serverInstallState;
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

    public MainViewModel(
        IInstanceStore store,
        IPlayerRosterStore rosterStore,
        IDiagnosticsService diagnostics,
        INetworkSetupService networkSetup,
        ISteamCmdService steamCmd,
        IServerProcessService processes,
        ISandboxSettingsService sandbox,
        IBackupService backups,
        IAdminListService adminList,
        IPlayerBanService bans,
        IServerInstallStateService serverInstallState,
        IAppPaths paths,
        ILogger<MainViewModel> logger)
    {
        _store = store;
        _rosterStore = rosterStore;
        _diagnostics = diagnostics;
        _networkSetup = networkSetup;
        _steamCmd = steamCmd;
        _processes = processes;
        _sandbox = sandbox;
        _backups = backups;
        _adminList = adminList;
        _bans = bans;
        _serverInstallState = serverInstallState;
        _paths = paths;
        _logger = logger;

        _processes.LogReceived += OnLogReceived;
        _processes.RuntimeChanged += OnRuntimeChanged;
    }

    public ObservableCollection<ServerInstanceViewModel> Worlds { get; } = [];

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

    public async Task InitializeAsync()
    {
        var loaded = await _store.LoadAsync();
        foreach (var model in loaded)
        {
            Worlds.Add(CreateWorldVm(model));
        }

        if (Worlds.Count == 0)
        {
            // First launch: stand up a ready-to-go world so the user only has to
            // rename it. The staged-config flow means this creates a profile and
            // editable sandbox WITHOUT an Abiotic Factor save folder, so it does
            // not reintroduce the partial-world-save bug.
            await AddWorldAsync("My World");
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
                line => Dispatcher().BeginInvoke(() => OnTailLine(world, line)),
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

    private void OnTailLine(ServerInstanceViewModel world, ServerLogLine line)
    {
        world.ApplyHealth(line);
        if (world.ApplyRosterActivity(line))
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
        if (_installPromptShown || SelectedWorld is null)
        {
            return;
        }

        _installPromptShown = true;

        if (string.IsNullOrWhiteSpace(SelectedWorld.InstallPath))
        {
            SelectedWorld.InstallPath = _paths.ManagedServerDirectory;
            await SaveAsync();
        }

        var state = _serverInstallState.Evaluate(SelectedWorld.Model);
        if (state.IsLaunchable)
        {
            RefreshInstallStatus(SelectedWorld);
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

    private ServerInstanceViewModel CreateWorldVm(ServerInstance model) =>
        new(model) { Sandbox = new SandboxSettingsViewModel(_sandbox) };

    private ServerInstance CreateDefaultInstance(string name) => new()
    {
        DisplayName = name,
        SteamServerName = name,
        WorldSaveName = name,
        InstallPath = _paths.DefaultServerInstallDirectory,
    };

    [RelayCommand]
    private async Task CreateWorld() =>
        await AddWorldAsync($"World {Worlds.Count + 1}");

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
            Clipboard.SetText(string.Join(Environment.NewLine, lines));

            world.Model.Network.LastRouterChecklistIpv4 = status.SuggestedRouterTarget;
            world.Model.Network.LastRouterChecklistCopiedAtUtc = DateTimeOffset.UtcNow;
            ApplyNetworkSetupStatus(world, await _networkSetup.InspectAsync(world.Model));
            await SaveAsync();

            var target = status.SuggestedRouterTarget ?? "the current LAN IPv4";
            MessageBox.Show(
                $"Router checklist copied for {target}. Reserve this IP in DHCP.",
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

            Clipboard.SetText(BuildNetworkDiagnosticsText(world, status));
            MessageBox.Show("Network diagnostics copied to the clipboard.", "Facility Overseer");
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

    private static void ApplyNetworkSetupStatus(
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
        world.LastRouterChecklistText = status.LastRouterChecklistIpv4 is { Length: > 0 } lastIp
            ? $"{lastIp} copied {FormatUtc(status.LastRouterChecklistCopiedAtUtc)}"
            : "No router checklist copied for this world yet.";
        world.LastFirewallRepairText = status.LastFirewallRepairAtUtc is { } repairedAt
            ? $"Last repaired {FormatUtc(repairedAt)}"
            : "No firewall repair has been run for this world yet.";

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

        lines.Add($"Last router checklist IP: {status.LastRouterChecklistIpv4 ?? "none"}");
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
        }
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
    }

    // ===================== Admin list (plan §6.3) =====================

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

            world.Admins.Clear();
            foreach (var id in _adminList.Load(path))
            {
                world.Admins.Add(id);
            }

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
            }
        }
    }

    // ===================== Backup & Restore (plan §14) =====================

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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task BanSelectedPlayer()
    {
        var world = SelectedWorld;
        var player = world?.SelectedRosterPlayer;
        if (world is null || player is null)
        {
            MessageBox.Show(
                "Select a player in the roster first.", "Facility Overseer - Ban");
            return;
        }

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
        var player = world?.SelectedRosterPlayer;
        if (world is null || player is null)
        {
            MessageBox.Show(
                "Select a player in the roster first.", "Facility Overseer - Unban");
            return;
        }

        var id = !string.IsNullOrWhiteSpace(player.SteamId64)
            ? player.SteamId64!
            : player.PrimaryId ?? "";

        var result = _bans.Unban(world.Model, id);
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
        var path = string.IsNullOrWhiteSpace(resolution.DefaultSandboxTarget)
            ? Path.Combine(
                _paths.DataRoot,
                "worlds",
                string.IsNullOrWhiteSpace(instance.Id) ? "default" : instance.Id,
                DefaultSandboxSettings.FileName)
            : resolution.DefaultSandboxTarget;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

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
        var tail = trimmed.Length > max ? "…" + trimmed[^max..] : trimmed;
        return "\n\nSteamCMD output:\n" + tail;
    }

    private void RefreshInstallStatus(ServerInstanceViewModel? world)
    {
        if (world is null)
        {
            return;
        }

        // While running, the health tracker owns StatusText (Starting/Online/
        // Blocked/Crashed). Only set it from install state when stopped.
        if (!world.IsRunningState)
        {
            var stopped = _serverInstallState.Evaluate(world.Model);
            world.StatusText = stopped.IsLaunchable ? "Stopped" : stopped.ValidationMessage;
        }

        RefreshHeaderInfo(_serverInstallState.Evaluate(world.Model));
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

    private bool CanPrepareServer() => SelectedWorld is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPrepareServer))]
    private async Task InstallOrUpdateServer()
    {
        if (SelectedWorld is null || IsBusy)
        {
            return;
        }

        var installPath = _paths.ManagedServerDirectory;

        SelectedWorld.InstallPath = installPath;
        await SaveAsync();

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
                }
            }
            else
            {
                StopLogTail(instanceId);
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
