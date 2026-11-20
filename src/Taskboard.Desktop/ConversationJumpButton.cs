using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class ConversationJumpButton : Button
{
    internal static bool Attention(WorkItem task, Stage stage) => task.ColumnId is null && task.Lane == Lane.Action
        && stage.IsRunning == true && stage.Codex is { } binding
        && AiAttention.IsPending(binding);
    internal ConversationJumpButton(BoardController controller, WorkItem task, Stage stage, bool readOnly = false, double lineHeight = 18)
    {
        Tag = "JumpToConversation"; DataContext = stage; Width = Height = lineHeight;
        Padding = new Thickness(0); Margin = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Bottom;
        IsEnabled = !readOnly && stage.Codex is { ThreadId.Length: > 0 };
        Foreground = !IsEnabled ? Ui.Brush("#536071") : Attention(task, stage) ? Ui.Brush("#FF704D") : Ui.Muted;
        Background = Ui.Brush("#01192333"); BorderThickness = new Thickness(0); Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
        ToolTip = stage.Codex is null ? "未绑定会话" : "跳转至会话 · " + AiState.Name(stage.Codex.Platform) + " · " + stage.Codex.ThreadName
            + (Attention(task, stage) ? "\n右键单击：标记此提醒已处理，保持当前列表" : "");
        System.Windows.Automation.AutomationProperties.SetName(this, stage.Codex is null ? "跳转至会话，未绑定" : "跳转至会话：" + stage.Codex.ThreadName);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "JumpConversation." + task.Id + "." + stage.Id);
        var frame = new FrameworkElementFactory(typeof(Border)); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        frame.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter);
        Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        var canvas = new Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
        canvas.Children.Add(new Path { Data = Geometry.Parse("M 8,3 L 5,3 Q 2,3 2,6 L 2,13 Q 2,16 5,16 L 12,16 Q 15,16 15,13 L 15,10 M 8,10 L 16,2 M 10,2 L 16,2 L 16,8"),
            Stroke = Foreground, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        Content = new Viewbox { Width = Math.Min(18, lineHeight - 2), Height = Math.Min(18, lineHeight - 2), Child = canvas };
        PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
        PreviewMouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!readOnly && IsEnabled && stage.Codex is { } binding)
                controller.Engine.AcknowledgeStageAttention(task.Id, stage.Id, binding.BindingId);
        };
        ContextMenuOpening += (_, e) => e.Handled = true;
        Click += async (_, e) =>
        {
            e.Handled = true; if (!IsEnabled || stage.Codex is not { } binding) return;
            IsEnabled = false;
            try
            {
                var result = await ConversationNavigator.Open(controller, task.Id, stage.Id, binding.BindingId);
                if (result.Success) { controller.Ball.HidePopups(); controller.SetExpanded(false); }
                System.Windows.Automation.AutomationProperties.SetHelpText(this, result.Success ? "已跳转至绑定会话" : result.Message);
                if (!result.Success) ToolTip = result.Message;
                if (!result.Success) Ui.Hint(this, result.Message, !controller.Smoke);
            }
            finally { if (IsLoaded) IsEnabled = stage.Codex is not null && !readOnly; }
        };
    }
    internal static bool Contains(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ConversationJumpButton or FrameworkElement { Tag: "ConversationJumpHitTarget" }) return true;
        return false;
    }
    internal static Grid Details(BoardController controller, WorkItem task, Stage stage, double fontSize, Brush foreground,
        bool ellipsize, double maximumHeight, bool readOnly = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), Tag = "StageDetailsWithJump", DataContext = stage };
        var hasBinding = task.Stages.Any(x => x.Codex is { ThreadId.Length: > 0 });
        var text = new StageDetailsText(stage.Text, fontSize, foreground, ellipsize, maximumHeight, reserveJumpSpace: hasBinding);
        text.Margin = new Thickness(0);
        var lineHeight = fontSize * text.FontFamily.LineSpacing;
        text.MinHeight = lineHeight * 2;
        grid.Children.Add(text);
        if (!hasBinding) return grid;
        var jump = new ConversationJumpButton(controller, task, stage, readOnly, lineHeight);
        // WPF retargets disabled-control input to its enabled parent. Keep an
        // enabled, exact-size hit surface so an inactive icon cannot edit/drag
        // the card or activate the preview's outer task button.
        var hit = new Grid { Tag = "ConversationJumpHitTarget", Width = lineHeight, Height = lineHeight,
            Background = Ui.Brush("#01192333"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        hit.Children.Add(jump);
        hit.PreviewMouseDown += (_, e) => { if (!jump.IsEnabled) e.Handled = true; };
        hit.MouseUp += (_, e) => { if (!jump.IsEnabled) e.Handled = true; };
        grid.Children.Add(hit);
        return grid;
    }
}
