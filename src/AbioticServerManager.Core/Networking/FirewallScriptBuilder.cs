using System.Globalization;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Generates the PowerShell used to inspect and repair the Windows Firewall rules.
/// The generated writer removes only Facility Overseer rules for the selected world
/// and recreates the game-port, query-port and executable rules with separate
/// New-NetFirewallRule parameter sets.
/// </summary>
public static class FirewallScriptBuilder
{
    public const string DisplayGroup = "Facility Overseer";

    public static string RoleToken(FirewallRuleRole role) => role switch
    {
        FirewallRuleRole.Game => "game",
        FirewallRuleRole.Query => "query",
        FirewallRuleRole.Program => "program",
        _ => "unknown",
    };

    public static string PurposeToken(FirewallRuleRole role) => role switch
    {
        FirewallRuleRole.Game => "GamePort",
        FirewallRuleRole.Query => "QueryPort",
        FirewallRuleRole.Program => "ServerExecutable",
        _ => "Unknown",
    };

    public static string RuleDisplayName(FirewallRuleRole role, int port) =>
        RuleDisplayName(role, "World", port);

    public static string RuleDisplayName(FirewallRuleRole role, string worldName, int port)
    {
        var world = CleanDisplaySegment(worldName);
        return role switch
        {
            FirewallRuleRole.Game =>
                $"Facility Overseer - {world} - Abiotic Factor Game UDP {port}",
            FirewallRuleRole.Query =>
                $"Facility Overseer - {world} - Abiotic Factor Query UDP {port}",
            FirewallRuleRole.Program =>
                $"Facility Overseer - {world} - Abiotic Factor Server executable",
            _ => $"Facility Overseer - {world} - Abiotic Factor",
        };
    }

    public static string RuleDescription(string worldId, FirewallRuleRole role) =>
        $"Managed by Facility Overseer. WorldId={Sanitize(worldId)}; Purpose={PurposeToken(role)}";

    /// <summary>Legacy marker from earlier Facility Overseer builds.</summary>
    public static string Marker(string worldId, FirewallRuleRole role) =>
        $"FOID={Sanitize(worldId)};ROLE={RoleToken(role)}";

    private static string CleanDisplaySegment(string value)
    {
        var cleaned = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return cleaned.Length == 0 ? "World" : cleaned;
    }

