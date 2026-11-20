using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private DispatcherTimer? aiCreationTimer;
    private ConversationDiscoverySource? aiDiscovery;
    private string discoveryConfiguration = "";
    private bool discovering;
    private long discoveryGeneration;
    private readonly CancellationTokenSource discoveryLifetime = new();
    internal string AiCreationStatus { get; private set; } = "自动新建任务已关闭。";
    internal Func<AiPlatform, Task<DiscoveredExecution[]>>? DiscoveryFixture { get; set; }
    internal void RefreshAiCreation()
    {
        discoveryGeneration++; aiDiscovery = null;
        aiCreationTimer?.Stop();
        if (!Engine.State.Preferences.AutoCreateAiTasks) { AiCreationStatus = "自动新建任务已关闭。"; settings?.RefreshAiCreationStatus(); return; }
        if (aiCreationTimer is null)
        {
            aiCreationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            aiCreationTimer.Tick += async (_, _) => await ScanAiCreation();
        }
        AiCreationStatus = "正在建立当前会话基线，随后自动创建新开始执行的会话。";
        settings?.RefreshAiCreationStatus();
        aiCreationTimer.Start();
    }
    internal async Task ScanAiCreation()
    {
        if (discovering || exiting || dataTransition || !Engine.State.Preferences.AutoCreateAiTasks || Isolated && DiscoveryFixture is null) return;
        discovering = true;
        var state = Engine.State; var generation = discoveryGeneration;
        var config = new Dictionary<string, string>(state.Preferences.ConnectionConfigs);
        var signature = Signature(config, state.Preferences.CodexBehaviorSupplement);
        if (aiDiscovery is null || discoveryConfiguration != signature)
        { aiDiscovery = new(behaviorEnabled: state.Preferences.CodexBehaviorSupplement); discoveryConfiguration = signature; }
        var source = aiDiscovery;
        var monitored = 0; var problems = new List<string>();
        try
        {
            foreach (var platform in new[] { AiPlatform.Codex, AiPlatform.Dsh, AiPlatform.ClaudeCode })
            {
                if (DiscoveryFixture is null && !(platform == AiPlatform.Codex ? Codex.MonitoringReady : AiModule(platform).Report.Configured && AiModule(platform).Observer?.IsRunning == true)) continue;
                monitored++;
                try
                {
                    var values = await (DiscoveryFixture?.Invoke(platform) ?? source.Read(platform, config, discoveryLifetime.Token));
                    if (!Current()) return;
                    if (platform == AiPlatform.Codex && DiscoveryFixture is null) RefreshCodexDirectory(source.LatestCodexDirectory);
                    foreach (var value in values)
                    {
                        if (!Current()) return;
                        if (value.Platform != platform || AiState.NormalizeSessionId(platform, value.Session) is null || value.Key.Length == 0) continue;
                        var cursor = platform + ":" + value.Session;
                        if (state.AiCreationCursors.GetValueOrDefault(cursor) == value.Key) continue;
                        var target = new ConversationDropTarget(value.Session, value.Name, "", Platform: platform);
                        await CreateFromConversation(target, state, (cursor, value.Key), value.NewExecution, Current, value.NewExecution ? value.Sources : null);
                    }
                    if (source.Errors.Count > 0) problems.Add(AiState.Name(platform) + "有会话暂不可读取");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
                { if (Current()) problems.Add(AiState.Name(platform) + "暂不可读取：" + e.Message); }
            }
            if (Current()) AiCreationStatus = problems.Count > 0 ? string.Join("；", problems) + "。其余会话继续监控，将自动重试。" :
                monitored == 0 ? "尚无已连接的平台，连接本机监听后自动开始。" : "正在监控已连接的平台；未绑定的会话开始执行时自动新建任务。";
        }
        catch (OperationCanceledException) when (discoveryLifetime.IsCancellationRequested) { }
        finally { discovering = false; settings?.RefreshAiCreationStatus(); }
        bool Current() => !exiting && !dataTransition && ReferenceEquals(state, Engine.State) && generation == discoveryGeneration &&
            Engine.State.Preferences.AutoCreateAiTasks && signature == Signature(Engine.State.Preferences.ConnectionConfigs, Engine.State.Preferences.CodexBehaviorSupplement);
        static string Signature(Dictionary<string, string> values, bool behavior) => JsonSerializer.Serialize(new { settings = values.OrderBy(x => x.Key), behavior });
    }
    internal async Task<string> CreateFromConversation(ConversationDropTarget target, object? expectedState = null,
        (string Key, string Value)? cursor = null, bool create = true, Func<bool>? stillCurrent = null, DiscoveredSourceExecution[]? provenSources = null)
    {
        if (target.IsUnbind) return "未新建：当前操作仅用于快速新建任务，未绑定会话，无需解除";
        var id = AiState.NormalizeSessionId(target.Platform, target.ThreadId ?? "");
        if (id is null) return string.IsNullOrWhiteSpace(target.Reason) ? "未绑定：未能获得准确的会话编号。" : "未绑定：" + target.Reason;
        if (target.Platform == AiPlatform.Codex && codexDirectory.FirstOrDefault(x => x.Id == id) is { } source)
        {
            if (source.SessionRoot.Length == 0) return "未绑定：侧边聊天的主会话关系尚未确认。";
            id = source.SessionRoot;
            if (codexDirectory.FirstOrDefault(x => x.Id == id) is { } main) target = target with { ThreadId = id, Name = main.Name };
        }
        var state = Engine.State;
        await humanTaskGate.WaitAsync();
        try
        {
            if (!Valid()) return "未绑定：数据或监听设置已变化，请重试。";
            await saveGate.WaitAsync(); explicitSave++;
            try
            {
                if (!Valid()) return "未绑定：数据或监听设置已变化，请重试。";
                var bound = state.Tasks.Concat(state.Sessions.Select(x => x.Working)).Any(t => t.Stages.Any(s => s.Codex?.Platform == target.Platform &&
                    (s.Codex.ThreadId == id || target.Platform == AiPlatform.Codex && codexDirectory.FirstOrDefault(x => x.Id == s.Codex.ThreadId)?.SessionRoot == id)));
                var candidate = Copies.Of(state); var proposal = new BoardEngine(candidate);
                EditSession? session = null;
                if (create && !bound)
                {
                    session = proposal.NewSession(Lane.Running);
                    session.Working.Title = string.IsNullOrWhiteSpace(target.Name) ? "未命名会话" : target.Name.Trim();
                    session.Working.Stages[0].Codex = new CodexBinding { Platform = target.Platform, ThreadId = id, ThreadName = target.Name };
                    proposal.Submit(session);
                }
                if (cursor is { } value) candidate.AiCreationCursors[value.Key] = value.Value;
                if (session is null && cursor is null) return "已绑定 · 此会话已有任务或草稿。";
                var captured = dirtyVersion;
                await Store.SaveAsync(candidate);
                // No UI task is inserted before the validated candidate reaches disk.
                if (!Valid()) { await Store.SaveAsync(Copies.Of(Engine.State)); return "未绑定：监听设置已变化，未添加任务。"; }
                if (cursor is { } saved) state.AiCreationCursors[saved.Key] = saved.Value;
                if (session is not null)
                {
                    var created = Engine.Submit(session);
                    RefreshCodexDirectory(codexDirectory, notify: false);
                    if (target.Platform == AiPlatform.Codex && provenSources is not null && created.Stages[0].Codex is { } rootBinding)
                        foreach (var proof in provenSources)
                            if (rootBinding.Sources.FirstOrDefault(x => x.ThreadId == proof.Session) is { } binding && proof.TurnId.Length > 0)
                            {
                                CodexState.RememberObservedExecution(binding, proof.TurnId);
                            }
                    Engine.Notify([created.Id]);
                }
                else Engine.Touch();
                savedVersion = captured; saveTimer.Stop(); saveTimer.Start();
                return session is null ? "已绑定 · 此会话已处理。" : "已绑定 · 已新建“" + session.Working.DisplayTitle + "”。";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { Report("会话任务未能保存，未添加空白任务：" + e.Message); return "未绑定：保存失败，请重试。"; }
            finally { explicitSave--; saveGate.Release(); }
        }
        finally { humanTaskGate.Release(); }
        bool Valid() => !exiting && !dataTransition && ReferenceEquals(state, Engine.State) && (expectedState is null || ReferenceEquals(expectedState, state)) && (stillCurrent?.Invoke() ?? true);
    }
}
