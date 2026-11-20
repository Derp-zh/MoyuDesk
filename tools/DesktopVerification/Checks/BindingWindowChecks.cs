using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class BindingWindowChecks
{
    internal static async Task Run(BoardController controller, string root)
    {
        if (!controller.Isolated || !controller.Smoke) throw new InvalidOperationException("仅隔离离屏检查");
        var checks = new List<string>();
        void Check(bool good, string text) { if (!good) throw new InvalidOperationException(text); checks.Add(text); }
        const string oldId = "11111111-1111-4111-8111-111111111111", newId = "22222222-2222-4222-8222-222222222222";
        var edit = controller.Engine.NewSession(Lane.Refining); edit.Working.Title = "绑定窗口隔离样本";
        edit.Working.Stages[0].Codex = new CodexBinding { ThreadId = oldId, ThreadName = "原会话" };
        var second = controller.Engine.AddStage(edit); second.Text = "另一阶段保持";
        var task = controller.Engine.Submit(edit);
        var editor = controller.EditTask(task.Id); await Task.Delay(220);
        var manager = controller.BindingWindow; var first = editor.StagePickers[task.Stages[0].Id]; var other = editor.StagePickers[second.Id];
        var catalog = Enumerable.Range(1, 80).Select(i => new CodexThread(i == 1 ? newId : Guid.NewGuid().ToString("D"), "会话" + i, i)).ToArray();
        first.SetCatalog(catalog); other.SetCatalog(catalog);
        Check(controller.Engine.State.Preferences.AutoOpenConversationBindingWindow, "自动打开会话绑定窗口默认开启");
        Check(SettingsSurface.SectionCaption("监听") == "通知监听" && SettingsSurface.SectionCaption("AI平台接入情况") == "AI平台监听", "两类监听设置名称分开，导航key保持");
        Check(Ui.HintDuration == TimeSpan.FromSeconds(4), "操作提示气泡统一4秒");
        manager.Open(first, editor.Host!, false); await Task.Delay(150);
        Check(manager.Surface is { Width: 268, Height: 560 } && manager.Current == first, "绑定窗常态268×560且来源准确");
        Check(first.Results.Visibility == Visibility.Visible && !Visual<Button>(first.Pane).Any(x => x.Content?.ToString() is "⌄" or "⌃"), "列表常驻且没有展开收起箭头");
        Render(manager.Surface!, Path.Combine(root, "binding-window-normal.png"));
        first.Input.Text = "会话"; first.Results.SelectedIndex = 3;
        var scroll = Visual<ScrollViewer>(first.Results).First(); scroll.ScrollToVerticalOffset(22); await Task.Delay(80);
        var originalOffset = scroll.VerticalOffset;
        Check(originalOffset > 0, "保留滚动检查使用真实非零列表偏移");
        var state = manager.BeginDrag(keepOpen: true); manager.Open(first, editor.Host!, true); manager.PreserveSearchDuringDrag(state, first);
        await Task.Delay(65);
        Check(manager.Surface!.Height == 560, "开始拖动保留长窗口，无需展开或改变高度");
        await Task.Delay(250);
        Check(manager.Surface!.Height == 560 && first.Input.Text == "会话", "拖动时保持长高度，同来源原搜索保留");
        Render(manager.Surface, Path.Combine(root, "binding-window-drag.png"));
        manager.FinishDrag(state, false); await Task.Delay(320);
        Check(manager.Current == first && manager.Surface.ShouldRestore && manager.Surface.Height == 560 && first.Input.Text == "会话" && first.Results.SelectedIndex == 3 && Math.Abs(scroll.VerticalOffset - originalOffset) < .1,
            "拖动前已打开且未绑定，恢复长窗口、搜索及选择，旧关闭回调不清理新界面");
        state = manager.BeginDrag(); manager.Open(other, editor.Host!, true); manager.FinishDrag(state, false); await Task.Delay(320);
        Check(manager.Current == first && first.Input.Text == "会话", "从其他阶段取消拖动，恢复原先窗口而非临时目标");
        state = manager.BeginDrag(); manager.Open(other, editor.Host!, true); manager.FinishDrag(state, true); await Task.Delay(150);
        Check(manager.Current is null && !manager.Surface.IsVisible, "既有窗口绑定成功后正常收起");
        state = manager.BeginDrag(); manager.Open(first, editor.Host!, true); manager.FinishDrag(state, false); await Task.Delay(140);
        Check(manager.Current is null && !manager.Surface.IsVisible, "本次拖动新开的窗口未绑定也收起");
        manager.Open(first, editor.Host!, false); state = manager.BeginDrag(); manager.Open(other, editor.Host!, true);
        manager.Open(other, editor.Host!, false); other.Input.Text = "新操作"; manager.FinishDrag(state, false);
        Check(manager.Current == other && other.Input.Text == "新操作", "旧拖动结果不覆盖用户新打开的窗口与搜索");
        Check(Application.Current.Windows.OfType<SurfaceWindow>().Count(x => x.Title == "绑定会话专属窗口") == 1, "所有来源复用全程序一个绑定窗口");
        manager.Close(); manager.Open(first, editor.Host!, false); state = manager.BeginDrag(); manager.Open(first, editor.Host!, true);
        editor.Host!.Hide(); manager.FinishDrag(state, false); await Task.Delay(150);
        Check(manager.Current is null, "父窗开始隐藏时同步收子窗，取消回执不会重开");
        editor.Host.Show(); await Task.Delay(150);
        Check(manager.Current is null && !manager.Surface.ShouldRestore, "父窗重开不记忆绑定窗打开状态");
        manager.Open(first, editor.Host!, false); first.Input.Text = "会话2";
        using (var cancelledDrag = new ConversationBindingDrag(controller, editor, editor.Session.Working.Stages[0]))
            cancelledDrag.Dispose();
        Check(manager.Current == first && first.Input.Text == "会话2" && manager.Surface.Height == 560, "真实拖动生命周期取消恢复既有长窗口与搜索");
        using (var failedRead = new ConversationBindingDrag(controller, editor, editor.Session.Working.Stages[0]))
        {
            failedRead.MoveTo(new Point(-30000, -30000));
            await failedRead.Complete(new Point(-30000, -30000), _ => throw new IOException("隔离读取失败"));
        }
        Check(manager.Current == first && first.Input.Text == "会话2" && editor.Session.Working.Stages[0].Codex?.ThreadId == oldId,
            "实际Complete捕获读取失败，恢复窗口搜索且不改绑定");
        controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = false;
        using (var manualOnly = new ConversationBindingDrag(controller, editor, editor.Session.Working.Stages[0]))
            Check(manager.Current == first && manager.Surface.Height == 560 && first.Input.Text == "会话2", "关闭自动打开时，手动已开的绑定窗继续可作落点且保持长高度");
        Check(manager.Current == first && first.Input.Text == "会话2", "自动打开关闭时取消仍保留手动窗口");
        controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = true;
        var delayed = new TaskCompletionSource<CodexThread[]>();
        var oldLoad = first.Load(() => delayed.Task);
        manager.Open(other, editor.Host!, false); manager.Open(first, editor.Host!, false); first.Input.Text = "会话2";
        delayed.SetResult([new CodexThread(oldId, "迟到的旧目录", 0)]); await oldLoad;
        Check(first.Input.Text == "会话2" && first.Matches.Any(x => x.Id == catalog[1].Id), "旧窗口迟到的目录结果不能覆盖快速重开的目录与搜索");
        manager.Close();
        var placement = ConversationBindingWindow.Placement(new Rect(500, 100, 500, 550), new Rect(0, 0, 1920, 1080), false);
        Check(!placement.Left && placement.Bounds.Left == 1008 && placement.Bounds.Height == 560, "充足空间向右定位并保持长高度");
        placement = ConversationBindingWindow.Placement(new Rect(1550, 500, 350, 550), new Rect(0, 0, 1920, 1080), true);
        Check(placement.Left && placement.Bounds.Bottom <= 1080 && placement.Bounds.Height == 560, "右侧不足翻左且双倍窗口保持工作区内");
        placement = ConversationBindingWindow.Placement(new Rect(-300, 100, 250, 550), new Rect(-1200, 0, 1200, 400), true);
        Check(placement.Left && placement.Bounds.Left >= -1200 && placement.Bounds.Height == 400, "负坐标屏幕与纵向不足按可用高度限制");
        controller.Columns.Last().Hide();
        var visible = controller.Columns.Where(x => x.IsVisible).Select(x => x.Definition.Id).ToHashSet();
        var working = editor.Session.Working.Stages[0]; var otherBefore = Copies.Of(editor.Session.Working.Stages[1]);
        using (var drag = new ConversationBindingDrag(controller, editor, working))
        {
            drag.MoveTo(new Point(-30000, -30000));
            Check(controller.Columns.All(x => !x.IsVisible) && editor.Host.ShouldRestore, "编辑胶囊拖出父窗只隐藏列表、保留悬浮父窗");
            await drag.Complete(new Point(-30000, -30000), _ => Task.FromResult(new ConversationDropTarget(newId, "新会话", "合成来源")));
        }
        Check(editor.Session.Working.Stages[0].Codex?.ThreadId == newId && controller.Engine.State.Tasks.Single().Stages[0].Codex?.ThreadId == oldId,
            "编辑拖动只改副本，不提前改正式绑定");
        Check(visible.SetEquals(controller.Columns.Where(x => x.IsVisible).Select(x => x.Definition.Id)) && Copies.Same(otherBefore, editor.Session.Working.Stages[1]),
            "结束只恢复原可见列、其他阶段不变");
        await controller.SubmitSession(editor.Session);
        Check(controller.Store.Load().Tasks.Single().Stages[0].Codex?.ThreadId == newId, "编辑拖动后经实际保存链和文件回读生效");
        controller.RemoveEditor(editor); editor.Destroy();
        foreach (var column in controller.Columns) column.Root.UpdateLayout();
        var card = controller.Columns.SelectMany(c => Visual<ConversationDragButton>(c.Root)).First(x => x.DataContext is Stage s && s.Id == task.Stages[0].Id);
        card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
        Check(controller.Editors.Any(x => x.Session.TaskId == task.Id && x.Host?.ShouldRestore == true), "会话文本共用实际Click动作打开任务编辑");
        var savedEditor = controller.Editors.Single(x => x.Session.TaskId == task.Id);
        var savedPicker = savedEditor.StagePickers[task.Stages[0].Id]; savedPicker.SetCatalog(catalog);
        manager.Open(savedPicker, savedEditor.Host!, false); savedPicker.Input.Text = "会话";
        var liveBeforeFailure = controller.Engine.State.Tasks.Single().Stages[0];
        using (var lockedFile = new FileStream(controller.Store.MainPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var failedSave = new ConversationBindingDrag(controller, controller.Columns.Single(x => x.Lane == Lane.Refining), task.Id,
            liveBeforeFailure.Id, liveBeforeFailure.Codex?.BindingId, "隔离保存失败"))
        {
            failedSave.MoveTo(new Point(-30000, -30000));
            var threw = false;
            try { await failedSave.Complete(new Point(-30000, -30000), _ => Task.FromResult(new ConversationDropTarget(oldId, "保存失败样本", "合成来源"))); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw && manager.Current == savedPicker && savedPicker.Input.Text == "会话", "实际绑定磁盘保存失败，也恢复拖动前窗口供重试");
        }
        Check(controller.Store.Load().Tasks.Single().Stages[0].Codex?.ThreadId == newId, "保存失败时隔离磁盘保留上次完整绑定");
        var changedBinding = controller.Engine.State.Tasks.Single().Stages[0].Codex!;
        controller.Engine.BindStageConversation(task.Id, liveBeforeFailure.Id, changedBinding.BindingId, newId, "新会话");
        await controller.Flush(); manager.Close();
        var live = controller.Engine.State.Tasks.Single(); controller.Engine.Move(live.Id, Lane.Done, null); controller.Engine.Archive(live.Id);
        var archivedEditor = controller.EditTask(live.Id); await Task.Delay(150);
        Check(Visual<TextBlock>(archivedEditor).Any(x => x.Text == "已归档 · 编辑任务"), "归档任务打开可编辑窗口并标识已归档");
        Render(archivedEditor.Host!, Path.Combine(root, "archived-editor.png"));
        archivedEditor.Session.Working.Title = "归档实际保存"; archivedEditor.Session.Working.Lane = Lane.Running;
        Check(await controller.SubmitSession(archivedEditor.Session), "归档通过实际控制器候选保存链提交");
        Check(controller.Store.Load().Archives.Single().Item.Title == "归档实际保存" && controller.Engine.State.Tasks.Count == 0, "归档编辑落盘仍归档、不成为活动监听任务");
        var archivePage = new SettingsSurface(controller) { SuppressActivation = true, Left = -18000, Top = -18000 };
        archivePage.Show(); archivePage.SelectSection("归档"); await Task.Delay(130);
        Render(archivePage, Path.Combine(root, "archive-settings.png"));
        var restoreButton = Visual<Button>(archivePage.Root).Single(x => Equals(x.Tag, "RestoreArchivedTask"));
        var deleteButton = Visual<Button>(archivePage.Root).Single(x => Equals(x.Tag, "DeleteArchivedTask"));
        Check(restoreButton.ActualHeight == 28 && deleteButton.ActualWidth == 24 && deleteButton.ActualHeight == 24,
            "归档重设计显示28高取消归档胶囊及24×24删除按钮");
        restoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
        Check(controller.Engine.State.Tasks.Single().Id == live.Id && controller.Engine.State.Tasks.Single().Lane == Lane.Running,
            "归档页实际按钮动作按编辑后状态取消归档");
        controller.Engine.Move(live.Id, Lane.Done, null); controller.Engine.Archive(live.Id); archivePage.Destroy(false);
        Check(!await controller.DeleteArchivedTask(live.Id, () => Task.FromResult(false)) && controller.Engine.State.Archives.Count == 1, "取消归档删除确认不改数据");
        Check(await controller.DeleteArchivedTask(live.Id, () => Task.FromResult(true)) && controller.Engine.State.Archives.Count == 0 && controller.Editors.All(x => x.Session.TaskId != live.Id),
            "确认永久删除归档清理实际编辑窗口与数据");
        Guid oldest = default;
        for (var i = 0; i < 41; i++)
        {
            var seed = controller.Engine.NewSession(Lane.Done); seed.Working.Title = "隔离自动清理" + i;
            var item = controller.Engine.Submit(seed); controller.Engine.Archive(item.Id);
            if (i == 0) oldest = item.Id;
        }
        _ = controller.EditTask(oldest); controller.Engine.State.Preferences.AutoDeleteArchives = true;
        Check(controller.TrimArchives() == 1 && controller.Engine.State.Archives.Count == 40 && controller.Editors.All(x => x.Session.TaskId != oldest),
            "自动清理较早归档时同步关闭被清理任务的编辑副本");
        var draft = controller.NewTask(Lane.Refining); draft.Session.Working.Title = "保留的首份草稿";
        controller.Engine.StageSession(draft.Session);
        var draftPicker = draft.StagePickers[draft.Session.Working.Stages[0].Id]; draftPicker.SetCatalog(catalog);
        manager.Open(draftPicker, draft.Host!, false); var draftScope = manager.BeginDrag(keepOpen: true);
        var nextDraft = controller.NewTask(Lane.Refining); manager.FinishDrag(draftScope, false);
        Check(manager.Current is null && !draftPicker.WindowAvailable, "新建或切换草稿同步关闭旧来源绑定窗，旧拖动取消不能重开");
        using (var draftDrag = new ConversationBindingDrag(controller, nextDraft, nextDraft.Session.Working.Stages[0]))
        {
            draftDrag.MoveTo(new Point(-30000, -30000));
            await draftDrag.Complete(new Point(-30000, -30000), _ => Task.FromResult(new ConversationDropTarget(newId, "草稿会话", "合成来源")));
        }
        Check(nextDraft.Session.Working.Stages[0].Codex?.ThreadId == newId && controller.Engine.State.Tasks.Count == 0,
            "新建胶囊拖动只绑定当前草稿，不提前创建正式任务");
        controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = false; controller.Engine.Touch(); await controller.Flush();
        Check(!controller.Store.Load().Preferences.AutoOpenConversationBindingWindow, "自动打开开关关闭后准确持久化");
        File.WriteAllText(Path.Combine(root, "binding-window-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
            scope = "实际离屏控件、窗口生命周期与隔离保存；目录和落点为夹具，不是物理拖动或原Codex来源",
            mouseMoved = false, realCodexDrop = false, clipboardAccess = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return item; }
    private static void Render(SurfaceWindow window, string path)
    {
        window.Root.Measure(new Size(window.Width, window.Height)); window.Root.Arrange(new Rect(0, 0, window.Width, window.Height)); window.Root.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(window.Root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(path); encoder.Save(output);
    }
}
