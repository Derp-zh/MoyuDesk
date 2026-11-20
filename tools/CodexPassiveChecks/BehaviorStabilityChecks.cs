using System.Diagnostics;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class BehaviorStabilityChecks
{
    private sealed class Activity : ICodexActivitySource
    {
        internal CodexActivity Value = new("turn-live", "inProgress", 1, true, 1, 1, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> ids) => ids.ToDictionary(x => x, _ => Value);
    }
    internal static async Task<IReadOnlyList<string>> Run(string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); checks.Add(text); }
        var now = DateTimeOffset.UtcNow;
        string Line(string text, int delta = 0) => now.AddMilliseconds(delta).ToString("O") +
            (text.StartsWith("received") ? " info [electron-message-handler] [desktop-notifications] " : " info [host-app-server-manager] ") +
            text.Replace("Reasoning summary item completed ", "Reasoning summary item completed summaryPartCount=0 ") + "\n";
        var activity = new Activity();
        var records = new Dictionary<string, CodexActivity> { ["a"] = activity.Value, ["b"] = activity.Value };
        var fileA = Path.Combine(output, "codex-desktop-11111111-1111-4111-8111-111111111111-123-t0-i1-120000-0.log");
        var fileB = Path.Combine(output, "codex-desktop-11111111-1111-4111-8111-111111111111-123-t0-i1-000000-0.log");
        await File.WriteAllTextAsync(fileA, "history\n");
        string[] files = [fileA];
        var reader = new CodexBehaviorSource(() => files, 0);
        reader.Read(records);
        records["b"] = records["b"] with { ProjectionCaughtUp = false };
        await File.AppendAllTextAsync(fileA, Line("received question conversationId=b requestId=10") + Line("received approval conversationId=a kind=commandExecution requestId=11", 1));
        Check(reader.Read(records, ["a", "b"]).Single().ThreadId == "a", "one lagging chat does not block another chat");
        records["b"] = records["b"] with { ProjectionCaughtUp = true };
        Check(reader.Read(records).Single().ThreadId == "b", "metadata deferred until exact local projection catches up");
        await File.WriteAllTextAsync(fileB, Line("Sending server response id=11 method=item/commandExecution/requestApproval response={}", 2) + Line("Reasoning summary item completed threadId=a turnId=turn-live", 3));
        files = [fileB, fileA];
        var rotated = reader.Read(records);
        Check(rotated.Count == 2 && rotated.Last().Method == "thread/status/changed", "new rotated file starts at beginning and response matches same boot");
        Check(reader.Flags("b").Length == 1 && reader.Flags("a").Length == 0, "parallel chat wait remains independent after recovery");
        await File.AppendAllTextAsync(fileA, Line("received question conversationId=a requestId=12", 4) + Line("received approval conversationId=a kind=fileChange requestId=13", 5));
        Check(reader.Read(records).Count == 2, "parallel exact requests both registered");
        await File.AppendAllTextAsync(fileB, Line("Sending server response id=12 method=item/tool/requestUserInput response={}", 6) + Line("Reasoning summary item completed threadId=a turnId=turn-live", 7));
        Check(reader.Read(records).Count == 1 && reader.Flags("a").Single() == "waitingOnApproval", "one resolved request cannot hide another approval");
        await File.AppendAllTextAsync(fileB, Line("Sending server response id=13 method=item/fileChange/requestApproval response={}", 8));
        Check(reader.Read(records).Single().Method == "serverRequest/resolved" && reader.Flags("a").Length == 1, "last response alone still preserves waiting");
        await File.AppendAllTextAsync(fileB, Line("Reasoning summary item completed threadId=a turnId=turn-live", 9));
        Check(reader.Read(records).Single().Method == "thread/status/changed", "all exact responses plus later same-turn execution restores running");
        await File.AppendAllTextAsync(fileA, Line("received question conversationId=a requestId=14", 10)); reader.Read(records);
        await File.WriteAllTextAsync(fileA, "truncated\n");
        var truncated = false;
        try { reader.Read(records); } catch (IOException) { reader.Unavailable(); truncated = true; }
        Check(truncated && reader.Flags("a").Length == 1, "truncation becomes unavailable without clearing wait");
        await File.AppendAllTextAsync(fileB, Line("Sending server response id=14 method=item/tool/requestUserInput response={}", 11) + Line("Reasoning summary item completed threadId=a turnId=turn-live", 12));
        Check(reader.Read(records).Count == 0 && reader.Flags("a").Length == 1, "numeric ID after truncation cannot clear old wait");
        records["a"] = records["a"] with { TurnId = "turn-new", StartedAt = now.AddSeconds(1).ToUnixTimeSeconds() };
        reader.Read(records);
        Check(reader.Flags("a").Length == 0, "new turn drops previous source uncertainty without inheriting wait");
        records["b"] = records["b"] with { Status = "completed" };
        reader.Read(records);
        Check(reader.Flags("b").Length == 0, "committed terminal clears incremental diagnostic waiting flags");
        await File.AppendAllTextAsync(fileB, Line("received question conversationId=b requestId=15", 13));
        Check(reader.Read(records).Count == 0, "wait arriving before new turn projection is deferred");
        records["b"] = records["b"] with { TurnId = "turn-next", Status = "inProgress", StartedAt = now.ToUnixTimeSeconds() };
        Check(reader.Read(records).Single().ThreadId == "b", "new committed turn consumes earlier deferred waiting metadata");
        var rollout = Path.Combine(output, "execution.jsonl"); await File.WriteAllTextAsync(rollout, "");
        var executionLog = Path.Combine(output, "execution.log"); await File.WriteAllTextAsync(executionLog, "");
        var executionReader = new CodexBehaviorSource(() => [executionLog], 0);
        var executionRecords = new Dictionary<string,CodexActivity> { ["a"] = activity.Value with { RolloutPath = rollout, RolloutBytes = 0 } };
        executionReader.Read(executionRecords);
        await File.AppendAllTextAsync(executionLog, Line("received question conversationId=a requestId=20", 20)); executionReader.Read(executionRecords);
        await File.AppendAllTextAsync(executionLog, Line("Sending server response id=20 method=item/tool/requestUserInput response={}", 21)); executionReader.Read(executionRecords);
        string Rollout(string kind, string name, int delta) => System.Text.Json.JsonSerializer.Serialize(new { timestamp = now.AddMilliseconds(delta), type = "response_item", payload = new { type = kind, name } }) + "\n";
        await File.AppendAllTextAsync(rollout, Rollout("function_call", "functions.request_user_input", 22) + Rollout("function_call_output", "functions.exec_command", 23));
        executionRecords["a"] = executionRecords["a"] with { RolloutBytes = new FileInfo(rollout).Length };
        Check(executionReader.Read(executionRecords).Count == 0 && executionReader.Flags("a").Length == 1, "input tool call and tool output are not post-answer execution evidence");
        await File.AppendAllTextAsync(rollout, Rollout("function_call", "functions.exec", 24));
        Check(executionReader.Read(executionRecords).Count == 0, "rollout bytes ahead of committed projection are not consumed");
        executionRecords["a"] = executionRecords["a"] with { RolloutBytes = new FileInfo(rollout).Length };
        Check(executionReader.Read(executionRecords).Single().Method == "thread/status/changed", "new committed same-turn model tool call proves recovery");

        // A bounded soak exercises cursor, request maps and repeated same-turn
        // waits. It is explicitly a fixture, not hours of original-client use.
        var soakFile = Path.Combine(output, "soak.log"); await File.WriteAllTextAsync(soakFile, "");
        var soak = new CodexBehaviorSource(() => [soakFile], 0);
        var soakRecords = new Dictionary<string, CodexActivity> { ["a"] = activity.Value };
        soak.Read(soakRecords); var clock = Stopwatch.StartNew();
        var transitions = 0;
        for (var i = 0; i < 1000; i++)
        {
            var delta = i * 3;
            await File.AppendAllTextAsync(soakFile, Line($"received question conversationId=a requestId={i}", delta));
            transitions += soak.Read(soakRecords).Count;
            await File.AppendAllTextAsync(soakFile, Line($"Sending server response id={i} method=item/tool/requestUserInput response={{}}", delta + 1) + Line("Reasoning summary item completed threadId=a turnId=turn-live", delta + 2));
            transitions += soak.Read(soakRecords).Count;
        }
        Check(transitions == 3000 && soak.Flags("a").Length == 0 && soak.Read(soakRecords).Count == 0, "1000 fixture lifecycles have no duplicate replay or stuck pending request");
        await File.WriteAllTextAsync(Path.Combine(output, "soak.json"), System.Text.Json.JsonSerializer.Serialize(new { fixture = true, lifecycles = 1000, transitions, elapsedMs = clock.ElapsedMilliseconds, originalClientSoak = false }));

        // Full reader -> observer -> Core path, without manual ReceiveBehavior.
        var liveFile = Path.Combine(output, "observer-live.log"); await File.WriteAllTextAsync(liveFile, "");
        var liveReader = new CodexBehaviorSource(() => [liveFile], 0);
        var task = new WorkItem { Title = "reader-to-observer fixture", Lane = Lane.Running };
        task.Stages[0].Codex = new() { ThreadId = "a" };
        await using var observer = new CodexObserver(activitySource: () => activity,  behaviorEnabled: true, behaviorSource: liveReader);
        var watch = new CodexWatch(task.Id, task.Stages[0].Id, task.CurrentCodex!.BindingId, "a", 1);
        observer.Event += (w, message, sequence, epoch, live) => CodexState.Apply(task, w.StageId, message, sequence, w.Generation, epoch, live);
        observer.Watch([watch]); observer.Start();
        async Task Until(Func<bool> done) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!done()) { if (DateTime.UtcNow > deadline) throw new Exception("Full reader path timed out"); await Task.Delay(20); } }
        await Until(() => liveReader.Available);
        now = DateTimeOffset.UtcNow.AddMilliseconds(1);
        await File.AppendAllTextAsync(liveFile, Line("received question conversationId=a requestId=1"));
        await Until(() => task.CurrentCodex!.Phase == CodexPhase.WaitingForInput);
        Check(task.Lane == Lane.Action, "file append through production reader and observer moves actual Core task to Action");
        task.TimerUnread = true;
        await File.AppendAllTextAsync(liveFile, Line("Sending server response id=1 method=item/tool/requestUserInput response={}", 1));
        await Until(() => observer.BehaviorStatus.Contains("等待同轮"));
        Check(task.Lane == Lane.Action && task.TimerUnread, "full reader path preserves lane and dot after response");
        activity.Value = activity.Value with { ProjectionCaughtUp = false };
        await Until(() => task.CurrentCodex!.Phase == CodexPhase.Unknown);
        activity.Value = activity.Value with { ProjectionCaughtUp = true };
        await Task.Delay(2200);
        Check(task.Lane == Lane.Action && task.TimerUnread && task.CurrentCodex!.AwaitingUserAction, "projection catching up after a response cannot manufacture execution or clear dot");
        await File.AppendAllTextAsync(liveFile, Line("Reasoning summary item completed threadId=a turnId=turn-live", 2));
        await Until(() => task.Lane == Lane.Running);
        Check(!task.TimerUnread, "full reader path restores running and clears dot only on execution");
        return checks;
    }
}
