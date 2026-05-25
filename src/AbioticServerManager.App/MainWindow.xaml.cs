using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AbioticServerManager.App.ViewModels;

namespace AbioticServerManager.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// The vertical tab rail condenses to icons when the user clicks into a
    /// tab's content (more room to work), and expands when they click a rail
    /// tab to navigate (so the labels are readable).
    /// </summary>
    private void OnVerticalRailMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TabControl rail &&
            rail.DataContext is ServerInstanceViewModel world)
        {
            world.IsMainTabRailCondensed =
                !ClickedRailTabHeader(e.OriginalSource as DependencyObject, rail);
        }
    }

    // True when the click landed on one of the rail's own tab headers (as
    // opposed to tab content, including any nested sub-tabs).
    private static bool ClickedRailTabHeader(DependencyObject? source, TabControl rail)
    {
        while (source is not null && source != rail)
        {
            if (source is TabItem tab &&
                ItemsControl.ItemsControlFromItemContainer(tab) == rail)
            {
                return true;
            }

            source = WalkUp(source);
        }

        return false;
    }

    // Inline text elements (Run, Bold, Hyperlink, Span) are FlowDocument
    // content - TextElement, NOT Visual. Calling VisualTreeHelper.GetParent
    // on them throws "X is not a Visual or Visual3D", which is exactly the
    // crash a user sees when they double-click a player's name (the
    // OriginalSource of the tunnelling PreviewMouseLeftButtonDown is the
    // Run that holds DisplayName). Walk the logical tree for those, and
    // the visual tree for everything else - the host TextBlock is a Visual,
    // so the traversal resumes normally one hop up.
    private static DependencyObject? WalkUp(DependencyObject source) =>
        source is Visual
            ? VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
}
