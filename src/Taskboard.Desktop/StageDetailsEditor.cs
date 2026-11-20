using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Taskboard.Desktop;

// Owns the editing viewport, not the stage data. A paused stage keeps its
// complete editable text while presenting a single ellipsized line at rest.
internal sealed class StageDetailsEditor : Grid
{
    internal TextBox Input { get; }
    private readonly TextBlock preview;
    private bool running;
    private bool collapseInactive;
    private bool contextMenuOpen;
    private bool suppressInitialDragSelection;
    private double targetHeight = -1;
    private double measuredWidth = double.NaN;
    private string? measuredText;
    private bool measuredExpanded;
    private ContextMenu? trackedMenu;

    private bool IsExpanded => running || !collapseInactive || Input.IsKeyboardFocusWithin || contextMenuOpen;

    internal StageDetailsEditor(string text, bool running, bool collapseInactive = true)
    {
        this.running = running; this.collapseInactive = collapseInactive;
        ClipToBounds = true; Tag = "StageDetailsViewport"; Background = Brushes.Transparent;
        Input = Ui.Input(text, true); Input.Style = (Style)FindResource("StageBodyInput");
        Input.Tag = "StageDetailsInput";
        Input.Background = Brushes.Transparent; Input.BorderThickness = new Thickness(0);
        Input.Margin = new Thickness(0); Input.Padding = new Thickness(10, 5, 10, 6);
        Input.MinHeight = 0; Input.MaxHeight = double.PositiveInfinity;
        Input.VerticalAlignment = VerticalAlignment.Top;
        Input.IsInactiveSelectionHighlightEnabled = true;
        preview = new TextBlock
        {
            Foreground = Input.Foreground, FontFamily = Input.FontFamily, FontSize = Input.FontSize,
            Margin = Input.Padding, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        Children.Add(Input); Children.Add(preview);
        Loaded += (_, _) => { preview.FontFamily = Input.FontFamily; Refresh(false); };
        Input.TextChanged += (_, _) => Refresh(false);
        Input.IsKeyboardFocusWithinChanged += (_, _) => Refresh(true);
        Input.PreviewMouseLeftButtonDown += InputPreviewMouseLeftButtonDown;
        Input.PreviewMouseLeftButtonUp += (_, _) => suppressInitialDragSelection = false;
        Input.LostMouseCapture += (_, _) => suppressInitialDragSelection = false;
        Input.SelectionChanged += (_, _) =>
        {
            if (suppressInitialDragSelection && Input.SelectionLength > 0) Input.SelectionLength = 0;
        };
        Input.ContextMenuOpening += OnInputContextMenuOpening;
        MouseLeftButtonDown += (_, e) =>
        {
            if (!Input.IsEnabled) return;
            var point = e.GetPosition(Input);
            Input.Focus();
            Input.UpdateLayout();
            Input.CaretIndex = RoundedInput.ResolveCaretIndex(Input, point);
            Input.SelectionLength = 0;
            e.Handled = true;
        };
        SizeChanged += (_, e) => { if (e.WidthChanged) Refresh(false); };
        Refresh(false, deferExpandedMeasure: true);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (IsExpanded && double.IsFinite(constraint.Width) && constraint.Width > 0)
            Refresh(false, widthOverride: constraint.Width);
        return base.MeasureOverride(constraint);
    }

    private void InputPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleCollapsedExpandClick(e.GetPosition(Input), e.ClickCount)) e.Handled = true;
    }

    internal bool HandleCollapsedExpandClick(Point point, int clickCount)
    {
        if (IsExpanded || clickCount != 1 || !Input.IsEnabled) return false;
        if (Input.Clip?.FillContains(point) == false) return false;
        suppressInitialDragSelection = true;
        try
        {
            Input.Focus();
            Input.UpdateLayout();
            Input.CaretIndex = RoundedInput.ResolveCaretIndex(Input, point);
            Input.SelectionLength = 0;
        }
        finally
        {
            suppressInitialDragSelection = false;
        }
        return true;
    }

    private void OnInputContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        contextMenuOpen = true;
        var menu = Input.ContextMenu;
        if (menu is not null && !ReferenceEquals(menu, trackedMenu))
        {
            if (trackedMenu is not null) trackedMenu.Closed -= OnContextMenuClosed;
            trackedMenu = menu;
            trackedMenu.Closed += OnContextMenuClosed;
        }
        Refresh(false);
    }

    private void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        contextMenuOpen = false;
        if (Input.IsLoaded && !Input.IsKeyboardFocusWithin &&
            (Keyboard.FocusedElement is null || Keyboard.FocusedElement is MenuItem || Keyboard.FocusedElement is ContextMenu))
            Input.Focus();
        Refresh(true);
    }

    internal void SetRunning(bool value)
    { if (running == value) return; running = value; Refresh(true); }

    internal void SetCollapseInactive(bool value)
    { if (collapseInactive == value) return; collapseInactive = value; Refresh(true); }

    private void Present()
    {
        var editing = IsExpanded;
        Input.Opacity = editing ? 1 : 0;
        preview.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        if (editing) return;
        var text = Input.Text;
        var lineBreak = text.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
        {
            var cap = Math.Min(lineBreak, 256);
            preview.Text = text[..cap].TrimEnd() + "…";
        }
        else preview.Text = text.Length > 256 ? text[..256].TrimEnd() + "…" : text;
    }

    private void Refresh(bool animate, bool deferExpandedMeasure = false, double? widthOverride = null)
    {
        var expanded = IsExpanded;
        var singleLineHeight = Input.FontFamily.LineSpacing * Input.FontSize + Input.Padding.Top + Input.Padding.Bottom;
        Input.MaxHeight = expanded ? double.PositiveInfinity : singleLineHeight;
        Input.MinLines = 1;
        Input.MinHeight = expanded ? Input.FontFamily.LineSpacing * Input.FontSize * 2 + Input.Padding.Top + Input.Padding.Bottom : 0;
        Input.TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Input.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Present();
        double target;
        if (!expanded)
        {
            target = singleLineHeight;
            measuredExpanded = false;
        }
        else
        {
            var measureWidth = widthOverride ?? (ActualWidth > 0 ? ActualWidth : 400);
            if (deferExpandedMeasure && ActualWidth <= 0)
            {
                target = Input.MinHeight;
            }
            else if (measuredExpanded && Math.Abs(measuredWidth - measureWidth) < .5 && string.Equals(measuredText, Input.Text, StringComparison.Ordinal))
            {
                target = targetHeight >= 0 ? targetHeight : Input.MinHeight;
            }
            else
            {
                Input.Measure(new Size(measureWidth, double.PositiveInfinity));
                target = Math.Clamp(Input.DesiredSize.Height, 0, Input.MaxHeight);
                measuredWidth = measureWidth;
                measuredText = Input.Text;
                measuredExpanded = true;
            }
        }
        if (Math.Abs(targetHeight - target) < .1) return;
        var from = double.IsNaN(Height) ? target : Height;
        targetHeight = target;
        BeginAnimation(HeightProperty, null); Height = target;
        if (animate && IsLoaded && Math.Abs(from - target) > .1)
            BeginAnimation(HeightProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
}
