using System.Text.Json;
using Taskboard.Desktop;

if (args.Length < 2 || args[0] != "--data-dir") throw new ArgumentException("Use --data-dir <fresh test-output directory>");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var output = Path.GetFullPath(args[1]);
if (!output.StartsWith(Path.Combine(root, "test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
    || Directory.Exists(output)) throw new InvalidOperationException("Use a fresh isolated test-output directory");
Directory.CreateDirectory(output);
if (args.Contains("--native-read-only")) return await NativeTargetChecks.Run(output);
if (args.Contains("--batch1-ui")) return Batch1UiChecks.Run(output);
if (args.Contains("--batch2-ui")) return Batch2UiChecks.Run(output);
if (args.Contains("--batch3-surface")) return Batch3SurfaceChecks.Run(output);
if (args.Contains("--profile-20")) return Batch3SurfaceChecks.RunProfile20(output);
var checks = new List<object>();
foreach (var mode in new[] { "still", "move5", "move200", "submenu", "duplicate", "preexisting-menu", "clipboard-before",
    "foreign-copy", "invalid-link", "invoke-acts-then-throws", "cancel-before-open", "cancel-on-open", "cancel-after-copy",
    "foreground-change", "target-change", "new-copy-after-read", "restore-failure", "cancel-restore-failure" })
{
    using var stop = new CancellationTokenSource();
    var clipboard = new FakeClipboard(mode);
    var menu = new FakeMenu(mode, clipboard, stop);
    var target = ConversationLinkResolver.Run(menu, clipboard, stop.Token);
    var expected = mode is "still" or "move5" or "move200" or "submenu" or "new-copy-after-read" or "restore-failure";
    if (target.CanBind != expected) throw new Exception(mode + ": unexpected bind result: " + target.Reason);
    if (menu.Invocations > 1) throw new Exception(mode + ": duplicate copy action");
    if (target.CanBind && target.ThreadId != FakeMenu.TargetId) throw new Exception(mode + ": wrong target identity");
    if (mode is "move5" or "move200" && menu.Pointer == 0) throw new Exception("movement trigger did not execute");
    if (mode is "foreign-copy" or "new-copy-after-read" && clipboard.OverwriteForeign) throw new Exception("overwrote a user's new copy");
    if (mode is "invoke-acts-then-throws" or "cancel-after-copy" && !clipboard.Restored) throw new Exception("copy side effect was not restored");
    if (mode == "restore-failure" && target.ClipboardRestored != false) throw new Exception("restore failure was hidden");
    if (mode == "cancel-restore-failure" && (target.ClipboardRestored != false || !target.Reason.Contains("剪贴板未确认恢复")))
        throw new Exception("unbound clipboard restore failure was hidden from the user");
    checks.Add(new { mode, bound = target.CanBind, menu.Invocations, clipboard.Restored, target.ClipboardRestored, simulatedPointer = menu.Pointer });
}
var report = new { passed = checks.Count, checks, nativeDesktopInput = false, clipboardAccess = false,
    scope = "Actual production ConversationLinkResolver transaction with controlled menu/clipboard adapters; not real UIA/menu or physical drag" };
File.WriteAllText(Path.Combine(output, "binding-transaction-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, nativeDesktopInput = false, clipboardAccess = false }));
return Batch2UiChecks.Run(Path.Combine(output, "batch2-ui"), () => Batch3SurfaceChecks.RunInlineAsync(Path.Combine(output, "batch3-surface")));

sealed class FakeClipboard(string mode) : IConversationLinkClipboard
{
    public uint Sequence { get; set; } = 1;
    public string Text = "original";
    public bool Foreign, Restored, OverwriteForeign;
    private int reads;
    public uint Capture() => Sequence;
    public ConversationClipboardRead Read()
    {
        if (mode == "foreign-copy") { Sequence++; Text = "user new copy"; Foreign = true; }
        var value = new ConversationClipboardRead(Sequence, Text, !Foreign);
        if (mode == "new-copy-after-read" && ++reads == 1) { Sequence++; Text = "user copy after read"; Foreign = true; }
        return value;
    }
    public bool Restore(uint expected)
    {
        if (Sequence != expected) return false;
        if (Foreign) OverwriteForeign = true;
        if (mode is "restore-failure" or "cancel-restore-failure") return false;
        Text = "original"; Restored = true; Sequence++; return true;
    }
    public void Dispose() { }
}
sealed class FakeMenu(string mode, FakeClipboard clipboard, CancellationTokenSource stop) : IConversationLinkMenu
{
    internal const string TargetId = "11111111-1111-4111-8111-111111111111";
    public int Invocations, Pointer;
    private bool opened, expanded;
    public ConversationDropTarget Target => new(null, "original target", "", true);
    public bool Valid => !(opened && mode == "target-change");
    public bool ForegroundOwned => mode != "foreground-change";
    public ConversationMenuCommand[] Commands()
    {
        if (mode == "cancel-before-open") stop.Cancel();
        if (!opened) return mode == "preexisting-menu" ? [new("old", "Copy")] : [];
        if (mode == "move5") Pointer = 5;
        if (mode == "move200") Pointer = 200;
        if (mode == "clipboard-before") clipboard.Sequence++;
        if (mode == "submenu" && !expanded) return [new("copy-menu", "Copy")];
        return mode == "duplicate" ? [new("a", "Copy deeplink"), new("b", "Copy deeplink")] : [new("target-action", "Copy deeplink")];
    }
    public void Open() { opened = true; if (mode == "cancel-on-open") stop.Cancel(); }
    public void Expand(ConversationMenuCommand command) => expanded = true;
    public void Invoke(ConversationMenuCommand command)
    {
        if (!Equals(command.Handle, "target-action")) throw new Exception("wrong menu action");
        Invocations++; clipboard.Text = mode == "invalid-link" ? "https://example.com" : "codex://threads/" + TargetId; clipboard.Sequence++;
        if (mode == "invoke-acts-then-throws") throw new InvalidOperationException("invoked but COM reported failure");
        if (mode is "cancel-after-copy" or "cancel-restore-failure") stop.Cancel();
    }
    public void CloseOwnedMenu() { }
    public void Dispose() { }
}
