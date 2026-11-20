using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;
using Taskboard.Desktop;

// Metadata fixture -> real file reader -> observer -> dispatcher/controller ->
// WPF card -> AtomicStore. No windows are shown and no desktop input is used.
internal static class CodexBehaviorControllerChecks
{
    private sealed class Records : ICodexActivitySource
    {
        internal bool Missing;
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> ids) => Missing
            ? throw new IOException("isolated projection outage")
            : ids.ToDictionary(id => id, _ => new CodexActivity("turn-a", "inProgress", 1, true, 1, 1, 0, 1));
    }
    internal static async Task Run(string root, List<string> checks)
    {
        var directory = Path.Combine(root, "codex-behavior"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "fixture.log"); await File.WriteAllTextAsync(file, "");
        using var controller = new BoardController(Path.Combine(directory, "board"), true, true);
        var source = new Records(); var reader = new CodexBehaviorSource(() => [file], 0);
        var observer = new CodexObserver(activitySource: () => source,  behaviorEnabled: true, behaviorSource: reader);
        await controller.Codex.DisposeAsync();
        typeof(BoardController).GetProperty("Codex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, observer);
        typeof(BoardController).GetMethod("AttachCodex", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "本地行为链隔离卡片";
        session.Working.Stages[0].Codex = new() { ThreadId = "fixture-a" };
        var item = controller.Engine.Submit(session);
        observer.Start();
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks.Add(text); }
        async Task Until(Func<bool> done) { var limit = DateTime.UtcNow.AddSeconds(12); while (!done()) { if (DateTime.UtcNow > limit) throw new Exception("Controller行为链超时"); await Task.Delay(20); } }
        async Task Write(string body)
        {
            var prefix = body.StartsWith("received") ? "[electron-message-handler] [desktop-notifications] " : "[host-app-server-manager] ";
            await File.AppendAllTextAsync(file, DateTimeOffset.UtcNow.ToString("O") + " info " + prefix + body + "\n");
        }
        TaskCard? Card()
        {
            var column = controller.Columns.Single(c => c.Definition.Contains(item));
            column.Root.Measure(new Size(320, 600)); column.Root.Arrange(new Rect(0, 0, 320, 600)); column.Root.UpdateLayout();
            return column.Locate(item.Id);
        }
        await Until(() => reader.Available && item.CurrentCodex!.Phase == CodexPhase.Running);
        await Write("received question conversationId=fixture-a requestId=1");
        await Until(() => item.CurrentCodex!.Phase == CodexPhase.WaitingForInput);
        Check(item.Lane == Lane.Action && Card() is not null, "Codex行为夹具经过实际Controller，真实WPF卡片进入待操作列表");
        var draft = controller.Engine.Edit(item.Id); draft.Working.Title = "等待时编辑的标题";
        item.Reminder.Enabled = true; item.Reminder.NextLocal = DateTime.Now.AddSeconds(-1); controller.TickReminders(DateTime.Now);
        Check(item.TimerUnread && Descendants<System.Windows.Shapes.Ellipse>(Card()!).Any(e => e.Width == 6), "实际到期提醒与卡片红点控件保留");
        await Write("Sending server response id=1 method=item/tool/requestUserInput response={}");
        await Until(() => observer.BehaviorStatus.Contains("等待同轮"));
        Check(item.Lane == Lane.Action && item.TimerUnread, "实际Controller收到响应后不提前移列或清红点");
        await Write("Reasoning summary item completed summaryPartCount=0 threadId=fixture-a turnId=turn-a");
        await Until(() => item.Lane == Lane.Running);
        Check(!item.TimerUnread && Card() is not null && !Descendants<System.Windows.Shapes.Ellipse>(Card()!).Any(e => e.Width == 6), "同轮执行通过实际Controller反向移列并移除真实卡片红点");
        controller.Engine.Submit(draft);
        item = controller.Engine.State.Tasks.Single(t => t.Id == item.Id);
        Check(item.Title == "等待时编辑的标题" && item.Lane == Lane.Running && !item.CurrentCodex!.AwaitingUserAction, "等待时的旧编辑副本保存不覆盖后台恢复状态");
        await Write("received approval conversationId=fixture-a kind=commandExecution requestId=2");
        await Until(() => item.CurrentCodex!.Phase == CodexPhase.WaitingForApproval);
        source.Missing = true; await Until(() => item.CurrentCodex!.Phase == CodexPhase.Unknown);
        Check(item.Lane == Lane.Action && item.CurrentCodex!.AwaitingUserAction, "本机记录中断经真实Controller保留已确认批准等待");
        source.Missing = false;
        await Until(() => observer.TransportConnected);
        await Write("Sending server response id=2 method=item/commandExecution/requestApproval response={}");
        await Until(() => observer.BehaviorStatus.Contains("等待同轮"));
        Check(item.Lane == Lane.Action, "重连后的准确批准响应仍需要执行证据");
        await Write("Reasoning summary item completed summaryPartCount=0 threadId=fixture-a turnId=turn-a");
        await Until(() => item.Lane == Lane.Running);
        await controller.Flush();
        var saved = controller.Store.Load().Tasks.Single(t => t.Id == item.Id);
        Check(saved.Lane == Lane.Running && !saved.TimerUnread && saved.Title == item.Title, "行为链实际Flush及AtomicStore回读列、红点和编辑内容一致");
        File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(new { passed = true, originalDesktop = false, fixture = true, actualController = true, actualWpfCard = true, actualStore = true, checks = checks.Where(c => c.Contains("Controller") || c.Contains("红点") || c.Contains("Flush")).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { if (root is T value) yield return value; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
