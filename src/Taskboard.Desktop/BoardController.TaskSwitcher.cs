using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private AppSwitcherWindow? taskSwitcher;
    private bool appWasForeground;
    private bool activatingFromSwitcher;
    private long suppressForegroundCollapseUntil;

    internal bool HasTaskSwitcherWindow => taskSwitcher is not null;
    internal IntPtr TaskSwitcherHandle => taskSwitcher is null ? IntPtr.Zero : new WindowInteropHelper(taskSwitcher).Handle;

    private void InitializeTaskSwitcher()
    {
        if (Smoke) return;
        taskSwitcher = new AppSwitcherWindow(this, trayIcon);
        taskSwitcher.Show();
    }

    internal void SuppressForegroundCollapse(int milliseconds = 350)
    {
        var until = Environment.TickCount64 + milliseconds;
        if (until > suppressForegroundCollapseUntil) suppressForegroundCollapseUntil = until;
    }

    internal void MarkAppForeground() => appWasForeground = true;

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
            var next = DismissibleWindow() ?? (SurfaceWindow?)VisibleColumns.FirstOrDefault();
            if (next is not null && next.IsVisible)
            {
                SuppressForegroundCollapse(350);
                appWasForeground = true;
                next.Activate();
                Native.RaiseBall();
            }
        }), DispatcherPriority.Input);
    }

    internal void ActivateFromTaskSwitcher()
    {
        if (exiting || dataTransition || activatingFromSwitcher) return;
        activatingFromSwitcher = true;
        try
        {
            appWasForeground = true;
            SuppressForegroundCollapse(400);
            if (!Expanded) SetExpanded(true);
            if (OffscreenNoShow) return;
            foreach (var column in VisibleColumns) Native.Raise(column);
            foreach (var window in FloatingWindows().Where(x => x.ShouldRestore).OrderBy(x => x.ActivationOrder))
                Native.Raise(window);
            var target = DismissibleWindow() ?? (SurfaceWindow?)VisibleColumns.FirstOrDefault() ?? Ball;
            target.Activate();
            Native.RaiseBall();
        }
        finally
        {
            activatingFromSwitcher = false;
        }
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
            Application.Current?.Dispatcher.BeginInvoke(new Action(Native.RaiseBall));
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

    private sealed class AppSwitcherWindow : Window
    {
        private readonly BoardController controller;
        private bool allowClose;

        internal AppSwitcherWindow(BoardController controller, System.Drawing.Icon trayIcon)
        {
            this.controller = controller;
            Title = Brand.Name;
            ShowInTaskbar = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Opacity = 0;
            Width = 1;
            Height = 1;
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
            Topmost = false;
            try
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                Native.ShowInTaskSwitcher(hwnd);
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            };
            Activated += (_, _) => controller.ActivateFromTaskSwitcher();
            Closing += (_, e) =>
            {
                if (allowClose || controller.exiting) return;
                e.Cancel = true;
                _ = controller.Exit();
            };
        }

        internal void DestroySwitcher()
        {
            allowClose = true;
            Close();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmSysCommand = 0x0112;
            const int ScMinimize = 0xF020;
            const int ScRestore = 0xF120;
            if (message == WmSysCommand)
            {
                var command = wParam.ToInt64() & 0xFFF0;
                if (command == ScMinimize)
                {
                    if (controller.Expanded && !controller.SuppressCollapse) controller.SetExpanded(false);
                    handled = true;
                    return IntPtr.Zero;
                }
                if (command == ScRestore)
                {
                    controller.ActivateFromTaskSwitcher();
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }
    }
}
