namespace AbioticServerManager.Core.Migration;

/// <summary>
/// The historical data roots from before the single-data-root model (master
/// plan Sec "Migration Plan"). Pure path construction so detection is testable
/// without touching the real filesystem.
/// </summary>
public static class LegacyDataLocations
{
    public const string ProductFolder = "FacilityOverseer";

    public static IReadOnlyList<string> Candidates(
        string? appDataRoaming, string? localAppData)
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(appDataRoaming))
        {
            list.Add(Path.Combine(appDataRoaming, ProductFolder));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            list.Add(Path.Combine(localAppData, ProductFolder));
        }

        list.Add(@"C:\Facility Overseer");
        list.Add(@"C:\AbioticFactorServer");

        return list;
    }
}
