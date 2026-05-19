namespace AbioticServerManager.Core.Schema;

public interface ISandboxSettingsService
{
    /// <summary>
    /// Parses the sandbox INI (creating an empty document if the file does not exist yet),
    /// merges metadata, and records discovered keys in the schema cache.
    /// </summary>
    Task<SandboxSettingsDocument> LoadAsync(string iniPath, CancellationToken ct = default);

    /// <summary>Updates a setting's value in both the descriptor and the backing INI.</summary>
    void Set(SandboxSettingsDocument document, SettingDescriptor setting, string newValue);

    /// <summary>
    /// Writes the document back atomically, preserving comments, ordering, blank lines and
    /// every key the app does not understand.
    /// </summary>
    Task SaveAsync(SandboxSettingsDocument document, CancellationToken ct = default);
}
