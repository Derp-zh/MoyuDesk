using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    // Preview seeds and diagnostics are test-only. Drag binding itself is an
    // ordinary card action and has no preview or integration-mode gate.
    internal bool ConversationPreviewMode { get; set; }
    internal ConversationBindingDrag? ConversationDrag { get; set; }
    internal void TraceBindingGesture(string action)
    {
        if (!ConversationPreviewMode || !Isolated) return;
        try { File.AppendAllText(Path.Combine(Store.DirectoryPath, "binding-gesture-trace.jsonl"), JsonSerializer.Serialize(new
            { action, time = Environment.TickCount64, drag = ConversationDrag is not null, opened = bindingWindow?.Current is not null,
                dragWindow = bindingWindow?.DragMode }) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    internal async Task<string> CompleteConversationBinding(Guid task, Guid stage, Guid? expected, ConversationDropTarget target, object? expectedState = null)
    {
        WriteDropDiagnostic(task, stage, target, false);
        if (!target.IsUnbind && target.ThreadId is null) return "未绑定：" + target.Reason;
        var state = Engine.State;
        await humanTaskGate.WaitAsync();
        try
        {
            if (exiting || dataTransition || !ReferenceEquals(state, Engine.State) || expectedState is not null && !ReferenceEquals(expectedState, state))
                return target.IsUnbind ? "未解绑：任务数据已变化，请重新拖动" : "未绑定：任务数据已变化，请重新拖动";
            await CleanupRetainedStages();
            if (target.IsUnbind)
            {
                if (expected is null) return "未解绑：当前阶段尚未绑定会话，无需解除";
                if (!Engine.UnbindStageConversation(task, stage, expected))
                    return "未解绑：任务、阶段或原绑定已变化，请重新拖动";
                var savedUnbind = await Flush();
                WriteDropDiagnostic(task, stage, target, savedUnbind);
                return savedUnbind ? "已解除绑定" : "已在内存中解除绑定，保存失败，请重试保存";
            }
            if (!Engine.BindStageConversation(task, stage, expected, target.ThreadId!, target.Name, target.Platform))
                return "未绑定：任务、阶段或原绑定已变化，请重新拖动";
            var saved = await Flush();
            WriteDropDiagnostic(task, stage, target, saved);
            var result = saved ? "已绑定 · " + target.Name : "绑定仍在内存中，保存失败，请重试保存";
            return target.ClipboardRestored == false ? result + "；剪贴板未确认恢复，已有新内容时不会覆盖" : result;
        }
        finally { humanTaskGate.Release(); }
    }
    private void WriteDropDiagnostic(Guid task, Guid stage, ConversationDropTarget target, bool saved)
    {
        if (!ConversationPreviewMode || !Isolated) return;
        try
        {
            File.WriteAllText(Path.Combine(Store.DirectoryPath, "last-conversation-drop.json"), JsonSerializer.Serialize(new
            { task, stage, target.ThreadId, target.Name, target.Method, target.Reason, target.ClipboardRestored, saved, menuStep = CodexConversationMenu.LastStep }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { /* A test receipt cannot invalidate or conceal a completed save. */ }
    }
}
