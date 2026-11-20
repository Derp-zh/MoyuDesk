using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

internal static class ColorValue
{
    internal static bool TryParse(string text, out Color color)
    {
        var value = text.Trim(); if (value.StartsWith('#')) value = value[1..];
        if (value.Length == 3) value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        { color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true; }
        color = default; return false;
    }
    internal static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    internal static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation; var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - chroma;
        var (r, g, b) = hue switch { < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x), < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    internal static (double hue, double saturation, double value) Hsv(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
}

internal sealed class ColorSpectrum : FrameworkElement
{
    internal double Hue { get; set; }
    internal double Saturation { get; set; }
    internal double Value { get; set; }
    internal event Action? Changed;
    internal ColorSpectrum()
    {
        Focusable = true; Cursor = Cursors.Cross; Tag = "ColorSpectrum";
        System.Windows.Automation.AutomationProperties.SetName(this, "颜色色谱；左右调整饱和度，上下调整明暗");
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var bounds = new Rect(RenderSize);
        drawing.DrawRectangle(new SolidColorBrush(ColorValue.FromHsv(Hue, 1, 1)), null, bounds);
        drawing.DrawRectangle(new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, .5), new Point(1, .5)), null, bounds);
        drawing.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, new Point(.5, 0), new Point(.5, 1)), null, bounds);
        var point = new Point(Math.Clamp(Saturation * ActualWidth, 5, Math.Max(5, ActualWidth - 5)), Math.Clamp((1 - Value) * ActualHeight, 5, Math.Max(5, ActualHeight - 5)));
        drawing.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
        drawing.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 5, 5);
        if (IsKeyboardFocused) drawing.DrawRectangle(null, new Pen(Ui.Accent, 2), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
    }
    private void Select(Point point)
    { Saturation = Math.Clamp(point.X / Math.Max(1, ActualWidth), 0, 1); Value = 1 - Math.Clamp(point.Y / Math.Max(1, ActualHeight), 0, 1); InvalidateVisual(); Changed?.Invoke(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); CaptureMouse(); Select(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) { Select(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) { Select(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
        switch (e.Key) { case Key.Left: Saturation -= step; break; case Key.Right: Saturation += step; break; case Key.Up: Value += step; break; case Key.Down: Value -= step; break; default: base.OnKeyDown(e); return; }
        Saturation = Math.Clamp(Saturation, 0, 1); Value = Math.Clamp(Value, 0, 1); InvalidateVisual(); Changed?.Invoke(); e.Handled = true;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
