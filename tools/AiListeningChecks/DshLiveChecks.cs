using System.IO;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class DshLiveChecks
{
    internal static async Task Run(string[] args)
    {
        if (args.Length != 6 || args[2] != "--data-dir" || args[4] != "--seconds") throw new ArgumentException("--live-dsh ID --data-dir NEW-DIRECTORY --seconds 1..300");
        var root = Path.GetFullPath(args[3]); var seconds = int.Parse(args[5]);
        if (seconds is < 1 or > 300 || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new ArgumentException("需要新的隔离目录及有界时间");
        Directory.CreateDirectory(root);
        var board = new BoardState(); var item = new WorkItem { Title = "DSH原应用隔离观察卡片", Lane = Lane.Running,
            Stages = [new Stage { IsRunning = true, Codex = new() { Platform = AiPlatform.Dsh, ThreadId = args[1] } }] };
        item.CurrentStageId = item.Stages[0].Id; board.Tasks.Add(item);
        var store = new AtomicStore(Path.Combine(root, "board")); await store.SaveAsync(board);
        var records = new List<object>(); var gate = new object(); var revision = 0; var savedRevision = 0;
        using var observer = new AiPlatformObserver();
        observer.Changed += (_, stage, binding, generation, observation) =>
        {
            lock (gate)
            {
                if (!AiState.Apply(item, stage, binding, generation, observation)) return;
                revision++; var b = item.Stages[0].Codex!;
                var record = new { at = DateTime.UtcNow, phase = b.Phase.ToString(), lane = item.Lane.ToString(), pending = b.PendingRequests.Count, approvals = b.PendingApprovals.Count, complete = observation.Complete, executing = observation.Executing };
                records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record));
                File.WriteAllText(Path.Combine(root, "trace.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            }
        };
        observer.Watch([item]); observer.Start(); Console.WriteLine("live-ready");
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until && !File.Exists(Path.Combine(root, "stop")))
        {
            BoardState? save = null;
            lock (gate) if (revision != savedRevision) { save = Copies.Of(board); savedRevision = revision; }
            if (save is not null) await store.SaveAsync(save);
            await Task.Delay(100);
        }
        BoardState final; lock (gate) final = Copies.Of(board); await store.SaveAsync(final);
        var readback = store.Load().Tasks.Single();
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { version = "0.9.35", source = "Original DSH native session, read-only; isolated model/storage only, no visible board card or mouse binding", records, finalPhase = readback.Stages[0].Codex!.Phase.ToString(), finalLane = readback.Lane.ToString(), status = observer.Status }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
