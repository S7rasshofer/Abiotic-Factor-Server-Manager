using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Runtime;

public sealed class LaunchArgumentBuilder : ILaunchArgumentBuilder
{
    private const string Mask = "********";

    public IReadOnlyList<string> BuildArguments(ServerInstance instance) =>
        Build(instance, maskSecrets: false);

    public string BuildMaskedCommandLine(ServerInstance instance) =>
        string.Join(' ', Build(instance, maskSecrets: true));

    private static List<string> Build(ServerInstance instance, bool maskSecrets)
    {
        var args = new List<string>
        {
            KeyValue("SteamServerName", instance.SteamServerName),
            KeyValue("WorldSaveName", instance.WorldSaveName),
            $"-MaxServerPlayers={instance.MaxPlayers}",
            $"-PORT={instance.GamePort}",
            $"-QUERYPORT={instance.QueryPort}",
        };

        if (!string.IsNullOrEmpty(instance.ServerPassword))
        {
            args.Add(KeyValue("ServerPassword", maskSecrets ? Mask : instance.ServerPassword));
        }

        if (!string.IsNullOrEmpty(instance.AdminPassword))
        {
            args.Add(KeyValue("AdminPassword", maskSecrets ? Mask : instance.AdminPassword));
        }

        var sandboxIniPath = ResolveIniArg(instance.InstallPath, instance.SandboxIniPath);
        if (!string.IsNullOrWhiteSpace(sandboxIniPath))
        {
            args.Add(KeyValue("SandboxIniPath", sandboxIniPath));
        }

        var adminIniPath = ResolveIniArg(instance.InstallPath, instance.AdminIniPath);
        if (!string.IsNullOrWhiteSpace(adminIniPath))
        {
            args.Add(KeyValue("AdminIniPath", adminIniPath));
        }

        if (!string.IsNullOrWhiteSpace(instance.MultiHomeAddress))
        {
            args.Add(KeyValue("MultiHome", instance.MultiHomeAddress));
        }

        if (instance.LanOnly)
        {
            args.Add("-LANOnly");
        }

        if (instance.UseLocalIps)
        {
            args.Add("-UseLocalIPs");
        }

        var platformArg = instance.PlatformAccessMode switch
        {
            PlatformAccessMode.PcOnly => "-PlatformLimited=PC",
            PlatformAccessMode.PlaystationOnly => "-PlatformLimited=Playstation",
            PlatformAccessMode.XboxOnly => "-PlatformLimited=Xbox",
            _ => null,
        };

        if (platformArg is not null)
        {
            args.Add(platformArg);
        }

        foreach (var extra in instance.AdditionalLaunchArguments)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                args.Add(extra.Trim());
            }
        }

        return args;
    }

    private static string KeyValue(string key, string value)
    {
        var needsQuotes = value.Length == 0 ||
                          value.Any(c => c is ' ' or '\t' or '"');
        var safe = value.Replace("\"", "\\\"");
        return needsQuotes ? $"-{key}=\"{safe}\"" : $"-{key}={value}";
    }

    /// <summary>
    /// §2.1: per-world INIs may live under <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/config/</c>
    /// (outside the server install). When the path can be expressed as relative to
    /// <c>&lt;install&gt;/AbioticFactor/Saved/</c> we keep that historical form;
    /// otherwise we emit the absolute path so an out-of-install config is reachable.
    /// </summary>
    private static string? ResolveIniArg(string installPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return NormalizeSeparators(path);
        }

        if (!string.IsNullOrWhiteSpace(installPath))
        {
            var savedRoot = Path.Combine(installPath, "AbioticFactor", "Saved");
            var relative = Path.GetRelativePath(savedRoot, path);
            if (IsRelativeSubPath(relative))
            {
                return NormalizeSeparators(relative);
            }
        }

        // Outside the install: emit the absolute path (Unreal accepts both forms).
        return path;
    }

    private static bool IsRelativeSubPath(string path) =>
        !Path.IsPathFullyQualified(path) &&
        path != ".." &&
        !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !path.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', '/');
}
