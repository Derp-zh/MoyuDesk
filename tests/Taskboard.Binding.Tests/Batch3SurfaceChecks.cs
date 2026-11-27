using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class Batch3SurfaceChecks
{
    internal static int Run(string output)
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
                try { summary = await RunOnStaAsync(output); }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Batch3SurfaceChecks failed: " + failure.Message, failure);
        Console.WriteLine(summary);
        return 0;
    }

    internal static int RunProfile20(string output)
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
                try { summary = await Profile20Async(output); }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Profile20 failed: " + failure.Message, failure);
        Console.WriteLine(summary);
        return 0;
    }

    private static async Task<string> Profile20Async(string output)
    {
        var boardDir = Path.Combine(output, "profile20-board");
        var controller = new BoardController(boardDir, isolated: true, smoke: true) { OffscreenNoShow = true };
        try
        {
            // Warm up with a tiny task so JIT / WPF Theme compilation doesn't skew long-text numbers.
            var warmSession = controller.Engine.NewSession(Lane.Running);
            warmSession.Working.Title = "预热任务";
            warmSession.Working.Stages[0].Text = "短文本预热";
            controller.Engine.Submit(warmSession);
            var warmTaskId = controller.Engine.State.Tasks[0].Id;

            static string BuildLongChineseText(int targetChars)
            {
                var sb = new System.Text.StringBuilder(targetChars + 256);
                var line = 1;
                while (sb.Length < targetChars)
                {
                    sb.Append($"第{line++:D3}段任务详情记录：这是一段包含中文标点、数字20261121以及长句换行的真实长文本内容，用于模拟用户在日常工作流中粘贴的需求文档、会议纪要与阶段执行日志。");
                    if (line % 3 == 0) sb.Append('\n');
                }
                return sb.ToString();
            }

            // Heavy task A: 5 stages x 12,000 chars, 2 running + 3 paused
            var sA = controller.Engine.NewSession(Lane.Running);
            sA.Working.Title = "重度长文本任务A（2运行+3未运行）";
            sA.Working.Stages[0].Name = "阶段0";
            sA.Working.Stages[0].Text = BuildLongChineseText(12000);
            sA.Working.Stages[0].IsRunning = true;
            for (var j = 1; j <= 4; j++)
            {
                sA.Working.Stages.Add(new Stage
                {
                    Number = j,
                    Name = "阶段" + j,
                    Text = BuildLongChineseText(12000),
                    IsRunning = j == 1
                });
            }
            controller.Engine.Submit(sA);
            var taskAId = sA.Working.Id;

            // Heavy task B: 5 stages x 12,000 chars, ALL 5 running
            var sB = controller.Engine.NewSession(Lane.Running);
            sB.Working.Title = "重度长文本任务B（5运行）";
            sB.Working.Stages[0].Name = "阶段0";
            sB.Working.Stages[0].Text = BuildLongChineseText(12000);
            sB.Working.Stages[0].IsRunning = true;
            for (var j = 1; j <= 4; j++)
            {
                sB.Working.Stages.Add(new Stage
                {
                    Number = j,
                    Name = "阶段" + j,
                    Text = BuildLongChineseText(12000),
                    IsRunning = true
                });
            }
            controller.Engine.Submit(sB);
            var taskBId = sB.Working.Id;

            // Control task C: 5 stages x 5 chars, ALL 5 running
            var sC = controller.Engine.NewSession(Lane.Running);
            sC.Working.Title = "对照组C（5运行短文本）";
            sC.Working.Stages[0].Name = "阶段0";
            sC.Working.Stages[0].Text = "短文本0";
            sC.Working.Stages[0].IsRunning = true;
            for (var j = 1; j <= 4; j++)
                sC.Working.Stages.Add(new Stage { Number = j, Name = "阶段" + j, Text = "短文本" + j, IsRunning = true });
            controller.Engine.Submit(sC);
            var taskCId = sC.Working.Id;

            async Task<object> MeasureOpen(Guid taskId, string label)
            {
                var total = Stopwatch.StartNew();
                var tEdit = Stopwatch.StartNew();
                var session = controller.Engine.Edit(taskId);
                tEdit.Stop();

                var tCtor = Stopwatch.StartNew();
                var editor = new EditorSurface(controller, session);
                tCtor.Stop();

                var tWin = Stopwatch.StartNew();
                var win = new TaskEditorWindow(editor) { SuppressActivation = true, ShowActivated = false, Left = -10000, Top = -10000 };
                tWin.Stop();

                var started = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                win.LiveScrollStarted += () => started.TrySetResult(total.Elapsed.TotalMilliseconds);

                var tShowSync = Stopwatch.StartNew();
                win.Show();
                tShowSync.Stop();

                var hydratedAfterShow = Descendants<StageDetailsEditor>(editor).Select(x =>
                {
                    var tb = x.Children.OfType<TextBlock>().Single();
                    return $"h{(x.IsHydrated ? 1 : 0)}:i{x.Input.Text.Length}:v{(int)x.Input.Visibility}:p{tb.Text.Length}:ah{x.ActualHeight:F0}";
                }).ToArray();

                var completed = await Task.WhenAny(started.Task, Task.Delay(5000));
                var liveScrollStartedMs = completed == started.Task ? started.Task.Result : -1;
                total.Stop();

                // Measure an extra Render priority tick after LiveScrollStarted
                var tPostRender = Stopwatch.StartNew();
                await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                tPostRender.Stop();

                // Verify all stage texts are immediately intact via CurrentText & Working model,
                // and also after explicit on-demand hydration (focus/edit/save path)
                var currentTexts = Descendants<StageDetailsEditor>(editor).Select(x => x.CurrentText).ToArray();
                var stageTexts = session.Working.Stages.Select(x => x.Text).ToArray();
                var textIntactBeforeHydrate = currentTexts.Length == stageTexts.Length && currentTexts.Zip(stageTexts).All(p => p.First == p.Second);
                editor.EnsureAllStagesHydrated();
                var hydratedTexts = Descendants<StageDetailsEditor>(editor).Select(x => x.Input.Text).ToArray();
                var textIntact = textIntactBeforeHydrate && hydratedTexts.Length == stageTexts.Length && hydratedTexts.Zip(stageTexts).All(p => p.First == p.Second);

                win.Destroy(false);
                controller.Engine.Discard(session);
                await Task.Delay(50);

                return new
                {
                    label,
                    totalChars = stageTexts.Sum(x => x.Length),
                    editMs = Math.Round(tEdit.Elapsed.TotalMilliseconds, 2),
                    editorCtorMs = Math.Round(tCtor.Elapsed.TotalMilliseconds, 2),
                    windowCtorMs = Math.Round(tWin.Elapsed.TotalMilliseconds, 2),
                    showSyncMs = Math.Round(tShowSync.Elapsed.TotalMilliseconds, 2),
                    prepToRevealStartMs = liveScrollStartedMs >= 0 ? Math.Round(liveScrollStartedMs - (tEdit.Elapsed.TotalMilliseconds + tCtor.Elapsed.TotalMilliseconds + tWin.Elapsed.TotalMilliseconds + tShowSync.Elapsed.TotalMilliseconds), 2) : -1,
                    endToEndRevealStartMs = Math.Round(liveScrollStartedMs, 2),
                    postRenderTickMs = Math.Round(tPostRender.Elapsed.TotalMilliseconds, 2),
                    hydratedAfterShow,
                    textIntact
                };
            }

            var rWarm = await MeasureOpen(warmTaskId, "WarmShortTask");
            var rC = await MeasureOpen(taskCId, "ControlC_5RunShort");
            var rA1 = await MeasureOpen(taskAId, "HeavyA_2Run3Paused_Run1");
            var rA2 = await MeasureOpen(taskAId, "HeavyA_2Run3Paused_Run2");
            var rB1 = await MeasureOpen(taskBId, "HeavyB_5Run_Run1");
            var rB2 = await MeasureOpen(taskBId, "HeavyB_5Run_Run2");

            // Micro-benchmark: isolate TextBox vs TextBlock shaping cost in WPF
            var sample650 = BuildLongChineseText(650);
            var sample12k = BuildLongChineseText(12000);
            static double BenchElement(Func<FrameworkElement> factory)
            {
                var sw = Stopwatch.StartNew();
                var el = factory();
                el.Measure(new Size(390, double.PositiveInfinity));
                el.Arrange(new Rect(0, 0, 390, el.DesiredSize.Height));
                sw.Stop();
                return Math.Round(sw.Elapsed.TotalMilliseconds, 2);
            }
            var micro = new
            {
                textBlock650Ms = BenchElement(() => new TextBlock { Text = sample650, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, TextWrapping = TextWrapping.Wrap }),
                textBlock12kMs = BenchElement(() => new TextBlock { Text = sample12k, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, TextWrapping = TextWrapping.Wrap }),
                textBox650Ms = BenchElement(() => { var t = Ui.Input(sample650, true); t.FontFamily = new FontFamily("Microsoft YaHei UI"); t.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; return t; }),
                textBox12kMs = BenchElement(() => { var t = Ui.Input(sample12k, true); t.FontFamily = new FontFamily("Microsoft YaHei UI"); t.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; return t; }),
            };

            var report = new { rWarm, rC, rA1, rA2, rB1, rB2, micro };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(output, "profile20.json"), json);
            return json;
        }
        finally
        {
            controller.Dispose();
        }
    }

    internal static async Task RunInlineAsync(string output)
    {
        Directory.CreateDirectory(output);
        var summary = await RunOnStaAsync(output);
        Console.WriteLine(summary);
    }

    private static async Task<string> RunOnStaAsync(string output)
    {
        var hostParams = new HwndSourceParameters("Batch3OffscreenHost")
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

        // #29: Expanded domestic & international IME immunity + strict non-IME guards
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Windows.UI.Core.CoreWindow", "TextInputHost", 0x08000000), "#29 微软拼音 TextInputHost 候选框免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Microsoft.IME.UIManager.CandidateWindow.Host", "ChsIME"), "#29 Microsoft.IME 候选窗口免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("PopupHost", "ctfmon", 0x08000000), "#29 系统 ctfmon 悬浮条免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "DoubaoInput"), "#29 豆包输入法 DoubaoInput 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "DBInput"), "#29 豆包输入法 DBInput 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Popup", "DoubaoHelper"), "#29 豆包输入法 DoubaoHelper 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "Doubao", 0x08000000), "#29 豆包主进程带WS_EX_NOACTIVATE悬浮条免疫");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "Doubao", 0), "#29 豆包大聊天主窗口不被误放行");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("WeTypeCandidate", "WeType"), "#29 微信输入法 WeType 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "wetype_renderer"), "#29 微信输入法 wetype_renderer 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "wetype_server"), "#29 微信输入法 wetype_server 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "WeChatInput"), "#29 微信输入法 WeChatInput 免疫");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("WeChatMainWndForPC", "WeChat", 0), "#29 微信聊天主窗口不被误判为IME");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("ZhipuInputWnd", "ZhipuInput"), "#29 智谱AI输入法 ZhipuInput 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("AutoTyperBar", "AutoTyper"), "#29 智谱AI输入法 AutoTyper 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("FlyIME_Comp", "FlyIME"), "#29 讯飞输入法 FlyIME 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("FlyPopup", "FlyDaemon"), "#29 讯飞输入法 FlyDaemon 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("iFlyIMEWnd", "iFlyIME"), "#29 讯飞输入法 iFlyIME 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("iFlytekInputWnd", "iFlytekInput"), "#29 讯飞输入法 iFlytekInput 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SogouPY", "SogouCloud"), "#29 搜狗拼音候选框免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SoPY_Status", "SGTool"), "#29 搜狗拼音状态栏免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SogouBroker", "SogouImeBroker"), "#29 搜狗 SogouImeBroker 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SGMyInputWnd", "SGMyInput"), "#29 搜狗 SGMyInput 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "SGWebRender"), "#29 搜狗 SGWebRender 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("BaiduIME", "BaiduPinyinCrossover"), "#29 百度输入法 BaiduPinyinCrossover 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("QQPY_Candidate", "QQPinyin"), "#29 QQ拼音候选框免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("QQStatus", "QQPinyinSvc"), "#29 QQ拼音服务进程免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("ShouXinWnd", "ShouXin"), "#29 手心输入法 ShouXin 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("WeaselCandidate", "WeaselServer"), "#29 Rime/小狼毫候选框免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("PIMECandidate", "pime-server"), "#29 PIME pime-server 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("GoogleIMEJaCand", "GoogleIMEJaRenderer"), "#29 Google日文输入法 Renderer 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("GoogleIMEJaConv", "GoogleIMEJaConverter"), "#29 Google日文输入法 Converter 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("MozcCandidateWindow", "mozc_renderer"), "#29 Mozc 候选框免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("MozcServerWnd", "mozc_server"), "#29 Mozc server 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("ATOKCand", "atok32"), "#29 ATOK 输入法免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("MSCTFIME UI", "IMJPUEX"), "#29 微软日文 IME (IMJPUEX) 免疫");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("IME", "IMKRMIG"), "#29 微软韩文 IME (IMKRMIG) 免疫");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Windows.UI.Core.CoreWindow", "CalculatorApp"), "#29 普通UWP应用不被误判为IME");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "SogouExplorer"), "#29 搜狗浏览器不被误判为搜狗输入法");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "chrome"), "#29 Chrome浏览器不被误判为IME");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("CASCADIA_HOSTING_WINDOW_CLASS", "WindowsTerminal"), "#29 Windows终端不被误判为IME");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("TXGuiFoundation", "QQ"), "#29 QQ主窗口不被误判为QQ输入法");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Notepad", "notepad"), "#29 普通记事本窗口正常触发外部点击收起");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("UnrealWindow", "GameClient"), "#29 全屏游戏窗口正常触发外部点击收起");

        // #30: MousePressGate & wraparound / deduplication
        var gate = new MousePressGate();
        Assert(gate.Accept(0, 1000, 1, 1), "#30 首次钩子按下被接受");
        Assert(!gate.Accept(0, 1003, 2, 1), "#30 同物理按下的原始输入(RawInput)正确去重");
        Assert(gate.Accept(0, 1005, 1, 1), "#30 同源快速连续点击不误吞");

        // #14 + #15 + #13 + #19 + Item 10 + #20 offscreen controller checks
        var boardDir = Path.Combine(output, "board");
        var controller = new BoardController(boardDir, isolated: true, smoke: true) { OffscreenNoShow = true };
        try
        {
            // #14: One-shot window width + single-layer capsule clip + smooth search fade + zero LiveBackdrop rebuilds
            controller.Ball.PrepareExpandedVisuals(true);
            var expandedGrid = Descendants<Grid>(controller.Ball.Root).FirstOrDefault(x => x.Children.OfType<TextBox>().Any());
            Assert(expandedGrid is not null && expandedGrid.Visibility == Visibility.Visible && expandedGrid.Opacity == 0.0,
                "#14 展开准备期搜索框透明占位(Opacity=0)，避免首帧闪现");
            Assert(SurfaceWindow.BallSearchOpacity(true, 0.0, 0.0) == 0.0
                && SurfaceWindow.BallSearchOpacity(true, 0.5, 0.0) > 0.3
                && SurfaceWindow.BallSearchOpacity(true, 1.0, 0.0) == 1.0,
                "#14 展开阶段搜索框随胶囊伸展平滑淡入到1");
            Assert(SurfaceWindow.BallSearchOpacity(false, 0.0, 1.0) == 1.0
                && SurfaceWindow.BallSearchOpacity(false, 0.3, 1.0) < 0.6
                && SurfaceWindow.BallSearchOpacity(false, 0.6, 1.0) == 0.0,
                "#14 收起阶段搜索框平滑淡出到0");
            Assert(SurfaceWindow.BallCapsuleProgress(0) == 0 && SurfaceWindow.BallCapsuleProgress(0.5) > 0.5 && SurfaceWindow.BallCapsuleProgress(1) == 1,
                "#14 单层胶囊缓出进度曲线平滑连续");

            // Ensure LiveBackdrop updates capsule geometry strictly in-place without rebuilding visual tree
            new WindowInteropHelper(controller.Ball).EnsureHandle();
            controller.Ball.Width = 370;
            controller.Ball.SetCapsuleRevealState(58, 0);
            controller.Ball.EnsureLiveBackdrop();
            var rebuildsBefore = controller.Ball.BackdropRebuildCount;
            var inPlaceBefore = controller.Ball.BackdropInPlaceUpdateCount;
            foreach (var w in new[] { 96.0, 168.0, 250.0, 330.0, 370.0 })
            {
                controller.Ball.SetCapsuleRevealState(w, SurfaceWindow.BallSearchOpacity(true, (w - 58) / 312.0, 0));
                controller.Ball.UpdateGlassGeometry();
            }
            var regionsExpanded = controller.Ball.GlassRegions().ToArray();
            Assert(regionsExpanded.Length == 1 && Math.Abs(regionsExpanded[0].Bounds.Width - 370) < 0.1,
                "#14 胶囊伸展结束玻璃区域同步到完整370宽");
            Assert(controller.Ball.BackdropRebuildCount == rebuildsBefore && controller.Ball.BackdropInPlaceUpdateCount > inPlaceBefore,
                $"#14 胶囊伸展过程 LiveBackdrop 全程就地更新零销毁重建(rebuilds={controller.Ball.BackdropRebuildCount}, inPlace={controller.Ball.BackdropInPlaceUpdateCount})");
            Assert(controller.Ball.LiveBallFrameResizeCount == 0, "#14 悬浮球展开动画帧内零 SetWindowPos 缩窗");
            controller.Ball.SetCapsuleRevealState(58, 0);
            controller.Ball.Width = 58;
            controller.Ball.UpdateGlassGeometry();

            // #19: 0% transparency skips HostBackdropBrush in-place without recreating HWND; >0% restores it
            var sampleCol = controller.Columns.First();
            var colHwnd = new WindowInteropHelper(sampleCol).EnsureHandle();
            controller.SetGlassTransparency(20);
            sampleCol.EnsureLiveBackdrop();
            Assert(!sampleCol.SkipsDesktopBlur && sampleCol.BackdropUsesDesktopBlur,
                "#19 20% 透明时列窗口启用 HostBackdropBrush 实时桌面模糊");
            controller.SetGlassTransparency(0);
            Assert(new WindowInteropHelper(sampleCol).Handle == colHwnd && sampleCol.SkipsDesktopBlur && !sampleCol.BackdropUsesDesktopBlur
                && sampleCol.BackdropMode.Contains("跳过桌面模糊", StringComparison.Ordinal),
                "#19 0% 透明(100%不透明)时同窗口不重建且跳过 HostBackdropBrush 模糊采样");
            controller.SetGlassTransparency(20);
            Assert(new WindowInteropHelper(sampleCol).Handle == colHwnd && !sampleCol.SkipsDesktopBlur && sampleCol.BackdropUsesDesktopBlur,
                "#19 透明度恢复 >0% 时平滑恢复 HostBackdropBrush 实时模糊");

            var ballHwnd = new WindowInteropHelper(controller.Ball).EnsureHandle();
            controller.SetBallTransparency(0);
            Assert(new WindowInteropHelper(controller.Ball).Handle == ballHwnd && controller.Ball.SkipsDesktopBlur && !controller.Ball.BackdropUsesDesktopBlur,
                "#19 悬浮球 0% 透明时同样跳过桌面实时模糊");
            controller.SetBallTransparency(40);
            Assert(!controller.Ball.SkipsDesktopBlur && controller.Ball.BackdropUsesDesktopBlur,
                "#19 悬浮球恢复 40% 透明时恢复实时模糊");

            var settingsProbe = new SettingsSurface(controller) { SuppressActivation = true };
            try
            {
                settingsProbe.SelectSection("窗口");
                Layout(settingsProbe.Root, 760, 680);
                var settingsTexts = Descendants<TextBlock>(settingsProbe.Root).Select(x => x.Text).ToArray();
                Assert(settingsTexts.Count(t => t.Contains("0% 为纯色不透明（跳过桌面实时模糊，更省性能）", StringComparison.Ordinal)) >= 3,
                    "#19 设置窗口页三个透明度滑块均已注明 0% 纯色不透明跳过模糊说明");
            }
            finally { settingsProbe.Destroy(false); }

            // #13: Deep TSF / IMM32 / outer WM_SETFOCUS / VK_CONTROL bridge checks while preserving HasFocusWithinCore=false & WM_CHAR
            var hasFocusMethod = typeof(GlassContentHost).GetMethod("HasFocusWithinCore", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(hasFocusMethod is not null, "#13 GlassContentHost.HasFocusWithinCore 方法存在");
            var hostField = typeof(SurfaceWindow).GetField("contentHost", BindingFlags.Instance | BindingFlags.NonPublic);
            var contentHostInstance = hostField?.GetValue(sampleCol);
            Assert(contentHostInstance is not null && (bool)hasFocusMethod!.Invoke(contentHostInstance, null)! == false,
                "#13 严格保留 GlassContentHost.HasFocusWithinCore() == false 防吞 WM_CHAR");
            var beforeSync = sampleCol.InputFocusSyncCount;
            sampleCol.SynchronizeInputFocus();
            Assert(sampleCol.InputFocusSyncCount == beforeSync + 1, "#13 SurfaceWindow 输入焦点同步入口正常工作");
            Assert(Native.IsTsfControlKeyBridgeInstalled, "#13 WPF VK_CONTROL TSF 桥接已注册");

            {
                var fgBeforeIme = Native.GetForegroundWindow();
                var imeSurf = new SurfaceWindow(300, 180)
                {
                    Left = -20000,
                    Top = -20000,
                    SuppressActivation = true,
                    ShowActivated = false,
                    FloatingAnimations = false
                };
                try
                {
                    var imeBox = Ui.Input("初始");
                    imeSurf.Root.Children.Add(imeBox);
                    imeSurf.Show();
                    await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var outerHwnd = new WindowInteropHelper(imeSurf).Handle;
                    var childHwnd = imeSurf.ContentHandle;
                    Assert(outerHwnd != IntPtr.Zero && childHwnd != IntPtr.Zero, "#13 离屏 SurfaceWindow 与 GlassContentHost 子窗句柄有效");

                    var himc = Native.ImmGetContext(childHwnd);
                    if (himc != IntPtr.Zero) Native.ImmReleaseContext(childHwnd, himc);
                    Assert(himc != IntPtr.Zero, "#13 GlassContentHost 子输入窗具备有效默认 IMM32 上下文 (ImmGetContext != 0)");

                    System.Windows.Input.Keyboard.Focus(imeBox);
                    await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    Assert(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, imeBox), "#13 聚焦 SurfaceWindow 内 TextBox 成功");
                    Assert(Native.GetFocus() == childHwnd, "#13 聚焦 TextBox 后 Win32 GetFocus() 严格指向 GlassContentHost ContentHandle");
                    Assert(System.Windows.Input.InputMethod.GetIsInputMethodEnabled(imeBox), "#13 TextBox InputMethod 处于启用态");

                    var emptyDimPtr = GetWpfEmptyDimIdentity();
                    var dimBefore = Native.GetTsfFocusedDocumentMgrIdentity();
                    Assert(dimBefore != IntPtr.Zero && (emptyDimPtr == IntPtr.Zero || dimBefore != emptyDimPtr),
                        "#13 聚焦 TextBox 后 ITfThreadMgr::GetFocus() 指向有效非空文档管理器且非 EmptyDim");

                    // Simulate Windows sending WM_SETFOCUS to the outer glass shell (e.g. on Activate/click/Alt+Tab)
                    SendMessageNative(outerHwnd, 0x0007, IntPtr.Zero, IntPtr.Zero);
                    await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var dimAfter = Native.GetTsfFocusedDocumentMgrIdentity();
                    Assert(ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, imeBox),
                        "#13 外层收到 WM_SETFOCUS 后不破坏 Keyboard.FocusedElement");
                    Assert(Native.GetFocus() == childHwnd,
                        "#13 外层收到 WM_SETFOCUS 后 Win32 焦点严格保持在 ContentHandle");
                    Assert(!IsKeyboardProviderActive(outerHwnd) && IsKeyboardProviderActive(childHwnd),
                        "#13 外层 WM_SETFOCUS 设 handled=true 后不激活外层空壳也不反激活子窗输入提供器");
                    Assert(dimAfter != IntPtr.Zero && (emptyDimPtr == IntPtr.Zero || dimAfter != emptyDimPtr),
                        "#13 外层收到 WM_SETFOCUS 后 ITfThreadMgr::GetFocus() 保持有效文档管理器不退回 EmptyDim");

                    // Verify VK_CONTROL TSF bridge forwards raw VK_CONTROL (0x11) + real scan code
                    var forwardBefore = Native.TsfControlKeyForwardCount;
                    var ctrlDown = new MSG { hwnd = childHwnd, message = 0x0100, wParam = new IntPtr(0x11), lParam = new IntPtr(0x001D0001) };
                    var ctrlUp = new MSG { hwnd = childHwnd, message = 0x0101, wParam = new IntPtr(0x11), lParam = unchecked(new IntPtr((int)0xC01D0001)) };
                    var fwdDown = Native.TryForwardTsfControlKey(ref ctrlDown, out _);
                    var fwdUp = Native.TryForwardTsfControlKey(ref ctrlUp, out _);
                    Assert(fwdDown && fwdUp && Native.TsfControlKeyForwardCount == forwardBefore + 2,
                        "#13 VK_CONTROL 按下与抬起以原始 0x11 + 扫描码 0x1D 准确桥接至 TSF ITfKeystrokeMgr");

                    // Combo shortcut keys (Ctrl+A / Ctrl+C / Ctrl+V / Ctrl+Z) must pass straight through to WPF untouched
                    foreach (var vk in new[] { 0x41, 0x43, 0x56, 0x5A })
                    {
                        var comboDown = new MSG { hwnd = childHwnd, message = 0x0100, wParam = new IntPtr(vk), lParam = new IntPtr(1) };
                        var comboUp = new MSG { hwnd = childHwnd, message = 0x0101, wParam = new IntPtr(vk), lParam = unchecked(new IntPtr((int)0xC0000001)) };
                        Assert(!Native.TryForwardTsfControlKey(ref comboDown, out _) && !Native.TryForwardTsfControlKey(ref comboUp, out _),
                            $"#13 常规组合键字符消息 0x{vk:X2} (Ctrl+A/C/V/Z) 完全不被 VK_CONTROL 桥接拦截");
                    }

                    // Disabled IME inputs (e.g. hotkey recorder) must NEVER be intercepted
                    System.Windows.Input.InputMethod.SetIsInputMethodEnabled(imeBox, false);
                    Assert(!Native.TryForwardTsfControlKey(ref ctrlDown, out _),
                        "#13 禁用输入法的输入框(如快捷键录制框)不被 VK_CONTROL TSF 桥接拦截");
                    System.Windows.Input.InputMethod.SetIsInputMethodEnabled(imeBox, true);

                    // Verify WM_CHAR Chinese character input remains intact through the real Unicode message pump
                    imeBox.CaretIndex = imeBox.Text.Length;
                    Native.PostMessage(childHwnd, 0x0102, new IntPtr('中'), new IntPtr(1));
                    await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert(imeBox.Text.Contains('中'), $"#13 修复 TSF 焦点与 Ctrl 桥接后 WM_CHAR 中文上屏完全保持正常(text={imeBox.Text})");
                    Assert(Native.GetForegroundWindow() == fgBeforeIme, "#13 离屏 TSF/IME 诊断全程不抢占用户前台窗口");
                }
                finally { imeSurf.Destroy(false); }
            }

            // #15 (0.9.56 Feedback #1): Zero taskbar windows & no Alt+Tab proxy window; foreground loss still collapses cleanly
            Assert(!controller.HasTaskSwitcherWindow, "#15 已彻底移除 Alt+Tab 离屏代理窗 (HasTaskSwitcherWindow == false)");
            Assert(controller.Columns.All(c => !c.ShowInTaskbar), "#15 所有独立列表窗口 ShowInTaskbar=false");
            Assert(!controller.Ball.ShowInTaskbar, "#15 悬浮球 ShowInTaskbar=false");
            Assert(Application.Current.Windows.Cast<Window>().All(w => !w.ShowInTaskbar),
                "#15 全程序不存在任何 ShowInTaskbar=true 窗口，任务栏绝对无窗口");
            Assert(controller.Columns.All(c =>
            {
                var h = new WindowInteropHelper(c).EnsureHandle();
                var ex = Native.GetWindowLongPtr(h, -20).ToInt64();
                return (ex & 0x80L) != 0 && (ex & 0x40000L) == 0;
            }), "#15 所有列表窗口均带 WS_EX_TOOLWINDOW 且无 WS_EX_APPWINDOW");

            controller.SetExpanded(true);
            controller.MarkAppForeground();
            Assert(controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => false, _ => false),
                "#15 切到外部前台窗口且非IME/切换器时允许自动收起到悬浮球");
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => true, _ => false),
                "#15/#29 外部前台为IME窗口时不触发收起");
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => false, _ => true),
                "#15 按住浏览任务切换器浮层期间不提前收起");
            controller.SuppressForegroundCollapse(500);
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64, _ => false, _ => false),
                "#15 内部弹窗关闭/切窗保护期内不误收起");
            controller.SetExpanded(false);
            Assert(!controller.Expanded, "#15 已收起到悬浮球");

            // Brand icon sanity check in Batch3 as well
            using (var trayIconCheck = Brand.TrayIcon())
                Assert(Brand.OrbGlyph() is Image { Source: not null } && trayIconCheck.Width > 0,
                    "#BrandIcon Batch3 核验新版猫咪悬浮球图标与托盘图标均可正常加载");

            // Item 10: Hiding ball while edge-docked & edge-hidden must stay completely hidden and never bounce back
            controller.SetBallEdgeAutoHide(true);
            controller.Ball.PreparePlacement();
            controller.Ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000), animate: false);
            Assert(controller.Ball.IsEdgeHidden, "Item10 开启吸附时悬浮球可正常进入边缘隐藏态");
            controller.HideBall();
            Assert(!controller.Engine.State.Preferences.BallVisible
                && !controller.Ball.IsVisible
                && !controller.Ball.IsEdgeHidden
                && !controller.Ball.IsEdgeIdleRunning
                && !controller.Ball.IsEdgeAnimating,
                "Item10 边缘吸附隐藏态下点击隐藏悬浮球后立即停定时器/动画、复位边缘态并隐藏");
            // Simulate menu close, settings refresh, edge timer tick, workspace expand/collapse — must never bounce back
            controller.Ball.SetEdgeMenuOpen(false);
            controller.Ball.RefreshEdgeAutoHide();
            controller.Ball.AdvanceEdgeIdle(DateTime.UtcNow.AddSeconds(10));
            controller.Ball.SetExpanded(true);
            controller.Ball.SetExpanded(false);
            controller.SetExpanded(true);
            controller.SetExpanded(false);
            controller.ApplySettings();
            Assert(!controller.Engine.State.Preferences.BallVisible
                && !controller.Ball.IsVisible
                && !controller.Ball.IsEdgeHidden
                && !controller.Ball.IsEdgeIdleRunning,
                "Item10 隐藏悬浮球后经菜单关闭/定时器/展开收起/应用设置均绝不反弹重新显示");
            controller.ShowBall();
            Assert(controller.Engine.State.Preferences.BallVisible, "Item10 点击恢复悬浮球可正常恢复 BallVisible=true");

            // #20: Long-text performance & equivalence checks
            var longLine = new string('测', 8000) + "\n" + string.Join("\n", Enumerable.Range(1, 120).Select(i => $"第{i}行超长阶段详情文本内容ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"));
            for (var i = 0; i < 18; i++)
            {
                var s = controller.Engine.NewSession(i % 2 == 0 ? Lane.Running : Lane.Action);
                s.Working.Title = $"长文本压测任务-{i}";
                s.Working.Stages[0].Name = "阶段0";
                s.Working.Stages[0].Text = longLine;
                s.Working.Stages[0].IsRunning = true;
                for (var j = 1; j <= 4; j++)
                {
                    s.Working.Stages.Add(new Stage
                    {
                        Number = j,
                        Name = "阶段" + j,
                        Text = longLine,
                        IsRunning = j == 1
                    });
                }
                controller.Engine.Submit(s);
            }
            var heavyTask = controller.Engine.State.Tasks[0];

            var noTitle = new WorkItem
            {
                Title = "",
                Stages = [new Stage { IsRunning = true, Text = "\n  \n  首行真实标题内容\n" + longLine }]
            };
            Assert(noTitle.DisplayTitle == "首行真实标题内容", "#20 DisplayTitle 零Split扫描准确提取首行标题");

            var jsonWatch = Stopwatch.StartNew();
            var jsonClone = JsonSerializer.Deserialize<BoardState>(JsonSerializer.SerializeToUtf8Bytes(controller.Engine.State, Copies.Json), Copies.Json)!;
            jsonWatch.Stop();

            var fastWatch = Stopwatch.StartNew();
            var fastClone = Copies.Of(controller.Engine.State);
            fastWatch.Stop();

            var jsonBytesOriginal = JsonSerializer.Serialize(controller.Engine.State, Copies.Json);
            var jsonBytesFast = JsonSerializer.Serialize(fastClone, Copies.Json);
            Assert(jsonBytesOriginal == jsonBytesFast, "#20 Copies.Of 快速深拷贝与JSON深拷贝完全等价");
            Assert(Copies.Same(controller.Engine.State.Tasks[0], fastClone.Tasks[0]), "#20 Copies.Same 快速比较与原任务完全一致");
            Assert(fastWatch.Elapsed.TotalMilliseconds < jsonWatch.Elapsed.TotalMilliseconds,
                $"#20 Copies.Of 全状态深拷贝显著提速(fast={fastWatch.Elapsed.TotalMilliseconds:F2}ms vs json={jsonWatch.Elapsed.TotalMilliseconds:F2}ms)");

            var detailsWatch = Stopwatch.StartNew();
            var detailsText = new StageDetailsText(longLine, 14, Ui.Text, true, 42, reserveJumpSpace: true);
            detailsText.Measure(new Size(220, double.PositiveInfinity));
            detailsText.Arrange(new Rect(0, 0, 220, detailsText.DesiredSize.Height));
            var firstPassMs = detailsWatch.Elapsed.TotalMilliseconds;
            detailsWatch.Restart();
            detailsText.Measure(new Size(220, double.PositiveInfinity));
            detailsText.Arrange(new Rect(0, 0, 220, detailsText.DesiredSize.Height));
            var cachedPassMs = detailsWatch.Elapsed.TotalMilliseconds;
            Assert(detailsText.DisplayedLines == 2 && firstPassMs < 200 && cachedPassMs < 5,
                $"#20 StageDetailsText 长文本预截断与缓存生效(first={firstPassMs:F2}ms, cached={cachedPassMs:F2}ms)");

            var openWatch = Stopwatch.StartNew();
            var editSession = controller.Engine.Edit(heavyTask.Id);
            var editor = new EditorSurface(controller, editSession);
            stageHost.Children.Add(editor);
            Layout(editor, 430, EditorSurface.DefaultHeight);
            openWatch.Stop();
            Assert(openWatch.Elapsed.TotalMilliseconds < 800,
                $"#20 点击打开多阶段长文本编辑窗体耗时正常({openWatch.Elapsed.TotalMilliseconds:F2}ms)");

            editor.Session.Working.Title = "长文本压测任务-已保存";
            var saveWatch = Stopwatch.StartNew();
            var savedOk = await controller.SubmitSession(editor.Session);
            saveWatch.Stop();
            Assert(savedOk && controller.Engine.State.Tasks.Any(x => x.Id == heavyTask.Id && x.Title == "长文本压测任务-已保存")
                && saveWatch.Elapsed.TotalMilliseconds < 1000,
                $"#20 点击保存多阶段长文本任务耗时正常({saveWatch.Elapsed.TotalMilliseconds:F2}ms)");
            stageHost.Children.Remove(editor);
            editor.Destroy();

            controller.SetFocusMode(true);
            var runningCol = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Running);
            Layout(runningCol.Root, 520, 620);
            var initialCards = Descendants<TaskCard>(runningCol.Root).ToArray();
            Assert(initialCards.Length >= 2, "#20 专注模式已渲染多张长文本任务卡片");
            var untouchedCard = initialCards.Last();
            var beforeBuilds = untouchedCard.BuildCount;
            var draggedId = ((WorkItem)initialCards[0].DataContext).Id;
            var draggedItem = (WorkItem)initialCards[0].DataContext;

            var dragWatch = Stopwatch.StartNew();
            Assert(runningCol.BeginCardDrag(draggedId), "#20 开始拖动长文本卡片");
            for (var step = 0; step < 12; step++)
            {
                var targetBefore = step % 2 == 0 ? controller.Engine.State.Tasks[2].Id : (Guid?)null;
                runningCol.SetCardPreview(draggedItem, targetBefore);
                Layout(runningCol.Root, 520, 620);
            }
            runningCol.EndCardDrag();
            Layout(runningCol.Root, 520, 620);
            dragWatch.Stop();
            Assert(untouchedCard.BuildCount == beforeBuilds,
                $"#20 拖动占位移动期间未破坏/重建未变动卡片(BuildCount={untouchedCard.BuildCount}=={beforeBuilds})");
            Assert(dragWatch.Elapsed.TotalMilliseconds < 1000,
                $"#20 12次拖动占位移动与重排总耗时正常({dragWatch.Elapsed.TotalMilliseconds:F2}ms)");

            // #20 (Re-opened real UI end-to-end open + lossless hydration + clean undo stack)
            {
                var realWatch = Stopwatch.StartNew();
                var realSession = controller.Engine.Edit(heavyTask.Id);
                var realEditor = new EditorSurface(controller, realSession);
                var realWin = new TaskEditorWindow(realEditor) { SuppressActivation = true, ShowActivated = false, Left = -10000, Top = -10000 };
                var revealed = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                realWin.LiveScrollStarted += () => revealed.TrySetResult(realWatch.Elapsed.TotalMilliseconds);
                realWin.Show();
                var done = await Task.WhenAny(revealed.Task, Task.Delay(4000));
                var realEndToEndMs = done == revealed.Task ? revealed.Task.Result : 9999;
                realWatch.Stop();

                var stageEdList = Descendants<StageDetailsEditor>(realEditor).ToArray();
                Assert(realEndToEndMs < 700, $"#20 真实 TaskEditorWindow 打开 5 阶段长文本端到端耗时达标({realEndToEndMs:F1}ms < 700ms)");
                Assert(stageEdList.Length == 5 && stageEdList.All(x => x.CurrentText == longLine),
                    "#20 真实编辑窗首帧未水合前 CurrentText 与 Working 数据 100% 完整无丢字");
                stageEdList[0].ActivateLiveInput(focus: false);
                Assert(stageEdList[0].IsHydrated && stageEdList[0].Input.Text == longLine && !stageEdList[0].Input.CanUndo,
                    "#20 点击/激活长文本运行中阶段无缝水合完整原文且不污染撤销栈");
                realWin.Destroy(false);
                controller.Engine.Discard(realSession);
            }

            // #25: Built-in Action lane Pin + Mouse Click-Through + Floating Unpin Mini-Window + Transparency + Hotkey State Retention
            {
                controller.SetFocusMode(false);
                var actionCol = controller.ActionColumn!;
                Assert(actionCol.PinToggleButton is not null, "#25 仅内置“待操作”列标题栏包含“常驻”按钮");
                Assert(controller.Columns.Where(c => c.Definition.BuiltinLane != Lane.Action).All(c => c.PinToggleButton is null),
                    "#25 其余内置列与自定义列均不显示“常驻”按钮");
                Assert(Math.Abs(controller.Engine.State.Preferences.PinnedActionTransparency - 70) < 0.01,
                    "#25 Preferences.PinnedActionTransparency 默认值严格为 70%");

                var invalidPref = Copies.Of(controller.Engine.State);
                invalidPref.Preferences.PinnedActionTransparency = 105;
                var rejected = false;
                try { Taskboard.Core.Validation.Check(invalidPref); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "#25 Validation 拦截超出 0–100 范围的 PinnedActionTransparency");

                var settings25 = new SettingsSurface(controller) { SuppressActivation = true };
                try
                {
                    settings25.SelectSection("窗口");
                    Layout(settings25.Root, 760, 680);
                    var pinSlider = Descendants<Slider>(settings25.Root).SingleOrDefault(x => Equals(x.Tag, "PinnedActionTransparencySlider"));
                    Assert(pinSlider is not null && Math.Abs(pinSlider.Value - 70) < 0.01,
                        "#25 设置“窗口”页新增“待操作常驻穿透透明度”独立滑块且默认 70%");
                    pinSlider!.Value = 55;
                    Assert(Math.Abs(controller.Engine.State.Preferences.PinnedActionTransparency - 55) < 0.01,
                        "#25 拖动独立滑块实时更新 PinnedActionTransparency");
                    pinSlider.Value = 70;
                }
                finally { settings25.Destroy(false); }

                controller.SetExpanded(true);
                var actionHwnd = new WindowInteropHelper(actionCol).EnsureHandle();
                actionCol.EnsureLiveBackdrop();
                controller.ToggleActionPinned();
                Assert(controller.ActionPinned && actionCol.IsPinnedOverlay && actionCol.IsClickThrough
                    && Native.IsClickThrough(actionHwnd)
                    && Math.Abs(actionCol.GlassTransparency - 70) < 0.01,
                    "#25 开启常驻后待操作列启用 WS_EX_TRANSPARENT 鼠标穿透并应用 70% 常驻透明度");

                // Verify #19 synergy: 0% pinned transparency skips desktop blur
                controller.SetPinnedActionTransparency(0);
                Assert(actionCol.SkipsDesktopBlur && !actionCol.BackdropUsesDesktopBlur,
                    "#25/#19 常驻透明度调至 0% 时联动跳过桌面实时模糊");
                controller.SetPinnedActionTransparency(70);
                Assert(!actionCol.SkipsDesktopBlur && actionCol.BackdropUsesDesktopBlur,
                    "#25/#19 常驻透明度调回 70% 时恢复实时模糊");

                var overlay = controller.ActionPinOverlay;
                Assert(overlay is not null && overlay.Topmost && overlay.LogicalVisible,
                    "#25 开启常驻后同步创建并显示置顶轻量迷你解除浮窗");
                var overlayHwnd = new WindowInteropHelper(overlay!).EnsureHandle();
                var overlayEx = Native.GetWindowLongPtr(overlayHwnd, -20).ToInt64();
                Assert((overlayEx & 0x80L) != 0 && !Native.IsClickThrough(overlayHwnd),
                    "#25 迷你解除浮窗具备 WS_EX_TOOLWINDOW 且无 WS_EX_TRANSPARENT（仅解除按钮可点）");

                // #25 Circular 26x26 original vector pushpin button consistency + enlarged 18x18 Viewbox + smooth top cap without side ears
                var headerPin = actionCol.PinToggleButton!;
                var overlayPin = overlay!.UnpinButton;
                Assert(Math.Abs(headerPin.Width - 26) < 0.1 && Math.Abs(headerPin.Height - 26) < 0.1
                    && Math.Abs(overlay.Width - 26) < 0.1 && Math.Abs(overlay.Height - 26) < 0.1
                    && Math.Abs(overlayPin.Width - 26) < 0.1 && Math.Abs(overlayPin.Height - 26) < 0.1,
                    "#25 列标题常驻按钮与迷你解除浮窗严格共用 26x26 正圆尺寸");
                Assert(headerPin.Content is Viewbox headerVb && overlayPin.Content is Viewbox overlayVb
                    && Math.Abs(headerVb.Width - 18) < 0.1 && Math.Abs(headerVb.Height - 18) < 0.1
                    && Math.Abs(overlayVb.Width - 18) < 0.1 && Math.Abs(overlayVb.Height - 18) < 0.1,
                    "#25 常驻按钮与解除浮窗均采用放大后的 18x18 原创矢量图钉图标");
                var headerPinPaths = Descendants<System.Windows.Shapes.Path>((DependencyObject)headerPin.Content).ToArray();
                var overlayPinPaths = Descendants<System.Windows.Shapes.Path>((DependencyObject)overlayPin.Content).ToArray();
                Assert(headerPinPaths.Any(p => Equals(p.Tag, "ActionPinBody")) && headerPinPaths.Any(p => Equals(p.Tag, "ActionPinNeedle"))
                    && overlayPinPaths.Any(p => Equals(p.Tag, "ActionPinBody")) && overlayPinPaths.Any(p => Equals(p.Tag, "ActionPinNeedle")),
                    "#25 原创图钉矢量图标包含平滑无耳顶帽钉身与细锐针尖且两处共用同一工厂");
                double SampleBodyWidthAtY(double y)
                {
                    var minX = double.PositiveInfinity;
                    var maxX = double.NegativeInfinity;
                    for (var x = 0.0; x <= 16.0; x += 0.1)
                    {
                        if (ActionPinIcon.PinBodyGeometry.FillContains(new Point(x, y)))
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                        }
                    }
                    return double.IsFinite(minX) ? maxX - minX : 0;
                }
                var topCapWidth = SampleBodyWidthAtY(2.6);
                var stemMidWidth = SampleBodyWidthAtY(4.8);
                var flangeWidth = SampleBodyWidthAtY(9.2);
                var pinTotalBounds = Rect.Union(ActionPinIcon.PinBodyGeometry.Bounds, ActionPinIcon.PinNeedleGeometry.Bounds);
                Assert(topCapWidth > 3.5 && topCapWidth <= stemMidWidth + 0.05 && flangeWidth >= 9.0 && pinTotalBounds.Height >= 13.0,
                    $"#25 图钉右上角顶帽已去除左右凸起耳朵(topW={topCapWidth:F2}<=stemW={stemMidWidth:F2})、保留下端宽挡片(flangeW={flangeWidth:F2})且占满约85%画布(h={pinTotalBounds.Height:F2}/16)");

                // #25 Click-through mouse wheel scrolling (overflow intercepts & scrolls; no-overflow, outside, hidden, unpinned pass through)
                actionCol.Left = -10000;
                actionCol.Top = -10000;
                actionCol.Width = 240;
                actionCol.Height = 360;
                Layout(actionCol.Root, 240, 360);
                var actionScroll = actionCol.ListScrollViewer;
                Assert(actionScroll is not null && actionCol.HasScrollableContent,
                    "#25 溢出待操作列布局后具备可滚动高度 (ScrollableHeight > 0)");
                controller.SyncActionPinOverlay();
                var wheelSnap = controller.CurrentPinnedWheelSnapshot;
                Assert(wheelSnap.Active && wheelSnap.HasScrollableContent,
                    "#25 常驻穿透开启后常驻滚轮原子快照同步激活");
                var insideX = (wheelSnap.Left + wheelSnap.Right) / 2;
                var insideY = (wheelSnap.Top + wheelSnap.Bottom) / 2;
                var offsetBefore = actionScroll!.VerticalOffset;
                Assert(controller.TryInterceptPinnedWheel(insideX, insideY, -120),
                    "#25 悬停在常驻穿透溢出列内向下滚动鼠标滚轮被拦截消费");
                actionCol.Root.UpdateLayout();
                Assert(actionScroll.VerticalOffset > offsetBefore + 1.0,
                    $"#25 常驻穿透向下滚轮真实推进列表 VerticalOffset ({offsetBefore:F1} -> {actionScroll.VerticalOffset:F1})");
                var offsetMid = actionScroll.VerticalOffset;
                Assert(controller.TryInterceptPinnedWheel(insideX, insideY, 120),
                    "#25 悬停在常驻穿透溢出列内向上滚动鼠标滚轮被拦截消费");
                actionCol.Root.UpdateLayout();
                Assert(actionScroll.VerticalOffset < offsetMid - 1.0,
                    $"#25 常驻穿透向上滚轮真实回滚列表 VerticalOffset ({offsetMid:F1} -> {actionScroll.VerticalOffset:F1})");
                Assert(!controller.TryInterceptPinnedWheel(wheelSnap.Left - 30, insideY, -120)
                    && !controller.TryInterceptPinnedWheel(wheelSnap.Right + 30, insideY, -120),
                    "#25 鼠标在常驻列矩形外滚动时放行不拦截");

                // Outside click / Alt+Tab collapse keeps pinned Action column + mini unpin overlay on desktop
                controller.SetExpanded(false);
                Assert(!controller.Expanded && controller.ActionPinned && controller.ShouldKeepActionPinnedVisible
                    && actionCol.IsPinnedOverlay && Native.IsClickThrough(actionHwnd) && overlay!.LogicalVisible,
                    "#25 外部点击/Alt+Tab 收起其他列表时，常驻待操作列与迷你解除浮窗严格保持常驻桌面不收起");
                Assert(controller.CurrentPinnedWheelSnapshot.Active && controller.TryInterceptPinnedWheel(insideX, insideY, -120),
                    "#25 其他列表收起后仅剩常驻穿透待操作列时仍支持鼠标滚轮上下滚动");

                // Global hotkey hide & wake: hides together on hotkey hide, strictly retains pinned+click-through+transparency on hotkey wake
                controller.SetExpanded(true);
                controller.ToggleFromHotkey();
                Assert(!controller.Expanded && controller.ActionPinned && controller.ActionPinnedHiddenByHotkey
                    && !overlay!.LogicalVisible && !controller.CurrentPinnedWheelSnapshot.Active
                    && !controller.TryInterceptPinnedWheel(insideX, insideY, -120),
                    "#25 按全局快捷键隐藏看板时，连同待操作列、迷你浮窗及滚轮拦截一并暂停并记住常驻态");
                controller.ToggleFromHotkey();
                Assert(controller.Expanded && controller.ActionPinned && !controller.ActionPinnedHiddenByHotkey
                    && actionCol.IsPinnedOverlay && actionCol.IsClickThrough && Native.IsClickThrough(actionHwnd)
                    && Math.Abs(actionCol.GlassTransparency - 70) < 0.01 && overlay!.LogicalVisible,
                    "#25 再次按全局快捷键唤起时，待操作列严格保持常驻 + 70%半透明 + 鼠标穿透 + 仅解除按钮可点");

                // Clicking mini overlay's unpin button cleanly exits pinned click-through mode
                overlay!.UnpinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(!controller.ActionPinned && !actionCol.IsPinnedOverlay && !actionCol.IsClickThrough
                    && !Native.IsClickThrough(actionHwnd) && !overlay.LogicalVisible
                    && !controller.CurrentPinnedWheelSnapshot.Active
                    && !controller.TryInterceptPinnedWheel(insideX, insideY, -120)
                    && Math.Abs(actionCol.GlassTransparency - SurfaceWindow.Transparency) < 0.01,
                    "#25 点击迷你浮窗图钉解除按钮一键恢复普通可点击状态、关闭滚轮拦截并恢复常规透明度");

                // #25 Loaded-window parity: normal MouseWheel vs pinned click-through wheel share identical QuickScroll motion, step, target accumulation & zero per-frame overlay sync storms
                {
                    var parityDir = Path.Combine(output, "pinned-wheel-parity");
                    var parityCtrl = new BoardController(parityDir, isolated: true, smoke: true) { OffscreenNoShow = true };
                    try
                    {
                        for (var k = 0; k < 14; k++)
                        {
                            var ps = parityCtrl.Engine.NewSession(Lane.Action);
                            ps.Working.Title = $"滚轮一致性验证任务-{k}";
                            ps.Working.Stages[0].Text = $"验证正常滚轮与常驻穿透滚轮步距与缓动动画完全一致-{k}\n第二行内容保证卡片高度";
                            parityCtrl.Engine.Submit(ps);
                        }
                        var pCol = parityCtrl.ActionColumn!;
                        pCol.Left = -20000;
                        pCol.Top = -20000;
                        pCol.Width = 240;
                        pCol.Height = 360;
                        pCol.SuppressActivation = true;
                        pCol.ShowActivated = false;
                        pCol.FloatingAnimations = false;
                        pCol.Show();
                        await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.Loaded);
                        pCol.Root.UpdateLayout();

                        var pScroll = pCol.ListScrollViewer!;
                        Assert(pScroll is not null && pScroll.IsLoaded && pScroll.IsVisible && QuickScroll.GetEnabled(pScroll) && pScroll.ScrollableHeight > 200,
                            $"#25 离屏加载态列 ScrollViewer 启用 QuickScroll 且具备充足溢出高度(scrollable={pScroll?.ScrollableHeight:F1})");

                        // 1. Normal wheel scroll (unpinned)
                        pScroll!.ScrollToVerticalOffset(0);
                        await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.Render);
                        var expectedStep = QuickScroll.ComputeWheelStep(pScroll.ViewportHeight, SystemParameters.WheelScrollLines);
                        var normalWheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                        {
                            RoutedEvent = UIElement.PreviewMouseWheelEvent,
                            Source = pScroll
                        };
                        pScroll.RaiseEvent(normalWheel);
                        var normalAnimating = QuickScroll.IsAnimating(pScroll);
                        var normalSingleTarget = QuickScroll.CurrentTarget(pScroll);
                        // Second rapid wheel notch during animation should accumulate smoothly from Target
                        var normalWheel2 = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 10, -120)
                        {
                            RoutedEvent = UIElement.PreviewMouseWheelEvent,
                            Source = pScroll
                        };
                        pScroll.RaiseEvent(normalWheel2);
                        var normalDoubleTarget = QuickScroll.CurrentTarget(pScroll);
                        await Task.Delay(140);
                        await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.Render);
                        var normalSettled = pScroll.VerticalOffset;

                        // 2. Pinned click-through wheel scroll
                        pScroll.ScrollToVerticalOffset(0);
                        await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.Render);
                        parityCtrl.SetExpanded(true);
                        parityCtrl.SetActionPinned(true);
                        parityCtrl.SyncActionPinOverlay();
                        var pSnap = parityCtrl.CurrentPinnedWheelSnapshot;
                        var px = (pSnap.Left + pSnap.Right) / 2;
                        var py = (pSnap.Top + pSnap.Bottom) / 2;
                        var syncCountBefore = parityCtrl.SyncActionPinOverlayCount;

                        var pinnedHit1 = parityCtrl.TryInterceptPinnedWheel(px, py, -120);
                        var pinnedAnimating = QuickScroll.IsAnimating(pScroll);
                        var pinnedSingleTarget = QuickScroll.CurrentTarget(pScroll);
                        var pinnedHit2 = parityCtrl.TryInterceptPinnedWheel(px, py, -120);
                        var pinnedDoubleTarget = QuickScroll.CurrentTarget(pScroll);
                        await Task.Delay(140);
                        await PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority.Render);
                        var pinnedSettled = pScroll.VerticalOffset;
                        var syncCountDelta = parityCtrl.SyncActionPinOverlayCount - syncCountBefore;

                        Assert(normalWheel.Handled && normalAnimating && pinnedHit1 && pinnedHit2 && pinnedAnimating,
                            "#25 正常滑动与常驻穿透滑动均触发同一 QuickScroll 90ms 缓出动画");
                        Assert(Math.Abs(normalSingleTarget - expectedStep) < 0.5
                            && Math.Abs(pinnedSingleTarget - normalSingleTarget) < 0.01
                            && Math.Abs(pinnedDoubleTarget - normalDoubleTarget) < 0.01
                            && Math.Abs(pinnedSettled - normalSettled) < 0.5,
                            $"#25 常驻穿透单格/连滚目标与最终落点与正常滑动完全一致(step={expectedStep:F1}, normal={normalSingleTarget:F1}/{normalDoubleTarget:F1}->{normalSettled:F1}, pinned={pinnedSingleTarget:F1}/{pinnedDoubleTarget:F1}->{pinnedSettled:F1})");
                        Assert(syncCountDelta == 0,
                            $"#25 常驻穿透滚动动画全程零冗余 SyncActionPinOverlay/RaiseBall 触发(delta={syncCountDelta})");

                        pCol.Hide();
                    }
                    finally { parityCtrl.Dispose(); }
                }

                // Verify non-overflow pinned Action column passes mouse wheel through to underlying app
                var nonOverflowDir = Path.Combine(output, "pinned-no-overflow");
                var shortController = new BoardController(nonOverflowDir, isolated: true, smoke: true) { OffscreenNoShow = true };
                try
                {
                    var shortAction = shortController.ActionColumn!;
                    shortAction.Left = -10000;
                    shortAction.Top = -10000;
                    shortAction.Width = 240;
                    shortAction.Height = 680;
                    Layout(shortAction.Root, 240, 680);
                    shortController.SetExpanded(true);
                    shortController.SetActionPinned(true);
                    shortController.SyncActionPinOverlay();
                    var shortSnap = shortController.CurrentPinnedWheelSnapshot;
                    Assert(shortSnap.Active && !shortSnap.HasScrollableContent
                        && !shortController.TryInterceptPinnedWheel((shortSnap.Left + shortSnap.Right) / 2, (shortSnap.Top + shortSnap.Bottom) / 2, -120),
                        "#25 无溢出内容的常驻穿透待操作列放行鼠标滚轮到底层软件不拦截");
                }
                finally { shortController.Dispose(); }

                controller.SetExpanded(false);
            }

            // #02: Auto-start diagnostics, startup retry resilience, GitHub Releases updater & settings UI
            {
                var dDisabled = StartupLinks.DiagnoseAutoStart(null, false, null, null);
                var dMissing = StartupLinks.DiagnoseAutoStart("\"C:\\missing\\MoyuDesk.exe\" --startup", false, null, "C:\\app\\MoyuDesk.exe");
                var dWinDisabled = StartupLinks.DiagnoseAutoStart("\"C:\\app\\MoyuDesk.exe\" --startup", true, [0x03, 0, 0, 0], "C:\\app\\MoyuDesk.exe");
                var dPathChanged = StartupLinks.DiagnoseAutoStart("\"C:\\old\\MoyuDesk.exe\" --startup", true, [0x02, 0, 0, 0], "C:\\new\\MoyuDesk.exe");
                var dHealthy = StartupLinks.DiagnoseAutoStart("\"C:\\app\\MoyuDesk.exe\" --startup", true, [0x02, 0, 0, 0], "C:\\app\\MoyuDesk.exe");
                Assert(dDisabled.Health == AutoStartHealth.Disabled
                    && dMissing.Health == AutoStartHealth.PathMissing
                    && dWinDisabled.Health == AutoStartHealth.WindowsDisabled
                    && dPathChanged.Health == AutoStartHealth.PathChanged
                    && dHealthy.Health == AutoStartHealth.Healthy,
                    "#02 StartupLinks.DiagnoseAutoStart 准确识别全部 5 种开机自启健康态（含 StartupApproved 禁用诊断）");

                var attempts = 0;
                var retried = Taskboard.Desktop.Program.RunWithStartupRetry(true, () =>
                {
                    if (++attempts < 3) throw new IOException("模拟开机早期挂载延迟");
                    return "ok";
                }, maxAttempts: 4, delayMs: 5);
                Assert(retried == "ok" && attempts == 3, "#02 --startup 早期瞬时 IO 异常可平滑重试成功");

                Assert(AppUpdater.IsNewerVersion("0.9.53", "v0.9.54")
                    && !AppUpdater.IsNewerVersion("0.9.53", "v0.9.53")
                    && !AppUpdater.IsNewerVersion("0.9.54", "0.9.53")
                    && AppUpdater.IsNewerVersion("0.9.53", "v1.0.0-beta.1"),
                    "#02 AppUpdater SemVer 版本比较准确");

                var sampleJson = """
                {
                  "tag_name": "v0.9.54",
                  "name": "MoyuDesk v0.9.54",
                  "body": "## 本次更新\n- 优化长文本打开速度\n- 新增待操作列常驻穿透",
                  "html_url": "https://github.com/Derp-zh/MoyuDesk/releases/tag/v0.9.54",
                  "assets": [
                    { "name": "MoyuDesk-0.9.54-portable-win-x64.zip", "browser_download_url": "https://github.com/Derp-zh/MoyuDesk/releases/download/v0.9.54/MoyuDesk-0.9.54-portable-win-x64.zip" },
                    { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/Derp-zh/MoyuDesk/releases/download/v0.9.54/SHA256SUMS.txt" }
                  ]
                }
                """;
                var rel = AppUpdater.ParseLatestReleaseJson(sampleJson);
                Assert(rel.Version == "0.9.54"
                    && rel.ZipAssetName == "MoyuDesk-0.9.54-portable-win-x64.zip"
                    && rel.ZipDownloadUrl!.EndsWith(".zip", StringComparison.Ordinal)
                    && rel.ChecksumsDownloadUrl!.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                    && rel.NotesSummary.Contains("优化长文本打开速度", StringComparison.Ordinal),
                    "#02 AppUpdater 准确解析 GitHub Releases JSON 资产链接与更新日志摘要");

                var testZipDir = Path.Combine(output, "updater-zip-unit");
                Directory.CreateDirectory(testZipDir);
                var payloadDir = Path.Combine(testZipDir, "payload");
                Directory.CreateDirectory(Path.Combine(payloadDir, "scripts"));
                File.WriteAllText(Path.Combine(payloadDir, "MoyuDesk.exe"), "stub-exe");
                File.WriteAllText(Path.Combine(payloadDir, "scripts", "install.ps1"), "# stub");
                var zipFile = Path.Combine(testZipDir, "MoyuDesk-0.9.54-portable-win-x64.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(payloadDir, zipFile);
                var realHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipFile)));
                var sumsText = $"{realHash.ToLowerInvariant()}  MoyuDesk-0.9.54-portable-win-x64.zip\n";
                var parsedHash = AppUpdater.ParseExpectedSha256(sumsText, "MoyuDesk-0.9.54-portable-win-x64.zip");
                Assert(parsedHash == realHash
                    && AppUpdater.VerifyFileSha256(zipFile, parsedHash!, out _)
                    && !AppUpdater.VerifyFileSha256(zipFile, new string('0', 64), out _),
                    "#02 AppUpdater SHA256SUMS.txt 解析、完整匹配校验及篡改拦截通过");

                var extractTo = Path.Combine(testZipDir, "extracted");
                AppUpdater.ExtractZipSafely(zipFile, extractTo);
                Assert(AppUpdater.FindInstallScript(extractTo)?.EndsWith("install.ps1", StringComparison.OrdinalIgnoreCase) == true,
                    "#02 AppUpdater 安全解压并定位内置 scripts/install.ps1");

                var settings02 = new SettingsSurface(controller) { SuppressActivation = true };
                try
                {
                    settings02.SelectSection("主设置");
                    Layout(settings02.Root, 760, 680);
                    Assert(Descendants<Button>(settings02.Root).Any(b => Equals(b.Tag, "CheckUpdateButton"))
                        && Descendants<Button>(settings02.Root).Any(b => Equals(b.Tag, "OpenReleasesButton"))
                        && Descendants<TextBlock>(settings02.Root).Any(t => Equals(t.Tag, "AutoStartDiagnosticStatus")),
                        "#02 设置“主设置”页包含开机自启诊断状态与 GitHub Releases 检查更新卡片");
                }
                finally { settings02.Destroy(false); }
            }

            var perfReport = new
            {
                passed = checks.Count,
                checks,
                metrics = new
                {
                    fastCloneMs = Math.Round(fastWatch.Elapsed.TotalMilliseconds, 3),
                    jsonCloneMs = Math.Round(jsonWatch.Elapsed.TotalMilliseconds, 3),
                    stageDetailsFirstPassMs = Math.Round(firstPassMs, 3),
                    stageDetailsCachedPassMs = Math.Round(cachedPassMs, 3),
                    editorOpenMs = Math.Round(openWatch.Elapsed.TotalMilliseconds, 3),
                    editorSaveMs = Math.Round(saveWatch.Elapsed.TotalMilliseconds, 3),
                    drag12MovesMs = Math.Round(dragWatch.Elapsed.TotalMilliseconds, 3)
                },
                offscreen = true,
                visibleWindows = 0,
                mouseMoved = false
            };
            File.WriteAllText(Path.Combine(output, "batch3-surface-report.json"),
                JsonSerializer.Serialize(perfReport, new JsonSerializerOptions { WriteIndented = true }));
            return JsonSerializer.Serialize(new { passed = checks.Count, perfReport.metrics, offscreen = true, visibleWindows = 0 });
        }
        finally
        {
            controller.Dispose();
        }
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static Task PumpDispatcherAsync(System.Windows.Threading.DispatcherPriority priority)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.BeginInvoke(priority, new Action(() => tcs.TrySetResult()));
        return tcs.Task;
    }

    private static IntPtr GetWpfEmptyDimIdentity()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var tscType = typeof(HwndSource).Assembly.GetType("System.Windows.Input.TextServicesContext");
        var tsc = tscType?.GetProperty("DispatcherCurrent", flags)?.GetValue(null);
        var field = tscType?.GetField("_dimEmpty", flags)?.GetValue(tsc);
        var raw = field?.GetType().GetProperty("Value", flags)?.GetValue(field) ?? field;
        if (raw is null || !System.Runtime.InteropServices.Marshal.IsComObject(raw)) return IntPtr.Zero;
        var ptr = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(raw);
        System.Runtime.InteropServices.Marshal.Release(ptr);
        return ptr;
    }

    private static bool IsKeyboardProviderActive(IntPtr hwnd)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var src = HwndSource.FromHwnd(hwnd);
        var kp = typeof(HwndSource).GetField("_keyboard", flags)?.GetValue(src);
        var val = kp?.GetType().GetProperty("Value", flags)?.GetValue(kp) ?? kp;
        return val is not null && (bool)(val.GetType().GetField("_active", flags)?.GetValue(val) ?? false);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    private static IntPtr SendMessageNative(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam) => SendMessage(hwnd, msg, wParam, lParam);

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
