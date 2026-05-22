using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AbioticServerManager.App.Views;

/// <summary>
/// First-launch dialog that lets the user choose where Facility Overseer keeps
/// its data folder. The choice is persisted via <c>DataRootChoiceFile</c> and
/// honored on every subsequent launch.
/// </summary>
public partial class DataRootPickerWindow : Window
{
    private const string PortableLeaf = "FacilityOverseerData";

    public DataRootPickerWindow()
    {
        InitializeComponent();
        DataContext = this;

        PortablePath = Path.Combine(AppContext.BaseDirectory, PortableLeaf);

        // Default to the application folder when it is writable; otherwise start
        // the user on the Custom option so they are not sitting on a broken default.
        if (CanWriteTo(AppContext.BaseDirectory))
        {
            PortableRadio.IsChecked = true;
        }
        else
        {
            CustomRadio.IsChecked = true;
        }
    }

    /// <summary>The data folder beside the application (shown on the first option).</summary>
    public string PortablePath { get; }

    /// <summary>The path the user accepted, or null if they cancelled.</summary>
    public string? ChosenPath { get; private set; }

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder for Facility Overseer data",
            InitialDirectory = Directory.Exists(CustomPathBox.Text)
                ? CustomPathBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) == true)
        {
            CustomPathBox.Text = dialog.FolderName;
            CustomRadio.IsChecked = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        ChosenPath = null;
        DialogResult = false;
        Close();
    }

    private void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        var candidate = PortableRadio.IsChecked == true
            ? PortablePath
            : (CustomPathBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            MessageBox.Show(this,
                "Pick a folder before continuing.",
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "That folder cannot be created or written to:\n\n" + ex.Message +
                "\n\nPick a different folder.",
                "Facility Overseer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ChosenPath = Path.GetFullPath(candidate);
        DialogResult = true;
        Close();
    }

    private static bool CanWriteTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".fo-picker-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
