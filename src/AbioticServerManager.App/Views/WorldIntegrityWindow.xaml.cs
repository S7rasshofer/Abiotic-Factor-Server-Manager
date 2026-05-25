using System.Windows;
using AbioticServerManager.App.ViewModels;

namespace AbioticServerManager.App.Views;

/// <summary>
/// Floating non-modal window showing the §4.5 world-integrity findings for a
/// single world. The chip on the Logs &amp; Status tab opens this so the long
/// card no longer crowds the log surface.
/// </summary>
public partial class WorldIntegrityWindow : Window
{
    public WorldIntegrityWindow(ServerInstanceViewModel world)
    {
        InitializeComponent();
        DataContext = world;
    }
}
