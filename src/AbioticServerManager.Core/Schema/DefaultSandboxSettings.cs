using System.Reflection;

namespace AbioticServerManager.Core.Schema;

public static class DefaultSandboxSettings
{
    public const string FileName = "SandboxSettings.ini";

    private const string ResourceSuffix = ".Schema.default-sandbox-settings.ini";

    public static async Task<string> LoadTemplateAsync(CancellationToken ct = default)
    {
        var assembly = typeof(DefaultSandboxSettings).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded default SandboxSettings.ini template was not found.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded default SandboxSettings.ini template could not be opened.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
