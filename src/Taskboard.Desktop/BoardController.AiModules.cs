using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private DispatcherTimer? otherAiRecoveryTimer;
    internal void StartOtherAiRecovery(TimeSpan? checkInterval = null)
    {
        if (Isolated && checkInterval is null) return;
        otherAiRecoveryTimer?.Stop();
        otherAiRecoveryTimer = new DispatcherTimer { Interval = checkInterval ?? TimeSpan.FromSeconds(5) };
        otherAiRecoveryTimer.Tick += async (_, _) =>
        {
            if (exiting || dataTransition) return;
            foreach (var module in aiModules.Values)
                if ((!Isolated || aiCheckFixtures.ContainsKey(module.Platform)) && !module.Busy && module.Report.State is AiConnectionState.NotChecked or AiConnectionState.NotInstalled or AiConnectionState.MissingRuntime or AiConnectionState.NeedsOpen or AiConnectionState.Failed)
                    await CheckOtherAi(module.Platform);
        };
        otherAiRecoveryTimer.Start();
    }
    private readonly Dictionary<AiPlatform, AiListeningModule> aiModules = new()
    { [AiPlatform.Dsh] = new(AiPlatform.Dsh), [AiPlatform.ClaudeCode] = new(AiPlatform.ClaudeCode) };
    private readonly Dictionary<AiPlatform, IAiListeningEnvironment> aiCheckFixtures = new();
    internal void SetAiEnvironmentForChecks(AiPlatform platform, IAiListeningEnvironment environment)
    { if (!Isolated) throw new InvalidOperationException("检查夹具仅限隔离数据目录。"); AiModule(platform); aiCheckFixtures[platform] = environment; }
    internal AiListeningModule AiModule(AiPlatform platform) => aiModules.TryGetValue(platform, out var module) ? module : throw new ArgumentOutOfRangeException(nameof(platform));
    private IAiListeningEnvironment AiEnvironment(AiPlatform platform) => platform == AiPlatform.Dsh ? new DshListeningEnvironment() : new ClaudeListeningEnvironment();
    internal string OtherAiStatus => string.Join(" · ", aiModules.Values.Select(x => AiState.Name(x.Platform) + "：" + (x.Observer?.Status ?? x.Report.Message)));
    private void StartOtherAi()
    {
        StartOtherAiRecovery();
        foreach (var module in aiModules.Values)
        {
            module.Revision++; module.Busy = false; module.Report = AiConnectionReport.Initial; module.Test = "尚未测试已关联会话。"; module.Tested = false; module.Catalog = null;
            StartOtherAi(module.Platform);
            if (!Isolated) _ = CheckOtherAi(module.Platform);
        }
    }
    private void StartOtherAi(AiPlatform platform)
    {
        var module = AiModule(platform); module.Observer?.Dispose(); module.Observer = null;
        try
        {
            var options = AiPlatformOptions.From(platform, Engine.State.Preferences.ConnectionConfigs);
            AttachOtherAi(new AiPlatformObserver(options.Events.Length > 0 ? options.Events : null, platform == AiPlatform.Dsh ? options.Home : null,
                platform, options.Node.Length > 0 ? options.Node : null, platform == AiPlatform.ClaudeCode ? options.Home : null), platform);
        }
        catch (Exception e) when (e is System.IO.IOException or ArgumentException or UnauthorizedAccessException)
        { module.Report = new(AiConnectionState.InvalidPath, "本机监听位置不可用", "请在此平台的手动修复中选择本机路径，再检查连接。", e.Message); }
    }
    internal void AttachOtherAi(AiPlatformObserver source, AiPlatform platform = AiPlatform.ClaudeCode)
    {
        var module = AiModule(platform); module.Observer?.Dispose(); module.Observer = source; module.Catalog = null;
        var sourceState = Engine.State;
        module.SourceConfiguration = new(Engine.State.Preferences.ConnectionConfigs);
        bool Current() => !exiting && !dataTransition && ReferenceEquals(sourceState, Engine.State) && module.Observer == source && AiPlatformOptions.SameConfiguration(platform, module.SourceConfiguration, Engine.State.Preferences.ConnectionConfigs);
        source.Changed += (taskId, stageId, bindingId, generation, value) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!Current() || value.Platform != platform) return;
            var task = Engine.State.Tasks.FirstOrDefault(x => x.Id == taskId); if (task is null) return;
            var before = task.Lane;
            if (AiState.Apply(task, stageId, bindingId, generation, value)) NotifyCodexChange(task, before);
            settings?.RefreshOtherAi(platform);
        }));
        source.StatusChanged += () => Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (Current()) settings?.RefreshOtherAi(platform); }));
        source.Watch(Engine.State.Tasks); source.Start();
    }
    private void WatchOtherAi() { foreach (var module in aiModules.Values) module.Observer?.Watch(Engine.State.Tasks); }
    private void DisposeOtherAi() { otherAiRecoveryTimer?.Stop(); foreach (var module in aiModules.Values) { module.Observer?.Dispose(); module.Observer = null; module.Revision++; } }
    internal async Task ReconnectOtherAi(AiPlatform platform)
    {
        var module = AiModule(platform); if (module.Busy || exiting || dataTransition) return;
        module.Catalog = null; module.Tested = false;
        await CheckOtherAi(platform);
        if (!Isolated) module.Observer?.Start();
    }
    internal async Task<CodexThread[]> LoadPlatformConversations(AiPlatform platform, Func<Task<CodexThread[]>>? read = null)
    {
        if (platform == AiPlatform.Codex) return await LoadConversations(read);
        if (Isolated && read is null) return ConversationFixture.Where(x => x.Platform == platform).ToArray();
        var module = AiModule(platform);
        if (read is null && module.Observer is null) StartOtherAi(platform);
        if (module.Catalog is null || module.Catalog.IsFaulted || module.Catalog.IsCanceled ||
            module.Catalog.IsCompletedSuccessfully && module.Catalog.Result.Length == 0 || DateTime.UtcNow - module.CatalogAt > TimeSpan.FromSeconds(30))
        {
            module.CatalogAt = DateTime.UtcNow;
            var revision = module.Revision; var configuration = new Dictionary<string, string>(Engine.State.Preferences.ConnectionConfigs); var state = Engine.State;
            async Task<CodexThread[]> Load()
            {
                var values = await (read ?? (() => module.Observer!.Catalog(platform)))();
                if (module.Revision != revision || !ReferenceEquals(state, Engine.State) || !AiPlatformOptions.SameConfiguration(platform, configuration, Engine.State.Preferences.ConnectionConfigs)) throw new OperationCanceledException("会话目录配置已变化，请重新打开。");
                if (values.Any(x => x.Platform != platform || AiState.NormalizeSessionId(platform, x.Id) is null)) throw new System.IO.IOException("会话目录的来源或编号无效。");
                return values;
            }
            module.Catalog = Load();
        }
        return await module.Catalog;
    }
    internal void OtherAiConfigurationChanged(AiPlatform platform)
    {
        var module = AiModule(platform); module.Revision++; module.Busy = false; module.Catalog = null; module.Test = "配置已变化，请重新测试已关联会话。"; module.Tested = false;
        module.Report = new(AiConnectionState.NotChecked, "配置已修改，正在准备自动检查", "停止输入后自动检查并恢复监听，也可点击检查连接。");
        module.Observer?.Dispose(); module.Observer = null;
        foreach (var task in Engine.State.Tasks.Where(x => x.Stages.Any(s => s.Codex?.Platform == platform)))
            foreach (var binding in task.Stages.Select(x => x.Codex).OfType<CodexBinding>().Where(x => x.Platform == platform)) binding.Phase = CodexPhase.Unknown;
        Engine.Touch(); Engine.Notify(Engine.State.Tasks.Where(x => x.Stages.Any(s => s.Codex?.Platform == platform)).Select(x => x.Id).ToArray());
        settings?.RefreshOtherAi(platform);
        _ = RecoverOtherAiConfiguration(platform, module.Revision, Engine.State);
    }
    private async Task RecoverOtherAiConfiguration(AiPlatform platform, long revision, object state)
    {
        // Coalesce typing and folder selection. Old work must never restart a new data/config scope.
        await Task.Delay(600);
        var module = AiModule(platform);
        if (exiting || dataTransition || !ReferenceEquals(state, Engine.State) || module.Revision != revision ||
            Isolated && !aiCheckFixtures.ContainsKey(platform)) return;
        await CheckOtherAi(platform);
    }
    internal async Task CheckOtherAi(AiPlatform platform, bool initialize = false, IAiListeningEnvironment? environment = null)
    {
        var module = AiModule(platform); if (module.Busy || exiting || dataTransition) return;
        if (Isolated && environment is null) aiCheckFixtures.TryGetValue(platform, out environment);
        if (Isolated && environment is null) { module.Report = new(AiConnectionState.Failed, "隔离测试不会检查或修改外部正式配置", "请使用隔离测试来源。"); settings?.RefreshOtherAi(platform); return; }
        var state = Engine.State; var original = new Dictionary<string, string>(state.Preferences.ConnectionConfigs); var revision = module.Revision;
        bool Current() => !exiting && !dataTransition && ReferenceEquals(state, Engine.State) && module.Revision == revision && AiPlatformOptions.SameConfiguration(platform, original, state.Preferences.ConnectionConfigs);
        module.Busy = true; module.Report = new(AiConnectionState.Checking, initialize ? "正在初始化监听…" : "正在检查连接…", "正在检查此平台的程序、本机目录和监听配置。"); settings?.RefreshOtherAi(platform);
        var service = environment ?? AiEnvironment(platform);
        try
        {
            var probe = await service.Probe(AiPlatformOptions.From(platform, original), CancellationToken.None);
            if (!Current()) return;
            if (probe.Options.Platform != platform) throw new InvalidOperationException("检查来源与当前平台不一致。");
            if (probe.Report.Installed && probe.Report.State is not (AiConnectionState.Failed or AiConnectionState.InvalidPath or AiConnectionState.MissingRuntime))
            {
                foreach (var pair in probe.Options.Values()) state.Preferences.ConnectionConfigs[pair.Key] = pair.Value;
                original = new(state.Preferences.ConnectionConfigs); Engine.Touch();
                if (!await Flush()) { module.Report = new(AiConnectionState.Failed, "连接配置未能保存", "请检查数据目录是否可写，再重新初始化。"); return; }
                if (!Current()) return;
                if (initialize)
                {
                    service.Initialize(probe.Options);
                    probe = await service.Probe(probe.Options, CancellationToken.None);
                    if (!Current()) return;
                }
                if (!Isolated && (module.Observer is null || !AiPlatformOptions.SameConfiguration(platform, module.SourceConfiguration, state.Preferences.ConnectionConfigs))) StartOtherAi(platform);
                module.Catalog = null;
            }
            module.Report = probe.Report;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or OperationCanceledException)
        { if (Current()) module.Report = new(AiConnectionState.Failed, "连接操作未完成", "请查看诊断，修复后重新检查；其他平台继续监听。", e.GetType().Name + "：" + e.Message); }
        finally
        {
            if (module.Revision == revision)
            {
                module.Busy = false;
                if (!Current()) module.Report = new(AiConnectionState.NotChecked, "配置或数据已变化，请重新检查", "已取消旧检查结果，不覆盖当前配置。");
            }
            settings?.RefreshOtherAi(platform);
        }
    }
    private string OtherAiBindingScope(AiPlatform platform) => string.Join(";", Engine.State.Tasks.SelectMany(t => t.Stages.Where(s => s.Codex?.Platform == platform)
        .Select(s => t.Id + "/" + s.Id + "/" + s.Codex!.BindingId + "/" + s.Codex.Generation + "/" + s.Codex.ThreadId)));
    internal void TestOtherAiConversations(AiPlatform platform)
    {
        var module = AiModule(platform); if (module.Busy || exiting || dataTransition) return;
        module.TestScope = OtherAiBindingScope(platform);
        module.Tested = true;
        var bindings = Engine.State.Tasks.SelectMany(t => t.Stages.Where(s => s.Codex?.Platform == platform).Select(s => (Task: t, Stage: s, Binding: s.Codex!))).ToArray();
        if (bindings.Length == 0) module.Test = "尚未关联此平台的会话。请先在任务阶段选择已有会话。";
        else
        {
            var currentSource = AiPlatformOptions.SameConfiguration(platform, module.SourceConfiguration, Engine.State.Preferences.ConnectionConfigs);
            module.Test = "已有监测结果（查看于 " + DateTime.Now.ToString("HH:mm:ss") + "）\n" + string.Join("\n", bindings.Select(x =>
            {
                var value = currentSource ? module.Observer?.Peek(x.Binding.ThreadId) : null;
                var valid = value is not null && value.Complete && value.AtUtc >= x.Binding.BoundAtUtc && value.Phase != CodexPhase.Unknown;
                var phase = !valid ? "状态待确认" : value!.Pending.Any(p => p.Approval) ? "等待批准" : value.Pending.Length > 0 ? "等待回答" : value.Phase switch
                { CodexPhase.Running => "最近确认正在执行", CodexPhase.ReadyForReview => "结果待检查", CodexPhase.Failed => "整轮失败", CodexPhase.Interrupted => "已中断", _ => "状态待确认" };
                return x.Task.Title + " / " + x.Stage.Name + "：" + phase + (valid ? $"；待处理 {value!.Pending.Length} 项；来源观察于 {value.AtUtc.ToLocalTime():HH:mm:ss}。" : "；已确认的等待提醒仍保留。");
            })) + "\n本次只查看已有快照，不连接新会话、不发送消息或改变任务。";
        }
        settings?.RefreshOtherAi(platform);
    }
    internal string OtherAiTest(AiPlatform platform)
    {
        var module = AiModule(platform);
        return module.Tested && module.TestScope != OtherAiBindingScope(platform) ? "会话关联已变化，请重新测试。" : module.Test;
    }
    internal async Task OpenOtherAi(AiPlatform platform, IAiListeningEnvironment? environment = null)
    {
        var module = AiModule(platform); if (module.Busy || dataTransition || exiting || Isolated && environment is null) return;
        var service = environment ?? AiEnvironment(platform); var desktop = AiPlatformOptions.From(platform, Engine.State.Preferences.ConnectionConfigs).Desktop;
        try
        {
            if (desktop.Length == 0) desktop = AiDesktopDiscovery.Find(platform, "");
            if (desktop.Length == 0) throw new System.IO.IOException("尚未找到已安装的程序，请先初始化或手动选择。");
            if (!service.Running(desktop)) service.Open(desktop);
            await CheckOtherAi(platform, environment: environment);
        }
        catch (Exception e) when (e is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        { module.Report = new(AiConnectionState.Failed, "未能打开对应程序", "请检查该平台的桌面程序路径。", e.Message); settings?.RefreshOtherAi(platform); }
    }
    internal void InstallClaudeListening()
    {
        if (Isolated) throw new InvalidOperationException("隔离测试不修改 Claude 的正式设置。");
        var options = AiPlatformOptions.From(AiPlatform.ClaudeCode, Engine.State.Preferences.ConnectionConfigs);
        ClaudeHook.Install(options.Home, options.Events);
    }
}
