using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace AbioticServerManager.App;

public partial class SandboxCategoryPanel : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable),
            typeof(SandboxCategoryPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty EmptyHintProperty =
        DependencyProperty.Register(nameof(EmptyHint), typeof(string),
            typeof(SandboxCategoryPanel),
            new PropertyMetadata("Load a sandbox file to edit settings."));

    public SandboxCategoryPanel() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string EmptyHint
    {
        get => (string)GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }
}
