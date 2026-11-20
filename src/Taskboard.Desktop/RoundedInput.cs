using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

// The clip limits hit testing as well as painting. Padding remains clickable,
// while the four empty corners outside the capsule do not receive input.
public static class RoundedInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RoundedInput), new PropertyMetadata(false, Enable));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedInput), new PropertyMetadata(18.0, (value, _) => { if (value is TextBox input) Update(input); }));
    public static double GetRadius(DependencyObject value) => (double)value.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject value, double radius) => value.SetValue(RadiusProperty, radius);
    public static readonly DependencyProperty EdgeArrowNavigationProperty = DependencyProperty.RegisterAttached(
        "EdgeArrowNavigation", typeof(bool), typeof(RoundedInput), new PropertyMetadata(true));
    public static bool GetEdgeArrowNavigation(DependencyObject value) => (bool)value.GetValue(EdgeArrowNavigationProperty);
    public static void SetEdgeArrowNavigation(DependencyObject value, bool enabled) => value.SetValue(EdgeArrowNavigationProperty, enabled);
    private static readonly DependencyProperty OwnedContextMenuProperty = DependencyProperty.RegisterAttached(
        "OwnedContextMenu", typeof(ContextMenu), typeof(RoundedInput));

    private static void Enable(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not TextBox input) return;
        if ((bool)args.OldValue)
        {
            input.Loaded -= Loaded;
            input.SizeChanged -= Resized;
            input.PreviewMouseLeftButtonDown -= Pressed;
            input.PreviewKeyDown -= KeyPressed;
            if (input.ContextMenu is { } existing && ReferenceEquals(existing, input.GetValue(OwnedContextMenuProperty)))
            {
                input.ClearValue(FrameworkElement.ContextMenuProperty);
                input.ClearValue(OwnedContextMenuProperty);
            }
        }
        if ((bool)args.NewValue)
        {
            input.IsInactiveSelectionHighlightEnabled = true;
            input.Loaded += Loaded;
            input.SizeChanged += Resized;
            input.PreviewMouseLeftButtonDown += Pressed;
            input.PreviewKeyDown += KeyPressed;
            EnsureContextMenu(input);
            Update(input);
        }
    }

    private static void Loaded(object sender, RoutedEventArgs args) => Update((TextBox)sender);
    private static void Resized(object sender, SizeChangedEventArgs args) => Update((TextBox)sender);
    private static void Update(TextBox input)
    {
        var radius = Math.Max(0, Math.Min(GetRadius(input), Math.Min(input.ActualWidth, input.ActualHeight) / 2));
        var clip = new RectangleGeometry(new Rect(0, 0, input.ActualWidth, input.ActualHeight), radius, radius);
        clip.Freeze(); input.Clip = clip;
    }

    private static void Pressed(object sender, MouseButtonEventArgs args)
    {
        var input = (TextBox)sender;
        if (!input.IsEnabled || input.Clip?.FillContains(args.GetPosition(input)) != true) return;
        var point = args.GetPosition(input);
        var presenter = ColumnSurface.FindChild<ScrollContentPresenter>(input);
        if (presenter is not null && new Rect(presenter.RenderSize).Contains(args.GetPosition(presenter))) return;
        input.Focus();
        input.CaretIndex = ResolveCaretIndex(input, point);
        input.SelectionLength = 0;
        args.Handled = true;
    }

    internal static int ResolveCaretIndex(TextBox input, Point point)
    {
        var text = input.Text;
        if (string.IsNullOrEmpty(text)) return 0;
        var raw = input.GetCharacterIndexFromPoint(point, true);
        if (raw < 0) return point.X < input.ActualWidth / 2 ? 0 : text.Length;
        var line = input.GetLineIndexFromCharacterIndex(raw);
        int lineStart, lineEnd;
        if (line < 0)
        {
            lineStart = 0;
            lineEnd = text.Length;
        }
        else
        {
            lineStart = input.GetCharacterIndexFromLineIndex(line);
            var lineLength = input.GetLineLength(line);
            lineEnd = Math.Clamp(lineStart + lineLength, lineStart, text.Length);
            while (lineEnd > lineStart && text[lineEnd - 1] is '\r' or '\n') lineEnd--;
        }
        if (lineEnd == lineStart) return lineStart;
        var firstRect = input.GetRectFromCharacterIndex(lineStart, false);
        var lastRect = input.GetRectFromCharacterIndex(lineEnd - 1, true);
        if (line == 0 && !firstRect.IsEmpty && point.Y < firstRect.Top && point.X <= firstRect.Left) return 0;
        if (input.LineCount > 0 && line == input.LineCount - 1 && !lastRect.IsEmpty && point.Y > lastRect.Bottom) return text.Length;
        if (!firstRect.IsEmpty && point.X <= firstRect.Left) return lineStart;
        if (!lastRect.IsEmpty && point.X >= lastRect.Right) return lineEnd;
        var index = Math.Clamp(raw, lineStart, lineEnd - 1);
        var leftRect = input.GetRectFromCharacterIndex(index, false);
        var rightRect = input.GetRectFromCharacterIndex(index, true);
        if (!leftRect.IsEmpty && !rightRect.IsEmpty)
            return point.X >= (leftRect.Left + rightRect.Right) / 2 ? Math.Min(index + 1, lineEnd) : index;
        return index;
    }

    private static void KeyPressed(object sender, KeyEventArgs args)
    {
        var input = (TextBox)sender;
        if (!GetEdgeArrowNavigation(input)) return;
        HandleEdgeArrow(input, args);
    }

    internal static void HandleEdgeArrow(TextBox input, KeyEventArgs args)
    {
        if (args.Handled || input.IsReadOnly || !input.IsEnabled || Keyboard.Modifiers != ModifierKeys.None) return;
        if (args.Key is not (Key.Up or Key.Down)) return;
        var (line, lastLine) = GetLinePosition(input);
        if (args.Key == Key.Up && line == 0 && (input.CaretIndex > 0 || input.SelectionLength > 0))
        {
            input.Select(0, 0);
            BringCaretIntoView(input, 0);
            args.Handled = true;
        }
        else if (args.Key == Key.Down && line == lastLine && (input.CaretIndex < input.Text.Length || input.SelectionLength > 0))
        {
            var end = input.Text.Length;
            input.Select(end, 0);
            BringCaretIntoView(input, end);
            args.Handled = true;
        }
    }

    private static (int Line, int LastLine) GetLinePosition(TextBox input)
    {
        if (input.LineCount > 0)
        {
            var caret = Math.Clamp(input.CaretIndex, 0, input.Text.Length);
            var line = input.GetLineIndexFromCharacterIndex(caret);
            if (line >= 0) return (line, input.LineCount - 1);
        }
        var text = input.Text;
        if (string.IsNullOrEmpty(text)) return (0, 0);
        var caretIndex = Math.Clamp(input.CaretIndex, 0, text.Length);
        int current = 0, total = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            total++;
            if (i < caretIndex) current++;
        }
        return (current, total);
    }

    private static void BringCaretIntoView(TextBox input, int caretIndex)
    {
        if (caretIndex == 0) input.ScrollToHome();
        else if (caretIndex >= input.Text.Length) input.ScrollToEnd();
        var rect = input.GetRectFromCharacterIndex(caretIndex, false);
        if (!rect.IsEmpty) input.BringIntoView(rect);
    }

    private static void EnsureContextMenu(TextBox input)
    {
        if (input.ReadLocalValue(FrameworkElement.ContextMenuProperty) != DependencyProperty.UnsetValue) return;
        var menu = new ContextMenu { PlacementTarget = input };
        var undo = new MenuItem { Header = "撤销", InputGestureText = "Ctrl+Z" };
        var cut = new MenuItem { Header = "剪切", InputGestureText = "Ctrl+X" };
        var copy = new MenuItem { Header = "复制", InputGestureText = "Ctrl+C" };
        var paste = new MenuItem { Header = "粘贴", InputGestureText = "Ctrl+V" };
        var selectAll = new MenuItem { Header = "全选", InputGestureText = "Ctrl+A" };
        undo.Click += (_, _) => { input.Focus(); if (!input.IsReadOnly && input.CanUndo) input.Undo(); };
        cut.Click += (_, _) => { input.Focus(); if (!input.IsReadOnly && input.SelectionLength > 0) input.Cut(); };
        copy.Click += (_, _) => { input.Focus(); if (input.SelectionLength > 0) input.Copy(); };
        paste.Click += (_, _) => { input.Focus(); if (!input.IsReadOnly) input.Paste(); };
        selectAll.Click += (_, _) => { input.Focus(); input.SelectAll(); };
        menu.Items.Add(undo);
        menu.Items.Add(new Separator());
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(selectAll);
        menu.Opened += (_, _) =>
        {
            undo.IsEnabled = !input.IsReadOnly && input.CanUndo;
            cut.IsEnabled = !input.IsReadOnly && input.SelectionLength > 0;
            copy.IsEnabled = input.SelectionLength > 0;
            paste.IsEnabled = !input.IsReadOnly && CanPasteText();
            selectAll.IsEnabled = input.Text.Length > 0;
        };
        input.SetValue(OwnedContextMenuProperty, menu);
        input.ContextMenu = menu;
    }

    private static bool CanPasteText()
    {
        try { return Clipboard.ContainsText(); }
        catch { return true; }
    }
}
