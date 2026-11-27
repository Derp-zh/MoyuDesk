using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal partial class SurfaceWindow : Window
{
    // 0: original centered reveal; -1: grow from left; 1: grow from right.
    internal virtual int HorizontalRevealAnchor => 0;
    internal virtual bool SidewaysListReveal => false;
    public readonly Grid Root = new SurfaceContentRoot();
    private bool allowClose;
    private bool activateWhenShown;
    private bool animating;
    protected bool HasActiveMotion => animating;
    internal bool IsRevealAnimating => animating;
    internal bool FloatingAnimations { get; set; } = true;
    internal bool SuppressActivation { get; set; }
    protected virtual bool ScrollEnabled => FloatingAnimations && GlassEnabled;
    private bool scrollRunning, destroyAfterScroll;
    private double scrollFrom, scrollTo, scrollDuration, scrollStartLight;
    private readonly Stopwatch scrollClock = new();
    internal bool IsScrollAnimating => scrollRunning;
    internal bool IsContentSwapping { get; private set; }
    internal bool IsContentSwapHandoff => IsContentSwapping || handoff is not null || handoffCommitting;
    internal event Action? ContentSwapCompleted;
    internal long ContentSwapCount { get; private set; }
    internal double LastContentSwapPreparationMs { get; private set; }
    internal long ContentSwapFinishTicks { get; private set; }
    private long contentSwapStartedAt;
    internal bool CanQueueContentSwap(long pressedAt) =>
        contentSwapStartedAt > 0 && pressedAt - contentSwapStartedAt >= DraftSwitchMotion.DurationMs * 2 / 3;
    internal bool IsEarlyContentSwapPress(long pressedAt) =>
        contentSwapStartedAt > 0 && pressedAt - contentSwapStartedAt < DraftSwitchMotion.DurationMs * 2 / 3;
    private Rect? swapBounds;
    internal Point SwapInputOffset { get; private set; }
    private void RestoreSwapBounds()
    {
        if (swapBounds is not Rect original) return;
        swapBounds = null; SwapInputOffset = new Point();
        Left = original.Left; Top = original.Top; Width = original.Width; Height = original.Height;
        Root.UpdateLayout(); UpdateGlassGeometry(); Native.ClipWindow(this, Height); imageDirty = true;
    }
    internal bool ShouldRestore => scrollRunning ? scrollTo > 0 : IsVisible;
    internal event Action? BeforeOpen;
    internal event Action? BeforeHide;
    internal double ScrollProgress => scrollRunning ? ScrollMotion.Progress(scrollFrom, scrollTo, scrollClock.Elapsed.TotalMilliseconds, scrollDuration) : IsVisible ? 1 : 0;
    internal double ScrollLight => scrollRunning ? ScrollMotion.Light(scrollClock.Elapsed.TotalMilliseconds / Math.Max(1, scrollDuration), scrollTo == 1, scrollStartLight) : 0;
    internal bool CanInterruptTaskOpening => !scrollRunning || scrollTo <= 0 ||
        (scrollClock.IsRunning && ScrollMotion.CanInterruptOpening(scrollClock.Elapsed.TotalMilliseconds));
    public new void Show()
    {
        if (destroyAfterScroll) return;
        // Calling an existing/opening window is a focus operation. Replaying
        // its reveal or restoring input mid-animation would cause a flash.
        if (IsVisible && (!scrollRunning || scrollTo > 0)) return;
        BeforeOpen?.Invoke();
        if (ScrollEnabled && (!IsVisible || scrollRunning)) { RevealScroll(true); return; }
        ShowImmediately();
    }
    internal bool IsClickThrough { get; private set; }
    internal void SetClickThrough(bool enabled)
    {
        IsClickThrough = enabled;
        ApplyClickThroughStyle();
    }
    internal void ApplyClickThroughStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) Native.SetClickThrough(handle, IsClickThrough);
        if (ContentHandle != IntPtr.Zero) Native.SetClickThrough(ContentHandle, IsClickThrough);
    }
    private void ShowImmediately() {if(!animating) {Root.Opacity=1;contentHost.Visibility=Visibility.Visible;backdrop?.SetVisible(true);} base.Show();if(!animating)Native.ShowWindow(ContentHandle,4);ApplyClickThroughStyle();if(activateWhenShown && !IsClickThrough){activateWhenShown=false;base.Activate();}Native.RaiseBall();}
    public new bool Activate() { if (IsClickThrough) return false; if (SuppressActivation) { ActivationOrder = ++activationSequence; return false; } if (!IsVisible) { activateWhenShown = true; ActivationOrder = ++activationSequence; return false; } return base.Activate(); }
    internal static event Action<SurfaceWindow>? AnyWindowActivated;
    internal static event Action<SurfaceWindow>? AnyActiveWindowHiding;
    private void NotifyActiveHidingIfNeeded()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (IsActive || (handle != IntPtr.Zero && Native.GetForegroundWindow() == handle))
            AnyActiveWindowHiding?.Invoke(this);
    }
    public new void Hide()
    {
        NotifyActiveHidingIfNeeded();
        BeforeHide?.Invoke();
        activateWhenShown = false;
        if (ScrollEnabled && IsVisible) { RevealScroll(false); return; }
        HideImmediately();
    }
    private void HideImmediately()
    {motion++;animating=false;scrollRunning=false;IsContentSwapping=false;contentHost.AnimationHidden=false;animationDeadline?.Stop();CancelHandoff();backdrop?.CancelAnimation();RestoreSwapBounds();backdrop?.SetVisible(false);Root.Opacity=0;Native.ShowWindow(ContentHandle,0);base.Hide();}
    public bool GlassEnabled { get; set; } = true;
    public int WindowLayer { get; set; } = 2;
    public static double Transparency { get; private set; } = 20;
    internal static double FloatingTransparency { get; private set; } = 10;
    public string BackdropMode { get; internal set; } = "未初始化";
    internal int NativeHideCount { get; private set; }
    internal int ActivationPaintsSuppressed { get; private set; }
    public long ActivationOrder { get; private set; }
    private static long activationSequence;
    private long motion;
    internal long MotionGeneration => motion;
    private DispatcherTimer? animationDeadline;
    private EventHandler? handoff;
    private bool handoffCommitting;
    private EventHandler? liveListFrame;
    private double liveListHeight;
    internal int LiveListRevealCount { get; private set; }
    private void CancelHandoff()
    {
        if(handoff is not null) CompositionTarget.Rendering-=handoff;
        if(liveListFrame is not null) CompositionTarget.Rendering-=liveListFrame;
        CancelLiveScrollFrame(); CancelLiveBallFrame();
        liveListFrame=null; handoff=null; handoffCommitting=false;
    }
    private async void CompleteHandoff(long generation, Action? complete = null)
    {
        if (generation != motion) return;
        handoffCommitting = true;
        try
        {
            void ShowLive()
            {
                contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible;
                Root.Opacity = 1; Native.ShowWindow(ContentHandle, 4);
            }
            if (backdrop is not null) await backdrop.CommitRestingFrame(ShowLive); else ShowLive();
            if (generation != motion) return;
            Native.DwmFlush(); backdrop?.CancelAnimation(); handoffCommitting = false; complete?.Invoke();
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException) { }
        finally { if (generation == motion) handoffCommitting = false; }
    }
    private byte[]? animationImage;
    private int imageWidth, imageHeight;
    private bool imageDirty = true;
    internal double LastAnimationPreparationMs { get; private set; }
    internal double LastSnapshotMs { get; private set; }
    internal double LastShowMs { get; private set; }
    internal long MotionDisplayStartTicks { get; private set; }
    internal long WorkspaceRevealGroup { get; private set; }
    internal bool UsesIndependentAnimation { get; private set; }
    internal string? LastAnimationError { get; private set; }
    private LiveBackdrop? backdrop;
    private readonly GlassContentHost contentHost;
    internal bool LiveContentHidden => contentHost.AnimationHidden;
    internal int RestingHandoffCount => backdrop?.RestingHandoffCount ?? 0;
    internal int PersistentBackdropMotionCount => LiveListRevealCount;
    internal double TaskOpeningElapsedMs => scrollClock.IsRunning ? scrollClock.Elapsed.TotalMilliseconds : -1;
    internal IntPtr ContentHandle => contentHost.ContentHandle;
    internal Rect LiveContentBounds => Native.ScreenBounds(ContentHandle);
    protected void UseFixedBallCanvas(bool anchoredRight = true)
    {
        Root.Width = 370; contentHost.Width = 370;
        contentHost.HorizontalAlignment = anchoredRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        contentHost.FixedCanvasWidth = 370; contentHost.AnchorCanvasRight = anchoredRight; contentHost.SynchronizeCanvas();
    }
    internal virtual double GlassTransparency => FloatingTransparency;
    internal double GlassRadius => this is BallSurface ? Height / 2 : 20;
    internal virtual IEnumerable<GlassRegion> GlassRegions() => [new(new Rect(0, 0, Width, Height), Radius: GlassRadius)];
    public event Action? PositionChanged;
    public SurfaceWindow(double width, double height, bool layered = false)
    {
        Native.EnsureTsfControlKeyBridgeInstalled();
        Width = width; Height = height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Ui.Text;
        Root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        Root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground);
        QuickScroll.SetVerticalBarWidth(Root, this is ColumnSurface ? 6 : 6 * 1.34);
        QuickScroll.SetThumbMargin(Root, this is ColumnSurface ? new Thickness(1) : new Thickness(1.34, 1, 1.34, 1));
        Root.Background = Ui.Surface;
        Root.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16);
        var presentation = new Grid();
        presentation.Children.Add(Root); presentation.Children.Add(scrollGlow);
        contentHost = new GlassContentHost(presentation, (SurfaceContentRoot)Root)
        {
            ShouldActivateOnClick = () => !SuppressActivation && IsVisible,
            EnsureHostActivated = () => { if (!SuppressActivation && IsVisible) base.Activate(); }
        };
        Content = contentHost;
        SourceInitialized += (_, _) =>
        {
            Native.HideFromTaskSwitcher(new WindowInteropHelper(this).Handle);
            if (GlassEnabled) Native.TryGlass(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(CompositionChanged);
            Native.ClipWindow(this, Height);
            ApplyClickThroughStyle();
        };
        Activated += (_, _) => { ActivationOrder = ++activationSequence; Native.RaiseBall(); AnyWindowActivated?.Invoke(this); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Native.RaiseBall(); };
        SizeChanged += (_, _) => { UpdateGlassGeometry(); if (!animating) Native.ClipWindow(this, ActualHeight); };
        DpiChanged += (_, _) => { UpdateGlassGeometry(); if (!animating) Native.ClipWindow(this, Height); };
        Root.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) => OnRootGotKeyboardFocus(e.NewFocus)));
        Root.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_) => InvalidateAnimation()));
        Root.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,_) => InvalidateAnimation()));
        Closed += (_, _) => { CancelHandoff(); animationDeadline?.Stop(); backdrop?.Dispose(); backdrop = null; contentHost.Dispose(); };
        Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Hide(); } };
    }
    private bool syncingInputFocus;
    internal int InputFocusSyncCount { get; private set; }
    private void OnRootGotKeyboardFocus(IInputElement? focused)
    {
        if (focused is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
            SynchronizeInputFocus(focused);
    }
    internal void SynchronizeInputFocus(IInputElement? target = null)
    {
        if (syncingInputFocus) return;
        syncingInputFocus = true;
        try
        {
            InputFocusSyncCount++;
            if (SuppressActivation || !IsVisible) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (Native.GetForegroundWindow() != hwnd) base.Activate();
            contentHost.EnsureNativeFocus();
            if (target is DependencyObject dep && !ReferenceEquals(Keyboard.FocusedElement, dep))
                Keyboard.Focus(target);
        }
        finally
        {
            syncingInputFocus = false;
        }
    }
    private IntPtr CompositionChanged(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Reanchor the native child before WPF handles the parent's size change
        // and performs layout. Waiting until after layout exposed shifted text
        // for several milliseconds on every leftward reveal frame.
        if (message == 0x0047 && this is BallSurface) { contentHost.SynchronizeCanvas(); UpdateGlassGeometry(); }
        if (message == 0x0046 && updatingBallBounds && lParam != IntPtr.Zero)
        {
            var position = Marshal.PtrToStructure<WindowPos>(lParam);
            position.Flags |= 3; // Reconcile both logical properties before one native move/resize.
            Marshal.StructureToPtr(position, lParam, false);
        }
        if (message == 0x031E && GlassEnabled) Native.TryGlass(this);
        if (message == 0x0018 && wParam == IntPtr.Zero) NativeHideCount++;
        if (message == 0x0014) { handled = true; return new IntPtr(1); }
        // Forward Win32 keyboard focus from the outer glass shell to the child WPF
        // input root AND set handled=true so the outer HwndKeyboardInputProvider.OnSetFocus
        // cannot run afterward, deactivate the child keyboard provider, clear WPF focus,
        // or reset the thread TSF ITfDocumentMgr to EmptyDim.
        if (message == 0x0007 && IsVisible && ContentHandle != IntPtr.Zero && !contentHost.AnimationHidden)
        {
            if (!SuppressActivation) contentHost.EnsureNativeFocus();
            handled = true;
            return IntPtr.Zero;
        }
        // These windows draw their entire frame themselves. Let activation
        // change keyboard focus, but prevent Windows from repainting a native
        // non-client frame over the transparent composition target on click.
        if (message == 0x0086 && GlassEnabled) { ActivationPaintsSuppressed++; handled = true; return new IntPtr(1); }
        if (message == 0x0202 && animating && this is BallSurface ball)
        {
            var packed = lParam.ToInt64(); var scale = Native.GetDpiForWindow(hwnd)/96.0;
            var x = (short)(packed & 0xffff)/scale; var y = (short)((packed >> 16)&0xffff)/scale;
            if (ball.ContainsOrbPoint(new Point(x, y))) { ball.ClickDuringAnimation(); handled=true; }
        }
        if (message == 0x0084 && IsClickThrough) { handled = true; return new IntPtr(-1); }
        if (message == 0x0084 && this is BallSurface entry)
        {
            var packed = lParam.ToInt64(); var p = new Point((short)(packed & 0xffff),(short)((packed >> 16)&0xffff));
            var bounds = Native.ScreenBounds(this); var radius = bounds.Height/2;
            var scale = bounds.Height / Math.Max(1, entry.Height);
            var visibleWidth = Math.Clamp(entry.CurrentCapsuleWidth * scale, bounds.Height, Math.Max(bounds.Height, bounds.Width));
            var capLeft = entry.ExpandsLeft ? bounds.Right - visibleWidth : bounds.Left;
            var capRight = entry.ExpandsLeft ? bounds.Right : bounds.Left + visibleWidth;
            if (p.X < capLeft || p.X > capRight || p.Y < bounds.Top || p.Y > bounds.Bottom) { handled = true; return new IntPtr(-1); }
            var x = Math.Clamp(p.X, capLeft + radius, capRight - radius); var y = bounds.Top + radius;
            var flat = entry.IsEdgeDocked && (entry.DockedLeft ? p.X <= capLeft + radius : p.X >= capRight - radius);
            if (!flat && (p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)>radius*radius) { handled = true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    public void Destroy() => Destroy(true);
    public void Destroy(bool animate)
    {
        NotifyActiveHidingIfNeeded();
        if (animate && ScrollEnabled && IsVisible) { destroyAfterScroll = true; RevealScroll(false); return; }
        HideImmediately(); allowClose = true; motion++; Close();
    }
    public void MoveWith(UIElement header)
    {
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInteractive(source)) return;
            if (e.ButtonState != MouseButtonState.Pressed) return;
            DragMove(); PositionChanged?.Invoke(); e.Handled = true;
        };
    }
    internal static bool IsInteractive(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase or TextBox or ComboBox
                or FrameworkElement { Tag: "TaskTimerDot" or "DismissReminderBubble" or "StageDetailsToggleBubble" }) return true;
        return false;
    }
    public static void SetTransparency(double value, double? floating = null)
    {
        Transparency = Math.Clamp(value, 0, 100);
        if (floating is double floatingValue) FloatingTransparency = Math.Clamp(floatingValue, 0, 100);
        foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().ToArray()) window.RefreshGlass();
    }
    internal bool SkipsDesktopBlur => GlassTransparency <= 0.001;
    internal bool BackdropUsesDesktopBlur => backdrop?.UsesHostBackdropBrush ?? false;
    internal int BackdropRebuildCount => backdrop?.RebuildCount ?? 0;
    internal int BackdropInPlaceUpdateCount => backdrop?.InPlaceUpdateCount ?? 0;
    internal Windows.UI.Color BackdropSurfaceColor => backdrop?.LastSurfaceColor ?? ThemePalette.Current.SurfaceCompositionColor;
    internal Brush CurrentGlassTintBrush => GlassTintBrush;
    public virtual void ApplyTheme()
    {
        Foreground = Ui.Text;
        Root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground);
        if (!GlassEnabled || backdrop is null) ApplyGlassTint();
        RefreshGlass();
    }
    public virtual void RefreshGlass()
    {
        imageDirty = true;
        if (!GlassEnabled || new WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        Native.UpdateWindowTheme(this);
        if (backdrop is not null) { ApplyGlassTint(); UpdateGlassGeometry(); BackdropMode = SkipsDesktopBlur ? "纯色不透明（跳过桌面模糊）" : "实时系统毛玻璃"; }
        else Native.TryGlass(this);
    }
    internal void EnsureLiveBackdrop()
    { backdrop ??= new LiveBackdrop(new WindowInteropHelper(this).Handle); ApplyGlassTint(); UpdateGlassGeometry(); }
    protected Brush GlassTintBrush => ThemePalette.Current.SurfaceWithAlpha((byte)Math.Round(255 * (1 - GlassTransparency / 100)));
    protected virtual void ApplyGlassTint() => Root.Background = GlassTintBrush;
    private (bool EdgeDocked, bool DockedLeft, bool ExpandsLeft, double CapsuleWidth, double Height) lastBallClipState = (false, false, true, -1, -1);
    internal void UpdateGlassGeometry()
    {
        var bounds = new Rect(0,0,(this is BallSurface ? 370 : Width) + (this is BallSurface { IsEdgeDocked: true } ? GlassRadius : 0),Height);
        if (this is BallSurface ball)
        {
            var clipState = (ball.IsEdgeDocked, ball.DockedLeft, ball.ExpandsLeft, ball.CurrentCapsuleWidth, Height);
            if (lastBallClipState != clipState) { lastBallClipState = clipState; Root.Clip = ball.CanvasClip; imageDirty = true; }
        }
        else if (Root.Clip is not RectangleGeometry old || old.Rect != bounds || old.RadiusX != GlassRadius)
        { Root.Clip = new RectangleGeometry(bounds,GlassRadius,GlassRadius); imageDirty = true; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            backdrop?.Update(GlassRegions(), Native.GetDpiForWindow(hwnd) / 96.0,
                this is ColumnSurface ? GlassTransparency : null, opaqueBackdrop: SkipsDesktopBlur,
                surfaceColor: ThemePalette.Current.SurfaceCompositionColor);
            if (backdrop is not null) BackdropMode = SkipsDesktopBlur ? "纯色不透明（跳过桌面模糊）" : "实时系统毛玻璃";
        }
    }
    internal void InvalidateAnimation() => imageDirty = true;
    internal async void SwapContent(Action change, double paneWidth, double selectedSlot)
    {
        if (!IsVisible || scrollRunning || backdrop is null) { change(); return; }
        var preparation = Stopwatch.StartNew();
        var generation = ++motion; CancelHandoff(); animationDeadline?.Stop(); backdrop.CancelAnimation();
        RestoreSwapBounds();
        animating = IsContentSwapping = true; contentSwapStartedAt = 0; ContentSwapCount++;
        var scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        Root.Opacity = 1; InvalidateAnimation(); WarmAnimation(); var oldWidth = imageWidth; var oldHeight = imageHeight;
        await backdrop.HoldContent(new WindowInteropHelper(this).Handle, animationImage!, imageWidth, imageHeight);
        if (generation != motion || !IsVisible) return;
        contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Hidden; Native.ShowWindow(ContentHandle, 0);
        var stack = (DraftStackSurface)this; var oldFront = stack.FrontCard;
        var beforeCard = stack.CaptureFrontCard(scale);
        var beforeSelectedCard = stack.CaptureCard(stack.SelectedCard(selectedSlot), scale, includeTint: false);
        change(); Root.Opacity = 1; WarmAnimation();
        var afterCard = stack.CaptureFrontCard(scale); var oldAfterCard = stack.CaptureCard(oldFront, scale, includeTint: false);
        if (oldWidth != imageWidth || oldHeight != imageHeight) { IsContentSwapping = false; animating = false; contentHost.AnimationHidden = false; Native.ShowWindow(ContentHandle, 4); return; }
        var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; ContentSwapFinishTicks = Stopwatch.GetTimestamp(); animationDeadline?.Stop(); animating = IsContentSwapping = false;
            RestoreSwapBounds();
            contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Visible; Root.Opacity = 1; Native.ShowWindow(ContentHandle, 0);
            var frames = 0; handoff = (_, _) => { if (++frames < 2) return; CancelHandoff(); if (generation == motion) CompleteHandoff(generation, () => ContentSwapCompleted?.Invoke()); };
            CompositionTarget.Rendering += handoff;
        }
        try
        {
            var padding = DraftSwitchMotion.Padding(paneWidth);
            await backdrop.AnimateSwap(new WindowInteropHelper(this).Handle, beforeCard, beforeSelectedCard, oldAfterCard, afterCard, animationImage!, imageWidth, imageHeight,
                paneWidth * scale, selectedSlot * scale, 0, 0,
                () => {
                    swapBounds = new Rect(Left, Top, Width, Height); SwapInputOffset = new Point();
                    contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0);
                    Width += padding;
                    Native.ClipWindow(this, Height);
                },
                () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render),
                () => contentSwapStartedAt = Environment.TickCount64);
            if (generation != motion || completed) return;
            contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0); UsesIndependentAnimation = true;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs + 60) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { if (generation != motion || completed) return; backdrop.CancelAnimation(); UsesIndependentAnimation = false; Finish(); }
        LastContentSwapPreparationMs = preparation.Elapsed.TotalMilliseconds;
    }
    public WindowPosition Position() => new() { X = Left, Y = Top, Width = Width, Height = Height };
    public void Place(WindowPosition position)
    { Width = position.Width; Height = position.Height; Left = position.X; Top = position.Y; }

    public void Reveal(bool visible, bool animate)
    {
        if (!visible) BeforeHide?.Invoke();
        if (this is ColumnSurface && animate) { if (SidewaysListReveal) RevealLiveScroll(visible); else RevealLiveList(visible); return; }
        if (ScrollEnabled && animate) { RevealScroll(visible); return; }
        var generation = ++motion;
        CancelHandoff();
        animationDeadline?.Stop();
        backdrop?.CancelAnimation();
        RestoreSwapBounds();
        if (!animate) { animating = false; scrollRunning = false; if (visible) ShowImmediately(); else HideImmediately(); Native.ClipWindow(this, Height); IsHitTestVisible = true; return; }
        var from = IsVisible ? Height : 0;
        animating = true; UpdateGlassGeometry();
        new WindowInteropHelper(this).EnsureHandle();
        IsHitTestVisible = visible;
        var target = visible ? Height : 0;
        RunMotion(generation,false,from,target,() =>
        {
            animating = false;
            if (!visible) Hide();
            Native.ClipWindow(this, Height);
            IsHitTestVisible = true;
        });
    }
    private async void RevealLiveList(bool visible)
    {
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        var from = IsVisible ? (animating ? liveListHeight : Height) : 0;
        var generation = ++motion;
        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation();
        animating = true; scrollRunning = false; IsContentSwapping = false; LastAnimationError = null;
        new WindowInteropHelper(this).EnsureHandle();
        contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible; Root.Opacity = 1;
        Root.Measure(new Size(Width, Height)); Root.Arrange(new Rect(0, 0, Width, Height)); Root.UpdateLayout();
        liveListHeight = from; Native.ClipLiveList(this, from);
        if (!IsVisible && visible) ShowImmediately();
        if (!IsVisible) { animating = false; return; }
        Native.ShowWindow(ContentHandle, 4); ApplyClickThroughStyle(); IsHitTestVisible = visible;
        // Keep real controls and the same glass/tint visible throughout. Ending
        // a list reveal never swaps a texture for a differently rasterized UI.
        await Dispatcher.InvokeAsync(() => Root.UpdateLayout(), DispatcherPriority.Render);
        if (generation != motion) return;
        // Real controls reach composition before the shared 100ms timeline.
        var prepared = new TaskCompletionSource<bool>(); var rendered = 0;
        var preparationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(80) };
        preparationDeadline.Tick += (_, _) => prepared.TrySetResult(true);
        cancelLivePreparation = () => { preparationDeadline.Stop(); prepared.TrySetResult(false); };
        liveListFrame = (_, _) => { if (++rendered >= 2) prepared.TrySetResult(true); };
        CompositionTarget.Rendering += liveListFrame;
        preparationDeadline.Start(); var ready = await prepared.Task; preparationDeadline.Stop();
        if (!ready || generation != motion) return;
        CompositionTarget.Rendering -= liveListFrame; liveListFrame = null; cancelLivePreparation = null;
        if (participant is not null) participant.Start(BeginMotion); else BeginMotion();
        void BeginMotion()
        {
            if (generation != motion) return;
            MotionDisplayStartTicks = Stopwatch.GetTimestamp(); LiveListRevealCount++;
            var clock = Stopwatch.StartNew(); var target = visible ? Height : 0;
            void Finish()
            {
                if (generation != motion || !animating) return;
                CancelHandoff(); animationDeadline?.Stop(); animating = false; liveListHeight = target;
                if (!visible) HideImmediately(); else Native.ClipWindow(this, Height);
                IsHitTestVisible = true;
            }
            liveListFrame = (_, _) =>
            {
                if (generation != motion || !animating) return;
                var fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / 100, 0, 1);
                liveListHeight = from + (target - from) * fraction;
                Native.ClipLiveList(this, liveListHeight);
                if (fraction >= 1) Finish();
            };
            CompositionTarget.Rendering += liveListFrame;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(110) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
            UsesIndependentAnimation = false;
        }
    }
    private void RevealScroll(bool visible) => RevealLiveScroll(visible);
    protected void RevealBall(bool expanded, bool extendLeft, Action prepare, Action complete) => RevealLiveBall(expanded, extendLeft, prepare, complete);
    protected void CancelBallReveal()
    {
        motion++;
        CancelLiveBallFrame();
        animationDeadline?.Stop();
        animating = false;
    }
    internal void WarmAnimation()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Root.Measure(new Size(Width,Height)); Root.Arrange(new Rect(0,0,Width,Height)); Root.UpdateLayout();
        var scale = Native.GetDpiForWindow(hwnd)/96.0;
        var width = (int)Math.Ceiling(Width*scale); var height = (int)Math.Ceiling(Height*scale);
        if (!imageDirty && animationImage is not null && imageWidth == width && imageHeight == height) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(Root); animationImage = new byte[width*height*4]; bitmap.CopyPixels(animationImage,width*4,0);
        imageWidth = width; imageHeight = height; imageDirty = false;
        backdrop?.Prepare(animationImage,width,height);
    }
    internal void PrimeWindow()
    {
        var left=Left; var top=Top;
        Left=SystemParameters.VirtualScreenLeft-Width-100; Top=SystemParameters.VirtualScreenTop-Height-100;
        try { contentHost.AnimationHidden=false; ShowImmediately(); Root.UpdateLayout(); WarmAnimation(); HideImmediately(); }
        finally { Left=left;Top=top; }
    }
    private async void RunMotion(long generation, bool horizontal, double from, double to, Action complete, bool scroll = false, double durationMs = 100)
    {
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        LastAnimationError = null;
        var watch = Stopwatch.StartNew();
        var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; animationDeadline?.Stop();
            var oldWidth=Width; complete(); contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Visible;
            if(!IsVisible) { Root.Opacity=0;Native.ShowWindow(ContentHandle,0);backdrop?.CancelAnimation();backdrop?.SetVisible(false);return; }
            // Prepare the live child while its native surface stays hidden.
            // The compositor handoff reveals it, without two visible copies.
            Root.Opacity=1;Native.ShowWindow(ContentHandle,0);
            backdrop?.AlignAnimation((Width-oldWidth)*Native.GetDpiForWindow(new WindowInteropHelper(this).Handle)/96.0);
            var rendered=0;
            handoff=(_,_) => { if(++rendered<2) return; CancelHandoff(); if(generation==motion) CompleteHandoff(generation); };
            CompositionTarget.Rendering+=handoff;
        }
        void PrepareDisplay()
        {
            if (generation != motion || completed) return;
            contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Hidden; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0);
            var showClock = Stopwatch.StartNew(); if (!IsVisible) ShowImmediately(); LastShowMs = showClock.Elapsed.TotalMilliseconds;
            Native.ClipWindow(this, Height);
        }
        try
        {
            Root.Opacity=1; WarmAnimation(); LastSnapshotMs=watch.Elapsed.TotalMilliseconds; var hwnd = new WindowInteropHelper(this).Handle;
            if (backdrop is null) throw new NotSupportedException("当前系统没有窗口动画合成支持");
            if (horizontal) Native.ClipHorizontal(this, from); else Native.ClipWindow(this,Height);
            if (scroll)
                await backdrop!.AnimateScroll(hwnd, animationImage!, imageWidth, imageHeight, from, to, durationMs, scrollStartLight, PrepareDisplay,
                    () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render),
                    () => { MotionDisplayStartTicks = Stopwatch.GetTimestamp(); scrollClock.Restart(); }, participant is null ? null : participant.Ready);
            else
                await backdrop!.Animate(hwnd,animationImage!,imageWidth,imageHeight,horizontal,from*Native.GetDpiForWindow(hwnd)/96.0,to*Native.GetDpiForWindow(hwnd)/96.0, PrepareDisplay,
                    () => Dispatcher.BeginInvoke(new Action(Finish),DispatcherPriority.Render), participant is null ? null : participant.Ready,
                    () => MotionDisplayStartTicks = Stopwatch.GetTimestamp(), flatRight: this is BallSurface { IsEdgeDocked: true });
            if (generation != motion || completed) return;
            UsesIndependentAnimation = true;
            // Occluded/offscreen targets can defer compositor completion events.
            // This is a single end deadline, never a frame-production timer.
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(durationMs + (scroll ? 60 : 0)) };
            animationDeadline.Tick += (_,_) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { if (generation != motion || completed) return; LastAnimationError = e.ToString(); backdrop?.CancelAnimation(); if (to > 0 && !IsVisible) ShowImmediately(); UsesIndependentAnimation = false; Finish(); }
        LastAnimationPreparationMs = watch.Elapsed.TotalMilliseconds;
    }
}

