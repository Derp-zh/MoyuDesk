using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Capture stays with the source button; no OLE data is dropped into Codex.
// Thus a failed/cancelled attempt cannot attach a file, insert text or navigate.
internal sealed class ConversationBindingDrag : IDisposable
{
    private readonly BoardController controller;
    private readonly ColumnSurface? source;
    private readonly EditorSurface? editor;
    private readonly Stage? workingStage;
    private StageConversationPicker? automaticPicker;
    private ConversationBindingWindow.DragState? windowState;
    private bool resolvedTarget;
    private readonly Guid task, stage;
    private readonly Guid? expectedBinding;
    private readonly bool quickCreate;
    private readonly object sourceState;
    private readonly bool previousSuppression;
    private readonly CancellationTokenSource stop = new();
    private readonly ConversationBindingInput? escape;
    private readonly List<ColumnSurface> hidden = [];
    private readonly Window? ghost;
    private readonly TextBlock? feedback;
    private readonly DispatcherTimer? timer;
    private Task<ConversationDropTarget>? probe;
    private bool disposed, completing, leftSource;
    private long nextProbe;
    private Point? feedbackPoint;
    private IntPtr handle;
    internal int HiddenCount => hidden.Count;
    internal bool LeftSource => leftSource;
    internal ConversationBindingDrag(BoardController controller, ColumnSurface source, Guid task, Guid stage, Guid? expectedBinding, string caption)
        : this(controller, source, null, null, task, stage, expectedBinding, caption) { }
    internal ConversationBindingDrag(BoardController controller, EditorSurface editor, Stage stage)
        : this(controller, null, editor, stage, editor.Session.Working.Id, stage.Id, stage.Codex?.BindingId,
            stage.Codex?.ThreadName ?? "未绑定") { }
    internal ConversationBindingDrag(BoardController controller, ColumnSurface source)
        : this(controller, source, null, null, Guid.Empty, Guid.Empty, null, "拖动会话快速新建任务", true) { }
    private ConversationBindingDrag(BoardController controller, ColumnSurface? source, EditorSurface? editor, Stage? workingStage,
        Guid task, Guid stage, Guid? expectedBinding, string caption, bool quickCreate = false)
    {
        this.controller = controller; this.source = source; this.editor = editor; this.workingStage = workingStage;
        this.task = task; this.stage = stage; this.expectedBinding = expectedBinding;
        this.quickCreate = quickCreate;
        sourceState = controller.Engine.State;
        controller.ConversationDrag?.Dispose(); controller.ConversationDrag = this;
        previousSuppression = controller.SuppressCollapse; controller.SuppressCollapse = true;
        try
        {
            windowState = controller.BindingWindow.BeginDrag(keepOpen: true);
            controller.TraceBindingGesture("drag-snapshot previous=" + (windowState.Previous is not null));
            if (controller.Engine.State.Preferences.AutoOpenConversationBindingWindow)
            {
                var host = (SurfaceWindow?)source ?? editor?.Host;
                if (host is not null)
                {
                    if (editor is not null && editor.StagePickers.TryGetValue(stage, out var picker)) automaticPicker = picker;
                    else if (controller.Engine.State.Tasks.FirstOrDefault(x => x.Id == task)?.Stages.FirstOrDefault(x => x.Id == stage) is { } live)
                        automaticPicker = new StageConversationPicker(controller, Copies.Of(live), () => { }, false);
                    else if (quickCreate) automaticPicker = new StageConversationPicker(controller, new Stage(), () => { }, false);
                    if (automaticPicker is not null)
                    {
                        controller.BindingWindow.Open(automaticPicker, host, true, editor is null,
                            reload: !ReferenceEquals(windowState.Previous?.Picker, automaticPicker) || !automaticPicker.HasCatalog);
                        controller.BindingWindow.PreserveSearchDuringDrag(windowState, automaticPicker);
                    }
                }
            }
            if (controller.Smoke) return; // Window state is testable without cursor/input probes.
            escape = new ConversationBindingInput(observeMouse: false, includeInjectedEscape: true);
            var pane = new StackPanel();
            pane.Children.Add(new TextBlock { Text = caption, Foreground = Ui.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 });
            feedback = new TextBlock { Text = "拖到绑定窗口会话行，或原 Codex / DSH 会话 · Esc 取消", Foreground = Ui.Muted, FontSize = 11, MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
            pane.Children.Add(feedback);
            ghost = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                IsHitTestVisible = false, SizeToContent = SizeToContent.WidthAndHeight, Left = -12000, Top = -12000,
                Title = "拖动绑定会话 · 名称预览",
                Content = new Border { Background = Ui.HintBackground, BorderBrush = Ui.Accent, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Child = pane } };
            ghost.SourceInitialized += (_, _) =>
            {
                handle = new WindowInteropHelper(ghost).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
            };
            ghost.Show();
            timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += Tick; timer.Start(); Tick(null, EventArgs.Empty);
        }
        catch { Dispose(); throw; }
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (disposed || completing) return;
        if (escape?.Token.IsCancellationRequested == true) { Dispose(); return; }
        var cursor = System.Windows.Forms.Cursor.Position; var point = new Point(cursor.X, cursor.Y);
        controller.BindingWindow.Hover(point);
        MoveTo(point);
        if (feedbackPoint is not { } previous || (point - previous).Length > 12)
        {
            feedbackPoint = point;
            if (feedback is not null) { feedback.Text = "正在识别目标会话…"; feedback.Foreground = Ui.Muted; }
        }
        if (handle != IntPtr.Zero && ghost is not null)
            Native.SetWindowPos(handle, new IntPtr(-1), cursor.X + 18, cursor.Y + 22, 0, 0, 0x0011);
        if (controller.BindingWindow.DropTarget(point) is { } local) { PresentPreview(local); return; }
        if (!leftSource || probe is { IsCompleted: false } || Environment.TickCount64 < nextProbe) return;
        nextProbe = Environment.TickCount64 + 250;
        _ = Preview(point);
    }
    private async Task Preview(Point point)
    {
        try
        {
            var local = controller.BindingWindow.DropTarget(point);
            var request = probe = local is null ? ReadDesktopTarget(point, false, stop.Token) : Task.FromResult(local);
            var target = await request;
            var cursor = System.Windows.Forms.Cursor.Position;
            if (disposed || completing || feedback is null || (new Point(cursor.X, cursor.Y) - point).Length > 12
                || controller.BindingWindow.DropTarget(new Point(cursor.X, cursor.Y)) is not null) return;
            PresentPreview(target);
        }
        catch { if (!disposed && feedback is not null) feedback.Text = "读取失败，松开不会绑定"; }
    }
    private void PresentPreview(ConversationDropTarget target)
    {
        if (feedback is null) return;
        if (target.IsUnbind)
        {
            feedback.Text = target.Reason;
            feedback.Foreground = Ui.Accent;
            return;
        }
        var available = target.CanBind || target.CanResolveOnDrop;
        feedback.Text = available ? "松开绑定 · " + AiState.Name(target.Platform) + " · " + target.Name
            + (target.ThreadId is { } id ? "\n" + id : "") : target.Reason;
        feedback.Foreground = available ? Ui.Accent : Ui.Muted;
    }
    internal void MoveTo(Point point)
    {
        if (disposed || leftSource || ContainsSource(point)) return;
        leftSource = true;
        foreach (var column in controller.Columns.Where(x => x != source && x.IsVisible))
        { hidden.Add(column); column.Hide(); }
    }
    private bool ContainsSource(Point point) => ((SurfaceWindow?)source ?? editor?.Host) is { } host && Contains(host, point);
    private static bool Contains(SurfaceWindow column, Point point)
    {
        try
        {
            if (!column.IsVisible) return false;
            return new Rect(column.PointToScreen(new Point()), column.PointToScreen(new Point(column.ActualWidth, column.ActualHeight))).Contains(point);
        }
        catch (InvalidOperationException) { return false; }
    }
    internal async Task Complete(Point point, Func<Point, Task<ConversationDropTarget>>? readForCheck = null)
    {
        if (disposed || completing) return;
        if (escape?.Token.IsCancellationRequested == true) { Dispose(); return; }
        completing = true; timer?.Stop();
        escape?.Dispose();
        ghost?.Hide();
        var target = ConversationDropTarget.Unavailable("还没有拖出当前任务卡片窗口，原绑定已为您保留；请拖到右侧绑定窗口或 Codex / DSH 会话后再松手");
        var cancelled = false;
        try
        {
            var local = controller.Smoke ? null : controller.BindingWindow.DropTarget(point);
            if (leftSource && (!ContainsSource(point) || local is not null) && (!controller.Smoke || readForCheck is not null))
            {
                // One fresh release lookup, retained through the directed menu.
                // Hover IDs/anchors never commit and are not looked up again after activation.
                if (local is not null) target = local;
                else if (readForCheck is not null) target = await readForCheck(point);
                else
                {
                    using var input = new ConversationBindingInput();
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, input.Token);
                    try { target = await ReadDesktopTarget(point, true, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(6)); }
                    finally { cancellation.Cancel(); }
                }
            }
        }
        catch (Exception) { target = ConversationDropTarget.Unavailable("读取会话等待超时或未能完成（可能客户端正在忙或正在加载），原绑定已为您保留。建议稍后再拖一次，或直接在右侧绑定窗口点击选择"); }
        finally { resolvedTarget = target.CanBind || target.IsUnbind; cancelled = disposed || !ReferenceEquals(controller.ConversationDrag, this); Dispose(); }
        if (cancelled) return;
        var successful = false; string message;
        try
        {
            message = quickCreate ? await controller.CreateFromConversation(target, sourceState) : editor is not null && workingStage is not null
                ? ReferenceEquals(sourceState, controller.Engine.State) && controller.Editors.Contains(editor)
                    ? editor.ApplyConversationDrop(workingStage, expectedBinding, target) : "未绑定：任务数据已变化，请重新打开后再试"
                : await controller.CompleteConversationBinding(task, stage, expectedBinding, target, sourceState);
            successful = message.StartsWith("已绑定", StringComparison.Ordinal) || message.StartsWith("已选择", StringComparison.Ordinal) || message.StartsWith("已解除绑定", StringComparison.Ordinal);
        }
        finally { if (!successful && windowState is not null) controller.BindingWindow.RestorePrevious(windowState); }
        var hintRoot = source?.Root ?? editor?.Root;
        if (hintRoot?.IsVisible == true) Ui.Hint(hintRoot, message, !controller.Smoke, hintRoot);
    }
    private async Task<ConversationDropTarget> ReadDesktopTarget(Point point, bool resolve, CancellationToken cancellation)
    {
        var configuration = new Dictionary<string, string>(controller.Engine.State.Preferences.ConnectionConfigs);
        var desktop = configuration.GetValueOrDefault("dsh.desktop", "");
        if (!DshConversationTarget.IsAt(point, desktop)) return resolve ? await CodexConversationMenu.Resolve(point, cancellation) : await CodexConversationMenu.Read(point);
        var target = resolve ? await ResolveDshTarget(point, cancellation, desktop) : await DshConversationTarget.Read(point, desktop);
        if (resolve && target.CanBind)
        {
            cancellation.ThrowIfCancellationRequested();
            var catalog = await controller.LoadPlatformConversations(AiPlatform.Dsh);
            cancellation.ThrowIfCancellationRequested();
            if (!AiPlatformOptions.SameConfiguration(AiPlatform.Dsh, configuration, controller.Engine.State.Preferences.ConnectionConfigs))
                return ConversationDropTarget.Unavailable("DSH监听配置已变化，请重新拖动");
            var exact = catalog.FirstOrDefault(x => x.Id == target.ThreadId);
            if (exact is null) return ConversationDropTarget.Unavailable("原 DSH 已返回编号，但本机会话记录尚不可读；请先初始化 DSH 监听后重试");
            target = target with { Name = exact.DisplayName };
        }
        return target;
    }
    private async Task<ConversationDropTarget> ResolveDshTarget(Point point, CancellationToken cancellation, string desktop)
    {
        // DSH only exposes its row menu while the row is physically hovered.
        // Temporarily cloak our two possible overlays without hiding the WPF
        // windows (which would close the picker or discard the editor context).
        var overlays = new[] { (SurfaceWindow?)source ?? editor?.Host, controller.BindingWindow.Surface }
            .Where(x => x is { IsVisible: true }).Distinct().Cast<SurfaceWindow>()
            .Select(x => (Window: x, Handle: new WindowInteropHelper(x).Handle)).ToArray();
        var cloaked = new List<(SurfaceWindow Window, IntPtr Handle)>();
        var foreground = Native.GetForegroundWindow();
        try
        {
            foreach (var overlay in overlays)
            {
                if (!Native.Cloak(overlay.Handle, true)) return ConversationDropTarget.Unavailable("本次无法让开 DSH 会话菜单，请移动看板窗口后重新拖动");
                cloaked.Add(overlay);
            }
            return await DshConversationTarget.Resolve(point, cancellation, desktop).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        }
        finally
        {
            var restoreFocus = !cancellation.IsCancellationRequested && DshConversationTarget.IsForeground(desktop);
            foreach (var overlay in cloaked)
                if (new WindowInteropHelper(overlay.Window).Handle == overlay.Handle) Native.Cloak(overlay.Handle, false);
            if (restoreFocus && cloaked.Any(x => x.Handle == foreground && x.Window.IsVisible)) CodexConversationMenu.Foreground(foreground);
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; stop.Cancel(); escape?.Dispose(); timer?.Stop(); ghost?.Close();
        if (windowState is not null) controller.BindingWindow.FinishDrag(windowState, resolvedTarget);
        // Hook/raw presses are delivered asynchronously. A cancelling right
        // press must not fold the restored workspace after this gesture ends.
        controller.FinishConversationInput();
        if (ReferenceEquals(controller.ConversationDrag, this)) controller.ConversationDrag = null;
        controller.SuppressCollapse = previousSuppression;
        foreach (var column in hidden.Where(x => controller.Expanded && x.Definition.Visible && !x.Definition.Deleted && controller.Columns.Contains(x)))
        { column.Show(); column.RestartCardMarquees(); }
        hidden.Clear();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
