using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Desktop 5.1231.2 stores its own ID separately from the engine's Hook ID.
// This catalog supplies names only; disk isRunning and timestamps are never
// execution evidence. Runtime state continues through the existing Hook chain.
internal static class ClaudeDesktopCatalog
{
    internal sealed record Entry(string DesktopId, string EngineId, string Title, long UpdatedAt);
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");

    internal static Entry[] Load(string? root = null)
    {
        if (root is null)
        {
            var roots = new List<string> { DefaultRoot };
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(packages))
                roots.AddRange(Directories(packages, "Claude_*")
                    .Select(x => Path.Combine(x, "LocalCache", "Roaming", "Claude")));
            return roots.SelectMany(x => Load(x)).GroupBy(x => x.EngineId, StringComparer.Ordinal)
                .Where(x => x.Select(e => e.DesktopId).Distinct().Count() == 1)
                .Select(x => x.OrderByDescending(e => e.UpdatedAt).First()).ToArray();
        }
        root = CodexLocalCatalog.LocalPath(root);
        var directory = Path.Combine(root, "claude-code-sessions");
        if (!Directory.Exists(directory) || LinkedDirectory(directory)) return [];
        var result = new List<Entry>();
        // userData / claude-code-sessions / account / organization / session.json
        // Do not recurse into waiting-input blobs, transcripts or other surfaces.
        var examined = 0;
        var limitReached = false;
        foreach (var account in Directories(directory))
        {
            if (limitReached) break;
            foreach (var organization in Directories(account))
            {
                if (limitReached) break;
                foreach (var file in Files(organization))
                {
                    if (++examined > 2000) { limitReached = true; break; }
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length is <= 0 or > 4 * 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
                        var row = json.RootElement;
                        var desktop = CodexState.Text(row, "sessionId");
                        var engine = CodexState.Text(row, "cliSessionId");
                        if (!desktop.StartsWith("local_", StringComparison.Ordinal) || AiState.NormalizeSessionId(AiPlatform.ClaudeCode, desktop) != desktop ||
                            desktop != Path.GetFileNameWithoutExtension(file) || !Guid.TryParse(engine, out var engineId) || engineId == Guid.Empty) continue;
                        engine = engineId.ToString("D");
                        // Remote executors cannot deliver local command Hooks. Keep them
                        // out of a catalog that promises locally observable sessions.
                        if (CodexState.Object(row, "sshConfig").ValueKind == JsonValueKind.Object ||
                            CodexState.Object(row, "wslConfig").ValueKind == JsonValueKind.Object ||
                            CodexState.Object(row, "movedToCloud").ValueKind is JsonValueKind.True or JsonValueKind.Object ||
                            CodexState.Object(row, "remoteTarget").ValueKind == JsonValueKind.Object ||
                            CodexState.Text(CodexState.Object(row, "backend"), "kind") is "ssh" or "remote" ||
                            CodexState.Object(row, "isArchived").ValueKind == JsonValueKind.True) continue;
                        var title = CodexState.Text(row, "title");
                        if (string.IsNullOrWhiteSpace(title)) title = "未命名 Claude 会话 · " + engine[..8];
                        if (title.Length > 512) title = title[..512];
                        result.Add(new(desktop, engine, title, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds()));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { }
                }
            }
        }
        // A reused engine ID mapped to distinct desktop rows is ambiguous.
        return Unique(result);
    }
    internal static string ResolveEngine(string session, Entry[] entries)
    {
        if (!session.StartsWith("local_", StringComparison.Ordinal)) return session;
        var matches = entries.Where(x => x.DesktopId == session).ToArray();
        if (matches.Length != 1) throw new IOException("Claude 桌面会话的执行身份暂不可确认，保留等待并重试。");
        return matches[0].EngineId;
    }
    internal static AiRawEvent MapEvent(AiRawEvent value, string desktopSession, string engineSession)
    {
        if (value.SessionId != engineSession) throw new IOException("Claude 事件记录与当前执行身份不符。");
        return value with { SessionId = desktopSession, Epoch = engineSession + ":" + value.Epoch };
    }
    private static Entry[] Unique(IEnumerable<Entry> values) => values.GroupBy(x => x.EngineId, StringComparer.Ordinal)
        .Where(x => x.Select(e => e.DesktopId).Distinct().Count() == 1)
        .Select(x => x.OrderByDescending(e => e.UpdatedAt).First()).ToArray();
    private static bool LinkedDirectory(string path)
    {
        try
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }
    private static string[] Directories(string root, string pattern = "*")
    {
        try { return Directory.GetDirectories(root, pattern).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).OrderBy(x => x, StringComparer.Ordinal).Take(128).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
    private static string[] Files(string root)
    {
        try { return Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(2001).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
