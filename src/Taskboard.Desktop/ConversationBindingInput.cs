using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Taskboard.Desktop;

// Observe only for the post-release transaction. Movement is free; a new click,
// wheel or Escape cancels instead of letting another menu replace the target.
// No event is swallowed and no global pointer/input restriction is installed.
internal sealed class ConversationBindingInput : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Hook mouseCallback, keyCallback;
    private readonly IntPtr mouse, keyboard;
    private bool disposed;
    internal CancellationToken Token => stop.Token;
    internal ConversationBindingInput(bool observeMouse = true, bool includeInjectedEscape = false)
    {
        mouseCallback = (code, message, data) =>
        {
            if (code >= 0 && message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20B or 0x20A or 0x20E) stop.Cancel();
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        keyCallback = (code, message, data) =>
        {
            if (code >= 0 && message.ToInt64() is 0x100 or 0x104 && Marshal.ReadInt32(data) == 27
                && (includeInjectedEscape || (Marshal.ReadInt32(data, 8) & 0x10) == 0)) stop.Cancel();
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        mouse = observeMouse ? SetWindowsHookEx(14, mouseCallback, IntPtr.Zero, 0) : IntPtr.Zero;
        keyboard = SetWindowsHookEx(13, keyCallback, IntPtr.Zero, 0);
        if (observeMouse && mouse == IntPtr.Zero || keyboard == IntPtr.Zero)
        { Dispose(); throw new InvalidOperationException("无法观察本次绑定的取消操作"); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        stop.Cancel();
        if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
        if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
        stop.Dispose();
    }
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
}