    private static string Sanitize(string id) =>
        new([.. id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);

    private static string Int(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Ps(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static string BuildInspectionScript(ServerInstance instance, string? executable)
    {
        var worldId = Ps(Sanitize(instance.Id));
        var gamePort = Int(instance.GamePort);
        var queryPort = Int(instance.QueryPort);
        var program = string.IsNullOrWhiteSpace(executable) ? "$null" : Ps(executable);
        var group = Ps(DisplayGroup);
        var gamePurpose = Ps(PurposeToken(FirewallRuleRole.Game));
        var queryPurpose = Ps(PurposeToken(FirewallRuleRole.Query));
        var programPurpose = Ps(PurposeToken(FirewallRuleRole.Program));
        var gameLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Game));
        var queryLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Query));
        var programLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Program));

        return $$"""
            $ProgressPreference = 'SilentlyContinue'
            $worldId = {{worldId}}
            $group = {{group}}
            $result = [ordered]@{
                Roles = New-Object 'System.Collections.Generic.List[object]'
                Ports = New-Object 'System.Collections.Generic.List[object]'
                Environment = [ordered]@{
                    IsElevated = $false
                    NetworkProfile = 'Unknown'
                    ServerProcessRunning = $false
                    ServerProcessNames = @()
                }
            }

            function Contains-Text($text, $needle) {
                if ([string]::IsNullOrWhiteSpace([string]$text) -or [string]::IsNullOrWhiteSpace([string]$needle)) {
                    return $false
                }
                return ([string]$text).IndexOf([string]$needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }

            function Is-ManagedForPurpose($rule, [string] $purpose, [string] $legacyMarker) {
                $description = [string]$rule.Description
                if ([string]::IsNullOrWhiteSpace($description)) { return $false }
                $hasWorld = (Contains-Text $description "WorldId=$worldId") -or
                    (Contains-Text $description "FOID=$worldId") -or
                    (Contains-Text $description $worldId)
                if (-not $hasWorld) { return $false }
                return (Contains-Text $description "Purpose=$purpose") -or
                    (Contains-Text $description $legacyMarker)
            }

            function Get-ManagedRules([string] $purpose, [string] $legacyMarker) {
                return @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue |
                    Where-Object { Is-ManagedForPurpose $_ $purpose $legacyMarker })
            }

            try {
                $id = [Security.Principal.WindowsIdentity]::GetCurrent()
                $p = New-Object Security.Principal.WindowsPrincipal($id)
                $result.Environment.IsElevated =
                    $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            } catch { }

            try {
                $cats = @(Get-NetConnectionProfile -ErrorAction Stop |
                    Select-Object -ExpandProperty NetworkCategory -Unique)
                if ($cats.Count -gt 0) {
                    $result.Environment.NetworkProfile = ($cats -join ', ')
                }
            } catch { }

            try {
                $srv = @(Get-Process -Name 'AbioticFactor*' -ErrorAction SilentlyContinue)
                if ($srv.Count -gt 0) {
                    $result.Environment.ServerProcessRunning = $true
                    $result.Environment.ServerProcessNames =
                        @($srv | Select-Object -ExpandProperty ProcessName -Unique)
                }
            } catch { }

            function Find-SimilarManualRules($expectProtocol, $expectPort, $expectProgram) {
                $matches = New-Object 'System.Collections.Generic.List[string]'
                try {
                    foreach ($rule in @(Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
                        if ([string]$rule.Group -eq $group -and (Contains-Text $rule.Description $worldId)) {
                            continue
                        }
                        if ([string]$rule.Direction -ne 'Inbound' -or [string]$rule.Action -ne 'Allow' -or [string]$rule.Enabled -ne 'True') {
                            continue
                        }
                        if ($expectProtocol) {
                            $pf = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                            if ($null -ne $pf -and [string]$pf.Protocol -eq $expectProtocol -and [string]$pf.LocalPort -eq [string]$expectPort) {
                                $matches.Add([string]$rule.DisplayName)
                            }
                        } elseif ($expectProgram) {
                            $af = @(Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                            if ($null -ne $af -and [string]::Equals([string]$af.Program, [string]$expectProgram, [System.StringComparison]::OrdinalIgnoreCase)) {
                                $matches.Add([string]$rule.DisplayName)
                            }
                        }
                    }
                } catch { }
                return @($matches | Select-Object -Unique)
            }

            function Get-RoleStatus($role, $purpose, $legacyMarker, $expectProtocol, $expectPort, $expectProgram) {
                $row = [ordered]@{
                    Role = $role
                    Exists = $false
                    IsCorrect = $false
                    DisplayName = ''
                    Problems = New-Object 'System.Collections.Generic.List[string]'
                    ManualMatches = @()
                }
                try {
                    $rules = Get-ManagedRules $purpose $legacyMarker
                    if ($rules.Count -eq 0) {
                        $row.Problems.Add('No Facility Overseer rule found for this world and purpose.')
                        $manual = @(Find-SimilarManualRules $expectProtocol $expectPort $expectProgram)
                        $row.ManualMatches = $manual
                        if ($manual.Count -gt 0) {
                            $row.Problems.Add('Similar non-managed inbound allow rule(s) exist: ' + ($manual -join ', ') + '. Repair will create Facility Overseer managed rules without deleting these manual rules.')
                        }
                        return [pscustomobject]$row
                    }
                    $rule = $rules[0]
                    $row.Exists = $true
                    $row.DisplayName = [string]$rule.DisplayName
                    if ([string]$rule.Enabled -ne 'True') { $row.Problems.Add('Rule is disabled.') }
                    if ([string]$rule.Direction -ne 'Inbound') { $row.Problems.Add('Direction is not Inbound.') }
                    if ([string]$rule.Action -ne 'Allow') { $row.Problems.Add('Action is not Allow.') }
                    if ([string]$rule.Profile -ne 'Any') {
                        $row.Problems.Add("Profile is '$($rule.Profile)', expected Any.")
                    }
                    if ($rules.Count -gt 1) {
                        $row.Problems.Add("$($rules.Count) duplicate app-managed rules exist for this world and purpose.")
                    }
                    if ($expectProtocol) {
                        $pf = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                        if ($null -eq $pf) { $row.Problems.Add('No port filter on the rule.') }
                        else {
                            if ([string]$pf.Protocol -ne $expectProtocol) {
                                $row.Problems.Add("Protocol is '$($pf.Protocol)', expected $expectProtocol.")
                            }
                            if ([string]$pf.LocalPort -ne [string]$expectPort) {
                                $row.Problems.Add("LocalPort is '$($pf.LocalPort)', expected $expectPort. This may be an app-managed rule for a stale port.")
                            }
                        }
                    }
                    if ($expectProgram) {
                        $af = @(Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                        if ($null -eq $af -or [string]::IsNullOrWhiteSpace([string]$af.Program)) {
                            $row.Problems.Add('No program filter on the rule.')
                        } elseif (-not [string]::Equals([string]$af.Program, $expectProgram, [System.StringComparison]::OrdinalIgnoreCase)) {
                            $row.Problems.Add("Program is '$($af.Program)', expected '$expectProgram'.")
                        }
                    }
                    $row.IsCorrect = ($row.Problems.Count -eq 0)
                } catch {
                    $row.Problems.Add("Inspection error: $($_.Exception.Message)")
                }
                return [pscustomobject]$row
            }

            $result.Roles.Add((Get-RoleStatus 'game' {{gamePurpose}} {{gameLegacy}} 'UDP' {{gamePort}} $null))
            $result.Roles.Add((Get-RoleStatus 'query' {{queryPurpose}} {{queryLegacy}} 'UDP' {{queryPort}} $null))

            $programPath = {{program}}
            if ($programPath) {
                $result.Roles.Add((Get-RoleStatus 'program' {{programPurpose}} {{programLegacy}} $null 0 $programPath))
            }

            function Get-PortBinding([int] $port, [string] $role) {
                $row = [ordered]@{
                    Port = $port
                    Role = $role
                    IsListening = $false
                    OwningPids = @()
                    OwningProcesses = @()
                    OwningProcessPaths = @()
                }
                $pids = @()
                try {
                    $eps = @(Get-NetUDPEndpoint -LocalPort $port -ErrorAction Stop)
                    $pids = @($eps | Select-Object -ExpandProperty OwningProcess -Unique)
                } catch {
                    try {
                        $lines = @(netstat -ano -p udp | Select-String ":$port\s")
                        foreach ($l in $lines) {
                            $tok = ($l.ToString().Trim() -split '\s+')
                            if ($tok.Length -ge 4) { $pids += [int]$tok[-1] }
                        }
                        $pids = @($pids | Select-Object -Unique)
                    } catch { }
                }
                if ($pids.Count -gt 0) {
                    $row.IsListening = $true
                    $row.OwningPids = $pids
                    $names = @()
                    $paths = @()
                    foreach ($procId in $pids) {
                        try {
                            $proc = Get-Process -Id $procId -ErrorAction Stop
                            $names += $proc.ProcessName
                            if (-not [string]::IsNullOrWhiteSpace([string]$proc.Path)) {
                                $paths += [string]$proc.Path
                            }
                        } catch { }
                    }
                    $row.OwningProcesses = @($names | Select-Object -Unique)
                    $row.OwningProcessPaths = @($paths | Select-Object -Unique)
                }
                return [pscustomobject]$row
            }

            $result.Ports.Add((Get-PortBinding {{gamePort}} 'game'))
            $result.Ports.Add((Get-PortBinding {{queryPort}} 'query'))

            ConvertTo-Json -InputObject $result -Depth 7 -Compress
            """;
    }

    public static string BuildEnsureRulesScript(
        ServerInstance instance,
        string? executable,
        string resultJsonPath)
    {
        var worldName = instance.DisplayName;
        var worldId = Ps(Sanitize(instance.Id));
        var group = Ps(DisplayGroup);
        var resultPath = Ps(resultJsonPath);

        var gameName = Ps(RuleDisplayName(FirewallRuleRole.Game, worldName, instance.GamePort));
        var queryName = Ps(RuleDisplayName(FirewallRuleRole.Query, worldName, instance.QueryPort));
        var programName = Ps(RuleDisplayName(FirewallRuleRole.Program, worldName, 0));

        var gamePurpose = Ps(PurposeToken(FirewallRuleRole.Game));
        var queryPurpose = Ps(PurposeToken(FirewallRuleRole.Query));
        var programPurpose = Ps(PurposeToken(FirewallRuleRole.Program));

        var gameLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Game));
        var queryLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Query));
        var programLegacy = Ps(Marker(instance.Id, FirewallRuleRole.Program));

        var gameDesc = Ps(RuleDescription(instance.Id, FirewallRuleRole.Game));
        var queryDesc = Ps(RuleDescription(instance.Id, FirewallRuleRole.Query));
        var programDesc = Ps(RuleDescription(instance.Id, FirewallRuleRole.Program));

        var gamePort = Int(instance.GamePort);
        var queryPort = Int(instance.QueryPort);
        var program = string.IsNullOrWhiteSpace(executable) ? "$null" : Ps(executable);

        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            $worldId = {{worldId}}
            $group = {{group}}
            $operations = New-Object 'System.Collections.Generic.List[object]'
            $errors = New-Object 'System.Collections.Generic.List[string]'
            $created = 0
            $removed = 0

            function Contains-Text($text, $needle) {
                if ([string]::IsNullOrWhiteSpace([string]$text) -or [string]::IsNullOrWhiteSpace([string]$needle)) {
                    return $false
                }
                return ([string]$text).IndexOf([string]$needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }

            function Is-ManagedForPurpose($rule, [string] $purpose, [string] $legacyMarker) {
                $description = [string]$rule.Description
                if ([string]::IsNullOrWhiteSpace($description)) { return $false }
                $hasWorld = (Contains-Text $description "WorldId=$worldId") -or
                    (Contains-Text $description "FOID=$worldId") -or
                    (Contains-Text $description $worldId)
                if (-not $hasWorld) { return $false }
                return (Contains-Text $description "Purpose=$purpose") -or
                    (Contains-Text $description $legacyMarker)
            }

            function Get-ManagedRules([string] $purpose, [string] $legacyMarker) {
                return @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue |
                    Where-Object { Is-ManagedForPurpose $_ $purpose $legacyMarker })
            }

            function Remove-ManagedRules([string] $purpose, [string] $legacyMarker) {
                foreach ($rule in @(Get-ManagedRules $purpose $legacyMarker)) {
                    Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
                    $script:removed++
                }
            }

            function Test-PortRule([string] $purpose, [string] $legacyMarker, [int] $port) {
                $rules = @(Get-ManagedRules $purpose $legacyMarker)
                if ($rules.Count -ne 1) { return $false }
                $rule = $rules[0]
                if ([string]$rule.Enabled -ne 'True') { return $false }
                if ([string]$rule.Direction -ne 'Inbound') { return $false }
                if ([string]$rule.Action -ne 'Allow') { return $false }
                if ([string]$rule.Profile -ne 'Any') { return $false }
                $pf = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                if ($null -eq $pf) { return $false }
                return [string]$pf.Protocol -eq 'UDP' -and [string]$pf.LocalPort -eq [string]$port
            }

            function Test-ProgramRule([string] $purpose, [string] $legacyMarker, [string] $programPath) {
                $rules = @(Get-ManagedRules $purpose $legacyMarker)
                if ($rules.Count -ne 1) { return $false }
                $rule = $rules[0]
                if ([string]$rule.Enabled -ne 'True') { return $false }
                if ([string]$rule.Direction -ne 'Inbound') { return $false }
                if ([string]$rule.Action -ne 'Allow') { return $false }
                if ([string]$rule.Profile -ne 'Any') { return $false }
                $af = @(Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue)[0]
                if ($null -eq $af) { return $false }
                return [string]::Equals([string]$af.Program, $programPath, [System.StringComparison]::OrdinalIgnoreCase)
            }

            function Add-Operation(
                [string] $purpose,
                [string] $displayName,
                $parameters,
                [int] $exitCode,
                [string] $stderr,
                [bool] $verifiedAfterCreate) {
                $operations.Add([ordered]@{
                    action = 'CreateFirewallRule'
                    worldId = $worldId
                    rulePurpose = $purpose
                    displayName = $displayName
                    parameters = $parameters
                    exitCode = $exitCode
                    stderr = $stderr
                    verifiedAfterCreate = $verifiedAfterCreate
                })
            }

            function Repair-PortRule(
                [string] $purpose,
                [string] $legacyMarker,
                [string] $displayName,
                [string] $description,
                [int] $port) {
                $params = [ordered]@{
                    Direction = 'Inbound'
                    Action = 'Allow'
                    Enabled = $true
                    Profile = 'Any'
                    Protocol = 'UDP'
                    LocalPort = $port
                }
                try {
                    Remove-ManagedRules $purpose $legacyMarker
                    New-NetFirewallRule `
                        -DisplayName $displayName `
                        -Group $group `
                        -Direction Inbound `
                        -Action Allow `
                        -Enabled True `
                        -Profile Any `
                        -Protocol UDP `
                        -LocalPort $port `
                        -Description $description | Out-Null
                    $script:created++
                    $verified = Test-PortRule $purpose $legacyMarker $port
                    if (-not $verified) {
                        $errors.Add("$purpose rule was created but verification failed.")
                    }
                    Add-Operation $purpose $displayName $params 0 '' $verified
                } catch {
                    $message = $_.Exception.Message
                    $errors.Add("$purpose rule failed: $message")
                    Add-Operation $purpose $displayName $params 1 $message $false
                }
            }

            function Repair-ProgramRule(
                [string] $purpose,
                [string] $legacyMarker,
                [string] $displayName,
                [string] $description,
                [string] $programPath) {
                $params = [ordered]@{
                    Direction = 'Inbound'
                    Action = 'Allow'
                    Enabled = $true
                    Profile = 'Any'
                    Program = $programPath
                }
                try {
                    if ([string]::IsNullOrWhiteSpace($programPath) -or -not (Test-Path -LiteralPath $programPath)) {
                        throw "Server executable not found: $programPath"
                    }
                    Remove-ManagedRules $purpose $legacyMarker
                    New-NetFirewallRule `
                        -DisplayName $displayName `
                        -Group $group `
                        -Direction Inbound `
                        -Action Allow `
                        -Enabled True `
                        -Profile Any `
                        -Program $programPath `
                        -Description $description | Out-Null
                    $script:created++
                    $verified = Test-ProgramRule $purpose $legacyMarker $programPath
                    if (-not $verified) {
                        $errors.Add("$purpose rule was created but verification failed.")
                    }
                    Add-Operation $purpose $displayName $params 0 '' $verified
                } catch {
                    $message = $_.Exception.Message
                    $errors.Add("$purpose rule failed: $message")
                    Add-Operation $purpose $displayName $params 1 $message $false
                }
            }

            function Write-Result([bool] $ok) {
                $rawLog = ''
                try {
                    $rawLog = (($operations | ForEach-Object {
                        ConvertTo-Json -InputObject $_ -Depth 8 -Compress
                    }) -join [Environment]::NewLine)
                } catch { }
                $payload = [ordered]@{
                    Success = $ok
                    RulesCreated = $created
                    RulesUpdated = 0
                    StaleRulesRemoved = $removed
                    Errors = @($errors)
                    RawLog = $rawLog
                }
                try {
                    $json = ConvertTo-Json -InputObject $payload -Depth 8
                    Set-Content -LiteralPath {{resultPath}} -Value $json -Encoding UTF8
                } catch { }
            }

            try {
                # The two UDP port rules never depend on the server executable, so
                # they are always created. The program rule is only attempted when
                # the executable path is known - a missing server install must NOT
                # block the port rules (that was the "no firewall rules appear" bug).
                Repair-PortRule {{gamePurpose}} {{gameLegacy}} {{gameName}} {{gameDesc}} {{gamePort}}
                Repair-PortRule {{queryPurpose}} {{queryLegacy}} {{queryName}} {{queryDesc}} {{queryPort}}

                $programPath = {{program}}
                if ($programPath -and -not [string]::IsNullOrWhiteSpace([string]$programPath)) {
                    Repair-ProgramRule {{programPurpose}} {{programLegacy}} {{programName}} {{programDesc}} $programPath
                } else {
                    Add-Operation {{programPurpose}} {{programName}} ([ordered]@{ Skipped = $true }) 0 'Server executable not installed yet; program rule skipped.' $false
                }

                $ok = ($errors.Count -eq 0)
                Write-Result $ok
                if (-not $ok) { exit 2 }
                exit 0
            } catch {
                $errors.Add("Fatal firewall repair failure: $($_.Exception.Message)")
                Write-Result $false
                exit 1
            }
            """;
    }
}
