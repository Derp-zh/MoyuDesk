using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal enum AiConnectionState { NotChecked, Checking, NotInstalled, InvalidPath, MissingRuntime, NeedsInitialization, NeedsOpen, Ready, Failed }
internal enum AiProcessPresence { NotRunning, Running, Unknown }
internal sealed record AiConnectionReport(AiConnectionState State, string Message, string Next, string Diagnostic = "",
    bool Installed = false, bool Running = false, bool Catalog = false, bool Configured = false, int Sessions = 0, bool RunningKnown = true)
{
    internal static AiConnectionReport Initial => new(AiConnectionState.NotChecked, "尚未检查连接", "点击一键初始化，自动寻找并配置此平台监听。");
}

internal sealed record AiPlatformOptions(AiPlatform Platform, string Desktop, string Home, string Node, string Events)
{
    internal string Prefix => Platform == AiPlatform.Dsh ? "dsh." : "claude.";
    internal static AiPlatformOptions From(AiPlatform platform, Dictionary<string, string> config) => platform switch
    {
        AiPlatform.Dsh => new(platform, config.GetValueOrDefault("dsh.desktop", ""), Value(config, "dsh.home", AiPaths.DshHome), config.GetValueOrDefault("dsh.node", ""), ""),
        AiPlatform.ClaudeCode => new(platform, config.GetValueOrDefault("claude.desktop", ""), Value(config, "claude.home", AiPaths.ClaudeHome), "", Value(config, "claude.events", AiPaths.Events)),
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };
    private static string Value(Dictionary<string, string> config, string key, string fallback) => config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    internal Dictionary<string, string> Values()
    {
        var values = new Dictionary<string, string> { [Prefix + "desktop"] = Desktop, [Prefix + "home"] = Home };
        if (Platform == AiPlatform.Dsh) values[Prefix + "node"] = Node; else values[Prefix + "events"] = Events;
        return values;
    }
    internal static bool SameConfiguration(AiPlatform platform, Dictionary<string, string> first, Dictionary<string, string> second)
    {
        var prefix = platform == AiPlatform.Dsh ? "dsh." : "claude.";
        return first.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(x => x.Key).SequenceEqual(
            second.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(x => x.Key));
    }
}

internal sealed record AiConnectionProbe(AiPlatformOptions Options, AiConnectionReport Report);
internal interface IAiListeningEnvironment
{
    Task<AiConnectionProbe> Probe(AiPlatformOptions options, CancellationToken cancellation);
    void Initialize(AiPlatformOptions options);
    bool Running(string executable);
    void Open(string executable);
}

