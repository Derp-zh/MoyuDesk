using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BallSurface
{
    private readonly DispatcherTimer edgeIdle = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Ellipse edgeDot = new()
    {
        Width = 6, Height = 6, IsHitTestVisible = false, RenderTransform = new TranslateTransform(0, -10),
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed, Tag = "BallEdgeDot"
    };
    private DateTime edgeLastUse = DateTime.UtcNow;
    private bool edgeMenuOpen, edgeRevealing;
    private double edgeVisibleLeft;
    private Rect edgeDesktop;
    private bool dockLeft, expandLeft = true, placementKnown, placementPending, placingBall;
    internal bool ExpandsLeft => expandLeft;
    internal bool DockedLeft => IsEdgeDocked && dockLeft;
    internal double ExpandedWidth => Math.Max(58, Math.Min(370, AvailableArea().Width));
    internal Rect OrbScreenBounds
    {
        get
        {
            new WindowInteropHelper(this).EnsureHandle();
            var bounds = Native.ScreenBounds(this); var scale = bounds.Height / Math.Max(1, Height);
            return new Rect(expandLeft ? bounds.Right - 58 * scale : bounds.Left, bounds.Top, 58 * scale, bounds.Height);
        }
    }
    private EventHandler? edgeFrame;
    private readonly Stopwatch edgeClock = new();
    internal bool IsEdgeRestoring => edgeFrame is not null && edgeRevealing;
    internal bool IsEdgeAnimating => edgeFrame is not null;
    internal Rect EdgeVisibleClip => new(Math.Clamp(edgeDesktop.Left - Left, 0, Width), 0,
        Math.Max(0, Math.Clamp(edgeDesktop.Right - Left, 0, Width) - Math.Clamp(edgeDesktop.Left - Left, 0, Width)), Height);
    internal bool IsEdgeHidden { get; private set; }
    internal bool EdgeHiddenOnLeft => dockLeft;
    internal Ellipse EdgeDot => edgeDot;

    private void InitializeEdgeAutoHide()
    {
        edgeDot.Fill = countBubble.BorderBrush;
        Root.Children.Add(edgeDot);
        edgeIdle.Tick += (_, _) => AdvanceEdgeIdle(DateTime.UtcNow);
        Root.MouseEnter += (_, _) => { RestoreFromEdge(animate: true); ResetEdgeIdle(); };
        Root.MouseLeave += (_, _) => ResetEdgeIdle();
        IsVisibleChanged += (_, _) => RefreshEdgeAutoHide();
        DpiChanged += (_, _) => PreparePlacement();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => { edgeIdle.Stop(); StopEdgeMotion(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
    }
    internal bool IsEdgeIdleRunning => edgeIdle.IsEnabled;
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() => { if (IsBallEnabled) PreparePlacement(); }));
    internal void ResetEdgeIdle() => edgeLastUse = DateTime.UtcNow;
    internal void SetEdgeMenuOpen(bool open)
    {
        edgeMenuOpen = open;
        if (!IsBallEnabled) { edgeIdle.Stop(); StopEdgeMotion(); IsEdgeHidden = false; return; }
        RestoreFromEdge();
        ResetEdgeIdle();
    }
    internal void RefreshEdgeAutoHide()
    {
        edgeIdle.Stop();
        if (!IsBallEnabled)
        {
            StopEdgeMotion();
            IsEdgeHidden = false;
            return;
        }
        RestoreFromEdge(); PreparePlacement();
        if (IsEdgeDocked && IsVisible) edgeIdle.Start();
        ResetEdgeIdle();
    }
    // Always persist the visible position; hidden/animated coordinates are temporary.
    internal double VisibleBallLeft => IsEdgeHidden ? edgeVisibleLeft : Left + (expandLeft ? Width - 58 : 0);
    internal void PreparePlacement()
    {
        if (placingBall || rightDown) return;
        if (HasActiveMotion || IsEdgeAnimating) { placementPending = true; return; }
        placingBall = true;
        try
        {
            RestoreFromEdge();
            var orbLeft = VisibleBallLeft; var area = AvailableArea();
            var orbBounds = new Rect(orbLeft, Top, 58, Height);
            if (IsEdgeDocked)
            {
                dockLeft = BallPlacement.NearestLeft(orbBounds, area, placementKnown ? dockLeft : null);
                expandLeft = !dockLeft; orbLeft = dockLeft ? area.Left : area.Right - 58;
            }
            else expandLeft = BallPlacement.ExpandLeft(orbBounds, area, placementKnown ? expandLeft : null);
            placementKnown = true; placementPending = false; edgeDesktop = area;
            Left = orbLeft - (expandLeft ? Width - 58 : 0);
            Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            edgeVisibleLeft = orbLeft;
            if (!HasActiveMotion) CurrentCapsuleWidth = Math.Clamp(Width, 58, ExpandedWidth);
            ApplyPlacement(); ApplyDockShape(); UpdateGlassGeometry(); Native.ClipWindow(this, Height);
        }
        finally { placingBall = false; }
    }
    internal void AdvanceEdgeIdle(DateTime now)
    {
        if (!IsBallEnabled) { edgeIdle.Stop(); StopEdgeMotion(); IsEdgeHidden = false; return; }
        if (!IsEdgeDocked || !IsVisible) { edgeIdle.Stop(); RestoreFromEdge(); return; }
        if (controller.Expanded || HasActiveMotion || IsEdgeAnimating || rightDown || dragging || edgeMenuOpen || preview.IsVisible || results.IsVisible)
        { edgeLastUse = now; return; }
        var pointer = System.Windows.Forms.Cursor.Position;
        var bounds = Native.ScreenBounds(this);
        var scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        var local = new Point((pointer.X - bounds.Left) / scale, (pointer.Y - bounds.Top) / scale);
        if (ContainsOrbPoint(local) && (!IsEdgeHidden || EdgeVisibleClip.Contains(local)))
        { RestoreFromEdge(animate: true); edgeLastUse = now; return; }
        if (!IsEdgeHidden && now - edgeLastUse >= TimeSpan.FromSeconds(2)) HideAtEdge(AvailableArea());
    }
    private Rect AvailableArea()
    {
        if (controller.Smoke) return new Rect(-12000, -12000, 4000, 4000);
        var screenPoint = OrbScreenBounds;
        var bounds = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)(screenPoint.Left + screenPoint.Width / 2), (int)(screenPoint.Top + screenPoint.Height / 2))).WorkingArea;
        // The actual WPF tree lives in our child HWND. FromVisual(window) can
        // be null, so an identity fallback incorrectly treats pixels as DIP.
        var scale = 96.0 / Math.Max(96, Native.GetDpiForWindow(new WindowInteropHelper(this).Handle));
        return new Rect(bounds.Left * scale, bounds.Top * scale, bounds.Width * scale, bounds.Height * scale);
    }
    internal void HideAtEdge(Rect desktop, bool animate = true)
    {
        if (!IsBallEnabled || !IsEdgeDocked || IsEdgeHidden || controller.Expanded || HasActiveMotion || Width > 58) return;
        edgeDesktop = desktop; edgeVisibleLeft = dockLeft ? desktop.Left : desktop.Right - Width;
        Top = Math.Clamp(Top, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - Height));
        IsEdgeHidden = true; HidePopups(); UpdateBadge();
        var target = dockLeft ? desktop.Left - Width * .75 : desktop.Right - Width * .25;
        if (animate && IsVisible) StartEdgeMotion(target, revealing: false);
        else { Left = target; edgeDot.Opacity = 1; Native.ClipWindow(this, Height); }
    }
    internal void RestoreFromEdge(bool animate = false)
    {
        if (!IsEdgeHidden) return;
        if (animate && IsVisible && IsBallEnabled)
        {
            if (!IsEdgeRestoring) StartEdgeMotion(edgeVisibleLeft, revealing: true);
            return;
        }
        StopEdgeMotion(); IsEdgeHidden = false; Left = edgeVisibleLeft;
        Native.ClipWindow(this, Height); UpdateBadge();
    }
    private void StartEdgeMotion(double target, bool revealing)
    {
        StopEdgeMotion();
        if (!IsBallEnabled || !IsVisible) { IsEdgeHidden = false; return; }
        edgeRevealing = revealing;
        var from = Left; var dotFrom = edgeDot.Opacity;
        if (!revealing) { dotFrom = 0; edgeDot.Opacity = 0; }
        ApplyEdgeBadgeFrame(0, revealing, dotFrom);
        edgeClock.Restart();
        edgeFrame = (_, _) =>
        {
            if (!IsBallEnabled || !IsVisible) { StopEdgeMotion(); IsEdgeHidden = false; return; }
            var fraction = Math.Clamp(edgeClock.Elapsed.TotalMilliseconds / 100, 0, 1);
            Left = from + (target - from) * (1 - Math.Pow(1 - fraction, 3));
            ApplyEdgeBadgeFrame(fraction, revealing, dotFrom);
            Native.ClipWindow(this, Height);
            if (fraction < 1) return;
            StopEdgeMotion();
            if (revealing) { IsEdgeHidden = false; Native.ClipWindow(this, Height); UpdateBadge(); }
        };
        CompositionTarget.Rendering += edgeFrame;
    }
    private void StopEdgeMotion()
    {
        if (edgeFrame is not null) CompositionTarget.Rendering -= edgeFrame;
        edgeFrame = null; edgeClock.Stop();
    }
    private void UpdateEdgeBadge(int count)
    {
          // Keep the number measured while hidden. Switching Collapsed back to
          // Visible at the end used to defer its layout beyond the dot's frame.
          countBubble.Opacity = IsEdgeHidden && !(IsEdgeRestoring && edgeDot.Opacity <= 0) ? 0 : 1;
        edgeDot.Visibility = IsEdgeHidden && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsEdgeAnimating) edgeDot.Opacity = 1;
    }
      internal void ApplyEdgeBadgeFrame(double fraction, bool revealing, double dotFrom = 1)
      {
          fraction = Math.Clamp(fraction, 0, 1);
          edgeDot.Opacity = revealing ? dotFrom * (1 - fraction) : fraction;
          countBubble.Opacity = revealing && (fraction >= 1 || dotFrom <= 0) ? 1 : 0;
      }
}
