using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Local SQLite directory only; retries and concurrency are owned by this module.
internal sealed class CodexCatalog(Func<Task<CodexThread[]>>? readLocal = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    internal bool Failed { get; private set; }
    internal async Task<CodexThread[]> Load()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var local = await (readLocal ?? (() => CodexLocalCatalog.Load()))().ConfigureAwait(false);
                    Failed = false; return local;
                }
                catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or OperationCanceledException or
                    UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Net.WebSockets.WebSocketException)
                {
                    if (attempt == 0) continue;
                    Failed = true; throw new IOException("Codex 会话列表暂不可用，已自动重试；请稍后再次点击绑定会话。", e);
                }
            }
        }
        finally { gate.Release(); }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
