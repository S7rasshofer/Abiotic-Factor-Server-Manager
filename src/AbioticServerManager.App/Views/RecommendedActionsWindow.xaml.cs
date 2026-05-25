using System.Windows;
using AbioticServerManager.App.ViewModels;

namespace AbioticServerManager.App.Views;

/// <summary>
/// Floating non-modal window showing a single world's recommended next steps.
/// The chip on the Logs &amp; Status tab opens this so the long card no longer
/// crowds the log surface. DataContext is the world VM; commands resolve via
/// the popup's <see cref="Window.Owner"/> back to the main view model.
/// </summary>
public partial class RecommendedActionsWindow : Window
{
    public RecommendedActionsWindow(ServerInstanceViewModel world)
    {
        InitializeComponent();
        DataContext = world;
    }
}
