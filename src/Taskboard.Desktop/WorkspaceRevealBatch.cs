using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// One barrier for a single workspace restore. Animation durations remain local.
internal sealed class WorkspaceRevealBatch
{
    internal static WorkspaceRevealBatch? Current { get; set; }
    private static long sequence;
    internal long Id { get; } = ++sequence;
    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer deadline;
    private int participants, ready;
    private bool sealedBatch;
    private bool releasing;
    private bool released;
    private readonly List<Action> starts = [];
    internal WorkspaceRevealBatch()
    {
        deadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(500) };
        deadline.Tick += (_, _) => Release();
    }
    internal Ticket Enroll() { participants++; return new Ticket(this); }
    internal void Seal() { sealedBatch = true; if (ready == participants) Release(); else deadline.Start(); }
    private void Release()
    {
        deadline.Stop();
        if (releasing) return;
        releasing = true;
        // The last arrival must await the same pending task as every other
        // participant. Completing inline lets that window animate at Render
        // priority while outside-click continuations are still queued at Input.
        deadline.Dispatcher.BeginInvoke(new Action(() =>
        {
            released = true;
            foreach (var start in starts) start();
            starts.Clear(); gate.TrySetResult();
        }), DispatcherPriority.Normal);
    }
    private Task Arrive(Action? start = null)
    {
        if (start is not null)
        {
            if (released) deadline.Dispatcher.BeginInvoke(start, DispatcherPriority.Normal);
            else starts.Add(start);
        }
        ready++; if (sealedBatch && ready == participants) Release(); return gate.Task;
    }
    internal sealed class Ticket(WorkspaceRevealBatch batch) : IDisposable
    {
        private bool arrived;
        internal long GroupId => batch.Id;
        internal Task Ready() { if (!arrived) { arrived = true; return batch.Arrive(); } return batch.gate.Task; }
        internal void Start(Action start) { if (!arrived) { arrived = true; _ = batch.Arrive(start); } }
        public void Dispose() { if (!arrived) { arrived = true; _ = batch.Arrive(); } }
    }
}
