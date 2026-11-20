using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using Taskboard.Desktop;

internal static class DshTargetChecks
{
    internal static async Task Probe(string root, int x, int y)
    {
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) throw new IOException("需新隔离目录");
        Directory.CreateDirectory(root);
        var point = new Point(x, y);
        var preview = DshConversationTarget.IsAt(point) ? await DshConversationTarget.Read(point) : await CodexConversationMenu.Read(point);
        var diagnostic = await ConversationAutomationWorker.Run(() =>
        {
            var items = new List<object>(); var depth = 0;
            for (var node = AutomationElement.FromPoint(new Point(x, y)); node is not null && depth++ < 40; node = TreeWalker.RawViewWalker.GetParent(node))
            {
                var value = node.Current; items.Add(new { role = value.ControlType.ProgrammaticName, cls = value.ClassName, bounds = value.BoundingRectangle.ToString() });
                if (value.ControlType == ControlType.Window)
                {
                    var regions = node.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group))
                        .Cast<AutomationElement>().Where(e => e.Current.ClassName.Contains("scrollBody") || e.Current.ClassName.Split(' ').Any(c => c.EndsWith("_body")))
                        .Select(e => new { cls = e.Current.ClassName, bounds = e.Current.BoundingRectangle.ToString(), offscreen = e.Current.IsOffscreen, identity = e.GetRuntimeId() }).ToArray();
                    items.Add(new { regions }); break;
                }
            }
            return ConversationDropTarget.Unavailable(JsonSerializer.Serialize(items));
        });
        File.WriteAllText(Path.Combine(root, "scope.json"), diagnostic.Reason);
        var original = ConversationClipboardThread.Run(() => new ConversationClipboard());
        var formatsRestored = false; var originalRestored = false;
        ConversationDropTarget target;
        try
        {
            target = DshConversationTarget.IsAt(point) ? await DshConversationTarget.Resolve(point, CancellationToken.None) : await CodexConversationMenu.Resolve(point);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (ConversationClipboardThread.Run(() => original.MatchesCurrent())) { formatsRestored = originalRestored = true; break; }
                await Task.Delay(20);
            }
        }
        finally
        {
            // The product transaction restores its owned copy. This probe only
            // verifies; it never seeds or performs a second clipboard write.
            ConversationClipboardThread.Run(() => { original.Dispose(); return true; });
        }
        string? catalogName = null;
        if (target.CanBind && target.Platform == Taskboard.Core.AiPlatform.Dsh)
        {
            using var observer = new AiPlatformObserver(onlyPlatform: Taskboard.Core.AiPlatform.Dsh);
            catalogName = (await observer.Catalog(Taskboard.Core.AiPlatform.Dsh)).Single(x => x.Id == target.ThreadId).DisplayName;
        }
        var json = JsonSerializer.Serialize(new { target.CanBind, target.ThreadId, target.Name, catalogName, previewName = preview.Name, preview.Reason, targetReason = target.Reason, target.Platform, target.Method, target.ClipboardRestored, formatsRestored, originalRestored, ConversationLinkResolver.LastFailure, x, y }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "target.json"), json); Console.WriteLine(json);
        if (!formatsRestored || !originalRestored || target.CanBind && target.ClipboardRestored != true) throw new InvalidOperationException("本次真实多格式剪贴板恢复未通过");
    }
}
