using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taskboard.Desktop;

internal sealed record CodexBehaviorEvent(string ThreadId, string TurnId, string Method, string RequestId, long AtMs, bool Reconciled = false);

// Passive file observer. Never opens a Codex pipe/socket, invokes a tool, launches
// an AI process, or changes logging/configuration. Only metadata leaves this class.
internal sealed class CodexBehaviorSource
{
    private sealed class Tail
    {
        internal long Offset;
        internal byte[] Partial = [];
        internal bool Initialized;
        internal int Generation;
        internal long Touched;
    }
    private sealed class Turn(string id)
    {
        internal readonly string Id = id;
        internal readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);
        internal long ResolvedAt;
        internal bool Waited;
        internal bool LastApproval;
        internal bool Uncertain;
        internal Tail? Rollout;
        internal string RolloutPath = "";
        internal string RolloutTurn = "";
        internal string LastMethod = "";
        internal string LastKey = "";
        internal bool VerifiedResume;
        internal long LastExecutionMs;
    }
    private sealed record Request(string Thread, string Turn, string Method, string Key, long AtMs);
    // Never retain prompt/answer/command text, even while a local projection lags.
    private sealed record Entry(long AtMs, string Origin, string Kind, string Thread, string Turn, string Id, string Method, bool Historical = false, long ObservedTick = 0);
    private readonly Dictionary<string, Tail> tails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Turn> turns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Request> requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> generations = new(StringComparer.Ordinal);
    private readonly Func<string[]> discover;
    private readonly long discoveryIntervalMs;
    private readonly List<Entry> deferred = [];
    private bool baselined;
    private readonly List<Entry> baselineEntries = [];
    private readonly HashSet<string> baselineThreads = new(StringComparer.Ordinal);
    private long baselineBytes;
    internal bool Reconciling { get; private set; }
    internal bool BootstrapIncomplete { get; private set; }
    private string[] files = [];
    private long discoveredAt;
    internal string Status { get; private set; } = "等待原客户端产生本地行为记录";
    internal bool Available { get; private set; }
    internal IReadOnlyList<CodexBehaviorEvent> Snapshot(string thread)
    {
        if (!baselined || BootstrapIncomplete || !turns.TryGetValue(thread, out var turn)) return [];
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (turn.Pending.Count > 0) return turn.Pending.Select(p => new CodexBehaviorEvent(thread, turn.Id, p.Value, p.Key, at, true)).ToArray();
        if (turn.Waited && turn.LastKey.Length > 0)
            return [new(thread,turn.Id,turn.LastMethod,turn.LastKey,at,true),new(thread,turn.Id,"serverRequest/resolved",turn.LastKey,at,true)];
        return turn.VerifiedResume ? [new(thread,turn.Id,"thread/status/changed","",at,true)] : [];
    }
    internal string[] Flags(string thread)
    {
        if (!turns.TryGetValue(thread, out var turn) || !turn.Waited) return [];
        var approval = turn.Pending.Count > 0 ? turn.Pending.Values.Any(m => m.EndsWith("requestApproval", StringComparison.Ordinal)) : turn.LastApproval;
        return [approval ? "waitingOnApproval" : "waitingOnUserInput"];
    }
    internal void Unavailable() { Available = false; Status = "本地行为记录暂不可读，保留已确认等待并自动重试"; }
    internal long ExecutionAt(string thread, string turn) => turns.TryGetValue(thread, out var state) && state.Id == turn ? state.LastExecutionMs : 0;
    internal CodexBehaviorSource(Func<string[]>? discover = null, long discoveryIntervalMs = 5000)
    { this.discover = discover ?? Discover; this.discoveryIntervalMs = discoveryIntervalMs; }
    private static readonly Regex Field = new(@"\b(?<key>\w+)=(?<value>.*?)(?=\s+\w+=|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Header = new(@"^\S+ (?:info|debug|warning) \[(?<source>electron-message-handler|host-app-server-manager)\] (?<body>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal IReadOnlyList<CodexBehaviorEvent> Read(IReadOnlyDictionary<string, CodexActivity> records, IReadOnlyCollection<string>? watched = null)
    {
        var result = new List<CodexBehaviorEvent>();
        watched ??= records.Keys.ToArray();
        if (baselined && watched.Any(id => !baselineThreads.Contains(id)))
        { baselined = false; baselineEntries.Clear(); baselineBytes = 0; tails.Clear(); requests.Clear(); turns.Clear(); BootstrapIncomplete = false; }
        baselineThreads.Clear(); baselineThreads.UnionWith(watched);
        foreach (var id in turns.Keys.Where(id => !watched.Contains(id)).ToArray()) turns.Remove(id);
        if (watched.Count == 0) { deferred.Clear(); Reconciling = false; Status = "本地行为补充已开启 · 尚未关联聊天"; return result; }
        foreach (var (id, record) in records)
            if (!turns.TryGetValue(id, out var turn) || turn.Id != record.TurnId ||
                record.ProjectionCaughtUp && record.Status != "inProgress") turns[id] = new(record.TurnId);
        foreach (var key in requests.Where(p => !turns.TryGetValue(p.Value.Thread, out var turn) || turn.Id != p.Value.Turn ||
            !turn.Pending.ContainsKey(p.Value.Key)).Select(p => p.Key).ToArray()) requests.Remove(key);
        var now = Environment.TickCount64;
        if (now - discoveredAt >= discoveryIntervalMs || discoveredAt == 0)
        { files = discover().Take(8).ToArray(); discoveredAt = now; }
        Available = files.Length > 0;
        var seeding = !baselined && watched.Any(id => !records.TryGetValue(id,out var r) || !r.ProjectionCaughtUp || r.Status == "inProgress");
        var batch = new List<Entry>(deferred); deferred.Clear();
        foreach (var file in files)
        {
            if (!tails.TryGetValue(file, out var tail)) tails[file] = tail = new() { Initialized = seeding || baselined };
            tail.Touched = now;
            var origin = Origin(file); var priorGeneration = tail.Generation;
            var before = tail.Offset;
            try
            {
                foreach (var line in ReadLines(file, tail))
                    if (Decode(line, origin + ":" + generations.GetValueOrDefault(origin)) is { } entry && (entry.Kind == "response" || watched.Contains(entry.Thread)))
                        batch.Add(entry with { Historical = seeding, ObservedTick = now });
            }
            catch (IOException)
            {
                if (tail.Generation != priorGeneration)
                {
                    generations[origin] = generations.GetValueOrDefault(origin) + 1;
                    foreach (var turn in turns.Values.Where(t => t.Waited)) turn.Uncertain = true;
                }
                deferred.AddRange(batch);
                throw;
            }
            finally { if (seeding) baselineBytes += Math.Max(0, tail.Offset - before); }
        }
        // Retain cursors through a temporary empty discovery. Otherwise a file
        // reappearing after a reminder could replay an old wait/resume chain.
        foreach (var old in tails.Where(p => !files.Contains(p.Key,StringComparer.OrdinalIgnoreCase)).OrderBy(p => p.Value.Touched).Take(Math.Max(0,tails.Count-32)).Select(p=>p.Key).ToArray()) tails.Remove(old);
        foreach (var (thread, record) in records)
        {
            if (!record.ProjectionCaughtUp || record.Status != "inProgress" || record.RolloutPath.Length == 0 || !turns.TryGetValue(thread, out var turn)) continue;
            if (turn.Rollout is null || turn.RolloutPath != record.RolloutPath)
            {
                turn.Rollout = new() { Initialized = true, Offset = seeding ? 0 : record.RolloutBytes };
                turn.RolloutPath = record.RolloutPath; turn.RolloutTurn = record.TurnId;
            }
            var before = turn.Rollout.Offset;
            try
            {
                foreach (var line in ReadLines(record.RolloutPath, turn.Rollout, record.RolloutBytes))
                    if (DecodeExecution(line, thread, turn) is { } execution) batch.Add(execution with { Historical = seeding, ObservedTick = now });
            }
            catch (Exception error) when (error is IOException or JsonException) { turn.Uncertain = true; deferred.AddRange(batch); throw; }
            finally { if (seeding) baselineBytes += Math.Max(0, turn.Rollout.Offset - before); }
        }
        if (seeding)
        {
            baselineEntries.AddRange(batch); batch.Clear();
            Reconciling = files.Any(f => tails[f].Offset < new FileInfo(f).Length) || records.Any(p => p.Value.ProjectionCaughtUp && p.Value.Status == "inProgress" &&
                turns.TryGetValue(p.Key,out var t) && t.Rollout is not null && t.Rollout.Offset < p.Value.RolloutBytes);
            if (baselineBytes > 64L * 1024 * 1024 || baselineEntries.Count > 8192)
            {
                BootstrapIncomplete = true; baselineEntries.Clear(); Reconciling = false;
                foreach (var t in turns.Values) t.Uncertain = true;
                foreach (var file in files) { tails[file].Offset = new FileInfo(file).Length; tails[file].Partial = []; }
            }
            if (Reconciling) { Status = "正在核对当前轮次已有请求，暂不推断恢复"; return result; }
            batch.AddRange(baselineEntries); baselineEntries.Clear();
        }
        else Reconciling = false;
        baselined = files.Length > 0;
        var reconcileThreads = batch.Where(e=>e.Historical && e.Thread.Length>0 && records.TryGetValue(e.Thread,out var r) && r.ProjectionCaughtUp)
            .Select(e=>e.Thread).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in batch.OrderBy(e => e.AtMs))
        {
            if (entry.Kind != "response" && !watched.Contains(entry.Thread)) continue;
            if (!Apply(entry, records, result) && now - entry.ObservedTick <= 600_000) deferred.Add(entry);
        }
        if (deferred.Count > 4096)
        { deferred.Clear(); foreach (var turn in turns.Values.Where(t => t.Waited)) turn.Uncertain = true; throw new IOException("本地行为积压过多，保留已确认等待，不推断恢复"); }
        foreach (var (thread, record) in records)
            if (turns.TryGetValue(thread, out var state) && state.Waited && !state.Uncertain && state.Pending.Count == 0 &&
                state.ResolvedAt > 0 && record.ExecutionStartedMs > state.ResolvedAt && record.Status == "inProgress" && record.ProjectionCaughtUp)
            { result.Add(new(thread, record.TurnId, "thread/status/changed", "", record.ExecutionStartedMs)); state.Waited = false; state.ResolvedAt = 0; state.VerifiedResume = true; }
        if (seeding || reconcileThreads.Count > 0)
        {
            var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (seeding) reconcileThreads.UnionWith(watched);
            var resolved = result.Where(e => reconcileThreads.Contains(e.ThreadId) && e.Method == "serverRequest/resolved").Select(e => e with { AtMs = at, Reconciled = true }).ToArray();
            result.RemoveAll(e=>reconcileThreads.Contains(e.ThreadId)); result.AddRange(resolved);
            foreach (var thread in reconcileThreads) result.AddRange(Snapshot(thread));
        }
        Status = !Available ? "未发现原客户端行为日志，保留本机轮次监听并自动重试" :
            BootstrapIncomplete ? "当前轮次补齐超出安全范围，状态待确认；继续监听新事件" :
            turns.Values.Any(t => t.Uncertain) ? "本地行为记录有缺口，保留已确认等待，等待新轮次或结束" :
            turns.Values.Any(t => t.Pending.Count > 0) ? "已识别原客户端待处理请求" :
            turns.Values.Any(t => t.Waited) ? "原请求已处理，等待同轮继续执行的确认" :
            "本地行为补充已就绪";
        return result;
    }

    private static string Origin(string file)
    {
        var name = Path.GetFileName(file);
        // Daily/size rotation changes the final time and part, not the boot UUID,
        // process ID or log-stream ID. Responses can land in the next file.
        var match = Regex.Match(name, @"^(codex-desktop-[0-9a-fA-F-]{36}-\d+-t0-i\d+)-\d+-\d+\.log$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : name;
    }

    internal void Parse(string line, string origin, IReadOnlyDictionary<string, CodexActivity> records, List<CodexBehaviorEvent> output)
    { if (Decode(line, origin) is { } entry) Apply(entry, records, output); }

    private static Entry? Decode(string line, string origin)
    {
        if (!line.Contains("received question ",StringComparison.Ordinal) && !line.Contains("received approval ",StringComparison.Ordinal) &&
            !line.Contains("Sending server response ",StringComparison.Ordinal) && !line.Contains("Reasoning summary item completed ",StringComparison.Ordinal)) return null;
        var space = line.IndexOf(' ');
        if (space < 0 || !DateTimeOffset.TryParse(line.AsSpan(0, space), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var at)) return null;
        var ms = at.ToUnixTimeMilliseconds();
        var header = Header.Match(line); if (!header.Success) return null;
        var body = header.Groups["body"].Value;
        var question = body.StartsWith("[desktop-notifications] received question ", StringComparison.Ordinal);
        var approval = body.StartsWith("[desktop-notifications] received approval ", StringComparison.Ordinal);
        var response = body.StartsWith("Sending server response ", StringComparison.Ordinal);
        var execution = body.StartsWith("Reasoning summary item completed ", StringComparison.Ordinal);
        if (!(question || approval || response || execution)) return null;
        if ((question || approval) && header.Groups["source"].Value != "electron-message-handler" ||
            execution && header.Groups["source"].Value != "host-app-server-manager") return null;
        // Responses contain sensitive user answers after this delimiter; summary
        // fields precede fixed metadata. Neither body is searched for identities.
        if (response && body.IndexOf(" response=", StringComparison.Ordinal) is var responseAt && responseAt >= 0) body = body[..responseAt];
        if (execution)
        {
            var metadata = body.LastIndexOf(" summaryPartCount=", StringComparison.Ordinal);
            if (metadata < 0) return null;
            body = body[metadata..];
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Field.Matches(body))
        {
            var fieldName = match.Groups["key"].Value;
            if (fieldName is "conversationId" or "threadId" or "turnId" or "requestId" or "id" or "method" or "kind" or "hostId") values[fieldName] = match.Groups["value"].Value;
        }
        if (values.TryGetValue("hostId", out var host) && host != "local") return null;
        string Get(string key) => values.GetValueOrDefault(key, "");
        if (response)
        {
            var responseMethod = Get("method");
            return responseMethod is "item/tool/requestUserInput" or "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval"
                ? new(ms, origin, "response", "", "", Get("id"), responseMethod) : null;
        }
        var thread = Get("conversationId"); if (thread.Length == 0) thread = Get("threadId");
        if (execution)
            return new(ms, origin, "execution", thread, Get("turnId"), "", "");
        var id = Get("requestId");
        if (id.Length is 0 or > 128 || id == "undefined") return null;
        var method = approval ? Get("kind") switch
        { "commandExecution" => "item/commandExecution/requestApproval", "fileChange" => "item/fileChange/requestApproval", "permissionRequest" => "item/permissions/requestApproval", _ => "" } : "item/tool/requestUserInput";
        return method.Length == 0 ? null : new(ms, origin, "wait", thread, "", id, method);
    }

    private bool Apply(Entry entry, IReadOnlyDictionary<string, CodexActivity> records, List<CodexBehaviorEvent> output)
    {
        var ms = entry.AtMs; var origin = entry.Origin;
        if (entry.Kind == "response")
        {
            var raw = origin + ":" + entry.Id;
            if (!requests.TryGetValue(raw, out var request)) return false;
            if (ms < request.AtMs) return true;
            if (request.Method != entry.Method ||
                !turns.TryGetValue(request.Thread, out var turn) || turn.Id != request.Turn ||
                !records.TryGetValue(request.Thread, out var record) || !record.ProjectionCaughtUp) return false;
            if (record.TurnId != request.Turn || record.Status != "inProgress" || ms < record.StartedAt * 1000) return true;
            requests.Remove(raw); turn.Pending.Remove(request.Key); turn.ResolvedAt = Math.Max(turn.ResolvedAt, ms);
            output.Add(new(request.Thread, request.Turn, "serverRequest/resolved", request.Key, ms));
            return true;
        }
        var thread = entry.Thread;
        if (!records.TryGetValue(thread, out var activity) || !activity.ProjectionCaughtUp) return false;
        if (activity.Status != "inProgress") return entry.Kind != "wait" || ms < activity.StartedAt * 1000;
        if (!turns.TryGetValue(thread, out var current) ||
            activity.StartedAt > 0 && ms < activity.StartedAt * 1000) return true;
        if (entry.Kind == "execution")
        {
            if (entry.Turn == current.Id) current.LastExecutionMs = Math.Max(current.LastExecutionMs, ms);
            if (entry.Turn == current.Id && current.Waited && !current.Uncertain && current.Pending.Count == 0 && current.ResolvedAt > 0 && ms > current.ResolvedAt)
            { output.Add(new(thread, current.Id, "thread/status/changed", "", ms)); current.ResolvedAt = 0; current.Waited = false; current.VerifiedResume = true; }
            return true;
        }
        var id = entry.Id; var method = entry.Method;
        var approval = method.EndsWith("requestApproval", StringComparison.Ordinal);
        // A boot/file-qualified ID prevents an unrelated source's numeric request
        // from clearing a previous client's request after restart or log rotation.
        var key = "behavior:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(origin)))[..16] + ":" + id;
        if (current.Pending.ContainsKey(key)) return true;
        if (current.Pending.Count >= 128) { current.Uncertain = true; return true; }
        current.Pending[key] = method; current.Waited = true; current.LastApproval = approval;
        current.LastKey = key; current.LastMethod = method; current.VerifiedResume = false;
        requests[origin + ":" + id] = new(thread, current.Id, method, key, ms);
        output.Add(new(thread, current.Id, method, key, ms));
        return true;
    }

    private static Entry? DecodeExecution(string line, string thread, Turn turn)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("type", out var payloadType)) return null;
        if (type.GetString() == "event_msg" && payloadType.GetString() == "task_started")
        { turn.RolloutTurn = payload.TryGetProperty("turn_id", out var id) ? id.GetString() ?? "" : ""; return null; }
        if (turn.RolloutTurn != turn.Id || type.GetString() != "response_item" || payloadType.GetString() is not ("function_call" or "custom_tool_call")) return null;
        if (!payload.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return null;
        // A new model tool call is execution evidence. A pending user-input call
        // or its output is not. Arguments/output/message content are ignored.
        var tool = name.GetString() ?? "";
        if (tool.Length == 0 || tool.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) || tool.Contains("requestApproval", StringComparison.OrdinalIgnoreCase)) return null;
        if (!root.TryGetProperty("timestamp", out var time) || !DateTimeOffset.TryParse(time.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var at)) return null;
        return new(at.ToUnixTimeMilliseconds(), "rollout", "execution", thread, turn.Id, "", "");
    }

    private static IEnumerable<string> ReadLines(string file, Tail tail, long? committedEnd = null)
    {
        using var stream = new FileStream(CodexLocalCatalog.LocalPath(file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!tail.Initialized)
        { tail.Offset = stream.Length; tail.Initialized = true; yield break; }
        if (stream.Length < tail.Offset)
        { tail.Offset = stream.Length; tail.Partial = []; tail.Generation++; throw new IOException("原客户端日志已截断，等待重新建立行为基线"); }
        var limit = Math.Min(stream.Length, committedEnd ?? long.MaxValue);
        if (limit < tail.Offset) throw new IOException("本机已提交轮次位置倒退，未推断执行");
        stream.Position = tail.Offset;
        var chunk = new byte[(int)Math.Min(1024 * 1024, limit - tail.Offset)];
        var read = stream.Read(chunk); tail.Offset += read;
        var bytes = new byte[tail.Partial.Length + read]; tail.Partial.CopyTo(bytes, 0); Array.Copy(chunk, 0, bytes, tail.Partial.Length, read);
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == 10) { yield return Encoding.UTF8.GetString(bytes, start, i - start).TrimEnd('\r'); start = i + 1; }
        if (bytes.Length - start > 1024 * 1024) throw new IOException("原客户端日志单条记录过大，未推断状态");
        tail.Partial = bytes[start..];
    }

    private static string[] Discover()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>();
        var packaged = Path.Combine(local, "Packages", "OpenAI.Codex_2p2nqsd0c76g0", "LocalCache", "Local", "Codex", "Logs");
        if (Directory.Exists(packaged)) roots.Add(packaged);
        else roots.Add(Path.Combine(local, "Codex", "Logs"));
        var ids = new HashSet<int>();
        using var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        using (process)
            try
            {
                var path = process.MainModule?.FileName ?? "";
                if (process.SessionId == currentProcess.SessionId && path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) ids.Add(process.Id);
            }
            catch (Exception) { }
        var files = new List<string>();
        foreach (var root in roots)
        for (var day = 0; day < 2; day++)
        {
            var path = Path.Combine(root, DateTime.UtcNow.AddDays(-day).ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "codex-desktop-*-t0-*.log").Take(128))
                if (ids.Any(id => Path.GetFileName(file).Contains("-" + id + "-t0-", StringComparison.Ordinal))) files.Add(file);
        }
        return files.OrderByDescending(File.GetLastWriteTimeUtc).Take(8).ToArray();
    }
}
