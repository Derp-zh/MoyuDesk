namespace Taskboard.Core;

public enum AiPlatform { Codex, Dsh, ClaudeCode }
public sealed record AiPending(string Id, bool Approval);
public sealed record AiObservation(AiPlatform Platform, string SessionId, string Epoch, long Sequence,
    string TurnId, DateTime StartedUtc, CodexPhase Phase, AiPending[] Pending, bool Complete,
    bool Executing, DateTime AtUtc, DateTime EventAtUtc = default);

// Platform-neutral state input, deliberately independent of Codex's RPC vocabulary.
public static class AiState
{
    public static string? NormalizeSessionId(AiPlatform platform, string? id)
    {
        if (!Enum.IsDefined(platform) || id is null || id.Length is 0 or > 256 || id.Any(char.IsControl)) return null;
        if (platform == AiPlatform.Dsh) return id;
        if (platform == AiPlatform.ClaudeCode && id.StartsWith("local_", StringComparison.Ordinal) && id.Length > 6 &&
            id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return id;
        return Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : null;
    }
    public static string Name(AiPlatform platform) => platform switch
    { AiPlatform.Dsh => "DSH", AiPlatform.ClaudeCode => "Claude Code", _ => "Codex" };

    public static bool Apply(WorkItem task, Guid stageId, Guid bindingId, int generation, AiObservation value)
    {
        var stage = task.Stages.FirstOrDefault(x => x.Id == stageId);
        var b = stage?.Codex;
        if (b is null || b.Platform == AiPlatform.Codex || b.Platform != value.Platform || b.BindingId != bindingId ||
            b.Generation != generation || b.ThreadId != value.SessionId || value.Sequence < 0 ||
            value.Epoch.Length is 0 or > 256 || value.Pending.Length > 128 ||
            value.Pending.Any(x => x.Id.Length is 0 or > 256) || value.Pending.Select(x => x.Id).Distinct().Count() != value.Pending.Length)
            return false;
        if (value.Epoch == b.SourceEpoch && value.Sequence <= b.LastSequence) return false;
        if (value.AtUtc < b.BoundAtUtc) return false;
        if (value.Complete && value.Phase != CodexPhase.Unknown && value.TurnId.Length is 0 or > 256) return false;
        var old = Copies.Of(b); var oldLane = task.Lane; var oldUnread = task.TimerUnread;
        var sameEpoch = b.SourceEpoch == value.Epoch;
        var sameTurn = b.TurnId == value.TurnId;
        // A new instance cannot erase confirmed pending with an incomplete replay.
        if (!sameEpoch && b.SourceEpoch.Length > 0 && !value.Complete)
        { b.Phase = CodexPhase.Unknown; return !Copies.Same(old, b); }
        if (value.Phase != CodexPhase.Unknown && value.Complete && !sameTurn && !string.IsNullOrEmpty(b.TurnId) &&
            (value.StartedUtc < b.BoundAtUtc || b.ExternalTurnStartedUtc != default && value.StartedUtc < b.ExternalTurnStartedUtc)) return false;
        var terminal = value.Phase is CodexPhase.ReadyForReview or CodexPhase.Failed or CodexPhase.Interrupted;
        if (value.Complete && terminal && !sameTurn && value.StartedUtc <= b.BoundAtUtc) return false;
        b.SourceEpoch = value.Epoch; b.LastSequence = value.Sequence;
        var waitingBefore = b.AwaitingUserAction || CodexState.NeedsUserAction(b.Phase);
        if (value.Phase == CodexPhase.Unknown || !value.Complete)
        { b.Phase = CodexPhase.Unknown; return !Copies.Same(old, b); }
        if (!sameTurn) { b.PendingRequests.Clear(); b.PendingApprovals.Clear(); b.PendingRequestOverflow = false; }
        b.TurnId = value.TurnId;
        b.ExternalTurnStartedUtc = value.StartedUtc;
        b.PendingRequests = value.Pending.Select(x => x.Id).ToList();
        b.PendingApprovals = value.Pending.Where(x => x.Approval).Select(x => x.Id).ToList();
        b.PendingRequestOverflow = false;
        var next = value.Pending.Length > 0
            ? (b.PendingApprovals.Count > 0 ? CodexPhase.WaitingForApproval : CodexPhase.WaitingForInput)
            : value.Phase;
        // Answering/deciding only closes a request. Actual later execution is required.
        if (next == CodexPhase.Running && waitingBefore && !value.Executing)
        { b.Phase = old.Phase; b.AwaitingUserAction = true; }
        else { b.Phase = next; b.AwaitingUserAction = CodexState.NeedsUserAction(next); }
        AiAttention.Observe(b, value.EventAtUtc == default ? 0 : new DateTimeOffset(value.EventAtUtc).ToUnixTimeMilliseconds());
        if (stage!.IsRunning == true && task.ColumnId is null && task.Lane is Lane.Running or Lane.Action)
        {
            var active = task.Stages.Where(x => x.IsRunning == true).Select(x => x.Codex).OfType<CodexBinding>().ToArray();
            if (active.Any(AiAttention.IsPending)) task.Lane = Lane.Action;
            else if (b.Phase == CodexPhase.Running && value.Executing)
            {
                if (task.Lane == Lane.Action && waitingBefore) task.TimerUnread = false;
                task.Lane = Lane.Running;
            }
        }
        return oldLane != task.Lane || oldUnread != task.TimerUnread || !Copies.Same(old, b);
    }
}
