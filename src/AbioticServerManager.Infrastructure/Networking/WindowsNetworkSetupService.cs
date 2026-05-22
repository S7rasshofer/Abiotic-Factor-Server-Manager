using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Networking;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Networking;

using SysProcess = System.Diagnostics.Process;

public sealed class WindowsNetworkSetupService : INetworkSetupService
{
    private readonly IServerExecutableLocator _locator;
    private readonly IAppPaths _paths;
    private readonly ILogger<WindowsNetworkSetupService> _logger;

    public WindowsNetworkSetupService(
        IServerExecutableLocator locator,
        IAppPaths paths,
        ILogger<WindowsNetworkSetupService> logger)
    {
        _locator = locator;
        _paths = paths;
        _logger = logger;
    }

    public Ipv4SelectionResult DetectLanIpv4() =>
        Ipv4Selection.Select(GatherCandidates());

    public async Task<NetworkSetupStatus> InspectAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        var executable = _locator.Locate(instance.InstallPath);
        var selection = Ipv4Selection.Select(GatherCandidates());
        var portValidation = NetworkPortValidation.Validate(instance.GamePort, instance.QueryPort);

        if (selection.Best is { Length: > 0 } best)
        {
            instance.Network.LastDetectedLanIpv4 = best;
        }

        var (inspection, inspectionError) = await InspectFirewallAsync(
            instance,
            executable,
            ct).ConfigureAwait(false);

        var checks = BuildChecks(
            instance,
            executable,
            selection,
            portValidation,
            inspection,
            inspectionError);

