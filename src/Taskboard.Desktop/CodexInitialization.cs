using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal enum CodexSetupState { NotStarted, Finding, Checking, NotFound, InvalidPath, MissingCli, Incompatible, NeedsOpen, NeedsReopen, ConnectionUnavailable, CatalogUnavailable, ExistingService, Ready, Failed }
internal sealed record CodexSetupReport(CodexSetupState State, string Message, string Next, string Diagnostic = "", bool Catalog = false, bool SourceReady = false, bool DesktopConnected = false)
{
    internal static CodexSetupReport Initial => new(CodexSetupState.NotStarted, "尚未初始化", "点击一键初始化，自动寻找并检查 Codex。");
}
internal sealed record CodexConnectionCheck(bool Catalog, bool Records, string Reason);
internal sealed record CodexSetupOutcome(CodexSetupReport Report, Dictionary<string, string> Config);

internal interface ICodexSetupEnvironment
{
    Task<CodexInstallation> Discover(string desktop, string cli);
    bool CanOpenDesktop(string desktop);
    Task<CodexConnectionCheck> Check();
    bool DesktopRunning(string desktop);
    Task OpenDesktop(string desktop, string inspector);
}

// No UI, chat binding, task mutation, or desktop lifecycle operation in this flow.
// OpenDesktop is a separate action invoked only after an explicit UI confirmation.
internal sealed class CodexInitialization(ICodexSetupEnvironment environment)
{
    internal const string InspectorKey = "codex.inspector";
    internal const string DesktopKey = "codex.desktop";
    internal const string CliKey = "codex.executable";
    internal const string PromptKey = "ai.initialization.prompt";
    internal static bool ShouldPrompt(Preferences preferences) => !preferences.ConnectionConfigs.ContainsKey(PromptKey);
    internal static void RememberPrompt(Preferences preferences, string choice) => preferences.ConnectionConfigs[PromptKey] = choice;
    internal static string Inspector(Dictionary<string, string> config) => config.GetValueOrDefault(InspectorKey,
        Environment.GetEnvironmentVariable("TASKBOARD_CODEX_INSPECTOR") ?? "");

