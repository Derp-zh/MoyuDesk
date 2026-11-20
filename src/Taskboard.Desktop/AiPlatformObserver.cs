using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class AiPlatformObserver : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly object workerGate = new();
    private Task? worker;
    internal bool IsRunning { get { lock (workerGate) return worker is { IsCompleted: false } && !lifetime.IsCancellationRequested; } }
    private readonly string eventsRoot, dshHome, claudeHome;
    private readonly string? claudeDesktopRoot;
    private readonly Dictionary<string, string> claudeEngines = new(StringComparer.Ordinal);
    private readonly string? node;
    private readonly AiPlatform? onlyPlatform;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AiObservation> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<(AiPlatform, string), (AiEventState State, long Offset)> states = new();
    private readonly Dictionary<string, (long Sequence, long? Turn, string Identity)> dshCursors = new(StringComparer.Ordinal);
    private readonly string observerEpoch = Guid.NewGuid().ToString("N");
    private long publicationSequence;
    private readonly HashSet<(AiPlatform, string)> failed = new();
    private readonly HashSet<(AiPlatform, string)> lagging = new();
    private readonly HashSet<Guid> delivered = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> watchSince = new();
    private (Guid Task, Guid Stage, Guid Binding, int Generation, AiPlatform Platform, string Session)[] watches = [];
    internal event Action<Guid, Guid, Guid, int, AiObservation>? Changed;
    internal event Action? StatusChanged;
    internal string Status { get; private set; } = "尚未开始";
    private void SetStatus(string value) { if (Status == value) return; Status = value; StatusChanged?.Invoke(); }
    internal AiPlatformObserver(string? eventsRoot = null, string? dshHome = null, AiPlatform? onlyPlatform = null, string? node = null, string? claudeHome = null, string? claudeDesktopRoot = null)
    {
        this.eventsRoot = CodexLocalCatalog.LocalPath(eventsRoot ?? AiPaths.Events); this.dshHome = CodexLocalCatalog.LocalPath(dshHome ?? AiPaths.DshHome);
        this.claudeDesktopRoot = claudeDesktopRoot;
        this.claudeHome = CodexLocalCatalog.LocalPath(claudeHome ?? AiPaths.ClaudeHome); this.onlyPlatform = onlyPlatform; this.node = node;
    }
    internal AiObservation? Peek(string session) => snapshots.GetValueOrDefault(session);
    internal void Watch(IEnumerable<WorkItem> tasks)
    {
        var next = tasks.SelectMany(t => t.Stages.Where(s => s.Codex is { Platform: not AiPlatform.Codex } binding && (onlyPlatform is null || binding.Platform == onlyPlatform))
            .Select(s => (Task: t.Id, Stage: s.Id, Binding: s.Codex!.BindingId, Generation: s.Codex.Generation, Platform: s.Codex.Platform, Session: s.Codex.ThreadId))).ToArray();
        foreach (var watch in next) watchSince.TryAdd(watch.Binding, DateTime.UtcNow);
        var activeBindings = next.Select(x => x.Binding).ToHashSet();
        foreach (var old in watchSince.Keys.Where(x => !activeBindings.Contains(x)).ToArray()) watchSince.TryRemove(old, out _);
        Volatile.Write(ref watches, next);
    }
    internal void Start()
    {
        lock (workerGate)
            if (!lifetime.IsCancellationRequested && (worker is null || worker.IsCompleted)) worker = Task.Run(Run);
    }
    private async Task Run()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var current = Volatile.Read(ref watches);
                var desktopEntries = current.Any(x => x.Platform == AiPlatform.ClaudeCode && x.Session.StartsWith("local_", StringComparison.Ordinal))
                    ? ClaudeDesktopCatalog.Load(claudeDesktopRoot) : [];
                var wanted = current.Select(x => (x.Platform, x.Session)).ToHashSet();
                foreach (var key in states.Keys.Where(x => !wanted.Contains(x)).ToArray())
                { states.Remove(key); failed.Remove(key); lagging.Remove(key); if (key.Item1 == AiPlatform.Dsh) dshCursors.Remove(key.Item2); }
                foreach (var group in current.GroupBy(x => (x.Platform, x.Session)))
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    try
                    {
                    if (!states.TryGetValue(group.Key, out var entry)) entry = (new(group.Key.Platform, group.Key.Session), 0);
                    var before = entry.State.Snapshot.Sequence;
                    var caughtUp = true;
                    if (group.Key.Platform == AiPlatform.ClaudeCode)
                    {
                        var engine = ClaudeDesktopCatalog.ResolveEngine(group.Key.Session, desktopEntries);
                        if (claudeEngines.TryGetValue(group.Key.Session, out var priorEngine) && priorEngine != engine)
                            entry = (new(group.Key.Platform, group.Key.Session), 0);
                        claudeEngines[group.Key.Session] = engine;
                        var path = Path.Combine(eventsRoot, "claude", AiPaths.FileName(engine) + ".jsonl");
                        if (File.Exists(path))
                        {
                            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (f.Length < entry.Offset) entry = (new(group.Key.Platform, group.Key.Session), 0);
                            f.Position = entry.Offset;
                            var bytes = new byte[(int)Math.Min(f.Length - f.Position, 1024 * 1024)]; var size = await f.ReadAsync(bytes, lifetime.Token).ConfigureAwait(false);
                            var last = size == 0 ? -1 : Array.LastIndexOf(bytes, (byte)'\n', size - 1, size);
                            if (last >= 0)
                            {
                                foreach (var line in Encoding.UTF8.GetString(bytes, 0, last + 1).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                                { var e = JsonSerializer.Deserialize<AiRawEvent>(line); if (e is not null) entry.State.Apply(ClaudeDesktopCatalog.MapEvent(e, group.Key.Session, engine)); }
                                entry.Offset += last + 1;
                            }
                            caughtUp = entry.Offset == f.Length;
                        }
                    }
                    else
                    {
                        var cursor = dshCursors.GetValueOrDefault(group.Key.Session, (-1L, null, ""));
                        var args = new List<string> { "--events", dshHome, group.Key.Session, "--offset", entry.Offset.ToString(), "--after-seq", cursor.Sequence.ToString() };
                        if (cursor.Turn is { } turn) { args.Add("--turn"); args.Add(turn.ToString()); }
                        var output = await DshProcess.Run(args.ToArray(), lifetime.Token, node).ConfigureAwait(false);
                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        using var batchDocument = JsonDocument.Parse(lines.Last()); var batch = batchDocument.RootElement;
                        var identity = batch.GetProperty("fileIdentity").GetString() + ":" + batch.GetProperty("createdAt").GetInt64();
                        if (cursor.Identity.Length > 0 && identity != cursor.Identity) throw new IOException("DSH 会话日志代次变化，保留等待并重新确认。");
                        foreach (var line in lines.Take(lines.Length - 1))
                        {
                            using var doc = JsonDocument.Parse(line);
                            var e = DshEvent(doc.RootElement, identity); if (e is not null) entry.State.Apply(e);
                        }
                        entry.Offset = batch.GetProperty("nextOffset").GetInt64();
                        caughtUp = !batch.GetProperty("hasMore").GetBoolean();
                        dshCursors[group.Key.Session] = (batch.GetProperty("lastSequence").ValueKind == JsonValueKind.Number ? batch.GetProperty("lastSequence").GetInt64() : cursor.Sequence,
                            batch.GetProperty("currentTurn").ValueKind == JsonValueKind.Number ? batch.GetProperty("currentTurn").GetInt64() : null, identity);
                    }
                    states[group.Key] = entry;
                    var recovered = failed.Remove(group.Key);
                    var catchupChanged = caughtUp ? lagging.Remove(group.Key) : lagging.Add(group.Key);
                    if (entry.State.Snapshot.Sequence != before || recovered || catchupChanged || group.Any(x => !delivered.Contains(x.Binding)))
                    {
                        var snapshot = entry.State.Snapshot;
                        var publication = Interlocked.Increment(ref publicationSequence);
                        snapshot = snapshot with { Epoch = observerEpoch + ":" + snapshot.Epoch, Sequence = publication };
                        foreach (var watch in group)
                        {
                            var value = snapshot;
                            if (!caughtUp || snapshot.Phase is CodexPhase.ReadyForReview or CodexPhase.Failed or CodexPhase.Interrupted && entry.State.LastEventUtc < watchSince.GetValueOrDefault(watch.Binding, DateTime.MaxValue))
                                value = value with { Phase = CodexPhase.Unknown, Complete = false, Executing = false };
                            snapshots[group.Key.Session] = value;
                            delivered.Add(watch.Binding); Changed?.Invoke(watch.Task, watch.Stage, watch.Binding, watch.Generation, value);
                        }
                    }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
                    {
                        SetStatus(AiState.Name(group.Key.Platform) + "：" + error.Message);
                        if (group.Key.Platform == AiPlatform.Dsh) { states.Remove(group.Key); dshCursors.Remove(group.Key.Session); }
                        if (failed.Add(group.Key))
                        {
                            var publication = Interlocked.Increment(ref publicationSequence);
                            var unknown = new AiObservation(group.Key.Platform, group.Key.Session, observerEpoch, publication, "", default, CodexPhase.Unknown, [], false, false, DateTime.UtcNow);
                            snapshots[group.Key.Session] = unknown;
                            foreach (var watch in group) Changed?.Invoke(watch.Task, watch.Stage, watch.Binding, watch.Generation, unknown);
                        }
                    }
                }
                if (failed.Count == 0) SetStatus(current.Length == 0 ? "等待绑定" + (onlyPlatform is { } platform ? AiState.Name(platform) : " DSH / Claude Code") + "会话" : "正在读取本机会话状态");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { SetStatus("本机状态暂不可用：" + e.Message); }
            try { await Task.Delay(750, lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }
    internal static AiRawEvent? DshEvent(JsonElement row, string epoch)
    {
        var type = CodexState.Text(row, "type"); var data = CodexState.Object(row, "data");
        var kind = type; var id = CodexState.Text(data, "id"); var name = CodexState.Text(data, "name"); var outcome = "";
        switch (type)
        {
            case "turn/start": break;
            case "turn/end":
                if (CodexState.Text(data, "reason") == "forked") return null;
                kind = CodexState.Text(data, "reason") switch
                { "completed" => "turn/completed", "aborted" or "interrupted" => "turn/interrupted", "error" or "max-tokens" or "blocked" => "turn/failed", _ => "unavailable" }; break;
            case "approval/asked": case "approval/decided": break;
            case "plan/mode": outcome = CodexState.Object(data, "active").ValueKind == JsonValueKind.True ? "active" : "inactive"; break;
            case "tool/call": case "tool/ptc-dispatch-start":
                kind = "question/asked"; id = CodexState.Text(data, "callId"); outcome = name == "exit_plan_mode" ? "approval" : ""; break;
            case "tool/result":
                if (CodexState.Object(data, "pending").ValueKind == JsonValueKind.True || CodexState.Text(data, "errorCode") == "TOOL_OUTCOME_UNKNOWN") return null;
                kind = "question/answered"; id = CodexState.Text(data, "callId"); break;
            case "tool/ptc-dispatch":
                id = CodexState.Text(data, "callId");
                kind = CodexState.Object(data, "pending").ValueKind == JsonValueKind.True || CodexState.Text(data, "errorCode") == "TOOL_OUTCOME_UNKNOWN" ? "question/asked" : "question/answered";
                outcome = name == "exit_plan_mode" ? "approval" : ""; break;
            case "user/message": if (CodexState.Text(data, "sourceKind") != "user-question-reply") return null;
                kind = "question/answered"; id = CodexState.Text(data, "callId"); break;
            case "assistant/message": kind = "execution"; break;
            default: return null;
        }
        var turn = CodexState.Object(row, "turn");
        return new(CodexState.Text(row, "sessionId"), epoch, row.GetProperty("seq").GetInt64(),
            DateTimeOffset.FromUnixTimeMilliseconds(row.GetProperty("time").GetInt64()).UtcDateTime, kind,
            turn.ValueKind == JsonValueKind.Number ? turn.GetInt64().ToString() : "", id, name, "", outcome);
    }
    internal async Task<CodexThread[]> Catalog(AiPlatform platform)
    {
        if (platform == AiPlatform.Dsh)
        {
            using var doc = JsonDocument.Parse(await DshProcess.Run(["--catalog", dshHome], lifetime.Token, node).ConfigureAwait(false));
            return doc.RootElement.EnumerateArray().Select(x => new CodexThread(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString()!, x.GetProperty("updatedAt").GetInt64(), platform)).ToArray();
        }
        return await Task.Run(() => ClaudeCatalog.Load(claudeHome, eventsRoot, claudeDesktopRoot), lifetime.Token).ConfigureAwait(false);
    }
    public void Dispose() => lifetime.Cancel();
}

internal static class DshProcess
{
    private static readonly ConcurrentDictionary<string, (long Length, long WrittenUtc)> supportedNodeVersions = new(StringComparer.OrdinalIgnoreCase);
    internal static IEnumerable<string> NodeCandidates(string? desktop = null, string? searchPath = null)
    {
        // DSH already ships the runtime needed by the reader. Explorer may
        // retain an old PATH for hours, and PATH can start with an older Node.
        var candidates = new List<string>();
        desktop ??= AiDesktopDiscovery.Find(AiPlatform.Dsh, "");
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            var directory = Path.GetDirectoryName(desktop)!;
            var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            candidates.Add(Path.Combine(directory, "resources", "runtime", architecture, "primary-runtime", "dependencies", "node", "bin", "node.exe"));
            candidates.Add(Path.Combine(directory, "resources", "runtime", "primary-runtime", "dependencies", "node", "bin", "node.exe"));
        }
        candidates.Add(Path.Combine(AiPaths.DshHome, "dsh-runtimes", "dsh-plugin-desktop-primary-runtime", "dependencies", "node", "bin", "node.exe"));
        var paths = searchPath is not null ? new[] { searchPath } : new[]
        {
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? ""
        };
        foreach (var value in paths)
            foreach (var dir in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'));
                    if (Path.IsPathFullyQualified(expanded)) candidates.Add(Path.Combine(expanded, "node.exe"));
                }
                catch (ArgumentException) { }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }
    internal static bool SupportedNode(string? path)
    {
        string? local = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetFileName(path), "node.exe", StringComparison.OrdinalIgnoreCase)) return false;
            local = CodexLocalCatalog.LocalPath(path);
            // A cached version never bypasses the local-path, current-file and
            // PE-header checks. Only repeated version-resource parsing is saved.
            if (!AiDesktopDiscovery.Executable(local)) { supportedNodeVersions.TryRemove(local, out _); return false; }
            var file = new FileInfo(local);
            var stamp = (file.Length, file.LastWriteTimeUtc.Ticks);
            if (supportedNodeVersions.TryGetValue(local, out var validated) && stamp == validated) return true;
            supportedNodeVersions.TryRemove(local, out _);
            if (FileVersionInfo.GetVersionInfo(local).FileMajorPart < 24) return false;
            file.Refresh();
            if (!file.Exists || stamp != (file.Length, file.LastWriteTimeUtc.Ticks)) return false;
            supportedNodeVersions[local] = stamp;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { if (local is not null) supportedNodeVersions.TryRemove(local, out _); return false; }
    }
    internal static string? NodePath(string? desktop = null, string? searchPath = null) => NodeCandidates(desktop, searchPath).FirstOrDefault(SupportedNode);
    internal static async Task<(string? Path, string Diagnostic)> FindNode(string manual, string desktop, CancellationToken cancellation)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "integrations", "dsh-observer.mjs"))) throw new IOException("缺少 DSH 本机读取组件，请使用完整便携包解压后运行。");
        var rejected = new List<string>();
        foreach (var candidate in (string.IsNullOrWhiteSpace(manual) ? Array.Empty<string>() : new[] { manual }).Concat(NodeCandidates(desktop)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!SupportedNode(candidate)) { if (candidate == manual) rejected.Add("原读取运行时已失效或版本低于24，已重新发现本机运行时。"); continue; }
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                using var probe = JsonDocument.Parse(await Run(["--self-check"], attempt.Token, candidate).ConfigureAwait(false));
                if (CodexState.Object(probe.RootElement, "nodeMajor").ValueKind == JsonValueKind.Number && CodexState.Object(probe.RootElement, "nodeMajor").TryGetInt32(out var major) && major >= 24 &&
                    CodexState.Object(probe.RootElement, "zstd").ValueKind == JsonValueKind.True)
                    return (candidate, string.Join("\n", rejected));
                rejected.Add("一个本机读取运行时未通过压缩日志检查，已尝试其他已安装运行时。");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or JsonException)
            { rejected.Add("一个本机读取运行时无法执行读取组件，已尝试其他已安装运行时。"); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { rejected.Add("一个本机读取运行时检查超时，已尝试其他已安装运行时。"); }
        }
        return (null, string.Join("\n", rejected));
    }
    internal static async Task<string> Run(string[] arguments, CancellationToken cancellation, string? executable = null)
    {
        var node = SupportedNode(executable) ? executable : NodePath();
        if (node is null) throw new IOException("DSH 本机日志读取需要 Node.js 24，尚未找到 node.exe。");
        var script = Path.Combine(AppContext.BaseDirectory, "integrations", "dsh-observer.mjs");
        if (!File.Exists(script)) throw new IOException("缺少 DSH 本机读取组件。");
        // Node writes UTF-8. A GUI launch on Chinese Windows can inherit CP936;
        // decoding with that code page can consume JSON backslashes as byte pairs.
        var start = new ProcessStartInfo(node) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment.Remove("NODE_OPTIONS"); start.Environment.Remove("NODE_PATH"); start.ArgumentList.Add(script);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("DSH 读取组件未启动。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false); var diagnostic = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                try { using var failure = JsonDocument.Parse(text.Trim()); diagnostic = CodexState.Text(failure.RootElement, "reason"); } catch (JsonException) { diagnostic = "读取组件暂不可用"; }
                throw new IOException("DSH 日志读取失败：" + diagnostic[..Math.Min(240, diagnostic.Length)]);
            }
            if (text.Length > 4 * 1024 * 1024) throw new IOException("DSH 结构事件批次超出范围。");
            return text;
        }
        catch (OperationCanceledException e) when (!cancellation.IsCancellationRequested)
        { throw new IOException("DSH 本机日志读取超时，将保留等待并自动重试。", e); }
        finally { await StopOwnedReader(process).ConfigureAwait(false); }
    }
    private static async Task StopOwnedReader(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { return; } // It exited between the check and Kill.
        catch (System.ComponentModel.Win32Exception e) { throw new IOException("DSH 自建读取进程未能结束；没有结束原 DSH 程序。", e); }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false); }
        catch (OperationCanceledException e) { throw new IOException("DSH 自建读取进程结束等待超时；没有结束原 DSH 程序。", e); }
    }
}

