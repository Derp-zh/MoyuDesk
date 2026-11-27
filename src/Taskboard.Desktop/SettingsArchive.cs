using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private bool archiveAllLoaded;
    internal void RefreshArchive() { if (CurrentSection == "归档") RefreshSection(); }
    private StackPanel ArchiveSettings()
    {
        var archives = controller.Engine.State.Archives;
        var total = archives.Count;
        var panel = new StackPanel { Tag = "ArchivePage" };
        var heading = Ui.Label("归档", 20); heading.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(heading);
        var intro = Ui.Label("管理归档规则，查看或恢复已归档的任务。", 12, Ui.Muted);
        intro.Margin = new Thickness(0, 2, 0, 14); panel.Children.Add(intro);
        var preferences = controller.Engine.State.Preferences;
        var policy = new StackPanel { Tag = "ArchivePolicy" };
        var automatic = new CheckBox { Content = "自动归档已完成任务", IsChecked = preferences.AutoArchiveCompleted, Tag = "AutoArchiveCompleted", Margin = new Thickness(0, 0, 0, 10) };
        policy.Children.Add(automatic);
        var daysRow = new WrapPanel { IsEnabled = preferences.AutoArchiveCompleted, Margin = new Thickness(22, 0, 0, 0) };
        var daysCaption = Ui.Label("完成后超过", 12); daysCaption.VerticalAlignment = VerticalAlignment.Center; daysRow.Children.Add(daysCaption);
        var days = Ui.Input(preferences.AutoArchiveAfterDays.ToString()); days.Width = 72; days.Tag = "AutoArchiveDays";
        days.Margin = new Thickness(8, 0, 8, 0); days.Padding = new Thickness(9, 5, 9, 5); days.MinHeight = 30;
        System.Windows.Automation.AutomationProperties.SetName(days, "自动归档天数"); daysRow.Children.Add(days);
        var suffix = Ui.Label("天自动归档", 12); suffix.VerticalAlignment = VerticalAlignment.Center; daysRow.Children.Add(suffix); policy.Children.Add(daysRow);
        var error = Ui.Label("", 11, Ui.Danger); error.Tag = "ArchiveDaysError"; error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(22, 6, 0, 0); policy.Children.Add(error);
        async void ApplyDays()
        {
            if (!int.TryParse(days.Text, out var value) || value is < 1 or > 36500)
            { error.Text = "请输入1至36500的整数天数。"; error.Visibility = Visibility.Visible; return; }
            error.Text = ""; error.Visibility = Visibility.Collapsed;
            if (preferences.AutoArchiveAfterDays == value) return;
            preferences.AutoArchiveAfterDays = value; controller.Engine.Touch(); await controller.CheckAutomaticArchives(true);
        }
        days.LostKeyboardFocus += (_, _) => ApplyDays(); days.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyDays(); e.Handled = true; } };
        automatic.Click += async (_, _) => { preferences.AutoArchiveCompleted = automatic.IsChecked == true; daysRow.IsEnabled = preferences.AutoArchiveCompleted; controller.Engine.Touch(); await controller.CheckAutomaticArchives(true); };
        var ruleNote = Ui.Label("从进入“已完成”开始计时；修改内容不重置时间。", 11, Ui.Muted);
        ruleNote.Margin = new Thickness(22, 8, 0, 12); policy.Children.Add(ruleNote);
        policy.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 0, 0, 12) });
        var purge = new CheckBox { Content = "只保留最近40条归档", IsChecked = preferences.AutoDeleteArchives, Tag = "AutoDeleteArchives", Margin = new Thickness(0) };
        purge.Click += async (_, _) =>
        {
            var enabled = purge.IsChecked == true;
            if (enabled && controller.Engine.State.Archives.Count > 40 && !await Ui.Confirm("开启后将永久删除较早的" + (controller.Engine.State.Archives.Count - 40) + "条归档，只保留最近40条。", "开启并删除")) { purge.IsChecked = false; return; }
            preferences.AutoDeleteArchives = enabled; controller.TrimArchives(); controller.Engine.Touch(); RefreshArchive();
        };
        policy.Children.Add(purge);
        var purgeNote = Ui.Label("默认关闭，保留全部。开启后，超出的较早归档将永久删除。", 11, Ui.Muted);
        purgeNote.Margin = new Thickness(22, 6, 0, 0); policy.Children.Add(purgeNote);
        panel.Children.Add(SettingsCard(policy));
        var listHeading = new DockPanel { Margin = new Thickness(0, 2, 0, 10) };
        var count = Ui.Label(total + " 条", 12, Ui.Muted); DockPanel.SetDock(count, Dock.Right); listHeading.Children.Add(count);
        var title = Ui.Label("已归档任务", 15); title.FontWeight = FontWeights.SemiBold; listHeading.Children.Add(title); panel.Children.Add(listHeading);
        if (total == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 8, 0, 8), Tag = "ArchiveEmptyState" };
            var emptyTitle = Ui.Label("暂无归档任务", 14); emptyTitle.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(emptyTitle);
            var emptyNote = Ui.Label("归档后的任务会保留在这里，随时可以恢复。", 12, Ui.Muted);
            emptyNote.TextAlignment = TextAlignment.Center; empty.Children.Add(emptyNote); panel.Children.Add(SettingsCard(empty));
        }
        foreach (var archived in archives.AsEnumerable().Reverse().Take(archiveAllLoaded ? int.MaxValue : 20))
        {
            var entry = new StackPanel();
            var card = new TaskCard { ArchivedPreview = true, CompactArchivedPreview = true, Controller = controller, DataContext = archived.Item, Cursor = Cursors.Hand };
            card.BorderThickness = new Thickness(0); card.Background = Brushes.Transparent;
            card.Margin = new Thickness(0); card.Padding = new Thickness(0); entry.Children.Add(card);
            var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var metadata = Ui.Label(archived.ArchivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + archived.OriginalColumn.Name, 11, Ui.Muted);
            metadata.VerticalAlignment = VerticalAlignment.Center; metadata.Margin = new Thickness(0, 0, 8, 0);
            metadata.TextWrapping = TextWrapping.NoWrap; metadata.TextTrimming = TextTrimming.CharacterEllipsis;
            metadata.ToolTip = "归档时间与原列表：" + metadata.Text; footer.Children.Add(metadata);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var restore = Ui.Button("取消归档", () => controller.UnarchiveTask(archived.Item.Id));
            restore.Tag = "RestoreArchivedTask"; restore.Height = 30; restore.Padding = new Thickness(11, 4, 11, 4); actions.Children.Add(restore);
            var remove = Ui.Button("×", async () => await controller.DeleteArchivedTask(archived.Item.Id));
            remove.Tag = "DeleteArchivedTask"; remove.Width = remove.Height = 26; remove.Padding = new Thickness(0); remove.ToolTip = "永久删除归档任务";
            remove.Foreground = Ui.Danger; Ui.ApplyCircularStyle(remove); actions.Children.Add(remove); Grid.SetColumn(actions, 1); footer.Children.Add(actions); entry.Children.Add(footer);
            var frame = SettingsCard(entry); frame.Tag = "ArchiveTaskEntry"; frame.DataContext = archived;
            panel.Children.Add(frame);
        }
        if (!archiveAllLoaded && total > 20) { var more = Ui.Button("加载更多（显示全部" + total + "条）", () => { archiveAllLoaded = true; RefreshArchive(); }); more.Tag = "LoadAllArchives"; panel.Children.Add(more); }
        return panel;
    }
}
