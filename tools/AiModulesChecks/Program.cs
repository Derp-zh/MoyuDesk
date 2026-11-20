using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class Program
{
    private static string NativeTestThread() =>
        Guid.TryParse(Environment.GetEnvironmentVariable("TASKBOARD_TEST_CODEX_THREAD_ID"), out var id)
            ? id.ToString()
            : throw new ArgumentException("请通过 TASKBOARD_TEST_CODEX_THREAD_ID 指定测试会话编号");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--sidebar-probe" && args[1] == "--data-dir")
        {
            var directory = Path.GetFullPath(args[2]);
            if (!directory.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new IOException("必须指定新的隔离目录");
            Directory.CreateDirectory(directory);
            var source = new CodexSidebarSource();
            for (var n = 0; n < 5; n++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var snapshot = source.Read([NativeTestThread()], CancellationToken.None).GetAwaiter().GetResult();
                var json = JsonSerializer.Serialize(new { snapshot, elapsedMs = clock.Elapsed.TotalMilliseconds });
                File.AppendAllText(Path.Combine(directory, "snapshots.jsonl"), json + "\n"); Console.WriteLine(json);
                Thread.Sleep(400);
            }
            return 0;
        }
        if (args.Length == 3 && args[0] == "--clipboard-recovery" && args[1] == "--data-dir") { ClipboardRecoveryChecks.Run(args[2]).GetAwaiter().GetResult(); return 0; }
        if (args.Length == 5 && args[0] == "--dsh-target-probe" && args[1] == "--data-dir")
        { try { DshTargetChecks.Probe(args[2], int.Parse(args[3]), int.Parse(args[4])).GetAwaiter().GetResult(); return 0; } catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; } }
        if (args.Length == 3 && args[0] == "--dsh-environment" && args[1] == "--data-dir") { DshEnvironmentChecks.Inspect(args[2]).GetAwaiter().GetResult(); return 0; }
        if (args.Length == 3 && args[0] == "--dsh-runtime-verify" && args[1] == "--data-dir") { DshEnvironmentChecks.VerifyRuntime(args[2]).GetAwaiter().GetResult(); return 0; }
        if (args.Length == 3 && args[0] == "--desktop" && args[1] == "--data-dir") return DesktopHarness.Run(args[2]);
        if (args.Length == 3 && args[0] == "--native-dsh-desktop" && args[1] == "--data-dir") return DesktopHarness.Run(args[2], true);
        if (args.Length == 3 && args[0] == "--native-codex-behavior" && args[1] == "--data-dir") return CodexBehaviorNativeDesktop.Run(args[2], NativeTestThread());
        if (args.Length == 3 && args[0] == "--native-codex-daily" && args[1] == "--data-dir") return CodexDailyNativeDesktop.Run(args[2], NativeTestThread());
        if (args.Length == 3 && args[0] == "--native-codex-daily-resume" && args[1] == "--data-dir") return CodexDailyNativeDesktop.Run(args[2], NativeTestThread(), true);
        if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("需显式指定新的隔离--data-dir");
        var root = Path.GetFullPath(args[1]);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("必须使用test-output下新空目录");
        Directory.CreateDirectory(root); var checks = new List<string>(); var exit = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(root, checks); Console.WriteLine(JsonSerializer.Serialize(new { passed = true, count = checks.Count, result = Path.Combine(root, "results.json") })); }
            catch (Exception e) { exit = 1; Console.Error.WriteLine(e); }
            finally { app.Shutdown(); }
        }));
        app.Run(); return exit;
    }
    private static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        await RecoveryChecks.Run(root, checks);
        await DshParityChecks.Run(root, checks);
        await PickerUxChecks.Run(root, checks);
        await CodexBehaviorControllerChecks.Run(root, checks);
        await CodexSidebarChecks.Run(root, checks);
        using var controller = new BoardController(Path.Combine(root, "board"), true, true);
        var config = controller.Engine.State.Preferences.ConnectionConfigs;
        config["codex.keep"] = "original-codex"; config["claude.keep"] = "original-claude";
        var dsh = controller.AiModule(AiPlatform.Dsh); var claude = controller.AiModule(AiPlatform.ClaudeCode);
        var dshOptions = new AiPlatformOptions(AiPlatform.Dsh, "D:/isolated/dsh.exe", Path.Combine(root, "dsh"), "D:/isolated/node.exe", "");
        var claudeOptions = new AiPlatformOptions(AiPlatform.ClaudeCode, "D:/isolated/claude.exe", Path.Combine(root, "claude"), "", Path.Combine(root, "events"));
        var dshEnv = new FakeEnvironment(dshOptions); var claudeEnv = new FakeEnvironment(claudeOptions);
        await controller.CheckOtherAi(AiPlatform.Dsh, initialize: true, environment: dshEnv);
        Check(dsh.Report.State == AiConnectionState.Ready && dshEnv.Initializations == 1 && dshEnv.Probes == 2, "DSH初始化实际经过检查、初始化、复查流程");
        Check(config["dsh.home"] == dshOptions.Home && config["codex.keep"] == "original-codex" && config["claude.keep"] == "original-claude", "DSH仅保存自身配置且不覆盖其他平台");
        Check(controller.Store.Load().Preferences.ConnectionConfigs["dsh.home"] == dshOptions.Home, "初始化配置经过原Flush链真实落盘");
        var before = dshEnv.Initializations; await controller.CheckOtherAi(AiPlatform.Dsh, environment: dshEnv);
        Check(dshEnv.Initializations == before, "检查连接不执行初始化副作用");
        await controller.CheckOtherAi(AiPlatform.ClaudeCode, initialize: true, environment: claudeEnv);
        Check(claude.Report.State == AiConnectionState.Ready && claudeEnv.Initializations == 1 && config["dsh.home"] == dshOptions.Home, "Claude独立初始化且保留DSH配置");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); dshEnv.Gate = gate.Task;
        var running = controller.CheckOtherAi(AiPlatform.Dsh, environment: dshEnv);
        Check(dsh.Busy && !claude.Busy, "一个平台检查中不占用另一个平台忙碌状态");
        await controller.CheckOtherAi(AiPlatform.ClaudeCode, environment: claudeEnv);
        Check(claude.Report.State == AiConnectionState.Ready && !claude.Busy && dsh.Busy, "DSH异步检查时Claude仍可独立完成检查");
        config["dsh.home"] = Path.Combine(root, "new-home"); gate.SetResult(); await running; dshEnv.Gate = null;
        Check(config["dsh.home"].EndsWith("new-home") && dsh.Report.State == AiConnectionState.NotChecked && !dsh.Busy, "迟到检查拒绝覆盖用户新配置并给出重检提示");
        dshEnv.Error = true; await controller.CheckOtherAi(AiPlatform.Dsh, environment: dshEnv); dshEnv.Error = false;
        Check(dsh.Report.State == AiConnectionState.Failed && claude.Report.State == AiConnectionState.Ready, "DSH失败不覆盖Claude成功结果");
        var unavailable = new FakeEnvironment(claudeOptions) { Installed = false }; await controller.CheckOtherAi(AiPlatform.ClaudeCode, true, unavailable);
        Check(unavailable.Initializations == 0 && claude.Report.State == AiConnectionState.NotInstalled, "未安装Claude时准确提示且不创建安装/初始化动作");
        await controller.OpenOtherAi(AiPlatform.ClaudeCode, claudeEnv);
        Check(claudeEnv.Opens == 1 && dshEnv.Opens == 0, "显式打开只调用对应平台入口且不启动其他平台");
        claudeEnv.IsRunning = true; await controller.OpenOtherAi(AiPlatform.ClaudeCode, claudeEnv);
        Check(claudeEnv.Opens == 1, "已有对应程序运行时不重复打开第二个实例");
        await controller.CheckOtherAi(AiPlatform.ClaudeCode);
        Check(claude.Report.State == AiConnectionState.Failed, "隔离控制器拒绝检查或修改真实外部平台配置");

        var calls = 0;
        async Task<CodexThread[]> Catalog() { calls++; await Task.Yield(); return calls == 1 ? [] : [new("native-dsh", "DSH目录", 0, AiPlatform.Dsh)]; }
        await controller.LoadPlatformConversations(AiPlatform.Dsh, Catalog); await controller.LoadPlatformConversations(AiPlatform.Dsh, Catalog); await controller.LoadPlatformConversations(AiPlatform.Dsh, Catalog);
        Check(calls == 2, "空目录不缓存、成功目录复用且不重复读取");
        var failures = 0; dsh.Catalog = null;
        Task<CodexThread[]> Failing() { failures++; return failures == 1 ? Task.FromException<CodexThread[]>(new IOException("fixture")) : Task.FromResult(new[] { new CodexThread("native-retry", "重试", 0, AiPlatform.Dsh) }); }
        try { await controller.LoadPlatformConversations(AiPlatform.Dsh, Failing); } catch (IOException) { }
        await controller.LoadPlatformConversations(AiPlatform.Dsh, Failing); Check(failures == 2, "目录失败不污染下次重试");
        dsh.Catalog = null; var wrongSource = false;
        try { await controller.LoadPlatformConversations(AiPlatform.Dsh, () => Task.FromResult(new[] { new CodexThread(Guid.NewGuid().ToString(), "错来源", 0, AiPlatform.ClaudeCode) })); } catch (IOException) { wrongSource = true; }
        Check(wrongSource, "拒绝其他平台冒充当前平台目录");
        dsh.Catalog = null; var catalogGate = new TaskCompletionSource<CodexThread[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateCatalog = controller.LoadPlatformConversations(AiPlatform.Dsh, () => catalogGate.Task);
        controller.OtherAiConfigurationChanged(AiPlatform.Dsh); catalogGate.SetResult([new("old-home", "旧目录", 0, AiPlatform.Dsh)]);
        var lateRefused = false; try { await lateCatalog; } catch (OperationCanceledException) { lateRefused = true; }
        Check(lateRefused, "目录读取期间改配置后拒绝迟到旧目录");
        controller.TestOtherAiConversations(AiPlatform.ClaudeCode);

        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "跨平台绑定测试";
        session.Working.Stages[0].Codex = new() { Platform = AiPlatform.Dsh, ThreadId = "native-binding" }; var task = controller.Engine.Submit(session);
        var binding = task.Stages[0].Codex!;
        Check(controller.Engine.BindStageConversation(task.Id, task.Stages[0].Id, binding.BindingId, "another-native", "DSH拖动", AiPlatform.Dsh), "列表拖动保存链支持DSH原生非UUID编号");
        var shared = Guid.NewGuid().ToString(); binding = task.Stages[0].Codex!;
        Check(controller.Engine.BindStageConversation(task.Id, task.Stages[0].Id, binding.BindingId, shared, "Claude拖动", AiPlatform.ClaudeCode), "列表拖动保存链支持Claude并准确更新平台");
        Check(controller.OtherAiTest(AiPlatform.ClaudeCode).Contains("关联已变化"), "从零绑定变为有绑定也使旧空测试结果失效");
        binding = task.Stages[0].Codex!; binding.AwaitingUserAction = true; binding.Phase = CodexPhase.WaitingForInput;
        Check(controller.Engine.BindStageConversation(task.Id, task.Stages[0].Id, binding.BindingId, shared, "Codex同编号", AiPlatform.Codex) && task.Stages[0].Codex is { Platform: AiPlatform.Codex, AwaitingUserAction: false }, "同编号跨平台拖动重建绑定且不继承等待保护");
        Check(!controller.Engine.BindStageConversation(task.Id, task.Stages[0].Id, task.Stages[0].Codex!.BindingId, "bad-id", "无效Claude", AiPlatform.ClaudeCode), "Claude拖动拒绝无效来源编号");

        // Exercise the real independent observers, dispatcher, card refresh and save chain.
        var events = Path.Combine(root, "observer-events"); var nativeHome = Path.Combine(root, "observer-dsh"); var nativeSession = "module-dsh"; var claudeSession = Guid.NewGuid().ToString();
        var ds = controller.Engine.NewSession(Lane.Running); ds.Working.Title = "仅DSH待处理"; ds.Working.Stages[0].Codex = new() { Platform = AiPlatform.Dsh, ThreadId = nativeSession }; var dsItem = controller.Engine.Submit(ds);
        var cs = controller.Engine.NewSession(Lane.Running); cs.Working.Title = "仅Claude待处理"; cs.Working.Stages[0].Codex = new() { Platform = AiPlatform.ClaudeCode, ThreadId = claudeSession }; var csItem = controller.Engine.Submit(cs);
        var dir = Path.Combine(nativeHome, "sessions", "project", nativeSession); Directory.CreateDirectory(dir); var log = Path.Combine(dir, "session.v4.jsonl");
        File.WriteAllText(log, JsonSerializer.Serialize(new { type = "session", version = 4, id = nativeSession, createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), isSeeded = false }) + "\n");
        void Dsh(int seq, string type, object data) => File.AppendAllText(log, JsonSerializer.Serialize(new { seq, type, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data }) + "\n");
        void Claude(string kind, string id = "", string name = "") => ClaudeHook.Write(events, new(claudeSession, "", 0, DateTime.UtcNow, kind, "own-turn", id, name));
        var dsReader = new AiPlatformObserver(events, nativeHome, AiPlatform.Dsh); var csReader = new AiPlatformObserver(events, nativeHome, AiPlatform.ClaudeCode);
        controller.AttachOtherAi(dsReader, AiPlatform.Dsh); controller.AttachOtherAi(csReader, AiPlatform.ClaudeCode);
        Dsh(0, "turn/start", new { turn = 1 }); Dsh(1, "approval/asked", new { id = "approval", toolName = "write" });
        Claude("UserPromptSubmit"); Claude("PreToolUse", "question", "AskUserQuestion");
        await Until(() => dsItem.Lane == Lane.Action && csItem.Lane == Lane.Action);
        Check(dsItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval && csItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput, "两个独立观察器准确区分各自批准与回答");
        var wholeBoardBefore = JsonSerializer.Serialize(controller.Engine.State);
        controller.TestOtherAiConversations(AiPlatform.Dsh); controller.TestOtherAiConversations(AiPlatform.ClaudeCode);
        Check(controller.OtherAiTest(AiPlatform.Dsh).Contains("仅DSH待处理") && !controller.OtherAiTest(AiPlatform.Dsh).Contains("仅Claude待处理") && controller.OtherAiTest(AiPlatform.ClaudeCode).Contains("仅Claude待处理"), "会话测试按平台隔离并读取实际观察器已有快照");
        Check(JsonSerializer.Serialize(controller.Engine.State) == wholeBoardBefore, "测试已关联会话不改任务、等待或保存字段");
        controller.OtherAiConfigurationChanged(AiPlatform.Dsh);
        Check(ReferenceEquals(claude.Observer, csReader) && csItem.Stages[0].Codex!.AwaitingUserAction && dsItem.Stages[0].Codex!.AwaitingUserAction, "单个平台配置失效保留等待且不重启其他平台观察器");
        Claude("PostToolUse", "question", "AskUserQuestion"); Claude("PostToolUse", "actual-tool", "Read");
        await Until(() => csItem.Lane == Lane.Running);
        Check(csItem.Stages[0].Codex!.PendingRequests.Count == 0 && dsItem.Lane == Lane.Action, "DSH停止观察期间Claude仍可实际恢复并正确移列");
        await controller.RestartCodex();
        Check(ReferenceEquals(claude.Observer, csReader), "单独重连Codex不重建Claude或DSH观察器");
        var saved = await controller.Flush(); Check(saved && controller.Store.Load().Tasks.Single(x => x.Id == csItem.Id).Lane == Lane.Running, "独立模块恢复经原Controller保存链落盘");
        var changed = controller.Engine.Edit(csItem.Id); changed.Working.Stages[0].Codex = null; controller.Engine.Submit(changed);
        Check(controller.OtherAiTest(AiPlatform.ClaudeCode).Contains("关联已变化"), "解绑后旧会话测试结果立即标记失效");

        // Native environment implementations use only fixture locations, never real settings.
        var fixtureExe = Path.Combine(root, "fixture-claude.exe"); File.WriteAllBytes(fixtureExe, [(byte)'M', (byte)'Z']);
        var fixtureOptions = claudeOptions with { Desktop = fixtureExe, Home = Path.Combine(root, "claude-environment"), Events = Path.Combine(root, "claude-environment-events") };
        var realClaudeEnvironment = new ClaudeListeningEnvironment(); var probe = await realClaudeEnvironment.Probe(fixtureOptions, CancellationToken.None);
        Check(probe.Report.State == AiConnectionState.NeedsInitialization && !Directory.Exists(fixtureOptions.Home), "Claude连接检查只读且缺配置时不创建目录");
        realClaudeEnvironment.Initialize(fixtureOptions); probe = await realClaudeEnvironment.Probe(fixtureOptions, CancellationToken.None);
        Check(probe.Report.Configured && probe.Report.State == AiConnectionState.NeedsOpen, "Claude初始化配置后准确区分已配置与原程序未运行");
        var settingsFile = Path.Combine(fixtureOptions.Home, "settings.json"); var bytesBefore = File.ReadAllBytes(settingsFile);
        await realClaudeEnvironment.Probe(fixtureOptions, CancellationToken.None);
        Check(bytesBefore.SequenceEqual(File.ReadAllBytes(settingsFile)), "重复检查Claude连接不重写其设置");
        Check(!ClaudeHook.Check(fixtureOptions.Home, fixtureOptions.Events, "D:/moved/Taskboard.exe", out _), "移动程序位置后检测旧Hook路径并要求重新初始化");
        File.WriteAllText(settingsFile, "{\"disableAllHooks\":true}");
        Check(!ClaudeHook.Check(fixtureOptions.Home, fixtureOptions.Events, Environment.ProcessPath!, out var disabled) && disabled.Contains("停用"), "Claude全局停用Hook时不伪报连接准备成功");
        var invalidHome = Path.Combine(root, "invalid-claude"); Directory.CreateDirectory(invalidHome); File.WriteAllText(Path.Combine(invalidHome, "settings.json"), "invalid-json");
        var invalidBefore = File.ReadAllBytes(Path.Combine(invalidHome, "settings.json")); var refused = false;
        try { ClaudeHook.Install(invalidHome, fixtureOptions.Events); } catch (JsonException) { refused = true; }
        Check(refused && invalidBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(invalidHome, "settings.json"))), "Claude配置损坏时拒绝覆盖并保留原文件");

        var nativeOptions = dshOptions with { Desktop = fixtureExe, Home = nativeHome, Node = DshProcess.NodePath() ?? "" };
        var nativeProbe = await new DshListeningEnvironment().Probe(nativeOptions, CancellationToken.None);
        Check(nativeProbe.Report.Catalog && nativeProbe.Report.Configured && nativeProbe.Report.Sessions == 1 && !nativeProbe.Report.Running, "DSH真实环境检查验证Node读取组件、v4目录且不冒充程序运行");
        await Ui(controller, root, checks);
        var dataGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); dshEnv.Gate = dataGate.Task;
        var oldDataCheck = controller.CheckOtherAi(AiPlatform.Dsh, environment: dshEnv);
        await controller.ChangeDataDirectory(Path.Combine(root, "switched-data")); dataGate.SetResult(); await oldDataCheck; dshEnv.Gate = null;
        Check(dsh.Report.State == AiConnectionState.NotChecked && !dsh.Busy, "隔离数据目录切换后拒绝旧检查结果且释放忙碌状态");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { version = typeof(BoardController).Assembly.GetName().Version?.ToString(), passed = true, scope = "Isolated setup/environment/controller/WPF checks; no original AI desktop, pointer, clipboard or real configuration operations", checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15); while (!condition() && DateTime.UtcNow < until) await Task.Delay(40);
        if (!condition()) throw new TimeoutException("独立观察链超时");
    }
    private static async Task Ui(BoardController controller, string root, List<string> checks)
    {
        var window = new SettingsSurface(controller) { SuppressActivation = true }; window.SelectSection("AI平台接入情况");
        foreach (var platform in new[] { AiPlatform.Codex, AiPlatform.Dsh, AiPlatform.ClaudeCode })
        {
            window.SelectAiPlatform(platform);
            window.Root.Measure(new Size(760, 680)); window.Root.Arrange(new Rect(0, 0, 760, 680)); window.Root.UpdateLayout();
            var buttons = Visual<Button>(window.Root).ToArray();
            var expected = platform == AiPlatform.Codex ? new[] { "AiInitializeAll", "AiInitialize", "AiTestConversations" } : new[] { "AiInitializeAll", platform + "CheckConnection", platform + "Reconnect", platform + "TestConversations" };
            if (expected.Any(tag => !buttons.Any(b => b.Tag?.ToString() == tag))) throw new Exception("设置操作缺失：" + platform);
            if (buttons.Count(b => b.Tag?.ToString()?.StartsWith("AiPlatformTab-") == true) != 3) throw new Exception("三平台独立子页导航缺失");
            window.Root.Measure(new Size(760, 680)); window.Root.Arrange(new Rect(0, 0, 760, 680)); window.Root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(760, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window.Root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(root, "settings-" + platform + ".png"))) png.Save(stream);
            checks.Add(platform + "设置独立子页、动作控件和760×680离屏布局通过");
            if (platform == AiPlatform.Codex)
            {
                var supplement = Visual<CheckBox>(window.Root).Single(x => Equals(x.Tag, "CodexBehaviorSupplement"));
                if (!controller.Engine.State.Preferences.CodexBehaviorSupplement || supplement.IsChecked != true ||
                    Visual<CheckBox>(window.Root).Any(x => Equals(x.Tag, "CodexNotificationWaiting"))) throw new Exception("本地行为默认开关或旧通知补充替换错误");
                checks.Add("Codex本地行为补充默认开启，旧Windows通知补充控件已移除");
                var dshObserver = controller.AiModule(AiPlatform.Dsh).Observer;
                var claudeObserver = controller.AiModule(AiPlatform.ClaudeCode).Observer;
                var oldCodex = controller.Codex;
                supplement.IsChecked = false; supplement.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await Until(() => !ReferenceEquals(oldCodex, controller.Codex) && controller.Codex.BehaviorStatus.Contains("已关闭"));
                await controller.Flush();
                if (controller.Store.Load().Preferences.CodexBehaviorSupplement ||
                    !ReferenceEquals(dshObserver, controller.AiModule(AiPlatform.Dsh).Observer) ||
                    !ReferenceEquals(claudeObserver, controller.AiModule(AiPlatform.ClaudeCode).Observer)) throw new Exception("本地行为关闭保存或独立平台保护失败");
                checks.Add("实际本地行为控件关闭只重建Codex观察器，隔离保存回读为关闭，其他平台观察器保持");
                oldCodex = controller.Codex;
                supplement.IsChecked = true; supplement.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await Until(() => !ReferenceEquals(oldCodex, controller.Codex) && !controller.Codex.BehaviorStatus.Contains("已关闭"));
                await controller.Flush();
                if (!controller.Store.Load().Preferences.CodexBehaviorSupplement) throw new Exception("本地行为重新开启未保存");
                checks.Add("实际本地行为控件重新开启并落盘，不操作原客户端或系统通知权限");
            }
            if (platform == AiPlatform.Dsh)
            {
                var options = AiPlatformOptions.From(platform, controller.Engine.State.Preferences.ConnectionConfigs);
                var environment = new FakeEnvironment(options); controller.SetAiEnvironmentForChecks(platform, environment);
                buttons.Single(b => Equals(b.Tag, "AiInitializeAll")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => environment.Initializations == 1 && !controller.AiModule(platform).Busy);
                if (controller.AiModule(platform).Report.State != AiConnectionState.Ready ||
                    controller.Store.Load().Preferences.ConnectionConfigs["dsh.home"] != options.Home) throw new Exception("DSH实际初始化控件未经过保存及检查链");
                checks.Add("离屏点击统一初始化控件，DSH经过检查/初始化/复查并独立回读保存");
            }
        }
        window.SelectAiPlatform(AiPlatform.Dsh); window.SelectSection("主设置"); window.SelectSection("AI平台接入情况");
        if (window.SelectedAiPlatform != AiPlatform.Dsh) throw new Exception("设置来回切换丢失平台子页");
        checks.Add("返回AI平台监听页面保留当前子页且不混入其他平台内容"); window.Destroy();
        var dshModule = controller.AiModule(AiPlatform.Dsh); var previous = dshModule.Report;
        dshModule.Report = new(AiConnectionState.Failed, "DSH已找到，目录暂不可读", "检查目录", "fixture-directory-error", Installed: true, Running: true);
        var failureWindow = new SettingsSurface(controller) { SuppressActivation = true };
        failureWindow.SelectSection("AI平台接入情况"); failureWindow.SelectAiPlatform(AiPlatform.Dsh);
        failureWindow.Root.Measure(new Size(760, 680)); failureWindow.Root.Arrange(new Rect(0, 0, 760, 680)); failureWindow.Root.UpdateLayout();
        if (!Visual<TextBlock>(failureWindow.Root).Any(x => x.Text.Contains("桌面程序　已找到"))) throw new Exception("DSH目录检查失败不得掩盖已经找到原程序的分项证据");
        if (!Visual<Expander>(failureWindow.Root).Any(x => Equals(x.Header, "手动修复与诊断") && x.IsExpanded)) throw new Exception("DSH错误诊断未自动展开");
        checks.Add("DSH目录失败仍显示已发现程序，错误诊断自动可见"); failureWindow.Destroy(); dshModule.Report = previous;
        var prompt = await controller.PromptAiInitialization(false);
        if (prompt is null || Visual<ComboBox>(prompt.Root).Any(x => x.Tag?.ToString() == "AiInitializationPlatform") || !Visual<Button>(prompt.Root).Any(x => Equals(x.Tag, "AiInitializeNow"))) throw new Exception("首次初始化缺少统一入口");
        checks.Add("首次初始化统一处理三个平台且稍后配置逻辑保留"); prompt.Destroy();
    }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class FakeEnvironment(AiPlatformOptions options) : IAiListeningEnvironment
    {
        internal int Probes, Initializations, Opens; internal bool Installed = true, Error, IsRunning; internal Task? Gate;
        public async Task<AiConnectionProbe> Probe(AiPlatformOptions ignored, CancellationToken cancellation)
        {
            Probes++; if (Gate is not null) await Gate;
            if (Error) throw new IOException("isolated-fixture");
            return new(options, Installed ? new(AiConnectionState.Ready, "隔离连接就绪", "下一步", Installed: true, Running: true, Catalog: true, Configured: true, Sessions: 1) : new(AiConnectionState.NotInstalled, "未安装", "手动选择"));
        }
        public void Initialize(AiPlatformOptions value) => Initializations++;
        public bool Running(string executable) => IsRunning;
        public void Open(string executable) => Opens++;
    }
}
