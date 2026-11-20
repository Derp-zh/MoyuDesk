using System.IO;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Taskboard.Desktop;

internal static class ClipboardRecoveryChecks
{
    internal static async Task Run(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!root.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) throw new IOException("需新的隔离报告目录");
        Directory.CreateDirectory(root);
        var history = await Clipboard.GetHistoryItemsAsync();
        var metadata = new List<object>(); var candidates = new List<ClipboardHistoryItem>();
        foreach (var item in history.Items)
        {
            if (!item.Content.Contains(StandardDataFormats.Text)) continue;
            var value = await item.Content.GetTextAsync();
            var test = value.StartsWith("isolated-dsh-", StringComparison.Ordinal) || value.StartsWith("codex://threads/", StringComparison.Ordinal)
                || value == "aa_53b3252605e32778_codex-6d5188f5-e996-4f0b-b9a3-044dea55f174" || value == "session-4eaf5f96-7ebc-40d9-adb9-66f473166e7f";
            metadata.Add(new { item.Id, at = item.Timestamp, test, formats = item.Content.AvailableFormats.ToArray() });
            if (!test) candidates.Add(item);
        }
        var currentIsTest = ConversationClipboardThread.Run(() => System.Windows.Forms.Clipboard.GetText().StartsWith("isolated-dsh-", StringComparison.Ordinal));
        var knownReceipt = Path.GetFullPath("test-output/conversation-0939-body-hover-debug/target.json");
        ClipboardHistoryItem? verified = null;
        if (File.Exists(knownReceipt))
        {
            using var receipt = JsonDocument.Parse(File.ReadAllText(knownReceipt));
            if (receipt.RootElement.GetProperty("originalRestored").GetBoolean())
            {
                var at = File.GetLastWriteTimeUtc(knownReceipt);
                var matches = candidates.Where(x => Math.Abs((x.Timestamp.UtcDateTime - at).TotalSeconds) < 2).ToArray();
                if (matches.Length == 1) verified = matches[0];
            }
        }
        string? restored = null;
        if (currentIsTest && (verified is not null || candidates.Count == 1))
        {
            var item = verified ?? candidates[0];
            var sequence = ConversationClipboardThread.Run(() => { using var current = new CodexConversationMenu.NativeClipboard(0); return current.Sequence; });
            var expected = await item.Content.GetTextAsync();
            var stillOwned = ConversationClipboardThread.Run(() => { using var current = new CodexConversationMenu.NativeClipboard(0); return current.Sequence == sequence && System.Windows.Forms.Clipboard.GetText().StartsWith("isolated-dsh-", StringComparison.Ordinal); });
            if (stillOwned)
            {
                var status = Clipboard.SetHistoryItemAsContent(item);
                var actual = await Clipboard.GetContent().GetTextAsync();
                if (status == SetHistoryItemAsContentStatus.Success && actual == expected) restored = item.Id;
            }
        }
        var report = new { historyEnabled = Clipboard.IsHistoryEnabled(), status = history.Status.ToString(), currentIsTest,
            candidateCount = candidates.Count, matchedSuccessfulReceipt = verified is not null, restoredItem = restored, metadata };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "recovery.json"), json); Console.WriteLine(json);
    }
}
