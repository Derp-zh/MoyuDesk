using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0945Checks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || !controller.Smoke) throw new InvalidOperationException("仅隔离离屏检查");
        var checks = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) => (condition ? checks : failures).Add(name);
        var root = controller.Store.DirectoryPath;
        foreach (var width in new[] { 135.0, 220.0, 360.0 })
        foreach (var trim in new[] { false, true })
        {
            var text = new StageDetailsText("第一行应该可以使用全部宽度的文字用于验证，不因为末行按钮缩短。第二段🙂 abc def ghi jkl\n末行内容", 14, Ui.Text, trim, 42);
            Layout(text, width);
            Check(text.LineWidths.Take(text.DisplayedLines - 1).All(x => Math.Abs(x - width) < .1)
                && text.LineWidths[^1] < width, $"{width}/{trim}仅末行让位");
            var reference = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip)
                { MaxTextWidth = width, LineHeight = text.LineHeight };
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawText(reference, new Point());
            var actual = Pixels(text, (int)width, 100); var expected = Pixels(visual, (int)width, 100);
            var firstLineBytes = (int)width * 4 * (int)(text.LineHeight - 1);
            Check(actual.Take(firstLineBytes).SequenceEqual(expected.Take(firstLineBytes)), $"{width}/{trim}首行像素与全宽正文一致");
            Save(text, (int)width, (int)Math.Ceiling(text.ActualHeight), Path.Combine(root, $"details-{width}-{trim}.png"));
        }
        var empty = new StageDetailsText("", 14, Ui.Text, true, 64); Layout(empty, 220);
        Check(empty.DisplayedLines == 2 && Math.Abs(empty.ActualHeight - empty.LineHeight * 2) < .1, "空正文只占两行，不补满三行");
        var shortText = new StageDetailsText("短正文", 14, Ui.Text, true, 64); Layout(shortText, 220);
        Check(shortText.DisplayedLines == 2, "一行正文保留两行，第一行仍全宽");
        var longText = new StageDetailsText(string.Join("\n", Enumerable.Repeat("完整内容🙂末尾", 30)), 14, Ui.Text, false, 42); Layout(longText, 220);
        Check(longText.ActualHeight >= 30 * longText.LineHeight && longText.Text.EndsWith("末尾", StringComparison.Ordinal), "关闭省略显示全部三十行和末字");
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "0945运行按钮";
        controller.Engine.AddStage(session);
        var editor = new EditorSurface(controller, session); Layout(editor, 430, 550);
        var buttons = StageFeatureChecks.Children<Button>(editor).Where(x => Equals(x.Tag, "StageRun")).ToArray();
        Check(buttons.Length == 2 && buttons.All(x => x.Content is StageRunIcon && x.Width == 24 && x.Height == 24), "运行按钮只有原图标且24DIP正圆");
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(session.Working.Stages[0].IsRunning == true, "最后一个运行阶段不能暂停");
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(session.Working.Stages[1].IsRunning == true, "圆形按钮能启动未运行阶段");
        var bindingButtons = StageFeatureChecks.Children<Button>(editor).Where(x => Equals(x.Tag, "StageCodex")).ToArray();
        Check(bindingButtons.Length == 2 && bindingButtons.All(x => x.Width > 90), "运行按钮缩小不缩窄绑定会话胶囊");
        var names = StageFeatureChecks.Children<TextBox>(editor).Where(x => Equals(x.Tag, "StageName")).ToArray();
        Check(names.Length == 2 && names.All(x => x.HorizontalAlignment == HorizontalAlignment.Left && x.ActualWidth < 120), "短阶段名按内容取宽，不初始占满剩余位置");
        var nameRect = names[0].GetRectFromCharacterIndex(0);
        Check(!nameRect.IsEmpty && Math.Abs(nameRect.Top + nameRect.Height / 2 - names[0].ActualHeight / 2) < 1.5, "阶段名称文字实际字符矩形纵向居中");
        names[0].Text = new string('长', 40); Layout(editor, 430, 550);
        Check(names[0].ActualWidth <= 155 && names[0].ActualWidth > 120, "长阶段名保持原155DIP上限");
        editor.Destroy(); controller.Engine.State.Sessions.Remove(session);
        controller.Settings(); var settings = GetSettings(controller); settings.SelectSection("AI平台接入情况"); settings.SelectAiPlatform(AiPlatform.Dsh);
        settings.Root.UpdateLayout(); await Task.Delay(160);
        Check(StageFeatureChecks.Children<Button>(settings.Root).Count(x => Equals(x.Tag, "AiInitializeAll")) == 1, "AI页只有一个全平台初始化入口");
        var codex = new CodexFixture(); var dsh = new OtherFixture(AiPlatform.Dsh); var claude = new OtherFixture(AiPlatform.ClaudeCode);
        controller.SetCodexSetupEnvironmentForChecks(codex); controller.SetAiEnvironmentForChecks(AiPlatform.Dsh, dsh); controller.SetAiEnvironmentForChecks(AiPlatform.ClaudeCode, claude);
        codex.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var all = controller.InitializeAllAi(); await controller.InitializeAllAi();
        Check(controller.AllAiSetupBusy && codex.Discoveries == 1 && dsh.Initializations == 0, "忙碌时重复初始化不会重入");
        codex.Pause.SetResult(true); await all;
        Check(codex.Discoveries == 1 && dsh.Initializations == 1 && claude.Initializations == 1, "一次调用实际初始化全部三个平台");
        Check(controller.AllAiSetupStatus.Contains("Codex") && controller.AllAiSetupStatus.Contains("DSH") && controller.AllAiSetupStatus.Contains("Claude") && !controller.AllAiSetupBusy, "分别报告三个平台且恢复按钮");
        dsh.Fail = true; await controller.InitializeAllAi();
        Check(dsh.Initializations == 1 && claude.Initializations == 2 && controller.AiModule(AiPlatform.Dsh).Report.State == AiConnectionState.Failed, "DSH失败不阻断后面的Claude初始化");
        dsh.Fail = false; codex.Fail = true; await controller.InitializeAllAi();
        Check(dsh.Initializations == 2 && claude.Initializations == 3 && !controller.AiSetupReport.SourceReady, "Codex未找到不阻断后面两个平台");
        codex.Fail = false;
        await controller.ConnectAllAi();
        Check(dsh.Initializations == 2 && claude.Initializations == 3 && controller.AllAiSetupStatus.StartsWith("本轮连接已结束", StringComparison.Ordinal), "一键连接处理三平台但不重新安装或初始化hook，不自动打开应用");
        Check(controller.EntryMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().Count(x => x.Name == "AiConnectAll" && x.Text == "一键连接 AI 平台") == 1, "悬浮球右键菜单有唯一的一键连接入口");
        var menu = controller.EntryMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().ToArray();
        Check(menu[0].Name == "BallVisibility" && menu[1].Name == "OpenSettings" && menu[2].Name == "RestoreWindowLayout" && menu.Any(x => x.Name == "ToggleWorkspace"), "0946恢复原非AI功能与顺序，保留展开收起命令");
        Check(menu[^1].Name == "ExitApplication" && menu.Any(x => x.Name == "Undo") && menu.Any(x => x.Name == "Redo" && x.Text == "恢复"), "原撤销恢复标签及退出位置保持");
        var aiGroup = menu.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(x => x.Name == "AiStatusGroup");
        Check(menu.All(x => x.Name != "WindowGroup")
            && aiGroup.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().Any(x => x.Name == "OpenAiSettings"), "非AI窗球操作恢复原层级，AI状态保留直接设置入口");
        using (var observer = new AiPlatformObserver(Path.Combine(root, "events"), Path.Combine(root, "dsh"), AiPlatform.Dsh))
        {
            observer.Start(); var field = typeof(AiPlatformObserver).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!; var worker = field.GetValue(observer);
            observer.Start(); observer.Start();
            Check(observer.IsRunning && ReferenceEquals(worker, field.GetValue(observer)), "重复恢复监听复用同一工作循环");
        }
        await controller.Flush(); var disk = new AtomicStore(root, controller.Store.ConfigurationDirectory).Load();
        Check(disk.Preferences.ConnectionConfigs.GetValueOrDefault("dsh.desktop") == "fixture-dsh.exe" && disk.Preferences.ConnectionConfigs.GetValueOrDefault("claude.desktop") == "fixture-claude.exe", "统一初始化的三平台配置实际落盘");
        controller.SetExpanded(false); await Task.Delay(160); controller.SetExpanded(true); await Task.Delay(160);
        Check(!settings.IsVisible && settings.CurrentSection == "AI平台接入情况" && settings.SelectedAiPlatform == AiPlatform.Dsh, "整组恢复不显示设置且保留当前细分页和平台");
        controller.Settings(); await Task.Delay(160);
        Check(settings.IsVisible && settings.CurrentSection == "AI平台接入情况" && settings.SelectedAiPlatform == AiPlatform.Dsh, "手动再开设置恢复本进程细分页");
        var fresh = new SettingsSurface(controller);
        Check(fresh.CurrentSection == "主设置" && fresh.SelectedAiPlatform == AiPlatform.Codex, "新设置实例默认常规页，不从磁盘恢复页状态"); fresh.Destroy(false); settings.Hide();
        File.WriteAllText(Path.Combine(root, "ux0945-report.json"), JsonSerializer.Serialize(new { passed = failures.Count == 0, checks, failures, isolatedData = true, offscreen = true, simulatedAiEnvironments = true, originalAiConfigurationChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("；", failures));
    }
    private static SettingsSurface GetSettings(BoardController c) => (SettingsSurface)typeof(BoardController).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
    private static void Layout(FrameworkElement e, double width, double height = double.PositiveInfinity)
    { e.Measure(new Size(width, height)); e.Arrange(new Rect(0, 0, width, double.IsFinite(height) ? height : e.DesiredSize.Height)); e.UpdateLayout(); }
    private static byte[] Pixels(Visual visual, int width, int height)
    { var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var bytes = new byte[width * height * 4]; bitmap.CopyPixels(bytes, width * 4, 0); return bytes; }
    private static void Save(Visual v, int width, int height, string path)
    { var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(v); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream); }
    internal sealed class CodexFixture : ICodexSetupEnvironment
    {
        internal int Discoveries; internal bool Fail;
        internal TaskCompletionSource<bool>? Pause;
        public async Task<CodexInstallation> Discover(string desktop, string cli)
        { Discoveries++; if (Pause is not null) await Pause.Task; return new(Fail ? "" : "fixture-codex.exe", "fixture-cli.exe", "fixture", [], "fixture"); }
        public bool CanOpenDesktop(string desktop) => true;
        public Task<CodexConnectionCheck> Check() => Task.FromResult(new CodexConnectionCheck(true, true, "fixture"));
        public bool DesktopRunning(string desktop) => true;
        public Task OpenDesktop(string desktop, string inspector) => throw new InvalidOperationException("不允许打开外部应用");
    }
    internal sealed class OtherFixture(AiPlatform platform) : IAiListeningEnvironment
    {
        internal int Initializations; internal bool Fail;
        public Task<AiConnectionProbe> Probe(AiPlatformOptions options, CancellationToken cancellation) => Task.FromResult(new AiConnectionProbe(
            options with { Desktop = platform == AiPlatform.Dsh ? "fixture-dsh.exe" : "fixture-claude.exe", Home = "fixture-home" },
            new(Fail ? AiConnectionState.Failed : AiConnectionState.Ready, Fail ? "fixture失败" : "fixture准备好", "fixture", Installed: !Fail, Catalog: !Fail, Configured: !Fail)));
        public void Initialize(AiPlatformOptions options) { Initializations++; }
        public bool Running(string executable) => true;
        public void Open(string executable) => throw new InvalidOperationException("不允许打开外部应用");
    }
}
