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
    internal static Func<BoardController, Guid, Guid, Guid, System.Threading.Tasks.Task<ConversationNavigationResult>>? OpenOverrideForVerification { get; set; }
    internal static bool Attention(WorkItem task, Stage stage) => task.ColumnId is null && task.Lane == Lane.Action
        && stage.IsRunning == true && stage.Codex is { } binding
        && AiAttention.IsPending(binding);
    internal ConversationJumpButton(BoardController controller, WorkItem task, Stage stage, bool readOnly = false, double lineHeight = 18)
    {
        Tag = "JumpToConversation"; DataContext = stage; Width = Height = lineHeight;
        Padding = new Thickness(0); Margin = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Bottom;
        IsEnabled = !readOnly && stage.Codex is { ThreadId.Length: > 0 };
        Foreground = !IsEnabled ? Ui.DisabledIcon : Attention(task, stage) ? Ui.Attention : Ui.Muted;
        Background = Ui.HitSurface; BorderThickness = new Thickness(0); Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
        ToolTip = stage.Codex is null ? "未绑定会话" : "跳转至会话 · " + AiState.Name(stage.Codex.Platform) + " · " + stage.Codex.ThreadName
            + (Attention(task, stage) ? "\n左键跳转成功后自动消除提醒；右键也可单独消除提醒（卡片留在当前列）" : "");
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
                var result = OpenOverrideForVerification is not null
                    ? await OpenOverrideForVerification(controller, task.Id, stage.Id, binding.BindingId)
                    : await ConversationNavigator.Open(controller, task.Id, stage.Id, binding.BindingId);
                if (result.Success)
                {
                    controller.Engine.AcknowledgeStageAttention(task.Id, stage.Id, binding.BindingId);
                    controller.Ball.HidePopups();
                    controller.SetExpanded(false);
                }
                System.Windows.Automation.AutomationProperties.SetHelpText(this, result.Success ? "已跳转至绑定会话" : result.Message);
                if (!result.Success) ToolTip = result.Message;
                if (!result.Success) Ui.Hint(this, result.Message, !controller.Smoke);
            }
            finally { IsEnabled = stage.Codex is not null && !readOnly; }
        };
    }
    internal static bool Contains(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ConversationJumpButton or FrameworkElement { Tag: "ConversationJumpHitTarget" or "StageDetailsToggleBubble" }) return true;
        return false;
    }
    internal static Grid Details(BoardController controller, WorkItem task, Stage stage, double fontSize, Brush foreground,
        bool ellipsize, double maximumHeight, bool readOnly = false, bool adaptiveEllipsize = false, bool userExpanded = false,
        Action<bool>? onToggleExpanded = null)
    {
        var hasBinding = task.Stages.Any(x => x.Codex is { ThreadId.Length: > 0 });
        var interactiveBubble = adaptiveEllipsize && ellipsize && !readOnly && onToggleExpanded is not null;
        var text = new StageDetailsText(stage.Text, fontSize, foreground, ellipsize, maximumHeight,
            reserveJumpSpace: hasBinding, adaptiveEllipsize: adaptiveEllipsize, userExpanded: userExpanded, interactiveBubble: interactiveBubble);
        text.Margin = new Thickness(0);
        var lineHeight = fontSize * text.FontFamily.LineSpacing;
        text.MinHeight = lineHeight * 2;

        Button? bubble = null;
        if (interactiveBubble)
        {
            bubble = CreateDetailsToggleBubble(stage, userExpanded, lineHeight, () => onToggleExpanded!(!userExpanded));
        }

        Grid? jumpHit = null;
        if (hasBinding)
        {
            var jump = new ConversationJumpButton(controller, task, stage, readOnly, lineHeight);
            // WPF retargets disabled-control input to its enabled parent. Keep an
            // enabled, exact-size hit surface so an inactive icon cannot edit/drag
            // the card or activate the preview's outer task button.
            jumpHit = new Grid { Tag = "ConversationJumpHitTarget", Width = lineHeight, Height = lineHeight,
                Background = Ui.HitSurface, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            jumpHit.Children.Add(jump);
            jumpHit.PreviewMouseDown += (_, e) => { if (!jump.IsEnabled) e.Handled = true; };
            jumpHit.MouseUp += (_, e) => { if (!jump.IsEnabled) e.Handled = true; };
        }

        var host = new StageDetailsHost(text, bubble, jumpHit, lineHeight)
        {
            Margin = new Thickness(0, 4, 0, 4),
            Tag = "StageDetailsWithJump",
            DataContext = stage
        };
        return host;
    }

    private static Button CreateDetailsToggleBubble(Stage stage, bool expanded, double lineHeight, Action click)
    {
        var height = Math.Max(16, Math.Round(lineHeight - 2));
        var button = new Button
        {
            Tag = "StageDetailsToggleBubble",
            DataContext = stage,
            Width = StageDetailsText.BubbleWidth,
            Height = height,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Ui.BubbleSurface,
            BorderBrush = Ui.ButtonBorder,
            BorderThickness = new Thickness(1),
            Foreground = Ui.Accent,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = expanded ? "▴" : "…",
            ToolTip = expanded ? "点击收起详情" : "点击展开全部详情",
            Visibility = Visibility.Collapsed
        };
        System.Windows.Automation.AutomationProperties.SetName(button, expanded ? "收起阶段详情" : "展开阶段详情");
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "bubbleBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(height / 2));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, expanded ? -1 : -2, 0, 0));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Accent, "bubbleBorder"));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.BubbleHover, "bubbleBorder"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72, "bubbleBorder"));
        template.Triggers.Add(pressed);
        button.Template = template;
        button.Click += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    private sealed class StageDetailsHost : Grid
    {
        private readonly StageDetailsText text;
        private readonly Button? bubble;
        private readonly Grid? jumpHit;
        private readonly double lineHeight;

        internal StageDetailsHost(StageDetailsText text, Button? bubble, Grid? jumpHit, double lineHeight)
        {
            this.text = text;
            this.bubble = bubble;
            this.jumpHit = jumpHit;
            this.lineHeight = lineHeight;
            Children.Add(text);
            if (bubble is not null) Children.Add(bubble);
            if (jumpHit is not null) Children.Add(jumpHit);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            text.Measure(constraint);
            if (bubble is not null)
            {
                bubble.Visibility = text.NeedsBubble ? Visibility.Visible : Visibility.Collapsed;
                if (text.NeedsBubble) bubble.Measure(text.BubbleBounds.Size);
            }
            jumpHit?.Measure(new Size(lineHeight, lineHeight));
            var width = double.IsFinite(constraint.Width) ? constraint.Width : text.DesiredSize.Width;
            var height = Math.Max(text.DesiredSize.Height, lineHeight * 2);
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            text.Arrange(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height));
            if (bubble is not null)
            {
                bubble.Visibility = text.NeedsBubble ? Visibility.Visible : Visibility.Collapsed;
                if (text.NeedsBubble) bubble.Arrange(text.BubbleBounds);
            }
            jumpHit?.Arrange(new Rect(Math.Max(0, arrangeSize.Width - lineHeight), Math.Max(0, arrangeSize.Height - lineHeight), lineHeight, lineHeight));
            return arrangeSize;
        }
    }
}
