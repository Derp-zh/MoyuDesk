using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal partial class SurfaceWindow
{
    private EventHandler? liveBallFrame;
    private bool updatingBallBounds;
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
    internal int LiveBallRevealCount { get; private set; }
    private void CancelLiveBallFrame()
    {
        if (liveBallFrame is not null) CompositionTarget.Rendering -= liveBallFrame;
        liveBallFrame = null;
    }
    private async void RevealLiveBall(bool expanded, bool extendLeft, Action prepare, Action complete)
    {
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        var generation = ++motion; var from = Width; var target = expanded ? this is BallSurface ball ? ball.ExpandedWidth : 370 : 58;
        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation();
        animating = true; LastAnimationError = null; UsesIndependentAnimation = false;
        prepare();
        new WindowInteropHelper(this).EnsureHandle();
        Root.Opacity = 1; contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible;
        UpdateGlassGeometry(); Native.ClipHorizontal(this, Width);
        if (!IsVisible) ShowImmediately();
        Native.ShowWindow(ContentHandle, 4);
        var anchor = extendLeft ? Left + Width : Left;
        await Dispatcher.InvokeAsync(() => Root.UpdateLayout(), DispatcherPriority.Render);
        if (generation != motion) return;
        if (participant is not null) participant.Start(BeginMotion); else BeginMotion();
        void BeginMotion()
        {
            if (generation != motion) return;
            MotionDisplayStartTicks = Stopwatch.GetTimestamp(); LiveBallRevealCount++;
            var clock = Stopwatch.StartNew();
            var placing = false; var finishPending = false; var completed = false;
            void Place(double width)
            {
                if (placing || generation != motion) return;
                placing = true;
                try
                {
                // Reconcile logical bounds first, then commit one native rectangle
                // rather than exposing separate move/resize notifications.
                updatingBallBounds = true;
                try { Width = width; Left = extendLeft ? anchor - width : anchor; }
                finally { updatingBallBounds = false; }
                if (generation != motion) return;
                var handle = new WindowInteropHelper(this).Handle; var scale = Native.GetDpiForWindow(handle) / 96.0;
                var leftPixel = (int)Math.Round((extendLeft ? anchor - width : anchor) * scale);
                var rightPixel = (int)Math.Round((extendLeft ? anchor : anchor + width) * scale);
                // Avoid bitblt-shifting stale pixels leftward before the child canvas and
                // native clip region match the updated width.
                Native.SetWindowPos(handle, IntPtr.Zero, leftPixel, (int)Math.Round(Top * scale),
                    rightPixel - leftPixel, (int)Math.Ceiling(Height * scale), 0x0114);
                Native.ClipHorizontal(this, width);
                contentHost.SynchronizeCanvas();
                if (generation != motion) return;
                contentHost.Height = Height; UpdateLayout();
                if (generation != motion) return;
                Root.Measure(new Size(370, Height)); Root.Arrange(new Rect(0, 0, 370, Height)); Root.UpdateLayout();
                if (generation != motion) return;
                UpdateGlassGeometry(); Native.ClipHorizontal(this, width); contentHost.SynchronizeCanvas();
                }
                finally { placing = false; if (finishPending) { finishPending = false; Finish(); } }
            }
            void Finish()
            {
                if (generation != motion || !animating || completed) return;
                if (placing) { finishPending = true; return; }
                completed = true;
                CancelLiveBallFrame(); animationDeadline?.Stop(); Place(target); complete();
                animating = false; UpdateGlassGeometry(); Native.ClipWindow(this, Height); contentHost.SynchronizeCanvas();
            }
            liveBallFrame = (_, _) =>
            {
                if (placing || completed || generation != motion) return;
                var fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / 100, 0, 1);
                Place(from + (target - from) * fraction);
                if (fraction >= 1) Finish();
            };
            CompositionTarget.Rendering += liveBallFrame;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(116) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        }
    }
}