internal static class ClaudeCatalog
{
    internal static CodexThread[] Load(string home, string eventRoot, string? desktopRoot = null) =>
        Load(home, eventRoot, ClaudeDesktopCatalog.Load(desktopRoot));
    internal static CodexThread[] Load(string home, string eventRoot, ClaudeDesktopCatalog.Entry[] desktopEntries)
    {
        var values = new Dictionary<string, CodexThread>(StringComparer.Ordinal);
        var projectRoot = Path.Combine(home, "projects");
        if (Directory.Exists(projectRoot))
        foreach (var path in Directory.EnumerateFiles(projectRoot, "*.jsonl", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "subagents" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(1000))
        {
            var id = Path.GetFileNameWithoutExtension(path); if (!Guid.TryParse(id, out _)) continue;
            var name = id;
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new byte[(int)Math.Min(f.Length, 128 * 1024)]; var read = f.Read(bytes);
                foreach (var line in Encoding.UTF8.GetString(bytes, 0, read).Split('\n').Take(32))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line); var cwd = CodexState.Text(doc.RootElement, "cwd");
                        var title = CodexState.Text(doc.RootElement, "customTitle");
                        if (title.Length > 0) { name = title; break; }
                        if (cwd.Length > 0) { name = Path.GetFileName(cwd.TrimEnd('/', '\\')) + " · " + id[..8]; break; }
                    }
                    catch (JsonException) { }
                }
                values[id] = new(id, name, new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds(), AiPlatform.ClaudeCode);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        var journal = Path.Combine(eventRoot, "claude");
        if (Directory.Exists(journal)) foreach (var path in Directory.EnumerateFiles(journal, "*.jsonl"))
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(f); var line = reader.ReadLine(); if (line is null) continue;
                var e = JsonSerializer.Deserialize<AiRawEvent>(line);
                if (e is not null && !values.ContainsKey(e.SessionId)) values[e.SessionId] = new(e.SessionId, e.SessionId, new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds(), AiPlatform.ClaudeCode);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        foreach (var row in desktopEntries)
        {
            values.Remove(row.EngineId);
            values[row.DesktopId] = new(row.DesktopId, row.Title, row.UpdatedAt, AiPlatform.ClaudeCode);
        }
        return values.Values.OrderByDescending(x => x.UpdatedAt).ToArray();
    }
}
