using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record ConversationNavigationResult(bool Success, string Message);

internal static class ConversationNavigator
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static string? CodexUri(string id) => AiState.NormalizeSessionId(AiPlatform.Codex, id) is { } normalized
        ? "codex://threads/" + normalized : null;
    internal static async Task<ConversationNavigationResult> Open(BoardController controller, Guid taskId, Guid stageId, Guid bindingId)
    {
        if (!await gate.WaitAsync(0)) return new(false, "正在跳转，请稍候");
        try
        {
            var state = controller.Engine.State;
            var stage = state.Tasks.FirstOrDefault(x => x.Id == taskId)?.Stages.FirstOrDefault(x => x.Id == stageId);
            if (stage?.Codex is not { } source || source.BindingId != bindingId) return new(false, "会话绑定已变化，请重新点击");
            var binding = Copies.Of(source);
            if (controller.Smoke) return new(false, "离屏检查不会打开外部客户端");
            if (binding.Platform == AiPlatform.Codex)
            {
                var uri = CodexUri(binding.ThreadId);
                if (uri is null) return new(false, "Codex会话编号无效，请重新绑定");
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return new(true, "");
            }
            if (binding.Platform == AiPlatform.ClaudeCode)
                return new(false, "Claude Code当前接入为本机事件监听，尚无已核实的客户端会话跳转入口");
            var desktop = state.Preferences.ConnectionConfigs.GetValueOrDefault("dsh.desktop", "");
            var exact = (await controller.LoadPlatformConversations(AiPlatform.Dsh)).FirstOrDefault(x => x.Id == binding.ThreadId);
            if (exact is null) return new(false, "本机DSH会话记录不可读，请先检查DSH监听设置");
            if (!ReferenceEquals(state, controller.Engine.State) || stage.Codex?.BindingId != bindingId)
                return new(false, "任务或会话绑定已变化，请重新点击");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var input = new ConversationBindingInput();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, input.Token);
            return await controller.WithConversationWindowsClear(() => DshConversationTarget.Navigate(binding.ThreadId, exact.DisplayName, desktop, stop.Token).WaitAsync(stop.Token));
        }
        catch (OperationCanceledException) { return new(false, "跳转已取消或超时，原绑定保持"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Windows.Automation.ElementNotAvailableException)
        { return new(false, "未能跳转至会话：" + error.Message); }
        finally { gate.Release(); }
    }
}

internal sealed partial class BoardController
{
    // Cloaking only lets the target's hover menus receive input. It does not
    // close editors, submit drafts, or change the user's expanded preference.
    internal async Task<ConversationNavigationResult> WithConversationWindowsClear(Func<Task<ConversationNavigationResult>> action)
    {
        var windows = Columns.Cast<SurfaceWindow>().Concat(taskWindows.Values).Concat(auxiliary)
            .Append(draftStack).Append(Ball).Where(x => x.IsVisible).Distinct().ToArray();
        var cloaked = new List<(SurfaceWindow Window, IntPtr Handle)>();
        var previous = SuppressCollapse; SuppressCollapse = true;
        try
        {
            foreach (var window in windows)
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (!Native.Cloak(handle, true)) return new(false, "看板窗口未能临时让开，请移动窗口后重试");
                cloaked.Add((window, handle));
            }
            return await action();
        }
        finally
        {
            foreach (var (window, handle) in cloaked)
                if (new WindowInteropHelper(window).Handle == handle) Native.Cloak(handle, false);
            SuppressCollapse = previous;
        }
    }
}
