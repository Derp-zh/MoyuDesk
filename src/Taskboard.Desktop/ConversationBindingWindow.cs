using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class ConversationBindingWindow(BoardController controller)
{
    private BindingSurface? surface;
    private SurfaceWindow? owner;
    private StageConversationPicker? current, retiring;
    private bool transient;
    private long openedAt;
    private long sequence;
    internal sealed record ViewSnapshot(StageConversationPicker Picker, SurfaceWindow Owner, string Search, string? SelectedId, double Offset,
        AiPlatform Platform, CodexThread[] Catalog);
    internal sealed record DragState(long Token, object DataState, ViewSnapshot? Previous);
    internal StageConversationPicker? Current => current;
    internal bool DragMode { get; private set; }
    internal SurfaceWindow? Surface => surface;
    internal static (Rect Bounds, bool Left) Placement(Rect anchor, Rect area, bool drag)
    {
        var width = Math.Min(268, area.Width); var height = Math.Min(560, area.Height);
        var left = anchor.Right + 8 + width > area.Right;
        var x = left ? anchor.Left - width - 8 : anchor.Right + 8;
        return (new Rect(Math.Clamp(x, area.Left, area.Right - width),
            Math.Clamp(anchor.Top, area.Top, area.Bottom - height), width, height), left);
    }
    internal void Toggle(StageConversationPicker picker)
    {
        if (ReferenceEquals(current, picker)) Close(picker);
        else if (picker.Editor?.Host is { } host) Open(picker, host, false);
    }
    internal void Open(StageConversationPicker picker, SurfaceWindow host, bool drag, bool owned = false,
        bool focus = true, bool preserveDrag = false, bool reload = true)
    {
        if (picker.Disposed) return;
        if (!drag && !preserveDrag) sequence++;
        if (surface is null)
        {
            surface = new BindingSurface { SuppressActivation = controller.Smoke };
            controller.RegisterAuxiliary(surface, false);
            surface.IsVisibleChanged += (_, _) => { if (!surface.IsVisible && current is null) DetachRetiring(); };
            surface.Root.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (controller.ConversationDrag is { } active) active.Dispose(); else Close();
                e.Handled = true;
            };
        }
        if (ReferenceEquals(current, picker) && ReferenceEquals(owner, host) && surface.IsVisible)
        {
            DragMode = drag; picker.SetDragMode(drag); picker.RefreshAvailability(); openedAt = Environment.TickCount64;
            Reposition();
            if (!drag && focus && !controller.Smoke) { surface.Activate(); picker.Input.Focus(); }
            return;
        }
        UnwatchOwner();
        var old = current ?? retiring;
        current = null; retiring = null;
        surface.Root.Children.Clear();
        if (old is not null) { old.WindowDetached(); if (transient && !ReferenceEquals(old, picker)) old.Dispose(); }
        retiring = null; current = picker; transient = owned; DragMode = drag; owner = host;
        openedAt = Environment.TickCount64;
        host.BeforeHide += OwnerHiding;
        host.LocationChanged += OwnerMoved; host.SizeChanged += OwnerResized; host.IsVisibleChanged += OwnerVisible;
        picker.PrepareWindow(drag, () => Close(picker));
        surface.Root.Children.Add(picker.Pane);
        Reposition(); surface.Show(); Native.Raise(surface); Native.RaiseBall();
        if (!drag && focus && !controller.Smoke) { surface.Activate(); picker.Input.Focus(); }
        if (reload) picker.LoadForWindow();
    }
    private void OwnerMoved(object? sender, EventArgs e) => Reposition();
    private void OwnerResized(object sender, SizeChangedEventArgs e) => Reposition();
    private void OwnerVisible(object sender, DependencyPropertyChangedEventArgs e) { if (owner?.IsVisible != true) Close(); }
    private void OwnerHiding() => Close();
    private void UnwatchOwner()
    {
        if (owner is null) return;
        owner.LocationChanged -= OwnerMoved; owner.SizeChanged -= OwnerResized; owner.IsVisibleChanged -= OwnerVisible;
        owner.BeforeHide -= OwnerHiding;
    }
    private void Reposition()
    {
        if (surface is null || owner is null) return;
        if (controller.Smoke) { surface.Left = -18000; surface.Top = -18000; surface.Width = 268; surface.Height = 560; return; }
        var placement = Placement(new Rect(owner.Left, owner.Top, owner.Width, owner.Height), controller.WorkArea(owner), DragMode);
        surface.OpenToLeft = placement.Left;
        surface.Width = placement.Bounds.Width;
        surface.Left = placement.Bounds.Left; surface.Top = placement.Bounds.Top;
        surface.Height = placement.Bounds.Height;
    }
    internal void Close(StageConversationPicker? picker = null, bool invalidateDrag = true)
    {
        if (picker is not null && !ReferenceEquals(current, picker)) return;
        if (current is null) return;
        if (invalidateDrag) sequence++;
        UnwatchOwner(); owner = null; retiring = current; current = null; DragMode = false;
        surface?.Hide();
        if (surface?.IsVisible != true) DetachRetiring();
    }
    internal DragState BeginDrag(bool keepOpen = false)
    {
        ViewSnapshot? previous = null;
        if (current is not null && !DragMode && owner is not null && surface?.ShouldRestore == true && current.WindowAvailable)
            previous = new(current, owner, current.Input.Text, (current.Results.SelectedItem as CodexThread)?.Id,
                FindScroll(current.Results)?.VerticalOffset ?? 0, current.SelectedPlatform, current.CatalogSnapshot);
        var state = new DragState(++sequence, controller.Engine.State, previous);
        if (keepOpen && previous is not null) { DragMode = true; current!.SetDragMode(true); }
        else Close(invalidateDrag: false);
        return state;
    }
    internal void PreserveSearchDuringDrag(DragState state, StageConversationPicker picker)
    {
        if (state.Previous is { } previous && ReferenceEquals(previous.Picker, picker)) RestoreContents(previous);
    }
    internal void FinishDrag(DragState state, bool successful)
    {
        if (state.Token != sequence) return;
        if (successful || !RestorePrevious(state)) Close(invalidateDrag: false);
    }
    internal bool RestorePrevious(DragState state)
    {
        if (state.Token != sequence || !ReferenceEquals(state.DataState, controller.Engine.State) || !controller.Expanded
            || state.Previous is not { } previous || !previous.Picker.WindowAvailable || !previous.Owner.ShouldRestore) return false;
        if (ReferenceEquals(current, previous.Picker) && !DragMode) return true;
        Open(previous.Picker, previous.Owner, false, focus: false, preserveDrag: true, reload: false);
        RestoreContents(previous);
        if (!previous.Picker.HasCatalog) previous.Picker.LoadForWindow();
        return true;
    }
    private void RestoreContents(ViewSnapshot snapshot)
    {
        snapshot.Picker.RestorePlatform(snapshot.Platform, snapshot.Catalog);
        snapshot.Picker.Input.Text = snapshot.Picker.SelectedPlatform == snapshot.Platform ? snapshot.Search : "";
        snapshot.Picker.Results.SelectedItem = snapshot.Picker.Matches.FirstOrDefault(x => x.Platform == snapshot.Platform && x.Id == snapshot.SelectedId);
        surface?.Root.UpdateLayout(); FindScroll(snapshot.Picker.Results)?.ScrollToVerticalOffset(snapshot.Offset);
    }
    private void DetachRetiring()
    {
        if (current is not null) return;
        surface?.Root.Children.Clear();
        var old = retiring; retiring = null;
        if (old is not null) { old.WindowDetached(); if (transient) old.Dispose(); }
        transient = false;
    }
    internal bool OwnsPoint(SurfaceWindow host, Point point) => ReferenceEquals(owner, host) && Covers(point);
    internal bool ContainsPoint(Point point) => Covers(point);
    internal void ObservePress(Point point, long? pressedAt)
    {
        if (current is null || pressedAt is long time && time <= openedAt || Covers(point)) return;
        try
        {
            var trigger = current.Trigger;
            if (trigger.IsVisible && new Rect(trigger.PointToScreen(new Point()),
                trigger.PointToScreen(new Point(trigger.ActualWidth, trigger.ActualHeight))).Contains(point)) return;
        }
        catch (InvalidOperationException) { }
        Close();
    }
    private bool Covers(Point point)
    {
        if (surface is null || current is null || !surface.ShouldRestore || !surface.IsVisible) return false;
        var hwnd = new WindowInteropHelper(surface).Handle;
        return GetAncestor(WindowFromPoint(new PixelPoint((int)point.X, (int)point.Y)), 2) == hwnd;
    }
    internal ConversationDropTarget? DropTarget(Point point)
    {
        if (!Covers(point)) return null;
        return DropTargetInPicker(current!, point);
    }
    internal static ConversationDropTarget DropTargetInPicker(StageConversationPicker source, Point point)
    {
        if (source.IsOverUnlink(point))
            return source.HasBinding
                ? ConversationDropTarget.Unbind("解除绑定", "松开解除当前会话绑定")
                : ConversationDropTarget.Unavailable("当前阶段尚未绑定会话，无需解除，原状态已保留");
        var local = source.Results.PointFromScreen(point);
        var hit = source.Results.InputHitTest(local) as DependencyObject;
        var row = hit is null ? null : ItemsControl.ContainerFromElement(source.Results, hit) as ListBoxItem;
        if (row?.DataContext is CodexThread thread && AiState.NormalizeSessionId(thread.Platform, thread.Id) is { } id)
            return new(id, thread.DisplayName, "已从本地会话列表取得准确来源和编号", false, "local-binding-window", Platform: thread.Platform);
        return ConversationDropTarget.Unavailable(source.DropHint);
    }
    internal void Hover(Point point)
    {
        if (!DragMode || current is null) return;
        if (!Covers(point)) { current.ResetPlatformHover(); current.SetUnlinkHover(false); return; }
        current.HoverPlatform(point, Environment.TickCount64);
        var overUnlink = current.IsOverUnlink(point);
        current.SetUnlinkHover(overUnlink && current.HasBinding);
        var target = overUnlink ? null : DropTarget(point);
        current.Results.SelectedItem = target?.CanBind == true ? current.Matches.FirstOrDefault(x => x.Platform == target.Platform && x.Id == target.ThreadId) : null;
    }
    internal bool ScrollAt(Point point, int delta)
    {
        if (!DragMode || current is null || !Covers(point)) return false;
        var scroll = FindScroll(current.Results);
        if (delta > 0) scroll?.LineUp(); else scroll?.LineDown();
        return true;
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private sealed class BindingSurface : SurfaceWindow
    {
        internal bool OpenToLeft;
        internal override int HorizontalRevealAnchor => OpenToLeft ? 1 : -1;
        internal BindingSurface() : base(268, 560) { Title = "绑定会话专属窗口"; WindowLayer = 2; }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct PixelPoint(int X, int Y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
