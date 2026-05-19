using System.Windows;
using System.Windows.Controls;

namespace AbioticServerManager.App;

public partial class PlaceholderPanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlaceholderPanel),
            new PropertyMetadata(""));

    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(nameof(Note), typeof(string), typeof(PlaceholderPanel),
            new PropertyMetadata(""));

    public PlaceholderPanel() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Note
    {
        get => (string)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
}
