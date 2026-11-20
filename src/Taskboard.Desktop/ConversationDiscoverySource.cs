using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record DiscoveredSourceExecution(string Session, string TurnId);
internal sealed record DiscoveredExecution(AiPlatform Platform, string Session, string Name, string Key, bool NewExecution, DiscoveredSourceExecution[]? Sources = null);

// Reads the same structural records as the bound-session listeners. The initial
// catalog is a baseline, never an instruction to import historical chats.
internal sealed class ConversationDiscoverySource
{
    private readonly Func<Task<CodexThread[]>> codexCatalog;
    private readonly ICodexActivitySource codexActivity;
    private readonly ICodexSidebarSource? codexControls;
    private readonly CodexBehaviorSource? codexBehavior;
    internal ConversationDiscoverySource(Func<Task<CodexThread[]>>? catalog = null, ICodexActivitySource? activity = null,
        ICodexSidebarSource? controls = null, CodexBehaviorSource? behavior = null, bool behaviorEnabled = true)
    {
        var original = catalog is null && activity is null;
        codexCatalog = catalog ?? (() => CodexLocalCatalog.Load()); codexActivity = activity ?? new CodexFileActivity();
        codexControls = controls ?? (original ? new CodexSidebarSource() : null);
        codexBehavior = behaviorEnabled ? behavior ?? (original ? new CodexBehaviorSource() : null) : null;
    }
    private readonly HashSet<AiPlatform> initialized = [];
    private readonly Dictionary<string, long> stamps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LogCursor> logs = new(StringComparer.Ordinal);
    private readonly DateTime enabledAt = DateTime.UtcNow;
    private readonly Dictionary<string, string> codexBaseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Turn, long At)> codexWaits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> codexResumptions = new(StringComparer.Ordinal);
    internal IReadOnlyList<string> Errors { get; private set; } = [];
    internal CodexThread[] LatestCodexDirectory { get; private set; } = [];
    private sealed class LogCursor(AiPlatform platform, string session)
    {
        internal AiEventState State = new(platform, session);
        internal long Offset, Sequence = -1;
        internal long? Turn;
        internal string Identity = "";
    }
    internal async Task<DiscoveredExecution[]> Read(AiPlatform platform, Dictionary<string, string> config, CancellationToken token)
    {
        Errors = [];
        if (platform == AiPlatform.Codex)
        {
            var catalog = CodexThreadFamilies.Resolve(await codexCatalog().ConfigureAwait(false));
            LatestCodexDirectory = catalog;
            var records = await Task.Run(() => codexActivity.Read(catalog.Select(x => x.Id).ToArray()), token).ConfigureAwait(false);
            var baseline = initialized.Add(platform);
            if (baseline) foreach (var chat in catalog) codexBaseline[chat.Id] = records.GetValueOrDefault(chat.Id)?.TurnId ?? "";
            codexBehavior?.Read(records);
            var waiting = records.Where(x => codexBehavior?.Flags(x.Key).Length > 0).Select(x => x.Key).ToHashSet();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (codexBehavior is { Available: true, BootstrapIncomplete: false, Reconciling: false })
                foreach (var id in waiting.Where(id => records[id].ProjectionCaughtUp && records[id].Status == "inProgress"))
                    codexWaits[id] = (records[id].TurnId, now);
            if (codexControls is not null)
            {
                var view = await codexControls.Read(catalog.Select(x => x.Id).ToArray(), token).ConfigureAwait(false);
                if (view.Foreground) foreach (var row in view.Rows.Where(x => x.Phase is CodexSidebarPhase.WaitingForInput or CodexSidebarPhase.WaitingForApproval))
                    if (records.TryGetValue(row.ThreadId, out var record) && record.ProjectionCaughtUp && record.Status == "inProgress")
                    { waiting.Add(row.ThreadId); codexWaits[row.ThreadId] = (record.TurnId, view.AtMs); }
            }
            foreach (var (id, record) in records)
            {
                if (!record.ProjectionCaughtUp || record.Status != "inProgress" || waiting.Contains(id)) continue;
                if (codexWaits.TryGetValue(id, out var wait) && wait.Turn == record.TurnId &&
                    Math.Max(record.ExecutionStartedMs, codexBehavior?.ExecutionAt(id, record.TurnId) ?? 0) is var executed && executed > wait.At)
                { codexResumptions[id] = record.TurnId + ":resume:" + executed; codexWaits.Remove(id); }
            }
            var executing = catalog.Where(x => x.SessionRoot.Length > 0 && records.TryGetValue(x.Id, out var value) && value.ProjectionCaughtUp && value.Status == "inProgress" && !waiting.Contains(x.Id) &&
                    (!codexWaits.TryGetValue(x.Id, out var wait) || wait.Turn != value.TurnId))
                .Select(x => { var value = records[x.Id]; return new DiscoveredExecution(platform, x.Id, x.Name,
                    codexResumptions.TryGetValue(x.Id, out var resumed) && resumed.StartsWith(value.TurnId + ":resume:", StringComparison.Ordinal) ? resumed : value.TurnId,
                    !baseline && (codexBaseline.GetValueOrDefault(x.Id) != value.TurnId && value.StartedAt >= new DateTimeOffset(enabledAt).ToUnixTimeSeconds() ||
                    codexResumptions.GetValueOrDefault(x.Id, "").StartsWith(value.TurnId + ":resume:", StringComparison.Ordinal))); }).ToArray();
            return executing.GroupBy(x => catalog.First(t => t.Id == x.Session).SessionRoot, StringComparer.Ordinal)
                .Where(group => !catalog.Any(t => t.SessionRoot == group.Key && (waiting.Contains(t.Id) || codexWaits.ContainsKey(t.Id))))
                .Select(group => new DiscoveredExecution(platform, group.Key, catalog.First(t => t.Id == group.Key).Name,
                    group.Count() == 1 && group.First().Session == group.Key ? group.First().Key :
                        string.Join("|", group.OrderBy(x => x.Session, StringComparer.Ordinal).Select(x => x.Session + ":" + x.Key)), group.Any(x => x.NewExecution),
                    group.Select(x => new DiscoveredSourceExecution(x.Session, records[x.Session].TurnId)).ToArray())).ToArray();
        }
        var options = AiPlatformOptions.From(platform, config);
        var desktopEntries = platform == AiPlatform.ClaudeCode ? ClaudeDesktopCatalog.Load() : [];
        CodexThread[] chats;
        if (platform == AiPlatform.Dsh)
        {
            using var doc = JsonDocument.Parse(await DshProcess.Run(["--catalog", options.Home], token, options.Node).ConfigureAwait(false));
            chats = doc.RootElement.EnumerateArray().Select(x => new CodexThread(x.GetProperty("id").GetString()!,
                x.GetProperty("name").GetString()!, x.GetProperty("updatedAt").GetInt64(), platform)).ToArray();
        }
        else chats = await Task.Run(() => ClaudeCatalog.Load(options.Home, options.Events, desktopEntries), token).ConfigureAwait(false);
        var first = initialized.Add(platform);
        var result = new List<DiscoveredExecution>();
        var errors = new List<string>();
        foreach (var chat in chats)
        {
            token.ThrowIfCancellationRequested();
            var key = platform + ":" + chat.Id;
            try
            {
                var engine = platform == AiPlatform.ClaudeCode ? ClaudeDesktopCatalog.ResolveEngine(chat.Id, desktopEntries) : chat.Id;
                var file = platform == AiPlatform.ClaudeCode ? Path.Combine(options.Events, "claude", AiPaths.FileName(engine) + ".jsonl") : "";
                var stamp = platform == AiPlatform.ClaudeCode ? (File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0) : chat.UpdatedAt;
                if (first) { stamps[key + ":" + engine] = stamp; continue; }
                if (stamps.TryGetValue(key + ":" + engine, out var old) && old == stamp) continue;
                if (!logs.TryGetValue(key, out var cursor)) logs[key] = cursor = new(platform, chat.Id);
                if (platform == AiPlatform.ClaudeCode && cursor.Identity != engine)
                { logs[key] = cursor = new(platform, chat.Id); cursor.Identity = engine; }
                bool caughtUp;
                if (platform == AiPlatform.Dsh)
                {
                    var args = new List<string> { "--events", options.Home, chat.Id, "--offset", cursor.Offset.ToString(), "--after-seq", cursor.Sequence.ToString() };
                    if (cursor.Turn is { } turn) { args.Add("--turn"); args.Add(turn.ToString()); }
                    var output = await DshProcess.Run(args.ToArray(), token, options.Node).ConfigureAwait(false);
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    using var batchDoc = JsonDocument.Parse(lines.Last()); var batch = batchDoc.RootElement;
                    var identity = batch.GetProperty("fileIdentity").GetString() + ":" + batch.GetProperty("createdAt").GetInt64();
                    if (cursor.Identity.Length > 0 && identity != cursor.Identity) { logs.Remove(key); continue; }
                    cursor.Identity = identity;
                    foreach (var line in lines.Take(lines.Length - 1))
                    { using var row = JsonDocument.Parse(line); if (AiPlatformObserver.DshEvent(row.RootElement, identity) is { } value) cursor.State.Apply(value); }
                    cursor.Offset = batch.GetProperty("nextOffset").GetInt64();
                    if (batch.GetProperty("lastSequence").ValueKind == JsonValueKind.Number) cursor.Sequence = batch.GetProperty("lastSequence").GetInt64();
                    cursor.Turn = batch.GetProperty("currentTurn").ValueKind == JsonValueKind.Number ? batch.GetProperty("currentTurn").GetInt64() : null;
                    caughtUp = !batch.GetProperty("hasMore").GetBoolean();
                }
                else
                {
                    if (!File.Exists(file)) continue;
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length < cursor.Offset) { logs.Remove(key); continue; }
                    stream.Position = cursor.Offset;
                    var bytes = new byte[(int)Math.Min(1024 * 1024, stream.Length - stream.Position)];
                    var count = await stream.ReadAsync(bytes, token).ConfigureAwait(false);
                    var last = count == 0 ? -1 : Array.LastIndexOf(bytes, (byte)'\n', count - 1, count);
                    if (last >= 0)
                    {
                        foreach (var line in Encoding.UTF8.GetString(bytes, 0, last + 1).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            if (JsonSerializer.Deserialize<AiRawEvent>(line) is { } value) cursor.State.Apply(ClaudeDesktopCatalog.MapEvent(value, chat.Id, engine));
                        cursor.Offset += last + 1;
                    }
                    caughtUp = cursor.Offset == stream.Length;
                }
                if (!caughtUp) continue;
                stamps[key + ":" + engine] = stamp;
                var snapshot = cursor.State.Snapshot;
                if (snapshot.Complete && snapshot.Executing && snapshot.Phase == CodexPhase.Running && snapshot.TurnId.Length > 0 && cursor.State.ExecutionStartedUtc >= enabledAt)
                    result.Add(new(platform, chat.Id, chat.Name, snapshot.Epoch + ":" + snapshot.TurnId + ":" + cursor.State.ExecutionSequence, true));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
            { logs.Remove(key); errors.Add(chat.Name + "：" + e.Message); }
        }
        Errors = errors;
        return result.ToArray();
    }
}
