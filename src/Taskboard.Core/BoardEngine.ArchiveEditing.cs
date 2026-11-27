namespace Taskboard.Core;

public sealed partial class BoardEngine
{
    private WorkItem SubmitArchived(EditSession session, bool recordUndo)
    {
        var archive = State.Archives.Single(x => x.Item.Id == session.TaskId);
        var live = archive.Item;
        var result = Merge(session.Original, session.Working, live);
        if (recordUndo && !Copies.Same(live, result))
            Remember(new(Copies.Of(live), Copies.Of(result), 0, false, Archived: true));
        SaveArchivedResult(archive, result, ReminderChanged(session.Original.Reminder, session.Working.Reminder));
        State.Sessions.Remove(session); PruneExpandedStageDetails(); Notify([result.Id]); return result;
    }
    private void SaveArchivedResult(ArchivedTask archive, WorkItem result, bool clearReminderUnread = false)
    {
        var column = State.Preferences.Columns.FirstOrDefault(x => !x.Deleted && x.Contains(result))
            ?? (archive.RestoreColumn?.Contains(result) == true ? archive.RestoreColumn : null)
            ?? (archive.OriginalColumn.Contains(result) ? archive.OriginalColumn : null)
            ?? throw new InvalidOperationException("归档任务的恢复列表已变化，请重新选择当前状态。");
        var snapshot = Copies.Of(column);
        CleanReferences(result); snapshot.Assign(result);
        if (clearReminderUnread) { result.TimerUnread = false; result.PendingReasons.RemoveAll(x => x == "定时提醒到期"); }
        result.UpdatedUtc = DateTime.UtcNow;
        archive.Item = result; archive.RestoreColumn = snapshot;
        // ArchivedUtc/OriginalColumn/OriginalIndex remain the original history.
    }
    private void ReplayArchivedEdit(UndoEntry entry, bool forward)
    {
        var archive = State.Archives.FirstOrDefault(x => x.Item.Id == entry.Before.Id);
        if (archive is null || entry.After is null) return;
        var original = forward ? entry.Before : entry.After;
        var edited = forward ? entry.After : entry.Before;
        SaveArchivedResult(archive, Merge(original, edited, archive.Item), ReminderChanged(original.Reminder, edited.Reminder));
    }
    public bool DeleteArchived(Guid id)
    {
        if (State.Archives.RemoveAll(x => x.Item.Id == id) == 0) return false;
        State.Sessions.RemoveAll(x => x.TaskId == id);
        PruneExpandedStageDetails();
        ForgetHistory(id); redo.Clear(); Notify([id]); return true;
    }
}
