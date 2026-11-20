using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using U = Interop.UIAutomationClient;

namespace Taskboard.Desktop;

// Native UIA3 only in this drag path; no FlaUI dependency or physical right-click.
// All elements stay on ConversationAutomationWorker's dedicated MTA.
internal static class CodexConversationMenu
{
    internal static string LastStep { get; private set; } = "not-started";
    internal static bool IsDeeplinkAction(string name) => new[] { "复制深度链接", "复制深层链接", "复制应用链接", "Copy deeplink", "Copy deep link" }
        .Any(prefix => name == prefix || name.StartsWith(prefix + " ", StringComparison.Ordinal) || name.StartsWith(prefix + "\t", StringComparison.Ordinal));
    private static readonly Lazy<U.IUIAutomation> automation = new(() => new U.CUIAutomation8());
    private static U.IUIAutomation Api => automation.Value;
    private const int NameProperty = 30005, ClassProperty = 30012, TypeProperty = 30003,
        OffscreenProperty = 30022, EnabledProperty = 30010, ProcessProperty = 30002;
    private sealed record ElementInfo(U.IUIAutomationElement Element, string Name, string Class, int Type, bool Visible, Rect Bounds);
    private sealed record Anchor(IntPtr Window, uint ProcessId, ElementInfo Element, string Name, bool Sidebar, int[] Runtime);
    private sealed record CommandHandle(U.IUIAutomationElement Element, int[] Runtime);
    private static Rect Bounds(U.IUIAutomationElement element)
    {
        var bounds = element.CurrentBoundingRectangle;
        return new Rect(bounds.left, bounds.top, Math.Max(0, bounds.right - bounds.left), Math.Max(0, bounds.bottom - bounds.top));
    }
    private static U.IUIAutomationCacheRequest Cache()
    {
        var cache = Api.CreateCacheRequest();
        foreach (var property in new[] { NameProperty, ClassProperty, TypeProperty, OffscreenProperty, EnabledProperty, ProcessProperty, 30011 })
            cache.AddProperty(property);
        return cache;
    }
    private static ElementInfo Info(U.IUIAutomationElement element, bool cached = false)
    {
        var copy = cached ? element : element.BuildUpdatedCache(Cache());
        var bounds = Bounds(copy);
        return new(copy, (string)copy.GetCachedPropertyValue(NameProperty), (string)copy.GetCachedPropertyValue(ClassProperty),
            Convert.ToInt32(copy.GetCachedPropertyValue(TypeProperty)), !(bool)copy.GetCachedPropertyValue(OffscreenProperty)
                && (bool)copy.GetCachedPropertyValue(EnabledProperty) && bounds.Width > 1 && bounds.Height > 1, bounds);
    }
    private static bool ThreadRow(ElementInfo info) => info.Type == 50000 && info.Visible
        && info.Class.Contains("sidebar-item", StringComparison.Ordinal)
        && !info.Class.Contains("group/folder-row", StringComparison.Ordinal)
        && (info.Class.Contains("app-action-sidebar-thread-selected", StringComparison.Ordinal)
            || info.Element.GetCurrentPattern(10000) is U.IUIAutomationInvokePattern);
    private static ElementInfo[] Buttons(U.IUIAutomationElement root)
    {
        var items = root.FindAllBuildCache((U.TreeScope)4, Api.CreatePropertyCondition(TypeProperty, 50000), Cache());
        var result = new List<ElementInfo>();
        for (var i = 0; i < items.Length; i++) { var item = Info(items.GetElement(i), true); if (item.Visible) result.Add(item); }
        return result.ToArray();
    }
    private static (ConversationDropTarget Target, Anchor? Anchor) Locate(Point point)
    {
        LastStep = "locate";
        var window = RootAt(point); GetWindowThreadProcessId(window, out var pid);
        if (!CodexConversationDropTarget.VerifyDesktop(pid))
            return (ConversationDropTarget.Unavailable("请拖到 Codex 会话上"), null);
        var hit = Api.ElementFromPoint(new U.tagPOINT { x = (int)point.X, y = (int)point.Y });
        var ancestors = new List<ElementInfo>(); ElementInfo? document = null;
        for (var current = hit; current is not null && ancestors.Count < 30; current = Api.ControlViewWalker.GetParentElement(current))
        {
            var info = Info(current); ancestors.Add(info);
            if ((uint)current.CurrentProcessId != pid) break;
            if (info.Type == 50005 && info.Bounds.Contains(point) && current.GetCurrentPattern(10002) is U.IUIAutomationValuePattern value
                && CodexConversationDropTarget.ParseThreadUri(value.CurrentValue) is { } id && RootAt(point) == window)
                return (new(id, string.IsNullOrWhiteSpace(info.Name) ? "未命名会话" : info.Name, "已取得明确会话编号"), null);
            if (ThreadRow(info)) return Found(info, info.Name, true);
            if (info.Type == 50030) document = info;
            if (info.Type == 50032) break;
        }
        if (document is null) return (ConversationDropTarget.Unavailable("请拖到 Codex 侧栏会话或当前聊天区域"), null);
        var buttons = Buttons(Api.ElementFromHandle(window));
        var headers = buttons.Where(item => item.Name is "聊天操作" or "Chat actions" or "Thread actions").ToArray();
        var rows = buttons.Where(item => item.Class.Contains("sidebar-item", StringComparison.Ordinal)).ToArray();
        // A folder/project/blank sidebar target never falls back to the open chat.
        if (rows.Length > 0 && point.X < rows.Max(item => item.Bounds.Right)) return (ConversationDropTarget.Unavailable("侧栏空白或项目不是会话"), null);
        foreach (var ancestor in ancestors)
        {
            if (!ancestor.Bounds.Contains(point) || ancestor.Bounds.Width < 300 || ancestor.Bounds.Height < 100) continue;
            var scoped = headers.Where(item => ancestor.Bounds.Contains(item.Bounds)).ToArray();
            if (scoped.Length == 1) return Found(scoped[0], document.Name, false);
        }
        return (ConversationDropTarget.Unavailable("无法确定所属聊天区域"), null);

        (ConversationDropTarget, Anchor?) Found(ElementInfo element, string name, bool sidebar)
        {
            if (RootAt(point) != window) return (ConversationDropTarget.Unavailable("落点窗口已变化"), null);
            name = string.IsNullOrWhiteSpace(name) ? "未命名会话" : name;
            return (new(null, name, "松开绑定 · " + name, true, "codex-element-menu"),
                new(window, pid, element, name, sidebar, element.Element.GetRuntimeId()));
        }
    }
    internal static Task<ConversationDropTarget> Read(Point point) => ConversationAutomationWorker.Run(() => Locate(point).Target);
    internal static Task<ConversationDropTarget> Resolve(Point point, CancellationToken cancellation = default) =>
        ConversationAutomationWorker.Run(() =>
        {
            var initialForeground = Native.GetForegroundWindow();
            var located = Locate(point); cancellation.ThrowIfCancellationRequested();
            if (located.Anchor is null || located.Target.CanBind) return located.Target;
            using var menu = new NativeMenu(located.Anchor, cancellation, initialForeground);
            using var clipboard = new NativeClipboard(located.Anchor.ProcessId);
            LastStep = "element-menu";
            var result = ConversationLinkResolver.Run(menu, clipboard, cancellation);
            LastStep = result.CanBind ? "copied" : "cancelled-or-unavailable";
            return result;
        }, cancellation);

