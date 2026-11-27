using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskboard.Desktop;

// Shared 26x26 circular pushpin toggle button used identically by the Action
// column header and the floating unpin overlay window.
internal static class ActionPinIcon
{
    internal const double ButtonDiameter = 26;
    internal const double IconViewboxSize = 18;
    internal static readonly Geometry PinBodyGeometry = CreatePinBodyGeometry();
    internal static readonly Geometry PinNeedleGeometry = CreatePinNeedleGeometry();

    private static Geometry CreatePinBodyGeometry()
    {
        // Smooth rounded top end (no left/right protruding cap ears) flowing directly
        // into the stem, keeping the wide lower flange and filling ~85% of the 16x16 canvas.
        var g = Geometry.Parse(
            "M 6.7,1.3 L 9.3,1.3 " +
            "Q 10.2,1.3 10.2,2.3 " +
            "C 10.2,4.2 10.3,5.8 10.7,7.2 " +
            "L 12.2,8.0 Q 13.0,8.45 13.0,9.2 Q 13.0,10.0 12.1,10.0 " +
            "L 3.9,10.0 Q 3.0,10.0 3.0,9.2 Q 3.0,8.45 3.8,8.0 " +
            "L 5.3,7.2 C 5.7,5.8 5.8,4.2 5.8,2.3 " +
            "Q 5.8,1.3 6.7,1.3 Z");
        g.Freeze();
        return g;
    }

    private static Geometry CreatePinNeedleGeometry()
    {
        var g = Geometry.Parse("M 7.15,9.7 L 8.85,9.7 L 8.28,14.45 Q 8.0,15.0 7.72,14.45 Z");
        g.Freeze();
        return g;
    }

    internal static Button CreateButton(bool pinned, Action onClick)
    {
        var button = new Button
        {
            Width = ButtonDiameter,
            Height = ButtonDiameter,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        button.Click += (_, _) => onClick();
        ApplyState(button, pinned);
        return button;
    }

    internal static void ApplyState(Button button, bool pinned)
    {
        var p = ThemePalette.Current;
        button.Template = CreateCircularTemplate(pinned);
        button.Background = pinned ? p.PinPinnedSurface : p.ButtonSurface;
        button.BorderBrush = pinned ? p.PinPinnedBorder : p.ButtonBorder;
        button.Foreground = pinned ? p.PinPinnedFill : p.PinIdleStroke;
        button.Content = CreateIconVisual(pinned);
        if (pinned)
        {
            button.ToolTip = "点击解除常驻";
            System.Windows.Automation.AutomationProperties.SetName(button, "已常驻穿透，点击解除常驻");
        }
        else
        {
            button.ToolTip = "常驻桌面并开启鼠标穿透（悬停可滚轮滚动，点击图钉可解除）";
            System.Windows.Automation.AutomationProperties.SetName(button, "常驻桌面并开启鼠标穿透");
        }
    }

    internal static Viewbox CreateIconVisual(bool pinned)
    {
        var p = ThemePalette.Current;
        var canvas = new Canvas
        {
            Width = 16,
            Height = 16,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(pinned ? 28 : 40)
        };
        var body = new Path
        {
            Tag = "ActionPinBody",
            Data = PinBodyGeometry,
            Fill = pinned ? p.PinPinnedFill : p.PinIdleFill,
            Stroke = pinned ? p.PinPinnedStroke : p.PinIdleStroke,
            StrokeThickness = pinned ? 0.8 : 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = false
        };
        var needle = new Path
        {
            Tag = "ActionPinNeedle",
            Data = PinNeedleGeometry,
            Fill = pinned ? p.PinPinnedStroke : p.PinIdleStroke,
            SnapsToDevicePixels = false
        };
        canvas.Children.Add(needle);
        canvas.Children.Add(body);
        return new Viewbox
        {
            Width = IconViewboxSize,
            Height = IconViewboxSize,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Child = canvas
        };
    }

    private static ControlTemplate CreateCircularTemplate(bool pinned)
    {
        var p = ThemePalette.Current;
        var circle = new FrameworkElementFactory(typeof(Border));
        circle.Name = "PinCircleBorder";
        circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(ButtonDiameter / 2));
        circle.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        circle.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        circle.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        circle.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        circle.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        circle.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = circle };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, pinned ? p.PinPinnedHoverBorder : p.Accent, "PinCircleBorder"));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, pinned ? p.PinPinnedHoverSurface : p.ButtonHover, "PinCircleBorder"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72, "PinCircleBorder"));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "PinCircleBorder"));
        template.Triggers.Add(disabled);

        return template;
    }
}