    internal async Task<CodexSetupOutcome> Run(Dictionary<string, string> original, Action<CodexSetupReport>? progress = null)
    {
        var config = new Dictionary<string, string>(original);
        CodexSetupOutcome Result(CodexSetupReport report) { progress?.Invoke(report); return new(report, config); }
        try
        {
            progress?.Invoke(new(CodexSetupState.Finding, "正在寻找 Codex", "正在检查本机已安装的 Codex。"));
            var desktop = config.GetValueOrDefault(DesktopKey, ""); var cli = config.GetValueOrDefault(CliKey, "");
            var install = await environment.Discover(desktop, cli);
            bool Changed(string before, string after) => before.Length > 0 && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
            bool CanRepair(string before, string after) => install.RegisteredDesktops.Contains(install.Desktop, StringComparer.OrdinalIgnoreCase) && CodexInstallationDiscovery.ReplacedStorePath(before, after);
            if (Changed(desktop, install.Desktop) && !CanRepair(desktop, install.Desktop))
                return Result(new(CodexSetupState.InvalidPath, "需要修复 Codex 位置", "请在“连接诊断”中重新选择 Codex 桌面程序。原配置已保留。", install.Diagnostic));
            if (Changed(desktop, install.Desktop)) config[DesktopKey + ".previous"] = desktop;
            var useDiscoveredCli = cli.Length == 0 || !Changed(cli, install.Cli) || CanRepair(cli, install.Cli);
            if (Changed(cli, install.Cli) && useDiscoveredCli) config[CliKey + ".previous"] = cli;
            if (install.Desktop.Length == 0)
                return Result(new(CodexSetupState.NotFound, "未找到 Codex，请手动选择", "使用“手动选择桌面程序”，选择已安装 Codex 的桌面程序。", install.Diagnostic));
            config[DesktopKey] = install.Desktop;
            if (install.Cli.Length > 0 && useDiscoveredCli) config[CliKey] = install.Cli;
            progress?.Invoke(new(CodexSetupState.Checking, "正在检查本机记录", "分别检查聊天目录和已有结构记录，不操作聊天。"));
            var check = await environment.Check();
            var diagnostics = install.Diagnostic + "\n桌面版本：" + install.Version + "\n" + check.Reason;
            if (!check.Catalog)
                return Result(new(CodexSetupState.CatalogUnavailable, "本地聊天目录暂不可用", "请展开诊断检查本机目录、路径和数据结构；不会联网补取聊天。", diagnostics));
            if (!check.Records)
                return Result(new(CodexSetupState.ConnectionUnavailable, "本机记录读取暂不可用", "看板会自动重试，也可重新检查。目录可读不代表记录与具体聊天已验证。", diagnostics, true));
            return Result(new(CodexSetupState.Ready, "本机记录监听已准备好", "启动后自动监听，无需重复初始化。在任务阶段关联已有聊天；具体聊天的提问、批准和恢复仍需分别确认。", diagnostics, true, true, false));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        { return Result(new(CodexSetupState.Failed, "初始化未完成", "请重新检查；仍失败时展开诊断，修复路径或连接配置。", e.GetType().Name + ": " + e.Message)); }
    }
}

internal sealed class CodexSetupEnvironment : ICodexSetupEnvironment
{
    // Describe only a snapshot already held by the normal observer. The settings
    // test must never create a connection, register a follower or reset idle time.
    internal static string DescribeSnapshot(CodexDesktopSnapshot snapshot, int index)
    {
        var prefix = $"聊天 {index + 1}：";
        if (!snapshot.LiveCapable) return prefix + "状态待确认，保留原有提醒；本次未额外连接聊天。";
        var type = CodexState.Text(snapshot.Status, "type");
        if (type == "idle") return prefix + "最近确认为空闲；提问、批准和恢复尚需分别验证。";
        if (type != "active") return prefix + "状态待确认，保留原有提醒。";
        var version = CodexState.Object(snapshot.Status, "pendingSourceVersion");
        var flags = CodexState.Object(snapshot.Status, "activeFlags");
        var flagsValid = flags.ValueKind == System.Text.Json.JsonValueKind.Array && flags.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.String);
        var values = flagsValid ? flags.EnumerateArray().Select(x => x.GetString()).ToArray() : [];
        var requests = CodexState.Object(snapshot.Status, "pendingRequests");
        var overflow = CodexState.Object(snapshot.Status, "pendingOverflow");
        var requestSet = requests.ValueKind == System.Text.Json.JsonValueKind.Array ? requests.EnumerateArray().ToArray() : [];
        var running = snapshot.Turns.Where(x => x.Status == "inProgress" && x.Id.Length > 0).Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();
        var complete = version.ValueKind == System.Text.Json.JsonValueKind.Number && version.TryGetInt32(out var value) && value == 1 &&
            flagsValid && running.Length == 1 && CodexState.Text(snapshot.Status, "pendingTurnId") == running[0] &&
            requests.ValueKind == System.Text.Json.JsonValueKind.Array && requestSet.Length <= 128 &&
            overflow.ValueKind == System.Text.Json.JsonValueKind.False && requestSet.All(x => CodexState.Text(x, "id").Length is > 0 and <= 256 &&
                CodexState.Object(x, "approval").ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) &&
            requestSet.Select(x => CodexState.Text(x, "id")).Distinct(StringComparer.Ordinal).Count() == requestSet.Length;
        var status = values.Contains("waitingOnApproval") ? "最近确认在等待批准" : values.Contains("waitingOnUserInput") ? "最近确认在等待回答" :
            complete && values.Length == 0 && requestSet.Length == 0 ? "最近确认正在执行" : "执行或等待状态仍待确认";
        return prefix + status + (complete ? "；本轮请求信息可读。" : "；提问／批准信息尚未确认完整。");
    }
    public Task<CodexInstallation> Discover(string desktop, string cli) => CodexInstallationDiscovery.Find(desktop, cli, inspectCli: false);
    public bool CanOpenDesktop(string desktop) => CodexInstallationDiscovery.TrustedDesktop(desktop);
    public async Task<CodexConnectionCheck> Check()
    {
        var catalog = false; var reason = "";
        async Task<bool> Catalog()
        {
            try
            {
                _ = await CodexLocalCatalog.Load().ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or System.Net.WebSockets.WebSocketException or System.ComponentModel.Win32Exception)
            { reason += "\ncatalog-unavailable-" + e.GetType().Name; return false; }
        }
        catalog = await Catalog().ConfigureAwait(false);
        reason += "\nlocal-sqlite-catalog; no-app-server; no-desktop-pipe; existing-endpoint-preserved-unused" +
            "\n状态读取本机记录，不订阅聊天。等待提问、批准和恢复读取客户端已有行为记录；目录可读不代表具体会话已验证。";
        if (catalog) reason += "\nlocal-records-ready";
        return new(catalog, catalog, reason);
    }
    public bool DesktopRunning(string desktop)
    {
        // Conservative: if a same-named process cannot be inspected, don't launch another.
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(desktop)))
            using (process) if (!process.HasExited) return true;
        return false;
    }
    public Task OpenDesktop(string desktop, string inspector)
    {
        if (!CanOpenDesktop(desktop)) throw new InvalidOperationException("所选程序未通过 Codex 系统登记与签名检查，请重新选择已安装的 Codex。");
        if (DesktopRunning(desktop)) throw new InvalidOperationException("Codex 仍在运行。请先保存工作并自行退出，再重试；看板不会关闭它。");
        var start = new ProcessStartInfo(desktop) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(desktop)!, WindowStyle = ProcessWindowStyle.Hidden };
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }
}
