using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record ConversationDropTarget(string? ThreadId, string Name, string Reason, bool CanResolveOnDrop = false,
    string Method = "explicit-link", bool? ClipboardRestored = null, AiPlatform Platform = AiPlatform.Codex, bool IsUnbind = false)
{
    internal bool CanBind => ThreadId is not null && !IsUnbind;
    internal static ConversationDropTarget Unavailable(string reason) => new(null, "", reason);
    internal static ConversationDropTarget Unbind(string name, string reason) => new(null, name, reason, Method: "local-binding-window-unlink", IsUnbind: true);
}

// Experimental source: read an explicit local thread URI from an accessible
// hyperlink in the authenticated desktop. No title matching, clipboard, input,
// navigation, renderer injection, chat content traversal or observer startup.
internal static class CodexConversationDropTarget
{
    internal static string? ParseThreadUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "codex" || uri.Host != "threads"
            || uri.UserInfo.Length != 0 || uri.Port != -1 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        if (uri.AbsolutePath.Length != 37 || uri.AbsolutePath[0] != '/') return null;
        var id = uri.AbsolutePath[1..];
        return Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : null;
    }
    internal static bool VerifyDesktop(uint processId)
    {
        if (processId == 0) return false;
        using var process = Process.GetProcessById((int)processId);
        using var self = Process.GetCurrentProcess();
        if (process.SessionId != self.SessionId) return false;
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return false;
        var family = new StringBuilder(256); uint count = (uint)family.Capacity;
        var packageError = GetPackageFamilyName(handle, ref count, family);
        if (packageError != 0 || family.ToString() != "OpenAI.Codex_2p2nqsd0c76g0")
        {
            if (packageError != 15700) return false;
            var image = process.MainModule?.FileName;
            if (image is null || !CodexInstallationDiscovery.RegisteredDesktops().Any(path =>
                string.Equals(path, image, StringComparison.OrdinalIgnoreCase) && CodexInstallationDiscovery.TrustedDesktop(path))) return false;
        }
        if (!OpenProcessToken(handle, 8, out var token)) return false;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var current = WindowsIdentity.GetCurrent())
            return identity.User is not null && current.User is not null && identity.User.Equals(current.User);
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
