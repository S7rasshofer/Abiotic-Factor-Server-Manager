using System.Windows;
using AbioticServerManager.Core.Services;

namespace AbioticServerManager.App.Views;

/// <summary>
/// Replacement for the old YesNo MessageBox: shows the user exactly which old
/// data root was found and which worlds would be imported, then lets them
/// choose between "Import" and "Start fresh" with equal weight. Returning
/// without a choice (Esc / close button) keeps the prompt for next launch.
/// </summary>
public partial class LegacyDataImportDialog : Window
{
    public LegacyDataImportDialog(IReadOnlyList<LegacyFinding> findings)
    {
        InitializeComponent();
        FindingsList.ItemsSource = findings.Select(FindingRow.From).ToList();
    }

    public LegacyImportChoice Choice { get; private set; } = LegacyImportChoice.Dismissed;

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        Choice = LegacyImportChoice.Import;
        DialogResult = true;
        Close();
    }

    private void OnStartFreshClicked(object sender, RoutedEventArgs e)
    {
        Choice = LegacyImportChoice.StartFresh;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// View-model row for one finding. Composes the "Worlds: A, B, C" line so
    /// XAML stays a dumb template (no converters needed for one place).
    /// </summary>
    private sealed record FindingRow(string Root, string WorldsLine)
    {
        public static FindingRow From(LegacyFinding f) => new(
            Root: f.Root,
            WorldsLine: f.WorldNames.Count == 0
                ? f.HasInstances
                    ? "Worlds: (could not preview - instances file present but unreadable)"
                    : "Worlds: (none - other config / logs / backups only)"
                : "Worlds: " + string.Join(", ", f.WorldNames));
    }
}

public enum LegacyImportChoice
{
    /// <summary>User closed the dialog without picking; ask again next launch.</summary>
    Dismissed,

    /// <summary>Copy legacy instances/settings and scrub stale paths.</summary>
    Import,

    /// <summary>Record the decline and never offer this prompt again.</summary>
    StartFresh,
}
