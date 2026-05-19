namespace AbioticServerManager.Core.Schema;

/// <summary>
/// Looks up enrichment metadata for a setting. Implementations layer a user-supplied
/// override file on top of the built-in catalog so a future game update can be described
/// without shipping a new app build.
/// </summary>
public interface ISettingMetadataCatalog
{
    SettingMetadata? Find(string section, string key);

    /// <summary>All known categories, in a stable display order.</summary>
    IReadOnlyList<string> Categories { get; }
}
