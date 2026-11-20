using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Taskboard.Core;
using Taskboard.Desktop;

// Real observer -> dispatcher/controller -> WPF card -> AtomicStore, with only
// the external control/record inputs replaced. No user desktop is accessed.
internal static class CodexSidebarChecks
{
    private const string Id = "11111111-1111-4111-8111-111111111111";
    private sealed class Records : ICodexActivitySource
    {
        internal CodexActivity Value = new("sidebar-turn", "inProgress", 1, true, 0, 0);
        internal bool Missing;
        public IReadOnlyDictionary<string,CodexActivity> Read(IReadOnlyCollection<string> ids) => Missing
            ? throw new IOException("isolated outage") : ids.ToDictionary(id => id, _ => Value);
    }
    private sealed class Controls : ICodexSidebarSource
    {
        internal bool Foreground = true;
        internal string View = "activation-1", Identity = "form:1";
        internal CodexSidebarPhase Phase;
        internal int Reads;
        public Task<CodexSidebarSnapshot> Read(string[] ids, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Reads);
            return Task.FromResult(new CodexSidebarSnapshot(Foreground, View, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                [new(Id, Phase, Identity)], Foreground ? "" : "不在前台"));
        }
    }
    internal static async Task Run(string root, List<string> checks)
    {
        var start = checks.Count;
        void Check(bool value, string name) { if (!value) throw new Exception("前台补充：" + name); checks.Add("前台补充：" + name); }
        async Task Until(Func<bool> done, string name)
        { var until = DateTime.UtcNow.AddSeconds(7); while (!done()) { if (DateTime.UtcNow > until) throw new Exception("前台补充超时：" + name); await Task.Delay(20); } }
        var names = CodexSidebarSource.UniqueNames([new(Id,"重名",0),new(Guid.NewGuid().ToString(),"重名",0),new("unique","唯一",0)], [Id,"unique"]);
        Check(!names.ContainsKey("重名") && names["唯一"] == "unique", "包括未绑定的重名聊天，拒绝含糊名称且继续唯一会话");
        var renamed = CodexSidebarSource.UniqueNames([new(Id,"改名后",0)], [Id]);
        Check(!renamed.ContainsKey("改名前") && renamed["改名后"] == Id, "重新读取目录后只接受当前名称");
        Check(!CodexSidebarSource.IdentityStillMatches(Id, new Dictionary<string,string> { ["旧名"] = Id },
            new Dictionary<string,string> { ["旧名"] = "another-thread", ["新名"] = Id }), "采样期间改名且旧名被另一聊天复用，拒绝缓存名称串绑");
        Check(CodexSidebarSource.IdentityStillMatches(Id, new Dictionary<string,string> { ["当前名"] = Id },
            new Dictionary<string,string> { ["当前名"] = Id }), "采样前后名称和UUID一致才采用控件状态");
        using var controller = new BoardController(Path.Combine(root, "sidebar-board"), true, true);
        var records = new Records(); var controls = new Controls();
        var observer = new CodexObserver(activitySource: () => records, sidebarSource: controls);
        await controller.Codex.DisposeAsync();
        typeof(BoardController).GetProperty("Codex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, observer);
        typeof(BoardController).GetMethod("AttachCodex", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "前台缺日志的实际状态链";
        session.Working.Stages[0].Codex = new() { ThreadId = Id };
        var item = controller.Engine.Submit(session);
        CodexBinding Binding() => item.Stages[0].Codex!;
        TaskCard? Card()
        {
            var column = controller.Columns.Single(c => c.Definition.Contains(item));
            column.Root.Measure(new Size(320, 600)); column.Root.Arrange(new Rect(0, 0, 320, 600)); column.Root.UpdateLayout();
            return column.Locate(item.Id);
        }
        observer.Start();
        await Until(() => Binding().Phase == CodexPhase.Running, "本机轮次");
        controls.Phase = CodexSidebarPhase.WaitingForInput;
        await Until(() => Binding().Phase == CodexPhase.WaitingForInput, "无日志前台问题");
        Check(item.Lane == Lane.Action && Card() is not null && Binding().SidebarWaitingSinceUnixMs > 0, "没有后台等待日志时，原输入控件信号仍经Controller把实际卡片移入待操作");
        var edit = controller.Engine.Edit(item.Id); edit.Working.Title = "前台等待时的用户编辑";
        item.Reminder.Enabled = true; item.Reminder.NextLocal = DateTime.Now.AddSeconds(-1); controller.TickReminders(DateTime.Now);
        Check(item.TimerUnread && Visual<System.Windows.Shapes.Ellipse>(Card()!).Any(e => e.Width == 6), "等待中到期生成实际红点控件");
        await controller.Flush();
        var stored = controller.Store.Load().Tasks.Single(t => t.Id == item.Id);
        Check(stored.CurrentCodex!.SidebarWaitingSinceUnixMs > 0 && stored.CurrentCodex.AwaitingUserAction, "等待时间、准确轮次和等待保护真实落盘");
        controls.Foreground = false; var count = controls.Reads;
        await Until(() => controls.Reads >= count + 2, "后台采样");
        Check(item.Lane == Lane.Action && item.TimerUnread, "最小化或离开前台保留等待和红点");
        controls.View = "activation-2"; controls.Phase = CodexSidebarPhase.Running; controls.Foreground = true; count = controls.Reads;
        await Until(() => controls.Reads >= count + 4, "新前台快照");
        Check(item.Lane == Lane.Action && Binding().AwaitingUserAction && item.TimerUnread, "重新激活后多份旧运行快照也不能冒充实际恢复");
        controls.Phase = CodexSidebarPhase.WaitingForInput;
        await Until(() => Binding().Phase == CodexPhase.WaitingForInput, "本代确认原问题");
        count = controls.Reads; await Until(() => controls.Reads >= count + 2, "等待被本代采样");
        controls.Phase = CodexSidebarPhase.Running;
        count = controls.Reads; await Until(() => controls.Reads >= count + 3, "仅关闭原表单");
        Check(item.Lane == Lane.Action && item.TimerUnread, "关闭表单后未出现新执行，旧处理中标志不能冒充恢复");
        records.Value = records.Value with { ExecutionStartedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await Until(() => item.Lane == Lane.Running, "同前台代次等待到运行");
        Check(!item.TimerUnread && Binding().SidebarWaitingSinceUnixMs == 0 && !Visual<System.Windows.Shapes.Ellipse>(Card()!).Any(e => e.Width == 6), "连续确认同代等待到运行，实际卡片恢复并清除红点");
        controller.Engine.Submit(edit); item = controller.Engine.State.Tasks.Single(t => t.Id == item.Id);
        Check(item.Title == "前台等待时的用户编辑" && item.Lane == Lane.Running && !Binding().AwaitingUserAction, "旧编辑副本保存保留后台最新恢复");
        controls.Phase = CodexSidebarPhase.WaitingForApproval; controls.Identity = "form:approval-2";
        await Until(() => Binding().Phase == CodexPhase.WaitingForApproval, "批准控件");
        Check(item.Lane == Lane.Action, "批准控件单独映射等待批准");
        records.Missing = true;
        await Until(() => Binding().Phase == CodexPhase.Unknown, "本机记录中断");
        Check(item.Lane == Lane.Action && Binding().AwaitingUserAction, "原记录不可读时保留前台已确认等待");
        records.Missing = false; await Until(() => observer.TransportConnected, "记录重连");
        count = controls.Reads; await Until(() => controls.Reads >= count + 3, "重连控件重新核对");
        controls.Phase = CodexSidebarPhase.Running;
        await Task.Delay(100);
        records.Value = records.Value with { ExecutionStartedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        await Until(() => item.Lane == Lane.Running, "重连后原请求实际恢复");
        Check(!Binding().AwaitingUserAction, "重连后的同轮控件变化可以正常恢复");
        records.Value = records.Value with { Status = "completed" };
        await Until(() => Binding().Phase == CodexPhase.ReadyForReview, "准确结束");
        count = controls.Reads; await Until(() => controls.Reads >= count + 3, "结束后旧控件");
        Check(item.Lane == Lane.Action && Binding().Phase == CodexPhase.ReadyForReview, "轮次结束优先，旧处理中控件不能把结果移回进行中");
        var previous = Binding().BindingId;
        Check(controller.Engine.BindStageConversation(item.Id, item.Stages[0].Id, previous, Guid.NewGuid().ToString(), "新会话"), "改绑经过原任务引擎");
        Check(Binding().BindingId != previous && Binding().SidebarWaitingSinceUnixMs == 0 && !Binding().AwaitingUserAction, "改绑清除旧前台信任和等待时间");
        controls.Phase = CodexSidebarPhase.WaitingForInput; count = controls.Reads;
        await Until(() => controls.Reads >= count + 3, "旧会话迟到采样");
        Check(!Binding().AwaitingUserAction, "旧会话控件不能影响新绑定");
        await controller.Flush();
        Check(controller.Store.Load().Tasks.Single(t => t.Id == item.Id).CurrentCodex!.BindingId == Binding().BindingId, "实际保存回读新绑定一致");
        CoreProtection(Check);
        File.WriteAllText(Path.Combine(root,"sidebar-report.json"), JsonSerializer.Serialize(new { passed = true, fixture = true, originalDesktop = false,
            actualController = true, actualWpfCard = true, actualStore = true, checks = checks.Skip(start).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void CoreProtection(Action<bool,string> check)
    {
        var item = new WorkItem { Lane = Lane.Running, TimerUnread = true }; item.Stages[0].Codex = new() { ThreadId = Id };
        var b = item.CurrentCodex!; long sequence = 0;
        bool Apply(string method, object data, string epoch = "test") => CodexState.Apply(item, item.Stages[0].Id,
            JsonSerializer.SerializeToElement(new { method, @params = data }), ++sequence, b.Generation, epoch, true);
        Apply("turn/started", new { threadId = Id, turnId = "a" });
        Apply("probe/sidebarWaiting", new { threadId = Id, turnId = "a", atMs = 10L, approval = false });
        b.PendingRequests.Add("accurate-request"); b.PendingApprovals.Add("accurate-request");
        Apply("probe/sidebarRunning", new { threadId = Id, turnId = "a" });
        check(item.Lane == Lane.Action && b.PendingRequests.Contains("accurate-request") && b.Phase == CodexPhase.WaitingForApproval,
            "UIA恢复不批量清除其他来源准确请求");
        check(!Apply("probe/sidebarRunning", new { threadId = Id, turnId = "old" }) && !Apply("probe/sidebarRunning", new { threadId = Id, turnId = "a" }, "old-epoch"),
            "拒绝旧轮次及旧观察代次的前台恢复");
        Apply("serverRequest/resolved", new { threadId = Id, turnId = "a", requestId = "accurate-request" });
        check(item.Lane == Lane.Action, "原响应只关闭请求，不独立清等待");
        var another = Copies.Of(item.Stages[0]); another.Id = Guid.NewGuid(); another.Codex = new() { ThreadId = "other", AwaitingUserAction = true, Phase = CodexPhase.WaitingForInput };
        item.Stages.Add(another);
        Apply("probe/sidebarRunning", new { threadId = Id, turnId = "a" });
        check(item.Lane == Lane.Action && item.TimerUnread, "另一运行阶段仍等待时整卡不回进行中、不清红点");
        another.Codex.SourceEpoch = "other-epoch"; another.Codex.TurnId = "b";
        CodexState.Apply(item, another.Id, JsonSerializer.SerializeToElement(new { method = "probe/sidebarRunning",
            @params = new { threadId = "other", turnId = "b" } }), 1, another.Codex.Generation, "other-epoch", true);
        check(item.Lane == Lane.Running && !item.TimerUnread, "全部运行阶段恢复后整卡才清提醒");
        item.Lane = Lane.Paused;
        Apply("probe/sidebarWaiting", new { threadId = Id, turnId = "a", atMs = 20L, approval = false });
        check(item.Lane == Lane.Paused, "前台补充不扩大原自动移列的两列范围");
    }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T value) yield return value; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var c in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return c; }
}
