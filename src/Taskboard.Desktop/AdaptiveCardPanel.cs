using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Taskboard.Desktop;

// Row-major cards with viewport-only containers. Heights are learned per row;
// a row takes its tallest card, preserving readable and predictable ordering.
internal sealed class AdaptiveCardPanel : VirtualizingPanel, IScrollInfo
{
    private readonly Dictionary<int, double> heights = new();
    private readonly Dictionary<object, double> measuredHeights = new();
    private readonly Dictionary<UIElement, int> indices = new();
    private int columns = 1;
    private double tileWidth, offset, extent, viewport, width;
    private int Count => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
    private double RowHeight(int row) => heights.TryGetValue(row, out var height) ? height : 160;
    private double RowTop(int row) { var top = 0.0; for (var i = 0; i < row; i++) top += RowHeight(i); return top; }
    protected override Size MeasureOverride(Size available)
    {
        width = double.IsFinite(available.Width) ? Math.Max(1, available.Width) : 480;
        viewport = double.IsFinite(available.Height) ? Math.Max(1, available.Height) : 600;
        var next = Math.Max(1, (int)((width + 10) / 250));
        var nextTile = width / next;
        if (next != columns || Math.Abs(nextTile - tileWidth) > .5) { heights.Clear(); measuredHeights.Clear(); }
        columns = next; tileWidth = nextTile;
        var count = Count; var rows = (count + columns - 1) / columns;
        var owner = ItemsControl.GetItemsOwner(this);
        heights.Clear();
        for (int row = 0; row < rows; row++)
        {
            var height = 1.0;
            for (int index = row * columns; index < Math.Min(count, (row + 1) * columns); index++)
                height = Math.Max(height, measuredHeights.GetValueOrDefault(ItemKey(owner!.Items[index]), 160));
            heights[row] = height;
        }
        extent = RowTop(rows); offset = Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
        var firstRow = 0; var top = 0.0;
        while (firstRow < rows && top + RowHeight(firstRow) < Math.Max(0, offset - viewport / 2)) top += RowHeight(firstRow++);
        var lastRow = firstRow;
        var bottom = top;
        while (lastRow < rows && bottom < offset + viewport * 1.5) bottom += RowHeight(lastRow++);
        var first = firstRow * columns; var last = Math.Min(count, lastRow * columns);
        // Initialize the generator before using its realized positions.
        _ = InternalChildren;
        var generator = ItemContainerGenerator;
        if (generator is null) return new Size(width, viewport);
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index < last) continue;
            indices.Remove(InternalChildren[childIndex]);
            if (generator is IRecyclingItemContainerGenerator recycling) recycling.Recycle(position, 1); else generator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
        if (last > first)
        {
            var start = generator.GeneratorPositionFromIndex(first);
            var visualIndex = start.Offset == 0 ? start.Index : start.Index + 1;
            using (generator.StartAt(start, GeneratorDirection.Forward, true))
            {
                for (var index = first; index < last; index++, visualIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var fresh);
                    if (fresh)
                    {
                        if (visualIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(visualIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    indices[child] = index;
                    child.Measure(new Size(Math.Max(1, tileWidth - 10), double.PositiveInfinity));
                    measuredHeights[ItemKey(owner!.Items[index])] = child.DesiredSize.Height;
                }
            }
            for (var row = firstRow; row < lastRow; row++)
            {
                var height = 1.0;
                foreach (UIElement child in InternalChildren)
                    if (indices.TryGetValue(child, out var index) && index / columns == row) height = Math.Max(height, child.DesiredSize.Height);
                heights[row] = height;
            }
        }
        extent = RowTop(rows);
        offset = Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
        ScrollOwner?.InvalidateScrollInfo();
        return new Size(width, viewport);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            if (indices.TryGetValue(child, out var index))
                child.Arrange(new Rect(index % columns * tileWidth + 5, RowTop(index / columns) - offset, Math.Max(1, tileWidth - 10), child.DesiredSize.Height));
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args); indices.Clear();
        if (measuredHeights.Count > Count + 512)
        {
            var live = new HashSet<object>(); var owner = ItemsControl.GetItemsOwner(this);
            if (owner is not null) foreach (var item in owner.Items) live.Add(ItemKey(item));
            foreach (var key in new List<object>(measuredHeights.Keys)) if (!live.Contains(key)) measuredHeights.Remove(key);
        }
        // Keep untouched realized containers alive across drag-placeholder
        // insertions/removals instead of destroying and re-shaping every card.
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && args.ItemUICount > 0
            && args.Position.Index >= 0 && args.Position.Index + args.ItemUICount <= InternalChildren.Count)
        {
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        }
        else if (args.Action is not (System.Collections.Specialized.NotifyCollectionChangedAction.Add
            or System.Collections.Specialized.NotifyCollectionChangedAction.Replace
            or System.Collections.Specialized.NotifyCollectionChangedAction.Remove))
        {
            ItemContainerGenerator.RemoveAll(); RemoveInternalChildRange(0, InternalChildren.Count);
        }
        InvalidateMeasure();
    }
    private static object ItemKey(object item) => item is Taskboard.Core.WorkItem task ? task.Id : item;
    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= Count) return;
        var top = RowTop(index / columns); var bottom = top + RowHeight(index / columns);
        if (top < offset) SetVerticalOffset(top); else if (bottom > offset + viewport) SetVerticalOffset(bottom - viewport);
        InvalidateMeasure(); UpdateLayout();
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual == this || !IsAncestorOf(visual)) return Rect.Empty;
        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
        if (bounds.Top < 0) SetVerticalOffset(offset + bounds.Top);
        else if (bounds.Bottom > viewport) SetVerticalOffset(offset + bounds.Bottom - viewport);
        return rectangle;
    }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => width;
    public double ExtentHeight => extent;
    public double ViewportWidth => width;
    public double ViewportHeight => viewport;
    public double HorizontalOffset => 0;
    public double VerticalOffset => offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void SetVerticalOffset(double value)
    {
        var next = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, Math.Max(0, extent - viewport));
        if (Math.Abs(next - offset) < .01) return;
        offset = next; ScrollOwner?.InvalidateScrollInfo(); InvalidateMeasure();
    }
    public void SetHorizontalOffset(double value) { }
    public void LineUp() => SetVerticalOffset(offset - 40);
    public void LineDown() => SetVerticalOffset(offset + 40);
    public void PageUp() => SetVerticalOffset(offset - viewport);
    public void PageDown() => SetVerticalOffset(offset + viewport);
    public void MouseWheelUp() => SetVerticalOffset(offset - 120);
    public void MouseWheelDown() => SetVerticalOffset(offset + 120);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
