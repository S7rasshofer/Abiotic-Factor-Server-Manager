using AbioticServerManager.Core.Config;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Services;

namespace AbioticServerManager.Infrastructure.Persistence;

public sealed class SandboxSettingsService : ISandboxSettingsService
{
    private readonly ISettingMetadataCatalog _catalog;
    private readonly ISchemaCache _schemaCache;

    public SandboxSettingsService(ISettingMetadataCatalog catalog, ISchemaCache schemaCache)
    {
        _catalog = catalog;
        _schemaCache = schemaCache;
    }

    public async Task<SandboxSettingsDocument> LoadAsync(
        string iniPath,
        CancellationToken ct = default)
    {
        var text = File.Exists(iniPath)
            ? await File.ReadAllTextAsync(iniPath, ct).ConfigureAwait(false)
            : string.Empty;

        var ini = IniDocument.Parse(text);

        // Walk lines in order so each setting inherits the most recent banner-comment
        // grouping (e.g. "; === WORLD ===") the live file uses. A new [Section] resets it.
        var descriptors = new List<SettingDescriptor>();
        string? currentCategory = null;
        foreach (var line in ini.Lines)
        {
            switch (line)
            {
                case IniSectionLine:
                    currentCategory = null;
                    break;

                case IniCommentLine comment
                    when SandboxCategoryHeading.TryParse(comment.Raw, out var category):
                    currentCategory = category;
                    break;

                case IniKeyValueLine kv:
                    descriptors.Add(new SettingDescriptor
                    {
                        Section = kv.Section,
                        Key = kv.Key,
                        Value = kv.Value,
                        Metadata = _catalog.Find(kv.Section, kv.Key),
                        CategoryHint = currentCategory,
                    });
                    break;
            }
        }

        await _schemaCache.LoadAsync(ct).ConfigureAwait(false);
        await _schemaCache
            .RecordAsync(descriptors.Select(d => (d.Section, d.Key)), ct)
            .ConfigureAwait(false);

        return new SandboxSettingsDocument
        {
            FilePath = iniPath,
            Ini = ini,
            Settings = descriptors,
        };
    }

    public void Set(SandboxSettingsDocument document, SettingDescriptor setting, string newValue)
    {
        setting.Value = newValue;
        document.Ini.SetValue(setting.Section, setting.Key, newValue);
    }

    public async Task SaveAsync(SandboxSettingsDocument document, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = document.FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, document.Ini.ToText(), ct).ConfigureAwait(false);

        if (File.Exists(document.FilePath))
        {
            File.Replace(temp, document.FilePath, null);
        }
        else
        {
            File.Move(temp, document.FilePath);
        }
    }
}
