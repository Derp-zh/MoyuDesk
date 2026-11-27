using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private SurfaceWindow? helpImagePreview;
    private sealed record Help(string Title, string Description, string Kind);
    private static readonly Dictionary<string, Help> HelpTopics = new()
    {
        ["RunningFocusMode"] = new("只看任务是否运行", "关闭：显示原来的各个列表。开启：进行中与待操作为两个宽窗，卡片自动分列。未勾选已完成时，拖动待操作卡片会临时展开右侧窄窗，结束或取消后自动收起；勾选则固定展开。右侧预留窄窗位置，开合不改变两个宽窗。其他列表任务保留，关闭本模式即可返回。", "focus"),
        ["EllipsizeStageDetails"] = new("列表详情显示多少", "关闭：完整显示详情，卡片随内容增高。开启：超出可见范围的内容用省略号表示。完整内容随时可在任务编辑中查看。", "details"),
        ["CollapseInactiveStageDetails"] = new("让未运行的阶段更紧凑", "关闭：未运行阶段也至少显示两行。开启：未编辑时显示一行，空白也保留一行。点击详情后会完整展开，离开输入框恢复一行，文字始终保留。", "inactive"),
        ["AutoOpenConversationBindingWindow"] = new("拖动时自动找到会话列表", "关闭：拖动绑定时不自动展开右侧会话列表。开启：开始拖动后展开会话选择窗口，拖到准确的会话上即可绑定。", "binding"),
        ["CodexBehaviorSupplement"] = new("识别需要你处理的时刻", "关闭：保留基础结构监听。开启：补充识别提问、批准、执行结束和恢复，让卡片在进行中与待操作之间移动；需先开启 Codex 通知权限。", "attention"),
        ["BallEdgeAutoHide"] = new("悬浮球靠边时自动收起", "关闭：悬浮球保持原外形。开启：吸附到最近的左侧或右侧边缘，鼠标离开后收起成小条，移上恢复。", "edge"),
        ["InspirationQuickEntry"] = new("灵感直接记在列表里", "关闭：点新建打开完整编辑窗口。开启：在灵感库列表内快速输入，之后也可转成完整编辑。", "quick"),
    };
    private StackPanel FocusViewSettings()
    {
        var panel = new StackPanel { Tag = "FocusViewSettings" };
        var title = Ui.Label("浏览方式", 15); title.FontWeight = FontWeights.SemiBold; panel.Children.Add(title);
        void Add(string key, string caption, bool value, Action<bool> change)
        {
            var check = new CheckBox { Content = caption, Tag = key, IsChecked = value, Margin = new Thickness(0, 8, 0, 8) };
            check.Click += (_, _) => change(check.IsChecked == true); panel.Children.Add(check);
        }
        Add("RunningFocusMode", "只看任务是否运行", controller.FocusMode, controller.SetFocusMode);
        Add("QuickAiOnAllColumns", "在所有列表标题显示 +AI", controller.Engine.State.Preferences.QuickAiOnAllColumns, value =>
        { controller.Engine.State.Preferences.QuickAiOnAllColumns = value; controller.ApplySettings(); controller.Engine.Touch(); });
        Add("CompletedChronological", "已完成按完成时间从早到晚排列", controller.Engine.State.Preferences.CompletedChronological, value =>
        { controller.Engine.State.Preferences.CompletedChronological = value; foreach (var column in controller.Columns) column.Refresh(); controller.Engine.Touch(); });
        return panel;
    }
    private void AttachHelp(DependencyObject root)
    {
        // Logical children are already available before a page is displayed.
        if (root is CheckBox check && check.Tag is string key && HelpTopics.ContainsKey(key) && (check.Content is string || check.Content is TextBlock))
        {
            var caption = check.Content is TextBlock label ? label.Text : (string)check.Content;
            var content = new DockPanel { LastChildFill = true };
            var button = Ui.Button("?", () => OpenHelp(check, key)); button.Tag = "SettingsHelp"; button.DataContext = key;
            button.Width = button.Height = 22; button.MinWidth = 0; button.Padding = new Thickness(0); button.Margin = new Thickness(7, 0, 0, 0);
            button.VerticalAlignment = VerticalAlignment.Center; button.ToolTip = "查看图解：" + HelpTopics[key].Title;
            System.Windows.Automation.AutomationProperties.SetName(button, "图解帮助：" + HelpTopics[key].Title);
            var chrome = new FrameworkElementFactory(typeof(Border)); chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            chrome.SetValue(Border.BackgroundProperty, Ui.HitSurface);
            chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            if (Application.Current?.TryFindResource("MutedBrush") is Brush) chrome.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
            else chrome.SetValue(Border.BorderBrushProperty, Ui.Muted);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            DockPanel.SetDock(button, Dock.Right); content.Children.Add(button);
            var text = Ui.Label(caption, check.FontSize); text.TextWrapping = TextWrapping.Wrap; text.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(text);
            check.Content = content; return;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray()) AttachHelp(child);
    }
    private void OpenHelp(FrameworkElement anchor, string key)
    {
        if (!HelpTopics.TryGetValue(key, out var help)) return;
        helpImagePreview?.Destroy(false);
        var area = controller.WorkArea(this);
        var desiredWidth = help.Kind == "focus" ? 1500 : help.Kind == "edge" ? 680 : 1180;
        var desiredHeight = help.Kind == "edge" ? 430 : help.Kind is "details" or "inactive" or "attention" ? 650 : 820;
        var viewer = new HelpComparisonWindow(Math.Max(1, Math.Min(area.Width - 32, desiredWidth)), Math.Max(1, Math.Min(area.Height - 32, desiredHeight)));
        helpImagePreview = viewer; viewer.Title = "功能帮助 · " + help.Title;
        viewer.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewer.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewer.Root.RowDefinitions.Add(new RowDefinition());
        viewer.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(18, 8, 12, 4), Background = Brushes.Transparent };
        var close = Ui.Button("×", viewer.Destroy); close.Tag = "HelpScreenshotClose"; close.Width = close.Height = 44; close.FontSize = 22; close.Padding = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = Ui.Label(help.Title, 17); title.VerticalAlignment = VerticalAlignment.Center; title.TextWrapping = TextWrapping.Wrap; header.Children.Add(title);
        viewer.Root.Children.Add(header); viewer.MoveWith(header);
        var description = Ui.Label(help.Description, 13); description.Tag = "HelpDescription"; description.TextWrapping = TextWrapping.Wrap;
        description.LineHeight = 21; description.Margin = new Thickness(22, 0, 22, 10); Grid.SetRow(description, 1); viewer.Root.Children.Add(description);
        var images = new Grid { Margin = new Thickness(16, 0, 16, 6), Tag = "HelpComparison" }; Grid.SetRow(images, 2); viewer.Root.Children.Add(images);
        var pictures = new Image[2]; var frames = new Border[2];
        for (int i = 0; i < 2; i++)
        {
            var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
            var caption = Ui.Label(i == 0 ? "关闭此选项" : "开启此选项", 13, i == 0 ? Ui.Muted : Ui.Accent); caption.Margin = new Thickness(10, 6, 10, 6); body.Children.Add(caption);
            var picture = pictures[i] = new Image { Source = HelpImage(help.Kind, i == 1), Stretch = Stretch.Uniform,
                Tag = "SettingsHelpImage", DataContext = help.Kind + (i == 1 ? "-on" : "-off"), Margin = new Thickness(8, 0, 8, 8) };
            Grid.SetRow(picture, 1); body.Children.Add(picture);
            var frame = frames[i] = new Border { Child = body, CornerRadius = new CornerRadius(10), BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1), Margin = new Thickness(4) }; images.Children.Add(frame);
        }
        bool? stacked = null;
        void LayoutPhotos()
        {
            var width = images.ActualWidth; var height = images.ActualHeight;
            if (width <= 1 || height <= 1) return;
            double Score(bool stack)
            {
                var cellWidth = stack ? width - 26 : width / 2 - 26;
                var cellHeight = (stack ? height / 2 : height) - 48;
                return pictures.Min(p => Math.Min(cellWidth / p.Source.Width, cellHeight / p.Source.Height));
            }
            var next = Score(true) > Score(false) * 1.05;
            if (stacked == next) return; stacked = next; images.RowDefinitions.Clear(); images.ColumnDefinitions.Clear();
            if (next) { images.RowDefinitions.Add(new RowDefinition()); images.RowDefinitions.Add(new RowDefinition()); }
            else { images.ColumnDefinitions.Add(new ColumnDefinition()); images.ColumnDefinitions.Add(new ColumnDefinition()); }
            for (int i = 0; i < 2; i++) { Grid.SetRow(frames[i], next ? i : 0); Grid.SetColumn(frames[i], next ? 0 : i); }
        }
        images.SizeChanged += (_, _) => LayoutPhotos();
        var footer = Ui.Label("真实程序截图 · 聚焦功能区域", 11, Ui.Muted); footer.Margin = new Thickness(22, 0, 22, 12); Grid.SetRow(footer, 3); viewer.Root.Children.Add(footer);
        viewer.Root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { viewer.Destroy(); anchor.Focus(); e.Handled = true; } };
        viewer.Closed += (_, _) => { if (ReferenceEquals(helpImagePreview, viewer)) helpImagePreview = null; };
        controller.RegisterAuxiliary(viewer, positionOnOpen: false); viewer.Left = area.Left + (area.Width - viewer.Width) / 2; viewer.Top = area.Top + (area.Height - viewer.Height) / 2;
        controller.Clamp(viewer); viewer.Show(); viewer.Activate(); close.Focus();
    }
    private sealed class HelpComparisonWindow(double width, double height) : SurfaceWindow(width, height)
    {
        internal override double GlassTransparency => 0;
    }
}
