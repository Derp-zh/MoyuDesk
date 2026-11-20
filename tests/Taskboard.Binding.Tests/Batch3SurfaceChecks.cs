using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        // #29: IME candidate / status window immunity vs normal external windows
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Windows.UI.Core.CoreWindow", "TextInputHost", 0x08000000), "#29 微软拼音 TextInputHost 候选框识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("Microsoft.IME.UIManager.CandidateWindow.Host", "ChsIME"), "#29 Microsoft.IME 候选窗口识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SogouPY", "SogouCloud"), "#29 搜狗拼音候选框识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("SoPY_Status", "SGTool"), "#29 搜狗拼音状态栏识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("QQPY_Candidate", "QQPinyin"), "#29 QQ拼音候选框识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("WeaselCandidate", "WeaselServer"), "#29 Rime/小狼毫候选框识别为输入法免疫窗口");
        Assert(ImeWindowFilter.IsImeCandidateOrStatus("MSCTFIME UI", "notepad"), "#29 IMM/TSF MSCTFIME UI 窗口识别为输入法免疫窗口");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Windows.UI.Core.CoreWindow", "CalculatorApp"), "#29 普通UWP应用(CalculatorApp CoreWindow)不被误判为IME");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Chrome_WidgetWin_1", "SogouExplorer"), "#29 搜狗浏览器不被误判为搜狗输入法");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("Notepad", "notepad"), "#29 普通记事本窗口正常触发外部点击收起");
        Assert(!ImeWindowFilter.IsImeCandidateOrStatus("UnrealWindow", "GameClient"), "#29 全屏游戏窗口正常触发外部点击收起");

        // #30: MousePressGate & wraparound / deduplication
        var gate = new MousePressGate();
        Assert(gate.Accept(0, 1000, 1, 1), "#30 首次钩子按下被接受");
        Assert(!gate.Accept(0, 1003, 2, 1), "#30 同物理按下的原始输入(RawInput)正确去重");
        Assert(gate.Accept(0, 1005, 1, 1), "#30 同源快速连续点击不误吞");

        // #14 + #15 + #20 offscreen controller checks
        var boardDir = Path.Combine(output, "board");
        var controller = new BoardController(boardDir, isolated: true, smoke: true) { OffscreenNoShow = true };
        try
        {
            // #14: BallSurface expand prepares expanded search grid at Opacity=1 immediately without 0->1 flash
            controller.Ball.PrepareExpandedVisuals(true);
            var expandedGrid = Descendants<Grid>(controller.Ball.Root).FirstOrDefault(x => x.Children.OfType<TextBox>().Any());
            Assert(expandedGrid is not null && expandedGrid.Visibility == Visibility.Visible && expandedGrid.Opacity == 1.0,
                "#14 悬浮球展开准备阶段搜索框直接以完整可见态随原生裁切展开，消除Width>64瞬跳闪烁");
            Layout(controller.Ball.Root, 370, 58);
            Assert(expandedGrid!.Opacity == 1.0, "#14 悬浮球布局变化过程搜索框Opacity保持稳定不闪烁");
            var regionsLeft = controller.Ball.GlassRegions().ToArray();
            Assert(regionsLeft.Length == 1 && regionsLeft[0].Bounds.Width == 370, "#14 悬浮球玻璃区域保持固定370画布宽度");

            // #15: Unified Alt+Tab / foreground switching semantics
            Assert(controller.Columns.All(c => !c.ShowInTaskbar), "#15 所有独立列表窗口 ShowInTaskbar=false");
            Assert(!controller.Ball.ShowInTaskbar, "#15 悬浮球 ShowInTaskbar=false");
            controller.SetExpanded(true);
            controller.MarkAppForeground();
            Assert(controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => false, _ => false),
                "#15 Alt+Tab切走(外部前台窗口且非IME/切换器)允许收起到悬浮球");
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => true, _ => false),
                "#15/#29 外部前台为IME窗口时不触发收起");
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64 + 1000, _ => false, _ => true),
                "#15 Alt+Tab按住浏览任务切换器浮层期间不提前收起");

            controller.SuppressForegroundCollapse(500);
            Assert(!controller.ShouldCollapseOnForegroundLoss(new IntPtr(0x1234), Environment.TickCount64, _ => false, _ => false),
                "#15 内部弹窗关闭/切窗保护期内不误收起");

            controller.SetExpanded(false);
            Assert(!controller.Expanded, "#15 已收起到悬浮球");
            controller.ActivateFromTaskSwitcher();
            Assert(controller.Expanded, "#15 Alt+Tab切回主代理窗口自动展开多列表独立窗口");
            controller.SetExpanded(false);

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

            // Verify DisplayTitle equivalence without array allocation
            var noTitle = new WorkItem
            {
                Title = "",
                Stages = [new Stage { IsRunning = true, Text = "\n  \n  首行真实标题内容\n" + longLine }]
            };
            Assert(noTitle.DisplayTitle == "首行真实标题内容", "#20 DisplayTitle 零Split扫描准确提取首行标题");

            // Verify Copies.Of fast clone vs JSON clone equivalence and benchmark speedup
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

            // StageDetailsText ellipsized pre-truncation & layout cache benchmark
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

            // Opening heavy task editor timing
            var openWatch = Stopwatch.StartNew();
            var editSession = controller.Engine.Edit(heavyTask.Id);
            var editor = new EditorSurface(controller, editSession);
            stageHost.Children.Add(editor);
            Layout(editor, 430, EditorSurface.DefaultHeight);
            openWatch.Stop();
            Assert(openWatch.Elapsed.TotalMilliseconds < 800,
                $"#20 点击打开多阶段长文本编辑窗体耗时正常({openWatch.Elapsed.TotalMilliseconds:F2}ms)");

            // Saving heavy task timing
            editor.Session.Working.Title = "长文本压测任务-已保存";
            var saveWatch = Stopwatch.StartNew();
            var savedOk = await controller.SubmitSession(editor.Session);
            saveWatch.Stop();
            Assert(savedOk && controller.Engine.State.Tasks.Any(x => x.Id == heavyTask.Id && x.Title == "长文本压测任务-已保存")
                && saveWatch.Elapsed.TotalMilliseconds < 1000,
                $"#20 点击保存多阶段长文本任务耗时正常({saveWatch.Elapsed.TotalMilliseconds:F2}ms)");
            stageHost.Children.Remove(editor);
            editor.Destroy();

            // FocusMode AdaptiveCardPanel incremental drag-preview benchmark (no full TaskCard destruction)
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
