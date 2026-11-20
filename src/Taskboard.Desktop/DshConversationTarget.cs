using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class DshConversationTarget
{
    internal static string? ParseSessionId(string value) => value.Length is > 0 and <= 256 &&
        value == value.Trim() && System.Linq.Enumerable.All(value, c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? AiState.NormalizeSessionId(AiPlatform.Dsh, value) : null;
    internal static bool IsAt(Point point, string desktop = "")
    {
        var window = GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2);
        GetWindowThreadProcessId(window, out var pid); return VerifyProcess(pid, desktop);
    }
    internal static bool IsForeground(string desktop)
    { GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid); return VerifyProcess(pid, desktop); }
    internal static Task<ConversationDropTarget> Read(Point point, string desktop = "") => ConversationAutomationWorker.Run(() => Locate(point, desktop).Target);
    internal static Task<ConversationNavigationResult> Navigate(string sessionId, string name, string desktop, CancellationToken cancellation) => ConversationAutomationWorker.Run<ConversationNavigationResult>(() =>
    {
        if (ParseSessionId(sessionId) is null) return new ConversationNavigationResult(false, "DSH会话编号无效");
        var executable = AiDesktopDiscovery.Find(AiPlatform.Dsh, desktop);
        if (executable.Length == 0) return new(false, "未找到DSH客户端，请在设置中检查安装路径");
        IntPtr FindWindow()
        {
            foreach (var process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(executable)))
                using (process) try { if (VerifyProcess((uint)process.Id, desktop) && process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle; }
                catch (InvalidOperationException) { }
            return IntPtr.Zero;
        }
        var window = FindWindow();
        if (window == IntPtr.Zero)
        {
            using var launched = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
            var wait = Stopwatch.StartNew();
            while (window == IntPtr.Zero && wait.Elapsed.TotalSeconds < 20)
            { cancellation.ThrowIfCancellationRequested(); Thread.Sleep(100); window = FindWindow(); }
        }
        if (window == IntPtr.Zero) return new(false, "DSH未提供可用的主窗口");
        GetWindowThreadProcessId(window, out var pid);
        if (IsIconic(window)) Native.ShowWindow(window, 9);
        CodexConversationMenu.Foreground(window);
        var root = AutomationElement.FromHandle(window);
        void Ensure()
        {
            cancellation.ThrowIfCancellationRequested();
            if (!VerifyProcess(pid, desktop) || !CodexConversationMenu.BelongsToWindow(Native.GetForegroundWindow(), window))
                throw new InvalidOperationException("DSH目标或前台已变化");
        }
        static bool Invoke(AutomationElement element)
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) { ((InvokePattern)invoke).Invoke(); return true; }
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) { ((SelectionItemPattern)select).Select(); return true; }
            var runtime = element.GetRuntimeId(); var bounds = element.Current.BoundingRectangle;
            var api = new Interop.UIAutomationClient.CUIAutomation8();
            var hit = api.ElementFromPoint(new Interop.UIAutomationClient.tagPOINT { x = (int)(bounds.Left + bounds.Width / 2), y = (int)(bounds.Top + bounds.Height / 2) });
            for (var depth = 0; hit is not null && depth++ < 15; hit = api.RawViewWalker.GetParentElement(hit))
                if (runtime.SequenceEqual(hit.GetRuntimeId()) && hit.GetCurrentPattern(10018) is Interop.UIAutomationClient.IUIAutomationLegacyIAccessiblePattern legacy)
                { legacy.DoDefaultAction(); return true; }
            return false;
        }
        var checkedRows = new HashSet<string>();
        ConversationNavigationResult? TryRows(IEnumerable<AutomationElement> candidates)
        {
            foreach (var row in candidates)
            {
                Ensure();
                var runtime = row.GetRuntimeId();
                if (!checkedRows.Add(string.Join(",", runtime))) continue;
                using var menu = new DshMenu(window, pid, row, new(null, RowTitle(row) ?? "DSH会话", "", true, "dsh-navigation-menu", Platform: AiPlatform.Dsh), false, desktop, Native.GetForegroundWindow(), cancellation);
                using var clipboard = new CodexConversationMenu.NativeClipboard(pid);
                var target = ConversationLinkResolver.Run(menu, clipboard, cancellation);
                if (!target.CanBind) { Ensure(); continue; }
                if (target.ThreadId != sessionId) continue;
                Ensure();
                if (!menu.Valid || !Invoke(row)) return new(false, "已核对编号，但DSH会话行不支持打开");
                var opened = Stopwatch.StartNew();
                while (opened.ElapsedMilliseconds < 1200)
                {
                    Ensure();
                    if (Selected(row)) return new(true, "");
                    Thread.Sleep(20);
                }
                return new(false, "DSH尚未确认切换到目标会话");
            }
            return null;
        }
        // Try a known visible title first, but only its own ID can authorize a
        // selection. Do not expand hundreds of unrelated rows before this check.
        // Expand project groups and their incremental session watermarks. These
        // are navigation controls only, never conversation or approval actions.
        for (var round = 0; round < 200; round++)
        {
            if (TryRows(SessionRows(root).Where(x => RowTitle(x) == name).ToArray()) is { } result) return result;
            Ensure(); var changed = false;
            var groups = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)).Cast<AutomationElement>()
                .Where(x => x.Current.ClassName.Split(' ').Any(c => c.EndsWith("_projectRow", StringComparison.Ordinal))).ToArray();
            foreach (var group in groups)
                if (group.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) && ((ExpandCollapsePattern)pattern).Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                { Ensure(); ((ExpandCollapsePattern)pattern).Expand(); changed = true; }
            var more = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .Where(x => x.Current.ClassName.Split(' ').Any(c => c.EndsWith("_sessionOverflowButton", StringComparison.Ordinal)) &&
                    (x.Current.Name.StartsWith("展开其余 ", StringComparison.Ordinal) || x.Current.Name.StartsWith("Show ", StringComparison.Ordinal) && x.Current.Name.EndsWith(" more sessions", StringComparison.Ordinal))).ToArray();
            foreach (var button in more) { Ensure(); changed |= Invoke(button); }
            if (!changed) break;
            Thread.Sleep(40);
        }
        if (TryRows(SessionRows(root)) is { } fallback) return fallback;
        return new(false, "DSH侧栏没有提供该编号的可访问会话，请打开所属工作区后重试");
    }, cancellation);
    internal static Task<ConversationDropTarget> Resolve(Point point, CancellationToken cancellation, string desktop = "") => ConversationAutomationWorker.Run(() =>
    {
        var initial = Native.GetForegroundWindow(); var location = Locate(point, desktop);
        if (location.Row is null) return location.Target;
        cancellation.ThrowIfCancellationRequested();
        using var menu = new DshMenu(location.Window, location.Pid, location.Row, location.Target, location.CurrentChat, desktop, initial, cancellation);
        using var clipboard = new CodexConversationMenu.NativeClipboard(location.Pid);
        return ConversationLinkResolver.Run(menu, clipboard, cancellation);
    }, cancellation);
    internal static Rect ClipBounds(Rect bounds, IEnumerable<Rect> viewports)
    {
        bool Usable(Rect value) => !value.IsEmpty && double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Width) && double.IsFinite(value.Height);
        if (!Usable(bounds)) return Rect.Empty;
        foreach (var viewport in viewports)
        { if (!Usable(viewport)) return Rect.Empty; bounds.Intersect(viewport); if (bounds.IsEmpty) return bounds; }
        return bounds;
    }
    internal static bool IsConversationRegion(IEnumerable<string> classes)
    {
        var tokens = classes.SelectMany(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        return tokens.Any(c => c.EndsWith("_scrollBody", StringComparison.Ordinal) && tokens.Contains(c[..^11] + "_body"));
    }
    private static Rect VisibleBounds(AutomationElement row, IntPtr window)
    {
        if (row.Current.IsOffscreen || !GetWindowRect(window, out var rect)) return Rect.Empty;
        var viewports = new List<Rect> { new(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)) };
        var depth = 0;
        for (var parent = TreeWalker.RawViewWalker.GetParent(row); parent is not null && depth++ < 35; parent = TreeWalker.RawViewWalker.GetParent(parent))
        {
            if (parent.Current.ControlType == ControlType.Window) break;
            if (parent.TryGetCurrentPattern(ScrollPattern.Pattern, out _)) viewports.Add(parent.Current.BoundingRectangle);
        }
        return ClipBounds(row.Current.BoundingRectangle, viewports);
    }
    private static AutomationElement[] SessionRows(AutomationElement root) => root
        .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem))
        .Cast<AutomationElement>().Where(x => x.Current.ClassName.Contains("sessionRow", StringComparison.Ordinal)).ToArray();
    private static int VisibleChatRegions(AutomationElement root) => ChatRegions(root, null).Count;
    private static (int Count, bool Contains) ChatRegions(AutomationElement root, Point? point)
    {
        // Chromium marks these layout groups as raw-view elements; FindAll on
        // the window's control view omits them. Prune transcript/row subtrees.
        var pending = new Stack<AutomationElement>(); pending.Push(root);
        var visited = 0; var count = 0; var contains = false;
        while (pending.Count > 0 && visited++ < 4000)
        {
            var node = pending.Pop(); var value = node.Current;
            if (value.IsOffscreen) continue;
            if (value.ClassName.Split(' ').Any(c => c.EndsWith("_scrollBody", StringComparison.Ordinal)))
            {
                var parent = TreeWalker.RawViewWalker.GetParent(node);
                if (parent is not null && IsConversationRegion([value.ClassName, parent.Current.ClassName]))
                { count++; contains |= point is { } at && value.BoundingRectangle.Contains(at); }
                if (count > 1) return (count, contains);
                continue;
            }
            if (value.ControlType != ControlType.Window && value.ControlType != ControlType.Pane && value.ControlType != ControlType.Document &&
                value.ControlType != ControlType.Group && value.ControlType != ControlType.Custom) continue;
            for (var child = TreeWalker.RawViewWalker.GetFirstChild(node); child is not null; child = TreeWalker.RawViewWalker.GetNextSibling(child)) pending.Push(child);
        }
        return (pending.Count == 0 ? count : -1, contains);
    }
    private static bool Selected(AutomationElement row) => row.Current.ClassName.Split(' ').Any(c => c.EndsWith("_selected", StringComparison.Ordinal));
    private static string? RowTitle(AutomationElement row)
    {
        var rowClass = row.Current.ClassName.Split(' ').FirstOrDefault(c => c.EndsWith("_sessionRow", StringComparison.Ordinal));
        if (rowClass is null) return null;
        var title = row.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, rowClass[..^10] + "title"));
        if (!string.IsNullOrWhiteSpace(title?.Current.Name)) return title.Current.Name;
        // DSH's live accessibility tree exports the title and relative time as
        // separate direct Text children, without the CSS title class.
        for (var child = TreeWalker.RawViewWalker.GetFirstChild(row); child is not null; child = TreeWalker.RawViewWalker.GetNextSibling(child))
            if (child.Current.ControlType == ControlType.Text && !string.IsNullOrWhiteSpace(child.Current.Name)) return child.Current.Name;
        return null;
    }
    private static (ConversationDropTarget Target, IntPtr Window, uint Pid, AutomationElement? Row, bool CurrentChat) Locate(Point point, string desktop)
    {
        var window = GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2);
        GetWindowThreadProcessId(window, out var pid);
        (ConversationDropTarget, IntPtr, uint, AutomationElement?, bool) Failed(string reason) => (ConversationDropTarget.Unavailable(reason), window, pid, null, false);
        if (!VerifyProcess(pid, desktop)) return Failed("请拖到原 DSH 会话上");
        var root = AutomationElement.FromHandle(window);
        var rows = SessionRows(root);
        var at = rows.Where(x => x.Current.IsEnabled && VisibleBounds(x, window).Contains(point)).ToArray();
        AutomationElement? row = at.Length == 1 ? at[0] : null;
        var currentChat = row is null;
        if (row is null)
        {
            var depth = 0; var ancestorClasses = new List<string>();
            for (var current = AutomationElement.FromPoint(point); current is not null && depth++ < 40; current = TreeWalker.RawViewWalker.GetParent(current))
            {
                ancestorClasses.Add(current.Current.ClassName);
                if (current.Current.ControlType == ControlType.Window) break;
            }
            var chat = IsConversationRegion(ancestorClasses);
            var hasContentClasses = ancestorClasses.Any(c => c.Split(' ').Any(token => token.Contains('_') && !token.StartsWith("Chrome_", StringComparison.Ordinal)));
            var regions = ChatRegions(root, point);
            if (!chat && !hasContentClasses) chat = regions.Contains;
            if (!chat) return Failed("请拖到 DSH 侧栏会话或当前聊天区域，空白/工具栏不会绑定");
            if (regions.Count != 1) return Failed("DSH 当前聊天区域无法唯一确认，请拖到准确侧栏会话");
            var selected = rows.Where(Selected).ToArray();
            if (selected.Length != 1) return Failed("当前 DSH 会话无法唯一确认，请拖到准确侧栏会话");
            row = selected[0];
        }
        if (GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2) != window) return Failed("落点窗口已变化");
        return (new(null, RowTitle(row) ?? "DSH 会话", "松开取得原 DSH 会话编号", true, "dsh-element-menu", Platform: AiPlatform.Dsh), window, pid, row, currentChat);
    }
    private sealed class DshMenu(IntPtr window, uint pid, AutomationElement row, ConversationDropTarget target, bool currentChat, string desktop, IntPtr initial,
        CancellationToken cancellation) : IConversationLinkMenu
    {
        private readonly int[] identity = row.GetRuntimeId();
        private readonly string? title = RowTitle(row);
        private readonly HashSet<string> owned = new();
        private bool opened;
        private bool activated;
        private long regionsCheckedAt;
        private int regionCount;
        private bool capturedCommands;
        private MutablePoint? previousPointer, ownedPointer;
        private ScrollPattern? previousScroll;
        private double previousHorizontal, previousVertical;
        private string step = "target";
        public ConversationDropTarget Target => target;
        public bool ForegroundOwned => CodexConversationMenu.BelongsToWindow(Native.GetForegroundWindow(), window);
        public bool Valid => !cancellation.IsCancellationRequested && VerifyProcess(pid, desktop) && identity.SequenceEqual(row.GetRuntimeId()) &&
            (title is null || RowTitle(row) == title) && row.Current.IsEnabled && (!currentChat || CurrentSelectionMatches());
        private bool CurrentSelectionMatches()
        {
            var root = AutomationElement.FromHandle(window);
            var selected = SessionRows(root).Where(Selected).ToArray();
            if (Environment.TickCount64 - regionsCheckedAt > 200)
            { regionCount = VisibleChatRegions(root); regionsCheckedAt = Environment.TickCount64; }
            return selected.Length == 1 && identity.SequenceEqual(selected[0].GetRuntimeId()) && regionCount == 1;
        }
        private void Ensure(bool foreground = true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Valid || foreground && !ForegroundOwned) throw new InvalidOperationException("DSH目标、当前会话或前台已变化：" + step +
                $";process={VerifyProcess(pid, desktop)};identity={identity.SequenceEqual(row.GetRuntimeId())};title={title is null || RowTitle(row) == title};enabled={row.Current.IsEnabled};selection={!currentChat || CurrentSelectionMatches()};foreground={ForegroundOwned}");
        }
        public ConversationMenuCommand[] Commands()
        {
            var root = AutomationElement.FromHandle(window);
            var commands = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                .Cast<AutomationElement>().Where(x => x.Current.IsEnabled && !x.Current.IsOffscreen)
                .Select(x => new ConversationMenuCommand(x, x.Current.Name)).ToArray();
            if (opened && !capturedCommands && commands.Length > 0)
            { foreach (var command in commands) owned.Add(string.Join(",", ((AutomationElement)command.Handle).GetRuntimeId())); capturedCommands = true; }
            return commands;
        }
        public void Open()
        {
            Ensure(false);
            if (Native.GetForegroundWindow() != initial) throw new InvalidOperationException("前台或DSH目标已变化");
            CodexConversationMenu.Foreground(window);
            activated = ForegroundOwned;
            step = "foreground";
            Ensure();
            var depth = 0;
            for (var parent = TreeWalker.RawViewWalker.GetParent(row); parent is not null && depth++ < 15; parent = TreeWalker.RawViewWalker.GetParent(parent))
                if (parent.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern))
                { previousScroll = (ScrollPattern)pattern; previousHorizontal = previousScroll.Current.HorizontalScrollPercent; previousVertical = previousScroll.Current.VerticalScrollPercent; break; }
            Ensure();
            step = "scroll";
            if (VisibleBounds(row, window) != row.Current.BoundingRectangle && row.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
            Ensure();
            var layoutWait = Stopwatch.StartNew();
            while (VisibleBounds(row, window).IsEmpty && layoutWait.ElapsedMilliseconds < 600)
            { Thread.Sleep(20); Ensure(); }
            var visible = VisibleBounds(row, window);
            if (GetCursorPos(out var before))
            {
                if (visible.IsEmpty) throw new InvalidOperationException("DSH目标会话行不可见");
                var hover = new MutablePoint { X = (int)(visible.Left + visible.Width / 2), Y = (int)(visible.Top + visible.Height / 2) };
                // The pointer may already be here while WPF still held capture;
                // DSH has not necessarily received a hover after mouse release.
                if (hover.X == before.X && hover.Y == before.Y) hover.X = (int)Math.Min(visible.Right - 1, hover.X + 1);
                if (GetAncestor(WindowFromPoint(new NativePoint(hover.X, hover.Y)), 2) != window) throw new InvalidOperationException("DSH目标会话行未在可见窗口内");
                Ensure();
                previousPointer = before; ownedPointer = hover;
                if (!SetCursorPos(hover.X, hover.Y)) throw new InvalidOperationException("未能显示DSH会话操作按钮");
                step = "hover";
                Thread.Sleep(100); Ensure();
            }
            AutomationElement[] Buttons() => row.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>().Where(x => x.Current.Name.StartsWith("会话“", StringComparison.Ordinal) && x.Current.Name.EndsWith("”的操作", StringComparison.Ordinal)
                    || x.Current.Name.StartsWith("Session actions for ", StringComparison.Ordinal)).ToArray();
            var buttons = Buttons(); var hoverWait = Stopwatch.StartNew();
            while (buttons.Length == 0 && hoverWait.ElapsedMilliseconds < 600)
            { Thread.Sleep(30); Ensure(); buttons = Buttons(); }
            Ensure();
            if (buttons.Length != 1 || !buttons[0].TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) throw new InvalidOperationException("DSH会话操作按钮不可用");
            Ensure();
            opened = true; ((InvokePattern)invoke).Invoke();
        }
        public void Expand(ConversationMenuCommand command) => throw new InvalidOperationException("DSH不需要复制子菜单");
        public void Invoke(ConversationMenuCommand command)
        {
            var element = (AutomationElement)command.Handle;
            if (!Valid || !ForegroundOwned || !owned.Contains(string.Join(",", element.GetRuntimeId())) || element.Current.Name != command.Name ||
                command.Name is not ("复制 Session ID" or "Copy Session ID")) throw new InvalidOperationException("DSH菜单来源或动作已变化");
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
        public void CloseOwnedMenu()
        {
            if (!activated || cancellation.IsCancellationRequested || !ForegroundOwned) return;
            var commands = Commands();
            if (!opened && commands.Length > 0) return;
            if (commands.Any(x => !owned.Contains(string.Join(",", ((AutomationElement)x.Handle).GetRuntimeId())))) return;
            if (commands.Length > 0) { CodexConversationMenu.SendEscape(); Thread.Sleep(20); }
            if (!ForegroundOwned || Commands().Length != 0) return;
            RestorePointer();
            if (ForegroundOwned) CodexConversationMenu.Foreground(initial);
        }
        private void RestorePointer()
        {
            if (!cancellation.IsCancellationRequested && ForegroundOwned && previousPointer is { } before && ownedPointer is { } hover && GetCursorPos(out var current) && current.X == hover.X && current.Y == hover.Y)
                SetCursorPos(before.X, before.Y);
            previousPointer = ownedPointer = null;
            if (!cancellation.IsCancellationRequested && ForegroundOwned && previousScroll is { } scroll)
                try { scroll.SetScrollPercent(previousHorizontal, previousVertical); } catch (InvalidOperationException) { }
            previousScroll = null;
        }
        public void Dispose() => RestorePointer();
    }
    internal static bool VerifyProcess(uint pid, string desktop = "")
    {
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid); using var self = Process.GetCurrentProcess();
            if (process.SessionId != self.SessionId) return false;
            var image = process.MainModule?.FileName;
            if (image is null || !string.Equals(image, AiDesktopDiscovery.Find(AiPlatform.Dsh, desktop), StringComparison.OrdinalIgnoreCase)) return false;
            using var handle = OpenProcess(0x1000, false, pid);
            if (handle.IsInvalid || !OpenProcessToken(handle, 8, out var token)) return false;
            using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle())) using (var current = WindowsIdentity.GetCurrent())
                return identity.User is not null && current.User is not null && identity.User.Equals(current.User);
        }
        catch (Exception e) when (e is System.IO.IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct MutablePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out MutablePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
