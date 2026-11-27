using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class OnboardingSurface : SurfaceWindow
{
    private readonly record struct Step(string Title, string Text, string Action, Action Run);
    private readonly Step[] steps;
    private readonly TextBlock heading = Ui.Label("", 18), progress = Ui.Label("", 11, Ui.Accent), description = Ui.Label("", 14);
    private readonly Button next, back, action;
    private int index;
    internal OnboardingSurface(BoardController controller) : base(392, 340)
    {
        Title = "新手教程"; WindowLayer = 3;
        steps = [
            new("从悬浮球开始", "点击悬浮球可以展开或收起整个看板。收起后任务提醒仍会继续；右键可以找到设置和退出。", "展开看板", () => controller.SetExpanded(true)),
            new("先记下一件事", "每个列表底部都能新建任务。写下名称或详情即可，其他字段按需填写。灵感库支持直接输入，也能转到完整编辑。", "打开新建任务", () => controller.NewTask(Lane.Running)),
            new("拖动整理进度", "按住任务卡片拖动，邻卡会自动让位。只看运行状态时有两个宽窗；拖待操作卡片会临时展开已完成，勾选已完成则保持展开。", "查看浏览设置", () => controller.OpenOnboardingSettings("窗口")),
            new("AI会话按需关联", "使用AI时，可把阶段关联到准确会话，再开启相应监听。等待你处理时卡片会提醒；无需AI也能正常使用看板。", "查看AI平台设置", () => controller.OpenOnboardingSettings("AI平台接入情况")),
            new("让提醒帮你记住", "在任务编辑中打开定时提醒，选择日期和时间；支持一次、每天或每周。设置旁的问号可以看功能前后对比。教程以后也能在常规设置中重看。", "打开常规设置", () => controller.OpenOnboardingSettings("主设置"))
        ];
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Root.RowDefinitions.Add(new RowDefinition()); Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new DockPanel { Margin = new Thickness(16, 10, 12, 4), Background = Brushes.Transparent };
        var close = Ui.Button("×", Destroy); close.Tag = "TutorialClose"; close.Width = 48; close.Height = 40; close.Padding = new Thickness(0);
        if (Application.Current?.TryFindResource("EditorCloseButton") is Style closeStyle) close.Style = closeStyle;
        else { close.Background = Ui.CloseButtonSurface; close.BorderBrush = Ui.CloseButtonBorder; close.Foreground = Ui.CloseButtonText; }
        DockPanel.SetDock(close, Dock.Right); top.Children.Add(close); top.Children.Add(Ui.Label("欢迎使用" + Brand.Name, 16)); Root.Children.Add(top); MoveWith(top);
        var body = new StackPanel { Margin = new Thickness(20, 2, 20, 8) }; progress.Tag = "TutorialProgress"; heading.Tag = "TutorialHeading"; body.Children.Add(progress); body.Children.Add(heading);
        description.LineHeight = 25; description.Margin = new Thickness(0, 8, 0, 12); body.Children.Add(description); action = Ui.Button("", () => steps[index].Run()); action.Tag = "TutorialAction"; action.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(action);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(12, 4, 12, 12) }; var skip = Ui.Button("跳过教程", Destroy); skip.Tag = "TutorialSkip"; DockPanel.SetDock(skip, Dock.Left); footer.Children.Add(skip);
        next = Ui.Button("下一步", () => { if (index == steps.Length - 1) Destroy(); else { index++; Refresh(); } }, true); next.Tag = "TutorialNext"; DockPanel.SetDock(next, Dock.Right); footer.Children.Add(next);
        back = Ui.Button("上一步", () => { if (index > 0) { index--; Refresh(); } }); back.Tag = "TutorialBack"; footer.Children.Add(back); Grid.SetRow(footer, 2); Root.Children.Add(footer);
        Root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Destroy(); e.Handled = true; } };
        Refresh();
    }
    private void Refresh() { var step = steps[index]; heading.Text = step.Title; description.Text = step.Text; progress.Text = $"{index + 1} / {steps.Length}"; action.Content = step.Action; back.IsEnabled = index > 0; next.Content = index == steps.Length - 1 ? "开始使用" : "下一步"; }
}
