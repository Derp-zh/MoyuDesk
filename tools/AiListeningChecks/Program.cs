using System.Text.Json;
using System.IO;
using Taskboard.Core;
using Taskboard.Desktop;

if (args.Length > 0 && args[0] == "--live-dsh") { await DshLiveChecks.Run(args); return; }
if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("必须明确指定新的隔离 --data-dir");
var root = Path.GetFullPath(args[1]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("必须使用新空目录");
Directory.CreateDirectory(root);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
var desktopCatalogChecks = new List<object>();
var desktopCatalogFailures = new List<string>();
ClaudeDesktopChecks.Run(root, (ok, name) => {
    desktopCatalogChecks.Add(new { passed = ok, name });
    if (ok) Check(true, name); else desktopCatalogFailures.Add(name);
});
File.WriteAllText(Path.Combine(root, "desktop-catalog-results.json"), JsonSerializer.Serialize(new { passed = desktopCatalogFailures.Count == 0, checks = desktopCatalogChecks }));
if (desktopCatalogFailures.Count > 0) throw new Exception(string.Join("; ", desktopCatalogFailures));
await ClaudeDesktopChecks.Observe(root, Check);
var clock = DateTime.UtcNow; var id = Guid.NewGuid().ToString();
AiRawEvent Event(string kind, long seq, string request = "", string name = "", string outcome = "") => new(id, "source-one", seq, clock.AddMilliseconds(seq), kind, "turn-1", request, name, "fp", outcome);
AiObservation Read(AiEventState s, long publication) => s.Snapshot with { Sequence = publication, AtUtc = DateTime.UtcNow.AddSeconds(1) };
WorkItem Item(AiPlatform platform)
{
    var value = new WorkItem { Title = "隔离监听", Lane = Lane.Running, Stages = [new Stage { IsRunning = true, Codex = new() { Platform = platform, ThreadId = id, BoundAtUtc = clock.AddSeconds(-1) } }] };
    value.CurrentStageId = value.Stages[0].Id; return value;
}
bool Apply(WorkItem item, AiObservation value, int? generation = null, Guid? bindingId = null)
{
    var stage = item.Stages[0]; var b = stage.Codex!;
    return AiState.Apply(item, stage.Id, bindingId ?? b.BindingId, generation ?? b.Generation, value);
}
var state = new AiEventState(AiPlatform.ClaudeCode, id); var item = Item(AiPlatform.ClaudeCode);
state.Apply(Event("UserPromptSubmit", 0)); Apply(item, Read(state, 1));
state.Apply(Event("PreToolUse", 1, "q1", "AskUserQuestion")); Apply(item, Read(state, 2));
Check(item.Lane == Lane.Action && item.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput, "Claude提问准确进入待操作");
state.Apply(Event("PreToolUse", 2, "q2", "AskUserQuestion")); Apply(item, Read(state, 3));
state.Apply(Event("PostToolUse", 3, "q1", "AskUserQuestion")); Apply(item, Read(state, 4));
Check(item.Lane == Lane.Action && item.Stages[0].Codex!.PendingRequests.SequenceEqual(new[] { "tool:q2" }), "同轮两个问题只答一个仍保持待操作");
state.Apply(Event("PreToolUse", 4, "cmd", "Bash")); state.Apply(Event("PermissionRequest", 5, "", "Bash")); Apply(item, Read(state, 5));
Check(item.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval, "Claude权限请求关联准确工具ID并优先显示等待批准");
state.Apply(Event("PostToolUse", 6, "cmd", "Bash")); Apply(item, Read(state, 6));
Check(item.Lane == Lane.Action && item.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput, "批准后仍有问题时不回进行中");
state.Apply(Event("PostToolUse", 7, "q2", "AskUserQuestion")); Apply(item, Read(state, 7));
Check(item.Lane == Lane.Action, "问题回答完毕本身不伪造实际恢复");
item.TimerUnread = true;
state.Apply(Event("PreToolUse", 8, "next", "Read")); Apply(item, Read(state, 8));
Check(item.Lane == Lane.Action, "调用前不冒充权限后的实际执行");
state.Apply(Event("PostToolUse", 9, "next", "Read")); Apply(item, Read(state, 9));
Check(item.Lane == Lane.Running && !item.TimerUnread, "实际后续执行整卡恢复并清红点");
state.Apply(Event("PreToolUse", 10, "plan", "ExitPlanMode")); Apply(item, Read(state, 10));
Check(item.Lane == Lane.Action && item.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval, "Claude计划确认进入等待批准");
var unknown = Read(state, 11) with { Complete = false, Phase = CodexPhase.Unknown, Pending = [], Epoch = "reconnect" };
Apply(item, unknown);
Check(item.Lane == Lane.Action && item.Stages[0].Codex!.AwaitingUserAction, "跨连接未知不清已确认等待");
Check(!Apply(item, Read(state, 12), generation: 99), "拒绝旧绑定代次");
Check(!Apply(item, Read(state, 12), bindingId: Guid.NewGuid()), "拒绝旧绑定身份");
Check(!Apply(item, Read(state, 12) with { Platform = AiPlatform.Dsh }), "拒绝不同平台相同会话编号");
Check(!Apply(item, Read(state, 12) with { SessionId = "another" }), "拒绝其他会话事件");
var ordered = Item(AiPlatform.Dsh);
Apply(ordered, new(AiPlatform.Dsh, id, "ordered", 1, "new-turn", clock.AddSeconds(1), CodexPhase.Running, [], true, true, DateTime.UtcNow.AddSeconds(2)));
Check(!Apply(ordered, new(AiPlatform.Dsh, id, "ordered", 2, "old-turn", clock, CodexPhase.ReadyForReview, [], true, false, DateTime.UtcNow.AddSeconds(2))) && ordered.Lane == Lane.Running, "晚到旧轮终态不覆盖更新轮次");
var rejectedBefore = JsonSerializer.Serialize(ordered);
Check(!Apply(ordered, new(AiPlatform.Dsh, id, "ordered", 2, "", clock, CodexPhase.Running, [], true, true, DateTime.UtcNow.AddSeconds(2))) && JsonSerializer.Serialize(ordered) == rejectedBefore, "拒绝无轮次完整状态且不暗改来源水位");
Apply(ordered, new(AiPlatform.Dsh, id, "ordered", 3, "", default, CodexPhase.Unknown, [], false, false, DateTime.UtcNow.AddSeconds(2)));
Check(ordered.Stages[0].Codex!.Phase == CodexPhase.Unknown, "读取中断确实转待确认而非继续冒充运行");

var dsh = new AiEventState(AiPlatform.Dsh, id); var dshItem = Item(AiPlatform.Dsh);
dsh.Apply(Event("turn/start", 0)); dsh.Apply(Event("approval/asked", 4, "a1")); Apply(dshItem, Read(dsh, 1));
Check(dshItem.Lane == Lane.Action && dshItem.Stages[0].Codex!.PendingApprovals.Contains("approval:a1"), "DSH原生权限批准读取且非连续筛选序号不误报丢帧");
dsh.Apply(Event("question/asked", 5, "q1")); dsh.Apply(Event("approval/decided", 6, "a1")); Apply(dshItem, Read(dsh, 2));
Check(dshItem.Lane == Lane.Action && dshItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput, "DSH批准结清仍有提问保持待操作");
dsh.Apply(Event("question/answered", 7, "q1")); Apply(dshItem, Read(dsh, 3));
Check(dshItem.Lane == Lane.Action, "DSH回答结清不假报恢复");
dsh.Apply(Event("execution", 8)); Apply(dshItem, Read(dsh, 4));
Check(dshItem.Lane == Lane.Running, "DSH同轮实际输出恢复进行中");
dsh.Apply(Event("turn/failed", 9)); Apply(dshItem, Read(dsh, 5));
Check(dshItem.Lane == Lane.Action && dshItem.Stages[0].Codex!.Phase == CodexPhase.Failed, "DSH整轮失败进入待操作");
var pendingJson = JsonSerializer.SerializeToElement(new { type = "tool/result", seq = 8, time = new DateTimeOffset(clock).ToUnixTimeMilliseconds(), turn = 1, sessionId = id, data = new { callId = "q", pending = true } });
Check(AiPlatformObserver.DshEvent(pendingJson, "dsh") is null, "DSH超时pending结果不当作用户答复");
var unknownEnd = JsonSerializer.SerializeToElement(new { type = "turn/end", seq = 8, time = new DateTimeOffset(clock).ToUnixTimeMilliseconds(), turn = 1, sessionId = id, data = new { reason = "future-new-reason" } });
Check(AiPlatformObserver.DshEvent(unknownEnd, "dsh")!.Kind == "unavailable", "未知DSH终态不默认完成");
var forkEnd = JsonSerializer.SerializeToElement(new { type = "turn/end", seq = 9, time = new DateTimeOffset(clock).ToUnixTimeMilliseconds(), turn = 1, sessionId = id, data = new { reason = "forked" } });
Check(AiPlatformObserver.DshEvent(forkEnd, "dsh") is null, "DSH分支种子终态不令新轮次永久未知");
var ptcEnd = JsonSerializer.SerializeToElement(new { type = "tool/ptc-dispatch", seq = 10, time = new DateTimeOffset(clock).ToUnixTimeMilliseconds(), turn = 1, sessionId = id, data = new { callId = "q", name = "ask_user_question", pending = false } });
Check(AiPlatformObserver.DshEvent(ptcEnd, "dsh")!.Kind == "question/answered", "DSH子调用PTC结果准确结清问题");
var ptcPending = JsonSerializer.SerializeToElement(new { type = "tool/ptc-dispatch", seq = 11, time = new DateTimeOffset(clock).ToUnixTimeMilliseconds(), turn = 1, sessionId = id, data = new { callId = "q", name = "ask_user_question", pending = true } });
Check(AiPlatformObserver.DshEvent(ptcPending, "dsh")!.Kind == "question/asked", "DSH子调用超时仍保留可回答问题");
var nativePlan = new AiEventState(AiPlatform.Dsh, id);
nativePlan.Apply(Event("turn/start", 0)); nativePlan.Apply(Event("question/asked", 1, "bad-plan", "exit_plan_mode", "approval"));
Check(nativePlan.Snapshot.Pending.Length == 0, "DSH非计划模式的失败exit_plan_mode调用不误报批准");
nativePlan.Apply(Event("plan/mode", 2, outcome: "active")); nativePlan.Apply(Event("question/asked", 3, "real-plan", "exit_plan_mode", "approval"));
Check(nativePlan.Snapshot.Pending is [{ Approval: true }], "DSH原生计划模式内的计划确认准确进入批准等待");
var questionHost = new AiEventState(AiPlatform.ClaudeCode, id);
questionHost.Apply(Event("UserPromptSubmit", 0)); questionHost.Apply(Event("PreToolUse", 1, "question", "AskUserQuestion"));
questionHost.Apply(Event("PermissionRequest", 2, "", "AskUserQuestion")); questionHost.Apply(Event("Notification", 3, "", "", "permission_prompt"));
Check(questionHost.Snapshot.Pending is [{ Approval: false }], "Claude问题经权限宿主提示仍归等待回答且不叠加匿名批准");
var form = new AiEventState(AiPlatform.ClaudeCode, id);
form.Apply(Event("UserPromptSubmit", 0)); form.Apply(Event("Elicitation", 1, "", "test-server"));
form.Apply(Event("ElicitationResult", 2, "", "test-server"));
Check(form.Snapshot.Pending.Length == 0 && !form.Snapshot.Executing, "Claude无显式ID的唯一表单可结清但不伪报恢复");
var previousTurn = new AiEventState(AiPlatform.ClaudeCode, id);
previousTurn.Apply(Event("UserPromptSubmit", 0)); previousTurn.Apply(Event("UserPromptSubmit", 1) with { Turn = "turn-2" });
previousTurn.Apply(Event("Stop", 2));
Check(previousTurn.Snapshot.TurnId == "turn-2" && previousTurn.Snapshot.Phase == CodexPhase.Running, "Claude旧prompt终态不覆盖新prompt");
var gap = new AiEventState(AiPlatform.ClaudeCode, id);
gap.Apply(Event("UserPromptSubmit", 0)); gap.Apply(Event("PreToolUse", 2, "gap", "AskUserQuestion"));
Check(!gap.Snapshot.Complete, "Claude同轮事件丢序号不能报完整状态");
gap.Apply(Event("UserPromptSubmit", 3) with { Turn = "clean-new-turn" });
Check(gap.Snapshot.Complete && gap.Snapshot.Pending.Length == 0, "明确新轮开始可结束旧轮缺帧保护并重新完整观察");

var mixed = Item(AiPlatform.Dsh);
mixed.Stages.Add(new Stage { IsRunning = true, Codex = new() { Platform = AiPlatform.ClaudeCode, ThreadId = "second", Phase = CodexPhase.WaitingForInput, AwaitingUserAction = true } });
Apply(mixed, new(AiPlatform.Dsh, id, "mix", 1, "current", clock, CodexPhase.Running, [], true, true, DateTime.UtcNow.AddSeconds(1)));
Check(mixed.Lane == Lane.Action, "跨DSH和Claude多阶段等待优先");
mixed.Lane = Lane.Done; Apply(mixed, Read(dsh, 6)); Check(mixed.Lane == Lane.Done, "已完成列表不自动移列");
var custom = Item(AiPlatform.Dsh); custom.ColumnId = Guid.NewGuid(); Apply(custom, Read(dsh, 7)); Check(custom.Lane == Lane.Running, "自定义列表不自动移列");
var notRunning = Item(AiPlatform.Dsh); notRunning.Stages[0].IsRunning = false; Apply(notRunning, Read(dsh, 8)); Check(notRunning.Lane == Lane.Running, "未运行阶段不自动移列");

var engine = new BoardEngine(new BoardState());
var edit = engine.NewSession(Lane.Running); edit.Working.Title = "编辑合并与落盘"; edit.Working.Stages[0].Codex = new() { Platform = AiPlatform.Dsh, ThreadId = id };
var saved = engine.Submit(edit); var editing = engine.Edit(saved.Id); editing.Working.Title = "修改名称";
var live = engine.State.Tasks.Single(); var liveB = live.Stages[0].Codex!; liveB.Phase = CodexPhase.WaitingForApproval; liveB.AwaitingUserAction = true; liveB.PendingRequests = ["approval:live"]; liveB.PendingApprovals = ["approval:live"];
engine.Submit(editing);
Check(engine.State.Tasks.Single().Stages[0].Codex!.PendingApprovals.Contains("approval:live"), "编辑副本合并保留后台等待");
var store = new AtomicStore(Path.Combine(root, "board")); await store.SaveAsync(engine.State);
var loaded = store.Load().Tasks.Single().Stages[0].Codex!;
Check(loaded.Platform == AiPlatform.Dsh && loaded.PendingApprovals.SequenceEqual(new[] { "approval:live" }), "真实落盘重读平台和等待集合");
var sameId = engine.Edit(saved.Id); sameId.Working.Stages[0].Codex = new() { Platform = AiPlatform.ClaudeCode, ThreadId = id }; engine.Submit(sameId);
Check(engine.State.Tasks.Single().Stages[0].Codex is { Platform: AiPlatform.ClaudeCode, AwaitingUserAction: false }, "同编号换平台重置旧等待保护");

var home = Path.Combine(root, "claude-home"); Directory.CreateDirectory(home);
File.WriteAllText(Path.Combine(home, "settings.json"), "{\"custom\":17,\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-owned-hook\"}]}]}}");
ClaudeHook.Install(home, Path.Combine(root, "events"), "D:/isolated/Taskboard.exe"); ClaudeHook.Install(home, Path.Combine(root, "events"), "D:/isolated/Taskboard.exe");
using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "settings.json"))))
{
    Check(doc.RootElement.GetProperty("custom").GetInt32() == 17, "Claude事件安装保留其他用户设置");
    Check(doc.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength() == 2, "重复安装不重复注册且保留原Hook");
    Check(doc.RootElement.GetProperty("hooks").TryGetProperty("PermissionRequest", out _) && doc.RootElement.GetProperty("hooks").TryGetProperty("PreToolUse", out _), "安装实际包含批准与问题工具Hook");
    var installed = doc.RootElement.GetProperty("hooks").GetProperty("PreToolUse")[0].GetProperty("hooks")[0];
    Check(installed.GetProperty("command").GetString() == "D:/isolated/Taskboard.exe" && installed.GetProperty("args")[0].GetString() == "--ai-hook" && installed.GetProperty("onFailure").GetString() == "continue", "Claude使用无需shell的可执行入口且监听失败不阻止工具");
}
var journal = Path.Combine(root, "journal");
ClaudeHook.Write(journal, Event("UserPromptSubmit", 500)); ClaudeHook.Write(journal, Event("Stop", 1));
var rows = File.ReadAllLines(Directory.GetFiles(Path.Combine(journal, "claude")).Single()).Select(x => JsonSerializer.Deserialize<AiRawEvent>(x)!).ToArray();
Check(rows[0].Seq == 0 && rows[1].Seq == 1 && rows[0].Epoch == rows[1].Epoch, "Hook队列实际分配连续序号和同来源代次");

