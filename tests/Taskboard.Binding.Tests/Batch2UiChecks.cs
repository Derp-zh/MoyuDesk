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

        // #06: First-use automatic onboarding intact + "重新体验新手指引" replays without resetting device marker
        var deviceDir = Path.Combine(output, "device-state");
        var onboardingDevice = new DeviceOnboarding(deviceDir, "machine-batch2-test");
        Assert(onboardingDevice.Claim(false).Show, "#06 首次Claim触发引导");
        Assert(!onboardingDevice.Claim(false).Show, "#06 第二次Claim不再重复触发");
        var boardDir = Path.Combine(output, "board");
        var controller = new BoardController(boardDir, isolated: true, smoke: true, onboardingDevice) { OffscreenNoShow = true };
        try
        {
            var taskSession = controller.Engine.NewSession(Lane.Paused);
            taskSession.Working.Title = "暂搁置定时提醒任务";
            taskSession.Working.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Daily, Time = TimeSpan.FromHours(8), NextLocal = DateTime.Today.AddHours(8) };
            var savedTask = controller.Engine.Submit(taskSession);
            controller.Engine.State.Preferences.GlassTransparency = 35;

            var settings = new SettingsSurface(controller);
            Layout(settings.Root, 760, 680);
            Assert(!Descendants<Button>(settings.Root).Any(x => Equals(x.Tag, "ResetOnboarding")), "#06 已移除“重置新手指引”按钮");
            var replayBtn = Descendants<Button>(settings.Root).Single(x => Equals(x.Tag, "ShowOnboarding"));
            Assert(Equals(replayBtn.Content, "重新体验新手指引"), "#06 常规设置页提供“重新体验新手指引”按钮");
            replayBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(controller.Engine.State.Tasks.Single().Title == "暂搁置定时提醒任务" &&
                   controller.Engine.State.Preferences.GlassTransparency == 35, "#06 重新体验新手指引不破坏任务与偏好数据");
            Assert(!onboardingDevice.Claim(false).Show, "#06 重新体验新手指引不擦除本机首次使用标记");
            Assert(controller.OnboardingForVerification is not null, "#06 点击重新体验按钮打开完整新手指引流程");
            settings.Destroy(false);

            // #04: ReminderEditor whole-row toggle + default alignment to current date & next whole hour + independent hour/minute lists + preserve existing enabled schedule
            var remClockNow = new DateTime(2026, 11, 27, 3, 40, 0);
            var remDraft = new Reminder { Enabled = false, Repeat = Repeat.Once, Time = new TimeSpan(9, 15, 0), NextLocal = new DateTime(2026, 11, 20, 9, 15, 0), Weekday = DayOfWeek.Monday };
            var remChangedCount = 0;
            var remEditor = new ReminderEditor(remDraft, () => remChangedCount++, () => remClockNow);
            stageHost.Children.Add(remEditor);
            Layout(remEditor, 390, 420);
            var remToggle = Descendants<CheckBox>(remEditor).Single(x => Equals(x.Tag, "ReminderToggle"));
            var remDateInput = Descendants<TextBox>(remEditor).Single(x => Equals(x.Tag, "ReminderDate"));
            var remTimeInput = Descendants<TextBox>(remEditor).Single(x => Equals(x.Tag, "ReminderTime"));
            var remWeekdayBox = Descendants<ComboBox>(remEditor).Single(x => Equals(x.Tag, "ReminderWeekday"));
            Assert(Equals(remToggle.Content, "定时提醒") && remToggle.HorizontalAlignment == HorizontalAlignment.Stretch,
                "#04 定时提醒开关包含标题文字整行可点");
            Assert(remDateInput.Text == "2026/11/27" && remTimeInput.Text == "04:00" && remWeekdayBox.SelectedIndex == (int)DayOfWeek.Friday,
                $"#04 未启用定时提醒初始化对齐当日下一个小时整点(date={remDateInput.Text}, time={remTimeInput.Text})");

            // Advance clock to an exact hour before clicking enable -> aligns to real click-time next whole hour (14:00 -> 15:00)
            remClockNow = new DateTime(2026, 11, 27, 14, 0, 0);
            remToggle.IsChecked = true;
            remToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(remDraft.Enabled && remDateInput.Text == "2026/11/27" && remTimeInput.Text == "15:00"
                && remDraft.NextLocal == new DateTime(2026, 11, 27, 15, 0, 0) && remDraft.Time == new TimeSpan(15, 0, 0),
                $"#04 从未勾选切勾选启用时实时按点击时刻对齐下一个整点(14:00->15:00, actual={remDateInput.Text} {remTimeInput.Text})");

            // Turn off, advance across midnight (23:40 -> next day 00:00), then turn on again
            remToggle.IsChecked = false;
            remToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            remClockNow = new DateTime(2026, 11, 27, 23, 40, 0);
            remToggle.IsChecked = true;
            remToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(remDraft.Enabled && remDateInput.Text == "2026/11/28" && remTimeInput.Text == "00:00"
                && remDraft.NextLocal == new DateTime(2026, 11, 28, 0, 0, 0) && remWeekdayBox.SelectedIndex == (int)DayOfWeek.Saturday,
                $"#04 跨午夜勾选启用自动对齐次日00:00与次日星期(actual={remDateInput.Text} {remTimeInput.Text})");

            var timePicker = remEditor.BuildTimePickerContent();
            stageHost.Children.Add(timePicker);
            Layout(timePicker, 220, 260);
            var hourList = Descendants<ListBox>(timePicker).Single(x => Equals(x.Tag, "ReminderHourList"));
            var minuteList = Descendants<ListBox>(timePicker).Single(x => Equals(x.Tag, "ReminderMinuteList"));
            Assert(hourList.Items.Count == 24 && minuteList.Items.Count == 60, "#04 时间选择器提供独立的24小时和60分钟滚动列表");
            hourList.SelectedIndex = 16;
            minuteList.SelectedIndex = 42;
            Assert(remTimeInput.Text == "16:42" && remDraft.Time == new TimeSpan(16, 42, 0), "#04 滚轮列表点选小时/分钟后即时更新生效无需二步确定");
            stageHost.Children.Remove(timePicker);
            stageHost.Children.Remove(remEditor);

            // Already-enabled reminder must preserve its saved date, time, and weekday when opening editor
            var existingRem = new Reminder { Enabled = true, Repeat = Repeat.Once, Time = new TimeSpan(18, 30, 0), NextLocal = new DateTime(2026, 12, 5, 18, 30, 0), Weekday = DayOfWeek.Saturday };
            var existingRemEditor = new ReminderEditor(existingRem, () => { }, () => new DateTime(2026, 11, 27, 3, 40, 0));
            stageHost.Children.Add(existingRemEditor);
            Layout(existingRemEditor, 390, 420);
            var existDate = Descendants<TextBox>(existingRemEditor).Single(x => Equals(x.Tag, "ReminderDate"));
            var existTime = Descendants<TextBox>(existingRemEditor).Single(x => Equals(x.Tag, "ReminderTime"));
            var existWeek = Descendants<ComboBox>(existingRemEditor).Single(x => Equals(x.Tag, "ReminderWeekday"));
            Assert(existDate.Text == "2026/12/05" && existTime.Text == "18:30" && existWeek.SelectedIndex == (int)DayOfWeek.Saturday
                && existingRem.NextLocal == new DateTime(2026, 12, 5, 18, 30, 0),
                "#04 已启用定时提醒打开编辑窗口严格保留原保存日期、时间与星期不覆盖");
            stageHost.Children.Remove(existingRemEditor);

            // #04 + #22: Due reminder stays in Paused (does NOT move to Action), click/edit/locate do NOT clear dot, compact popup + hover corridor polling + dismiss
            var dueIds = controller.TickReminders(DateTime.Today.AddHours(9));
            Assert(dueIds.Contains(savedTask.Id) && savedTask.TimerUnread && savedTask.Lane == Lane.Paused,
                "#04 真实ReminderClock到期后卡片留在原暂搁置列不动仅点亮红点");
            controller.Locate(savedTask.Id);
            controller.SetExpanded(false);
            var editCheck = controller.Engine.Edit(savedTask.Id);
            Assert(savedTask.TimerUnread, "#22 Locate与打开编辑均不误消提醒红点");
            controller.Engine.Discard(editCheck);

            var pausedCol = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Paused);
            Layout(pausedCol.Root, 260, 600);
            var card = pausedCol.RenderedCard(savedTask.Id);
            Assert(card is not null, "#04/#22 暂搁置列原位渲染带提醒红点的任务卡片");
            var timerDot = Descendants<FrameworkElement>(card!).FirstOrDefault(x => Equals(x.Tag, "TaskTimerDot"));
            Assert(timerDot is not null, "#22 卡片显示可交互的提醒红点区域");
            var popup = card!.OpenReminderPreviewForVerification();
            Assert(popup is not null, "#22 卡片红点可打开提醒预览气泡框");
            var previewTime = Descendants<TextBlock>(popup!.Child).Single(x => Equals(x.Tag, "ReminderPreviewTime"));
            Assert(previewTime.Text == "每天 08:00", $"#22 悬停预览窗紧凑显示提醒时间(实际={previewTime.Text})");
            var anchorRect = new Rect(100, 100, 16, 16);
            var popupRect = new Rect(60, 120, 110, 56);
            Assert(card.PollReminderHoverForVerification(new Point(108, 108), 1000, anchorRect, popupRect), "#22 指针在红点靶区内保持气泡打开");
            Assert(card.PollReminderHoverForVerification(new Point(108, 118), 1080, anchorRect, popupRect), "#22 指针在红点与气泡过渡通道内保持打开");
            Assert(card.PollReminderHoverForVerification(new Point(80, 140), 1160, anchorRect, popupRect), "#22 指针在气泡框内保持打开");
            Assert(card.PollReminderHoverForVerification(new Point(300, 300), 1240, anchorRect, popupRect), "#22 指针移出后进入极短防抖延迟");
            Assert(!card.PollReminderHoverForVerification(new Point(300, 300), 1370, anchorRect, popupRect) && !card.IsReminderPreviewOpenForVerification,
                "#22 指针移出超过120ms后轮询强制关闭气泡彻底根除常驻关不了Bug");

            // Reopen and click "关闭提醒"
            popup = card.OpenReminderPreviewForVerification();
            var dismissBtn = Descendants<Button>(popup!.Child).Single(x => Equals(x.Tag, "DismissReminderBubble"));
            dismissBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && (savedTask.TimerUnread || savedTask.Reminder.Enabled); i++) await Task.Delay(20);
            Layout(pausedCol.Root, 260, 600);
            Assert(!savedTask.TimerUnread && !savedTask.PendingReasons.Contains("定时提醒到期") && !savedTask.Reminder.Enabled, "#22 点击气泡“关闭提醒”后清除红点、待处理原因并关闭定时提醒");
            Assert(!Descendants<FrameworkElement>(pausedCol.RenderedCard(savedTask.Id)!).Any(x => Equals(x.Tag, "TaskTimerDot")), "#22 刷新卡片后红点已消失");
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
            controller.SetFocusMode(false);

            // #26 Plan A: Left-click jump to conversation clears orange-red attention on success (with #12 same-session sync) and keeps card in Action; failure keeps attention
            var sharedJumpThread = Guid.NewGuid().ToString("D");
            var j1 = controller.Engine.NewSession(Lane.Action);
            j1.Working.Title = "跳转消红测试1";
            j1.Working.Stages[0].Text = "测试跳转消红";
            j1.Working.Stages[0].Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = sharedJumpThread, ThreadName = "同会话跳转" };
            var jumpTask1 = controller.Engine.Submit(j1);
            var j2 = controller.Engine.NewSession(Lane.Action);
            j2.Working.Title = "跳转消红测试2";
            j2.Working.Stages[0].Text = "测试同会话联动消红";
            j2.Working.Stages[0].Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = sharedJumpThread, ThreadName = "同会话跳转" };
            var jumpTask2 = controller.Engine.Submit(j2);
            var askEvent = JsonSerializer.SerializeToElement(new { method = "item/tool/requestUserInput", id = "jump-req-1", @params = new { threadId = sharedJumpThread, turnId = "turn-jump" } });
            CodexState.ApplySource(jumpTask1, jumpTask1.Stages[0].Id, jumpTask1.Stages[0].Codex!.BindingId, askEvent, 1, jumpTask1.Stages[0].Codex!.Generation, "ep-j", true);
            CodexState.ApplySource(jumpTask2, jumpTask2.Stages[0].Id, jumpTask2.Stages[0].Codex!.BindingId, askEvent, 1, jumpTask2.Stages[0].Codex!.Generation, "ep-j", true);
            controller.Engine.Notify([jumpTask1.Id, jumpTask2.Id]);
            Assert(ConversationJumpButton.Attention(jumpTask1, jumpTask1.Stages[0]) && ConversationJumpButton.Attention(jumpTask2, jumpTask2.Stages[0]),
                "#26 初始两个同会话绑定阶段均亮起橙红提醒");
            var actionCol2 = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Action);
            Layout(actionCol2.Root, 260, 600);
            var jumpCard = actionCol2.RenderedCard(jumpTask1.Id)!;
            var jumpBtn = Descendants<ConversationJumpButton>(jumpCard).Single();
            try
            {
                ConversationJumpButton.OpenOverrideForVerification = (_, _, _, _) => Task.FromResult(new ConversationNavigationResult(false, "模拟跳转失败"));
                jumpBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(40);
                Assert(ConversationJumpButton.Attention(jumpTask1, jumpTask1.Stages[0]) && ConversationJumpButton.Attention(jumpTask2, jumpTask2.Stages[0]),
                    "#26 跳转失败时不误消橙红提醒");

                Layout(actionCol2.Root, 260, 600);
                jumpBtn = Descendants<ConversationJumpButton>(actionCol2.RenderedCard(jumpTask1.Id)!).Single();
                ConversationJumpButton.OpenOverrideForVerification = (_, _, _, _) => Task.FromResult(new ConversationNavigationResult(true, ""));
                jumpBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 40 && ConversationJumpButton.Attention(jumpTask1, jumpTask1.Stages[0]); i++) await Task.Delay(15);
                Assert(!ConversationJumpButton.Attention(jumpTask1, jumpTask1.Stages[0]) && !ConversationJumpButton.Attention(jumpTask2, jumpTask2.Stages[0]),
                    "#26 左键跳转成功后自动消除本阶段及同会话绑定阶段的橙红提醒(#12联动)");
                Assert(jumpTask1.Lane == Lane.Action && jumpTask2.Lane == Lane.Action, "#26 跳转消红后卡片继续留在待操作列不变");
            }
            finally { ConversationJumpButton.OpenOverrideForVerification = null; }

            // #16 Plan A: Unified comfortable fixed wheel step (80 DIP, capped at 25% viewport height)
            Assert(Math.Abs(QuickScroll.ComputeWheelStep(600, 3) - 80) < 0.01, "#16 常规高度列表单格滚轮固定步距80 DIP");
            Assert(Math.Abs(QuickScroll.ComputeWheelStep(200, 3) - 50) < 0.01, "#16 矮视口滚轮步距不超过视口高度25%");
            Assert(QuickScroll.ComputeWheelStep(600, 0) == 0, "#16 系统滚轮行数为0时不滚动");

            // #17: Adaptive proportional ellipsis (default ON) + […]/[▴] bubble toggle + persisted expansion
            Assert(controller.Engine.State.Preferences.EllipsizeStageDetails, "#17 省略多列表阶段详情默认开启");
            Assert(StageDetailsText.ComputeAdaptiveVisibleLines(4) == 4
                && StageDetailsText.ComputeAdaptiveVisibleLines(5) == 3
                && StageDetailsText.ComputeAdaptiveVisibleLines(10) == 4
                && StageDetailsText.ComputeAdaptiveVisibleLines(12) == 5
                && StageDetailsText.ComputeAdaptiveVisibleLines(15) == 6
                && StageDetailsText.ComputeAdaptiveVisibleLines(20) == 8
                && StageDetailsText.ComputeAdaptiveVisibleLines(25) == 10
                && StageDetailsText.ComputeAdaptiveVisibleLines(60) == 10,
                "#17 按文本行数等比例自适应显示行数公式准确(<=4不省略, >4按round(0.4*N)钳位3..10)");

            var shortSess = controller.Engine.NewSession(Lane.Running);
            shortSess.Working.Title = "短阶段不省略";
            shortSess.Working.Stages[0].Text = "第1行\n第2行\n第3行\n第4行";
            var shortTask = controller.Engine.Submit(shortSess);

            var longSess = controller.Engine.NewSession(Lane.Running);
            longSess.Working.Title = "长阶段自适应省略";
            longSess.Working.Stages[0].Text = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"第{i}行详情文本内容测试"));
            longSess.Working.Stages[0].Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = Guid.NewGuid().ToString("D"), ThreadName = "有跳转按钮" };
            var longTask = controller.Engine.Submit(longSess);

            Layout(runningCol.Root, 260, 700);
            var shortCard = runningCol.RenderedCard(shortTask.Id)!;
            var shortText = Descendants<StageDetailsText>(shortCard).Single();
            var shortBubble = Descendants<Button>(shortCard).FirstOrDefault(x => Equals(x.Tag, "StageDetailsToggleBubble"));
            Assert(!shortText.IsTruncated && !shortText.NeedsBubble && (shortBubble is null || shortBubble.Visibility == Visibility.Collapsed),
                "#17 4行及以内阶段完整显示且不出现省略气泡");

            var longCard = runningCol.RenderedCard(longTask.Id)!;
            var longText = Descendants<StageDetailsText>(longCard).Single();
            var longBubble = Descendants<Button>(longCard).Single(x => Equals(x.Tag, "StageDetailsToggleBubble"));
            var longJumpHit = Descendants<FrameworkElement>(longCard).Single(x => Equals(x.Tag, "ConversationJumpHitTarget"));
            Assert(longText.IsTruncated && longText.DisplayedLines == 5 && longBubble.Visibility == Visibility.Visible && Equals(longBubble.Content, "…"),
                $"#17 12行长阶段自适应省略为5行并显示[…]气泡按钮(displayed={longText.DisplayedLines})");
            Assert(SurfaceWindow.IsInteractive(longBubble), "#17 […]气泡按钮被SurfaceWindow.IsInteractive识别，点击不触发编辑或拖动");
            var bubbleRectInHost = longText.BubbleBounds;
            var jumpLeftInHost = longText.ActualWidth - longJumpHit.Width;
            Assert(bubbleRectInHost.Right <= jumpLeftInHost + 0.5, $"#17 […]气泡避开右下角跳转会话按钮无遮挡(bubbleRight={bubbleRectInHost.Right:F1}, jumpLeft={jumpLeftInHost:F1})");

            // Click […] to expand and persist
            longBubble.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(runningCol.Root, 260, 700);
            Assert(controller.Engine.IsStageDetailsExpanded(longTask.Id, longTask.Stages[0].Id), "#17 点击[…]气泡后展开状态写入Preferences持久化记忆");
            var expandedCard = runningCol.RenderedCard(longTask.Id)!;
            var expandedText = Descendants<StageDetailsText>(expandedCard).Single();
            var collapseBubble = Descendants<Button>(expandedCard).Single(x => Equals(x.Tag, "StageDetailsToggleBubble"));
            Assert(!expandedText.IsTruncated && expandedText.DisplayedLines >= 12 && collapseBubble.Visibility == Visibility.Visible && Equals(collapseBubble.Content, "▴"),
                $"#17 展开态显示全部内容并在末尾呈现[▴]收起气泡(displayed={expandedText.DisplayedLines})");

            // Click [▴] to collapse back
            collapseBubble.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(runningCol.Root, 260, 700);
            Assert(!controller.Engine.IsStageDetailsExpanded(longTask.Id, longTask.Stages[0].Id), "#17 点击[▴]收起气泡后移出展开记忆");
            var recollapsedText = Descendants<StageDetailsText>(runningCol.RenderedCard(longTask.Id)!).Single();
            Assert(recollapsedText.IsTruncated && recollapsedText.DisplayedLines == 5, "#17 点击[▴]后恢复自适应省略状态");

            // UI Button Shape & Contrast Polish (0.9.55 Feedback #6)
            {
                static double RelativeLuminance(Color c)
                {
                    static double Linear(byte channel)
                    {
                        var v = channel / 255.0;
                        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                    }
                    return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
                }
                static Color CompositeOver(Color fg, Color bg)
                {
                    var a = fg.A / 255.0;
                    return Color.FromRgb(
                        (byte)Math.Round(fg.R * a + bg.R * (1 - a)),
                        (byte)Math.Round(fg.G * a + bg.G * (1 - a)),
                        (byte)Math.Round(fg.B * a + bg.B * (1 - a)));
                }
                static double ContrastRatio(Color a, Color b)
                {
                    var la = RelativeLuminance(a);
                    var lb = RelativeLuminance(b);
                    return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
                }

                var surfaceColor = ((SolidColorBrush)Ui.Surface).Color;
                var cardEffective = CompositeOver(((SolidColorBrush)Ui.Card).Color, surfaceColor);
                var btnEffective = CompositeOver(((SolidColorBrush)Ui.ButtonSurface).Color, cardEffective);
                var btnHoverEffective = CompositeOver(((SolidColorBrush)Ui.ButtonHover).Color, cardEffective);
                var btnBorderColor = ((SolidColorBrush)Ui.ButtonBorder).Color;
                var btnTextColor = ((SolidColorBrush)Ui.ButtonText).Color;
                var accentColor = ((SolidColorBrush)Ui.Accent).Color;
                var accentTextColor = ((SolidColorBrush)Ui.AccentText).Color;

                var lumCard = RelativeLuminance(cardEffective);
                var lumBtn = RelativeLuminance(btnEffective);
                var lumHover = RelativeLuminance(btnHoverEffective);
                var lumBorder = RelativeLuminance(btnBorderColor);
                var normalTextContrast = ContrastRatio(btnTextColor, btnEffective);
                var accentTextContrast = ContrastRatio(accentTextColor, accentColor);

                Assert(lumBtn > lumCard + 0.02 && lumHover > lumBtn + 0.015 && lumBorder > lumBtn + 0.05,
                    $"#BtnPolish 按钮底色/悬停态/边框相对卡片背景具有清晰亮度层次(cardL={lumCard:F3}, btnL={lumBtn:F3}, hoverL={lumHover:F3}, borderL={lumBorder:F3})");
                Assert(normalTextContrast >= 7.0 && accentTextContrast >= 7.0,
                    $"#BtnPolish 普通按钮与强调主按钮文字对比度均达 WCAG AAA(normal={normalTextContrast:F2}:1, accent={accentTextContrast:F2}:1)");

                var uiEditSession = controller.Engine.Edit(longTask.Id);
                var uiEditor = new EditorSurface(controller, uiEditSession);
                stageHost.Children.Add(uiEditor);
                Layout(uiEditor, 430, EditorSurface.DefaultHeight);
                var saveBtn = Descendants<Button>(uiEditor).Single(x => Equals(x.Tag, "SaveTask"));
                var closeBtn = Descendants<Button>(uiEditor).Single(x => Equals(x.Tag, "EditorClose"));
                var addStageBtn = Descendants<Button>(uiEditor).Single(x => Equals(x.Tag, "AddStage"));
                var stageRemoveBtn = Descendants<Button>(uiEditor).First(x => Equals(x.Tag, "StageRemove"));
                var stageRunBtn = Descendants<Button>(uiEditor).First(x => Equals(x.Tag, "StageRun"));
                var stageNameBox = Descendants<TextBox>(uiEditor).First(x => Equals(x.Tag, "StageName"));

                var saveBorder = (Border)saveBtn.Template.FindName("border", saveBtn)!;
                var closeBorder = (Border)closeBtn.Template.FindName("frame", closeBtn)!;
                var addStageBorder = (Border)addStageBtn.Template.FindName("border", addStageBtn)!;
                var removeBorder = (Border)stageRemoveBtn.Template.FindName("border", stageRemoveBtn)!;
                var runBorder = (Border)stageRunBtn.Template.FindName("border", stageRunBtn)!;
                var stageNameFrame = (Border)stageNameBox.Template.FindName("frame", stageNameBox)!;

                Assert(Math.Abs(saveBorder.CornerRadius.TopLeft - 9) < 0.1
                    && Math.Abs(closeBorder.CornerRadius.TopLeft - 9) < 0.1
                    && Math.Abs(addStageBorder.CornerRadius.TopLeft - 9) < 0.1,
                    "#BtnPolish 所有常规文字按钮采用利落圆角矩形(CornerRadius=9)，不再呈纯椭圆");
                Assert(addStageBtn.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue
                    && saveBtn.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue
                    && addStageBtn.Style.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsMouseOverProperty && t.Setters.OfType<Setter>().Any(s => s.Property == Control.BackgroundProperty))
                    && saveBtn.Style.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsMouseOverProperty && t.Setters.OfType<Setter>().Any(s => s.Property == Control.BackgroundProperty)),
                    "#BtnPolish 普通按钮与强调按钮通过 Style+Triggers 提供默认底色与悬停提亮，无本地属性遮蔽");
                Assert(Math.Abs(stageRemoveBtn.Width - 24) < 0.1 && Math.Abs(stageRemoveBtn.Height - 24) < 0.1 && removeBorder.CornerRadius.TopLeft >= 12
                    && Math.Abs(stageRunBtn.Width - 24) < 0.1 && Math.Abs(stageRunBtn.Height - 24) < 0.1 && runBorder.CornerRadius.TopLeft >= 12,
                    "#BtnPolish 固定正方小图标按钮(StageRemove/StageRun)保持正圆造型");
                Assert(Math.Abs((double)RoundedInput.RadiusProperty.DefaultMetadata.DefaultValue - 10.0) < 0.1
                    && Math.Abs(stageNameFrame.CornerRadius.TopLeft - 14.0) < 0.1,
                    "#BtnPolish 常规输入框默认圆角收紧至10DIP且阶段名胶囊保持14DIP与Clip严丝合缝");

                var dashedGrid = Ui.DashedButton("＋ 新建任务", () => { });
                var dashedRect = dashedGrid.Children.OfType<System.Windows.Shapes.Rectangle>().Single();
                var dashedBtn = dashedGrid.Children.OfType<Button>().Single();
                Assert(Math.Abs(dashedRect.RadiusX - 12) < 0.1 && Math.Abs(dashedRect.RadiusY - 12) < 0.1
                    && dashedBtn.Background is SolidColorBrush { Color.A: > 0 },
                    "#BtnPolish 虚线新建入口圆角协调为12DIP并保留非零Alpha全区命中");

                var ballSearch = Descendants<TextBox>(controller.Ball.Root).Single();
                var ballClear = Descendants<Button>(controller.Ball.Root).Single(x => Equals(x.Tag, "BallSearchClear"));
                var ballSettings = Descendants<Button>(controller.Ball.Root).Single(x => Equals(x.Tag, "BallSettingsButton"));
                ballClear.ApplyTemplate();
                ballSettings.ApplyTemplate();
                var ballClearBorder = (Border)ballClear.Template.FindName("border", ballClear)!;
                var ballSettingsBorder = (Border)ballSettings.Template.FindName("border", ballSettings)!;
                Assert(Math.Abs(ballSearch.Height - 36) < 0.1
                    && Math.Abs(ballClear.Width - 28) < 0.1 && Math.Abs(ballClear.Height - 28) < 0.1
                    && ballClearBorder.CornerRadius.TopLeft >= ballClear.Width / 2
                    && Math.Abs(ballSettings.Width - 44) < 0.1 && Math.Abs(ballSettings.Height - 36) < 0.1
                    && Math.Abs(ballSettingsBorder.CornerRadius.TopLeft - 9) < 0.1,
                    "#BtnPolish 悬浮球展开横框搜索框36高、清零按钮28×28正圆、设置按钮44×36与搜索框严丝合缝平齐");

                // 0.9.56 Feedback #3: CheckBox/RadioButton/Slider/Input high-contrast semantic brushes & balanced text button proportions
                var checkSurface = (SolidColorBrush)Application.Current.FindResource("CheckSurfaceBrush");
                var checkBorder = (SolidColorBrush)Application.Current.FindResource("CheckBorderBrush");
                var checkChecked = (SolidColorBrush)Application.Current.FindResource("CheckCheckedFillBrush");
                var checkMark = (SolidColorBrush)Application.Current.FindResource("CheckMarkBrush");
                var inputBorder = (SolidColorBrush)Application.Current.FindResource("InputBorderBrush");
                var sliderTrack = (SolidColorBrush)Application.Current.FindResource("SliderTrackBrush");
                var comboArrow = (SolidColorBrush)Application.Current.FindResource("ComboArrowBrush");
                var fieldColor = ((SolidColorBrush)Application.Current.FindResource("FieldBrush")).Color;
                var lineColor = ((SolidColorBrush)Application.Current.FindResource("LineBrush")).Color;
                Assert(RelativeLuminance(checkSurface.Color) > RelativeLuminance(fieldColor) + 0.015
                    && RelativeLuminance(checkBorder.Color) > RelativeLuminance(lineColor) + 0.15
                    && ContrastRatio(checkMark.Color, checkChecked.Color) >= 7.0
                    && RelativeLuminance(inputBorder.Color) > RelativeLuminance(lineColor) + 0.05
                    && RelativeLuminance(sliderTrack.Color) > RelativeLuminance(lineColor) + 0.05
                    && RelativeLuminance(comboArrow.Color) > 0.65,
                    "#BtnPolish 勾选圆框/单选圆框/滑轨/输入框边框/下拉箭头语义画刷对比度全面达标");

                var probeCheck = new CheckBox { Content = "测试勾选" };
                var probeRadio = new RadioButton { Content = "测试单选" };
                stageHost.Children.Add(probeCheck);
                stageHost.Children.Add(probeRadio);
                Layout(probeCheck, 160, 32);
                Layout(probeRadio, 160, 32);
                var probeCheckBox = (Border)probeCheck.Template.FindName("box", probeCheck)!;
                var probeCheckTick = (TextBlock)probeCheck.Template.FindName("tick", probeCheck)!;
                var probeRadioCircle = (Border)probeRadio.Template.FindName("circle", probeRadio)!;
                Assert(Math.Abs(probeCheckBox.BorderThickness.Left - 1.5) < 0.05
                    && Equals(((SolidColorBrush)probeCheckBox.Background).Color, checkSurface.Color)
                    && probeCheckTick.FontWeight == FontWeights.Bold
                    && Math.Abs(probeRadioCircle.BorderThickness.Left - 1.5) < 0.05
                    && Equals(((SolidColorBrush)probeRadioCircle.Background).Color, checkSurface.Color),
                    "#BtnPolish CheckBox与RadioButton圆框启用1.5px高亮边框、提亮底色与粗体勾选标记");
                stageHost.Children.Remove(probeCheck);
                stageHost.Children.Remove(probeRadio);

                var remBtnCheck = new ReminderEditor(new Reminder { Enabled = true }, () => { }, () => new DateTime(2026, 11, 27, 10, 0, 0));
                stageHost.Children.Add(remBtnCheck);
                Layout(remBtnCheck, 390, 420);
                var remDateBtn = Descendants<Button>(remBtnCheck).Single(x => Equals(x.Tag, "ReminderDateOpen"));
                var remTimeBtn = Descendants<Button>(remBtnCheck).Single(x => Equals(x.Tag, "ReminderTimeOpen"));
                var remTodayBtn = Descendants<Button>(remBtnCheck).Single(x => Equals(x.Tag, "ReminderToday"));
                var remTimePanel = remBtnCheck.BuildTimePickerContent();
                var remDoneBtn = Descendants<Button>(remTimePanel).Single(x => Equals(x.Tag, "ReminderTimeClose"));
                Assert(remDateBtn.MinHeight >= 34 && remTimeBtn.MinHeight >= 34
                    && remTodayBtn.MinHeight >= 30 && remDoneBtn.MinHeight >= 28
                    && dismissBtn.MinHeight >= 28 && unlinkBtn.MinHeight >= 32,
                    "#BtnPolish 提醒日历/选择时间/今天/完成、关闭提醒、解除绑定等文字按钮高度比例饱满协调");
                stageHost.Children.Remove(remBtnCheck);

                // 0.9.56 Feedback #4: Official cat mascot icon (orb.png + orb.ico) & BallSurface layering
                var appIcon = Brand.AppIconImage();
                Assert(appIcon is System.Windows.Media.Imaging.BitmapSource bmpIcon && bmpIcon.PixelWidth == 256 && bmpIcon.PixelHeight == 256,
                    "#BrandIcon Brand.AppIconImage 正常加载 256×256 新版猫咪圆标资源");
                var orbGlyph = Brand.OrbGlyph();
                Assert(orbGlyph is Image orbImg && orbImg.Source is not null
                    && RenderOptions.GetBitmapScalingMode(orbImg) == BitmapScalingMode.HighQuality
                    && orbImg.Width >= 46 && orbImg.Height >= 46,
                    "#BrandIcon Brand.OrbGlyph 渲染高画质猫咪图标 Image");
                using (var loadedTrayIcon = Brand.TrayIcon())
                {
                    Assert(loadedTrayIcon.Width > 0 && loadedTrayIcon.Height > 0,
                        $"#BrandIcon Brand.TrayIcon 正常加载新版 orb.ico ({loadedTrayIcon.Width}x{loadedTrayIcon.Height})");
                }
                var ballIcon = Descendants<Image>(controller.Ball.Root).FirstOrDefault(x => Equals(x.Tag, "BrandOrbIcon"));
                var orbContainer = ballIcon?.Parent as Grid;
                Assert(ballIcon is not null && orbContainer is not null
                    && orbContainer.Children.IndexOf(controller.Ball.CountBubble) > orbContainer.Children.IndexOf(ballIcon)
                    && controller.Ball.Root.Children.IndexOf(controller.Ball.EdgeDot) > controller.Ball.Root.Children.IndexOf(orbContainer.Parent as UIElement),
                    "#BrandIcon 悬浮球猫咪图标位于底图、待操作计数红圈与边缘吸附红点严格位于图标上方不被遮挡");

                stageHost.Children.Remove(uiEditor);
                uiEditor.Destroy();
                controller.Engine.Discard(uiEditSession);

                // 0.9.56 Feedback #5: Appearance settings tab + Dark/Light/System live theme engine & 9 high-contrast presets
                Assert(ThemePalette.DarkPresets.Count == 5 && ThemePalette.LightPresets.Count == 4 && ThemePalette.AllPresets.Count == 9,
                    "#Theme 提供 5 套深色预设与 4 套浅色预设共 9 套内置方案");
                Assert(ThemePalette.Resolve(ThemeMode.Dark, "midnight-cyan", "daylight-mist").Id == "midnight-cyan"
                    && ThemePalette.Resolve(ThemeMode.Light, "midnight-cyan", "daylight-mist").Id == "daylight-mist"
                    && ThemePalette.Resolve(ThemeMode.System, "obsidian-amber", "warm-paper", () => false).Id == "obsidian-amber"
                    && ThemePalette.Resolve(ThemeMode.System, "obsidian-amber", "warm-paper", () => true).Id == "warm-paper"
                    && ThemePalette.Resolve(ThemeMode.Dark, "unknown", "daylight-mist").Id == "midnight-cyan"
                    && ThemePalette.Resolve(ThemeMode.Light, "midnight-cyan", "unknown").Id == "daylight-mist",
                    "#Theme ThemePalette.Resolve 准确解析 Dark / Light / System(dark/light) 与未知预设回退");

                foreach (var preset in ThemePalette.AllPresets)
                {
                    var pResolved = ThemePalette.Resolve(preset.IsDark ? ThemeMode.Dark : ThemeMode.Light, preset.Id, preset.Id);
                    Assert(pResolved.Id == preset.Id && pResolved.IsDark == preset.IsDark, $"#Theme 可精确解析预设 {preset.Id}");

                    var effCard = CompositeOver(preset.Card.Color, preset.SurfaceColor);
                    var effBtn = CompositeOver(preset.ButtonSurface.Color, effCard);
                    var textOnSurface = ContrastRatio(preset.Text.Color, preset.SurfaceColor);
                    var textOnCard = ContrastRatio(preset.Text.Color, effCard);
                    var mutedOnCard = ContrastRatio(preset.Muted.Color, effCard);
                    var btnTextOnBtn = ContrastRatio(preset.ButtonText.Color, effBtn);
                    var accentTextOnAccent = ContrastRatio(preset.AccentText.Color, preset.Accent.Color);
                    var checkMarkOnChecked = ContrastRatio(preset.CheckMark.Color, preset.CheckCheckedFill.Color);
                    var checkBorderVsSurface = ContrastRatio(preset.CheckBorder.Color, preset.CheckSurface.Color);

                    Assert(textOnSurface >= 7.0 && textOnCard >= 7.0 && mutedOnCard >= 4.2,
                        $"#Theme 预设 {preset.Id} 主/次文字对比度清晰达标(surf={textOnSurface:F2}, card={textOnCard:F2}, muted={mutedOnCard:F2})");
                    Assert(btnTextOnBtn >= 7.0 && accentTextOnAccent >= 4.3 && checkMarkOnChecked >= 4.3 && checkBorderVsSurface >= 2.0,
                        $"#Theme 预设 {preset.Id} 按钮/主按钮/勾选框对比度清晰达标(btn={btnTextOnBtn:F2}, accent={accentTextOnAccent:F2}, tick={checkMarkOnChecked:F2}, chkBorder={checkBorderVsSurface:F2})");
                }

                // Live dark <-> light switching syncs DynamicResource, SurfaceWindow.GlassTintBrush, LiveBackdrop RGB, OrbMenuRenderer & ActionPinIcon
                var themeCol = controller.Columns.First();
                new WindowInteropHelper(themeCol).EnsureHandle();
                themeCol.EnsureLiveBackdrop();
                var darkTintBefore = ((SolidColorBrush)themeCol.CurrentGlassTintBrush).Color;
                Assert(darkTintBefore.R == 25 && darkTintBefore.G == 35 && darkTintBefore.B == 51
                    && themeCol.BackdropSurfaceColor.R == 25 && themeCol.BackdropSurfaceColor.G == 35 && themeCol.BackdropSurfaceColor.B == 51,
                    "#Theme 默认深夜青蓝 GlassTintBrush 与 LiveBackdrop RGB 为 (25,35,51)");

                controller.SetThemeMode(ThemeMode.Light);
                var lightTint = ((SolidColorBrush)themeCol.CurrentGlassTintBrush).Color;
                var lightTextRes = ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color;
                var lightCardRes = ((SolidColorBrush)Application.Current.FindResource("CardBrush")).Color;
                var lightMenuRes = ((SolidColorBrush)Application.Current.FindResource("MenuSurfaceBrush")).Color;
                var lightCheckRes = ((SolidColorBrush)Application.Current.FindResource("CheckSurfaceBrush")).Color;
                var lightCheckCheckedRes = ((SolidColorBrush)Application.Current.FindResource("CheckCheckedFillBrush")).Color;
                Assert(!ThemePalette.Current.IsDark && ThemePalette.Current.Id == "daylight-mist"
                    && lightTint.R == 0xED && lightTint.G == 0xF3 && lightTint.B == 0xFA
                    && themeCol.BackdropSurfaceColor.R == 0xED && themeCol.BackdropSurfaceColor.G == 0xF3 && themeCol.BackdropSurfaceColor.B == 0xFA
                    && lightTextRes == ThemePalette.DaylightMist.Text.Color
                    && lightCardRes == ThemePalette.DaylightMist.CardSolid.Color
                    && lightMenuRes == ThemePalette.DaylightMist.MenuSurface.Color
                    && lightCheckRes == ThemePalette.DaylightMist.CheckSurface.Color
                    && lightCheckCheckedRes == ThemePalette.DaylightMist.CheckCheckedFill.Color,
                    "#Theme 切换白天模式后 LiveBackdrop/GlassTintBrush RGB 与全部 DynamicResource 画刷实时同步至晴昼青雾");

                // 0% opaque light mode still skips HostBackdropBrush and uses light surface color
                controller.SetGlassTransparency(0);
                Assert(themeCol.SkipsDesktopBlur && !themeCol.BackdropUsesDesktopBlur
                    && themeCol.BackdropSurfaceColor.R == 0xED && themeCol.BackdropSurfaceColor.G == 0xF3 && themeCol.BackdropSurfaceColor.B == 0xFA,
                    "#Theme 白天模式 0% 不透明跳过 HostBackdropBrush 且采用浅色底板 RGB");
                controller.SetGlassTransparency(20);

                // Verify SettingsSurface "外观" tab navigation, 3 mode radios, 5 dark + 4 light preset cards & instant click switching
                Assert(SettingsSurface.Sections.Length == 7 && SettingsSurface.Sections[0] == "主设置" && SettingsSurface.Sections[1] == "外观",
                    "#Theme 设置导航栏在“常规”后新增“外观”独立分页");
                controller.SetThemeMode(ThemeMode.Dark);
                var appearanceSettings = new SettingsSurface(controller) { SuppressActivation = true };
                try
                {
                    appearanceSettings.SelectSection("外观");
                    Layout(appearanceSettings.Root, 760, 680);
                    var modeRadios = Descendants<RadioButton>(appearanceSettings.Root).Where(x => Equals(x.Tag, "ThemeModeOption")).ToArray();
                    var darkCards = Descendants<Button>(appearanceSettings.Root).Where(x => Equals(x.Tag, "DarkThemePresetCard")).ToArray();
                    var lightCards = Descendants<Button>(appearanceSettings.Root).Where(x => Equals(x.Tag, "LightThemePresetCard")).ToArray();
                    var goTransparency = Descendants<Button>(appearanceSettings.Root).Single(x => Equals(x.Tag, "GoToWindowTransparency"));
                    Assert(modeRadios.Length == 3 && modeRadios[0].IsChecked == true
                        && darkCards.Length == 5 && lightCards.Length == 4,
                        "#Theme 外观页包含 3 种主题模式、5 套深色色卡、4 套浅色色卡及透明度快捷入口");

                    // Click light preset card "warm-paper" -> saves LightThemePreset & switches immediately to Light mode
                    var warmCard = lightCards.Single(x => Equals(x.DataContext, "warm-paper"));
                    warmCard.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Layout(appearanceSettings.Root, 760, 680);
                    Assert(controller.Engine.State.Preferences.ThemeMode == ThemeMode.Light
                        && controller.Engine.State.Preferences.LightThemePreset == "warm-paper"
                        && ThemePalette.Current.Id == "warm-paper"
                        && !ThemePalette.Current.IsDark,
                        "#Theme 点击浅色色卡“暖纸杏棕”立即保存并实时切换至浅色暖纸主题");

                    // Click dark preset card "forest-jade" -> saves DarkThemePreset & switches immediately to Dark mode
                    var forestCard = Descendants<Button>(appearanceSettings.Root).Single(x => Equals(x.Tag, "DarkThemePresetCard") && Equals(x.DataContext, "forest-jade"));
                    forestCard.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Layout(appearanceSettings.Root, 760, 680);
                    Assert(controller.Engine.State.Preferences.ThemeMode == ThemeMode.Dark
                        && controller.Engine.State.Preferences.DarkThemePreset == "forest-jade"
                        && ThemePalette.Current.Id == "forest-jade"
                        && ThemePalette.Current.IsDark,
                        "#Theme 点击深色色卡“深林松翠”立即保存并实时切换至深林松翠主题");

                    // Click System radio -> saves ThemeMode.System & resolves against simulated OS light/dark
                    var sysRadio = Descendants<RadioButton>(appearanceSettings.Root).Single(x => Equals(x.Tag, "ThemeModeOption") && Equals(x.DataContext, ThemeMode.System));
                    sysRadio.IsChecked = true;
                    sysRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Layout(appearanceSettings.Root, 760, 680);
                    Assert(controller.Engine.State.Preferences.ThemeMode == ThemeMode.System, "#Theme 点击跟随系统单选更新偏好为 ThemeMode.System");
                    controller.ApplyAppearance(systemIsLight: () => true);
                    Assert(ThemePalette.Current.Id == "warm-paper", "#Theme 跟随系统在浅色系统下实时启用已选浅色预设");
                    controller.ApplyAppearance(systemIsLight: () => false);
                    Assert(ThemePalette.Current.Id == "forest-jade", "#Theme 跟随系统在深色系统下实时启用已选深色预设");

                    // Shortcut button navigates cleanly to "窗口"
                    goTransparency = Descendants<Button>(appearanceSettings.Root).Single(x => Equals(x.Tag, "GoToWindowTransparency"));
                    goTransparency.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Assert(appearanceSettings.CurrentSection == "窗口", "#Theme 外观页底部快捷按钮可一键跳转至窗口与列表页调节透明度");
                }
                finally
                {
                    appearanceSettings.Destroy(false);
                    controller.SetDarkThemePreset(ThemePresets.DefaultDark, switchToDark: true);
                    controller.Engine.State.Preferences.LightThemePreset = ThemePresets.DefaultLight;
                    controller.ApplyAppearance();
                }
            }
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
