using System;
using System.Linq;
using System.Windows;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    internal bool FocusMode => Engine.State.Preferences.RunningFocusMode;
    internal bool FocusCompletedOpen { get; private set; }
    internal bool FocusCompletedPinned { get; private set; }
    private bool focusCompletedTransient;
    private System.Windows.Threading.DispatcherTimer? focusCompletedClose;
    private Rect focusRange = Rect.Empty;
    private Rect focusCompletedSlot = Rect.Empty;
    private Rect focusCompletedResizeStart = Rect.Empty;
    private bool placingFocus;
    internal bool ColumnIsVisible(ColumnSurface column) => !column.Definition.Deleted && (FocusMode
        ? column.Definition.BuiltinLane is Lane.Running or Lane.Action || column.Definition.BuiltinLane == Lane.Done && FocusCompletedOpen
        : column.Definition.Visible);
    internal bool IsFocusColumn(ColumnSurface column) => FocusMode && column.Definition.BuiltinLane is Lane.Running or Lane.Action or Lane.Done;
    internal void SetFocusMode(bool enabled)
    {
        if (FocusMode == enabled) return;
        CancelAutomaticMoves();
        if (enabled)
        {
            var positions = ConfiguredColumns.Select(x => x.Position()).Where(FinitePosition).ToArray();
            focusRange = positions.Length == 0 ? Rect.Empty : Bounds(positions);
        }
        Engine.State.Preferences.RunningFocusMode = enabled;
        focusCompletedClose?.Stop();
        FocusCompletedPinned = FocusCompletedOpen = focusCompletedTransient = false;
        RestorePositions();
        foreach (var column in Columns)
        {
            column.ApplySettings(); column.Refresh();
            column.Reveal(Expanded && ColumnIsVisible(column), false);
        }
        Engine.Touch();
    }
    internal void SetFocusCompletedOpen(bool open)
    {
        if (!FocusMode) return;
        focusCompletedClose?.Stop();
        FocusCompletedPinned = open;
        focusCompletedTransient = false;
        RefreshFocusCompletedVisibility();
    }
    internal void BeginFocusCompletedDrag()
    {
        if (!FocusMode) return;
        focusCompletedClose?.Stop(); focusCompletedTransient = true;
        RefreshFocusCompletedVisibility();
    }
    internal void EndFocusCompletedDrag()
    {
        if (exiting || !FocusMode || FocusCompletedPinned || !focusCompletedTransient) return;
        focusCompletedClose ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        focusCompletedClose.Stop(); focusCompletedClose.Tick -= CloseTemporaryCompleted;
        focusCompletedClose.Tick += CloseTemporaryCompleted; focusCompletedClose.Start();
    }
    private void CloseTemporaryCompleted(object? sender, EventArgs e)
    { focusCompletedClose?.Stop(); focusCompletedTransient = false; RefreshFocusCompletedVisibility(); }
    private void CancelTemporaryCompleted()
    { focusCompletedClose?.Stop(); focusCompletedTransient = false; RefreshFocusCompletedVisibility(); }
    private void RefreshFocusCompletedVisibility()
    {
        if (!FocusMode) return;
        var open = FocusCompletedPinned || focusCompletedTransient;
        if (FocusCompletedOpen == open) { foreach (var column in Columns) column.ApplySettings(); return; }
        FocusCompletedOpen = open;
        var done = Columns.Single(x => x.Definition.BuiltinLane == Lane.Done);
        if (open) PlaceCompletedDrawer(done);
        done.Reveal(Expanded && open, Expanded);
        foreach (var column in Columns) column.ApplySettings();
    }
    private void ApplyFocusPositions()
    {
        if (!FocusMode) return;
        var area = WorkArea(Ball);
        if (Smoke) area = new Rect(-10000, -10000, area.Width, area.Height);
        if (focusRange.IsEmpty)
        {
            var normal = ConfiguredColumns.Select(x => x.Position()).Where(FinitePosition).ToArray();
            focusRange = normal.Length == 0 ? new Rect(area.Left + area.Width / 16, area.Top + 30, area.Width * 7 / 8, Math.Max(260, area.Height - 60)) : Bounds(normal);
        }
        var width = Math.Min(focusRange.Width, area.Width);
        var height = Math.Min(focusRange.Height, area.Height);
        var left = Math.Clamp(focusRange.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(focusRange.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        var gap = Math.Min(24, width / 40);
        var completedWidth = Math.Min(260, Math.Max(160, width * .19));
        var panelWidth = Math.Max(80, (width - completedWidth - 2 * gap) / 2);
        focusCompletedSlot = new Rect(left + 2 * (panelWidth + gap), top, completedWidth, Math.Max(260, height));
        placingFocus = true;
        try
        {
            foreach (var column in Columns.Where(x => IsFocusColumn(x) && x.Definition.BuiltinLane != Lane.Done))
            {
                var role = column.Definition.BuiltinLane;
                var position = new WindowPosition { X = left + (role == Lane.Running ? 0 : panelWidth + gap),
                    Y = top, Width = Math.Max(80, panelWidth), Height = Math.Max(260, height) };
                if (Engine.State.Preferences.Positions.TryGetValue(FocusPositionKey(column), out var saved))
                    position = Copies.Of(saved);
                column.Place(position); if (!Smoke) Clamp(column); column.ApplySettings();
            }
        }
        finally { placingFocus = false; }
        if (FocusCompletedOpen) PlaceCompletedDrawer(Columns.Single(x => x.Definition.BuiltinLane == Lane.Done));
    }
    private void PlaceCompletedDrawer(ColumnSurface done)
    {
        if (focusCompletedSlot.IsEmpty) ApplyFocusPositions();
        var position = new WindowPosition { X = focusCompletedSlot.Left, Y = focusCompletedSlot.Top,
            Width = focusCompletedSlot.Width, Height = focusCompletedSlot.Height };
        if (Engine.State.Preferences.Positions.TryGetValue(FocusPositionKey(done), out var saved)) position = Copies.Of(saved);
        placingFocus = true;
        try { done.Place(position); if (!Smoke) Clamp(done); focusCompletedSlot = new Rect(done.Left, done.Top, done.Width, done.Height); }
        finally { placingFocus = false; }
    }
    private string FocusPositionKey(ColumnSurface column) => (column.Definition.BuiltinLane == Lane.Done ? "focus:completed:" : "focus:wide:") + column.Definition.PositionKey;
    private static bool FinitePosition(WindowPosition value) => double.IsFinite(value.X) && double.IsFinite(value.Y) &&
        double.IsFinite(value.Width) && double.IsFinite(value.Height) && value.Width > 0 && value.Height > 0;
}