internal static class Native
{
    internal static void BeginWorkspaceChange() { ordering = true; WorkspaceRevealBatch.Current = new WorkspaceRevealBatch(); }
    internal static void EndWorkspaceChange() { var batch = WorkspaceRevealBatch.Current; WorkspaceRevealBatch.Current = null; batch?.Seal(); ordering=false;RaiseBall(); }
    private static Window? ball;
    private static Window? pinnedOverlay;
    private static bool ordering;
    internal static void SetPinnedOverlay(Window? overlay) { pinnedOverlay = overlay; RaiseBall(); }
    internal static bool SetClickThrough(IntPtr handle, bool enabled)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle)) return false;
        const long WsExTransparent = 0x20L;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        var next = enabled ? (style | WsExTransparent) : (style & ~WsExTransparent);
        if (next != style)
        {
            SetWindowLongPtr(handle, -20, new IntPtr(next));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
        }
        return true;
    }
    internal static bool IsClickThrough(IntPtr handle) =>
        handle != IntPtr.Zero && IsWindow(handle) && (GetWindowLongPtr(handle, -20).ToInt64() & 0x20L) != 0;
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static bool Cloak(IntPtr hwnd,bool hidden) {var value=hidden?1:0;return DwmSetWindowAttribute(hwnd,13,ref value,4)==0;}
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    internal static void HideFromTaskSwitcher(IntPtr handle)
    {
        // A native tool window stays interactive but has no Alt+Tab/taskbar entry.
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr((style | 0x80) & ~0x40000));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }
    [DllImport("imm32.dll")] private static extern bool ImmAssociateContextEx(IntPtr hwnd, IntPtr himc, uint flags);
    [DllImport("imm32.dll")] internal static extern IntPtr ImmGetContext(IntPtr hwnd);
    [DllImport("imm32.dll")] internal static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    internal static bool EnsureDefaultImeContext(IntPtr handle) =>
        handle != IntPtr.Zero && IsWindow(handle) && ImmAssociateContextEx(handle, IntPtr.Zero, 0x0010);
    // Match WPF InputMethod.IsImm32Ime: legacy IMM32 HKLs live in 0xE0xxxxxx,
    // while modern Windows 11 TSF TIPs (e.g. WeType / Microsoft Pinyin on 0x08040804)
    // report ImmIsIME(hkl) == true even though they are pure TSF services.
    internal static bool IsImm32ImeActive() => (unchecked((uint)GetKeyboardLayout(0).ToInt64()) & 0xF0000000u) == 0xE0000000u;

    [ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfThreadMgr
    {
        [PreserveSig] int Activate(out uint clientId);
        [PreserveSig] int Deactivate();
        [PreserveSig] int CreateDocumentMgr(out IntPtr dim);
        [PreserveSig] int EnumDocumentMgrs(out IntPtr enumDims);
        [PreserveSig] int GetFocus(out IntPtr dim);
        [PreserveSig] int SetFocus(IntPtr dim);
        [PreserveSig] int AssociateFocus(IntPtr hwnd, IntPtr newDim, out IntPtr prevDim);
        [PreserveSig] int IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool isFocus);
    }
    [ComImport, Guid("aa80e7f0-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfKeystrokeMgr
    {
        [PreserveSig] int AdviseKeyEventSink(uint clientId, IntPtr punk, [MarshalAs(UnmanagedType.Bool)] bool foreground);
        [PreserveSig] int UnadviseKeyEventSink(uint clientId);
        [PreserveSig] int GetForeground(out Guid clsid);
        [PreserveSig] int TestKeyDown(UIntPtr wParam, IntPtr lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);
        [PreserveSig] int TestKeyUp(UIntPtr wParam, IntPtr lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);
        [PreserveSig] int KeyDown(UIntPtr wParam, IntPtr lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);
        [PreserveSig] int KeyUp(UIntPtr wParam, IntPtr lParam, [MarshalAs(UnmanagedType.Bool)] out bool eaten);
    }
    [DllImport("msctf.dll")] private static extern int TF_GetThreadMgr(out ITfThreadMgr threadMgr);

    [ThreadStatic] private static bool tsfControlKeyBridgeInstalled;
    internal static int TsfControlKeyForwardCount { get; private set; }
    internal static bool IsTsfControlKeyBridgeInstalled => tsfControlKeyBridgeInstalled;

    internal static IntPtr GetTsfFocusedDocumentMgrIdentity()
    {
        try
        {
            if (TF_GetThreadMgr(out var threadMgr) != 0 || threadMgr is null) return IntPtr.Zero;
            if (threadMgr.GetFocus(out var rawDim) != 0 || rawDim == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                var obj = Marshal.GetObjectForIUnknown(rawDim);
                var unk = Marshal.GetIUnknownForObject(obj);
                Marshal.Release(unk);
                return unk;
            }
            finally { Marshal.Release(rawDim); }
        }
        catch { return IntPtr.Zero; }
    }

    internal static void EnsureTsfControlKeyBridgeInstalled()
    {
        if (tsfControlKeyBridgeInstalled) return;
        tsfControlKeyBridgeInstalled = true;
        ComponentDispatcher.ThreadFilterMessage += OnTsfControlKeyFilterMessage;
    }

    private static void OnTsfControlKeyFilterMessage(ref MSG msg, ref bool handled)
    {
        if (handled) return;
        if (TryForwardTsfControlKey(ref msg, out _)) handled = true;
    }

    // .NET WPF's TextServicesManager.TextServicesKeystroke maps LeftShift/RightShift
    // back to VK_SHIFT (0x10) + scan code 42/54, but omits LeftCtrl/RightCtrl, sending
    // wParam = VK_LCONTROL (0xA2) / VK_RCONTROL (0xA3) with scanCode = 0 to TSF.
    // Pure TSF Chinese IMEs such as WeType expect standard Win32 WM_KEYDOWN/WM_KEYUP
    // wParam = VK_CONTROL (0x11) with the real scan code / extended flag in lParam.
    // Forward VK_CONTROL directly to ITfKeystrokeMgr before WPF replaces wParam with
    // 0xA2/0xA3, while leaving Ctrl+C/V/A/Z (via Win32 GetKeyState) and IME-disabled
    // inputs (e.g. SettingsHotkey) completely untouched.
    internal static bool TryForwardTsfControlKey(ref MSG msg, out bool eaten)
    {
        eaten = false;
        const int WmKeyDown = 0x0100;
        const int WmKeyUp = 0x0101;
        const long VkControl = 0x11;
        if (msg.message is not (WmKeyDown or WmKeyUp) || msg.wParam.ToInt64() != VkControl) return false;
        if (Keyboard.FocusedElement is not DependencyObject focused) return false;
        if (focused is not (System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)) return false;
        if (!InputMethod.GetIsInputMethodEnabled(focused)) return false;
        if (focused is not Visual visual || PresentationSource.FromVisual(visual) is not HwndSource source || source.Handle != msg.hwnd) return false;
        if (IsImm32ImeActive()) return false;
        try
        {
            if (TF_GetThreadMgr(out var threadMgr) != 0 || threadMgr is null) return false;
            if (threadMgr.GetFocus(out var dim) != 0 || dim == IntPtr.Zero) return false;
            Marshal.Release(dim);
            if (threadMgr is not ITfKeystrokeMgr keystrokeMgr) return false;
            var wParam = new UIntPtr((ulong)VkControl);
            if (msg.message == WmKeyDown)
            {
                if (keystrokeMgr.TestKeyDown(wParam, msg.lParam, out var testEaten) == 0 && testEaten)
                {
                    keystrokeMgr.KeyDown(wParam, msg.lParam, out eaten);
                    eaten = true;
                }
            }
            else
            {
                if (keystrokeMgr.TestKeyUp(wParam, msg.lParam, out var testEaten) == 0 && testEaten)
                {
                    keystrokeMgr.KeyUp(wParam, msg.lParam, out eaten);
                    eaten = true;
                }
            }
            TsfControlKeyForwardCount++;
            return true;
        }
        catch { return false; }
    }
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr hwnd);
    public static void SetBall(Window window) { ball = window; RaiseBall(); }
    public static void RaiseBall()
    {
        if (ordering || Application.Current is null) return; ordering = true;
        try
        {
            var handles = Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x.IsVisible && x != ball)
                .OrderBy(x => x.WindowLayer).ThenBy(x => x is ColumnSurface column && column.SidewaysListReveal ? 1 : 0)
                .ThenBy(x => x.ActivationOrder).Select(x => new WindowInteropHelper(x).Handle)
                .Concat(pinnedOverlay?.IsVisible == true ? new[] { new WindowInteropHelper(pinnedOverlay).Handle } : Array.Empty<IntPtr>())
                .Concat(Dialogs()).Concat(ball?.IsVisible == true ? new[] { new WindowInteropHelper(ball).Handle } : Array.Empty<IntPtr>())
                .Where(h => h != IntPtr.Zero)
                .Distinct().ToArray();
            var owned = handles.ToHashSet(); var current = new List<IntPtr>();
            EnumWindows((handle, _) => { if (owned.Contains(handle)) current.Add(handle); return true; }, IntPtr.Zero);
            if (current.SequenceEqual(handles.Reverse())) return;
            // Apply the complete stack in one native transaction; sequential
            // raises briefly put every list above its floating editors.
            var positions = BeginDeferWindowPos(handles.Length);
            foreach (var handle in handles)
            {
                if (positions == IntPtr.Zero) break;
                positions = DeferWindowPos(positions, handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            }
            if (positions != IntPtr.Zero) EndDeferWindowPos(positions);
        }
        finally { ordering = false; }
    }
    public static void Raise(Window window)
    { if (window.IsVisible) SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
    [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr positions, IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr positions);
    [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State; public int Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct Data { public int Attribute; public IntPtr DataPointer; public int Size; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref Data data);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr hwnd,bool enabled);
    internal static IntPtr[] Dialogs()
    {
        var result = new List<IntPtr>();
        EnumWindows((handle, _) => { GetWindowThreadProcessId(handle, out var process); if (process != Environment.ProcessId || !IsWindowVisible(handle)) return true;
            var name = new StringBuilder(64); GetClassName(handle, name, 64); if (name.ToString() == "#32770") result.Add(handle); return true; }, IntPtr.Zero);
        result.Reverse(); return result.ToArray();
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    internal static void ClearWindowRegion(IntPtr handle) => SetWindowRgn(handle, IntPtr.Zero, false);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect bounds);
    internal static Rect ScreenBounds(Window window)
        => ScreenBounds(new WindowInteropHelper(window).Handle);
    internal static Rect ScreenBounds(IntPtr handle)
    { GetWindowRect(handle,out var r); return new Rect(r.Left,r.Top,Math.Max(0,r.Right-r.Left),Math.Max(0,r.Bottom-r.Top)); }
    internal static void ClipLiveList(Window window, double height)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var scale = GetDpiForWindow(handle) / 96.0;
        var region = CreateRectRgn(0, 0, (int)Math.Ceiling(window.Width * scale), Math.Max(1, (int)Math.Round(height * scale)));
        if (SetWindowRgn(handle, region, false) == 0) DeleteObject(region);
    }
    internal static void ClipLiveScroll(Window window, double progress)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var scale = GetDpiForWindow(handle) / 96.0;
        var width = Math.Max(1, (int)Math.Ceiling(window.Width * scale));
        var visible = Math.Clamp((int)Math.Round(width * progress), 1, width);
        var left = window is SurfaceWindow surface && surface.HorizontalRevealAnchor != 0
            ? (surface.HorizontalRevealAnchor < 0 ? 0 : width - visible) : (width - visible) / 2;
        var region = CreateRectRgn(left, 0, left + visible, (int)Math.Ceiling(window.Height * scale));
        if (SetWindowRgn(handle, region, false) == 0) DeleteObject(region);
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public static void ClipWindow(Window window, double height)
    {
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        if (window is BallSurface ball)
        {
            if (!ball.IsEdgeHidden) { ClipHorizontal(window, window.Width); return; }
            // Clip the part outside this monitor too, so it cannot leak onto an
            // adjacent display in a multi-monitor desktop.
            var dpi = GetDpiForWindow(hwnd) / 96.0;
            var visible = ball.EdgeVisibleClip;
            var edgeRegion = CreateRectRgn((int)Math.Round(visible.Left * dpi), 0,
                (int)Math.Ceiling(visible.Right * dpi), (int)Math.Ceiling(window.Height * dpi));
            var shape = CreateRoundRectRgn((int)Math.Round((ball.DockedLeft ? -window.Height / 2 : 0) * dpi), 0,
                (int)Math.Ceiling((window.Width + (ball.IsEdgeDocked && !ball.DockedLeft ? window.Height / 2 : 0)) * dpi),
                (int)Math.Ceiling(window.Height * dpi), (int)Math.Round(window.Height * dpi), (int)Math.Round(window.Height * dpi));
            const int RegionAnd = 1;
            CombineRgn(edgeRegion, edgeRegion, shape, RegionAnd); DeleteObject(shape);
            if (SetWindowRgn(hwnd, edgeRegion, true) == 0) DeleteObject(edgeRegion);
            return;
        }
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var diameter = window is BallSurface ? 58 : 40;
        var pixelWidth = (int)Math.Ceiling(window.Width * scale); var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var region = window is BallSurface && Math.Abs(window.Width - window.Height) < 1 ? CreateEllipticRgn(0, 0, pixelWidth, pixelHeight) : CreateRoundRectRgn(0, 0, pixelWidth + 1, pixelHeight + 1, (int)(diameter * scale), (int)(diameter * scale));
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }
    internal static void ClipHorizontal(Window window, double visibleWidth)
    {
        var hwnd = new WindowInteropHelper(window).Handle; var scale = GetDpiForWindow(hwnd) / 96.0;
        var left = window is BallSurface { DockedLeft: true } ? -window.Height / 2 : window.Width - visibleWidth;
        var right = window.Width + (window is BallSurface { IsEdgeDocked: true, DockedLeft: false } ? window.Height / 2 : 0);
        var region = CreateRoundRectRgn((int)Math.Round(left * scale), 0, (int)Math.Ceiling(right * scale),
            (int)Math.Ceiling(window.Height * scale), (int)(window.Height * scale), (int)(window.Height * scale));
        if (window is BallSurface)
        {
            var client = CreateRectRgn(0, 0, (int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(window.Height * scale));
            CombineRgn(region, region, client, 1); DeleteObject(client);
        }
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }
    internal static void UpdateWindowTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = ThemePalette.Current.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    public static void TryGlass(SurfaceWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        int dark = ThemePalette.Current.IsDark ? 1 : 0, rounded = 1, hostBrush = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        // The compositor owns shaped live backdrop visuals within this HWND;
        // no full rectangular accent layer and no capture/Hide/Show cycle.
        try
        {
            if (DwmSetWindowAttribute(handle, 17, ref hostBrush, sizeof(int)) != 0)
            {
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
                try
                {
                    Marshal.StructureToPtr(new Accent { State = 5 }, pointer, false);
                    var data = new Data { Attribute = 19, DataPointer = pointer, Size = Marshal.SizeOf<Accent>() };
                    if (SetWindowCompositionAttribute(handle, ref data) == 0) throw new NotSupportedException("系统没有实时背景合成支持");
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            window.EnsureLiveBackdrop(); window.BackdropMode = window.SkipsDesktopBlur ? "纯色不透明（跳过桌面模糊）" : "实时系统毛玻璃";
        }
        catch (Exception e) when (e is COMException or NotSupportedException or EntryPointNotFoundException)
        { window.Root.Background = Ui.Surface; window.BackdropMode = ThemePalette.Current.IsDark ? "暗色兼容背景" : "浅色兼容背景"; }
    }
}
