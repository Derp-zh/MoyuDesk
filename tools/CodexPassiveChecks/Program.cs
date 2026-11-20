using System.Reflection;
using System.Text.Json;
using Taskboard.Desktop;

if (args.Length != 3 || args[0] != "--data-dir") throw new ArgumentException("必须指定新的隔离--data-dir及--daily/--behavior");
var root = Path.GetFullPath(args[1]);
if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
    throw new ArgumentException("必须使用test-output下新的隔离目录");
Directory.CreateDirectory(root);
var production = typeof(CodexObserver).Assembly;
var forbidden = new[] { "CodexRpc", "CodexDesktopBridge", "CodexPendingBridge", "CodexLocalEvents", "CodexLocalState", "CodexReadBudget", "CodexFollowPolicy", "CodexTurnTracker", "CodexWindowsNotifications" };
var leaked = production.GetTypes().Where(t => forbidden.Contains(t.Name) || t.Name.EndsWith("Checks")).Select(t => t.FullName).ToArray();
if (leaked.Length != 0) throw new Exception("生产程序集残留退役/验证代码：" + string.Join(",", leaked));
File.WriteAllText(Path.Combine(root, "production-boundary.json"), JsonSerializer.Serialize(new { passed = true, production = production.Location, forbidden, legacyTypes = leaked, fixture = true, originalDesktop = false }));
if (args[2] == "--daily") { if (!await DailyChecks.Run(root)) Environment.ExitCode = 1; }
else if (args[2] == "--behavior") await BehaviorChecks.Run(root);
else throw new ArgumentException("未知检查模式");
