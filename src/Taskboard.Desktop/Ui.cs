using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal static partial class Ui
{
    public static Brush Text => ThemePalette.Current.Text;
    public static Brush Muted => ThemePalette.Current.Muted;
    public static Brush Surface => ThemePalette.Current.Surface;
    public static Brush HitSurface => ThemePalette.Current.HitSurface;
    public static Brush Field => ThemePalette.Current.Field;
    public static Brush Card => ThemePalette.Current.Card;
    public static Brush CardSolid => ThemePalette.Current.CardSolid;
    public static Brush StagePanelSurface => ThemePalette.Current.StagePanel;
    public static Brush Line => ThemePalette.Current.Line;
    public static Brush Accent => ThemePalette.Current.Accent;
    public static Brush ButtonSurface => ThemePalette.Current.ButtonSurface;
    public static Brush ButtonHover => ThemePalette.Current.ButtonHover;
    public static Brush ButtonPressed => ThemePalette.Current.ButtonPressed;
    public static Brush ButtonBorder => ThemePalette.Current.ButtonBorder;
    public static Brush ButtonText => ThemePalette.Current.ButtonText;
    public static Brush AccentHover => ThemePalette.Current.AccentHover;
    public static Brush AccentPressed => ThemePalette.Current.AccentPressed;
    public static Brush AccentBorder => ThemePalette.Current.AccentBorder;
    public static Brush AccentText => ThemePalette.Current.AccentText;
    public static Brush CloseButtonSurface => ThemePalette.Current.CloseButtonSurface;
    public static Brush CloseButtonBorder => ThemePalette.Current.CloseButtonBorder;
    public static Brush CloseButtonText => ThemePalette.Current.CloseButtonText;
    public static Brush CheckSurface => ThemePalette.Current.CheckSurface;
    public static Brush CheckHoverSurface => ThemePalette.Current.CheckHoverSurface;
    public static Brush CheckBorder => ThemePalette.Current.CheckBorder;
    public static Brush CheckCheckedFill => ThemePalette.Current.CheckCheckedFill;
    public static Brush CheckMark => ThemePalette.Current.CheckMark;
    public static Brush InputBorder => ThemePalette.Current.InputBorder;
    public static Brush SliderTrack => ThemePalette.Current.SliderTrack;
    public static Brush MenuSurface => ThemePalette.Current.MenuSurface;
    public static Brush PillBackground => ThemePalette.Current.PillBackground;
    public static Brush HintBackground => ThemePalette.Current.HintBackground;
    public static Brush BubbleSurface => ThemePalette.Current.BubbleSurface;
    public static Brush BubbleHover => ThemePalette.Current.BubbleHover;
    public static Brush DashedStroke => ThemePalette.Current.DashedStroke;
    public static Brush Danger => ThemePalette.Current.Danger;
    public static Brush Warn => ThemePalette.Current.Warn;
    public static Brush Attention => ThemePalette.Current.Attention;
    public static Brush DisabledIcon => ThemePalette.Current.DisabledIcon;

    public static Brush Brush(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b;
    }

    internal static SolidColorBrush FreezeColor(Color color)
    {
        var b = new SolidColorBrush(color); b.Freeze(); return b;
    }

    internal static void BindBrush(FrameworkElement element, DependencyProperty property, string resourceKey, Brush fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush)
            element.SetResourceReference(property, resourceKey);
        else
            element.SetValue(property, fallback);
    }

    internal static Brush ReadableTagBrush(string colorHex, Brush? background = null)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(colorHex);
        var palette = ThemePalette.Current;
        if (palette.IsDark) return FreezeColor(baseColor);
        var bgColor = background is SolidColorBrush sb
            ? ThemePalette.CompositeOver(sb.Color, palette.SurfaceColor)
            : palette.PillBackground.Color;
        if (ThemePalette.ContrastRatio(baseColor, bgColor) >= 4.2) return FreezeColor(baseColor);
        var c = baseColor;
        for (var step = 0; step < 12 && ThemePalette.ContrastRatio(c, bgColor) < 4.5; step++)
        {
            c = Color.FromRgb(
                (byte)Math.Round(c.R * 0.82),
                (byte)Math.Round(c.G * 0.82),
                (byte)Math.Round(c.B * 0.82));
        }
        return FreezeColor(c);
    }

    public static TextBlock Label(string text, double size = 13, Brush? color = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        var p = ThemePalette.Current;
        if (color is null || ReferenceEquals(color, p.Text)) BindBrush(label, TextBlock.ForegroundProperty, "TextBrush", p.Text);
        else if (ReferenceEquals(color, p.Muted)) BindBrush(label, TextBlock.ForegroundProperty, "MutedBrush", p.Muted);
        else if (ReferenceEquals(color, p.Accent)) BindBrush(label, TextBlock.ForegroundProperty, "AccentBrush", p.Accent);
        else if (ReferenceEquals(color, p.Danger)) BindBrush(label, TextBlock.ForegroundProperty, "DangerBrush", p.Danger);
        else if (ReferenceEquals(color, p.Warn)) BindBrush(label, TextBlock.ForegroundProperty, "WarnBrush", p.Warn);
        else label.Foreground = color;
        return label;
    }

    public static Button Button(string text, Action click, bool accent = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(3),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        if (Application.Current?.TryFindResource(accent ? "AccentButton" : typeof(Button)) is Style style)
            button.Style = style;
        else
        {
            button.Background = accent ? Accent : ButtonSurface;
            button.Foreground = accent ? AccentText : ButtonText;
            button.BorderBrush = accent ? AccentBorder : ButtonBorder;
        }
        button.Click += (_, _) => click();
        return button;
    }

    public static void ApplyCircularStyle(Button button)
    {
        if (Application.Current?.TryFindResource("CircularIconButton") is Style style)
            button.Style = style;
    }

    public static TextBox Input(string text = "", bool multiline = false)
    {
        var box = new TextBox
        {
            Text = text,
            Padding = new Thickness(9),
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 13,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 80 : 34,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        BindBrush(box, Control.ForegroundProperty, "TextBrush", Text);
        BindBrush(box, Control.BackgroundProperty, "FieldBrush", Field);
        BindBrush(box, Control.BorderBrushProperty, "InputBorderBrush", InputBorder);
        return box;
    }

    public static Grid DashedButton(string text, Action click)
    {
        var grid = new Grid { Margin = new Thickness(3), MinHeight = 38 };
        var outline = new System.Windows.Shapes.Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 4]), IsHitTestVisible = false };
        BindBrush(outline, System.Windows.Shapes.Shape.StrokeProperty, "DashedStrokeBrush", DashedStroke);
        grid.Children.Add(outline);
        var button = Button(text, click); button.Tag = "NewTaskEntry"; button.Margin = new Thickness(0);
        if (Application.Current?.TryFindResource("DashedEntryButton") is Style dashedStyle) button.Style = dashedStyle;
        // Layered child windows pass fully transparent pixels to the desktop.
        // Keep an imperceptible alpha across the whole outlined hit target.
        button.Background = HitSurface; button.BorderThickness = new Thickness(0); grid.Children.Add(button); return grid;
    }

    public static Border Panel(UIElement child, Brush? background = null, double padding = 12)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var p = ThemePalette.Current;
        if (background is null || ReferenceEquals(background, p.Card)) BindBrush(border, Border.BackgroundProperty, "CardPanelBrush", p.Card);
        else if (ReferenceEquals(background, p.StagePanel)) BindBrush(border, Border.BackgroundProperty, "StagePanelBrush", p.StagePanel);
        else border.Background = background;
        BindBrush(border, Border.BorderBrushProperty, "LineBrush", p.Line);
        return border;
    }

    public static Border Pill(string name, string color)
    {
        var fg = ReadableTagBrush(color, PillBackground);
        var border = new Border
        {
            Child = Label(name, 11, fg),
            BorderBrush = fg,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(0, 3, 5, 0),
            CornerRadius = new CornerRadius(12)
        };
        BindBrush(border, Border.BackgroundProperty, "PillBackgroundBrush", PillBackground);
        return border;
    }

    public static Action<SurfaceWindow>? WindowCreated { get; set; }
    public static void Error(string text)
    {
        var window = MessageSurface(text); var body = (StackPanel)((ScrollViewer)window.Root.Children[0]).Content;
        body.Children.Add(Button("知道了", window.Destroy)); window.Show(); window.Activate(); Native.RaiseBall();
    }
    public static Task<bool> Confirm(string text, string accept = "确认导入")
    {
        var completion = new TaskCompletionSource<bool>(); var window = MessageSurface(text);
        var body = (StackPanel)((ScrollViewer)window.Root.Children[0]).Content;
        body.Children.Add(Button(accept, () => { completion.TrySetResult(true); window.Destroy(); }, true));
        body.Children.Add(Button("取消", window.Destroy)); window.Closed += (_, _) => completion.TrySetResult(false);
        window.Show(); window.Activate(); Native.RaiseBall(); return completion.Task;
    }
    private static SurfaceWindow MessageSurface(string text)
    {
        var window = new SurfaceWindow(400, 230) { Title = Brand.Name + "提示" }; var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(Label(text)); window.Root.Children.Add(new ScrollViewer { Content = body });
        var area = SystemParameters.WorkArea; window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
        WindowCreated?.Invoke(window); return window;
    }
}
