using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbioticServerManager.Core.Schema;

/// <summary>
/// Loads the built-in metadata embedded in this assembly, then layers an optional
/// user-supplied JSON file on top (same shape). The override lets a future game update be
/// described without a new app build — "Do not hardcode the facility. Discover it."
/// </summary>
public sealed class SettingMetadataCatalog : ISettingMetadataCatalog
{
    private static readonly string[] CategoryOrder =
        ["World", "Player", "Enemy", "Survival", "Items", "Advanced"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<string, SettingMetadata> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SettingMetadata> _byKeyOnly = new(StringComparer.OrdinalIgnoreCase);

    public SettingMetadataCatalog(string? overrideFilePath = null)
    {
        Merge(ReadEmbedded());

        if (!string.IsNullOrWhiteSpace(overrideFilePath) && File.Exists(overrideFilePath))
        {
            try
            {
                Merge(File.ReadAllText(overrideFilePath));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A broken override must never take down the catalog; built-in wins.
            }
        }

        Categories = [.. _byKey.Values
            .Select(m => m.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => Array.FindIndex(CategoryOrder, x =>
                string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0
                ? i
                : int.MaxValue)
            .ThenBy(c => c)];
    }

    public IReadOnlyList<string> Categories { get; }

    public SettingMetadata? Find(string section, string key) =>
        _byKey.GetValueOrDefault(Compose(section, key))
        ?? _byKeyOnly.GetValueOrDefault(key);

    private static string Compose(string section, string key) => $"{section}{key}";

    private static string ReadEmbedded()
    {
        var assembly = typeof(SettingMetadataCatalog).Assembly;
        var name = assembly
            .GetManifestResourceNames()
            .Single(n => n.EndsWith("setting-metadata.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Merge(string json)
    {
        var root = JsonSerializer
            .Deserialize<Dictionary<string, Dictionary<string, MetadataDto>>>(json, JsonOptions);
        if (root is null)
        {
            return;
        }

        foreach (var (section, keys) in root)
        {
            foreach (var (key, dto) in keys)
            {
                var metadata = new SettingMetadata
                {
                    Section = section,
                    Key = key,
                    DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? key : dto.DisplayName,
                    Category = string.IsNullOrWhiteSpace(dto.Category) ? "Advanced" : dto.Category,
                    Type = ParseType(dto.Type),
                    Min = dto.Min,
                    Max = dto.Max,
                    Step = dto.Step,
                    Default = dto.Default,
                    Description = dto.Description,
                    Warning = dto.Warning,
                    Options = dto.Options ?? [],
                    OptionLabels = dto.OptionLabels ?? [],
                };

                _byKey[Compose(section, key)] = metadata;
                // Key-only index: real sandbox files group the same keys under named
                // sections ([Player], [Enemy], ...) rather than the catalog's [SandboxSettings].
                _byKeyOnly[key] = metadata;
            }
        }
    }

    private static SettingValueType ParseType(string? type) => type?.ToLowerInvariant() switch
    {
        "boolean" or "bool" => SettingValueType.Boolean,
        "number" or "int" or "float" or "decimal" => SettingValueType.Number,
        "enum" => SettingValueType.Enum,
        _ => SettingValueType.Text,
    };

    private sealed class MetadataDto
    {
        public string? DisplayName { get; init; }
        public string? Category { get; init; }
        public string? Type { get; init; }
        public double? Min { get; init; }
        public double? Max { get; init; }
        public double? Step { get; init; }
        public string? Default { get; init; }
        public string? Description { get; init; }
        public string? Warning { get; init; }
        public List<string>? Options { get; init; }
        public List<string>? OptionLabels { get; init; }
    }
}