// Execute the real asynchronous reader -> reducer -> persistence chain on isolated files.
var observeItem = Item(AiPlatform.Dsh); var observeRoot = Path.Combine(root, "dsh");
var sessionDir = Path.Combine(observeRoot, "sessions", "test-project", id); Directory.CreateDirectory(sessionDir);
var log = Path.Combine(sessionDir, "session.v4.jsonl"); var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
File.WriteAllText(log, JsonSerializer.Serialize(new { type = "session", version = 4, id, createdAt = at, isSeeded = false }) + "\n");
void Append(int seq, string type, object data) => File.AppendAllText(log, JsonSerializer.Serialize(new { seq, type, time = Math.Max(at + seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), data }) + "\n");
Append(0, "turn/start", new { turn = 1 }); Append(1, "approval/asked", new { id = "approve-real-reader", callId = "shell" });
Append(2, "tool/call", new { turn = 1, step = 1, name = "ask_user_question", callId = "question-real-reader" });
using (var observer = new AiPlatformObserver(Path.Combine(root, "observe-events"), observeRoot))
{
    observer.Watch([observeItem]);
    observer.Changed += (_, stage, binding, generation, snapshot) => { lock (observeItem) AiState.Apply(observeItem, stage, binding, generation, snapshot); };
    observer.Start();
    async Task WaitFor(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until) { lock (observeItem) if (predicate()) return; await Task.Delay(50); }
        throw new Exception("实际观察链超时：" + observer.Status);
    }
    await WaitFor(() => observeItem.Stages[0].Codex!.PendingRequests.Count == 2);
    Check(observeItem.Lane == Lane.Action && observeItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval, "实际Node组件到观察器再到Core的批准/提问移列");
    Append(3, "approval/decided", new { id = "approve-real-reader", outcome = "allowed-once" });
    await WaitFor(() => observeItem.Stages[0].Codex!.PendingApprovals.Count == 0);
    Check(observeItem.Lane == Lane.Action && observeItem.Stages[0].Codex!.PendingRequests.Count == 1, "实际增量读取只结清对应批准");
    Append(4, "tool/result", new { turn = 1, step = 1, message = new { toolCallId = "question-real-reader", isError = false, content = new[] { new { type = "text", text = "{\"pending\":true}" } } } });
    await Task.Delay(1200);
    Check(observeItem.Stages[0].Codex!.PendingRequests.Count == 1, "真实规范pending结果经过Node仍保留等待");
    Append(5, "user/message", new { source = new { kind = "user-question-reply", callId = "question-real-reader" } });
    Append(6, "assistant/message", new { turn = 1, step = 2 });
    await WaitFor(() => observeItem.Lane == Lane.Running);
    Check(observeItem.Stages[0].Codex!.PendingRequests.Count == 0, "真实本机日志增量结清并在后续输出恢复进行中");
    Append(7, "turn/end", new { turn = 1, reason = new { kind = "completed" } });
    await WaitFor(() => observeItem.Stages[0].Codex!.Phase == CodexPhase.ReadyForReview);
    Check(observeItem.Lane == Lane.Action, "实际观察链整轮完成进入结果待检查");
    var flowState = new BoardState(); flowState.Tasks.Add(observeItem); var flowStore = new AtomicStore(Path.Combine(root, "observed-board"));
    await flowStore.SaveAsync(flowState);
    Check(flowStore.Load().Tasks.Single().Lane == Lane.Action, "实际观察链的最终移列状态落盘回读");
    observer.Watch([]); await Task.Delay(1000); observer.Watch([observeItem]);
    Append(8, "turn/start", new { turn = 2 }); Append(9, "approval/asked", new { id = "after-rebind", callId = "new-shell" });
    await WaitFor(() => observeItem.Stages[0].Codex!.PendingApprovals.Contains("approval:after-rebind"));
    Check(observeItem.Lane == Lane.Action, "全部停止观察再观察同会话游标与发布序号可恢复");
    var metadata = JsonSerializer.Serialize(new { seq = 10, type = "session/title", time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data = new { title = "结构尾帧测试" } });
    File.AppendAllText(log, metadata[..20]);
    await WaitFor(() => observeItem.Stages[0].Codex!.Phase == CodexPhase.Unknown);
    Check(observeItem.Lane == Lane.Action && observeItem.Stages[0].Codex!.AwaitingUserAction, "实际半帧导致不完整但不丢等待保护");
    File.AppendAllText(log, metadata[20..] + "\n");
    await WaitFor(() => observeItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval);
    Check(observeItem.Stages[0].Codex!.PendingApprovals.Contains("approval:after-rebind"), "只有非状态元数据的尾帧补齐也恢复完整等待投影");
}
var hookExecutable = Environment.GetEnvironmentVariable("TASKBOARD_AI_HOOK_EXECUTABLE");
if (!string.IsNullOrEmpty(hookExecutable))
{
    var claudeRoot = Path.Combine(root, "packaged-hook-events"); var claudeItem = Item(AiPlatform.ClaudeCode);
    using var observer = new AiPlatformObserver(claudeRoot, Path.Combine(root, "unused-dsh"));
    observer.Changed += (_, stage, binding, generation, snapshot) => { lock (claudeItem) AiState.Apply(claudeItem, stage, binding, generation, snapshot); };
    observer.Watch([claudeItem]); observer.Start();
    async Task Emit(string kind, string toolId = "", string toolName = "")
    {
        var launch = new System.Diagnostics.ProcessStartInfo(hookExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "--ai-hook", "claude", "--ai-events-root", claudeRoot }) launch.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(launch) ?? throw new Exception("包内Hook未启动");
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { session_id = id, prompt_id = "packaged-prompt", hook_event_name = kind, tool_use_id = toolId, tool_name = toolName, prompt = "DO_NOT_PERSIST_TEST_PROMPT", tool_input = new { secret = "DO_NOT_PERSIST_TEST_INPUT" } }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0 || (await stdout).Length != 0 || (await stderr).Length != 0) throw new Exception("包内Hook输出或退出码异常");
    }
    async Task WaitClaude(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until) { lock (claudeItem) if (predicate()) return; await Task.Delay(40); }
        throw new Exception("实际包Claude事件观察链超时：" + observer.Status);
    }
    await Emit("UserPromptSubmit"); await Emit("PreToolUse", "question", "AskUserQuestion");
    await WaitClaude(() => claudeItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput);
    Check(claudeItem.Lane == Lane.Action, "实际包exe读取stdin提问事件并经观察器移到待操作");
    await Emit("PreToolUse", "command", "Bash"); await Emit("PermissionRequest", "", "Bash");
    await WaitClaude(() => claudeItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForApproval);
    Check(claudeItem.Stages[0].Codex!.PendingRequests.Count == 2, "实际包权限事件关联工具并与未答问题同时保留");
    await Emit("PostToolUse", "command", "Bash");
    await WaitClaude(() => claudeItem.Stages[0].Codex!.Phase == CodexPhase.WaitingForInput);
    Check(claudeItem.Lane == Lane.Action, "实际包批准完成仍有问题不提前移列");
    await Emit("PostToolUse", "question", "AskUserQuestion"); await Task.Delay(900);
    Check(claudeItem.Lane == Lane.Action, "实际包答案事件本身不假报恢复");
    await Emit("PreToolUse", "followup", "Read"); await Emit("PostToolUse", "followup", "Read");
    await WaitClaude(() => claudeItem.Lane == Lane.Running);
    Check(claudeItem.Stages[0].Codex!.PendingRequests.Count == 0, "实际包后续工具完成才恢复进行中");
    await Emit("Stop"); await WaitClaude(() => claudeItem.Stages[0].Codex!.Phase == CodexPhase.ReadyForReview);
    Check(claudeItem.Lane == Lane.Action, "实际包Stop事件回待操作检查结果");
    var journalText = File.ReadAllText(Directory.GetFiles(Path.Combine(claudeRoot, "claude")).Single());
    Check(!journalText.Contains("DO_NOT_PERSIST"), "实际exe事件记录不保存输入参数或聊天正文");
    var packageState = new BoardState(); packageState.Tasks.Add(claudeItem); var packageStore = new AtomicStore(Path.Combine(root, "packaged-hook-board"));
    await packageStore.SaveAsync(packageState);
    Check(packageStore.Load().Tasks.Single().Stages[0].Codex is { Platform: AiPlatform.ClaudeCode, Phase: CodexPhase.ReadyForReview }, "实际包Claude观察链结束与平台落盘回读");
}
checks.AddRange(await UiChecks.Run(root));
File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { version = typeof(AiPlatformObserver).Assembly.GetName().Version?.ToString(), passed = true, scope = "Isolated source/core/event/installer/storage tests; no desktop or original AI sessions", checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, count = checks.Count, result = Path.Combine(root, "results.json") }));
