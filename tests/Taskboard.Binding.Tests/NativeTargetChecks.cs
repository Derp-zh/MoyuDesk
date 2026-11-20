using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Taskboard.Desktop;
using U = Interop.UIAutomationClient;

internal static class NativeTargetChecks
{
    internal static async Task<int> Run(string output)
    {
        SetProcessDpiAwarenessContext(new nint(-4));
        var beforeForeground = GetForegroundWindow(); var beforeSequence = GetClipboardSequenceNumber();
        var windows = new List<(nint Window, uint Pid)>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var pid);
            try { if (CodexConversationDropTarget.VerifyDesktop(pid)) windows.Add((window, pid)); }
            catch (Exception) { }
            return true;
        }, 0);
        var api = (U.IUIAutomation)new U.CUIAutomation8(); var candidates = new List<(Point Point, string Name)>();
        foreach (var window in windows)
        {
            var items = api.ElementFromHandle(window.Window).FindAll((U.TreeScope)4, api.CreatePropertyCondition(30003, 50000));
            for (var i = 0; i < items.Length && candidates.Count < 2; i++)
            {
                var element = items.GetElement(i);
                if (element.CurrentIsOffscreen != 0 || !element.CurrentClassName.Contains("app-action-sidebar-thread-selected", StringComparison.Ordinal)) continue;
                var rect = element.CurrentBoundingRectangle;
                var point = new NativePoint { X = (rect.left + rect.right) / 2, Y = (rect.top + rect.bottom) / 2 };
                if (GetAncestor(WindowFromPoint(point), 2) != window.Window) continue;
                candidates.Add((new Point(point.X, point.Y), element.CurrentName));
            }
        }
        var checks = new List<object>();
        foreach (var candidate in candidates)
        {
            var target = await CodexConversationMenu.Read(candidate.Point).WaitAsync(TimeSpan.FromSeconds(4));
            checks.Add(new { resolveOnDrop = target.CanResolveOnDrop, nameMatches = target.Name == candidate.Name,
                nameHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Name))), method = target.Method });
            if (!target.CanResolveOnDrop || target.Name != candidate.Name) throw new InvalidOperationException("Native read-only target lookup failed: " + target.Reason);
        }
        var report = new { passed = checks.Count, checks, foregroundUnchanged = GetForegroundWindow() == beforeForeground,
            clipboardSequenceUnchanged = GetClipboardSequenceNumber() == beforeSequence, inputSent = false, menusOpened = false,
            scope = "Actual production UIA3 release-target lookup on existing Codex sidebar rows; no menu, clipboard content or physical drag" };
        File.WriteAllText(Path.Combine(output, "native-target-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return candidates.Count == 2 ? 0 : 2;
    }
    private delegate bool WindowCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
