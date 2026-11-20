using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class BehaviorChecks
{
    private sealed class Source : ICodexActivitySource
    {
        internal CodexActivity Value = new("turn-a", "inProgress", 1, true, 1, 1, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> ids) => ids.ToDictionary(x => x, _ => Value);
    }
    internal static async Task Run(string output)
    {
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var file = Path.Combine(output, "client.log"); await File.WriteAllTextAsync(file, "old history\n");
        var source = new CodexBehaviorSource(() => [file]);
        var activity = new Source(); var records = activity.Read(["a"]);
        Check(source.Read(records).Count == 0, "first-open history is not replayed");
        string Line(string text, long delta = 0) => DateTimeOffset.UtcNow.AddMilliseconds(delta).ToString("O") +
            (text.StartsWith("[desktop-notifications]") ? " info [electron-message-handler] " : " info [host-app-server-manager] ") + text.Replace("Reasoning summary item completed ", "Reasoning summary item completed summaryPartCount=0 ");
        var parsed = new List<CodexBehaviorEvent>();
        source.Parse(Line("[desktop-notifications] received approval conversationId=a kind=commandExecution requestId=1"), "boot-a", records, parsed);
        Check(parsed.Count == 1 && parsed[0].Method == "item/commandExecution/requestApproval", "exact approval and namespaced ID");
        var request = parsed[0]; parsed.Clear();
        source.Parse(Line("Sending server response id=1 method=item/commandExecution/requestApproval response={\"decision\":\"accept\"}"), "other-boot", records, parsed);
        Check(parsed.Count == 0, "another boot cannot resolve numeric ID");
        source.Parse(Line("Sending server response id=1 method=item/tool/requestUserInput response={}"), "boot-a", records, parsed);
        Check(parsed.Count == 0, "wrong method cannot resolve request");
        source.Parse(Line("Sending server response id=1 method=item/commandExecution/requestApproval response={\"decision\":\"accept\"}"), "boot-a", records, parsed);
        Check(parsed.Count == 1 && parsed[0].RequestId == request.RequestId, "exact response resolves only original request");
        var resolved = parsed[0]; parsed.Clear();
        source.Parse(Line("Reasoning summary item completed threadId=a turnId=old" ,100), "boot-a", records, parsed);
        Check(parsed.Count == 0, "old turn cannot prove recovery");
        source.Parse(Line("Reasoning summary item completed threadId=a turnId=turn-a", 200), "boot-a", records, parsed);
        Check(parsed.Count == 1 && parsed[0].Method == "thread/status/changed", "same-turn execution after response proves recovery");
        var resumed = parsed[0];
        parsed.Clear();
        source.Parse(Line("Sending server response id=99 method=item/tool/requestUserInput response={\"answer\":\"received approval conversationId=a kind=commandExecution requestId=spoof\"}"), "boot-a", records, parsed);
        Check(parsed.Count == 0, "answer body cannot manufacture a waiting event");
        source.Parse(Line("Unrelated log contains received question conversationId=a requestId=spoof"), "boot-a", records, parsed);
        Check(parsed.Count == 0, "fixed log message prefix required");
        source.Parse(Line("[desktop-notifications] received question conversationId=a requestId=remote hostId=remote"), "boot-a", records, parsed);
        Check(parsed.Count == 0, "explicit remote host is rejected");
        var utf8 = System.Text.Encoding.UTF8.GetBytes(Line("[desktop-notifications] received question conversationId=a requestId=2 note=中") + "\n");
        await using (var append = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) await append.WriteAsync(utf8.AsMemory(0, utf8.Length - 3));
        Check(source.Read(records).Count == 0, "partial UTF8 line not published");
        await using (var append = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) await append.WriteAsync(utf8.AsMemory(utf8.Length - 3));
        Check(source.Read(records).Single().Method == "item/tool/requestUserInput", "completed partial line published once");
        Check(source.Read(records).Count == 0, "file cursor prevents replay");
        var task = new WorkItem { Title = "isolated behavior", Lane = Lane.Running };
        task.Stages[0].Codex = new() { ThreadId = "a" };
        await using var observer = new CodexObserver( activitySource: () => activity);
        var watch = new CodexWatch(task.Id, task.Stages[0].Id, task.CurrentCodex!.BindingId, "a", 1);
        observer.Event += (w, message, seq, epoch, live) => CodexState.Apply(task,w.StageId,message,seq,w.Generation,epoch,live);
        observer.Watch([watch]); observer.Start();
        async Task Until(Func<bool> done) { var end = DateTime.UtcNow.AddSeconds(7); while (!done()) { if (DateTime.UtcNow > end) throw new Exception("Observer timeout"); await Task.Delay(20); } }
        await Until(() => task.CurrentCodex!.Phase == CodexPhase.Running);
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1;
        Check(observer.ReceiveBehavior(request with { AtMs = at }), "observer accepts exact current request");
        Check(task.Lane == Lane.Action && task.CurrentCodex!.Phase == CodexPhase.WaitingForApproval, "actual observer moves task to Action");
        task.TimerUnread = true;
        Check(observer.ReceiveBehavior(resolved with { AtMs = at + 1 }), "observer accepts matching response");
        Check(task.Lane == Lane.Action && task.TimerUnread, "response alone does not restore Running or clear dot");
        Check(observer.ReceiveBehavior(resumed with { AtMs = at + 2 }), "observer accepts later execution");
        Check(task.Lane == Lane.Running && !task.TimerUnread, "verified recovery restores Running and clears dot");
        Check(!observer.ReceiveBehavior(request with { TurnId = "old", AtMs = at + 3 }), "old turn rejected");
        observer.Watch([]);
        Check(!observer.ReceiveBehavior(request with { AtMs = at + 4 }), "unbound watch rejects late request");
        var store = new AtomicStore(Path.Combine(output, "board")); var state = new BoardState(); state.Tasks.Add(task); await store.SaveAsync(state);
        Check(store.Load().Tasks.Single().Lane == Lane.Running, "actual isolated store readback");
        var protectedTask = new WorkItem { Title = "saved wait", Lane = Lane.Action, TimerUnread = true };
        protectedTask.Stages[0].Codex = new() { ThreadId = "a", AwaitingUserAction = true, Phase = CodexPhase.WaitingForApproval };
        var preserved = protectedTask.CurrentCodex!;
        CodexState.Apply(protectedTask, protectedTask.Stages[0].Id, JsonSerializer.SerializeToElement(new { method = "probe/preserveLocalTurn", @params = new { threadId = "a", turnId = "turn-a" } }),1,1,"startup",true);
        Check(protectedTask.Lane == Lane.Action && preserved.AwaitingUserAction && protectedTask.TimerUnread, "startup local identity does not clear saved waiting or dot");
        CodexState.Apply(protectedTask, protectedTask.Stages[0].Id, JsonSerializer.SerializeToElement(new { method = "turn/completed", @params = new { threadId = "a", turn = new { id = "turn-a", status = "completed" } } }),2,1,"startup",true);
        Check(preserved.Phase == CodexPhase.ReadyForReview && protectedTask.Lane == Lane.Action, "same observed local turn can finish after saved wait startup");
        checks.AddRange(await BehaviorStabilityChecks.Run(output));
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = checks.Count }));
    }
}
