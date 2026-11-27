using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record AiRawEvent(string SessionId, string Epoch, long Seq, DateTime AtUtc, string Kind,
    string Turn = "", string Id = "", string Name = "", string Fingerprint = "", string Outcome = "");

internal static class AiPaths
{
    internal static string Events => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstantTaskboard", "ai-events");
    internal static string ClaudeHome => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    internal static string DshHome => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DSH_HOME")) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh") : Environment.GetEnvironmentVariable("DSH_HOME")!;
    internal static string FileName(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
}

internal static class ClaudeHook
{
    internal static readonly string[] Events = ["SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "PermissionDenied", "Notification", "Stop", "StopFailure", "Elicitation", "ElicitationResult"];
    internal static bool Check(string home, string root, string executable, out string reason)
    {
        var path = Path.Combine(home, "settings.json");
        if (!File.Exists(path)) { reason = "尚未配置此看板的 Claude Code 原生事件监听。"; return false; }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); var config = doc.RootElement;
            if (CodexState.Object(config, "disableAllHooks").ValueKind == JsonValueKind.True)
            { reason = "Claude 的所有事件 Hook 已停用，请在 Claude 中启用后重新检查。"; return false; }
            var hooks = CodexState.Object(config, "hooks");
            foreach (var name in Events)
            {
                var groups = CodexState.Object(hooks, name);
                if (groups.ValueKind != JsonValueKind.Array || !groups.EnumerateArray().Any(group =>
                {
                    var items = CodexState.Object(group, "hooks");
                    return CodexState.Text(group, "matcher").Length == 0 && items.ValueKind == JsonValueKind.Array && items.EnumerateArray().Any(item =>
                    {
                        var argv = CodexState.Object(item, "args");
                        return CodexState.Text(item, "type") == "command" &&
                            string.Equals(CodexState.Text(item, "command").Replace('\\', '/'), executable.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
                            argv.ValueKind == JsonValueKind.Array && argv.GetArrayLength() == 4 && argv[0].GetString() == "--ai-hook" && argv[1].GetString() == "claude" && argv[2].GetString() == "--ai-events-root" &&
                            string.Equals(argv[3].GetString()?.Replace('\\', '/'), root.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) && CodexState.Text(item, "onFailure") == "continue";
                    });
                })) { reason = "原生事件配置缺失或程序位置已变化：" + name + "。请重新初始化。"; return false; }
            }
            reason = "已核对全部原生事件及当前看板程序位置；等待 Claude Code 实际会话事件。"; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { reason = "Claude 事件设置暂不可读：" + e.GetType().Name; return false; }
    }
    internal static int Run(string? root = null)
    {
        try
        {
            using var stdin = Console.OpenStandardInput(); using var buffer = new MemoryStream();
            var block = new byte[8192]; int count;
            while ((count = stdin.Read(block)) > 0) { buffer.Write(block, 0, count); if (buffer.Length > 1024 * 1024) return 0; }
            using var json = JsonDocument.Parse(buffer.ToArray()); var p = json.RootElement;
            var session = CodexState.Text(p, "session_id"); var kind = CodexState.Text(p, "hook_event_name");
            if (session.Length is 0 or > 256 || !Events.Contains(kind, StringComparer.Ordinal)) return 0;
            var input = CodexState.Object(p, "tool_input");
            var fingerprint = input.ValueKind == JsonValueKind.Object ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.GetRawText()))) : "";
            var outcome = kind == "Notification" ? CodexState.Text(p, "notification_type") : kind == "StopFailure" ? CodexState.Text(p, "error") : "";
            // Only allowlisted error enums survive; never retain tool input, answers or message text.
            if (kind == "StopFailure" && outcome is not ("rate_limit" or "authentication_failed" or "billing_error" or "server_error")) outcome = "failed";
            var id = CodexState.Text(p, "tool_use_id");
            if (kind is "Elicitation" or "ElicitationResult") id = CodexState.Text(p, "elicitation_id");
            if (kind == "PostToolUseFailure" && CodexState.Object(p, "is_interrupt").ValueKind == JsonValueKind.True) outcome = "interrupted";
            var e = new AiRawEvent(session, "", 0, DateTime.UtcNow, kind, CodexState.Text(p, "prompt_id"), id,
                kind is "Elicitation" or "ElicitationResult" ? CodexState.Text(p, "mcp_server_name") : CodexState.Text(p, "tool_name"), fingerprint, outcome);
            Write(root ?? AiPaths.Events, e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { }
        return 0; // Observation never answers, approves, prints context, or blocks Claude.
    }
    internal static void Write(string root, AiRawEvent value)
    {
        var key = AiPaths.FileName(value.SessionId); var dir = Path.Combine(root, "claude"); Directory.CreateDirectory(dir);
        using var mutex = new Mutex(false, "Local\\InstantTaskboard.AiHook." + key);
        bool owned; try { owned = mutex.WaitOne(1500); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return;
        try
        {
            var path = Path.Combine(dir, key + ".jsonl"); long seq = 0; var epoch = Guid.NewGuid().ToString("N");
            if (File.Exists(path))
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                f.Seek(Math.Max(0, f.Length - 16384), SeekOrigin.Begin); using var reader = new StreamReader(f);
                var tail = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (tail is not null) try { var old = JsonSerializer.Deserialize<AiRawEvent>(tail); if (old is not null) { seq = old.Seq + 1; epoch = old.Epoch; } } catch (JsonException) { }
            }
            File.AppendAllText(path, JsonSerializer.Serialize(value with { Seq = seq, Epoch = epoch }) + "\n", new UTF8Encoding(false));
        }
        finally { mutex.ReleaseMutex(); }
    }
    internal static void Install(string? home = null, string? root = null, string? executable = null)
    {
        home ??= AiPaths.ClaudeHome; root ??= AiPaths.Events; executable ??= Environment.ProcessPath!;
        var path = Path.Combine(home, "settings.json");
        var config = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new IOException("Claude 设置格式无效，原文件未修改。") : new JsonObject();
        var hooks = config["hooks"] as JsonObject;
        if (config["hooks"] is not null && hooks is null) throw new IOException("Claude hooks 设置格式无效，原文件未修改。");
        if (hooks is null) { hooks = new(); config["hooks"] = hooks; }
        foreach (var name in Events)
        {
            if (hooks[name] is not null && hooks[name] is not JsonArray) throw new IOException("Claude hook 列表格式无效，原文件未修改。");
            var entries = hooks[name] as JsonArray ?? new JsonArray();
            foreach (var group in entries.OfType<JsonObject>().ToArray())
            {
                if (group["hooks"] is not JsonArray items) continue;
                foreach (var item in items.OfType<JsonObject>().Where(x =>
                    x["command"]?.GetValue<string>()?.Contains("--ai-hook claude", StringComparison.Ordinal) == true ||
                    x["args"] is JsonArray argv && argv.Count >= 2 && argv[0]?.GetValue<string>() == "--ai-hook" && argv[1]?.GetValue<string>() == "claude").ToArray()) items.Remove(item);
                if (items.Count == 0) entries.Remove(group);
            }
            // Exec form is shell-independent on Windows, including paths with spaces/Unicode.
            // Explicit continue keeps observation failures from blocking the user's tools.
            entries.Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = executable,
                ["args"] = new JsonArray("--ai-hook", "claude", "--ai-events-root", root), ["timeout"] = 3, ["onFailure"] = "continue" }) });
            if (hooks[name] is null) hooks[name] = entries;
        }
        Directory.CreateDirectory(home);
        if (File.Exists(path)) File.Copy(path, path + ".taskboard-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false);
        var temp = path + ".taskboard.tmp"; File.WriteAllText(temp, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false)); File.Move(temp, path, true);
    }
}

