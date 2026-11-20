namespace Taskboard.Core;

// User acknowledgement never fabricates a source response. The source latch
// stays intact while only this exact set of actionable event identities is muted.
public static class AiAttention
{
    private static string Prefix(CodexBinding b) => (b.TurnId ?? "") + ":" + b.ActionCycle + "|";
    public static string[] Tokens(CodexBinding b)
    {
        if (b.ActionTokens.Count > 0) return b.ActionTokens.ToArray();
        if (!b.AwaitingUserAction && !(b.SourceEpoch.Length > 0 && CodexState.NeedsUserAction(b.Phase))) return [];
        return CurrentTokens(b);
    }
    private static string[] CurrentTokens(CodexBinding b)
    {
        var prefix = Prefix(b);
        if (b.PendingRequests.Count > 0) return b.PendingRequests.Select(x => prefix + "request:" + x).ToArray();
        if (b.SidebarWaitIdentity.Length > 0) return [prefix + "sidebar:" + b.SidebarWaitIdentity];
        return [prefix + "phase:" + (int)b.Phase];
    }
    public static bool IsSourcePending(CodexBinding b) => Tokens(b).Any(x => !b.AcknowledgedActionTokens.Contains(x));
    public static bool IsPending(CodexBinding b) => b.Sources.Any(IsSourcePending);
    public static void Observe(CodexBinding b, long evidenceAtMs = 0)
    {
        if (b.Phase == CodexPhase.Unknown) return;
        if (b.Phase == CodexPhase.Running && b.ActionTokens.Count > 0) b.ActionCycle++;
        b.ActionScope = Prefix(b);
        b.ActionTokens = CodexState.NeedsUserAction(b.Phase) ? CurrentTokens(b).ToList() : [];
        if (evidenceAtMs > 0 && evidenceAtMs <= b.AcknowledgedAtUnixMs && b.AcknowledgedActionScope == Prefix(b))
            b.AcknowledgedActionTokens = b.AcknowledgedActionTokens.Concat(b.ActionTokens).Distinct().TakeLast(256).ToList();
        if (b.Phase == CodexPhase.Running) b.SidebarWaitIdentity = "";
        b.AcknowledgedActionTokens.RemoveAll(x => !x.StartsWith(Prefix(b), StringComparison.Ordinal));
    }
    public static bool SameConversation(CodexBinding first, CodexBinding second)
    {
        if (first.Platform != second.Platform) return false;
        var left = AiState.NormalizeSessionId(first.Platform, first.ThreadId);
        var right = AiState.NormalizeSessionId(second.Platform, second.ThreadId);
        return left is not null && right is not null
            ? string.Equals(left, right, StringComparison.Ordinal)
            : first.ThreadId.Length > 0 && string.Equals(first.ThreadId, second.ThreadId,
                first.Platform == AiPlatform.Codex ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    public static void Acknowledge(WorkItem task)
    {
        foreach (var b in task.Stages.Select(x => x.Codex).OfType<CodexBinding>())
            Acknowledge(b);
    }
    public static void Acknowledge(CodexBinding binding)
    {
        foreach (var b in binding.Sources) AcknowledgeSource(b);
    }
    public static void AcknowledgeSource(CodexBinding b)
    {
        b.AcknowledgedActionTokens = b.AcknowledgedActionTokens.Concat(Tokens(b)).Distinct().TakeLast(256).ToList();
        b.AcknowledgedActionScope = b.ActionScope.Length > 0 ? b.ActionScope : Prefix(b);
        b.AcknowledgedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
