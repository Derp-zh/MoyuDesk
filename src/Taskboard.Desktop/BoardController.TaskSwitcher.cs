using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private bool appWasForeground;
    private long suppressForegroundCollapseUntil;

    internal bool HasTaskSwitcherWindow => false;

    internal void SuppressForegroundCollapse(int milliseconds = 350)
    {
        var until = Environment.TickCount64 + milliseconds;
        if (until > suppressForegroundCollapseUntil) suppressForegroundCollapseUntil = until;
    }

    internal void MarkAppForeground()
    {
        appWasForeground = true;
    }

    internal void NotifyActiveWindowHiding(SurfaceWindow window)
    {
        if (exiting || !Expanded) return;
        SuppressForegroundCollapse(350);
        if (Smoke) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting || !Expanded || SuppressCollapse || restoringGroup) return;
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fg, out var pid);
                if (pid == Environment.ProcessId) return;
            }
            var next = DismissibleWindow() ?? (SurfaceWindow?)VisibleColumns.FirstOrDefault(x => !x.IsPinnedOverlay);
            if (next is not null && next.IsVisible)
            {
                SuppressForegroundCollapse(350);
                MarkAppForeground();
                next.Activate();
                Native.RaiseBall();
            }
        }), DispatcherPriority.Input);
    }

    internal bool ShouldCollapseOnForegroundLoss(IntPtr externalHwnd, long nowTicks,
        Func<IntPtr, bool>? isImeWindow = null, Func<IntPtr, bool>? isSwitcherOverlay = null)
    {
        if (exiting || dataTransition || !Expanded || SuppressCollapse || restoringGroup) return false;
        if (ConversationDrag is not null || CardDrag is not null) return false;
        if (!appWasForeground) return false;
        if (nowTicks < suppressForegroundCollapseUntil) return false;
        if (Native.Dialogs().Length != 0) return false;
        if (externalHwnd != IntPtr.Zero)
        {
            if ((isImeWindow ?? ImeWindowFilter.IsImeWindow)(externalHwnd)) return false;
            if ((isSwitcherOverlay ?? ImeWindowFilter.IsTaskSwitcherOverlay)(externalHwnd)) return false;
        }
        return true;
    }

    private void OnForegroundChanged(IntPtr handle)
    {
        if (handle == IntPtr.Zero || exiting) return;
        GetWindowThreadProcessId(handle, out var process);
        if (process == Environment.ProcessId)
        {
            appWasForeground = true;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (exiting) return;
                Native.RaiseBall();
            }));
            return;
        }
        if (!Expanded || SuppressCollapse || restoringGroup || !appWasForeground) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting || !Expanded) return;
            var current = Native.GetForegroundWindow();
            if (current != IntPtr.Zero)
            {
                GetWindowThreadProcessId(current, out var currentPid);
                if (currentPid == Environment.ProcessId) return;
            }
            var target = current != IntPtr.Zero ? current : handle;
            if (!ShouldCollapseOnForegroundLoss(target, Environment.TickCount64)) return;
            appWasForeground = false;
            SetExpanded(false);
        }), DispatcherPriority.Input);
    }
}