        return new NetworkSetupStatus
        {
            Checks = checks,
            RouterChecklist = RouterChecklistBuilder.Build(
                instance.DisplayName,
                instance.GamePort,
                instance.QueryPort,
                selection.Best),
            LocalIpv4Addresses = selection.Usable,
            LocalIpv4CandidateDetails = FormatCandidateDetails(selection.UsableCandidates),
            SuggestedRouterTarget = selection.Best,
            ServerExecutablePath = executable,
            InspectionError = inspectionError,
            LogPath = CurrentLogPath(),
            PortValidationMessages = portValidation.Messages,
            Environment = inspection?.Environment,
            PortBindings = inspection?.Ports ?? [],
            LastFirewallRepairAtUtc = instance.Network.LastFirewallRepairAtUtc,
            MultipleIpWarning = selection.HasAmbiguity ? selection.Warning : null,
        };
    }

    public async Task<FirewallSetupResult> EnsureFirewallRulesAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        var logPath = CurrentLogPath();
        var portValidation = NetworkPortValidation.Validate(instance.GamePort, instance.QueryPort);
        if (!portValidation.CanCreateRules)
        {
            var why = string.Join(
                " ",
                portValidation.Messages
                    .Where(m => m.Severity == DiagnosticSeverity.Error)
                    .Select(m => m.Message));
            return new FirewallSetupResult
            {
                Success = false,
                Message = "Cannot create firewall rules: " + why,
                Diagnostics = why,
                LogPath = logPath,
            };
        }

        // The server executable may not exist yet (server not installed). That
        // must NOT block firewall creation — the two UDP port rules are what
        // friends actually need, and they do not depend on the executable. Only
        // the optional program rule needs it, and the script skips that cleanly.
        var executable = _locator.Locate(instance.InstallPath);
        var hasExecutable = !string.IsNullOrWhiteSpace(executable);

        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"facility-overseer-firewall-{Guid.NewGuid():N}.json");

        var script = FirewallScriptBuilder.BuildEnsureRulesScript(instance, executable, resultPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " +
                        EncodePowerShell(script),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = SysProcess.Start(startInfo);
            if (process is null)
            {
                return new FirewallSetupResult
                {
                    Success = false,
                    Message = "Windows did not start the elevated firewall repair process.",
                    LogPath = logPath,
                };
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var exitCode = process.ExitCode;
            var outcome = ReadOutcome(resultPath);

            if (outcome is null)
            {
                _logger.LogWarning(
                    "Elevated firewall writer exited {Exit} without a result file", exitCode);
                return new FirewallSetupResult
                {
                    Success = false,
                    Message = "The firewall repair finished but did not report a result " +
                              $"(exit code {exitCode}). No router settings were changed. " +
                              $"Diagnostics were saved to: {logPath}",
                    Diagnostics = $"Exit code {exitCode}; no result file at {resultPath}.",
                    LogPath = logPath,
                };
            }

            LogOutcome(instance, exitCode, outcome);

            var (inspection, inspectionError) = await InspectFirewallAsync(
                instance,
                executable,
                ct).ConfigureAwait(false);
            var verified = VerifyRequiredRules(inspection, requireProgramRule: hasExecutable);
            var verificationProblems = VerificationProblems(inspection, inspectionError, hasExecutable);

            return MapOutcome(outcome, verified, verificationProblems, logPath, hasExecutable);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new FirewallSetupResult
            {
                Success = false,
                NeedsAdmin = true,
                Message = "Administrator permission is required to change Windows Firewall. " +
                          "The permission prompt was declined, so no rules were changed.",
                LogPath = logPath,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair Windows Firewall rules");
            return new FirewallSetupResult
            {
                Success = false,
                Message = "Firewall repair failed: " + ex.Message +
                          $"\n\nNo router settings were changed.\nDiagnostics were saved to:\n{logPath}",
                Diagnostics = ex.ToString(),
                LogPath = logPath,
            };
        }
        finally
        {
            TryDelete(resultPath);
        }
    }

    private async Task<(FirewallInspection? Inspection, string? Error)> InspectFirewallAsync(
        ServerInstance instance,
        string? executable,
        CancellationToken ct)
    {
        try
        {
            var script = FirewallScriptBuilder.BuildInspectionScript(instance, executable);
            var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
            {
                return (FirewallInspectionParser.ParseInspection(result.Output.Trim()), null);
            }

            var error = result.Error.Trim() is { Length: > 0 } e
                ? e
                : "PowerShell could not inspect Windows Firewall rules.";
            _logger.LogWarning("PowerShell firewall inspection failed: {Error}", error);
            return (null, CleanPowerShellError(error));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not inspect Windows Firewall rules");
            return (null, CleanPowerShellError(ex.Message));
        }
    }

    private void LogOutcome(ServerInstance instance, int exitCode, FirewallApplyOutcome outcome)
    {
        _logger.LogInformation(
            "Firewall repair finished for {WorldId} (exit {Exit}): created={Created} " +
            "removed={Removed} errors={Errors}",
            instance.Id,
            exitCode,
            outcome.RulesCreated,
            outcome.StaleRulesRemoved,
            outcome.Errors.Count);

        foreach (var line in outcome.RawLog.Split(
            [Environment.NewLine],
            StringSplitOptions.RemoveEmptyEntries))
        {
            _logger.LogInformation("Firewall rule diagnostic: {DiagnosticJson}", line);
        }

        foreach (var error in outcome.Errors)
        {
            _logger.LogWarning("Firewall repair error for {WorldId}: {Error}", instance.Id, error);
        }
    }

    private static FirewallSetupResult MapOutcome(
        FirewallApplyOutcome outcome,
        bool verified,
        IReadOnlyList<string> verificationProblems,
        string logPath,
        bool hasExecutable)
    {
        if (outcome.Success && verified)
        {
            return new FirewallSetupResult
            {
                Success = true,
                Message = hasExecutable
                    ? "Windows Firewall rules were repaired and verified: game UDP, " +
                      "query UDP, and server executable."
                    : "Windows Firewall game and query UDP rules were created and verified. " +
                      "The optional server-executable rule was skipped because the dedicated " +
                      "server is not installed yet — run Prepare / Update Server, then " +
                      "Create / Repair rules again to add it.",
                Diagnostics = outcome.RawLog,
                LogPath = logPath,
            };
        }

        var firstError = outcome.Errors.FirstOrDefault()
            ?? verificationProblems.FirstOrDefault()
            ?? "Windows Firewall verification failed.";
        var details = string.Join(
            Environment.NewLine,
            outcome.Errors
                .Concat(verificationProblems)
                .Append(outcome.RawLog)
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new FirewallSetupResult
        {
            Success = false,
            Message = "Firewall repair failed while creating or verifying the selected " +
                      $"world's rules.\n\nWindows reported: {firstError}\n\n" +
                      "No router settings were changed.\nDiagnostics were saved to:\n" +
                      logPath,
            Diagnostics = details,
            LogPath = logPath,
        };
    }

    private static bool VerifyRequiredRules(FirewallInspection? inspection, bool requireProgramRule)
    {
        if (inspection is null)
        {
            return false;
        }

        // The program rule is only a hard requirement when the server executable
        // exists. Without it, game + query UDP rules are the full required set.
        var required = requireProgramRule
            ? new[] { FirewallRuleRole.Game, FirewallRuleRole.Query, FirewallRuleRole.Program }
            : [FirewallRuleRole.Game, FirewallRuleRole.Query];

        return required.All(role =>
            inspection.Roles.Any(r => r.Role == role && r.Exists && r.IsCorrect));
    }

    private static IReadOnlyList<string> VerificationProblems(
        FirewallInspection? inspection,
        string? inspectionError,
        bool requireProgramRule)
    {
        if (inspection is null)
        {
            return [inspectionError ?? "Firewall state could not be re-read after repair."];
        }

        return [.. inspection.Roles
            // A missing program rule is expected when the server is not installed —
            // do not report it as a verification problem.
            .Where(r => requireProgramRule || r.Role != FirewallRuleRole.Program)
            .Where(r => !r.Exists || !r.IsCorrect)
            .Select(r => $"{r.Role}: {string.Join(" ", r.Problems)}")
            .Where(s => s.Length > 0)];
    }

    private static IReadOnlyList<NetworkCheckResult> BuildChecks(
        ServerInstance instance,
        string? executable,
        Ipv4SelectionResult selection,
        PortValidationResult portValidation,
        FirewallInspection? inspection,
        string? inspectionError)
    {
        var checks = new List<NetworkCheckResult>();

        AddSourceOfTruthChecks(checks, instance, executable, portValidation);
        AddLanChecks(checks, selection);
        AddFirewallChecks(checks, instance, executable, inspection, inspectionError);
        AddEnvironmentChecks(checks, inspection);
        AddExternalReachabilityCheck(checks);

        return checks;
    }

    private static void AddSourceOfTruthChecks(
        List<NetworkCheckResult> checks,
        ServerInstance instance,
        string? executable,
        PortValidationResult validation)
    {
        checks.Add(PortCheck(
            "ports.game",
            "Game UDP port",
            instance.GamePort,
            validation.Messages.Where(m => m.Code.StartsWith("GAME_", StringComparison.Ordinal))));
        checks.Add(PortCheck(
            "ports.query",
            "Query UDP port",
            instance.QueryPort,
            validation.Messages.Where(m => m.Code.StartsWith("QUERY_", StringComparison.Ordinal))));

        var collision = validation.Messages.FirstOrDefault(m => m.Code == "PORT_CONFLICT");
        checks.Add(new NetworkCheckResult
        {
            Id = "ports.distinct",
            Label = "Game/query port pairing",
            Category = "Source of truth",
            Value = $"{instance.GamePort} / {instance.QueryPort}",
            Status = collision is null ? NetworkCheckStatus.Pass : NetworkCheckStatus.Fail,
            Summary = collision is null
                ? "Game and query ports are distinct."
                : collision.Message,
            Remediation = collision?.SuggestedFix ?? "",
        });

        var warnings = validation.Messages
            .Where(m => m.Severity == DiagnosticSeverity.Warning)
            .Select(m => m.Message)
            .ToList();
        if (warnings.Count > 0)
        {
            checks.Add(new NetworkCheckResult
            {
                Id = "ports.warning",
                Label = "Port risk warning",
                Category = "Source of truth",
                Value = $"{instance.GamePort} / {instance.QueryPort}",
                Status = NetworkCheckStatus.Warn,
                Summary = string.Join(" ", warnings),
                Remediation = "Use the Abiotic Factor defaults or another high UDP port pair unless you know why this port is needed.",
            });
        }

        checks.Add(new NetworkCheckResult
        {
            Id = "launch.args",
            Label = "Server launch arguments",
            Category = "Source of truth",
            Value = $"-PORT={instance.GamePort} -QUERYPORT={instance.QueryPort}",
            Status = NetworkCheckStatus.Pass,
            Summary = "The server launch command is generated from the Server tab ports.",
            Details = "Firewall rules and the router checklist use these same current values.",
        });

        checks.Add(new NetworkCheckResult
        {
            Id = "server.exe",
            Label = "Dedicated server executable",
            Category = "Source of truth",
            Value = executable ?? "Not found",
            Status = string.IsNullOrWhiteSpace(executable)
                ? NetworkCheckStatus.Fail
                : NetworkCheckStatus.Pass,
            Summary = string.IsNullOrWhiteSpace(executable)
                ? "The dedicated server executable could not be located."
                : "The executable path is available for the firewall program rule.",
            Remediation = string.IsNullOrWhiteSpace(executable)
                ? "Prepare or update the server, then run Check Setup again."
                : "",
        });

        checks.Add(new NetworkCheckResult
        {
            Id = "router.checklist.ports",
            Label = "Router checklist ports",
            Category = "Source of truth",
            Value = $"UDP {instance.GamePort}, UDP {instance.QueryPort}",
            Status = validation.CanCreateRules ? NetworkCheckStatus.Pass : NetworkCheckStatus.Fail,
            Summary = "The router checklist is generated from the selected world's current Server tab ports.",
            Remediation = validation.CanCreateRules
                ? ""
                : "Fix the Server tab port values before copying router instructions.",
        });
    }

    private static NetworkCheckResult PortCheck(
        string id,
        string label,
        int port,
        IEnumerable<DiagnosticMessage> messages)
    {
        var errors = messages.Where(m => m.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            return new NetworkCheckResult
            {
                Id = id,
                Label = label,
                Category = "Source of truth",
                Value = port.ToString(),
                Status = NetworkCheckStatus.Fail,
                Summary = string.Join(" ", errors.Select(e => e.Message)),
                Remediation = string.Join(" ", errors.Select(e => e.SuggestedFix).Where(s => s is { Length: > 0 })) ?? "",
            };
        }

        return new NetworkCheckResult
        {
            Id = id,
            Label = label,
            Category = "Source of truth",
            Value = port.ToString(),
            Status = NetworkCheckStatus.Pass,
            Summary = $"UDP {port} is a valid port number.",
        };
    }

    private static void AddLanChecks(
        List<NetworkCheckResult> checks,
        Ipv4SelectionResult selection)
    {
        checks.Add(new NetworkCheckResult
        {
            Id = "lan.best",
            Label = "Best LAN IPv4",
            Category = "LAN IPv4",
            Value = selection.Best ?? "None",
            Status = selection.Best is { Length: > 0 }
                ? NetworkCheckStatus.Pass
                : NetworkCheckStatus.Fail,
            Summary = selection.Best is { Length: > 0 } best
                ? $"Using {best} as the router checklist target."
                : "No usable LAN IPv4 detected.",
            Details = selection.Warning ?? "",
            Remediation = selection.Best is { Length: > 0 }
                ? "Reserve this IP in DHCP so it does not change."
                : "Connect this PC to your LAN and run Check Setup again.",
        });

        if (selection.HasAmbiguity && selection.Warning is { Length: > 0 } warning)
        {
            checks.Add(new NetworkCheckResult
            {
                Id = "lan.multiple",
                Label = "Multiple LAN IPv4 candidates",
                Category = "LAN IPv4",
                Value = string.Join(", ", selection.Usable),
                Status = NetworkCheckStatus.Warn,
                Summary = warning,
                Remediation = "Confirm which adapter your router uses before updating port forwards.",
            });
        }

        // Router-target drift is covered by the §3.1a launch banner
        // (last-seen LAN IPv4 vs current), so it is no longer tracked here.
    }

    private static void AddFirewallChecks(
        List<NetworkCheckResult> checks,
        ServerInstance instance,
        string? executable,
        FirewallInspection? inspection,
        string? inspectionError)
    {
        checks.Add(RuleCheck(
            FirewallRuleRole.Game,
            "Game UDP firewall rule",
            $"UDP {instance.GamePort}",
            inspection,
            inspectionError));
        checks.Add(RuleCheck(
            FirewallRuleRole.Query,
            "Query UDP firewall rule",
            $"UDP {instance.QueryPort}",
            inspection,
            inspectionError));
        checks.Add(RuleCheck(
            FirewallRuleRole.Program,
            "Server executable firewall rule",
            executable ?? "Not found",
            inspection,
            inspectionError));
    }

    private static NetworkCheckResult RuleCheck(
        FirewallRuleRole role,
        string label,
        string value,
        FirewallInspection? inspection,
        string? inspectionError)
    {
        var status = inspection?.Roles.FirstOrDefault(r => r.Role == role);
        var id = role switch
        {
            FirewallRuleRole.Game => "firewall.game",
            FirewallRuleRole.Query => "firewall.query",
            FirewallRuleRole.Program => "firewall.exe",
            _ => "firewall.unknown",
        };

        if (status is null)
        {
            return new NetworkCheckResult
            {
                Id = id,
                Label = label,
                Category = SetupCheckCategory.FirewallRule,
                Value = value,
                Status = inspectionError is { Length: > 0 }
                    ? NetworkCheckStatus.Unknown
                    : NetworkCheckStatus.Fail,
                Summary = inspectionError is { Length: > 0 }
                    ? "Firewall state could not be inspected."
                    : "No Facility Overseer managed rule was found for this world.",
                Details = inspectionError ?? "",
                Remediation = "Run Create / Repair Windows Firewall Rules.",
            };
        }

        if (status.Exists && status.IsCorrect)
        {
            return new NetworkCheckResult
            {
                Id = id,
                Label = label,
                Category = SetupCheckCategory.FirewallRule,
                Value = value,
                Status = NetworkCheckStatus.Pass,
                Summary = "Managed rule is correct.",
                Details = $"Matched \"{status.DisplayName}\".",
            };
        }

        return new NetworkCheckResult
        {
            Id = id,
            Label = label,
            Category = SetupCheckCategory.FirewallRule,
            Value = value,
            Status = NetworkCheckStatus.Fail,
            Summary = status.Exists
                ? "Managed rule exists but needs repair."
                : "Managed rule is missing.",
            Details = status.Problems.Count > 0
                ? string.Join(" ", status.Problems)
                : "",
            Remediation = "Run Create / Repair Windows Firewall Rules.",
        };
    }

    private static void AddEnvironmentChecks(
        List<NetworkCheckResult> checks,
        FirewallInspection? inspection)
    {
        if (inspection is null)
        {
            return;
        }

        var env = inspection.Environment;
        checks.Add(new NetworkCheckResult
        {
            Id = "environment.admin",
            Label = "Administrator rights",
            Category = SetupCheckCategory.Environment,
            Value = env.IsElevated ? "Elevated" : "Standard",
            Status = env.IsElevated ? NetworkCheckStatus.Pass : NetworkCheckStatus.NeedsAdmin,
            Summary = env.IsElevated
                ? "The app is running elevated."
                : "Check Setup works without admin; firewall repair will request administrator permission.",
        });

        checks.Add(new NetworkCheckResult
        {
            Id = "environment.profile",
            Label = "Windows network profile",
            Category = SetupCheckCategory.Environment,
            Value = env.NetworkProfile,
            Status = NetworkCheckStatus.Pass,
            Summary = "Facility Overseer firewall rules use Profile Any.",
            Details = "They apply on Public, Private and Domain profiles.",
        });

        checks.Add(new NetworkCheckResult
        {
            Id = "process.server",
            Label = "Dedicated server process",
            Category = SetupCheckCategory.Environment,
            Value = env.ServerProcessRunning
                ? string.Join(", ", env.ServerProcessNames)
                : "Not running",
            Status = env.ServerProcessRunning ? NetworkCheckStatus.Pass : NetworkCheckStatus.Warn,
            Summary = env.ServerProcessRunning
                ? "The dedicated server process is running."
                : "The server is stopped, so UDP endpoints may not be bound yet.",
            Remediation = env.ServerProcessRunning
                ? ""
                : "Start the server, then run Check Setup again.",
        });

        foreach (var port in inspection.Ports)
        {
            checks.Add(PortBindingCheck(port));
        }
    }

    private static NetworkCheckResult PortBindingCheck(PortBindingStatus port)
    {
        var roleLabel = port.Role == "game" ? "Game UDP endpoint" : "Query UDP endpoint";
        if (!port.IsListening)
        {
            return new NetworkCheckResult
            {
                Id = $"endpoint.{port.Role}",
                Label = roleLabel,
                Category = SetupCheckCategory.Port,
                Value = $"UDP {port.Port}",
                Status = NetworkCheckStatus.Warn,
                Summary = $"Nothing is bound to UDP {port.Port}.",
                Details = "This is expected when the server is stopped.",
                Remediation = "Start or restart the server and run Check Setup again.",
            };
        }

        var owners = DescribeOwners(port);
        if (port.ForeignOwner)
        {
            return new NetworkCheckResult
            {
                Id = $"endpoint.{port.Role}",
                Label = roleLabel,
                Category = SetupCheckCategory.Port,
                Value = $"UDP {port.Port}",
                Status = NetworkCheckStatus.Fail,
                Summary = $"UDP {port.Port} is owned by another process.",
                Details = owners,
                Remediation = "Stop that process or change the Server tab ports.",
            };
        }

        return new NetworkCheckResult
        {
            Id = $"endpoint.{port.Role}",
            Label = roleLabel,
            Category = SetupCheckCategory.Port,
            Value = $"UDP {port.Port}",
            Status = NetworkCheckStatus.Pass,
            Summary = $"UDP {port.Port} is bound.",
            Details = owners,
        };
    }

    private static void AddExternalReachabilityCheck(List<NetworkCheckResult> checks) =>
        checks.Add(new NetworkCheckResult
        {
            Id = "external.reachability",
            Label = "External reachability",
            Category = "Router / internet",
            Value = "Unknown",
            Status = NetworkCheckStatus.Unknown,
            Summary = "This app cannot reliably prove internet reachability from inside your own network.",
            Details = "Some routers do not support hairpin NAT, so local tests against your public IP can lie. Ask someone outside your network to test joining.",
        });

    private static string DescribeOwners(PortBindingStatus port)
    {
        var pids = port.OwningPids.Count > 0
            ? "pid " + string.Join(", ", port.OwningPids)
            : "pid unknown";
        var names = port.OwningProcesses.Count > 0
            ? string.Join(", ", port.OwningProcesses)
            : "unknown process";
        var paths = port.OwningProcessPaths.Count > 0
            ? " Path: " + string.Join("; ", port.OwningProcessPaths)
            : "";
        return $"{names} ({pids}).{paths}";
    }

    private static IReadOnlyList<Ipv4Candidate> GatherCandidates()
    {
        try
        {
            var list = new List<Ipv4Candidate>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = nic.GetIPProperties();
                var gateways = props.GatewayAddresses
                    .Where(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(g.Address))
                    .Select(g => g.Address.ToString())
                    .ToList();
                var gateway = gateways.FirstOrDefault() ?? "";

                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    list.Add(new Ipv4Candidate
                    {
                        Address = ua.Address.ToString(),
                        InterfaceName = nic.Name,
                        InterfaceDescription = nic.Description,
                        IsUp = nic.OperationalStatus == OperationalStatus.Up,
                        HasDefaultGateway = gateways.Count > 0,
                        GatewayAddress = gateway,
                        IsLoopbackOrTunnel =
                            nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                or NetworkInterfaceType.Tunnel,
                    });
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> FormatCandidateDetails(
        IReadOnlyList<Ipv4Candidate> candidates) =>
        [.. candidates.Select(c =>
        {
            var gateway = string.IsNullOrWhiteSpace(c.GatewayAddress)
                ? "none"
                : c.GatewayAddress;
            return $"{c.Address} via {c.InterfaceName}, gateway {gateway}";
        })];

    private FirewallApplyOutcome? ReadOutcome(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return FirewallInspectionParser.ParseOutcome(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read firewall outcome file {Path}", path);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp file; best effort.
        }
    }

    private async Task<PowerShellResult> RunPowerShellAsync(
        string script,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " +
                        EncodePowerShell(script),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = SysProcess.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell did not start.");

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new PowerShellResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }

    private string CurrentLogPath() =>
        Path.Combine(_paths.LogsDirectory, $"overseer-{DateTimeOffset.Now:yyyyMMdd}.log");

    private static string CleanPowerShellError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "";
        }

        var cleaned = error.Trim();
        if (cleaned.StartsWith("#< CLIXML", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["#< CLIXML".Length..];
        }

        cleaned = cleaned
            .Replace("_x000D__x000A_", Environment.NewLine, StringComparison.Ordinal)
            .Replace("_x000A_", Environment.NewLine, StringComparison.Ordinal)
            .Replace("_x000D_", Environment.NewLine, StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"<S\s+S=""Error"">", " ");
        cleaned = Regex.Replace(cleaned, @"</S>|<[^>]+>", " ");
        cleaned = WebUtility.HtmlDecode(cleaned);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        const int maxDisplayLength = 360;
        return cleaned.Length <= maxDisplayLength
            ? cleaned
            : cleaned[..maxDisplayLength] + "...";
    }

    private static string EncodePowerShell(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
