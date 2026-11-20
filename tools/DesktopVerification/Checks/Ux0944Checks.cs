using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0944Checks
{
    internal static async Task PrepareHost(BoardController controller)
    {
        if (!controller.Isolated || controller.Smoke || !controller.Store.DirectoryPath.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("桌面宿主必须显式使用test-output隔离目录");
        using var reader = new AiPlatformObserver(onlyPlatform: AiPlatform.Dsh);
        var dsh = await reader.Catalog(AiPlatform.Dsh);
        var codex = await controller.Codex.Catalog();
        controller.ConversationFixture = codex.Concat(dsh).ToArray();
        controller.SetConversationPlatformsForChecks(AiPlatform.Codex, AiPlatform.Dsh);
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "host-ready.json"), JsonSerializer.Serialize(new
        { codex = codex.Length, dsh = dsh.Length, actualReadOnlyCatalog = true, taskStateFixture = true, process = Environment.ProcessId }));
    }
    internal static async Task Probe(BoardController controller)
    {
        if (!controller.Isolated) throw new InvalidOperationException("只允许隔离验收");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(controller.Store.DirectoryPath, "probe-input.json")));
        var value = doc.RootElement; var point = new Point(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble());
        using var stop = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
        var target = value.GetProperty("platform").GetInt32() == 1
            ? await DshConversationTarget.Resolve(point, stop.Token)
            : await CodexConversationMenu.Resolve(point, stop.Token);
        var passed = target.ThreadId == value.GetProperty("expectedId").GetString() && target.CanBind && target.ClipboardRestored != false;
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "probe-result.json"), JsonSerializer.Serialize(new { passed, target, step = CodexConversationMenu.LastStep, failure = ConversationLinkResolver.LastFailure, fixture = false, source = "original-desktop-own-menu", clipboardUsed = true }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("原客户端准确编号复核失败");
    }
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || !controller.Smoke) throw new InvalidOperationException("仅允许隔离离屏检查");
        var checks = new List<string>(); var failures = new List<string>(); var measurements = new List<object>();
        void Check(bool ok, string name) => (ok ? checks : failures).Add(name);
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "0944高度反馈";
        var editor = new EditorSurface(controller, session) { Width = 520, Height = 550 };
        editor.Measure(new Size(520, 550)); editor.Arrange(new Rect(0, 0, 520, 550)); editor.UpdateLayout();
        var text = StageFeatureChecks.Children<TextBox>(editor).Single(x => Equals(x.Tag, "StageDetailsInput"));
        var twoLines = text.FontFamily.LineSpacing * text.FontSize * 2 + text.Padding.Top + text.Padding.Bottom;
        Check(text.ActualHeight >= twoLines - 2 && text.ActualHeight <= twoLines + 3, "运行空正文实际高度为两行而非四行");
        measurements.Add(new { text.ActualHeight, twoLines, text.MinHeight });
        Check(MarqueeText.InitialPauseSeconds == 1 && MarqueeText.EndPauseSeconds == .5, "滚动首停1秒末停0.5秒");
        var side = new SideSurface { Left = -18000, Top = -18000, SuppressActivation = true };
        side.Show();
        var glow = (Grid)typeof(SurfaceWindow).GetField("scrollGlow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(side)!;
        Check(glow.Visibility != Visibility.Visible, "侧向开合从首帧起无中心白条");
        side.Destroy(false); editor.Destroy();
        var center = new SurfaceWindow(430, 550) { Left = -18000, Top = -18000, SuppressActivation = true };
        center.Show();
        var centerGlow = (Grid)typeof(SurfaceWindow).GetField("scrollGlow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(center)!;
        Check(centerGlow.Visibility == Visibility.Visible, "中心开合仍显示白芯与泛光"); center.Destroy(false);
        Check(ConversationNavigator.CodexUri("00000000-0000-0000-0000-000000000000") is null && ConversationNavigator.CodexUri("bad/id") is null,
            "跳转拒绝空编号与路径注入");
        var uuid = Guid.NewGuid().ToString("D");
        Check(ConversationNavigator.CodexUri(uuid) == "codex://threads/" + uuid, "Codex跳转使用准确编号深度链接");
        var sample = controller.Engine.NewSession(Lane.Running); sample.Working.Title = "0944排序和归档";
        sample.Working.Stages[0].Text = "一行";
        var extra = controller.Engine.AddStage(sample); extra.Text = "第一行\n第二行很长很长很长";
        extra.Codex = new CodexBinding { ThreadId = uuid, ThreadName = "排序会话" };
        var item = controller.Engine.Submit(sample);
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(250);
        var edit = controller.EditTask(item.Id); edit.Show(); await Task.Delay(200); edit.Root.UpdateLayout();
        Check(StageFeatureChecks.Children<Thumb>(edit).Count(x => Equals(x.Tag, "StageReorder")) == 2, "两个阶段均有独立圆形排序手柄");
        Check(StageFeatureChecks.Children<Button>(edit).Any(x => Equals(x.Tag, "ArchiveTask") && Equals(x.Content, "归档任务")), "已有任务左下为归档入口");
        var bodies = StageFeatureChecks.Children<StageDetailsEditor>(edit).ToArray();
        var paused = bodies[1];
        Check(paused.ActualHeight <= paused.Input.FontFamily.LineSpacing * paused.Input.FontSize + 16, "未运行多行内容折叠为一行");
        paused.Input.Text = ""; edit.Root.UpdateLayout();
        Check(paused.ActualHeight < .1 && edit.Session.Working.Stages[1].Text == "", "未运行空正文0行且编辑副本同步");
        paused.Input.Text = "完整内容\n第二行\n第三行";
        paused.SetCollapseInactive(false); await Task.Delay(220); edit.Root.UpdateLayout();
        Check(paused.ActualHeight >= twoLines - 1 && paused.Input.Text.Contains("第三行"), "关闭未运行折叠后完整展开且不丢输入");
        paused.SetCollapseInactive(true); await Task.Delay(220); edit.Root.UpdateLayout();
        Check(paused.ActualHeight < 34, "重新开启未运行折叠后恢复一行");
        paused.SetRunning(true); await Task.Delay(75); var openingHeight = paused.ActualHeight;
        paused.SetRunning(false); await Task.Delay(240); edit.Root.UpdateLayout();
        Check(openingHeight > 32 && paused.ActualHeight < 34 && paused.Input.Text.Contains("第三行"), "展开中途反向连续收起且不丢完整正文");
        var originalFirst = edit.Session.Working.Stages[0].Id;
        var moved = edit.Session.Working.Stages[1]; var movedBinding = moved.Codex!.BindingId;
        Check(controller.Engine.ReorderStage(edit.Session, moved.Id, 0) && edit.Session.Working.Stages[0].Id == moved.Id
            && moved.Codex.BindingId == movedBinding && edit.Session.Working.CurrentStageId == originalFirst,
            "排序保持阶段身份、会话、运行和当前指针");
        Check(!controller.Engine.ReorderStage(edit.Session, moved.Id, -1), "无效排序不改副本");
        var column = controller.Columns.Single(x => x.Lane == Lane.Running);
        column.Root.UpdateLayout(); var card = column.Locate(item.Id)!;
        var detailsText = StageFeatureChecks.Children<StageDetailsText>(card).Single(x => Equals(x.Tag, "TaskStageDetails"));
        Check(detailsText.ActualHeight >= detailsText.FontFamily.LineSpacing * detailsText.FontSize * 2 - 1, "列表一行正文实际至少占两行");
        item.Stages[0].Text = ""; controller.Engine.Notify([item.Id]); column.Root.UpdateLayout(); card = column.Locate(item.Id)!;
        detailsText = StageFeatureChecks.Children<StageDetailsText>(card).Single(x => Equals(x.Tag, "TaskStageDetails"));
        Check(detailsText.ActualHeight >= detailsText.FontFamily.LineSpacing * detailsText.FontSize * 2 - 1, "列表空正文实际至少占两行");
        var disabled = StageFeatureChecks.Children<ConversationJumpButton>(card).Single();
        Check(!disabled.IsEnabled && disabled.BorderThickness == new Thickness(0), "未绑定跳转图标变暗禁用且无圆形边界");
        var hit = card.InputHitTest(disabled.TranslatePoint(new Point(disabled.ActualWidth / 2, disabled.ActualHeight / 2), card)) as DependencyObject;
        Check(ConversationJumpButton.Contains(hit), "禁用跳转图标落点仍由跳转手势处理，不交给卡片");
        var iconRect = new Rect(disabled.TranslatePoint(new Point(), detailsText), disabled.RenderSize);
        Check(Math.Abs(disabled.ActualHeight - detailsText.FontFamily.LineSpacing * detailsText.FontSize) < .5
            && new Rect(detailsText.RenderSize).Contains(iconRect) && detailsText.LineWidths[^1] < detailsText.ActualWidth,
            "跳转图标一行高且在文本范围右下角，仅让出窄内部位置");
        item.Stages[0].Text = string.Join("\n", Enumerable.Repeat("长详情文字用于检查完整展示和省略开关", 15));
        controller.Engine.Notify([item.Id]); column.Root.UpdateLayout(); card = column.Locate(item.Id)!;
        detailsText = StageFeatureChecks.Children<StageDetailsText>(card).Single(x => Equals(x.Tag, "TaskStageDetails"));
        var trimmed = detailsText.ActualHeight;
        controller.Engine.State.Preferences.EllipsizeStageDetails = false; controller.Engine.Notify([item.Id]); column.Root.UpdateLayout(); card = column.Locate(item.Id)!;
        controller.Engine.State.Preferences.CollapseInactiveStageDetails = false;
        edit.RefreshStageDetailsPolicy(); await Task.Delay(220);
        Check(paused.ActualHeight >= twoLines - 1 && !controller.Engine.State.Preferences.EllipsizeStageDetails,
            "两个详情偏好相互独立，现有编辑即时更新");
        detailsText = StageFeatureChecks.Children<StageDetailsText>(card).Single(x => Equals(x.Tag, "TaskStageDetails"));
        Check(detailsText.ActualHeight > trimmed * 3 && !detailsText.Ellipsize, "关闭省略后完整详情使卡片增高");
        item.Lane = Lane.Action;
        item.Stages[0].Codex = new CodexBinding { ThreadId = uuid, AwaitingUserAction = true, Phase = CodexPhase.WaitingForInput, SourceEpoch = "fixture" };
        Check(ConversationJumpButton.Attention(item, item.Stages[0]) && !ConversationJumpButton.Attention(item, item.Stages[1]), "仅实际等待阶段显示橙红");
        item.Stages[0].Codex!.AwaitingUserAction = false; item.Stages[0].Codex!.Phase = CodexPhase.Running;
        Check(!ConversationJumpButton.Attention(item, item.Stages[0]), "实际恢复后取消橙红");
        controller.Engine.Notify([item.Id]);
        var preview = controller.Ball.PreviewForVerification();
        Check(StageFeatureChecks.Children<ConversationJumpButton>(preview.Root).Count() == 1, "球待操作预览逐运行阶段有跳转按钮"); preview.Hide();
        await controller.Flush();
        edit.Session.Working.Title = "归档保留尚未提交的修改";
        var liveTitle = controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title;
        using (var locked = new FileStream(controller.Store.MainPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = false;
            try { failed = !await controller.SubmitSession(edit.Session, archive: true); }
            catch (InvalidOperationException error) { failed = error.Message.Contains("任务未能保存", StringComparison.Ordinal); }
            Check(failed, "归档写入失败返回失败");
            Check(controller.Engine.State.Tasks.Any(x => x.Id == item.Id && x.Title == liveTitle) && !controller.Engine.State.Archives.Any(x => x.Item.Id == item.Id)
                && edit.Session.Working.Title == "归档保留尚未提交的修改", "保存失败正式任务及归档未改变、编辑输入保留");
        }
        Check(await controller.SubmitSession(edit.Session, archive: true), "进行中任务可从编辑入口保存并归档");
        var disk = AtomicStore.Read(controller.Store.MainPath);
        Check(!disk.Tasks.Any(x => x.Id == item.Id) && disk.Archives.Single(x => x.Item.Id == item.Id).Item is { } archived
            && archived.Title == "归档保留尚未提交的修改" && archived.Stages[0].Id == moved.Id && archived.Stages[0].Codex!.BindingId == movedBinding,
            "磁盘回读归档新字段、排序与会话身份保持");
        Check(!disk.Preferences.EllipsizeStageDetails, "省略偏好实际落盘");
        Check(!disk.Preferences.CollapseInactiveStageDetails, "未运行折叠偏好实际落盘");
        controller.RemoveEditor(edit); edit.Destroy();
        await Task.Delay(30);
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "ux0944-report.json"), JsonSerializer.Serialize(new { checks, failures, measurements, passed = checks.Count, mouseMoved = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("；", failures));
    }
    private sealed class SideSurface() : SurfaceWindow(268, 560)
    { internal override int HorizontalRevealAnchor => -1; }
}
