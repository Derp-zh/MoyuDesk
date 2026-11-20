using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using U = Interop.UIAutomationClient;

namespace Taskboard.Desktop;

internal enum CodexSidebarPhase { Unknown, Running, WaitingForInput, WaitingForApproval }
internal sealed record CodexSidebarRow(string ThreadId, CodexSidebarPhase Phase, string WaitIdentity = "");
internal sealed record CodexSidebarSnapshot(bool Foreground, string ViewEpoch, long AtMs,
    CodexSidebarRow[] Rows, string Reason = "")
{
    internal static CodexSidebarSnapshot Unavailable(string reason) => new(false, "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), [], reason);
}
internal interface ICodexSidebarSource
{
    Task<CodexSidebarSnapshot> Read(string[] threadIds, CancellationToken cancellation);
}

// Only the verified foreground desktop's accessibility controls. No invocation,
// scrolling, focus, clipboard, messages, injection, or Codex transport is used.
internal sealed class CodexSidebarSource : ICodexSidebarSource
{
    private CodexThread[] directory = [];
    private long directoryAt;
    private IntPtr previousWindow;
    private string viewEpoch = "";
    private Task<CodexSidebarSnapshot>? pending;

    internal static Dictionary<string, string> UniqueNames(IEnumerable<CodexThread> threads, IReadOnlyCollection<string> ids) => threads
        .Where(t => t.Platform == Taskboard.Core.AiPlatform.Codex && !string.IsNullOrWhiteSpace(t.Name))
        .GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == 1)
        .Where(g => ids.Contains(g.First().Id)).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

    internal static bool IdentityStillMatches(string threadId, IReadOnlyDictionary<string,string> sampled, IReadOnlyDictionary<string,string> current) =>
        sampled.Any(pair => pair.Value == threadId && current.TryGetValue(pair.Key, out var id) && id == threadId);

    public async Task<CodexSidebarSnapshot> Read(string[] threadIds, CancellationToken cancellation)
    {
        try
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var pid);
            if (window == IntPtr.Zero || IsIconic(window) || !CodexConversationDropTarget.VerifyDesktop(pid))
            { previousWindow = IntPtr.Zero; viewEpoch = ""; return CodexSidebarSnapshot.Unavailable("Codex不在前台"); }
            if (previousWindow != window) { previousWindow = window; viewEpoch = Guid.NewGuid().ToString("N"); }
            if (pending is { IsCompleted: false }) return CodexSidebarSnapshot.Unavailable("前台控件读取仍在等待，后台监听保持");
            var now = Environment.TickCount64;
            if (directory.Length == 0 || now - directoryAt >= 5000)
            { directory = await CodexLocalCatalog.Load().ConfigureAwait(false); directoryAt = now; }
            var names = UniqueNames(directory, threadIds);
            var epoch = viewEpoch;
            pending = SidebarWorker.Read(() => Query(window, pid, epoch, names));
            var snapshot = await pending.WaitAsync(TimeSpan.FromMilliseconds(1800), cancellation).ConfigureAwait(false);
            // A title can be renamed/reused while UIA is reading. Revalidate
            // against a new committed directory read before associating controls
            // with a UUID; the short directory cache alone is never identity proof.
            directory = await CodexLocalCatalog.Load().ConfigureAwait(false); directoryAt = Environment.TickCount64;
            var currentNames = UniqueNames(directory, threadIds);
            snapshot = snapshot with { Rows = snapshot.Rows.Where(row => IdentityStillMatches(row.ThreadId, names, currentNames)).ToArray() };
            if (GetForegroundWindow() != window || IsIconic(window))
            { previousWindow = IntPtr.Zero; viewEpoch = ""; return CodexSidebarSnapshot.Unavailable("前台已变化，丢弃本次采样"); }
            return snapshot;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { previousWindow = IntPtr.Zero; viewEpoch = ""; return CodexSidebarSnapshot.Unavailable("前台控件暂不可读，后台监听保持"); }
    }

    private static CodexSidebarSnapshot Query(IntPtr window, uint pid, string epoch, Dictionary<string, string> names)
    {
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (GetForegroundWindow() != window || IsIconic(window)) return CodexSidebarSnapshot.Unavailable("前台已变化");
        if (names.Count == 0) return new(true, epoch, at, [], "没有可唯一对应的会话名称");
        var api = SidebarWorker.Api;
        var root = api.ElementFromHandle(window);
        if ((uint)root.CurrentProcessId != pid) return CodexSidebarSnapshot.Unavailable("窗口来源已变化");
        var clock = Stopwatch.StartNew();
        var filter = names.Keys.Select(name => api.CreatePropertyCondition(30005, name)).ToArray();
        var matching = filter.Length == 1 ? filter[0] : api.CreateOrConditionFromArray(filter);
        var buttons = root.FindAll((U.TreeScope)4, api.CreateAndCondition(api.CreatePropertyCondition(30003, 50000), matching));
        var rows = new List<CodexSidebarRow>();
        for (var i = 0; i < buttons.Length; i++)
        {
            if (clock.ElapsedMilliseconds > 1200) throw new TimeoutException();
            var button = buttons.GetElement(i);
            var cls = button.CurrentClassName;
            if (!cls.Contains("sidebar-item", StringComparison.Ordinal) || cls.Contains("group/folder-row", StringComparison.Ordinal)) continue;
            var name = button.CurrentName;
            if (!names.TryGetValue(name, out var id)) continue;
            // IsOffscreen is deliberately not a filter: recent entries can keep
            // their accessible state while outside the scroll viewport.
            var statuses = button.FindAll((U.TreeScope)4, api.CreatePropertyCondition(30003, 50017));
            var phase = CodexSidebarPhase.Unknown;
            for (var n = 0; n < statuses.Length; n++)
                if (statuses.GetElement(n).CurrentName is "处理中" or "Working" or "Processing") phase = CodexSidebarPhase.Running;
            rows.Add(new(id, phase));
        }
        // Some desktop versions keep the generic sidebar spinner even while an
        // input card is open. Verify that current local document's live form by
        // control roles and fixed action labels; never read prompts or answers.
        var documents = root.FindAll((U.TreeScope)4, api.CreatePropertyCondition(30003, 50030));
        for (var i = 0; i < documents.Length; i++)
        {
            if (clock.ElapsedMilliseconds > 1200) throw new TimeoutException();
            var document = documents.GetElement(i);
            if (!names.TryGetValue(document.CurrentName, out var id)) continue;
            var waiting = PendingForm(api, document);
            if (waiting is not null) rows.Add(new(id, waiting.Value.Phase, waiting.Value.Identity));
            else
            {
                var stops = document.FindAll((U.TreeScope)4, api.CreateAndCondition(api.CreatePropertyCondition(30003, 50000),
                    api.CreateOrCondition(api.CreatePropertyCondition(30005, "停止"), api.CreatePropertyCondition(30005, "Stop"))));
                for (var n = 0; n < stops.Length; n++)
                {
                    var stop = stops.GetElement(n);
                    if (stop.CurrentIsEnabled != 0 && stop.CurrentClassName.Contains("size-token-button-composer", StringComparison.Ordinal))
                    { rows.Add(new(id, CodexSidebarPhase.Running)); break; }
                }
            }
        }
        var merged = rows.GroupBy(r => r.ThreadId, StringComparer.Ordinal).Select(g =>
        {
            var waiting = g.Where(r => r.Phase is CodexSidebarPhase.WaitingForInput or CodexSidebarPhase.WaitingForApproval).ToArray();
            if (waiting.Length > 0) return waiting.OrderByDescending(r => r.Phase == CodexSidebarPhase.WaitingForApproval).First();
            return new CodexSidebarRow(g.Key, g.Any(r => r.Phase == CodexSidebarPhase.Running) ? CodexSidebarPhase.Running : CodexSidebarPhase.Unknown);
        }).ToArray();
        if (GetForegroundWindow() != window || IsIconic(window)) return CodexSidebarSnapshot.Unavailable("前台已变化");
        return new(true, epoch, at, merged);
    }

    private static (CodexSidebarPhase Phase, string Identity)? PendingForm(U.IUIAutomation api, U.IUIAutomationElement document)
    {
        U.IUIAutomationCondition NamedButtons(params string[] names) => api.CreateAndCondition(api.CreatePropertyCondition(30003, 50000),
            api.CreateOrConditionFromArray(names.Select(name => api.CreatePropertyCondition(30005, name)).ToArray()));
        var approvals = document.FindAll((U.TreeScope)4, NamedButtons("允许一次", "Allow once"));
        for (var i = 0; i < approvals.Length; i++)
        {
            var button = approvals.GetElement(i);
            if (button.CurrentIsOffscreen == 0 && button.CurrentIsEnabled != 0)
                return (CodexSidebarPhase.WaitingForApproval, "form:" + string.Join(".", button.GetRuntimeId()));
        }
        var skips = document.FindAll((U.TreeScope)4, NamedButtons("跳过", "Skip"));
        for (var i = 0; i < skips.Length; i++)
        {
            var skip = skips.GetElement(i);
            if (skip.CurrentIsOffscreen != 0 || skip.CurrentIsEnabled == 0) continue;
            var parent = skip;
            for (var n = 0; n < 5; n++)
            {
                parent = api.ControlViewWalker.GetParentElement(parent);
                if (parent is null || parent.CurrentControlType == 50030) break;
                if (parent.CurrentControlType != 50026 || !parent.CurrentClassName.Contains("@container/request-card", StringComparison.Ordinal)) continue;
                var inputs = parent.FindAll((U.TreeScope)4, api.CreateOrConditionFromArray(new[] { 50004, 50013, 50002, 50003 }
                    .Select(type => api.CreatePropertyCondition(30003, type)).ToArray()));
                var send = parent.FindAll((U.TreeScope)4, NamedButtons("发送", "Send", "提交", "Submit", "下一步", "Next"));
                if (inputs.Length > 0 && send.Length == 1)
                    return (CodexSidebarPhase.WaitingForInput, "form:" + string.Join(".", parent.GetRuntimeId()));
            }
        }
        return null;
    }

    private static class SidebarWorker
    {
        private static readonly BlockingCollection<Action> queue = new(1);
        private static readonly Thread thread = Start();
        [ThreadStatic] private static U.IUIAutomation? api;
        internal static U.IUIAutomation Api
        {
            get
            {
                if (api is null)
                {
                    api = new U.CUIAutomation8();
                    ((U.IUIAutomation2)api).ConnectionTimeout = 750;
                    ((U.IUIAutomation2)api).TransactionTimeout = 1000;
                }
                return api;
            }
        }
        private static Thread Start()
        {
            var value = new Thread(() => { foreach (var action in queue.GetConsumingEnumerable()) action(); })
                { IsBackground = true, Name = "Codex foreground controls" };
            value.SetApartmentState(ApartmentState.MTA); value.Start(); return value;
        }
        internal static Task<CodexSidebarSnapshot> Read(Func<CodexSidebarSnapshot> read)
        {
            _ = thread;
            var result = new TaskCompletionSource<CodexSidebarSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!queue.TryAdd(() =>
            {
                try { result.TrySetResult(read()); }
                catch (Exception) { result.TrySetResult(CodexSidebarSnapshot.Unavailable("前台控件结构变化或暂不可读")); }
            })) result.TrySetResult(CodexSidebarSnapshot.Unavailable("前台控件读取正忙"));
            return result.Task;
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
}
