using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class Batch2UiChecks
{
    internal static int Run(string output, Func<Task>? afterRun = null)
    {
        Directory.CreateDirectory(output);
        Exception? failure = null;
        string? summary = null;
        var thread = new Thread(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var theme = new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) };
            app.Resources.MergedDictionaries.Add(theme);
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    summary = await RunOnStaAsync(output);
                    if (afterRun is not null) await afterRun();
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Batch2UiChecks failed: " + failure.Message, failure);
        Console.WriteLine(summary);
        return 0;
    }

    private static async Task<string> RunOnStaAsync(string output)
    {
        var hostParams = new HwndSourceParameters("Batch2OffscreenHost")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0x08000000,
            PositionX = -20000,
            PositionY = -20000,
            Width = 820,
            Height = 760
        };
        using var host = new HwndSource(hostParams);
        var stageHost = new Grid { Width = 820, Height = 760 };
        host.RootVisual = stageHost;

        var checks = new List<string>();
        void Assert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("失败：" + name);
            checks.Add(name);
        }

        // #06: Onboarding reset tool (without touching tasks or preferences)
        var deviceDir = Path.Combine(output, "device-state");
        var onboardingDevice = new DeviceOnboarding(deviceDir, "machine-batch2-test");
        Assert(onboardingDevice.Claim(false).Show, "#06 首次Claim触发引导");
        Assert(!onboardingDevice.Claim(false).Show, "#06 第二次Claim不再重复触发");
        var boardDir = Path.Combine(output, "board");
        var controller = new BoardController(boardDir, isolated: true, smoke: true, onboardingDevice);
        try
        {
            var taskSession = controller.Engine.NewSession(Lane.Running);
            taskSession.Working.Title = "不被重置破坏的任务";
            taskSession.Working.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Daily, Time = TimeSpan.FromHours(8), NextLocal = DateTime.Today.AddHours(8) };
            var savedTask = controller.Engine.Submit(taskSession);
            controller.Engine.State.Preferences.GlassTransparency = 35;

            var settings = new SettingsSurface(controller);
            Layout(settings.Root, 760, 680);
            var resetBtn = Descendants<Button>(settings.Root).Single(x => Equals(x.Tag, "ResetOnboarding"));
            resetBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(controller.Engine.State.Tasks.Single().Title == "不被重置破坏的任务" &&
                   controller.Engine.State.Preferences.GlassTransparency == 35, "#06 重置新手指引不破坏任务与偏好数据");
            Assert(onboardingDevice.Claim(false).Show, "#06 点击重置按钮后本机首次引导标记已恢复，可再次触发");
            Assert(controller.OnboardingForVerification is not null, "#06 重置同时准备就绪新手指引界面");
            settings.Destroy(false);

            // #22: Card timer reminder red dot hover preview + "关闭提醒" bubble button (Plan A)
            var dueIds = ReminderClock.Tick(controller.Engine.State, DateTime.Today.AddHours(9));
            Assert(dueIds.Contains(savedTask.Id) && savedTask.TimerUnread && savedTask.Lane == Lane.Action, "#22 真实ReminderClock到期进入待操作并点亮红点");
            controller.Engine.Notify([savedTask.Id]);
            var actionCol = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Action);
            Layout(actionCol.Root, 260, 600);
            var card = actionCol.RenderedCard(savedTask.Id);
            Assert(card is not null, "#22 待操作列已渲染带提醒红点的任务卡片");
            var timerDot = Descendants<FrameworkElement>(card!).FirstOrDefault(x => Equals(x.Tag, "TaskTimerDot"));
            Assert(timerDot is not null, "#22 卡片显示可交互的提醒红点区域");
            var popup = card!.OpenReminderPreviewForVerification();
            Assert(popup is not null, "#22 卡片红点可打开提醒预览气泡框");
            var dismissBtn = Descendants<Button>(popup!.Child).Single(x => Equals(x.Tag, "DismissReminderBubble"));
            dismissBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && (savedTask.TimerUnread || savedTask.Reminder.Enabled); i++) await Task.Delay(20);
            Layout(actionCol.Root, 260, 600);
            Assert(!savedTask.TimerUnread && !savedTask.PendingReasons.Contains("定时提醒到期") && !savedTask.Reminder.Enabled, "#22 点击气泡“关闭提醒”后清除红点、待处理原因并关闭定时提醒");
            Assert(!Descendants<FrameworkElement>(actionCol.RenderedCard(savedTask.Id)!).Any(x => Equals(x.Tag, "TaskTimerDot")), "#22 刷新卡片后红点已消失");
            var runningCol = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Running);

            // #23: Unbound task card hides "Jump to conversation" button; bound card shows it
            var unboundSession = controller.Engine.NewSession(Lane.Running);
            unboundSession.Working.Title = "未绑定卡片";
            unboundSession.Working.Stages[0].Text = "这是一段用于测试未绑定会话时隐藏跳转按钮的长文本";
            var unboundTask = controller.Engine.Submit(unboundSession);
            Layout(runningCol.Root, 260, 600);
            var unboundCard = runningCol.RenderedCard(unboundTask.Id)!;
            Assert(!Descendants<ConversationJumpButton>(unboundCard).Any(), "#23 未绑定会话的任务卡片不显示跳转到会话按钮");
            Assert(!Descendants<FrameworkElement>(unboundCard).Any(x => Equals(x.Tag, "ConversationJumpHitTarget")), "#23 未绑定会话的任务卡片无跳转命中占位区");
            var unboundDetails = Descendants<StageDetailsText>(unboundCard).Single();
            Assert(unboundDetails.LineWidths.All(w => Math.Abs(w - unboundDetails.ActualWidth) < 0.5), "#23 未绑定时详情文本末行保留完整宽度不留空白");

            var boundId = Guid.NewGuid().ToString("D");
            Assert(controller.Engine.BindStageConversation(unboundTask.Id, unboundTask.Stages[0].Id, null, boundId, "已绑会话", AiPlatform.Codex), "#23 绑定会话成功");
            Layout(runningCol.Root, 260, 600);
            var nowBoundCard = runningCol.RenderedCard(unboundTask.Id)!;
            Assert(Descendants<ConversationJumpButton>(nowBoundCard).Count() == 1, "#23 绑定会话后任务卡片恢复显示跳转到会话按钮");

            // #09: Dragging onto "解除绑定" button area in StageConversationPicker
            var editSession = controller.Engine.Edit(unboundTask.Id);
            var editor = new EditorSurface(controller, editSession);
            stageHost.Children.Add(editor);
            Layout(editor, 430, EditorSurface.DefaultHeight);
            var editStage = editSession.Working.Stages[0];
            var picker = editor.StagePickers[editStage.Id];
            var pickerHost = new Border { Width = 268, Height = 560, Background = Ui.Surface, Child = picker.Pane };
            stageHost.Children.Add(pickerHost);
            Layout(pickerHost, 268, 560);
            var unlinkBtn = picker.UnlinkButton;
            var unlinkCenter = unlinkBtn.PointToScreen(new Point(unlinkBtn.ActualWidth / 2, unlinkBtn.ActualHeight / 2));
            var dropUnbind = ConversationBindingWindow.DropTargetInPicker(picker, unlinkCenter);
            Assert(dropUnbind.IsUnbind && !dropUnbind.CanBind, "#09 拖到已绑定阶段专属窗口的解除绑定区域返回有效解绑目标");
            picker.SetUnlinkHover(true);
            Assert(Equals(unlinkBtn.BorderBrush, Ui.Accent), "#09 悬停解除绑定区域呈现高亮反馈");
            picker.SetUnlinkHover(false);
            var editorDropMsg = editor.ApplyConversationDrop(editStage, editStage.Codex!.BindingId, dropUnbind);
            Assert(editorDropMsg.StartsWith("已解除绑定", StringComparison.Ordinal) && editStage.Codex is null, "#09 编辑窗口内拖放到解除绑定区域成功清空副本绑定");
            var dropAlreadyUnbound = ConversationBindingWindow.DropTargetInPicker(picker, unlinkCenter);
            Assert(!dropAlreadyUnbound.IsUnbind && !dropAlreadyUnbound.CanBind && dropAlreadyUnbound.Reason.Contains("尚未绑定"), "#09 对未绑定阶段拖到解除绑定区域给出明确提示且不生效");
            var listDropMsg = await controller.CompleteConversationBinding(unboundTask.Id, unboundTask.Stages[0].Id, unboundTask.Stages[0].Codex!.BindingId, dropUnbind);
            Assert(listDropMsg == "已解除绑定" && unboundTask.Stages[0].Codex is null, "#09 列表卡片拖放到解除绑定区域直接解除并保存");
            var quickCreateMsg = await controller.CreateFromConversation(dropUnbind);
            Assert(quickCreateMsg.StartsWith("未新建", StringComparison.Ordinal), "#09 +AI快速新建拖到解除绑定区域不会创建空任务");
            stageHost.Children.Remove(pickerHost);
            pickerHost.Child = null;
            stageHost.Children.Remove(editor);
            editor.Destroy();

            // #24: Bottom-left WorkspaceResizeGrip verification + Shift translate workspace
            var visible = controller.VisibleColumns;
            Assert(visible.Length >= 2, "#24 常规模式至少存在两列");
            var firstGrip = Descendants<Thumb>(visible[0].Root).Single(x => Equals(x.Tag, "WorkspaceResizeGrip"));
            var secondGrip = Descendants<Thumb>(visible[1].Root).Single(x => Equals(x.Tag, "WorkspaceResizeGrip"));
            Assert(firstGrip.Visibility == Visibility.Visible && secondGrip.Visibility == Visibility.Collapsed, "#24 左下角最大范围入口仅在第一可见列显示");
            Assert(firstGrip.Width == 22 && firstGrip.Height == 22 && firstGrip.Margin == new Thickness(6), "#24 左下角入口尺寸22×22与边距6留在圆角裁切内");
            firstGrip.ApplyTemplate();
            Layout(visible[0].Root, visible[0].Width, visible[0].Height);
            var colRoot = visible[0].Root;
            var gripRect = new Rect(firstGrip.TranslatePoint(new Point(), colRoot), firstGrip.RenderSize);
            var roundedClip = new RectangleGeometry(new Rect(0, 0, visible[0].Width, visible[0].Height), 14, 14);
            Assert(Math.Abs(gripRect.Width - 22) < .5 && Math.Abs(gripRect.Height - 22) < .5
                && Math.Abs(gripRect.Left - 6) < .5 && Math.Abs(visible[0].Height - gripRect.Bottom - 6) < .5,
                "#24 左下角最大范围入口准确位于左下角Margin(6)且尺寸为22×22");
            foreach (var p in new[] { new Point(2, 2), new Point(20, 2), new Point(2, 20), new Point(20, 20), new Point(11, 11) })
            {
                var pt = new Point(gripRect.Left + p.X, gripRect.Top + p.Y);
                Assert(roundedClip.FillContains(pt), $"#24 左下角入口区域点 {p} 落在圆角裁切内部");
            }
            var gripIdx = colRoot.Children.IndexOf(firstGrip);
            Assert(gripIdx == colRoot.Children.Count - 1 && Grid.GetRowSpan(firstGrip) == 3, "#24 左下角入口位于列顶层且跨全部三行无遮挡");
            var gripTemplateBorder = Descendants<Border>(firstGrip).FirstOrDefault();
            Assert(gripTemplateBorder?.Background is SolidColorBrush { Color.A: > 0 }, "#24 左下角入口整块底板使用非零Alpha画刷保证透明命中");

            // Normal mode Shift translate
            controller.BeginWorkspaceResize();
            var beforeTranslate = visible.Select(x => x.Position()).ToArray();
            controller.MoveWorkspace(45, 30);
            controller.EndWorkspaceResize();
            var afterTranslate = visible.Select(x => x.Position()).ToArray();
            Assert(visible.Select((x, i) => Math.Abs((afterTranslate[i].X - beforeTranslate[i].X) - 45) < 1.5
                && Math.Abs((afterTranslate[i].Y - beforeTranslate[i].Y) - 30) < 1.5
                && Math.Abs(afterTranslate[i].Width - beforeTranslate[i].Width) < 0.5
                && Math.Abs(afterTranslate[i].Height - beforeTranslate[i].Height) < 0.5).All(ok => ok),
                "#24 Shift整体平移常规六列位置且保持各列宽高不变");
            await controller.Flush();
            var savedFirst = controller.Store.Load().Preferences.Positions[visible[0].Definition.PositionKey];
            Assert(Math.Abs(savedFirst.X - afterTranslate[0].X) < 1, "#24 平移后的最大范围坐标持久化保存");

            // Clamp check inside work area
            controller.BeginWorkspaceResize();
            controller.MoveWorkspace(50000, 50000);
            controller.EndWorkspaceResize();
            var clampedRange = controller.MaximumRange;
            var area = controller.WorkArea(controller.Ball);
            Assert(clampedRange.Width <= area.Width + 2 && clampedRange.Right <= -10000 + area.Width + 2, "#24 Shift平移受工作区边界限制不会拖出屏幕");

            // Focus mode Shift translate
            controller.RestoreDefaults();
            controller.SetFocusMode(true);
            var focusVisible = controller.VisibleColumns;
            var focusBefore = focusVisible.Select(x => x.Position()).ToArray();
            controller.BeginWorkspaceResize();
            controller.MoveWorkspace(30, 20);
            controller.EndWorkspaceResize();
            var focusAfter = focusVisible.Select(x => x.Position()).ToArray();
            Assert(focusVisible.Select((x, i) => Math.Abs((focusAfter[i].X - focusBefore[i].X) - 30) < 1.5
                && Math.Abs((focusAfter[i].Y - focusBefore[i].Y) - 20) < 1.5).All(ok => ok),
                "#24 专注模式下Shift整体平移同样同步移动双宽窗");
        }
        finally
        {
            controller.Dispose();
        }

        var report = new { passed = checks.Count, checks, offscreen = true, visibleWindows = 0, mouseMoved = false };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "batch2-ui-report.json"), json);
        return JsonSerializer.Serialize(new { passed = checks.Count, offscreen = true, visibleWindows = 0 });
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
