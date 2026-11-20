using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record CodexThread(string Id, string Name, long UpdatedAt, AiPlatform Platform = AiPlatform.Codex, string ParentId = "", string RootId = "")
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名会话" : Name;
    public string Caption => DisplayName + " · " + Id;
    internal string SessionRoot => ParentId.Length == 0 ? Id : RootId;
}
internal sealed record CodexWatch(Guid TaskId, Guid StageId, Guid BindingId, string ThreadId, int Generation, long RelatedBoundAtMs = 0);
internal sealed record CodexThreadCapability(string ThreadId, bool Live, string Reason, string StatusType,
    bool PendingAvailable = false, string PendingReason = "not-checked");
internal sealed partial class CodexObserver : IAsyncDisposable
{
    private sealed class BindingState
    { internal readonly long CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<CodexWatch, BindingState> trackers = new();
    private readonly CodexCatalog catalog;
    private readonly bool metadataEnabled;
    private Task? directoryWorker;
    private readonly Func<ICodexActivitySource> connectActivity;
    private readonly CodexBehaviorSource? behavior;
    private bool behaviorUnavailable;
    internal string BehaviorStatus => (behavior?.Status ?? "本地行为补充已关闭") + " · " + SidebarStatus;
    private readonly ConcurrentDictionary<CodexWatch, (CodexActivity Activity, bool Waiting)> passiveStates = new();
    private readonly HashSet<CodexWatch> passiveUnavailable = new();
    private readonly HashSet<CodexWatch> protectedInitialWait = new();
    private readonly Dictionary<CodexWatch,string> savedWaitingTurns = new();
    private readonly Dictionary<CodexWatch,string> observedProofs = new(), pendingProofs = new();
    private readonly Dictionary<CodexWatch,long> projectionLagSince = new();
    private readonly object workerGate = new(), localApplyGate = new();
    private Task? worker;
    private CodexWatch[] watches = [];
    private CodexThreadCapability[] capabilities = [];
    internal static readonly TimeSpan LocalInterval = TimeSpan.FromSeconds(1);
    internal CodexObserver(Func<ICodexActivitySource>? activitySource = null,
        bool behaviorEnabled = false, CodexBehaviorSource? behaviorSource = null, ICodexSidebarSource? sidebarSource = null, Func<Task<CodexThread[]>>? readCatalog = null)
    {
        connectActivity = activitySource ?? (() => new CodexFileActivity());
        behavior = behaviorEnabled ? behaviorSource ?? new CodexBehaviorSource() : null;
        sidebar = sidebarSource ?? (activitySource is null ? new CodexSidebarSource() : null);
        catalog = new(readCatalog); metadataEnabled = activitySource is null || readCatalog is not null;
    }
    private long sequence, localReadCount;
    internal long LocalReadCount => Interlocked.Read(ref localReadCount);
    private string epoch = "";
    internal string Epoch => epoch;
    internal bool Authoritative { get; private set; }
    internal bool TransportConnected { get; private set; }
    internal bool SourceIdentityVerified { get; private set; }
    internal bool MonitoringReady => TransportConnected;
    internal string SourceKind { get; private set; } = "none";
    internal string ReadMode { get; private set; } = "waiting-local";
    internal string PendingSourceReason { get; private set; } = "disabled";
    internal CodexThreadCapability[] Capabilities => Volatile.Read(ref capabilities);
    internal string Status { get; private set; } = "尚未连接 · 状态待确认";
    internal string ConnectionHint => !MonitoringReady ? "Codex 未连接，正在自动重试" :
        catalog.Failed ? "Codex 会话列表暂不可用，选择会话时将重试" : "";
    internal event Action? Changed;
    internal event Action<CodexThread[]>? DirectoryChanged;
    internal event Action<CodexWatch, JsonElement, long, string, bool>? Event;
    internal void Watch(CodexWatch[] values, IReadOnlySet<Guid>? preservedWaitingBindings = null,
        IReadOnlyDictionary<Guid,string>? preservedWaitingTurns = null, IReadOnlyDictionary<Guid,long>? preservedSidebarWaiting = null,
        IReadOnlyDictionary<Guid,string>? observedLocalTurns = null)
    {
        Volatile.Write(ref watches, values);
        lock (localApplyGate)
        {
            sidebarWaitingBindings = preservedWaitingBindings ?? new HashSet<Guid>();
            sidebarSavedWaits = preservedSidebarWaiting ?? new Dictionary<Guid,long>();
            foreach (var removed in sidebarStates.Keys.Where(w => !values.Contains(w)).ToArray()) sidebarStates.Remove(removed);
            protectedInitialWait.RemoveWhere(watch => !values.Contains(watch));
            foreach(var removed in savedWaitingTurns.Keys.Where(w=>!values.Contains(w)).ToArray()) savedWaitingTurns.Remove(removed);
            foreach (var removed in observedProofs.Keys.Where(w => !values.Contains(w)).ToArray()) { observedProofs.Remove(removed); pendingProofs.Remove(removed); }
            foreach (var watch in values)
            {
                if (!trackers.ContainsKey(watch) && preservedWaitingBindings?.Contains(watch.BindingId) == true)
                {
                    protectedInitialWait.Add(watch);
                    if(preservedWaitingTurns?.TryGetValue(watch.BindingId,out var savedTurn)==true && !string.IsNullOrWhiteSpace(savedTurn))
                        savedWaitingTurns[watch]=savedTurn;
                }
                trackers.GetOrAdd(watch, value => new BindingState());
                if (observedLocalTurns?.TryGetValue(watch.BindingId, out var observed) == true && observed.Length > 0 && observedProofs.GetValueOrDefault(watch) != observed)
                    observedProofs[watch] = pendingProofs[watch] = observed;
            }
            foreach (var removed in trackers.Keys.Where(x => !values.Contains(x))) trackers.TryRemove(removed, out _);
        }
    }
    internal void Start() => EnsureConnection();
    internal void EnsureConnection()
    {
        lock (workerGate)
        {
            if (!lifetime.IsCancellationRequested && (worker is null || worker.IsCompleted)) worker = Task.Run(RunPassive);
            if (sidebar is not null && !lifetime.IsCancellationRequested && (sidebarWorker is null || sidebarWorker.IsCompleted))
                sidebarWorker = Task.Run(RunSidebar);
            if (metadataEnabled && !lifetime.IsCancellationRequested && (directoryWorker is null || directoryWorker.IsCompleted))
                directoryWorker = Task.Run(RunDirectory);
        }
    }
    private void SetStatus(string value) { if (Status == value) return; Status = value; Changed?.Invoke(); }
    private async Task RunPassive()
    {
        epoch = Guid.NewGuid().ToString("N"); sequence = 0; SourceKind = "local-files"; ReadMode = "local-files";
        PendingSourceReason = behavior is null ? "local-record-no-request-index" : "local-behavior-incremental-not-full-index";
        var source = connectActivity();
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var current = Volatile.Read(ref watches);
                Interlocked.Increment(ref localReadCount);
                var records = source.Read(current.Select(x => x.ThreadId).Distinct().ToArray());
                lock (localApplyGate)
                {
                    IReadOnlyList<CodexBehaviorEvent> behaviorChanges = [];
                    if (behavior is not null)
                    {
                        try { behaviorChanges = behavior.Read(records, current.Select(w => w.ThreadId).Distinct().ToArray()); behaviorUnavailable = false; }
                        catch (Exception) when (!lifetime.IsCancellationRequested)
                        {
                            behavior.Unavailable();
                            if (!behaviorUnavailable) foreach (var watch in current) EmitUnknown(watch, epoch);
                            behaviorUnavailable = true;
                        }
                    }
                    foreach (var removed in passiveStates.Keys.Where(w => !current.Contains(w)))
                    { passiveStates.TryRemove(removed, out _); passiveUnavailable.Remove(removed); projectionLagSince.Remove(removed); }
                    var next = new List<CodexThreadCapability>();
                    var retainedRecentEvidence = false;
                    foreach (var watch in current)
                    {
                        if (!records.TryGetValue(watch.ThreadId, out var activity) || !activity.ProjectionCaughtUp)
                        {
                            if (behavior is not null && passiveStates.ContainsKey(watch))
                            {
                                var now = Environment.TickCount64;
                                if (!projectionLagSince.TryGetValue(watch,out var since)) projectionLagSince[watch] = since = now;
                                if (now-since < 1500)
                                { retainedRecentEvidence=true; next.Add(new(watch.ThreadId,false,"local-projection-catching-up","unknown")); continue; }
                            }
                            if (passiveUnavailable.Add(watch)) EmitUnknown(watch, epoch);
                            next.Add(new(watch.ThreadId, false, "local-projection-unavailable", "unknown")); continue;
                        }
                        projectionLagSince.Remove(watch);
                        var wasUnavailable = passiveUnavailable.Remove(watch);
                        var known = passiveStates.TryGetValue(watch, out var old);
                        if (behavior?.Reconciling == true && activity.Status == "inProgress")
                        {
                            if (passiveUnavailable.Add(watch) && !known) EmitUnknown(watch,epoch);
                            retainedRecentEvidence |= known;
                            next.Add(new(watch.ThreadId,false,"reconciling-current-requests","unknown")); continue;
                        }
                        var sameTurn = known && old.Activity.TurnId == activity.TurnId;
                        if (known && (activity.Ordinal < old.Activity.Ordinal || activity.Ordinal == old.Activity.Ordinal && !sameTurn ||
                            sameTurn && old.Activity.Status != "inProgress" && activity.Status == "inProgress"))
                        {
                            if (passiveUnavailable.Add(watch)) EmitUnknown(watch, epoch);
                            next.Add(new(watch.ThreadId,false,"local-record-regressed","unknown")); continue;
                        }
                        // Baseline terminal rows are history, not new results for a newly bound card.
                        var freshTerminal = sameTurn && old.Activity.Status == "inProgress";
                        var preservingWait = !known && protectedInitialWait.Remove(watch);
                        if (preservingWait)
                        {
                            // Reconcile an offline result only for the exact turn saved by this
                            // waiting binding. A newly bound card must never inherit old history.
                            var savedTerminal=activity.Status!="inProgress" && savedWaitingTurns.TryGetValue(watch,out var savedTurn) && savedTurn==activity.TurnId;
                            if (savedWaitingTurns.GetValueOrDefault(watch) == activity.TurnId && sidebarSavedWaits.GetValueOrDefault(watch.BindingId) > 0)
                                sidebarStates[watch] = new(activity.TurnId);
                            if(activity.Status=="inProgress" || savedTerminal)
                                Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/preserveLocalTurn", @params = new { threadId = watch.ThreadId, turnId = activity.TurnId } }), true, epoch);
                            if(savedTerminal)
                                Emit(watch,JsonSerializer.SerializeToElement(new {method="turn/completed",@params=new {threadId=watch.ThreadId,turn=new{id=activity.TurnId,status=activity.Status}}}),true,epoch);
                            savedWaitingTurns.Remove(watch);
                        }
                        else if (activity.Status != "inProgress" && pendingProofs.GetValueOrDefault(watch) == activity.TurnId)
                        {
                            pendingProofs.Remove(watch);
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "turn/completed", @params = new { threadId = watch.ThreadId,
                                turn = new { id = activity.TurnId, status = activity.Status } } }), true, epoch);
                        }
                        else if (!known && watch.RelatedBoundAtMs > 0 && activity.Status != "inProgress" && activity.StartedAt > watch.RelatedBoundAtMs / 1000 + 1)
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/localTurnCompleted", @params = new { threadId = watch.ThreadId,
                                baselineStartedAt = watch.RelatedBoundAtMs / 1000 + 1, turn = new { id = activity.TurnId, status = activity.Status, startedAt = activity.StartedAt } } }), true, epoch);
                        else if (activity.Status == "inProgress" && behavior is not null && (!behavior.Available || behavior.BootstrapIncomplete))
                        {
                            if (!known) Emit(watch,JsonSerializer.SerializeToElement(new {method="probe/observeLocalTurn",@params=new {threadId=watch.ThreadId,turnId=activity.TurnId}}),true,epoch);
                            if (passiveUnavailable.Add(watch) && !old.Waiting) EmitUnknown(watch,epoch);
                        }
                        else if (activity.Status == "inProgress" && (!sameTurn || old.Activity.Status != activity.Status))
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = watch.ThreadId, turnId = activity.TurnId,
                                status = new {type="active",activeFlags=behavior?.Flags(watch.ThreadId) ?? []} } }), true, epoch);
                        else if (activity.Status != "inProgress" && freshTerminal)
                        {
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "turn/completed", @params = new { threadId = watch.ThreadId, turn = new { id = activity.TurnId, status = activity.Status } } }), true, epoch);
                        }
                        else if (known && !sameTurn && activity.Status != "inProgress" && trackers.TryGetValue(watch, out var bindingState) &&
                            activity.StartedAt > bindingState.CreatedUnixMs / 1000 + 1 && activity.Ordinal > old.Activity.Ordinal)
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/localTurnCompleted", @params = new { threadId = watch.ThreadId,
                                baselineStartedAt = bindingState.CreatedUnixMs / 1000 + 1, turn = new { id = activity.TurnId, status = activity.Status, startedAt = activity.StartedAt } } }), true, epoch);
                        else if (wasUnavailable && sameTurn && !old.Waiting && activity.Status == "inProgress")
                            Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/localStatusObserved", @params = new { threadId = watch.ThreadId, turnId = activity.TurnId } }), true, epoch);
                        passiveStates[watch] = (activity, (preservingWait || sameTurn && old.Waiting) && activity.Status == "inProgress");
                        if (!known && behavior is not null && !behaviorChanges.Any(c => c.ThreadId == watch.ThreadId))
                            foreach (var snapshot in behavior.Snapshot(watch.ThreadId)) ReceiveBehavior(snapshot,watch);
                        next.Add(new(watch.ThreadId, true, "local-record", activity.Status, false, behavior is null ? "behavior-disabled" : "local-behavior-incremental"));
                    }
                    foreach (var change in behaviorChanges) ReceiveBehavior(change);
                    foreach (var watch in current)
                        if (records.TryGetValue(watch.ThreadId, out var activity) && !passiveUnavailable.Contains(watch) && !projectionLagSince.ContainsKey(watch))
                            ReconcileSidebarExecution(watch, activity);
                    Volatile.Write(ref capabilities, next.ToArray()); Authoritative = next.Any(x => x.Live) || retainedRecentEvidence;
                    TransportConnected = true; SourceIdentityVerified = false;
                }
                SetStatus(behavior is null ? "Codex 本机轮次监听" : "Codex 本机轮次监听 · " + BehaviorStatus);
            }
            catch (Exception error) when (!lifetime.IsCancellationRequested)
            {
                TransportConnected = false; SourceIdentityVerified = false; Authoritative = false;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>());
                lock (localApplyGate) foreach (var watch in Volatile.Read(ref watches))
                    if (passiveUnavailable.Add(watch)) EmitUnknown(watch, epoch);
                SetStatus("Codex 本机记录暂不可读：" + error.Message);
            }
            try { await Task.Delay(LocalInterval, lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
    internal bool ReceiveBehavior(CodexBehaviorEvent change, CodexWatch? target = null)
    {
        lock (localApplyGate)
        {
            var accepted = false;
            foreach (var watch in Volatile.Read(ref watches).Where(x => x.ThreadId == change.ThreadId && (target is null || x == target)))
            {
                if (!passiveStates.TryGetValue(watch, out var state) || state.Activity.TurnId != change.TurnId ||
                    state.Activity.Status != "inProgress" || !state.Activity.ProjectionCaughtUp || passiveUnavailable.Contains(watch) ||
                    !trackers.TryGetValue(watch, out var tracker) || tracker.CreatedUnixMs > change.AtMs) continue;
                if (change.Reconciled && change.Method == "thread/status/changed" && !state.Waiting) continue;
                var message = change.Method == "serverRequest/resolved"
                    ? JsonSerializer.SerializeToElement(new { method = change.Method, @params = new { threadId = change.ThreadId, turnId = change.TurnId, requestId = change.RequestId } })
                    : change.Method == "thread/status/changed"
                    ? JsonSerializer.SerializeToElement(new { method = change.Method, @params = new { threadId = change.ThreadId, turnId = change.TurnId, status = new { type = "active", activeFlags = Array.Empty<string>() } } })
                    : JsonSerializer.SerializeToElement(new { method = change.Method, id = change.RequestId, @params = new { threadId = change.ThreadId, turnId = change.TurnId, atMs = change.AtMs } });
                if (change.Method is not ("serverRequest/resolved" or "thread/status/changed" or "item/tool/requestUserInput" or "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval")) continue;
                Emit(watch, message, true, epoch);
                if (change.Method != "serverRequest/resolved")
                    passiveStates[watch] = (state.Activity, change.Method != "thread/status/changed");
                accepted = true;
            }
            return accepted;
        }
    }
    internal CodexDesktopSnapshot PeekSnapshot(string threadId)
    {
        lock (localApplyGate)
        {
            var entry = passiveStates.FirstOrDefault(x => x.Key.ThreadId == threadId);
            if (entry.Key is null || !TransportConnected || passiveUnavailable.Contains(entry.Key) || projectionLagSince.ContainsKey(entry.Key))
                return new(default, [], false, "local-record-unavailable", "local");
            return new(JsonSerializer.SerializeToElement(new { type = entry.Value.Activity.Status == "inProgress" ? "active" : "idle",
                activeFlags = behavior?.Flags(threadId) ?? Array.Empty<string>() }), [new CodexTurnMetadata(entry.Value.Activity.TurnId, entry.Value.Activity.Status)],
                true, behavior is null ? "local-record-not-live-request-index" : "local-behavior-incremental-not-full-index", "local");
        }
    }
    private void EmitUnknown(CodexWatch watch, string sourceEpoch) => Emit(watch,
        JsonSerializer.SerializeToElement(new { method = "probe/disconnected", @params = new { threadId = watch.ThreadId } }), true, sourceEpoch);
    private void Emit(CodexWatch watch, JsonElement message, bool authoritative, string sourceEpoch)
    {
        if (sourceEpoch == epoch && Volatile.Read(ref watches).Contains(watch))
            Event?.Invoke(watch, message, Interlocked.Increment(ref sequence), sourceEpoch, authoritative);
    }
    internal async Task<CodexThread[]> Catalog()
    {
        try
        {
            var directory = await catalog.Load().ConfigureAwait(false);
            if (!lifetime.IsCancellationRequested) DirectoryChanged?.Invoke(directory);
            return directory;
        }
        finally { if (!lifetime.IsCancellationRequested) Changed?.Invoke(); }
    }
    private async Task RunDirectory()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                if (Volatile.Read(ref watches).Length > 0)
                {
                    var directory = await catalog.Load().ConfigureAwait(false);
                    if (!lifetime.IsCancellationRequested) { DirectoryChanged?.Invoke(directory); Changed?.Invoke(); }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            { Changed?.Invoke(); await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token).ConfigureAwait(false); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (worker is not null) { try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        if (sidebarWorker is not null) { try { await sidebarWorker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        if (directoryWorker is not null) { try { await directoryWorker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        await catalog.DisposeAsync().ConfigureAwait(false);
    }
}
