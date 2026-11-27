using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class ColumnSurface
{
    internal override int HorizontalRevealAnchor => controller.IsFocusColumn(this) && Lane == Lane.Done ? -1 : 0;
    internal override bool SidewaysListReveal => controller.IsFocusColumn(this) && Lane == Lane.Done;
    private Button? quickAi;
    private CheckBox? completedToggle;
    private Button? pinToggle;
    private Rect lastPinToggleLocalBounds;
    private ScrollViewer? cachedListScroll;
    internal Button? PinToggleButton => pinToggle;
    private bool multiColumn;
    private void RefreshViewSettings()
    {
        if (quickAi is not null) quickAi.Visibility = Definition.BuiltinLane == Lane.Running || controller.Engine.State.Preferences.QuickAiOnAllColumns ? Visibility.Visible : Visibility.Collapsed;
        if (completedToggle is not null)
        {
            completedToggle.Visibility = controller.FocusMode ? Visibility.Visible : Visibility.Collapsed;
            completedToggle.IsChecked = controller.FocusCompletedPinned;
        }
        RefreshPinToggleAppearance();
        var next = controller.FocusMode && Definition.BuiltinLane is Lane.Running or Lane.Action;
        if (multiColumn == next) return;
        multiColumn = next;
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(next ? typeof(AdaptiveCardPanel) : typeof(VirtualizingStackPanel)));
    }
    private void AddCompletedToggle(Panel rightSlot)
    {
        if (Definition.BuiltinLane != Lane.Action) return;
        completedToggle = new CheckBox { Content = "已完成", Tag = "FocusCompletedToggle",
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed,
            ToolTip = "勾选后固定展开已完成。未勾选时，拖动待操作任务会临时展开，拖动结束或取消后自动收起。" };
        Ui.BindBrush(completedToggle, Control.ForegroundProperty, "AccentBrush", Ui.Accent);
        completedToggle.Click += (_, _) => { controller.SetFocusCompletedOpen(completedToggle.IsChecked == true); PinnedOverlayGeometryChanged?.Invoke(); };
        rightSlot.Children.Add(completedToggle);
    }
    private void AddPinToggle(Panel rightSlot)
    {
        if (Definition.BuiltinLane != Lane.Action) return;
        pinToggle = ActionPinIcon.CreateButton(false, () => controller.ToggleActionPinned());
        pinToggle.Tag = "ActionPinToggle";
        pinToggle.Margin = new Thickness(2, 0, 6, 0);
        pinToggle.VerticalAlignment = VerticalAlignment.Center;
        pinToggle.SizeChanged += (_, _) => NotifyPinToggleBoundsChanged();
        pinToggle.LayoutUpdated += (_, _) => { if (IsPinnedOverlay) NotifyPinToggleBoundsChanged(); };
        RefreshPinToggleAppearance();
        rightSlot.Children.Add(pinToggle);
    }
    private Rect CapturePinToggleLocalBounds()
    {
        if (pinToggle is null || !pinToggle.IsLoaded || pinToggle.ActualWidth < 1 || pinToggle.ActualHeight < 1)
            return Rect.Empty;
        try { return new Rect(pinToggle.TranslatePoint(new Point(0, 0), Root), new Size(pinToggle.ActualWidth, pinToggle.ActualHeight)); }
        catch (InvalidOperationException) { return Rect.Empty; }
    }
    private void NotifyPinToggleBoundsChanged()
    {
        var current = CapturePinToggleLocalBounds();
        if (current.IsEmpty) return;
        if (!lastPinToggleLocalBounds.IsEmpty
            && Math.Abs(current.Left - lastPinToggleLocalBounds.Left) <= 0.25
            && Math.Abs(current.Top - lastPinToggleLocalBounds.Top) <= 0.25
            && Math.Abs(current.Width - lastPinToggleLocalBounds.Width) <= 0.25
            && Math.Abs(current.Height - lastPinToggleLocalBounds.Height) <= 0.25)
            return;
        lastPinToggleLocalBounds = current;
        PinnedOverlayGeometryChanged?.Invoke();
    }
    internal void NotifyPinnedScrollabilityIfChanged(bool force = false)
    {
        var current = HasScrollableContent;
        if (!force && lastReportedScrollable == current) return;
        lastReportedScrollable = current;
        PinnedScrollabilityChanged?.Invoke();
    }
    private void RefreshPinToggleAppearance()
    {
        if (pinToggle is null) return;
        ActionPinIcon.ApplyState(pinToggle, IsPinnedOverlay);
    }
    internal void SetPinnedOverlay(bool pinned)
    {
        if (Definition.BuiltinLane != Lane.Action) return;
        IsPinnedOverlay = pinned;
        lastPinToggleLocalBounds = CapturePinToggleLocalBounds();
        lastReportedScrollable = null;
        RefreshPinToggleAppearance();
        SetClickThrough(pinned);
        RefreshGlass();
        PinnedOverlayGeometryChanged?.Invoke();
    }
    internal ScrollViewer? ListScrollViewer
    {
        get
        {
            if (cachedListScroll is { } cached && cached.IsDescendantOf(list)) return cached;
            return cachedListScroll = FindScroll(list);
        }
    }
    internal bool HasScrollableContent => ListScrollViewer is { } scroll && scroll.ScrollableHeight > 0.5;
    internal bool ScrollByWheelDelta(int wheelDelta)
    {
        var scroll = ListScrollViewer;
        if (scroll is null || scroll.ScrollableHeight <= 0.5) return false;
        return QuickScroll.ScrollByWheelDelta(scroll, wheelDelta, requireVisible: !controller.OffscreenNoShow);
    }
    internal Rect PinToggleBoundsInScreenDip()
    {
        const double fallbackSize = ActionPinIcon.ButtonDiameter;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var scale = hwnd != IntPtr.Zero ? Math.Max(1.0, Native.GetDpiForWindow(hwnd) / 96.0) : 1.0;
        if (pinToggle is not null && pinToggle.IsLoaded && pinToggle.ActualWidth > 1 && pinToggle.ActualHeight > 1)
        {
            try
            {
                var screen = pinToggle.PointToScreen(new Point(0, 0));
                return new Rect(screen.X / scale, screen.Y / scale, pinToggle.ActualWidth, pinToggle.ActualHeight);
            }
            catch (InvalidOperationException) { }
        }
        var w = pinToggle is { ActualWidth: > 1 } ? pinToggle.ActualWidth : fallbackSize;
        var h = pinToggle is { ActualHeight: > 1 } ? pinToggle.ActualHeight : fallbackSize;
        return new Rect(Left + Math.Max(8, Width - 12 - 22 - w), Top + 8, w, h);
    }
    private System.Collections.Generic.List<WorkItem> OrderForDisplay(System.Collections.Generic.List<WorkItem> tasks)
        => Definition.BuiltinLane == Lane.Done && controller.Engine.State.Preferences.CompletedChronological
            ? tasks.OrderBy(x => x.CompletedSinceUtc ?? x.UpdatedUtc).ThenBy(x => x.Id).ToList() : tasks;
    private static System.Collections.Generic.IEnumerable<ListBoxItem> RealizedItems(DependencyObject node)
    {
        if (node is ListBoxItem item) { yield return item; yield break; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in RealizedItems(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private Guid? ReorderDropBefore(Point point)
    {
        var slots = RealizedItems(list).Select(container => (Item: list.ItemContainerGenerator.ItemFromContainer(container),
            Bounds: new Rect(container.TranslatePoint(new Point(), list), container.RenderSize)))
            .Where(x => x.Item is WorkItem || ReferenceEquals(x.Item, cardPreview)).ToArray();
        if (slots.Length == 0) return null;
        if (cardPreview is not null)
        {
            var gap = slots.FirstOrDefault(x => ReferenceEquals(x.Item, cardPreview)).Bounds;
            if (gap.Width > 0 && gap.Height > 0) { gap.Inflate(6, 6); if (gap.Contains(point)) return previewBefore; }
        }
        var rows = slots.GroupBy(x => Math.Round(x.Bounds.Top, 1)).OrderBy(x => x.Key).ToArray();
        var row = rows.FirstOrDefault(x => point.Y <= x.Max(s => s.Bounds.Bottom));
        if (row is null) return null;
        var ordered = row.OrderBy(x => x.Bounds.Left).ToArray();
        var last = ordered[^1];
        if (multiColumn && point.X > last.Bounds.Right + 2)
            return LogicalAfter(last.Item);
        var selected = multiColumn
            ? ordered.OrderBy(x => Math.Abs(point.X - (x.Bounds.Left + x.Bounds.Width / 2))).First()
            : ordered[0];
        if (ReferenceEquals(selected.Item, cardPreview)) return previewBefore;
        if (selected.Item is not WorkItem task) return null;
        // Moving the empty slot past an occupied slot shifts that neighbour
        // into the old gap. Only presentation changes until the actual drop.
        var gapIndex = cardPreview is null ? -1 : items.IndexOf(cardPreview);
        if (gapIndex >= 0 && items.IndexOf(task) > gapIndex) return LogicalAfter(task);
        if (gapIndex < 0 && !multiColumn && point.Y > selected.Bounds.Top + selected.Bounds.Height / 2) return LogicalAfter(task);
        return task.Id;
    }
    private Guid? LogicalAfter(object item) => items.Skip(items.IndexOf(item) + 1).OfType<WorkItem>().FirstOrDefault()?.Id;
}
