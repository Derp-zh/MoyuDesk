using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

internal sealed class ColorPickerPanel : Border
{
    internal static readonly string[] Presets = ["#F28F98", "#FCA5A5", "#FFA773", "#F6C177", "#E9C582", "#F2E6A7", "#B7D99A", "#77CBB0", "#96DACC", "#67D4D9", "#82AFF7", "#A5C7FF", "#A5A0F2", "#C1A0EB", "#D8B4FE", "#E9A5D0", "#FFFFFF", "#E9EAF0", "#C4CBD8", "#93A6BE", "#64748B", "#334155", "#192333", "#000000"];
    private readonly TextBox hex = Ui.Input();
    private readonly TextBox[] channels = [Ui.Input(), Ui.Input(), Ui.Input()];
    private readonly ColorSpectrum spectrum = new() { Height = 148, Margin = new Thickness(0, 4, 0, 8) };
    private readonly Slider hue = new() { Minimum = 0, Maximum = 359.99, SmallChange = 1, LargeChange = 15, Height = 24, Tag = "ColorHue" };
    private readonly Border preview = new() { CornerRadius = new CornerRadius(7), MinHeight = 34, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
    private readonly TextBlock notice = Ui.Label("", 11, Ui.Muted);
    private readonly Button apply;
    private readonly Func<string, bool> commit;
    private bool updating;
    internal string SelectedColor { get; private set; } = "#000000";
    internal bool InputValid { get; private set; } = true;
    internal ColorPickerPanel(string initial, IEnumerable<string> recent, Func<string, bool> commit, Action dismiss)
    {
        this.commit = commit; Width = 336; Padding = new Thickness(16); CornerRadius = new CornerRadius(14);
        Background = Ui.Surface; BorderBrush = Ui.Line; BorderThickness = new Thickness(1); Tag = "ColorPickerPanel";
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Child = layout;
        var body = new StackPanel(); var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        var title = Ui.Label("选择颜色", 16); title.FontWeight = FontWeights.SemiBold; body.Children.Add(title);
        body.Children.Add(Ui.Label("常用颜色", 11, Ui.Muted)); body.Children.Add(Swatches(Presets, "PresetColor"));
        var recentValues = recent.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        if (recentValues.Length > 0) { body.Children.Add(Ui.Label("最近使用", 11, Ui.Muted)); body.Children.Add(Swatches(recentValues, "RecentColor")); }
        body.Children.Add(spectrum);
        var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        for (var i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(ColorValue.FromHsv(i * 60, 1, 1), i / 6d));
        var hueRow = new Grid(); hueRow.Children.Add(new Border { Background = hueBrush, CornerRadius = new CornerRadius(4), Height = 10, Margin = new Thickness(4, 0, 4, 0), IsHitTestVisible = false });
        hue.Background = Brushes.Transparent; System.Windows.Automation.AutomationProperties.SetName(hue, "色相"); hueRow.Children.Add(hue); body.Children.Add(hueRow);
        var previews = new Grid { Margin = new Thickness(0, 10, 0, 6) }; previews.ColumnDefinitions.Add(new ColumnDefinition()); previews.ColumnDefinitions.Add(new ColumnDefinition());
        var original = Ui.Button("原色", () => SetColor(initial)); original.Tag = "OriginalColor"; original.ToolTip = "恢复本次修改前的颜色"; original.Background = Ui.Brush(initial); original.Foreground = Contrast(initial); original.Height = 34; original.Padding = new Thickness(0); original.Margin = new Thickness(0, 0, 5, 0); previews.Children.Add(original);
        var previewLabel = Ui.Label("新颜色", 12); previewLabel.HorizontalAlignment = HorizontalAlignment.Center; previewLabel.VerticalAlignment = VerticalAlignment.Center;
        preview.Child = previewLabel; preview.Tag = "ColorPreview"; preview.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(preview, 1); previews.Children.Add(preview); body.Children.Add(previews);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) fields.ColumnDefinitions.Add(new ColumnDefinition());
        AddField(fields, "HEX", hex, 0, "ColorHex"); hex.MaxLength = 7;
        for (var i = 0; i < 3; i++) { AddField(fields, new[] { "R", "G", "B" }[i], channels[i], i + 1, "ColorRgb" + i); channels[i].MaxLength = 3; }
        body.Children.Add(fields); notice.Tag = "ColorNotice"; notice.Margin = new Thickness(0, 3, 0, 6); notice.Visibility = Visibility.Collapsed; body.Children.Add(notice);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = Ui.Button("取消", dismiss); cancel.Tag = "ColorCancel"; cancel.Margin = new Thickness(0, 0, 5, 0); actions.Children.Add(cancel);
        apply = Ui.Button("应用颜色", () => { if (Apply()) dismiss(); }, true); apply.Tag = "ColorApply"; apply.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(apply, 1); actions.Children.Add(apply);
        actions.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(actions, 1); layout.Children.Add(actions);
        hex.TextChanged += (_, _) => { if (!updating) ReadHex(); };
        foreach (var channel in channels) channel.TextChanged += (_, _) => { if (!updating) ReadRgb(); };
        spectrum.Changed += () => SetColor(ColorValue.Hex(ColorValue.FromHsv(spectrum.Hue, spectrum.Saturation, spectrum.Value)), fromSpectrum: true);
        hue.ValueChanged += (_, _) => { if (!updating) { spectrum.Hue = hue.Value; SetColor(ColorValue.Hex(ColorValue.FromHsv(spectrum.Hue, spectrum.Saturation, spectrum.Value)), fromSpectrum: true); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dismiss(); e.Handled = true; } else if (e.Key == Key.Enter && InputValid && !cancel.IsKeyboardFocusWithin) { if (Apply()) dismiss(); e.Handled = true; } };
        SetColor(initial);
    }
    private static void AddField(Grid row, string caption, TextBox input, int column, string tag)
    {
        var field = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 5, 0, 0, 0) };
        field.Children.Add(Ui.Label(caption, 11, Ui.Muted)); input.Tag = tag; input.Padding = new Thickness(5, 5, 5, 5); input.MinHeight = 30; input.Margin = new Thickness(0, 0, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(input, caption); field.Children.Add(input); Grid.SetColumn(field, column); row.Children.Add(field);
    }
    private WrapPanel Swatches(IEnumerable<string> colors, string tag)
    {
        var row = new WrapPanel();
        foreach (var value in colors)
        {
            var button = Ui.Button("", () => SetColor(value)); button.Width = button.Height = 29; button.Margin = new Thickness(0, 0, 8, 6); button.Padding = new Thickness(0);
            button.Tag = tag; button.DataContext = value; button.ToolTip = value; button.Background = Ui.Brush(value);
            System.Windows.Automation.AutomationProperties.SetName(button, "选择颜色 " + value); row.Children.Add(button);
        }
        return row;
    }
    private static Brush Contrast(string value)
    { ColorValue.TryParse(value, out var c); return c.R * .299 + c.G * .587 + c.B * .114 > 155 ? Ui.Brush("#152033") : Brushes.White; }
    private void Invalid(string message)
    { InputValid = false; apply.IsEnabled = false; notice.Text = message; notice.Foreground = Ui.Danger; notice.Visibility = Visibility.Visible; }
    private void ReadHex()
    { if (ColorValue.TryParse(hex.Text, out var color)) SetColor(ColorValue.Hex(color), preserveHex: true); else Invalid("HEX请输入3位或6位色值，例如 #C1A0EB。"); }
    private void ReadRgb()
    {
        var values = new byte[3];
        for (var i = 0; i < 3; i++) if (!byte.TryParse(channels[i].Text, out values[i])) { Invalid("R、G、B均需为0至255的整数。"); return; }
        SetColor(ColorValue.Hex(Color.FromRgb(values[0], values[1], values[2])), preserveRgb: true);
    }
    private void SetColor(string value, bool preserveHex = false, bool preserveRgb = false, bool fromSpectrum = false)
    {
        if (!ColorValue.TryParse(value, out var color)) return;
        updating = true;
        try
        {
            SelectedColor = ColorValue.Hex(color); InputValid = true; apply.IsEnabled = true;
            if (!preserveHex) hex.Text = SelectedColor;
            if (!preserveRgb) { channels[0].Text = color.R.ToString(); channels[1].Text = color.G.ToString(); channels[2].Text = color.B.ToString(); }
            if (!fromSpectrum) { var hsv = ColorValue.Hsv(color); if (hsv.saturation > 0) spectrum.Hue = hsv.hue; spectrum.Saturation = hsv.saturation; spectrum.Value = hsv.value; }
            hue.Value = spectrum.Hue; spectrum.InvalidateVisual(); preview.Background = new SolidColorBrush(color);
            ((TextBlock)preview.Child).Foreground = Contrast(SelectedColor);
            var luma = color.R * .299 + color.G * .587 + color.B * .114;
            var lowContrast = ThemePalette.Current.IsDark ? luma < 75 : luma > 205;
            notice.Text = lowContrast
                ? (ThemePalette.Current.IsDark ? "此颜色在深色背景上较暗，可提高明度。" : "此颜色在浅色背景上较浅，可加深明度。")
                : "";
            notice.Foreground = Ui.Muted; notice.Visibility = lowContrast ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { updating = false; }
    }
    internal bool Apply()
    {
        if (!InputValid) return false;
        if (commit(SelectedColor)) return true;
        Invalid("选项已变化，请关闭后重新选择颜色。"); return false;
    }
}
