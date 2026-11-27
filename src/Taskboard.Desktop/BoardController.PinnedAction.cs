using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    internal sealed record PinnedWheelSnapshot(bool Active, int Left, int Top, int Right, int Bottom, bool HasScrollableContent)
    {
        internal static readonly PinnedWheelSnapshot Inactive = new(false, 0, 0, 0, 0, false);
        internal bool ShouldIntercept(int x, int y) =>
            Active && HasScrollableContent && x >= Left && x < Right && y >= Top && y < Bottom;
    }

    internal bool ActionPinned { get; private set; }
    internal bool ActionPinnedHiddenByHotkey { get; private set; }
    internal ColumnSurface? ActionColumn => Columns.FirstOrDefault(x => x.Definition.BuiltinLane == Lane.Action && !x.Definition.Deleted);
    internal ActionPinOverlayWindow? ActionPinOverlay { get; private set; }
    private ColumnSurface? subscribedPinnedColumn;
    private bool pinnedOverlayAttached;
    private volatile PinnedWheelSnapshot pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
    internal PinnedWheelSnapshot CurrentPinnedWheelSnapshot => pinnedWheelSnapshot;
    internal int SyncActionPinOverlayCount { get; private set; }

    internal bool ShouldKeepActionPinnedVisible =>
        ActionPinned && !ActionPinnedHiddenByHotkey && ActionColumn is { } col && ColumnIsVisible(col);

    internal bool TryInterceptPinnedWheel(int screenX, int screenY, int wheelDelta, Action<int>? dispatchScroll = null)
    {
        if (wheelDelta == 0) return false;
        var snapshot = pinnedWheelSnapshot;
        if (!snapshot.ShouldIntercept(screenX, screenY)) return false;
        if (dispatchScroll is not null) dispatchScroll(wheelDelta);
        else ScrollPinnedActionColumn(wheelDelta);
        return true;
    }

    internal void ScrollPinnedActionColumn(int wheelDelta)
    {
        if (exiting || !ShouldKeepActionPinnedVisible) return;
        var column = ActionColumn;
        if (column is null || !column.IsPinnedOverlay || !column.IsClickThrough) return;
        if (!OffscreenNoShow && !column.IsVisible) return;
        column.ScrollByWheelDelta(wheelDelta);
        if (column.HasScrollableContent != pinnedWheelSnapshot.HasScrollableContent)
            UpdatePinnedWheelSnapshot(column);
    }

    private void RefreshPinnedWheelScrollability()
    {
        if (exiting) return;
        UpdatePinnedWheelSnapshot(ActionColumn);
    }

    internal void UpdatePinnedWheelSnapshot(ColumnSurface? column = null)
    {
        column ??= ActionColumn;
        if (exiting || !ShouldKeepActionPinnedVisible || column is null || !column.IsPinnedOverlay || !column.IsClickThrough)
        {
            pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
            return;
        }
        if (!OffscreenNoShow && !column.IsVisible)
        {
            pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
            return;
        }
        var hwnd = new WindowInteropHelper(column).Handle;
        var bounds = hwnd != IntPtr.Zero ? Native.ScreenBounds(hwnd) : Rect.Empty;
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            var scale = hwnd != IntPtr.Zero ? Math.Max(1.0, Native.GetDpiForWindow(hwnd) / 96.0) : 1.0;
            bounds = new Rect(column.Left * scale, column.Top * scale, Math.Max(0, column.Width) * scale, Math.Max(0, column.Height) * scale);
        }
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
            return;
        }
        pinnedWheelSnapshot = new PinnedWheelSnapshot(
            true,
            (int)Math.Floor(bounds.Left),
            (int)Math.Floor(bounds.Top),
            (int)Math.Ceiling(bounds.Right),
            (int)Math.Ceiling(bounds.Bottom),
            column.HasScrollableContent);
    }

    internal void ToggleActionPinned() => SetActionPinned(!ActionPinned);

    internal void SetActionPinned(bool pinned)
    {
        if (exiting) return;
        var column = ActionColumn;
        if (column is null)
        {
            ActionPinned = false;
            ActionPinnedHiddenByHotkey = false;
            SyncActionPinOverlay();
            return;
        }
        if (pinned && !ColumnIsVisible(column))
        {
            // Built-in Action column is hidden in normal mode; do not create an orphan pin overlay.
            ActionPinned = false;
            ActionPinnedHiddenByHotkey = false;
            column.SetPinnedOverlay(false);
            SyncActionPinOverlay();
            return;
        }

        ActionPinned = pinned;
        if (pinned) ActionPinnedHiddenByHotkey = false;
        AttachPinnedColumnSubscription(column);
        column.SetPinnedOverlay(pinned);

        if (pinned)
        {
            if (!OffscreenNoShow && !column.IsVisible) column.Reveal(true, false);
        }
        else
        {
            if (!Expanded && !OffscreenNoShow && column.IsVisible) column.Hide();
        }
        SyncActionPinOverlay();
        if (!OffscreenNoShow) Native.RaiseBall();
    }

    internal void SetPinnedActionTransparency(double value)
    {
        Engine.State.Preferences.PinnedActionTransparency = Math.Clamp(value, 0, 100);
        ActionColumn?.RefreshGlass();
        Engine.Touch();
    }

    private void AttachPinnedColumnSubscription(ColumnSurface column)
    {
        if (ReferenceEquals(subscribedPinnedColumn, column)) return;
        if (subscribedPinnedColumn is not null)
        {
            subscribedPinnedColumn.PinnedOverlayGeometryChanged -= SyncActionPinOverlay;
            subscribedPinnedColumn.PinnedScrollabilityChanged -= RefreshPinnedWheelScrollability;
        }
        subscribedPinnedColumn = column;
        subscribedPinnedColumn.PinnedOverlayGeometryChanged += SyncActionPinOverlay;
        subscribedPinnedColumn.PinnedScrollabilityChanged += RefreshPinnedWheelScrollability;
    }

    internal void SyncActionPinOverlay()
    {
        if (exiting) return;
        SyncActionPinOverlayCount++;
        var column = ActionColumn;
        if (!ActionPinned || column is null || !ColumnIsVisible(column))
        {
            pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
            if (ActionPinOverlay is not null)
            {
                ActionPinOverlay.LogicalVisible = false;
                if (ActionPinOverlay.IsVisible) ActionPinOverlay.Hide();
            }
            if (pinnedOverlayAttached)
            {
                pinnedOverlayAttached = false;
                Native.SetPinnedOverlay(null);
            }
            return;
        }

        AttachPinnedColumnSubscription(column);
        if (ActionPinOverlay is null)
        {
            ActionPinOverlay = new ActionPinOverlayWindow(() => SetActionPinned(false), Smoke);
        }

        var bounds = column.PinToggleBoundsInScreenDip();
        ActionPinOverlay.SyncBounds(bounds);

        var shouldShow = ShouldKeepActionPinnedVisible && (Expanded || !ActionPinnedHiddenByHotkey);
        ActionPinOverlay.LogicalVisible = shouldShow;
        UpdatePinnedWheelSnapshot(column);
        if (OffscreenNoShow) return;

        if (shouldShow && column.IsVisible)
        {
            var wasVisible = ActionPinOverlay.IsVisible;
            if (!wasVisible) ActionPinOverlay.Show();
            if (!wasVisible || !pinnedOverlayAttached)
            {
                pinnedOverlayAttached = true;
                Native.SetPinnedOverlay(ActionPinOverlay);
            }
        }
        else
        {
            if (ActionPinOverlay.IsVisible) ActionPinOverlay.Hide();
            if (pinnedOverlayAttached)
            {
                pinnedOverlayAttached = false;
                Native.SetPinnedOverlay(null);
            }
        }
    }

    private void DisposeActionPinOverlay()
    {
        pinnedWheelSnapshot = PinnedWheelSnapshot.Inactive;
        if (subscribedPinnedColumn is not null)
        {
            subscribedPinnedColumn.PinnedOverlayGeometryChanged -= SyncActionPinOverlay;
            subscribedPinnedColumn.PinnedScrollabilityChanged -= RefreshPinnedWheelScrollability;
            subscribedPinnedColumn = null;
        }
        pinnedOverlayAttached = false;
        Native.SetPinnedOverlay(null);
        if (ActionPinOverlay is not null)
        {
            ActionPinOverlay.DestroyOverlay();
            ActionPinOverlay = null;
        }
    }

    internal sealed class ActionPinOverlayWindow : Window
    {
        private bool allowClose;
        internal Button UnpinButton { get; }
        internal bool LogicalVisible { get; set; }

        internal ActionPinOverlayWindow(Action onUnpin, bool smoke)
        {
            Title = "解除待操作列常驻";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Width = ActionPinIcon.ButtonDiameter;
            Height = ActionPinIcon.ButtonDiameter;
            if (smoke) { Left = -10000; Top = -10000; }

            UnpinButton = ActionPinIcon.CreateButton(pinned: true, onUnpin);
            UnpinButton.Tag = "PinnedActionUnpinButton";
            UnpinButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            UnpinButton.VerticalAlignment = VerticalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(UnpinButton, "点击解除待操作列常驻");
            Content = UnpinButton;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                // WS_EX_TOOLWINDOW only, strictly NO WS_EX_TRANSPARENT so this mini
                // button remains clickable above the click-through Action column.
                Native.HideFromTaskSwitcher(hwnd);
                Native.SetClickThrough(hwnd, false);
            };
            Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Hide(); } };
        }

        internal void SyncBounds(Rect bounds)
        {
            var width = Math.Max(ActionPinIcon.ButtonDiameter, bounds.Width);
            var height = Math.Max(ActionPinIcon.ButtonDiameter, bounds.Height);
            if (Math.Abs(Left - bounds.Left) > 0.25) Left = bounds.Left;
            if (Math.Abs(Top - bounds.Top) > 0.25) Top = bounds.Top;
            if (Math.Abs(Width - width) > 0.25) Width = width;
            if (Math.Abs(Height - height) > 0.25) Height = height;
        }

        internal void ApplyTheme() => ActionPinIcon.ApplyState(UnpinButton, pinned: true);

        internal void DestroyOverlay()
        {
            allowClose = true;
            Close();
        }
    }
}
