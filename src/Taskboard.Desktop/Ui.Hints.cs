using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Taskboard.Desktop;

// Shared short-lived operation hints; separate from hover tips and notifications.
internal static partial class Ui
{
    private static ToolTip? activeHint;
    internal static ToolTip? ActiveHint => activeHint;
    internal static int HintCount { get; private set; }
    internal static string? LastHint { get; private set; }
    internal static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(4);
    internal static ControlTemplate HintTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(ToolTip)) { VisualTree = presenter };
    }
    internal static void Hint(FrameworkElement anchor, string message, bool show = true, FrameworkElement? stableRoot = null)
    {
        HintCount++; LastHint = message;
        if (!show) return;
        var bubble = CreateHint(anchor, message, stableRoot);
        if (bubble is not null) OpenHint(bubble);
    }
    internal static ToolTip? CreateHint(FrameworkElement anchor, string message, FrameworkElement? stableRoot = null, Point? screenPosition = null)
    {
        // Our controls belong to a child HwndSource, not a WPF Window.
        // Window.GetWindow(anchor) is null; WindowInteropHelper(null) throws
        // and the dispatcher replaces this hint with an error window.
        if (PresentationSource.FromVisual(anchor) is not System.Windows.Interop.HwndSource source || source.Handle == IntPtr.Zero) return null;
        var target = stableRoot ?? anchor;
        if (!ReferenceEquals(PresentationSource.FromVisual(target), source)) return null;
        var area = System.Windows.Forms.Screen.FromHandle(source.Handle).WorkingArea;
        var position = screenPosition ?? new Point(area.Left + area.Width / 2.0, area.Top + area.Height * .62);
        var center = target.PointFromScreen(position);
        return new ToolTip { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = (size, _, _) => [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(center.X - size.Width / 2, center.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None)],
            Template = HintTemplate(), HasDropShadow = false, Tag = position,
            StaysOpen = true, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Content = new Border { CornerRadius = new CornerRadius(12), Background = HintBackground, BorderBrush = Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 8, 11, 8), Child = new TextBlock { Text = message, Foreground = Text, FontSize = 12 } } };
    }
    internal static void OpenHint(ToolTip bubble)
    {
        if (activeHint is not null) activeHint.IsOpen = false;
        var target = (FrameworkElement)bubble.PlacementTarget;
        // System tooltip fade otherwise adds time after the requested timeout.
        bubble.Opened += (_, _) =>
        {
            if (LogicalTreeHelper.GetParent(bubble) is System.Windows.Controls.Primitives.Popup popup) popup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
            if (bubble.Tag is Point position) HintScreenPin.Attach(bubble, position);
        };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = HintDuration };
        RoutedEventHandler unloaded = (_, _) => bubble.IsOpen = false;
        DependencyPropertyChangedEventHandler visible = (_, _) => { if (!target.IsVisible) bubble.IsOpen = false; };
        target.Unloaded += unloaded; target.IsVisibleChanged += visible;
        bubble.Closed += (_, _) =>
        {
            timer.Stop(); target.Unloaded -= unloaded; target.IsVisibleChanged -= visible;
            if (ReferenceEquals(activeHint, bubble)) activeHint = null;
        };
        timer.Tick += (_, _) => bubble.IsOpen = false;
        activeHint = bubble; bubble.IsOpen = true; timer.Start();
    }
}
