using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class DesktopHarness
{
    internal static int Run(string directory, bool nativeDsh = false)
    {
        var root = Path.GetFullPath(directory);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("需要新隔离目录");
        Directory.CreateDirectory(root); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        using var controller = new BoardController(Path.Combine(root, "board"), true, false);
        if (!nativeDsh) controller.SetConversationPlatformsForChecks(AiPlatform.Codex, AiPlatform.Dsh, AiPlatform.ClaudeCode);
        var config = controller.Engine.State.Preferences.ConnectionConfigs; var home = nativeDsh ? AiPaths.DshHome : Path.Combine(root, "dsh"); var events = Path.Combine(root, "events");
        var options = new AiPlatformOptions(AiPlatform.Dsh, nativeDsh ? AiDesktopDiscovery.Find(AiPlatform.Dsh, "") : Path.Combine(root, "fixture-dsh.exe"), home, DshProcess.NodePath() ?? "", "");
        var cp = new AiPlatformOptions(AiPlatform.ClaudeCode, "", Path.Combine(root, "claude"), "", events);
        foreach (var pair in options.Values().Concat(cp.Values())) config[pair.Key] = pair.Value;
        controller.SetAiEnvironmentForChecks(AiPlatform.Dsh, nativeDsh ? new DshListeningEnvironment() : new Fixture(options, true)); controller.SetAiEnvironmentForChecks(AiPlatform.ClaudeCode, new Fixture(cp, false));
        const string id = "desktop-fixture-dsh"; var claudeId = "33333333-3333-4333-8333-333333333333";
        controller.ConversationFixture = [new("11111111-1111-4111-8111-111111111111", "Codex隔离会话", 0), new(id, "DSH隔离会话", 0, AiPlatform.Dsh), new(claudeId, "Claude隔离会话", 0, AiPlatform.ClaudeCode)];
        var task = controller.Engine.NewSession(Lane.Running); task.Working.Title = "三平台设置与绑定鼠标测试"; var item = controller.Engine.Submit(task);
        var sessionDir = Path.Combine(nativeDsh ? Path.Combine(root, "unused-fixture") : home, "sessions", "project", id); Directory.CreateDirectory(sessionDir); var log = Path.Combine(sessionDir, "session.v4.jsonl");
        File.WriteAllText(log, JsonSerializer.Serialize(new { type = "session", version = 4, id, createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), isSeeded = false }) + "\n");
        var seq = 0;
        void Event(string type, object data) { if (nativeDsh) throw new InvalidOperationException("真实DSH检查不写原日志或注入原会话事件"); File.AppendAllText(log, JsonSerializer.Serialize(new { seq = seq++, type, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data }) + "\n"); }
        controller.AttachOtherAi(new(events, home, AiPlatform.Dsh), AiPlatform.Dsh);
        controller.AttachOtherAi(new(events, home, AiPlatform.ClaudeCode), AiPlatform.ClaudeCode);
        var commandFile = Path.Combine(root, "command.json"); var stateFile = Path.Combine(root, "state.json"); long command = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (File.Exists(commandFile))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(commandFile)); var row = doc.RootElement;
                    if (row.GetProperty("id").GetInt64() > command)
                    {
                        command = row.GetProperty("id").GetInt64();
                        switch (row.GetProperty("action").GetString())
                        {
                            case "editor": controller.EditTask(item.Id); break;
                            case "new":
                                var draft = controller.NewTask(Lane.Running);
                                Visual(draft.Root).OfType<TextBox>().First().Text = "原DSH新建绑定验证";
                                break;
                            case "auto-off": controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = false; break;
                            case "auto-on": controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = true; break;
                            case "empty-claude": controller.ConversationFixture = controller.ConversationFixture.Where(x => x.Platform != AiPlatform.ClaudeCode).ToArray(); break;
                            case "hide-claude": controller.SetConversationPlatformsForChecks(AiPlatform.Codex, AiPlatform.Dsh); break;
                            case "hide-all": controller.SetConversationPlatformsForChecks(); break;
                            case "show-dsh": controller.SetConversationPlatformsForChecks(AiPlatform.Dsh); break;
                            case "editor-left": if (controller.Editors.FirstOrDefault()?.Host is { } host) { host.Left = 0; host.Top = 150; } break;
                            case "settings": controller.Settings(); break;
                            case "wait": Event("turn/start", new { turn = 1 }); Event("approval/asked", new { id = "mouse-approval", toolName = "write" }); break;
                            case "resume": Event("approval/decided", new { id = "mouse-approval", outcome = "allowed-once" }); Event("assistant/message", new { turn = 1, step = 2 }); break;
                            case "claude-wait": ClaudeHook.Write(events, new(claudeId, "", 0, DateTime.UtcNow, "UserPromptSubmit", "ui-turn")); ClaudeHook.Write(events, new(claudeId, "", 0, DateTime.UtcNow, "PreToolUse", "ui-turn", "ui-question", "AskUserQuestion")); break;
                            case "claude-resume": ClaudeHook.Write(events, new(claudeId, "", 0, DateTime.UtcNow, "PostToolUse", "ui-turn", "ui-question", "AskUserQuestion")); ClaudeHook.Write(events, new(claudeId, "", 0, DateTime.UtcNow, "PostToolUse", "ui-turn", "ui-read", "Read")); break;
                            case "stop": await controller.Flush(); app.Shutdown(); return;
                        }
                    }
                }
                var live = controller.Engine.State.Tasks.Single(x => x.Id == item.Id);
                var points = new Dictionary<string, object>();
                foreach (var window in app.Windows.OfType<SurfaceWindow>().Where(x => x.IsVisible))
                foreach (var element in Visual(window.Root).OfType<FrameworkElement>().Where(x => x.IsVisible && x.ActualWidth > 1 && x.ActualHeight > 1))
                {
                    var key = element.Tag as string;
                    if (key == "SettingsNavigation") key = "nav:" + element.DataContext;
                    if (element is ListBoxItem { DataContext: CodexThread row }) key = "row:" + row.Platform;
                    if (key is null) continue;
                    try
                    {
                        var screen = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                        var hit = window.Root.InputHitTest(window.Root.PointFromScreen(screen)) as DependencyObject;
                        var inside = false;
                        for (var parent = hit; parent is not null; parent = VisualTreeHelper.GetParent(parent)) if (ReferenceEquals(parent, element)) { inside = true; break; }
                        if (inside)
                        {
                            var edge = element.PointToScreen(new Point(element.ActualWidth * .05, element.ActualHeight * .1));
                            var edgeHit = window.Root.InputHitTest(window.Root.PointFromScreen(edge)) as DependencyObject;
                            var edgeInside = false;
                            for (var parent = edgeHit; parent is not null; parent = VisualTreeHelper.GetParent(parent)) if (ReferenceEquals(parent, element)) { edgeInside = true; break; }
                            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle.ToInt64();
                            points[key] = new { x = screen.X, y = screen.Y, internalHit = true, window = handle, edge = new { x = edge.X, y = edge.Y, internalHit = edgeInside, window = handle } };
                        }
                    }
                    catch (InvalidOperationException) { }
                }
                var selected = app.Windows.OfType<SettingsSurface>().FirstOrDefault()?.SelectedAiPlatform ?? AiPlatform.Codex;
                var ghostText = app.Windows.OfType<Window>().Where(x => x.Title == "拖动绑定会话 · 名称预览" && x.Content is DependencyObject)
                    .SelectMany(x => Visual((DependencyObject)x.Content)).OfType<TextBlock>().Select(x => x.Text).ToArray();
                File.WriteAllText(stateFile + ".tmp", JsonSerializer.Serialize(new { process = Environment.ProcessId, command, selected, points, ghostText,
                    captured = System.Windows.Input.Mouse.Captured is not null, pickerPlatform = controller.BindingWindow.Current?.SelectedPlatform,
                    pickerSearch = controller.BindingWindow.Current?.Input.Text, pickerRows = controller.BindingWindow.Current?.Matches.Select(x => new { x.Id, x.Platform, x.DisplayName }).ToArray(),
                    availablePlatforms = controller.BindingWindow.Current?.AvailablePlatforms,
                    task = live.Id, lane = live.Lane.ToString(), binding = live.Stages[0].Codex, editing = controller.Editors.FirstOrDefault()?.Session.Working.Stages[0].Codex, dsh = controller.AiModule(AiPlatform.Dsh).Report, claude = controller.AiModule(AiPlatform.ClaudeCode).Report, linkFailure = ConversationLinkResolver.LastFailure, dragging = controller.ConversationDrag is not null }));
                File.Move(stateFile + ".tmp", stateFile, true);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); }
        };
        CodexInitialization.RememberPrompt(controller.Engine.State.Preferences, "deferred");
        app.Startup += async (_, _) =>
        {
            try
            {
                if (nativeDsh) controller.ConversationFixture = await controller.AiModule(AiPlatform.Dsh).Observer!.Catalog(AiPlatform.Dsh);
                await controller.Start();
                if (nativeDsh) { await controller.CheckOtherAi(AiPlatform.Dsh); await controller.CheckOtherAi(AiPlatform.ClaudeCode); await controller.InitializeAiPlatform(new CodexSetupEnvironment()); }
                controller.ConversationPreviewMode = true; controller.Settings();
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); app.Shutdown(1); }
        };
        timer.Start(); app.Run(); timer.Stop(); return 0;
    }
    private static IEnumerable<DependencyObject> Visual(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Fixture(AiPlatformOptions options, bool installed) : IAiListeningEnvironment
    {
        public Task<AiConnectionProbe> Probe(AiPlatformOptions value, CancellationToken cancellation) => Task.FromResult(new AiConnectionProbe(options,
            installed ? new(AiConnectionState.Ready, "DSH 隔离连接已准备好", "此窗口使用隔离来源检查操作流程。", Installed: true, Running: true, Catalog: true, Configured: true, Sessions: 1)
            : new(AiConnectionState.NotInstalled, "未找到 Claude", "本测试不安装 Claude，可选择已有安装位置。")));
        public void Initialize(AiPlatformOptions value) { }
        public bool Running(string executable) => true;
        public void Open(string executable) => throw new InvalidOperationException("桌面夹具不打开外部应用");
    }
}