    private sealed class NativeMenu(Anchor anchor, CancellationToken cancellation, IntPtr initialForeground) : IConversationLinkMenu
    {
        private bool opened, acceptCommands, awaitingLinkSubmenu;
        private readonly HashSet<string> ownedCommands = [];
        public ConversationDropTarget Target => new(null, anchor.Name, "", true, "codex-element-menu");
        public bool ForegroundOwned => BelongsToWindow(Native.GetForegroundWindow(), anchor.Window);
        public bool Valid => !cancellation.IsCancellationRequested && IsWindow(anchor.Window)
            && WindowPid(anchor.Window) == anchor.ProcessId && anchor.Runtime.SequenceEqual(anchor.Element.Element.GetRuntimeId())
            && anchor.Element.Element.CurrentName == anchor.Element.Name && anchor.Element.Element.CurrentIsEnabled != 0
            && (uint)anchor.Element.Element.CurrentProcessId == anchor.ProcessId;
        public ConversationMenuCommand[] Commands()
        {
            var windows = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                if (IsWindowVisible(window) && WindowPid(window) == anchor.ProcessId && BelongsToWindow(window, anchor.Window)) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            var result = new List<ConversationMenuCommand>(); var seen = new HashSet<string>();
            var condition = Api.CreateAndCondition(Api.CreatePropertyCondition(TypeProperty, 50011), Api.CreatePropertyCondition(OffscreenProperty, false));
            foreach (var window in windows)
            {
                try
                {
                    var items = Api.ElementFromHandle(window).FindAllBuildCache((U.TreeScope)4, condition, Cache());
                    for (var i = 0; i < items.Length; i++)
                    {
                        var item = items.GetElement(i);
                        if (!(bool)item.GetCachedPropertyValue(EnabledProperty)
                            || ((string)item.GetCachedPropertyValue(30011)).StartsWith("application-menu-trigger-", StringComparison.Ordinal)) continue;
                        var runtime = item.GetRuntimeId(); var key = string.Join(",", runtime);
                        if (seen.Add(key)) result.Add(new(new CommandHandle(item, runtime), (string)item.GetCachedPropertyValue(NameProperty)));
                    }
                }
                catch (COMException) { /* A separate popup can disappear while scanning. */ }
            }
            // Expanding Copy can first return the unchanged parent menu.
            // Retain the pending capture until the actual link submenu arrives.
            if (acceptCommands && result.Count > 0 && (!awaitingLinkSubmenu || result.Any(item => IsDeeplinkAction(item.Name))))
            {
                foreach (var item in result) ownedCommands.Add(Key(item));
                acceptCommands = false;
                awaitingLinkSubmenu = false;
            }
            return result.ToArray();
        }
        public void Open()
        {
            var currentForeground = Native.GetForegroundWindow();
            if (!Valid || currentForeground != initialForeground && !BelongsToWindow(currentForeground, anchor.Window))
                throw new InvalidOperationException("前台已变化");
            Foreground(anchor.Window);
            for (var wait = 0; wait < 250 && !ForegroundOwned && !cancellation.IsCancellationRequested; wait += 15)
                Thread.Sleep(15);
            if (!Valid || !ForegroundOwned) throw new InvalidOperationException("无法取得目标前台");
            opened = acceptCommands = true;
            if (anchor.Sidebar) ((U.IUIAutomationElement3)anchor.Element.Element).ShowContextMenu();
            else
            {
                var pattern = anchor.Element.Element.GetCurrentPattern(10005);
                if (pattern is U.IUIAutomationExpandCollapsePattern expand) expand.Expand();
                else if (anchor.Element.Element.GetCurrentPattern(10000) is U.IUIAutomationInvokePattern invoke) invoke.Invoke();
                else throw new InvalidOperationException("聊天操作菜单不支持打开");
            }
        }
        private CommandHandle Checked(ConversationMenuCommand command)
        {
            var handle = (CommandHandle)command.Handle;
            if (!Valid || !ForegroundOwned || !ownedCommands.Contains(Key(command))
                || !handle.Runtime.SequenceEqual(handle.Element.GetRuntimeId()) || handle.Element.CurrentName != command.Name)
                throw new InvalidOperationException($"菜单来源已变化;target={Valid};foreground={ForegroundOwned};owned={ownedCommands.Contains(Key(command))};runtime={handle.Runtime.SequenceEqual(handle.Element.GetRuntimeId())};name={handle.Element.CurrentName == command.Name}");
            return handle;
        }
        public void Expand(ConversationMenuCommand command)
        {
            var handle = Checked(command); acceptCommands = awaitingLinkSubmenu = true;
            ((U.IUIAutomationExpandCollapsePattern)handle.Element.GetCurrentPattern(10005)).Expand();
        }
        public void Invoke(ConversationMenuCommand command) =>
            ((U.IUIAutomationInvokePattern)Checked(command).Element.GetCurrentPattern(10000)).Invoke();
        private static string Key(ConversationMenuCommand command) => string.Join(",", ((CommandHandle)command.Handle).Runtime);
        public void CloseOwnedMenu()
        {
            if (cancellation.IsCancellationRequested || !opened || !ForegroundOwned) return;
            for (var i = 0; i < 2; i++)
            {
                var commands = Commands();
                if (commands.Length == 0) break;
                if (!commands.All(item => ownedCommands.Contains(Key(item))) || !ForegroundOwned) return;
                SendEscape(); Thread.Sleep(20);
            }
            if (!cancellation.IsCancellationRequested && ForegroundOwned && Commands().Length == 0 && IsWindow(initialForeground))
                Foreground(initialForeground);
        }
        public void Dispose() { }
    }
    internal sealed class NativeClipboard(uint pid) : IConversationLinkClipboard
    {
        private ConversationClipboard? previous;
        public uint Sequence => GetClipboardSequenceNumber();
        public uint Capture() => ConversationClipboardThread.Run(() => { previous = new ConversationClipboard(); return previous.Sequence; });
        public ConversationClipboardRead Read() => ConversationClipboardThread.Run(() =>
        {
            var sequence = GetClipboardSequenceNumber(); var owner = GetClipboardOwner(); var ownerPid = WindowPid(owner);
            var text = System.Windows.Clipboard.GetText();
            return new ConversationClipboardRead(sequence, text, sequence == GetClipboardSequenceNumber() && (ownerPid == 0 || ownerPid == pid));
        });
        public bool Restore(uint sequence) => ConversationClipboardThread.Run(() => previous?.Restore(sequence) == true);
        public void Dispose() => ConversationClipboardThread.Run(() => { previous?.Dispose(); return true; });
    }
    private static uint WindowPid(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid; }
    internal static bool BelongsToWindow(IntPtr window, IntPtr target)
    {
        for (var i = 0; window != IntPtr.Zero && i < 8; i++, window = GetWindow(GetAncestor(window, 2), 4))
            if (GetAncestor(window, 2) == target) return true;
        return false;
    }
    private static IntPtr RootAt(Point point) => GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2);
    internal static void Foreground(IntPtr window)
    {
        var other = GetWindowThreadProcessId(Native.GetForegroundWindow(), out _); var self = GetCurrentThreadId();
        var attached = other != self && AttachThreadInput(self, other, true);
        try { BringWindowToTop(window); SetForegroundWindow(window); }
        finally { if (attached) AttachThreadInput(self, other, false); }
    }
    internal static void SendEscape()
    {
        var inputs = new[] { new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 27 } } },
            new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 27, Flags = 2 } } } };
        SendInput(2, inputs, Marshal.SizeOf<Input>());
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
}
