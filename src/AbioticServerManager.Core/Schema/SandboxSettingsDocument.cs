using AbioticServerManager.Core.Config;

namespace AbioticServerManager.Core.Schema;

/// <summary>
/// A loaded sandbox settings file: the loss-less INI document plus the merged descriptors
/// the UI binds to. Editing always goes through <see cref="ISandboxSettingsService.Set"/>
/// so the underlying file keeps every comment and unknown line.
/// </summary>
public sealed class SandboxSettingsDocument
{
    public required string FilePath { get; init; }
    public required IniDocument Ini { get; init; }
    public required IReadOnlyList<SettingDescriptor> Settings { get; init; }

    public IReadOnlyList<SettingCategoryGroup> GroupByCategory(IReadOnlyList<string> categoryOrder)
    {
        int Rank(string category)
        {
            for (var i = 0; i < categoryOrder.Count; i++)
            {
                if (string.Equals(categoryOrder[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        return [.. Settings
            .GroupBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SettingCategoryGroup(g.Key, [.. g.OrderBy(s => s.DisplayName)]))
            .OrderBy(g => Rank(g.Category))
            .ThenBy(g => g.Category)];
    }
}

public sealed record SettingCategoryGroup(string Category, IReadOnlyList<SettingDescriptor> Settings);
