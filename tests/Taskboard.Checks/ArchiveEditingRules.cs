using Taskboard.Core;

internal static class ArchiveEditingRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var engine = new BoardEngine(new BoardState());
        var create = engine.NewSession(Lane.Done); create.Working.Title = "归档编辑原件";
        create.Working.Stages[0].Codex = new CodexBinding { ThreadId = "archive-thread", AwaitingUserAction = true, Phase = CodexPhase.WaitingForInput };
        var item = engine.Submit(create); var id = item.Id;
        engine.Archive(id); var archive = engine.State.Archives.Single(); var archivedAt = archive.ArchivedUtc;
        var edit = engine.Edit(id); edit.Working.Title = "尚未提交"; engine.StageSession(edit);
        Validation.Check(engine.State);
        check(engine.State.Tasks.Count == 0 && archive.Item.Title == "归档编辑原件", "归档编辑副本不恢复活动任务、不提前修改原件");
        var store = new AtomicStore(Path.Combine(root, "archive-edit"), Path.Combine(root, "archive-edit-config"));
        await store.SaveAsync(engine.State);
        check(store.Load().Sessions.Single().Working.Title == "尚未提交", "归档未提交副本可落盘并恢复");
        engine.Discard(edit);
        check(archive.Item.Title == "归档编辑原件" && engine.State.Sessions.Count == 0, "归档取消保存保持原件");
        edit = engine.Edit(id); edit.Working.Title = "保存后的归档"; edit.Working.Lane = Lane.Running;
        edit.Working.Stages[0].Text = "保存阶段详情";
        engine.Submit(edit); Validation.Check(engine.State);
        check(engine.State.Tasks.Count == 0 && archive.Item.Title == "保存后的归档" && archive.Item.Lane == Lane.Running, "归档保存可以修改状态与正文且仍留在归档");
        check(archive.ArchivedUtc == archivedAt && archive.OriginalColumn.BuiltinLane == Lane.Done && archive.RestoreColumn?.BuiltinLane == Lane.Running,
            "归档原始历史保留，保存后的恢复状态独立记录");
        engine.Undo();
        check(engine.State.Tasks.Count == 0 && archive.Item.Title == "归档编辑原件" && archive.Item.Lane == Lane.Done, "归档编辑撤销仍在归档、不生成活动任务");
        engine.Redo();
        check(archive.Item.Title == "保存后的归档" && archive.RestoreColumn?.BuiltinLane == Lane.Running, "归档编辑恢复同时恢复目标状态");
        await store.SaveAsync(engine.State);
        engine = new BoardEngine(store.Load()); archive = engine.State.Archives.Single();
        check(archive.Item.Stages[0].Text == "保存阶段详情" && archive.RestoreColumn?.BuiltinLane == Lane.Running, "归档内容与恢复状态经过磁盘回读");
        var pending = engine.Edit(id); pending.Working.Title = "取消归档时保留的输入";
        var restored = engine.Unarchive(id); Validation.Check(engine.State);
        check(restored.Id == id && restored.Lane == Lane.Running && engine.State.Archives.Count == 0, "取消归档按保存后的状态恢复相同任务身份");
        check(engine.State.Sessions.Contains(pending), "取消归档不丢仍打开编辑窗口的输入副本");
        var submitted = engine.Submit(pending);
        check(submitted.Title == "取消归档时保留的输入" && !submitted.Stages[0].Codex!.AwaitingUserAction, "归档编辑切回活动编辑后合并输入、不恢复旧等待保护");
        engine.Move(id, Lane.Done, null); engine.Archive(id);
        var firstCustom = new BoardColumn { Name = "其他自定义", BuiltinLane = null };
        var targetCustom = new BoardColumn { Name = "恢复目标自定义", BuiltinLane = null };
        engine.State.Preferences.Columns.Add(firstCustom); engine.State.Preferences.Columns.Add(targetCustom);
        edit = engine.Edit(id); targetCustom.Assign(edit.Working); engine.Submit(edit);
        targetCustom.Deleted = true; targetCustom.Visible = false; Validation.Check(engine.State);
        restored = engine.Unarchive(id);
        check(restored.ColumnId == targetCustom.Id && !targetCustom.Deleted && targetCustom.Visible && restored.ColumnId != firstCustom.Id,
            "归档编辑后的自定义恢复目标按准确身份恢复，不误选其他自定义列表");
        engine.Move(id, Lane.Done, null); engine.Archive(id); _ = engine.Edit(id);
        check(engine.DeleteArchived(id) && engine.State.Sessions.All(x => x.TaskId != id), "永久删除归档同时清理对应编辑副本");
        engine.Undo();
        check(engine.State.Tasks.All(x => x.Id != id) && engine.State.Archives.All(x => x.Item.Id != id), "归档删除不能被活动撤销重建");
        Validation.Check(engine.State);
    }
}
