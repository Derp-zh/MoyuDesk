using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// Owns the editing viewport, not the stage data. A paused stage keeps its
// complete editable text while presenting a single ellipsized line at rest.
// Long stages (> DeferredHydrationThreshold chars) render via the lightweight
// TextBlock display layer while unfocused (keeping WPF's heavy TextBox/TSF
// layout collapsed until the user clicks, focuses, or opens its context menu),
// and hydrate the full text on first idle/scroll/edit without losing data.
internal sealed class StageDetailsEditor : Grid
{
    internal const int DeferredHydrationThreshold = 1500;
    internal const int PrimaryInitialSliceChars = 360;
    internal const int SecondaryInitialSliceChars = 64;
    private const int CollapsedPreviewMaxChars = 64;

    internal TextBox Input { get; }
    private readonly TextBlock preview;
    private readonly int initialSliceLimit;
    private readonly bool snapshotMode;
    private string fullText;
    private bool hydrated;
    private bool previewFull;
    private bool editingActive;
    private bool internalTextSwap;
    private bool running;
    private bool collapseInactive;
    private bool contextMenuOpen;
    private bool expandingFromClick;
    private bool suppressInitialDragSelection;
    private bool lastExpanded;
    private bool heightAnimating;
    private ContextMenu? trackedMenu;
    private DispatcherTimer? idleHydrateTimer;

    internal bool IsHydrated => hydrated;
    internal string CurrentText => hydrated ? Input.Text : fullText;
    internal event Action<string>? TextValueChanged;

    private bool IsExpanded => running || !collapseInactive || Input.IsKeyboardFocusWithin || contextMenuOpen || expandingFromClick;
    private bool UseLightweightReadView => IsExpanded && !editingActive && fullText.Length > DeferredHydrationThreshold;

