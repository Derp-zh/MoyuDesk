using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class CodexDailyNativeDesktop
{
    internal static int Run(string directory, string threadId, bool resume = false)
    {
        var root = Path.GetFullPath(directory);
        var assemblyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(BoardController).Assembly.Location)));
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            (resume ? !File.Exists(Path.Combine(root,"daily-scope.json")) : Directory.Exists(root))) throw new IOException("需要明确隔离目录及本轮标记");
        Directory.CreateDirectory(root); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var exit = 0;
        var scopePath = Path.Combine(root, "daily-scope.json");
        if (!resume) File.WriteAllText(scopePath, JsonSerializer.Serialize(new { originalThread = threadId, productionWrites = false }));
        else
        {
            using var scope = JsonDocument.Parse(File.ReadAllText(scopePath));
            if (scope.RootElement.GetProperty("originalThread").GetString() != threadId)
                throw new IOException("测试会话编号与上一轮记录不符");
        }
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        using var controller = new BoardController(Path.Combine(root, "board"), true, false);
        var checks = new List<string>(); var events = new List<object>(); var checkpoints = new List<string>();
        if(!resume)
        {
            var single = controller.Engine.NewSession(Lane.Running); single.Working.Title = typeof(BoardController).Assembly.GetName().Version + "日常自动移列验收";
            single.Working.Stages[0].Codex = new() { ThreadId = threadId, ThreadName = "Codex 测试会话" };
            controller.Engine.Submit(single);
        }
        if(controller.Engine.State.Tasks.Count!=1) throw new IOException("仅允许本轮单张测试卡片");
        var id = controller.Engine.State.Tasks.Single().Id;
        if(resume && File.Exists(Path.Combine(root,"checkpoints-list.json"))) checkpoints.AddRange(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(root,"checkpoints-list.json")))!);
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; long lastCommand = 0; var busy = false;
        if(resume && File.Exists(Path.Combine(root,"command.json"))) {using var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"command.json")));lastCommand=prior.RootElement.GetProperty("id").GetInt64();}
        EditorSurface? editor = null; var start = DateTime.UtcNow; var last = "";
        var frameClock=System.Diagnostics.Stopwatch.StartNew(); long previousTick=0,maxTickGapMs=0;
        void AttachEvents()
        {
            controller.Codex.Event += (_,message,_,_,_) =>
            {
                var row=new {at=DateTime.UtcNow,method=CodexState.Text(message,"method"),turn=CodexState.Text(CodexState.Object(message,"params"),"turnId")};
                lock(events){events.Add(row);File.AppendAllText(Path.Combine(root,"events.jsonl"),JsonSerializer.Serialize(row)+"\n");}
            };
        }
        WorkItem Item() => controller.Engine.State.Tasks.Single(t => t.Id == id);
        object Snapshot()
        {
            var value = Item(); var binding = value.Stages[0].Codex!;
            var column = controller.Columns.FirstOrDefault(c => c.Definition.Contains(value));
            var card = column?.Locate(id);
            return new { utc = DateTime.UtcNow, lane = value.Lane.ToString(), phase = binding.Phase.ToString(), binding.AwaitingUserAction,
                binding.TurnId, binding.SidebarWaitingSinceUnixMs, value.TimerUnread, cardInExpectedColumn = card is not null,
                cardRedDot = card is not null && Visual<System.Windows.Shapes.Ellipse>(card).Any(e => e.Width == 6),
                behavior = controller.Codex.BehaviorStatus, source = controller.Codex.SourceKind, controller.Codex.TransportConnected,
                controller.Codex.SourceIdentityVerified, editorOpen = editor is not null,
                assembly = typeof(BoardController).Assembly.Location, assemblySHA256 = assemblyHash, command = lastCommand,maxTickGapMs };
        }
        async Task Checkpoint(JsonElement command)
        {
            await controller.PendingAutomaticMoves;
            var value = Item(); var state = command.GetProperty("expect");
            if (state.TryGetProperty("lane", out var lane) && value.Lane.ToString() != lane.GetString()) throw new Exception("实际卡片列不匹配");
            if (state.TryGetProperty("phase", out var phase) && value.CurrentCodex!.Phase.ToString() != phase.GetString()) throw new Exception("实际阶段状态不匹配");
            if (state.TryGetProperty("dot", out var dot) && value.TimerUnread != dot.GetBoolean()) throw new Exception("实际提醒红点不匹配");
            var card = controller.Columns.Single(c => c.Definition.Contains(value)).Locate(id) ?? throw new Exception("实际列表卡片不存在");
            if (Visual<System.Windows.Shapes.Ellipse>(card).Any(e => e.Width == 6) != value.TimerUnread) throw new Exception("卡片红点控件不匹配");
            await controller.Flush(); var saved = controller.Store.Load().Tasks.Single(t => t.Id == id);
            if (saved.Lane != value.Lane || saved.TimerUnread != value.TimerUnread || saved.CurrentCodex!.AwaitingUserAction != value.CurrentCodex!.AwaitingUserAction) throw new Exception("实际落盘回读不匹配");
            var label = command.GetProperty("label").GetString()!; checks.Add(label + "：实际卡片／阶段／红点控件／保存回读一致"); checkpoints.Add(label);
            File.WriteAllText(Path.Combine(root,"checkpoints-list.json"),JsonSerializer.Serialize(checkpoints));
            File.WriteAllText(Path.Combine(root, label + ".json"), JsonSerializer.Serialize(Snapshot(), new JsonSerializerOptions { WriteIndented = true }));
        }
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await controller.Start(); controller.SuppressCollapse = true; controller.SetExpanded(true);
                var area = controller.WorkArea(controller.Ball);
                for (var i = 0; i < controller.Columns.Count; i++) { var c = controller.Columns[i]; c.Left = area.Left + 8 + i * 205; c.Top = area.Top + 10; c.Width = 195; c.Height = 440; }
                await controller.RestartCodex();
                AttachEvents();
                File.WriteAllText(Path.Combine(root, "ready.json"), "{\"ready\":true}"); tick.Start();
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(root, "error.txt"), error.ToString()); exit = 1; app.Shutdown(); }
        }));
        tick.Tick += async (_, _) =>
        {
            if (busy) return; busy = true;
            if(previousTick>0)maxTickGapMs=Math.Max(maxTickGapMs,frameClock.ElapsedMilliseconds-previousTick);previousTick=frameClock.ElapsedMilliseconds;
            try
            {
                var path = Path.Combine(root, "command.json");
                if (File.Exists(path))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path)); var command = document.RootElement;
                    var sequence = command.GetProperty("id").GetInt64();
                    if (sequence > lastCommand)
                    {
                        lastCommand = sequence; string? failure = null;
                        try
                        {
                            switch (command.GetProperty("action").GetString())
                            {
                                case "checkpoint": await Checkpoint(command); break;
                                case "reset": controller.Engine.Move(id,Lane.Running,null,null); break;
                                case "reconnect": await controller.RestartCodex(); AttachEvents(); break;
                                case "rebind":
                                    var unbind=controller.Engine.Edit(id);unbind.Working.Stages[0].Codex=null;controller.Engine.Submit(unbind);
                                    var bind=controller.Engine.Edit(id);bind.Working.Stages[0].Codex=new(){ThreadId=threadId,ThreadName="Codex 测试会话"};controller.Engine.Submit(bind);break;
                                case "pauseForRestart":
                                    await controller.Flush();tick.Stop();File.WriteAllText(Path.Combine(root,"command-done.json"),JsonSerializer.Serialize(new{id=sequence,success=true,error=(string?)null}));
                                    await controller.Exit(); app.Shutdown();return;
                                case "reminder": Item().Reminder.Enabled = true; Item().Reminder.NextLocal = DateTime.Now.AddSeconds(-2); controller.TickReminders(DateTime.Now); break;
                                case "editor": editor = controller.EditTask(id); Visual<TextBox>(editor!).First().Text = "原客户端恢复保留编辑内容"; break;
                                case "saveEditor": if (editor is null || !await editor.SaveAndClose()) throw new Exception("编辑未保存"); editor = null; break;
                                case "settings": controller.Settings(); var settings = app.Windows.OfType<SettingsSurface>().Single(); settings.SelectSection("AI平台接入情况"); settings.SelectAiPlatform(AiPlatform.Codex); break;
                                case "hideSettings": foreach (var hiddenSettings in app.Windows.OfType<SettingsSurface>()) hiddenSettings.Hide(); break;
                                case "finish":
                                    await controller.Flush(); tick.Stop();
                                    File.WriteAllText(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new { passed = checkpoints.Count>0, originalDesktop = true, fixture = false,
                                        version = typeof(BoardController).Assembly.GetName().Version!.ToString(), originalThread = threadId,
                                        actualCards = true, actualStore = true, checks, events, checkpoints, productionDataChanged = false,
                                        codexRequests = 0, cliStarted = false, source = "original client existing logs and turn records", systemNetworkMeasured = false,
                                        assembly = typeof(BoardController).Assembly.Location, assemblySHA256 = assemblyHash, elapsedSeconds = (DateTime.UtcNow - start).TotalSeconds,maxTickGapMs,
                                        scope="Only individually recorded checkpoints; daily matrix failures reported separately" }, new JsonSerializerOptions { WriteIndented = true }));
                                    File.WriteAllText(Path.Combine(root,"command-done.json"),JsonSerializer.Serialize(new{id=sequence,success=true,error=(string?)null}));
                                    await controller.Exit(); app.Shutdown(); return;
                                default: throw new Exception("未知隔离验收命令");
                            }
                        }
                        catch (Exception error) { failure = error.Message; }
                        File.WriteAllText(Path.Combine(root, "command-done.json"), JsonSerializer.Serialize(new { id = sequence, success = failure is null, error = failure }));
                    }
                }
                var json = JsonSerializer.Serialize(Snapshot());
                File.WriteAllText(Path.Combine(root, "state.json"), json);
                var key = Item().Lane + ":" + Item().CurrentCodex!.Phase + ":" + Item().TimerUnread;
                if (key != last) { File.AppendAllText(Path.Combine(root, "transitions.jsonl"), json + "\n"); last = key; }
                if (DateTime.UtcNow - start > TimeSpan.FromMinutes(45)) throw new Exception("本轮桌面验收安全时限已到");
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(root, "error.txt"), error.ToString()); exit = 1; tick.Stop(); await controller.Exit(); app.Shutdown(); }
            finally { busy = false; }
        };
        app.Run(); return exit;
    }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T value) yield return value; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
