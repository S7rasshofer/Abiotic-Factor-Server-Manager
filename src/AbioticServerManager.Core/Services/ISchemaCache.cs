namespace AbioticServerManager.Core.Services;

/// <summary>
/// Remembers every setting key the app has ever observed in live files. Lets the app tell
/// "known since forever" from "newly appeared after a game update" so unknown keys can be
/// surfaced (Advanced tab) instead of lost. Implements plan §7.5.
/// </summary>
public interface ISchemaCache
{
    Task LoadAsync(CancellationToken ct = default);

    bool HasSeen(string section, string key);

    /// <summary>Records any not-yet-seen keys and persists if the set changed.</summary>
    Task RecordAsync(IEnumerable<(string Section, string Key)> keys, CancellationToken ct = default);
}