internal sealed class AiEventState(AiPlatform platform, string session)
{
    private readonly Dictionary<string, AiPending> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Name, string Fingerprint)> tools = new(StringComparer.Ordinal);
    private string epoch = "", turn = "";
    private long sequence = -1;
    private DateTime started;
    private CodexPhase phase = CodexPhase.Unknown;
    private bool executing, complete = true, planMode;
    internal DateTime LastEventUtc { get; private set; }
    internal long ExecutionSequence { get; private set; } = -1;
    internal DateTime ExecutionStartedUtc { get; private set; }
    internal AiObservation Snapshot => new(platform, session, epoch, sequence, turn, started, phase, pending.Values.ToArray(), complete, executing, DateTime.UtcNow, LastEventUtc);
    internal void Apply(AiRawEvent e)
    {
        if (e.SessionId != session || e.Seq <= sequence && e.Epoch == epoch) return;
        if (epoch.Length > 0 && e.Epoch != epoch) { pending.Clear(); tools.Clear(); turn = ""; phase = CodexPhase.Unknown; planMode = false; }
        if (platform == AiPlatform.ClaudeCode && sequence >= 0 && epoch == e.Epoch && e.Seq != sequence + 1) complete = false;
        epoch = e.Epoch; sequence = e.Seq;
        if (e.Turn.Length > 0 && turn.Length > 0 && e.Turn != turn && e.Kind is not ("UserPromptSubmit" or "turn/start")) return;
        LastEventUtc = e.AtUtc;
        var wasExecuting = executing; var priorTurn = turn;
        void Begin()
        { turn = e.Turn.Length > 0 ? e.Turn : e.Epoch + ":" + e.Seq; started = e.AtUtc; pending.Clear(); tools.Clear(); phase = CodexPhase.Running; executing = true; }
        void Wait(string id, bool approval) { if (pending.Count >= 128) { complete = false; return; } pending[id] = new(id, approval); executing = false; }
        switch (e.Kind)
        {
            case "UserPromptSubmit": case "turn/start": complete = true; Begin(); break;
            case "SessionStart": break;
            case "PreToolUse":
                if (turn.Length == 0) { Begin(); executing = false; }
                if (e.Id.Length > 0) tools[e.Id] = (e.Name, e.Fingerprint);
                if (e.Name is "AskUserQuestion" or "ExitPlanMode") Wait("tool:" + e.Id, e.Name == "ExitPlanMode");
                else if (pending.Count == 0) phase = CodexPhase.Running;
                break;
            case "PermissionRequest":
                if (turn.Length == 0) { Begin(); executing = false; }
                var matches = tools.Where(x => x.Value.Name == e.Name && x.Value.Fingerprint == e.Fingerprint).Select(x => x.Key).ToArray();
                if (matches.Length == 0) matches = tools.Where(x => x.Value.Name == e.Name).Select(x => x.Key).ToArray();
                if (matches.Length > 0) foreach (var match in matches) Wait("tool:" + match, e.Name != "AskUserQuestion");
                else Wait("permission:" + e.Seq, e.Name != "AskUserQuestion");
                break;
            case "Notification":
                if (turn.Length == 0) break;
                // Idle merely means no activity. AskUserQuestion is identified by its tool hook.
                if (e.Outcome == "permission_prompt" && pending.Count == 0) Wait("notice:" + e.Seq, true);
                else if (e.Outcome is "elicitation_dialog" or "elicitation_url_dialog" && pending.Count == 0) Wait("notice:" + e.Seq, false);
                break;
            case "Elicitation": if (turn.Length == 0) { Begin(); executing = false; } Wait("elicitation:" + (e.Id.Length > 0 ? e.Id : e.Name + ":" + e.Seq), false); break;
            case "ElicitationResult":
                if (e.Id.Length > 0) pending.Remove("elicitation:" + e.Id);
                else { var forms = pending.Keys.Where(x => x.StartsWith("elicitation:" + e.Name + ":", StringComparison.Ordinal)).ToArray(); if (forms.Length == 1) pending.Remove(forms[0]); }
                executing = false; break;
            case "PostToolUse": case "PostToolUseFailure": case "PermissionDenied":
                pending.Remove("tool:" + e.Id); tools.Remove(e.Id);
                if (e.Outcome == "interrupted") { phase = CodexPhase.Interrupted; executing = false; pending.Clear(); break; }
                executing = e.Kind != "PermissionDenied" && e.Name is not ("AskUserQuestion" or "ExitPlanMode") && pending.Count == 0;
                if (executing) phase = CodexPhase.Running;
                break;
            case "approval/asked": Wait("approval:" + e.Id, true); break;
            case "approval/decided": pending.Remove("approval:" + e.Id); executing = false; break;
            case "plan/mode": planMode = e.Outcome == "active"; break;
            case "question/asked":
                if (e.Name != "exit_plan_mode" || planMode) Wait("question:" + e.Id, e.Outcome == "approval");
                break;
            case "question/answered": pending.Remove("question:" + e.Id); executing = false; break;
            case "execution":
                if (turn.Length > 0 && pending.Count == 0) { phase = CodexPhase.Running; executing = true; }
                break;
            case "Stop": case "turn/completed": phase = CodexPhase.ReadyForReview; pending.Clear(); tools.Clear(); executing = false; break;
            case "StopFailure": case "turn/failed": phase = CodexPhase.Failed; pending.Clear(); tools.Clear(); executing = false; break;
            case "turn/interrupted": phase = CodexPhase.Interrupted; pending.Clear(); tools.Clear(); executing = false; break;
            case "SessionEnd": phase = CodexPhase.Unknown; executing = false; break;
            case "unavailable": complete = false; phase = CodexPhase.Unknown; executing = false; break;
        }
        if (executing && (!wasExecuting || priorTurn != turn)) { ExecutionSequence = e.Seq; ExecutionStartedUtc = e.AtUtc; }
    }
}