    internal StageDetailsEditor(string text, bool running, bool collapseInactive = true,
        int initialSliceLimit = PrimaryInitialSliceChars, bool snapshotMode = false)
    {
        this.running = running;
        this.collapseInactive = collapseInactive;
        this.initialSliceLimit = Math.Max(40, initialSliceLimit);
        this.snapshotMode = snapshotMode;
        fullText = text ?? "";

        ClipToBounds = true; Tag = "StageDetailsViewport"; Background = Brushes.Transparent;

        var isLong = fullText.Length > DeferredHydrationThreshold;
        hydrated = !isLong;
        editingActive = !isLong;
        previewFull = !isLong;

        Input = Ui.Input(hydrated ? fullText : "", true); Input.Style = (Style)FindResource("StageBodyInput");
        Input.Tag = "StageDetailsInput";
        Input.Background = Brushes.Transparent; Input.BorderThickness = new Thickness(0);
        Input.Margin = new Thickness(0); Input.Padding = new Thickness(10, 5, 10, 6);
        Input.MinHeight = 0; Input.MaxHeight = double.PositiveInfinity;
        Input.VerticalAlignment = VerticalAlignment.Top;
        Input.IsInactiveSelectionHighlightEnabled = true;

        preview = new TextBlock
        {
            FontSize = Input.FontSize,
            Margin = Input.Padding, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        Ui.BindBrush(preview, TextBlock.ForegroundProperty, "TextBrush", Ui.Text);
        Children.Add(Input); Children.Add(preview);

        Loaded += (_, _) =>
        {
            ScheduleIdlePreviewExpansionIfNeeded();
        };
        Unloaded += (_, _) => CancelIdleHydration();

        Input.TextChanged += (_, _) =>
        {
            if (internalTextSwap) return;
            hydrated = true;
            editingActive = true;
            previewFull = true;
            CancelIdleHydration();
            fullText = Input.Text;
            TextValueChanged?.Invoke(fullText);
            Refresh(false);
        };
        Input.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (Input.IsKeyboardFocusWithin)
            {
                editingActive = true;
                EnsureHydrated(preserveCaret: true);
            }
            Refresh(true);
        };
        Input.PreviewMouseLeftButtonDown += InputPreviewMouseLeftButtonDown;
        Input.PreviewMouseLeftButtonUp += (_, _) => suppressInitialDragSelection = false;
        Input.LostMouseCapture += (_, _) => suppressInitialDragSelection = false;
        Input.SelectionChanged += (_, _) =>
        {
            if (suppressInitialDragSelection && Input.SelectionLength > 0) Input.SelectionLength = 0;
        };
        Input.ContextMenuOpening += OnInputContextMenuOpening;
        Input.PreviewKeyDown += (_, _) => EnsureHydrated(preserveCaret: true);
        DataObject.AddPastingHandler(Input, (_, _) => EnsureHydrated(preserveCaret: true));

        MouseRightButtonDown += (_, _) =>
        {
            if (!Input.IsEnabled || !UseLightweightReadView) return;
            ActivateLiveInput(focus: true);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!Input.IsEnabled) return;
            if (!IsExpanded)
            {
                if (HandleCollapsedExpandClick(e.GetPosition(this), e.ClickCount)) e.Handled = true;
                return;
            }
            var point = e.GetPosition(this);
            if (UseLightweightReadView)
            {
                ActivateLiveInput(focus: false);
                Input.Focus();
                Input.UpdateLayout();
                Input.CaretIndex = RoundedInput.ResolveCaretIndex(Input, point);
                Input.SelectionLength = 0;
                e.Handled = true;
                return;
            }
            var inputPoint = e.GetPosition(Input);
            Input.Focus();
            Input.UpdateLayout();
            Input.CaretIndex = RoundedInput.ResolveCaretIndex(Input, inputPoint);
            Input.SelectionLength = 0;
            e.Handled = true;
        };
        lastExpanded = IsExpanded;
        Refresh(false);
    }

    internal static string ComputeInitialSlice(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
        var cut = maxChars;
        var lastBreak = text.LastIndexOf('\n', cut, Math.Min(cut, 120));
        if (lastBreak > maxChars / 2) cut = lastBreak;
        return text[..cut];
    }

    internal void EnsurePreviewExpanded()
    {
        if (previewFull) return;
        CancelIdleHydration();
        previewFull = true;
        if (UseLightweightReadView) preview.Text = fullText;
    }

    internal void ActivateLiveInput(bool focus)
    {
        editingActive = true;
        EnsureHydrated(preserveCaret: false);
        Refresh(false);
        if (focus) Input.Focus();
    }

    internal void EnsureHydrated(bool preserveCaret = false)
    {
        editingActive = true;
        previewFull = true;
        if (hydrated) return;
        CancelIdleHydration();
        var caret = preserveCaret ? Input.CaretIndex : 0;
        var selStart = preserveCaret ? Input.SelectionStart : 0;
        var selLen = preserveCaret ? Input.SelectionLength : 0;
        internalTextSwap = true;
        try
        {
            Input.IsUndoEnabled = false;
            Input.Text = fullText;
            Input.IsUndoEnabled = true;
            hydrated = true;
            if (preserveCaret)
            {
                if (selLen > 0) Input.Select(Math.Clamp(selStart, 0, fullText.Length), Math.Clamp(selLen, 0, Math.Max(0, fullText.Length - selStart)));
                else Input.CaretIndex = Math.Clamp(caret, 0, fullText.Length);
            }
        }
        finally
        {
            internalTextSwap = false;
        }
    }

    internal void CancelIdleHydration()
    {
        if (idleHydrateTimer is null) return;
        idleHydrateTimer.Stop();
        idleHydrateTimer = null;
    }

    private void ScheduleIdlePreviewExpansionIfNeeded()
    {
        if (previewFull || snapshotMode || !UseLightweightReadView || !IsLoaded) return;
        if (idleHydrateTimer is not null) return;
        // Expand the lightweight read-only TextBlock to full text on idle after
        // the 100ms reveal finishes, keeping TextBox/TSF collapsed until edited.
        idleHydrateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        idleHydrateTimer.Tick += (_, _) =>
        {
            CancelIdleHydration();
            if (!IsLoaded || previewFull || !UseLightweightReadView) return;
            EnsurePreviewExpanded();
        };
        idleHydrateTimer.Start();
    }

    private void InputPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleCollapsedExpandClick(e.GetPosition(Input), e.ClickCount)) e.Handled = true;
    }

    internal bool HandleCollapsedExpandClick(Point point, int clickCount)
    {
        if (IsExpanded || clickCount != 1 || !Input.IsEnabled) return false;
        if (Input.Visibility == Visibility.Visible && Input.Clip?.FillContains(point) == false) return false;
        suppressInitialDragSelection = true;
        expandingFromClick = true;
        try
        {
            editingActive = true;
            EnsureHydrated(preserveCaret: false);
            Refresh(false);
            Input.Focus();
            Input.UpdateLayout();
            Input.CaretIndex = RoundedInput.ResolveCaretIndex(Input, point);
            Input.SelectionLength = 0;
        }
        finally
        {
            expandingFromClick = false;
            suppressInitialDragSelection = false;
        }
        return true;
    }

    private void OnInputContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        editingActive = true;
        EnsureHydrated(preserveCaret: true);
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
    {
        if (running == value) return;
        running = value;
        Refresh(true);
        ScheduleIdlePreviewExpansionIfNeeded();
    }

    internal void SetCollapseInactive(bool value)
    {
        if (collapseInactive == value) return;
        collapseInactive = value;
        Refresh(true);
        ScheduleIdlePreviewExpansionIfNeeded();
    }

    private void Present(bool expanded)
    {
        var readView = expanded && !editingActive && fullText.Length > DeferredHydrationThreshold;
        var showInput = expanded && !readView;
        Input.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
        Input.Opacity = showInput ? 1 : 0;
        preview.Visibility = showInput ? Visibility.Collapsed : Visibility.Visible;
        Cursor = readView ? Cursors.IBeam : null;
        if (showInput) return;

        if (readView)
        {
            preview.TextWrapping = TextWrapping.Wrap;
            preview.TextTrimming = TextTrimming.None;
            preview.MinHeight = Input.MinHeight;
            preview.Text = previewFull ? fullText : ComputeInitialSlice(fullText, initialSliceLimit);
            return;
        }

        preview.TextWrapping = TextWrapping.NoWrap;
        preview.TextTrimming = TextTrimming.CharacterEllipsis;
        preview.MinHeight = 0;
        var text = CurrentText;
        var lineBreak = text.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
        {
            var cap = Math.Min(lineBreak, CollapsedPreviewMaxChars);
            preview.Text = text[..cap].TrimEnd() + "…";
        }
        else preview.Text = text.Length > CollapsedPreviewMaxChars ? text[..CollapsedPreviewMaxChars].TrimEnd() + "…" : text;
    }

    private void Refresh(bool animate)
    {
        var expanded = IsExpanded;
        var stateChanged = expanded != lastExpanded;
        lastExpanded = expanded;

        var singleLineHeight = Input.FontFamily.LineSpacing * Input.FontSize + Input.Padding.Top + Input.Padding.Bottom;
        var desiredMaxHeight = expanded ? double.PositiveInfinity : singleLineHeight;
        var desiredMinHeight = expanded ? Input.FontFamily.LineSpacing * Input.FontSize * 2 + Input.Padding.Top + Input.Padding.Bottom : 0;
        var desiredWrap = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;

        if (!Input.MaxHeight.Equals(desiredMaxHeight)) Input.MaxHeight = desiredMaxHeight;
        if (Input.MinLines != 1) Input.MinLines = 1;
        if (Math.Abs(Input.MinHeight - desiredMinHeight) > 0.1) Input.MinHeight = desiredMinHeight;
        if (Input.TextWrapping != desiredWrap) Input.TextWrapping = desiredWrap;
        if (Input.VerticalScrollBarVisibility != ScrollBarVisibility.Hidden) Input.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

        var fromHeight = ActualHeight > 0 ? ActualHeight : singleLineHeight;
        Present(expanded);

        if (!animate || !stateChanged || !IsLoaded || ActualWidth <= 0)
        {
            if (heightAnimating || !double.IsNaN(Height))
            {
                heightAnimating = false;
                BeginAnimation(HeightProperty, null);
                Height = double.NaN;
            }
            return;
        }

        double target;
        if (!expanded)
        {
            target = singleLineHeight;
        }
        else if (UseLightweightReadView)
        {
            preview.Measure(new Size(Math.Max(1, ActualWidth - preview.Margin.Left - preview.Margin.Right), double.PositiveInfinity));
            target = Math.Max(preview.DesiredSize.Height + preview.Margin.Top + preview.Margin.Bottom, desiredMinHeight);
        }
        else
        {
            EnsureHydrated(preserveCaret: false);
            Input.Measure(new Size(ActualWidth, double.PositiveInfinity));
            target = Math.Max(Input.DesiredSize.Height, desiredMinHeight);
        }

        if (Math.Abs(fromHeight - target) <= 0.5)
        {
            heightAnimating = false;
            BeginAnimation(HeightProperty, null);
            Height = double.NaN;
            return;
        }

        heightAnimating = true;
        var anim = new DoubleAnimation(fromHeight, target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        anim.Completed += (_, _) =>
        {
            heightAnimating = false;
            BeginAnimation(HeightProperty, null);
            Height = double.NaN;
        };
        BeginAnimation(HeightProperty, anim);
    }
}
