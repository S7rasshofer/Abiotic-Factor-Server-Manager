using System.Windows;
using System.Windows.Controls;

namespace AbioticServerManager.App.Behaviors;

/// <summary>
/// Enables two-way binding to <see cref="PasswordBox.Password"/> (which is not a
/// DependencyProperty for security reasons) via an attached <c>BoundPassword</c> property.
/// </summary>
public static class PasswordBoxBehavior
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBehavior),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached",
            typeof(bool),
            typeof(PasswordBoxBehavior),
            new PropertyMetadata(false, OnIsAttachedChanged));

    public static string GetBoundPassword(DependencyObject obj) =>
        (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value) =>
        obj.SetValue(BoundPasswordProperty, value);

    public static bool GetIsAttached(DependencyObject obj) =>
        (bool)obj.GetValue(IsAttachedProperty);

    public static void SetIsAttached(DependencyObject obj, bool value) =>
        obj.SetValue(IsAttachedProperty, value);

    private static void OnBoundPasswordChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && box.Password != (string)(e.NewValue ?? string.Empty))
        {
            box.Password = (string)(e.NewValue ?? string.Empty);
        }
    }

    private static void OnIsAttachedChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            box.PasswordChanged -= OnPasswordChanged;
        }

        if ((bool)e.NewValue)
        {
            box.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            SetBoundPassword(box, box.Password);
        }
    }
}
