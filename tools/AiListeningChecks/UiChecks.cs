using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class UiChecks
{
    internal static Task<List<string>> Run(string root)
    {
        var done = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
                using var controller = new BoardController(Path.Combine(root, "ui"), true, true);
                var stage = new Stage(); var changes = 0;
                using var picker = new StageConversationPicker(controller, stage, () => changes++, true);
                var platforms = picker.Pane.Children.OfType<Grid>().Single(x => Equals(x.Tag, "StageAiPlatformSidebar")).Children.OfType<Button>().ToArray();
                if (platforms.Length != 3) throw new Exception("平台侧栏选择项缺失");
                platforms[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.Select(new("dsh-example", "DSH例子", 0, AiPlatform.Dsh));
                if (stage.Codex is not { Platform: AiPlatform.Dsh, ThreadId: "dsh-example" }) throw new Exception("DSH选择未绑定");
                platforms[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.Select(new("claude-example", "Claude例子", 0, AiPlatform.ClaudeCode));
                if (stage.Codex is not { Platform: AiPlatform.ClaudeCode, ThreadId: "claude-example", AwaitingUserAction: false }) throw new Exception("Claude选择未重置绑定");
                var host = new Border { Width = 268, Height = 560, Background = Ui.Surface, Child = picker.Pane };
                host.Measure(new Size(268, 560)); host.Arrange(new Rect(0, 0, 268, 560)); host.UpdateLayout();
                if (platforms.Any(x => x.ActualWidth <= 0) || picker.Input.ActualWidth <= 0 || changes != 2) throw new Exception("绑定控件布局或事件异常");
                var bitmap = new RenderTargetBitmap(420, 840, 144, 144, PixelFormats.Pbgra32); bitmap.Render(host);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "binding-picker.png"))) encoder.Save(file);
                var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "跨平台拖动隔离";
                var sharedId = Guid.NewGuid().ToString("D"); session.Working.Stages[0].Codex = new() { Platform = AiPlatform.ClaudeCode, ThreadId = sharedId };
                var saved = controller.Engine.Submit(session); var editing = controller.Engine.Edit(saved.Id);
                var editor = new EditorSurface(controller, editing); var editStage = editing.Working.Stages[0];
                editor.ApplyConversationDrop(editStage, editStage.Codex!.BindingId, new(sharedId, "同编号Codex来源", "合成准确Codex链接"));
                if (editStage.Codex?.Platform != AiPlatform.Codex) throw new Exception("拖动同编号Codex链接仍保留Claude来源");
                editor.Destroy();
                void PumpUntil(Func<bool> condition)
                {
                    var frame = new DispatcherFrame(); var until = DateTime.UtcNow.AddSeconds(12);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= until) frame.Continue = false; };
                    timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                    if (!condition()) throw new Exception("实际Controller回调/保存超时");
                }
                var eventsRoot = Path.Combine(root, "controller-events"); var nativeId = Guid.NewGuid().ToString();
                var liveSession = controller.Engine.NewSession(Lane.Running); liveSession.Working.Title = "真实Controller链隔离卡片";
                liveSession.Working.Stages[0].Codex = new() { Platform = AiPlatform.ClaudeCode, ThreadId = nativeId };
                var live = controller.Engine.Submit(liveSession);
                controller.AttachOtherAi(new AiPlatformObserver(eventsRoot, Path.Combine(root, "unused-native-dsh")));
                void Emit(string kind, string id = "", string name = "") => ClaudeHook.Write(eventsRoot, new(nativeId, "", 0, DateTime.UtcNow, kind, "controller-turn", id, name));
                Emit("UserPromptSubmit"); Emit("PreToolUse", "live-question", "AskUserQuestion");
                PumpUntil(() => live.Lane == Lane.Action);
                var actionColumn = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Action);
                actionColumn.Root.Measure(new Size(370, 620)); actionColumn.Root.Arrange(new Rect(0, 0, 370, 620)); actionColumn.Root.UpdateLayout();
                if (actionColumn.RenderedCard(live.Id) is null) throw new Exception("真实Controller移列后未生成待操作卡片控件");
                Emit("PostToolUse", "live-question", "AskUserQuestion"); Emit("PostToolUse", "actual-read", "Read");
                PumpUntil(() => live.Lane == Lane.Running);
                var flush = controller.Flush(); PumpUntil(() => flush.IsCompleted);
                if (!flush.GetAwaiter().GetResult() || controller.Store.Load().Tasks.Single(x => x.Id == live.Id).Lane != Lane.Running) throw new Exception("真实Controller移列状态未落盘");
                done.TrySetResult(["实际WPF绑定窗口包含三平台侧栏选择", "DSH选择绑定准确来源与编号", "切换Claude重建绑定且不继承等待", "268×560离屏布局与控件回调可用", "编辑器拖动准确Codex链接覆盖同编号Claude绑定时来源也更新", "实际Controller经Dispatcher移列并生成待操作TaskCard控件", "实际Controller恢复执行后沿原Flush链保存并回读"]);
                app.Shutdown();
            }
            catch (Exception e) { done.TrySetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return done.Task;
    }
}
