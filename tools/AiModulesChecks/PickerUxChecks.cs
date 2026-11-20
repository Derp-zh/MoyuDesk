using System.IO;
using System.Windows;
using System.Windows.Controls;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class PickerUxChecks
{
    internal static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
        using var controller = new BoardController(Path.Combine(root, "picker-ux"), true, true);
        var original = new CodexBinding { ThreadId = "11111111-1111-4111-8111-111111111111", ThreadName = "原Codex", AwaitingUserAction = true };
        var stage = new Stage { Codex = original }; var changes = 0;
        using var picker = new StageConversationPicker(controller, stage, () => changes++, true);
        var sidebar = picker.Pane.Children.OfType<Grid>().Single(x => Equals(x.Tag, "StageAiPlatformSidebar"));
        var buttons = sidebar.Children.OfType<Button>().ToArray();
        Check(buttons.Length == 3 && !picker.Pane.Children.OfType<ComboBox>().Any(), "已接入三平台使用矩形侧栏且不依赖下拉框");
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(picker.SelectedPlatform == AiPlatform.Dsh && ReferenceEquals(stage.Codex, original) && changes == 0, "切换平台只浏览目录，不提前绑定或清除等待");
        var mixed = new[] { new CodexThread("same", "DSH同名", 0, AiPlatform.Dsh), new CodexThread("same", "Claude同名", 0, AiPlatform.ClaudeCode) };
        await picker.Load(() => Task.FromResult(mixed));
        Check(picker.Matches is [{ Platform: AiPlatform.Dsh }], "同编号混合目录仅展示当前平台会话，不串来源");
        var gate = new TaskCompletionSource<CodexThread[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoad = picker.Load(() => gate.Task);
        buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await picker.Load(() => Task.FromResult(new[] { mixed[1] }));
        gate.SetResult([mixed[0]]); await oldLoad;
        Check(picker.SelectedPlatform == AiPlatform.ClaudeCode && picker.Matches is [{ Platform: AiPlatform.ClaudeCode }], "快速跨平台切换拒绝迟到的旧平台目录");
        var sourceCatalog = new[] { new CodexThread(original.ThreadId, "原Codex", 0) };
        var delayed = new TaskCompletionSource<CodexThread[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = picker.Load(() => delayed.Task);
        picker.RestorePlatform(AiPlatform.Codex, sourceCatalog); picker.Input.Text = "原Codex";
        delayed.SetResult([mixed[1]]); await late;
        Check(picker.SelectedPlatform == AiPlatform.Codex && picker.Matches.Single().Id == original.ThreadId && picker.Input.Text == "原Codex", "取消恢复的平台目录和搜索不被迟到异步结果覆盖");
        picker.Select(mixed[1]);
        Check(ReferenceEquals(stage.Codex, original) && original.AwaitingUserAction, "旧平台行的迟到选择不改当前绑定或等待保护");
        var host = new Border { Width = 268, Height = 560, Child = picker.Pane };
        host.Measure(new Size(268, 560)); host.Arrange(new Rect(0, 0, 268, 560)); host.UpdateLayout();
        Check(buttons.All(x => x.ActualWidth >= 39 && x.ActualHeight >= 170) && Math.Abs(picker.Input.ActualWidth - 200) < 1 && Grid.GetColumn(sidebar) == 1,
            "右侧窄栏保持可用命中面积，会话区域200宽等于原300宽的三分之二");
        host.Child = null;
        controller.SetConversationPlatformsForChecks(AiPlatform.Codex, AiPlatform.Dsh);
        picker.SwitchPlatform(AiPlatform.ClaudeCode, false); picker.RefreshAvailability(false);
        Check(picker.AvailablePlatforms.SequenceEqual(new[] { AiPlatform.Codex, AiPlatform.Dsh }) && buttons[2].Visibility == Visibility.Collapsed &&
            sidebar.RowDefinitions[2].Height.Value == 0 && picker.SelectedPlatform == AiPlatform.Codex && ReferenceEquals(stage.Codex, original),
            "未接入平台隐藏且不占行，当前平台失效时回退可用平台但保留绑定");
        picker.SwitchPlatform(AiPlatform.ClaudeCode, false);
        Check(picker.SelectedPlatform == AiPlatform.Codex, "隐藏平台不能通过迟到点击或悬停切入");
        controller.SetConversationPlatformsForChecks(); picker.RefreshAvailability(false); picker.SetCatalog(sourceCatalog); picker.Select(sourceCatalog[0]);
        Check(picker.AvailablePlatforms.Length == 0 && !picker.Input.IsEnabled && picker.Matches.Length == 0 && picker.DropHint.Contains("初始化") && ReferenceEquals(stage.Codex, original),
            "全部未接入时显示初始化提示，拒绝旧目录选择并保留已有绑定");
        controller.SetConversationPlatformsForChecks(AiPlatform.Dsh); picker.RefreshAvailability(false);
        Check(picker.SelectedPlatform == AiPlatform.Dsh && picker.Input.IsEnabled && buttons[1].Visibility == Visibility.Visible && buttons[0].Visibility == Visibility.Collapsed,
            "完成接入后窗口可恢复唯一可用平台且没有空白平台行");
        using var realReports = new BoardController(Path.Combine(root, "picker-reports"), true, false);
        var module = realReports.AiModule(AiPlatform.Dsh);
        module.Report = new(AiConnectionState.Ready, "", "", Installed: true, Configured: true, Catalog: true);
        Check(realReports.ConversationPlatformAvailable(AiPlatform.Dsh) == true, "平台可用依据本机安装配置与目录，不要求客户端运行");
        module.Report = module.Report with { State = AiConnectionState.NeedsOpen, Running = false };
        Check(realReports.ConversationPlatformAvailable(AiPlatform.Dsh) == true, "已接入客户端暂未运行仍保留本地目录选择");
        module.Report = module.Report with { State = AiConnectionState.MissingRuntime };
        Check(realReports.ConversationPlatformAvailable(AiPlatform.Dsh) == false, "运行环境缺失即使旧能力位存在也隐藏平台");
        module.Busy = true;
        Check(realReports.ConversationPlatformAvailable(AiPlatform.Dsh) is null, "检查期间保留最近确定结果以避免侧栏闪烁");
    }
}
