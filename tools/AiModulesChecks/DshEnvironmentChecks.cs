using System.IO;
using System.Text.Json;
using System.Text;
using Taskboard.Core;
using Taskboard.Desktop;

// Read-only environment inspection. Never constructs a controller/store, opens
// the DSH window, reads conversation bodies, or writes application settings.
internal static class DshEnvironmentChecks
{
    internal static async Task Inspect(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("需使用新的test-output隔离目录");
        Directory.CreateDirectory(root);
        var saved = ReadSavedPaths();
        var environment = new DshListeningEnvironment();
        var defaults = await environment.Probe(AiPlatformOptions.From(AiPlatform.Dsh, new()), CancellationToken.None);
        var configured = await environment.Probe(AiPlatformOptions.From(AiPlatform.Dsh, saved), CancellationToken.None);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        AiConnectionProbe noPath;
        string? observerRuntime;
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            noPath = await environment.Probe(AiPlatformOptions.From(AiPlatform.Dsh, saved), CancellationToken.None);
            observerRuntime = DshProcess.NodePath();
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
        object Report(AiConnectionProbe value) => new
        {
            state = value.Report.State.ToString(), value.Report.Message, value.Report.Next, value.Report.Diagnostic,
            value.Report.Installed, value.Report.Running, value.Report.RunningKnown,
            value.Report.Catalog, value.Report.Configured, value.Report.Sessions,
            paths = new { value.Options.Desktop, value.Options.Home, value.Options.Node }
        };
        var report = new
        {
            inspectedAtUtc = DateTime.UtcNow,
            scope = "Read-only native environment and structural catalog; no original conversation IDs, titles, bodies, credentials, UI, or external configuration writes",
            defaults = Report(defaults), configured = Report(configured), withoutInheritedPath = Report(noPath),
            readerPresent = File.Exists(Path.Combine(AppContext.BaseDirectory, "integrations", "dsh-observer.mjs")),
            savedPathKeys = saved.Keys.ToArray(),
            environmentDshHomeSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DSH_HOME")),
            installedPathNode = DshProcess.NodePath(), observerRuntimeWithoutInheritedPath = observerRuntime
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "dsh-environment.json"), json);
        Console.WriteLine(json);
    }

    internal static async Task VerifyRuntime(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("需使用新的test-output隔离目录");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var desktop = AiDesktopDiscovery.Find(AiPlatform.Dsh, "");
        var node = DshProcess.NodePath(desktop) ?? throw new IOException("此本机环境检查需要已有DSH/Node24，不会下载安装");
        Check(AiDesktopDiscovery.Find(AiPlatform.Dsh, Path.Combine(root, "removed-dsh.exe")) == desktop, "DSH自动记录的旧安装路径失效后重新发现当前已安装程序");
        var customDesktop = Path.Combine(root, "custom-dsh.exe"); File.WriteAllBytes(customDesktop, [(byte)'M', (byte)'Z']);
        Check(AiDesktopDiscovery.Find(AiPlatform.Dsh, customDesktop) == customDesktop, "有效手动DSH程序路径保持");
        var manual = await DshProcess.FindNode(node, desktop, CancellationToken.None);
        Check(string.Equals(manual.Path, node, StringComparison.OrdinalIgnoreCase), "有效手动Node24经真实读取组件自检后保留");
        var repaired = await DshProcess.FindNode(Path.Combine(root, "missing-node.exe"), desktop, CancellationToken.None);
        Check(repaired.Path is not null && repaired.Diagnostic.Length > 0, "失效读取运行时自动恢复为已有DSH自带Node并给出诊断");
        var invalid = Path.Combine(root, "node.exe"); File.WriteAllBytes(invalid, [(byte)'M', (byte)'Z']);
        Check(!DshProcess.SupportedNode(invalid), "同名MZ文件不冒充受支持Node24");
        repaired = await DshProcess.FindNode(invalid, desktop, CancellationToken.None);
        Check(repaired.Path is not null && repaired.Path != invalid, "版本不符或损坏的手动运行时不阻断自动发现");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalHome = Environment.GetEnvironmentVariable("DSH_HOME");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable("DSH_HOME", " ");
            Check(AiPaths.DshHome == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"), "空白DSH_HOME回到原生默认目录");
            var resolved = DshProcess.NodePath(desktop);
            Check(resolved is not null && DshProcess.SupportedNode(resolved), "未初始化观察器在无继承PATH时同样能发现DSH自带Node");
            var probe = await new DshListeningEnvironment().Probe(AiPlatformOptions.From(AiPlatform.Dsh, new()), CancellationToken.None);
            Check(probe.Report.Catalog && probe.Report.Configured && probe.Report.Installed, "实际DSH在无继承PATH时完成目录与读取组件检查");
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); Environment.SetEnvironmentVariable("DSH_HOME", originalHome); }
        var customHome = Path.Combine(root, "custom-home"); Directory.CreateDirectory(Path.Combine(customHome, "sessions"));
        var custom = await new DshListeningEnvironment().Probe(new(AiPlatform.Dsh, desktop, customHome, node, ""), CancellationToken.None);
        Check(custom.Options.Home == customHome && custom.Report.Catalog && custom.Report.Sessions == 0, "有效手动DSH目录保持且空目录不误报已有会话");
        var unicodeHome = Path.Combine(root, "unicode-home");
        var unicodeSession = Path.Combine(unicodeHome, "sessions", "project", "unicode-native"); Directory.CreateDirectory(unicodeSession);
        File.WriteAllText(Path.Combine(unicodeSession, "session.v4.jsonl"), JsonSerializer.Serialize(new { type = "session", version = 4, id = "unicode-native", createdAt = 1 }) + "\n");
        var projection = Path.Combine(unicodeHome, "storages", "session_projcache", "sessions"); Directory.CreateDirectory(projection);
        const string unicodeTitle = "中文会话：路径“测试”\\中文😀";
        File.WriteAllText(Path.Combine(projection, "unicode-native.json"), JsonSerializer.Serialize(new { record = new { rows = new { title = new { val = unicodeTitle } } } }));
        var previousEncoding = Console.OutputEncoding;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = Encoding.GetEncoding(936);
            using var unicodeCatalog = JsonDocument.Parse(await DshProcess.Run(["--catalog", unicodeHome], CancellationToken.None, node));
            Check(unicodeCatalog.RootElement[0].GetProperty("name").GetString() == unicodeTitle,
                "Windows中文代码页启动时，DSH中文/引号/反斜杠/表情标题仍按UTF8读取且编号准确");
        }
        finally { Console.OutputEncoding = previousEncoding; }
        var cacheDirectory = Path.Combine(root, "runtime-cache"); Directory.CreateDirectory(cacheDirectory);
        var copy = Path.Combine(cacheDirectory, "node.exe"); File.Copy(node, copy);
        Check(DshProcess.SupportedNode(copy) && DshProcess.SupportedNode(copy), "同一本机运行时重复版本验证可复用正缓存");
        using (var changed = new FileStream(copy, FileMode.Open, FileAccess.Write)) changed.SetLength(128);
        Check(!DshProcess.SupportedNode(copy), "运行时保持MZ头但文件截断后，旧版本缓存立即失效");
        File.Copy(node, copy, true); Check(DshProcess.SupportedNode(copy), "修复后的同路径运行时可以重新验证");
        var written = File.GetLastWriteTimeUtc(copy);
        using (var changed = new FileStream(copy, FileMode.Open, FileAccess.Write)) changed.Write([0, 0]);
        File.SetLastWriteTimeUtc(copy, written);
        Check(!DshProcess.SupportedNode(copy), "运行时大小时间未变也不能跳过当前MZ头检查");
        File.Delete(copy); Check(!DshProcess.SupportedNode(copy), "运行时已删除时不得沿用缓存");
        var result = new { passed = true, scope = "Isolated paths and real installed runtime/component self-check; no conversations, UI or configuration writes", checks };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "runtime-checks.json"), json); Console.WriteLine(json);
    }

    private static Dictionary<string, string> ReadSavedPaths()
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(DataPaths.SelectionFile)) return result;
        using var selection = JsonDocument.Parse(File.ReadAllText(DataPaths.SelectionFile));
        if (!selection.RootElement.TryGetProperty("ConfigurationDirectory", out var directory) || directory.ValueKind != JsonValueKind.String) return result;
        var parent = Path.GetDirectoryName(DataPaths.SelectionFile)!;
        var settings = Path.Combine(Path.GetFullPath(directory.GetString()!, parent), "settings.json");
        if (!File.Exists(settings)) return result;
        using var document = JsonDocument.Parse(File.ReadAllText(settings));
        if (!document.RootElement.TryGetProperty("Preferences", out var preferences) ||
            !preferences.TryGetProperty("ConnectionConfigs", out var configurations)) return result;
        foreach (var key in new[] { "dsh.desktop", "dsh.home", "dsh.node" })
            if (configurations.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) result[key] = value.GetString()!;
        return result;
    }
}
