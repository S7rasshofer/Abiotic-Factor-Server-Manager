using System.Text.Json;
using AbioticServerManager.Core.Diagnostics;

namespace AbioticServerManager.Core.Networking;

public sealed record FirewallInspection
{
    public IReadOnlyList<FirewallRuleStatus> Roles { get; init; } = [];
    public IReadOnlyList<PortBindingStatus> Ports { get; init; } = [];
    public NetworkEnvironment Environment { get; init; } = new() { IsElevated = false };
}

/// <summary>
/// Parses the JSON emitted by <see cref="FirewallScriptBuilder.BuildInspectionScript"/>
/// and the outcome file from the elevated writer. Pure and unit-testable with
/// canned JSON so the firewall reporting can be verified without PowerShell.
/// </summary>
public static class FirewallInspectionParser
{
    private static readonly string[] ServerProcessNeedles =
        ["abioticfactorserver", "abioticfactor"];

    public static FirewallInspection ParseInspection(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new FirewallInspection();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var roles = new List<FirewallRuleStatus>();
        if (root.TryGetProperty("Roles", out var rolesEl) &&
            rolesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rolesEl.EnumerateArray())
            {
                var roleToken = Str(r, "Role");
                if (!TryRole(roleToken, out var role))
                {
                    continue;
                }

                roles.Add(new FirewallRuleStatus
                {
                    Role = role,
                    Exists = Bool(r, "Exists"),
                    IsCorrect = Bool(r, "IsCorrect"),
                    DisplayName = Str(r, "DisplayName"),
                    Problems = StrList(r, "Problems"),
                    ManualMatches = StrList(r, "ManualMatches"),
                });
            }
        }

        var env = new NetworkEnvironment { IsElevated = false };
        if (root.TryGetProperty("Environment", out var envEl) &&
            envEl.ValueKind == JsonValueKind.Object)
        {
            env = new NetworkEnvironment
            {
                IsElevated = Bool(envEl, "IsElevated"),
                NetworkProfile = Str(envEl, "NetworkProfile") is { Length: > 0 } p
                    ? p
                    : "Unknown",
                ServerProcessRunning = Bool(envEl, "ServerProcessRunning"),
                ServerProcessNames = StrList(envEl, "ServerProcessNames"),
            };
        }

        var ports = new List<PortBindingStatus>();
        if (root.TryGetProperty("Ports", out var portsEl) &&
            portsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var pEl in portsEl.EnumerateArray())
            {
                var owningProcesses = StrList(pEl, "OwningProcesses");
                var listening = Bool(pEl, "IsListening");
                ports.Add(new PortBindingStatus
                {
                    Port = Int(pEl, "Port"),
                    Role = Str(pEl, "Role"),
                    IsListening = listening,
                    OwningPids = IntList(pEl, "OwningPids"),
                    OwningProcesses = owningProcesses,
                    OwningProcessPaths = StrList(pEl, "OwningProcessPaths"),
                    ForeignOwner = listening && owningProcesses.Count > 0 &&
                        !owningProcesses.Any(IsServerProcess),
                });
            }
        }

        return new FirewallInspection { Roles = roles, Ports = ports, Environment = env };
    }

    public static FirewallApplyOutcome ParseOutcome(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new FirewallApplyOutcome
            {
                Success = false,
                Errors = ["The elevated firewall writer did not report a result."],
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new FirewallApplyOutcome
            {
                Success = Bool(root, "Success"),
                RulesCreated = Int(root, "RulesCreated"),
                RulesUpdated = Int(root, "RulesUpdated"),
                StaleRulesRemoved = Int(root, "StaleRulesRemoved"),
                Errors = StrList(root, "Errors"),
                RawLog = Str(root, "RawLog"),
            };
        }
        catch (JsonException ex)
        {
            return new FirewallApplyOutcome
            {
                Success = false,
                Errors = [$"Could not read the firewall result file: {ex.Message}"],
                RawLog = json,
            };
        }
    }

    public static bool IsServerProcess(string processName) =>
        ServerProcessNeedles.Any(n =>
            processName.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool TryRole(string token, out FirewallRuleRole role)
    {
        switch (token.ToLowerInvariant())
        {
            case "game": role = FirewallRuleRole.Game; return true;
            case "query": role = FirewallRuleRole.Query; return true;
            case "program": role = FirewallRuleRole.Program; return true;
            default: role = FirewallRuleRole.Game; return false;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
            : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) &&
        v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    // Windows PowerShell 5.1's ConvertTo-Json unwraps a single-element array to a
    // scalar, so these must tolerate both "x" and ["x"].
    private static IReadOnlyList<string> StrList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (v.ValueKind == JsonValueKind.Array)
        {
            return [.. v.EnumerateArray()
                .Select(AsString)
                .Where(s => s.Length > 0)];
        }

        var single = AsString(v);
        return single.Length > 0 ? [single] : [];
    }

    private static IReadOnlyList<int> IntList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (v.ValueKind == JsonValueKind.Array)
        {
            return [.. v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => x.TryGetInt32(out var n) ? n : 0)];
        }

        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var single)
            ? [single]
            : [];
    }

    private static string AsString(JsonElement x) =>
        x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString();
}
