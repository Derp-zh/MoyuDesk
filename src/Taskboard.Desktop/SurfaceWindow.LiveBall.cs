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
    internal int LiveBallFrameResizeCount { get; private set; }
    private void CancelLiveBallFrame()
    {
        if (liveBallFrame is not null) CompositionTarget.Rendering -= liveBallFrame;
        liveBallFrame = null;
    }
    internal static double BallCapsuleProgress(double fraction)
    {
        var t = Math.Clamp(fraction, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }
    internal static double BallSearchOpacity(bool expanding, double fraction, double startOpacity)
    {
        var t = Math.Clamp(fraction, 0, 1);
        if (expanding)
        {
            if (t <= 0.18) return Math.Clamp(startOpacity, 0, 1);
            var fade = Math.Clamp((t - 0.18) / 0.67, 0, 1);
            return Math.Clamp(startOpacity + (1 - startOpacity) * fade, 0, 1);
        }
        var outFade = Math.Clamp(t / 0.55, 0, 1);
        return Math.Clamp(startOpacity * (1 - outFade), 0, 1);
    }
    private async void RevealLiveBall(bool expanded, bool extendLeft, Action prepare, Action complete)
    {
        if (this is BallSurface hiddenBall && !hiddenBall.IsBallEnabled)
        {
            CancelBallReveal();
            return;
        }
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        var ball = this as BallSurface;
        var generation = ++motion;
        var fullWidth = ball?.ExpandedWidth ?? 370;
        var anchor = extendLeft ? Left + Width : Left;
        var fromWidth = ball is not null
            ? Math.Clamp(animating ? ball.CurrentCapsuleWidth : (expanded ? 58 : Math.Max(58, Width)), 58, fullWidth)
            : Math.Clamp(Width, 58, fullWidth);
        var targetWidth = expanded ? fullWidth : 58;

        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation();
        animating = true; LastAnimationError = null; UsesIndependentAnimation = false;
        prepare();
        var startOpacity = ball?.SearchOpacity ?? (expanded ? 0 : 1);
        new WindowInteropHelper(this).EnsureHandle();
        Root.Opacity = 1; contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible;

        void SetWindowWidthOnce(double targetWindowWidth)
        {
            var targetLeft = extendLeft ? anchor - targetWindowWidth : anchor;
            updatingBallBounds = true;
            try { Width = targetWindowWidth; Left = targetLeft; }
            finally { updatingBallBounds = false; }
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var scale = Native.GetDpiForWindow(handle) / 96.0;
                var leftPixel = (int)Math.Round(targetLeft * scale);
                var rightPixel = (int)Math.Round((targetLeft + targetWindowWidth) * scale);
                Native.SetWindowPos(handle, IntPtr.Zero, leftPixel, (int)Math.Round(Top * scale),
                    Math.Max(1, rightPixel - leftPixel), (int)Math.Ceiling(Height * scale), 0x0114);
                Native.ClipHorizontal(this, targetWindowWidth);
            }
            contentHost.SynchronizeCanvas();
            contentHost.Height = Height;
            Root.Measure(new Size(370, Height));
            Root.Arrange(new Rect(0, 0, 370, Height));
            Root.UpdateLayout();
            UpdateGlassGeometry();
        }

        // Resize once to full expanded width before motion starts; keep the
        // window completely stationary while only the single-layer capsule clip
        // and search content opacity animate smoothly.
        ball?.SetCapsuleRevealState(fromWidth, startOpacity);
        if (Math.Abs(Width - fullWidth) > 0.5) SetWindowWidthOnce(fullWidth);
        else { contentHost.SynchronizeCanvas(); UpdateGlassGeometry(); Native.ClipHorizontal(this, Width); }

        if (ball is not null && !ball.IsBallEnabled) { CancelBallReveal(); return; }
        if (!IsVisible) ShowImmediately();
        Native.ShowWindow(ContentHandle, 4);
        await Dispatcher.InvokeAsync(() => Root.UpdateLayout(), DispatcherPriority.Render);
        if (generation != motion) return;
        if (ball is not null && !ball.IsBallEnabled) { CancelBallReveal(); return; }
        if (participant is not null) participant.Start(BeginMotion); else BeginMotion();
        void BeginMotion()
        {
            if (generation != motion) return;
            if (ball is not null && !ball.IsBallEnabled) { CancelBallReveal(); return; }
            MotionDisplayStartTicks = Stopwatch.GetTimestamp(); LiveBallRevealCount++;
            var clock = Stopwatch.StartNew();
            var completed = false;
            void Finish()
            {
                if (generation != motion || !animating || completed) return;
                completed = true;
                CancelLiveBallFrame(); animationDeadline?.Stop();
                ball?.SetCapsuleRevealState(targetWidth, expanded ? 1 : 0);
                complete();
                if (!expanded) SetWindowWidthOnce(58);
                animating = false;
                UpdateGlassGeometry(); Native.ClipWindow(this, Height); contentHost.SynchronizeCanvas();
            }
            liveBallFrame = (_, _) =>
            {
                if (completed || generation != motion) return;
                if (ball is not null && !ball.IsBallEnabled) { CancelBallReveal(); return; }
                var fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / 100.0, 0, 1);
                var currentWidth = fromWidth + (targetWidth - fromWidth) * BallCapsuleProgress(fraction);
                var opacity = BallSearchOpacity(expanded, fraction, startOpacity);
                ball?.SetCapsuleRevealState(currentWidth, opacity);
                UpdateGlassGeometry();
                if (fraction >= 1) Finish();
            };
            CompositionTarget.Rendering += liveBallFrame;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(116) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        }
    }
}
