using System.IO;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class RecoveryChecks
{
    internal static async Task Run(string root, List<string> checks)
    {
        using var controller = new BoardController(Path.Combine(root, "recovery-board"), true, true);
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        await using (var observer = new CodexObserver(activitySource: () => new EmptyRecords()))
        {
            observer.Start();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!observer.TransportConnected && DateTime.UtcNow < deadline) await Task.Delay(20);
            Check(observer.TransportConnected && !observer.SourceIdentityVerified && observer.ConnectionHint.Length == 0,
                "Codex记录读取可用不冒充桌面身份核验，也不残留断线提示");
        }
        var result = await new CodexInitialization(new LocalCodex()).Run(new());
        Check(result.Report.State == CodexSetupState.Ready && result.Report.SourceReady && !result.Report.DesktopConnected,
            "Codex本机记录检查成功后初始化完成，不冒充桌面身份连接或陷入旧管道重连提示");
        var options = new AiPlatformOptions(AiPlatform.Dsh, "D:/fixture/dsh.exe", Path.Combine(root, "auto-home"), "D:/fixture/node.exe", "");
        var environment = new DshEnvironment(options);
        controller.SetAiEnvironmentForChecks(AiPlatform.Dsh, environment);
        controller.Engine.State.Preferences.ConnectionConfigs["dsh.home"] = options.Home;
        controller.OtherAiConfigurationChanged(AiPlatform.Dsh);
        controller.Engine.State.Preferences.ConnectionConfigs["dsh.home"] = options.Home + "-latest";
        controller.OtherAiConfigurationChanged(AiPlatform.Dsh);
        await Task.Delay(1400);
        Check(environment.Probes == 1 && controller.AiModule(AiPlatform.Dsh).Report.State == AiConnectionState.Ready,
            "连续修复DSH路径后自动合并检查，无需再次点击检查连接");
        Check(controller.Store.Load().Preferences.ConnectionConfigs["dsh.home"] == options.Home + "-latest",
            "自动检查发现的有效路径经过保存链落盘");
        Check(environment.Initializations == 0, "路径自动恢复只检查，不执行外部初始化副作用");
    }
    private sealed class EmptyRecords : ICodexActivitySource
    {
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> ids) => new Dictionary<string, CodexActivity>();
    }
    private sealed class LocalCodex : ICodexSetupEnvironment
    {
        public Task<CodexInstallation> Discover(string desktop, string cli) => Task.FromResult(new CodexInstallation("fixture-codex.exe", "", "fixture", [], ""));
        public Task<CodexConnectionCheck> Check() => Task.FromResult(new CodexConnectionCheck(true, true, "local-records-ready"));
        public bool CanOpenDesktop(string desktop) => true;
        public bool DesktopRunning(string desktop) => true;
        public Task OpenDesktop(string desktop, string inspector) => throw new Exception("不能打开桌面");
    }
    private sealed class DshEnvironment(AiPlatformOptions options) : IAiListeningEnvironment
    {
        internal int Probes, Initializations;
        public Task<AiConnectionProbe> Probe(AiPlatformOptions value, CancellationToken cancellation)
        { Probes++; return Task.FromResult(new AiConnectionProbe(options with { Home = value.Home }, new(AiConnectionState.Ready, "隔离就绪", "", Installed: true, Catalog: true, Configured: true))); }
        public void Initialize(AiPlatformOptions value) => Initializations++;
        public bool Running(string executable) => true;
        public void Open(string executable) => throw new Exception("不能打开桌面");
    }
}