internal static class AiDesktopDiscovery
{
    internal static string Find(AiPlatform platform, string manual)
    {
        var invalidManual = false;
        if (manual.Length > 0)
        {
            try
            {
                var selected = CodexLocalCatalog.LocalPath(manual);
                if (Executable(selected)) return selected;
            }
            catch (Exception e) when (platform == AiPlatform.Dsh && e is IOException or ArgumentException or UnauthorizedAccessException) { }
            if (platform != AiPlatform.Dsh) throw new IOException("桌面程序路径无效，请重新选择已安装的程序。");
            invalidManual = true;
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = platform == AiPlatform.Dsh
            ? new[] { Path.Combine(program, "DSH Desktop", "DSH Desktop.exe"), Path.Combine(local, "Programs", "DSH Desktop", "DSH Desktop.exe") }
            : new[] { Path.Combine(local, "AnthropicClaude", "claude.exe"), Path.Combine(local, "Programs", "Claude", "Claude.exe"), Path.Combine(program, "Claude", "Claude.exe") };
        var registryName = platform == AiPlatform.Dsh ? "DSH Desktop.exe" : "claude.exe";
        var paths = candidates.Concat(AppPaths(registryName));
        if (platform == AiPlatform.ClaudeCode) paths = paths.Concat(ClaudePackages());
        foreach (var path in paths) if (Executable(path)) return Path.GetFullPath(path);
        // A currently running, inspected image is also an exact installed location.
        foreach (var name in platform == AiPlatform.Dsh ? new[] { "DSH Desktop" } : new[] { "Claude", "claude" })
        foreach (var process in Process.GetProcessesByName(name))
            using (process) try { var image = process.MainModule?.FileName; if (image is not null && Executable(image)) return image; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        if (invalidManual) throw new IOException("原 DSH 程序位置已失效，尚未在本机找到可用安装；请选择已有的 DSH 桌面程序。");
        return "";
    }
    private static string[] ClaudePackages()
    {
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            return manager.FindPackagesForUser("").Where(x => string.Equals(x.Id.Name, "Claude", StringComparison.Ordinal))
                .Select(x => Path.Combine(x.InstalledLocation.Path, "app", "Claude.exe")).ToArray();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { return []; }
    }
    private static IEnumerable<string> AppPaths(string name)
    {
        var found = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try { using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name); if (key?.GetValue("") is string path) found.Add(path.Trim('"')); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return found;
    }
    internal static bool Executable(string path)
    {
        try
        {
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(CodexLocalCatalog.LocalPath(path))) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return file.ReadByte() == 'M' && file.ReadByte() == 'Z';
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    internal static AiProcessPresence Presence(string executable)
    {
        if (executable.Length == 0) return AiProcessPresence.NotRunning;
        var unknown = false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            using (process) try
            {
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return AiProcessPresence.Running;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { unknown = true; }
        return unknown ? AiProcessPresence.Unknown : AiProcessPresence.NotRunning;
    }
    internal static bool Running(string executable) => Presence(executable) != AiProcessPresence.NotRunning;
    internal static void Open(string executable)
    {
        if (!Executable(executable)) throw new IOException("所选桌面程序不可用，请重新检查路径。");
        if (Running(executable)) return;
        // Only called from the explicit Open button, never from checks or initialization.
        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!, WindowStyle = ProcessWindowStyle.Normal });
    }
}

internal abstract class AiListeningEnvironment : IAiListeningEnvironment
{
    public bool Running(string executable) => AiDesktopDiscovery.Running(executable);
    public void Open(string executable) => AiDesktopDiscovery.Open(executable);
    public abstract Task<AiConnectionProbe> Probe(AiPlatformOptions options, CancellationToken cancellation);
    public virtual void Initialize(AiPlatformOptions options) { }
    protected static AiConnectionProbe Failure(AiPlatformOptions options, Exception error) => new(options,
        new(AiConnectionState.Failed, "本次连接检查未完成", "请展开手动修复与诊断，检查路径后重试。", error.GetType().Name + "：" + error.Message));
}

internal sealed class DshListeningEnvironment : AiListeningEnvironment
{
    public override async Task<AiConnectionProbe> Probe(AiPlatformOptions options, CancellationToken cancellation)
    {
        var found = options;
        var installed = false;
        var presence = AiProcessPresence.NotRunning;
        try
        {
            var desktop = AiDesktopDiscovery.Find(AiPlatform.Dsh, options.Desktop);
            if (desktop.Length == 0) return new(options, new(AiConnectionState.NotInstalled, "未找到 DSH", "请选择已有的 DSH 桌面程序；看板不会安装 DSH。"));
            installed = true; found = options with { Desktop = desktop };
            presence = AiDesktopDiscovery.Presence(desktop);
            var home = CodexLocalCatalog.LocalPath(options.Home);
            var runtime = await DshProcess.FindNode(options.Node, desktop, cancellation).ConfigureAwait(false);
            var node = runtime.Path ?? "";
            found = options with { Desktop = desktop, Home = home, Node = node }; var running = presence == AiProcessPresence.Running;
            if (node.Length == 0) return new(found, new(AiConnectionState.MissingRuntime, "未找到可用的本机读取运行时", "已检查 DSH 自带运行时和本机已安装 Node.js；可在手动修复中选择 Node.js 24 或更高版本的 node.exe。", runtime.Diagnostic, Installed: true, Running: running, RunningKnown: presence != AiProcessPresence.Unknown));
            if (!Directory.Exists(home)) return new(found, new(AiConnectionState.NeedsOpen, "DSH 本机目录尚未建立", "请正常打开 DSH 后重新检查，或选择已有本机目录。", Installed: true, Running: running, RunningKnown: presence != AiProcessPresence.Unknown));
            using var json = System.Text.Json.JsonDocument.Parse(await DshProcess.Run(["--catalog", home], cancellation, node).ConfigureAwait(false));
            var count = json.RootElement.GetArrayLength();
            return new(found, new(running ? AiConnectionState.Ready : AiConnectionState.NeedsOpen,
                running ? "DSH 监听已准备好" : presence == AiProcessPresence.Unknown ? "本机目录可读，程序运行情况待确认" : "本机目录可读，DSH 尚未运行",
                running ? "在任务阶段绑定已有 DSH 会话；可测试已关联会话的当前监测结果。" : presence == AiProcessPresence.Unknown ? "无法核实同名进程的位置。请确保两个程序使用相同用户和权限后重新检查。" : "打开 DSH 后会继续读取本机会话；无需重新初始化。",
                "原生 v4 结构日志及读取组件可用。只读本机，不依赖桌面测试桥。\n本机目录：" + home + "\n读取运行时：" + node + (runtime.Diagnostic.Length > 0 ? "\n" + runtime.Diagnostic : ""),
                true, running, true, true, count, presence != AiProcessPresence.Unknown));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { return new(found, new(AiConnectionState.Failed, installed ? "DSH 已找到，监听检查未完成" : "本次 DSH 检查未完成", "展开手动修复与诊断查看具体原因；已找到的程序与路径仍保留。", e.GetType().Name + "：" + e.Message, Installed: installed, Running: presence == AiProcessPresence.Running, RunningKnown: presence != AiProcessPresence.Unknown)); }
    }
}

internal sealed class ClaudeListeningEnvironment : AiListeningEnvironment
{
    public override Task<AiConnectionProbe> Probe(AiPlatformOptions options, CancellationToken cancellation) => Task.Run(() =>
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var desktop = AiDesktopDiscovery.Find(AiPlatform.ClaudeCode, options.Desktop);
            if (desktop.Length == 0) return new AiConnectionProbe(options, new(AiConnectionState.NotInstalled, "未找到 Claude", "此电脑尚未找到 Claude。已有安装时可手动选择程序；看板不会下载安装应用。"));
            var found = options with { Desktop = desktop, Home = CodexLocalCatalog.LocalPath(options.Home), Events = CodexLocalCatalog.LocalPath(options.Events) };
            var presence = AiDesktopDiscovery.Presence(desktop); var running = presence == AiProcessPresence.Running; var configured = ClaudeHook.Check(found.Home, found.Events, Environment.ProcessPath!, out var reason);
            var values = ClaudeCatalog.Load(found.Home, found.Events);
            return new AiConnectionProbe(found, new(!configured ? AiConnectionState.NeedsInitialization : !running ? AiConnectionState.NeedsOpen : AiConnectionState.Ready,
                !configured ? "Claude 监听需要初始化" : presence == AiProcessPresence.Unknown ? "监听已配置，程序运行情况待确认" : !running ? "监听已配置，Claude 尚未运行" : "Claude 监听已配置",
                !configured ? "点击一键初始化，备份并配置本机事件监听。" : running ? "新开或重载 Claude Code 会话后接收事件；可测试已关联会话。" : "打开 Claude 的 Code 会话后会自动接收事件。",
                reason + "\nClaude 本机目录：" + found.Home + "\n监听记录目录：" + found.Events,
                true, running, Directory.Exists(found.Home) || values.Length > 0, configured, values.Length, presence != AiProcessPresence.Unknown));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { return Failure(options, e); }
    }, cancellation);
    public override void Initialize(AiPlatformOptions options) => ClaudeHook.Install(options.Home, options.Events);
}

internal sealed class AiListeningModule(AiPlatform platform)
{
    internal AiPlatform Platform { get; } = platform;
    internal AiConnectionReport Report { get; set; } = AiConnectionReport.Initial;
    internal bool Busy { get; set; }
    internal string Test { get; set; } = "尚未测试已关联会话。";
    internal string TestScope { get; set; } = "";
    internal bool Tested { get; set; }
    internal AiPlatformObserver? Observer { get; set; }
    internal Dictionary<string, string> SourceConfiguration { get; set; } = new();
    internal Task<CodexThread[]>? Catalog { get; set; }
    internal DateTime CatalogAt { get; set; }
    internal long Revision { get; set; }
}
