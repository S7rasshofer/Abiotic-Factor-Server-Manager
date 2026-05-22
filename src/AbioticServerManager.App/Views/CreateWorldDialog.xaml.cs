using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace AbioticServerManager.App.Views;

/// <summary>
/// Modal dialog shown when the user creates a world. It collects the world
/// name and the starting difficulty, mapped to Abiotic Factor's
/// <c>GameDifficulty</c> sandbox key ("1" Normal, "2" Hard, "3" Apocalyptic).
/// </summary>
public partial class CreateWorldDialog : Window
{
    // Indexed to match the ComboBox order: Normal, Hard, Apocalyptic.
    private static readonly string[] DifficultyHints =
    [
        "A balanced experience — the default for most groups.",
        "Tougher enemies and tighter survival pressure.",
        "The harshest setting. (Hardcore Mode also forces this difficulty.)",
    ];

    public CreateWorldDialog(string suggestedName)
    {
        InitializeComponent();
        NameBox.Text = suggestedName;
        NameBox.SelectAll();
        DifficultyBox.SelectedIndex = 0;
        NameBox.Focus();
    }

    /// <summary>The name the user accepted. Valid only when the dialog returns true.</summary>
    public string WorldName { get; private set; } = "";

    /// <summary>
    /// Abiotic Factor <c>GameDifficulty</c> value — "1" Normal, "2" Hard,
    /// "3" Apocalyptic. Valid only when the dialog returns true.
    /// </summary>
    public string GameDifficulty { get; private set; } = "1";

    private void OnDifficultyChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = DifficultyBox.SelectedIndex;
        if (index >= 0 && index < DifficultyHints.Length)
        {
            DifficultyHint.Text = DifficultyHints[index];
        }
    }

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(
                this,
                "Enter a name for the world.",
                "Facility Overseer — Create World",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        WorldName = name;
        GameDifficulty = (DifficultyBox.SelectedIndex + 1)
            .ToString(CultureInfo.InvariantCulture);
        DialogResult = true;
        Close();
    }
}
