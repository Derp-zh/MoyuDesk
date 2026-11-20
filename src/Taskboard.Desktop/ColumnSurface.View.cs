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
    private bool multiColumn;
    private void RefreshViewSettings()
    {
        if (quickAi is not null) quickAi.Visibility = Definition.BuiltinLane == Lane.Running || controller.Engine.State.Preferences.QuickAiOnAllColumns ? Visibility.Visible : Visibility.Collapsed;
        if (completedToggle is not null)
        {
            completedToggle.Visibility = controller.FocusMode ? Visibility.Visible : Visibility.Collapsed;
            completedToggle.IsChecked = controller.FocusCompletedPinned;
        }
        var next = controller.FocusMode && Definition.BuiltinLane is Lane.Running or Lane.Action;
        if (multiColumn == next) return;
        multiColumn = next;
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(next ? typeof(AdaptiveCardPanel) : typeof(VirtualizingStackPanel)));
    }
    private void AddCompletedToggle(Panel header)
    {
        if (Definition.BuiltinLane != Lane.Action) return;
        completedToggle = new CheckBox { Content = "已完成", Tag = "FocusCompletedToggle", Foreground = Ui.Accent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 24, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed,
            ToolTip = "勾选后固定展开已完成。未勾选时，拖动待操作任务会临时展开，拖动结束或取消后自动收起。" };
        completedToggle.Click += (_, _) => controller.SetFocusCompletedOpen(completedToggle.IsChecked == true);
        Grid.SetColumn(completedToggle, 1); header.Children.Add(completedToggle);
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
