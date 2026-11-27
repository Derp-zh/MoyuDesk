using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private AiPlatform selectedAiPlatform = AiPlatform.Codex;
    private StackPanel? aiSubpage;
    private readonly Dictionary<AiPlatform, Button> aiSubpages = new();
    private AiPlatformPage? otherAiPage;
    private Button? initializeAllAi;
    private Button? connectAllAi;
    private TextBlock? allAiSetupStatus;
    internal void RefreshAllAiInitialization()
    {
        if (initializeAllAi is not null)
        {
            initializeAllAi.IsEnabled = !controller.AllAiSetupBusy && !controller.AiSetupBusy &&
                !controller.AiModule(AiPlatform.Dsh).Busy && !controller.AiModule(AiPlatform.ClaudeCode).Busy;
            initializeAllAi.Content = controller.AllAiSetupBusy ? "正在初始化全部 AI 平台…" : "一键初始化全部 AI 平台";
            if (connectAllAi is not null) connectAllAi.IsEnabled = initializeAllAi.IsEnabled;
        }
        if (allAiSetupStatus is not null) allAiSetupStatus.Text = controller.AllAiSetupStatus;
        RefreshAiInitialization(); RefreshOtherAi(selectedAiPlatform);
    }
    internal AiPlatform SelectedAiPlatform => selectedAiPlatform;
    private sealed class AiPlatformPage(AiPlatform platform)
    {
        internal AiPlatform Platform { get; } = platform;
        internal TextBlock Result { get; } = Ui.Label("", 14);
        internal TextBlock Next { get; } = Ui.Label("", 12);
        internal TextBlock Capabilities { get; } = Ui.Label("", 12, Ui.Muted);
        internal TextBlock Observing { get; } = Ui.Label("", 11, Ui.Muted);
        internal TextBlock Test { get; } = Ui.Label("", 11, Ui.Muted);
        internal TextBlock Diagnostic { get; } = Ui.Label("", 11, Ui.Muted);
        internal List<Control> Controls { get; } = new();
        internal Dictionary<string, TextBox> Paths { get; } = new();
        internal Button? Open { get; set; }
        internal Expander? Details { get; set; }
        internal AiConnectionState? LastState { get; set; }
        internal bool Updating { get; set; }
    }
    private void BuildAiSubpages(StackPanel panel)
    {
        initializeAllAi = Ui.Button("一键初始化全部 AI 平台", async () => await controller.InitializeAllAi(), true);
        initializeAllAi.Tag = "AiInitializeAll"; initializeAllAi.HorizontalAlignment = HorizontalAlignment.Left;
        allAiSetupStatus = Ui.Label(controller.AllAiSetupStatus, 12, Ui.Muted); allAiSetupStatus.Tag = "AllAiInitializationResult";
        connectAllAi = Ui.Button("一键连接全部 AI 平台", async () => await controller.ConnectAllAi());
        connectAllAi.Tag = "AiConnectAll";
        var actions = new WrapPanel(); actions.Children.Add(initializeAllAi); actions.Children.Add(connectAllAi);
        panel.Children.Add(actions); panel.Children.Add(allAiSetupStatus);
        aiSubpages.Clear(); var tabs = new WrapPanel { Margin = new Thickness(0, 4, 0, 12), Tag = "AiPlatformTabs" };
        foreach (var platform in new[] { AiPlatform.Codex, AiPlatform.Dsh, AiPlatform.ClaudeCode })
        {
            var button = Ui.Button(platform == AiPlatform.ClaudeCode ? "Claude监听" : AiState.Name(platform) + "监听", () => SelectAiPlatform(platform));
            button.Tag = "AiPlatformTab-" + platform; tabs.Children.Add(button); aiSubpages[platform] = button;
        }
        panel.Children.Add(tabs); aiSubpage = new StackPanel(); panel.Children.Add(aiSubpage); SelectAiPlatform(selectedAiPlatform); RefreshAllAiInitialization();
    }
    internal void SelectAiPlatform(AiPlatform platform)
    {
        if (!Enum.IsDefined(platform)) return;
        selectedAiPlatform = platform; if (aiSubpage is null) return;
        aiSubpage.Children.Clear(); aiSubpage.Tag = "AiPlatformSubpage-" + platform;
        aiSetupTitle = null; otherAiPage = null;
        foreach (var entry in aiSubpages)
        {
            if (entry.Key == platform)
            {
                if (Application.Current?.TryFindResource("AccentButton") is Style accentStyle) entry.Value.Style = accentStyle;
                else { entry.Value.Background = Ui.Accent; entry.Value.Foreground = Ui.AccentText; }
            }
            else
            {
                if (Application.Current?.TryFindResource(typeof(Button)) is Style defaultStyle) entry.Value.Style = defaultStyle;
                entry.Value.ClearValue(Control.BackgroundProperty);
                entry.Value.ClearValue(Control.BorderBrushProperty);
                entry.Value.ClearValue(Control.ForegroundProperty);
            }
        }
        aiSubpage.Children.Add(platform == AiPlatform.Codex ? CodexSettings() : OtherAiSettings(platform));
        AttachHelp(aiSubpage);
    }
    private Border OtherAiSettings(AiPlatform platform)
    {
        var page = new AiPlatformPage(platform); otherAiPage = page; var name = AiState.Name(platform);
        var body = new StackPanel(); var title = Ui.Label(platform == AiPlatform.ClaudeCode ? "Claude监听 · Claude Code" : name + "监听", 16); title.FontWeight = FontWeights.SemiBold; body.Children.Add(title);
        body.Children.Add(Ui.Label(platform == AiPlatform.Dsh ? "关联 DSH 会话，按运行、提问、批准和结束自动移动任务。" : "关联 Claude Code 会话，按运行、提问、批准和结束自动移动任务。", 12, Ui.Muted));
        page.Result.Margin = new Thickness(0, 12, 0, 3); page.Result.Tag = platform + "ConnectionResult"; body.Children.Add(page.Result); body.Children.Add(page.Next);
        page.Capabilities.LineHeight = 23; page.Capabilities.Margin = new Thickness(0, 8, 0, 8); body.Children.Add(page.Capabilities);
        page.Observing.Margin = new Thickness(0, 0, 0, 8); page.Observing.Tag = platform + "ObservationStatus"; body.Children.Add(page.Observing);
        var actions = new WrapPanel { Margin = new Thickness(-3, 0, -3, 5) };
        void Action(string caption, string tag, Func<System.Threading.Tasks.Task> action)
        { var button = Ui.Button(caption, async () => { await action(); RefreshOtherAi(platform); }, caption == "一键初始化"); button.Tag = platform + tag; page.Controls.Add(button); actions.Children.Add(button); }
        Action("检查连接", "CheckConnection", () => controller.CheckOtherAi(platform));
        Action("重新连接", "Reconnect", () => controller.ReconnectOtherAi(platform));
        Action("测试已关联会话", "TestConversations", () => { controller.TestOtherAiConversations(platform); return System.Threading.Tasks.Task.CompletedTask; });
        page.Open = Ui.Button("打开 " + (platform == AiPlatform.ClaudeCode ? "Claude" : name), async () => { await controller.OpenOtherAi(platform); RefreshOtherAi(platform); });
        page.Open.Tag = platform + "OpenDesktop"; page.Controls.Add(page.Open); actions.Children.Add(page.Open); body.Children.Add(actions);
        var test = SettingsDisclosure("已关联会话检查结果", page.Test); test.Tag = platform + "ConversationTestDetails"; body.Children.Add(test);
        body.Children.Add(Ui.Label(platform == AiPlatform.ClaudeCode
            ? "初始化会备份并配置本机事件监听；新开或重载 Code 会话后生效。不会安装 Claude，也不会代答或批准。"
            : "初始化只检查并保存本机读取配置；不会修改 DSH 或代答、批准。", 11, Ui.Muted));
        var diagnostics = new StackPanel(); diagnostics.Children.Add(page.Diagnostic);
        var prefix = platform == AiPlatform.Dsh ? "dsh." : "claude.";
        AddOtherAiPath(diagnostics, page, "桌面程序", prefix + "desktop", folder: false);
        AddOtherAiPath(diagnostics, page, "本机会话目录", prefix + "home", folder: true);
        if (platform == AiPlatform.Dsh) AddOtherAiPath(diagnostics, page, "Node.js 读取运行时", "dsh.node", folder: false);
        else AddOtherAiPath(diagnostics, page, "监听记录目录", "claude.events", folder: true);
        page.Details = SettingsDisclosure("手动修复与诊断", diagnostics); body.Children.Add(page.Details);
        RefreshOtherAi(platform); return SettingsCard(body);
    }
    private void AddOtherAiPath(StackPanel body, AiPlatformPage page, string caption, string key, bool folder)
    {
        body.Children.Add(Ui.Label(caption, 12, Ui.Muted));
        var input = Ui.Input(); input.Tag = key; page.Paths[key] = input; page.Controls.Add(input);
        System.Windows.Automation.AutomationProperties.SetName(input, AiState.Name(page.Platform) + " " + caption);
        input.TextChanged += (_, _) =>
        {
            if (page.Updating) return;
            controller.Engine.State.Preferences.ConnectionConfigs[key] = input.Text.Trim(); controller.OtherAiConfigurationChanged(page.Platform);
        };
        body.Children.Add(input);
        var choose = Ui.Button("选择" + caption, () =>
        {
            string? selected = null;
            if (folder)
            { var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择" + AiState.Name(page.Platform) + caption }; if (dialog.ShowDialog(this) == true) selected = dialog.FolderName; }
            else
            { var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择" + AiState.Name(page.Platform) + caption, Filter = "Windows 程序|*.exe", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) selected = dialog.FileName; }
            if (selected is not null) input.Text = selected;
        });
        choose.Tag = key + ".choose"; page.Controls.Add(choose); body.Children.Add(choose);
    }
    internal void RefreshOtherAi(AiPlatform platform)
    {
        var page = otherAiPage; if (page is null || page.Platform != platform) return;
        var module = controller.AiModule(platform); var report = module.Report; var name = AiState.Name(platform);
        page.Result.Text = report.Message; page.Result.Foreground = report.State == AiConnectionState.Ready ? Ui.Accent : Ui.Text;
        page.Next.Text = report.Next;
        var bindings = controller.Engine.State.Tasks.SelectMany(t => t.Stages).Where(s => s.Codex?.Platform == platform).Select(s => s.Codex!).ToArray();
        var known = bindings.Count(b => module.Observer?.Peek(b.ThreadId) is { Complete: true, Phase: not CodexPhase.Unknown } snapshot && snapshot.AtUtc >= b.BoundAtUtc);
        var checkedState = report.State is not (AiConnectionState.NotChecked or AiConnectionState.Checking);
        page.Capabilities.Text = "桌面程序　" + (report.Installed ? "已找到" : !checkedState ? "待检查" : report.State == AiConnectionState.Failed ? "本次未能核实" : "未找到") +
            "\n程序运行　" + (!checkedState ? "待检查" : !report.Installed || !report.RunningKnown ? "本次未能核实" : report.Running ? "本次检查正在运行" : "本次检查未运行") +
            "\n会话目录　" + (report.Catalog ? $"本次可读 · {report.Sessions} 个" : "尚未确认可读") +
            "\n监听准备　" + (report.Configured ? "已配置" : "尚未完成") + "\n关联会话　" + (bindings.Length == 0 ? "尚未关联" : $"{known}/{bindings.Length} 个有完整监测结果");
        page.Observing.Text = name + "当前监听：" + (module.Observer?.Status ?? "等待检查连接");
        page.Test.Text = controller.OtherAiTest(platform); page.Diagnostic.Text = report.Diagnostic.Length > 0 ? report.Diagnostic : "自动寻找本机位置；只有找不到或位置变化时才需要手动修复。";
        if (page.Details is not null && page.LastState != report.State && report.State is AiConnectionState.Failed or AiConnectionState.InvalidPath or AiConnectionState.MissingRuntime or AiConnectionState.NotInstalled) page.Details.IsExpanded = true;
        page.LastState = report.State;
        foreach (var control in page.Controls) control.IsEnabled = !module.Busy && !controller.AllAiSetupBusy;
        page.Open!.Visibility = report.Installed ? Visibility.Visible : Visibility.Collapsed;
        page.Updating = true;
        try
        {
            var values = AiPlatformOptions.From(platform, controller.Engine.State.Preferences.ConnectionConfigs).Values();
            foreach (var pair in page.Paths)
                if (!pair.Value.IsKeyboardFocusWithin && pair.Value.Text != values.GetValueOrDefault(pair.Key, "")) pair.Value.Text = values.GetValueOrDefault(pair.Key, "");
        }
        finally { page.Updating = false; }
    }
}
