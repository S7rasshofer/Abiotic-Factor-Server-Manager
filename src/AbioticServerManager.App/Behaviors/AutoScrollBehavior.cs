using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AbioticServerManager.App.Behaviors;

/// <summary>
/// Keeps a <see cref="ListBox"/> pinned to its newest entry. Auto-scroll pauses
/// the moment the user scrolls up to read history and resumes automatically once
/// they scroll back to the bottom.
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty StickToBottomProperty =
        DependencyProperty.RegisterAttached(
            "StickToBottom",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnStickToBottomChanged));

    // Per-ScrollViewer flag: are we currently following the tail?
    private static readonly DependencyProperty IsPinnedProperty =
        DependencyProperty.RegisterAttached(
            "IsPinned",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(true));

    public static bool GetStickToBottom(DependencyObject o) =>
        (bool)o.GetValue(StickToBottomProperty);

    public static void SetStickToBottom(DependencyObject o, bool value) =>
        o.SetValue(StickToBottomProperty, value);

    private static void OnStickToBottomChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox lb || e.NewValue is not true)
        {
            return;
        }

        lb.Loaded += static (s, _) =>
        {
            if (s is ListBox box)
            {
                Hook(box);
            }
        };

        if (lb.IsLoaded)
        {
            Hook(lb);
        }
    }

    private static void Hook(ListBox lb)
    {
        if (FindScrollViewer(lb) is not { } sv)
        {
            return;
        }

        sv.ScrollChanged -= OnScrollChanged;
        sv.ScrollChanged += OnScrollChanged;
        sv.ScrollToEnd();
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
        {
            return;
        }

        if (e.ExtentHeightChange == 0)
        {
            // A user/layout scroll (content size unchanged): re-evaluate whether
            // the view is parked at the bottom.
            var atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 1.0;
            sv.SetValue(IsPinnedProperty, atBottom);
        }
        else if ((bool)sv.GetValue(IsPinnedProperty))
        {
            // New content arrived while pinned: follow the tail.
            sv.ScrollToVerticalOffset(sv.ExtentHeight);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } sv)
            {
                return sv;
            }
        }

        return null;
    }
}
