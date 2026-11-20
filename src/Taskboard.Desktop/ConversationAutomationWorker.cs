using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// Keep all native UIA elements on one MTA, including lookup and invocation.
// Read-only hover never transfers a COM element to the UI/clipboard apartment.
internal static class ConversationAutomationWorker
{
    private static readonly BlockingCollection<Action> work = new(8);
    private static readonly Thread thread = Start();
    private static Thread Start()
    {
        var worker = new Thread(() => { foreach (var action in work.GetConsumingEnumerable()) action(); })
            { IsBackground = true, Name = "Conversation UIA3" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start(); return worker;
    }
    internal static Task<ConversationDropTarget> Run(Func<ConversationDropTarget> action, CancellationToken cancellation = default)
    {
        _ = thread;
        var result = new TaskCompletionSource<ConversationDropTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.TryAdd(() =>
        {
            try { cancellation.ThrowIfCancellationRequested(); result.TrySetResult(action()); }
            catch (Exception) { result.TrySetResult(ConversationDropTarget.Unavailable("读取会话未完成，原绑定保留")); }
        })) result.TrySetResult(ConversationDropTarget.Unavailable("会话读取正忙，请稍后重新拖动"));
        return result.Task;
    }
    internal static Task<T> Run<T>(Func<T> action, CancellationToken cancellation = default)
    {
        _ = thread;
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.TryAdd(() =>
        {
            try { cancellation.ThrowIfCancellationRequested(); result.TrySetResult(action()); }
            catch (OperationCanceledException) { result.TrySetCanceled(cancellation); }
            catch (Exception error) { result.TrySetException(error); }
        })) result.TrySetException(new InvalidOperationException("会话读取正忙，请稍后重试"));
        return result.Task;
    }
}

internal static class ConversationClipboardThread
{
    private static readonly Lazy<Dispatcher> dispatcher = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => { ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); })
            { IsBackground = true, Name = "Conversation clipboard" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); return ready.Task.GetAwaiter().GetResult();
    });
    internal static T Run<T>(Func<T> action) => dispatcher.Value.Invoke(action);
}
