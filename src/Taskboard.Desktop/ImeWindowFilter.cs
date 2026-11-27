using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Taskboard.Desktop;

internal static class ImeWindowFilter
{
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private static readonly ConcurrentDictionary<uint, (string Name, long ExpiresAt)> processNames = new();

    internal static bool IsImeCandidateOrStatus(string? className, string? processName, long exStyle = 0, bool ownerInCurrentProcess = false)
    {
        var cls = (className ?? "").Trim();
        var proc = NormalizeProcessName(processName);

        if (cls.Length > 0)
        {
            if (cls.Equals("IME", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("CiceroUIWndFrame", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("CiceroUIWndFrame-TFM", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("TouchKeyboard", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("IPTip_Main_Window", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("MozcCandidateWindow", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("Microsoft.IME.", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("MSIME", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("IMJP", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("IMKR", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("CiceroUIWnd", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("Weasel", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("PIME", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("DoubaoInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("DBInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("DoubaoIme", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("WeType", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("WeChatInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("ZhipuInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("AutoTyper", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("SogouPY", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("SoPY_", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("SGTool", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("SGMyInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("TSF_Sogou", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("QQPY", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("QQPinyin", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("BaiduIME", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("BaiduPinyin", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("FlyIME", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("iFlyIME", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("iFlytekInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("Unispim", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("GooglePinyin", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("GoogleIMEJa", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("Mozc", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("ATOK", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("PalmInput", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("ShouXin", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("BingIME", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("CandidateWindow", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("ImeCandidate", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("Sogou_Comp", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("Sogou_Candidate", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("SogouStatus", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("SogouCloud", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("SogouIme", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("SogouTip", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("SogouSkin", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("QQ_Comp", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("QQ_Status", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("QQIme", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (proc.Length > 0)
        {
            if (proc.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("WindowsInternal.ComposableShell.Experiences.TextInput.InputApp", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("ChsIME", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("ChtIME", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("ctfmon", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("IMJPUEX", StringComparison.OrdinalIgnoreCase)
                || proc.Equals("IMKRMIG", StringComparison.OrdinalIgnoreCase)
                || proc.StartsWith("IMJP", StringComparison.OrdinalIgnoreCase)
                || proc.StartsWith("IMKR", StringComparison.OrdinalIgnoreCase))
            {
                if (cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)
                    || cls.Equals("InputSiteWindowClass", StringComparison.OrdinalIgnoreCase)
                    || cls.Equals("PopupHost", StringComparison.OrdinalIgnoreCase)
                    || cls.StartsWith("Microsoft.IME.", StringComparison.OrdinalIgnoreCase)
                    || cls.StartsWith("MSIME", StringComparison.OrdinalIgnoreCase)
                    || (exStyle & (WsExNoActivate | WsExToolWindow)) != 0
                    || cls.Length == 0)
                    return true;
            }

            // Doubao main process may host an auxiliary floating IME/helper bar
            // (WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW); never exempt the normal chat window.
            if (proc.Equals("Doubao", StringComparison.OrdinalIgnoreCase)
                && (exStyle & (WsExNoActivate | WsExToolWindow)) != 0)
                return true;

            if (IsThirdPartyImeProcess(proc))
                return true;
        }

        if (ownerInCurrentProcess && (exStyle & WsExNoActivate) != 0 && cls.Length > 0
            && !cls.StartsWith("HwndWrapper[", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsThirdPartyImeProcess(string proc) =>
        // Doubao IME
        proc.StartsWith("DoubaoInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("DBInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("DoubaoHelper", StringComparison.OrdinalIgnoreCase)
        // WeChat / WeType IME (never matches WeChat/Weixin main chat process)
        || proc.StartsWith("WeType", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("WeChatInput", StringComparison.OrdinalIgnoreCase)
        // Zhipu AI IME
        || proc.StartsWith("ZhipuInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("AutoTyper", StringComparison.OrdinalIgnoreCase)
        // iFlytek IME
        || proc.StartsWith("FlyIME", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("FlyDaemon", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("iFlyIME", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("iFlyInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("iFlytekInput", StringComparison.OrdinalIgnoreCase)
        // Sogou IME (never matches SogouExplorer)
        || proc.StartsWith("SogouPY", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SogouCloud", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SogouIme", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SogouPinyin", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SGTool", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SGMyInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("SGWebRender", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("SogouExe", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("SogouService", StringComparison.OrdinalIgnoreCase)
        // Baidu IME
        || proc.StartsWith("BaiduPinyin", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("BaiduIME", StringComparison.OrdinalIgnoreCase)
        // QQ IME (never matches QQ / QQBrowser)
        || proc.StartsWith("QQPY", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("QQPinyin", StringComparison.OrdinalIgnoreCase)
        // Palm / ShouXin IME
        || proc.StartsWith("PalmInput", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("ShouXin", StringComparison.OrdinalIgnoreCase)
        // Rime / Weasel & PIME & Unispim
        || proc.Equals("WeaselServer", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("WeaselDeployer", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("pime-server", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("PIMELauncher", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("Unispim", StringComparison.OrdinalIgnoreCase)
        // International: Google Pinyin / Google Japanese Input / Mozc / ATOK
        || proc.StartsWith("GooglePinyin", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("GoogleIMEJa", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("mozc_", StringComparison.OrdinalIgnoreCase)
        || proc.StartsWith("atok", StringComparison.OrdinalIgnoreCase);

    internal static bool IsImeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (InspectHandle(hwnd)) return true;
        var root = Native.GetAncestor(hwnd, 2);
        if (root != IntPtr.Zero && root != hwnd && InspectHandle(root)) return true;
        var current = root != IntPtr.Zero ? root : hwnd;
        for (var i = 0; i < 4; i++)
        {
            var owner = GetWindow(current, 4);
            if (owner == IntPtr.Zero) break;
            if (InspectHandle(owner)) return true;
            current = owner;
        }
        return false;
    }

    internal static bool IsCursorOverImeWindow()
    {
        if (!GetCursorPos(out var point)) return false;
        return IsImeWindow(WindowFromPoint(point));
    }

    internal static bool IsTaskSwitcherOverlay(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var root = Native.GetAncestor(hwnd, 2);
        var target = root != IntPtr.Zero ? root : hwnd;
        var cls = ReadClassName(target);
        if (cls.Equals("MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("TaskSwitcherWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("TaskSwitcherOverlayWnd", StringComparison.OrdinalIgnoreCase)
            || cls.Equals("ForegroundStaging", StringComparison.OrdinalIgnoreCase))
            return true;
        if (cls.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase))
        {
            GetWindowThreadProcessId(target, out var pid);
            var proc = ReadProcessName(pid);
            if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase) && (GetAsyncKeyState(0x12) & 0x8000) != 0)
                return true;
        }
        return false;
    }

    private static bool InspectHandle(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        var cls = ReadClassName(hwnd);
        if (pid == Environment.ProcessId)
            return cls.Equals("IME", StringComparison.OrdinalIgnoreCase)
                || cls.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("CiceroUIWnd", StringComparison.OrdinalIgnoreCase);

        var exStyle = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
        var proc = ReadProcessName(pid);
        var owner = GetWindow(hwnd, 4);
        var ownerInProcess = false;
        if (owner != IntPtr.Zero)
        {
            GetWindowThreadProcessId(owner, out var ownerPid);
            ownerInProcess = ownerPid == Environment.ProcessId;
        }
        return IsImeCandidateOrStatus(cls, proc, exStyle, ownerInProcess);
    }

    private static string NormalizeProcessName(string? name)
    {
        var value = (name ?? "").Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(128);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string ReadProcessName(uint pid)
    {
        if (pid == 0) return "";
        var now = Environment.TickCount64;
        if (processNames.TryGetValue(pid, out var cached) && now < cached.ExpiresAt)
            return cached.Name;
        var name = "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch { }
        if (processNames.Count > 256) processNames.Clear();
        processNames[pid] = (name, now + 5000);
        return name;
    }

    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
