namespace Taskboard.Core;

public sealed partial class BoardEngine
{
    public bool AcknowledgeStageAttention(Guid taskId, Guid stageId, Guid expectedBinding)
    {
        var task = State.Tasks.FirstOrDefault(x => x.Id == taskId);
        var binding = task?.Stages.FirstOrDefault(x => x.Id == stageId)?.Codex;
        if (task is null || binding is null || binding.BindingId != expectedBinding || !AiAttention.IsPending(binding)) return false;
        var pendingSources = binding.Sources.Where(AiAttention.IsSourcePending).ToArray();
        AiAttention.Acknowledge(binding);
        var affected = new List<Guid> { taskId };
        AcknowledgeSameConversation(pendingSources, affected);
        Notify(affected);
        return true;
    }
    internal void AcknowledgeSameConversation(IEnumerable<CodexBinding> sources, List<Guid> affectedTaskIds)
    {
        var targets = sources.Where(x => !string.IsNullOrWhiteSpace(x.ThreadId)).ToArray();
        if (targets.Length == 0) return;
        foreach (var task in State.Tasks)
        {
            var changed = false;
            foreach (var candidate in task.Stages.Select(x => x.Codex).OfType<CodexBinding>().SelectMany(x => x.Sources))
            {
                if (!AiAttention.IsSourcePending(candidate) || !targets.Any(t => AiAttention.SameConversation(t, candidate))) continue;
                AiAttention.AcknowledgeSource(candidate);
                changed = true;
            }
            if (changed && !affectedTaskIds.Contains(task.Id)) affectedTaskIds.Add(task.Id);
        }
    }
    // A list action changes exactly one binding, without submitting an open editor.
    public bool UnbindStageConversation(Guid taskId, Guid stageId, Guid? expectedBinding)
    {
        var item = State.Tasks.FirstOrDefault(x => x.Id == taskId);
        var stage = item?.Stages.FirstOrDefault(x => x.Id == stageId);
        if (item is null || stage is null || stage.Codex is null || stage.Codex.BindingId != expectedBinding) return false;
        var before = Copies.Of(item);
        stage.Codex = null;
        item.UpdatedUtc = DateTime.UtcNow;
        Remember(new(before, Copies.Of(item), State.Tasks.IndexOf(item), false));
        Notify([item.Id]);
        return true;
    }
    public bool BindStageConversation(Guid taskId, Guid stageId, Guid? expectedBinding, string threadId, string name, AiPlatform platform = AiPlatform.Codex)
    {
        var normalized = AiState.NormalizeSessionId(platform, threadId); if (normalized is null) return false;
        threadId = normalized;
        var item = State.Tasks.FirstOrDefault(x => x.Id == taskId);
        var stage = item?.Stages.FirstOrDefault(x => x.Id == stageId);
        if (item is null || stage is null || stage.Codex?.BindingId != expectedBinding) return false;
        var sameThread = stage.Codex?.Platform == platform && string.Equals(stage.Codex?.ThreadId, threadId, platform == AiPlatform.Dsh ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        if (sameThread && stage.Codex!.ThreadName == name) return true;
        var before = Copies.Of(item);
        if (sameThread) stage.Codex!.ThreadName = name;
        else stage.Codex = new CodexBinding
        {
            Platform = platform, ThreadId = threadId, ThreadName = name, Generation = (stage.Codex?.Generation ?? 0) + 1,
            PreviousThreads = stage.Codex is null ? [] : stage.Codex.PreviousThreads.Append(stage.Codex.ThreadId).TakeLast(20).ToList()
        };
        item.UpdatedUtc = DateTime.UtcNow;
        Remember(new(before, Copies.Of(item), State.Tasks.IndexOf(item), false));
        Notify([item.Id]); return true;
    }
}
