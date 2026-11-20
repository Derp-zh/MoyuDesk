using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;

if (args.Length != 2) throw new ArgumentException("Use <package-directory> <report-file>");
var root = Path.GetFullPath(args[0]);
var sourceVersion = XDocument.Load("src/Taskboard.Desktop/Taskboard.Desktop.csproj").Descendants("Version").Single().Value;
var expectedVersion = Version.Parse(sourceVersion + ".0");
var forbidden = new HashSet<string>(new[] { "CodexRpc", "CodexDesktopBridge", "CodexPendingBridge", "CodexLocalEvents", "CodexLocalState", "CodexReadBudget", "CodexFollowPolicy", "CodexTurnTracker", "CodexWindowsNotifications", "CodexNotificationRules" });
var rows = new List<object>();
foreach (var name in new[] { "Taskboard.dll", "Taskboard.Core.dll" })
{
    using var file = File.OpenRead(Path.Combine(root, name)); using var pe = new PEReader(file);
    var reader = pe.GetMetadataReader();
    var types = reader.TypeDefinitions.Select(h => reader.GetString(reader.GetTypeDefinition(h).Name)).ToArray();
    var invalid = types.Where(t => forbidden.Contains(t) || t.EndsWith("Checks") || t == "DailySoak").ToArray();
    if (invalid.Length > 0) throw new Exception("Retired/test types in package: " + string.Join(",", invalid));
    if (reader.MethodDefinitions.Any(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "PrepareConversationPreview"))
        throw new Exception("Retired preview fixture in production");
    var resources = reader.ManifestResources.Select(h => reader.GetString(reader.GetManifestResource(h).Name)).ToArray();
    if (name == "Taskboard.dll" && (!resources.Contains("Taskboard.DefaultNotification.wav") || !resources.Contains("Taskboard.g.resources")))
        throw new Exception("Missing embedded sound or WPF resources");
    if (name == "Taskboard.dll" && reader.GetAssemblyDefinition().Version != expectedVersion) throw new Exception("Unexpected assembly version");
    rows.Add(new { name, types = types.Length, resources });
}
foreach (var required in new[] { "MoyuDesk.exe", "Taskboard.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "PresentationFramework.dll", "integrations/dsh-observer.mjs", "使用说明.md", "版本说明.md", "THIRD-PARTY-NOTICES.txt" })
    if (!File.Exists(Path.Combine(root,required))) throw new Exception("Incomplete portable package: " + required);
var entry = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(root, "MoyuDesk.exe"));
if (entry.ProductName != "摸鱼台 · MoyuDesk" || entry.FileDescription != entry.ProductName || entry.ProductVersion != sourceVersion)
    throw new Exception("Unexpected portable launcher name or version");
var unexpected = Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Where(f => new[] { ".cs", ".ps1", ".py", ".csproj", ".zst" }.Contains(Path.GetExtension(f))).ToArray();
if (unexpected.Length != 0) throw new Exception("Source or historical tooling leaked into package");
var fileHashes = Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
    .ToDictionary(f => Path.GetRelativePath(root,f).Replace('\\','/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
File.WriteAllText(args[1], JsonSerializer.Serialize(new { passed = true, root, sourceVersion, assemblies = rows, files = fileHashes.Count, fileHashes, originalDesktop = false, scope = "Actual PE metadata, resources and portable runtime; no UI launch" },new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Portable assembly, resource and runtime checks passed.");
