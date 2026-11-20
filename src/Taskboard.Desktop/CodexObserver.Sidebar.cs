using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal sealed partial class CodexObserver
{
    private readonly ICodexSidebarSource? sidebar;
    private Task? sidebarWorker;
    private readonly Dictionary<CodexWatch, SidebarState> sidebarStates = new();
    private IReadOnlySet<Guid> sidebarWaitingBindings = new HashSet<Guid>();
    private IReadOnlyDictionary<Guid,long> sidebarSavedWaits = new Dictionary<Guid,long>();
    private string sidebarStatus = "前台控件等待检测";
    internal string SidebarStatus => Volatile.Read(ref sidebarStatus);
    private sealed class SidebarState(string turn)
    {
        internal readonly string Turn = turn;
        internal string View = "", WaitIdentity = "";
        internal long At, WaitAt, LastWaitingSampleAt;
        internal int RunningSamples;
        internal bool SawWaiting;
        internal CodexSidebarPhase Phase;
    }
    private async Task RunSidebar()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var current = Volatile.Read(ref watches);
                if (current.Length > 0)
                {
                    var snapshot = await sidebar!.Read(current.Select(w => w.ThreadId).Distinct().ToArray(), lifetime.Token).ConfigureAwait(false);
                    lock (localApplyGate) ApplySidebar(snapshot, current);
                    Volatile.Write(ref sidebarStatus, snapshot.Foreground ? "前台侧栏与输入控件监听" : snapshot.Reason);
                }
                await Task.Delay(current.Length > 0 ? 400 : 1000, lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception) { Volatile.Write(ref sidebarStatus, "前台控件暂不可读，后台监听保持"); await Task.Delay(1000).ConfigureAwait(false); }
        }
    }
    private void ApplySidebar(CodexSidebarSnapshot snapshot, CodexWatch[] sampledWatches)
    {
        if (!snapshot.Foreground || snapshot.ViewEpoch.Length == 0)
        { foreach (var state in sidebarStates.Values) { state.View = ""; state.SawWaiting = false; state.RunningSamples = 0; } return; }
        foreach (var watch in sampledWatches)
        {
            if (!Volatile.Read(ref watches).Contains(watch) || !trackers.TryGetValue(watch, out var binding) || binding.CreatedUnixMs > snapshot.AtMs ||
                !passiveStates.TryGetValue(watch, out var local) || local.Activity.Status != "inProgress" ||
                !local.Activity.ProjectionCaughtUp || passiveUnavailable.Contains(watch) || projectionLagSince.ContainsKey(watch)) continue;
            if (!sidebarStates.TryGetValue(watch, out var state) || state.Turn != local.Activity.TurnId)
                sidebarStates[watch] = state = new(local.Activity.TurnId);
            if (snapshot.AtMs <= state.At) continue;
            var row = snapshot.Rows.SingleOrDefault(r => r.ThreadId == watch.ThreadId);
            if (state.View != snapshot.ViewEpoch || snapshot.AtMs - state.At > 2500 || row is null || row.Phase == CodexSidebarPhase.Unknown)
            { state.SawWaiting = false; state.RunningSamples = 0; state.Phase = CodexSidebarPhase.Unknown; }
            state.View = snapshot.ViewEpoch; state.At = snapshot.AtMs;
            if (row is null || row.Phase == CodexSidebarPhase.Unknown) continue;
            if (row.Phase is CodexSidebarPhase.WaitingForInput or CodexSidebarPhase.WaitingForApproval)
            {
                var changed = !state.SawWaiting || state.WaitIdentity != row.WaitIdentity || state.Phase != row.Phase;
                state.SawWaiting = true; state.RunningSamples = 0; state.Phase = row.Phase;
                state.LastWaitingSampleAt = snapshot.AtMs;
                if (!changed) continue;
                state.WaitIdentity = row.WaitIdentity; state.WaitAt = snapshot.AtMs;
                Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/sidebarWaiting", @params = new
                { threadId = watch.ThreadId, turnId = state.Turn, atMs = state.WaitAt, identity = row.WaitIdentity, approval = row.Phase == CodexSidebarPhase.WaitingForApproval } }), true, epoch);
                passiveStates[watch] = (local.Activity, true);
            }
            else if (row.Phase == CodexSidebarPhase.Running)
            {
                state.RunningSamples = state.Phase == CodexSidebarPhase.Running ? state.RunningSamples + 1 : 1;
                state.Phase = CodexSidebarPhase.Running;
                var waitAt = Math.Max(state.WaitAt, sidebarSavedWaits.GetValueOrDefault(watch.BindingId));
                var executionAt = Math.Max(local.Activity.ExecutionStartedMs, behavior?.ExecutionAt(watch.ThreadId, state.Turn) ?? 0);
                var laterExecution = waitAt > 0 && executionAt > waitAt;
                // An active conversation can retain the same generic spinner
                // throughout its form. Closing that form only proves a response;
                // require execution after its last confirmed waiting sample.
                var transition = state.SawWaiting && (!state.WaitIdentity.StartsWith("form:", StringComparison.Ordinal) || executionAt > state.LastWaitingSampleAt);
                if (state.RunningSamples < 2 || !(transition || !state.SawWaiting && laterExecution) ||
                    !(local.Waiting || sidebarWaitingBindings.Contains(watch.BindingId))) continue;
                Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/sidebarRunning", @params = new
                { threadId = watch.ThreadId, turnId = state.Turn } }), true, epoch);
                // Exact log requests retain their own protection in CodexState.
                // Rechecking a stable live running control can complete recovery
                // after a separately identified response arrives one poll later.
                passiveStates[watch] = (local.Activity, behavior?.Flags(watch.ThreadId).Length > 0);
            }
        }
    }
    private void ReconcileSidebarExecution(CodexWatch watch, CodexActivity activity)
    {
        if (!activity.ProjectionCaughtUp || activity.Status != "inProgress") return;
        if (!sidebarStates.TryGetValue(watch, out var state)) return;
        if (state.Turn != activity.TurnId) return;
        var waitAt = Math.Max(state.LastWaitingSampleAt, sidebarSavedWaits.GetValueOrDefault(watch.BindingId));
        var executionAt = Math.Max(activity.ExecutionStartedMs, behavior?.ExecutionAt(watch.ThreadId, activity.TurnId) ?? 0);
        if (waitAt <= 0 || executionAt <= waitAt) return;
        Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/sidebarRunning", @params = new
        { threadId = watch.ThreadId, turnId = activity.TurnId } }), true, epoch);
        state.WaitAt = state.LastWaitingSampleAt = 0; state.SawWaiting = false;
        sidebarSavedWaits = sidebarSavedWaits.Where(x => x.Key != watch.BindingId).ToDictionary(x => x.Key, x => x.Value);
        passiveStates[watch] = (activity, behavior?.Flags(watch.ThreadId).Length > 0);
    }
}
