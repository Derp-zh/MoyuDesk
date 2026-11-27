using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class ColumnSurface : SurfaceWindow
{
    protected override bool ScrollEnabled => false;
    internal override double GlassTransparency => IsPinnedOverlay ? controller.Engine.State.Preferences.PinnedActionTransparency : Transparency;
    internal bool IsPinnedOverlay { get; private set; }
    internal event Action? PinnedOverlayGeometryChanged;
    internal event Action? PinnedScrollabilityChanged;
    private bool? lastReportedScrollable;
    // A layered child needs nonzero alpha over its entire existing input area.
    // The visible tint remains in the persistent compositor layer.
    protected override void ApplyGlassTint() => Root.Background = Ui.HitSurface;
    public override void ApplyTheme()
    {
        Ui.BindBrush(codexConnectionHint, TextBlock.ForegroundProperty, "MutedBrush", Ui.Muted);
        RefreshPinToggleAppearance();
        if (completedToggle is not null) Ui.BindBrush(completedToggle, Control.ForegroundProperty, "AccentBrush", Ui.Accent);
        base.ApplyTheme();
        if (!QuickEntryActive) ResetFooter();
        Repaint();
    }
    private readonly BoardController controller;
    public BoardColumn Definition { get; private set; }
    public Lane Lane => Definition.BuiltinLane ?? Lane.Refining;
    private readonly TextBlock heading;
    private readonly TextBlock codexConnectionHint = new() { Tag = "CodexConnectionHint", FontSize = 10,
        Foreground = Ui.Muted, Opacity = .65, Margin = new Thickness(14, 1, 22, 8),
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    internal void RefreshCodexConnectionHint()
    {
        var hint = Definition.BuiltinLane is Taskboard.Core.Lane.Running or Taskboard.Core.Lane.Action ? controller.CodexConnectionHint : "";
        codexConnectionHint.Text = hint;
        codexConnectionHint.Visibility = hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private readonly Thumb group;
    private readonly ObservableCollection<object> items = new();
    private readonly ListBox list;
    private readonly TextBlock count;
    private readonly Grid footer = new();
    private readonly Thumb corner;
    private bool edgeResize;
    private readonly MenuItem resizeToggle;
    private readonly Grid edges = new();
    private Action? flushInline;
    private Action? finishInline;
    private Action? releaseInlineInput;
    private Guid? inlineTask;
    private System.Windows.Threading.DispatcherTimer? inlineTimer;
    private Guid? draggedTask;
    private Grid? cardPreview;
    private Guid? previewBefore;
    private long lastDragScroll;
    private readonly System.Collections.Generic.Dictionary<Guid, (WorkItem Item, int Index)> outgoing = new();
    private readonly System.Collections.Generic.HashSet<Guid> incoming = new();
    internal void HoldAutomaticExit(WorkItem item)
        => outgoing[item.Id] = (item, items.OfType<WorkItem>().TakeWhile(x => x.Id != item.Id).Count());
    internal void HoldAutomaticEntry(Guid id) => incoming.Add(id);
    internal void ReleaseAutomaticMove(Guid id) { outgoing.Remove(id); incoming.Remove(id); }
    internal TaskCard? PrepareAutomaticEntry(Guid id)
    { Refresh(); list.UpdateLayout(); return RenderedCard(id); }
    internal void RestartCardMarquees()
    {
        foreach (var item in items.OfType<WorkItem>()) RenderedCard(item.Id)?.SetMarquees(true);
        InvalidateAnimation();
    }
    public ColumnSurface(BoardController controller, Lane lane) : this(controller, controller.Engine.State.Preferences.Columns.First(x => x.BuiltinLane == lane)) { }
    public ColumnSurface(BoardController controller, BoardColumn definition) : base(240, 680)
    {
        this.controller = controller; Definition = definition; MinWidth = 80; MinHeight = 260; WindowLayer = 0;
        Title = definition.Name;
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        Root.RowDefinitions.Add(new RowDefinition());
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(codexConnectionHint, 2); Root.Children.Add(codexConnectionHint);
        var header = new Grid { Margin = new Thickness(12, 3.6, 12, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = heading = Ui.Label(definition.Name, 15); title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 2.4, 0, 2.4);
        title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
        {
            header.Children.Remove(title);
            var area = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            area.ColumnDefinitions.Add(new ColumnDefinition()); area.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            area.Children.Add(title);
            var quick = quickAi = Ui.Button("", () => { }); quick.Tag = "QuickAiTask"; quick.Width = 38; quick.Height = 26;
            var chrome = new FrameworkElementFactory(typeof(Border)); chrome.Name = "QuickAiCapsuleChrome";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            chrome.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            chrome.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            chrome.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Control.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var contents = new FrameworkElementFactory(typeof(ContentPresenter)); contents.SetBinding(ContentPresenter.ContentProperty,
                new System.Windows.Data.Binding(nameof(ContentControl.Content)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            contents.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); contents.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(contents); var quickTemplate = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var useDynamic = Application.Current?.TryFindResource("AccentBrush") is Brush;
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, useDynamic ? new DynamicResourceExtension("AccentBrush") : (object)Ui.Accent, "QuickAiCapsuleChrome"));
            hover.Setters.Add(new Setter(Border.BackgroundProperty, useDynamic ? new DynamicResourceExtension("ButtonHoverBrush") : (object)Ui.ButtonHover, "QuickAiCapsuleChrome")); quickTemplate.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, useDynamic ? new DynamicResourceExtension("ButtonPressedBrush") : (object)Ui.ButtonPressed, "QuickAiCapsuleChrome")); quickTemplate.Triggers.Add(pressed);
            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(OpacityProperty, .45, "QuickAiCapsuleChrome")); quickTemplate.Triggers.Add(disabled);
            quick.Template = quickTemplate;
            quick.Padding = new Thickness(0); quick.Margin = new Thickness(6, 0, 5, 0); quick.VerticalAlignment = VerticalAlignment.Center;
            quick.ToolTip = "按住拖到会话：用会话名称新建任务并绑定";
            var icon = new Grid { Width = 26, Height = 22, IsHitTestVisible = false };
            var plusPath = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 4,13 L 16,13 M 10,7 L 10,19"), StrokeThickness = 1.8 };
            Ui.BindBrush(plusPath, System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush", Ui.Accent);
            icon.Children.Add(plusPath);
            var aiTag = new TextBlock { Text = "AI", FontSize = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Ui.BindBrush(aiTag, TextBlock.ForegroundProperty, "AccentBrush", Ui.Accent);
            icon.Children.Add(aiTag);
            quick.Content = icon;
            var gesture = new ConversationCapsuleGesture(quick, controller, () => new ConversationBindingDrag(controller, this),
                () => Ui.Hint(quick, "按住并拖到绑定窗口中的会话，或 Codex / DSH 会话", !controller.Smoke));
            quick.Unloaded += (_, _) => gesture.Dispose(); Grid.SetColumn(quick, 1); area.Children.Add(quick); header.Children.Add(area);
        }
        var rightSlot = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        AddCompletedToggle(rightSlot);
        AddPinToggle(rightSlot);
        count = Ui.Label("0", 12, Ui.Muted); count.VerticalAlignment = VerticalAlignment.Center; count.HorizontalAlignment = HorizontalAlignment.Right;
        count.Margin = new Thickness(0, 2.4, 0, 2.4);
        rightSlot.Children.Add(count);
        Grid.SetColumn(rightSlot, 1); header.Children.Add(rightSlot); Root.Children.Add(header); MoveWith(header);
        list = new ListBox { ItemsSource = items, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(list, true); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.FocusableProperty, false));
        var template = new ControlTemplate(typeof(ListBoxItem));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); style.Setters.Add(new Setter(Control.TemplateProperty, template));
        list.ItemContainerStyle = style;
        var cardFactory = new FrameworkElementFactory(typeof(TaskCard));
        cardFactory.SetValue(TaskCard.ControllerProperty, controller);
        var taskTemplate = new DataTemplate { VisualTree = cardFactory };
        var entryFactory = new FrameworkElementFactory(typeof(ContentPresenter)); entryFactory.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding());
        list.ItemTemplateSelector = new LaneTemplateSelector(taskTemplate, new DataTemplate { VisualTree = entryFactory });
        Grid.SetRow(list, 1); Root.Children.Add(list);
        list.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("Taskboard.Task") ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effects == DragDropEffects.Move)
            {
                var point = e.GetPosition(list); var scroll = FindScroll(list);
                if (Environment.TickCount64 - lastDragScroll >= 70)
                {
                    if (point.Y < 30 && scroll is not null) { QuickScroll.StepItem(scroll, -1); lastDragScroll = Environment.TickCount64; }
                    else if (point.Y > list.ActualHeight - 30 && scroll is not null) { QuickScroll.StepItem(scroll, 1); lastDragScroll = Environment.TickCount64; }
                }
                if (controller.CardDrag is { } drag) drag.Preview(this, DropBefore(point));
            }
            e.Handled = true;
        };
        list.DragLeave += (_, e) => { if (!new Rect(list.RenderSize).Contains(e.GetPosition(list))) controller.CardDrag?.Leave(this); };
        list.Drop += (_, e) =>
        {
            if (e.Data.GetData("Taskboard.Task") is not Guid id) return;
            if (controller.CardDrag is { } drag && drag.Id == id)
            {
                var previewTarget = cardPreview is not null ? previewBefore : DropBefore(e.GetPosition(list));
                drag.Dispose(); controller.CardDrag = null;
                controller.MoveTask(id, Lane, previewTarget, Definition.BuiltinLane is null ? Definition.Id : null); e.Effects = DragDropEffects.Move; e.Handled = true; return;
            }
            var point = e.GetPosition(list); var source = list.InputHitTest(point) as DependencyObject;
            var card = Ancestor<TaskCard>(source);
            Guid? before = null;
            if (card?.DataContext is WorkItem hit)
            {
                if (e.GetPosition(card).Y < card.ActualHeight / 2) before = hit.Id;
                else before = items.Skip(items.IndexOf(hit) + 1).OfType<WorkItem>().FirstOrDefault()?.Id;
            }
            controller.MoveTask(id, Lane, before, Definition.BuiltinLane is null ? Definition.Id : null); e.Handled = true;
        };
        footer.Margin = new Thickness(0, 0, 0, 10); items.Add(footer);
        ResetFooter();
        var menu = new ContextMenu(); resizeToggle = new MenuItem { Header = "调整本窗口大小" };
        resizeToggle.Click += (_, _) => { if (edgeResize) StopResize(); else { edgeResize = true; resizeToggle.Header = "结束调整"; edges.Visibility = Visibility.Visible; count.Text = "调整中"; controller.SuppressCollapse = true; } }; menu.Items.Add(resizeToggle);
        var reset = new MenuItem { Header = "本窗口恢复到默认" };
        reset.Click += (_, _) => { if (edgeResize) StopResize(); controller.RestoreColumnDefault(this); }; menu.Items.Add(reset);
        Root.ContextMenu = menu;
        Root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) StopResize(); };
        Deactivated += (_, _) => { if (edgeResize) StopResize(); };
        var useDynamicGrip = Application.Current?.TryFindResource("AccentBrush") is Brush;
        corner = new Thumb { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent, Opacity = .9, Margin = new Thickness(6), Tag = "ColumnResizeGrip", ToolTip = "拖动调整本窗口大小" };
        var cornerArea = new FrameworkElementFactory(typeof(Grid)); cornerArea.SetValue(Panel.BackgroundProperty, Ui.HitSurface);
        var cornerGlyph = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)); cornerGlyph.Name = "ColumnResizeGlyph";
        cornerGlyph.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 4,14 L 14,4 M 9,14 L 14,9 M 13,14 L 14,13"));
        if (useDynamicGrip) cornerGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        else cornerGlyph.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Ui.Accent);
        cornerGlyph.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.6);
        cornerGlyph.SetValue(IsHitTestVisibleProperty, false); cornerArea.AppendChild(cornerGlyph);
        corner.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = cornerArea };
        Grid.SetRowSpan(corner, 3); Root.Children.Add(corner);
        edges.Visibility = Visibility.Collapsed; Grid.SetRowSpan(edges, 3); Root.Children.Add(edges);
        Edge(HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, e => { var delta = Math.Min(e.HorizontalChange, Width - MinWidth); Left += delta; Width -= delta; });
        Edge(HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, e => Width = Math.Max(MinWidth, Width + e.HorizontalChange));
        Edge(HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, e => { var delta = Math.Min(e.VerticalChange, Height - MinHeight); Top += delta; Height -= delta; });
        Edge(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, e => Height = Math.Max(MinHeight, Height + e.VerticalChange));
        corner.DragDelta += (_, e) => { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); };
        corner.DragCompleted += (_, _) => controller.SavePosition(Definition.PositionKey, this);
        PositionChanged += () => { controller.SavePosition(Definition.PositionKey, this); PinnedOverlayGeometryChanged?.Invoke(); };
        LocationChanged += (_, _) => PinnedOverlayGeometryChanged?.Invoke();
        SizeChanged += (_, _) => PinnedOverlayGeometryChanged?.Invoke();
        DpiChanged += (_, _) => PinnedOverlayGeometryChanged?.Invoke();
        IsVisibleChanged += (_, _) => PinnedOverlayGeometryChanged?.Invoke();
        list.Loaded += (_, _) => { if (IsPinnedOverlay) NotifyPinnedScrollabilityIfChanged(force: true); };
        list.SizeChanged += (_, _) => { if (IsPinnedOverlay) NotifyPinnedScrollabilityIfChanged(force: true); };
        Root.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (IsPinnedOverlay && (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 || lastReportedScrollable is null))
                NotifyPinnedScrollabilityIfChanged();
        }));
        {
            group = new Thumb { Width = 22, Height = 22, Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNESW, ToolTip = "拖动：等比例调整最大范围；按住 Shift 拖动：整体平移最大范围" };
            var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetValue(TextBlock.TextProperty, "⤢"); glyph.SetValue(TextBlock.FontSizeProperty, 20.0);
            if (useDynamicGrip) glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            else glyph.SetValue(TextBlock.ForegroundProperty, Ui.Accent);
            glyph.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
            var dragArea = new FrameworkElementFactory(typeof(Border)); dragArea.SetValue(Border.BackgroundProperty, Ui.HitSurface); dragArea.AppendChild(glyph);
            group.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = dragArea }; group.Tag = "WorkspaceResizeGrip"; Grid.SetRowSpan(group, 3); Root.Children.Add(group);
            group.DragStarted += (_, _) => controller.BeginWorkspaceResize();
            group.DragDelta += (_, _) =>
            {
                var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                group.Cursor = shift ? Cursors.SizeAll : Cursors.SizeNESW;
                controller.ResizeWorkspaceFromPointer(shift);
            };
            group.DragCompleted += (_, _) => { group.Cursor = Cursors.SizeNESW; controller.EndWorkspaceResize(); };
        }
        Refresh(); ApplySettings();
        Root.PreviewMouseDown += (_, _) => controller.EnsureCodexConnection();
        Closed += (_, _) => { inlineTimer?.Stop(); releaseInlineInput?.Invoke(); };
    }

    private void Edge(HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor, Action<DragDeltaEventArgs> drag)
    {
        var thumb = new Thumb { HorizontalAlignment = horizontal, VerticalAlignment = vertical, Cursor = cursor, Background = Brushes.Transparent };
        if (horizontal == HorizontalAlignment.Stretch) thumb.Height = 8; else thumb.Width = 8;
        var template = new ControlTemplate(typeof(Thumb)); var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent); template.VisualTree = border; thumb.Template = template;
        thumb.DragDelta += (_, e) => drag(e); thumb.DragCompleted += (_, _) => StopResize(); edges.Children.Add(thumb);
    }
    private void StopResize() { edgeResize = false; resizeToggle.Header = "调整本窗口大小"; edges.Visibility = Visibility.Collapsed; count.Text = items.OfType<WorkItem>().Count().ToString(); controller.SuppressCollapse = false; controller.SavePosition(Definition.PositionKey, this); }
    public void ApplySettings() { RefreshViewSettings(); corner.Visibility = controller.Engine.State.Preferences.ResizeCorner ? Visibility.Visible : Visibility.Collapsed; group.Visibility = controller.VisibleColumns.FirstOrDefault()?.Definition.Id == Definition.Id ? Visibility.Visible : Visibility.Collapsed; }
    public void Rebind(BoardColumn definition) { Definition = definition; Title = heading.Text = definition.Name; Refresh(); ApplySettings(); }
    public void Refresh()
    {
        RefreshCodexConnectionHint();
        InvalidateAnimation();
        if (cardPreview is not null) items.Remove(cardPreview);
        var wanted = controller.Engine.State.Tasks.Where(x => Definition.Contains(x) && x.Id != draggedTask && x.Id != inlineTask && !incoming.Contains(x.Id)).ToList();
        foreach (var retained in outgoing.Values.OrderBy(x => x.Index))
            if (!wanted.Any(x => x.Id == retained.Item.Id)) wanted.Insert(Math.Min(retained.Index, wanted.Count), retained.Item);
        var desired = OrderForDisplay(wanted).ToArray();
        for (var i = items.Count - 1; i >= 0; i--) if (items[i] is WorkItem item && !desired.Any(x => x.Id == item.Id)) items.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < items.Count && items[i] is WorkItem at && at.Id == desired[i].Id) { if (!ReferenceEquals(items[i], desired[i])) items[i] = desired[i]; continue; }
            var existing = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == desired[i].Id);
            if (existing is not null) { var index = items.IndexOf(existing); items.Move(index, i); if (!ReferenceEquals(items[i], desired[i])) items[i] = desired[i]; }
            else items.Insert(i, desired[i]);
        }
        count.Text = edgeResize ? "调整中" : (desired.Length + (cardPreview is null ? 0 : 1) + (inlineTask is Guid id && controller.Engine.State.Tasks.Any(x => x.Id == id && Definition.Contains(x)) ? 1 : 0)).ToString();
        if (cardPreview is not null) InsertPreview();
        if (IsPinnedOverlay) NotifyPinnedScrollabilityIfChanged(force: true);
    }
    internal bool BeginCardDrag(Guid id)
    {
        var source = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == id);
        if (source is null) return false;
        var before = items.OfType<WorkItem>().SkipWhile(x => x.Id != id).Skip(1).FirstOrDefault()?.Id;
        draggedTask = id; Refresh();
        SetCardPreview(source, before);
        return true;
    }
    internal void EndCardDrag()
    {
        var hadDrag = draggedTask is not null || cardPreview is not null;
        ClearCardPreview(); draggedTask = null;
        if (hadDrag) Refresh();
    }
    internal void ClearCardPreview()
    {
        if (cardPreview is not null) items.Remove(cardPreview); cardPreview = null; previewBefore = null;
        if (!edgeResize) count.Text = items.OfType<WorkItem>().Count().ToString();
    }
    internal bool SetCardPreview(WorkItem item, Guid? before)
    {
        if (Definition.BuiltinLane == Lane.Done && controller.Engine.State.Preferences.CompletedChronological)
        {
            var completed = Definition.Contains(item) ? item.CompletedSinceUtc ?? item.UpdatedUtc : DateTime.UtcNow;
            before = items.OfType<WorkItem>().FirstOrDefault(x => (x.CompletedSinceUtc ?? x.UpdatedUtc) > completed ||
                (x.CompletedSinceUtc ?? x.UpdatedUtc) == completed && x.Id.CompareTo(item.Id) > 0)?.Id;
        }
        if (cardPreview is not null && previewBefore == before) return false;
        if (cardPreview is null)
        {
            cardPreview = new Grid { Margin = new Thickness(0, 0, 0, 8), IsHitTestVisible = false, ClipToBounds = true, Tag = "CardDropPlaceholder" };
            var availableWidth = Math.Max(1, list.ActualWidth - list.Padding.Left - list.Padding.Right);
            var targetCardWidth = multiColumn
                ? Math.Max(1, availableWidth / Math.Max(1, (int)((availableWidth + 10) / 250)) - 10)
                : availableWidth;
            if (controller.CardDrag is { } drag && drag.Id == item.Id && drag.Height > 0 && Math.Abs(targetCardWidth - drag.Width) <= 12)
            {
                cardPreview.Height = drag.Height;
                cardPreview.Children.Add(new Image { Source = drag.Snapshot, Height = drag.Height, Stretch = Stretch.Fill, Opacity = .12, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            else
            {
                var previewItem = Copies.Of(item); Definition.Assign(previewItem);
                var card = new TaskCard { Controller = controller, DataContext = previewItem, Margin = new Thickness(0), BorderBrush = Brushes.Transparent, Opacity = .12 };
                cardPreview.Children.Add(card);
            }
            cardPreview.Children.Add(new System.Windows.Shapes.Rectangle { RadiusX = 12, RadiusY = 12, Stroke = Ui.Accent,
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 4]), Margin = new Thickness(.5) });
        }
        else items.Remove(cardPreview);
        previewBefore = before; InsertPreview(); if (!edgeResize) count.Text = (items.OfType<WorkItem>().Count() + 1).ToString(); return true;
    }
    private void InsertPreview()
    {
        if (cardPreview is null) return;
        var before = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == previewBefore);
        items.Insert(before is null ? items.IndexOf(footer) : items.IndexOf(before), cardPreview);
    }
    private Guid? DropBefore(Point point)
    {
        return ReorderDropBefore(point);
    }
    internal bool AdvanceCardDrag(WorkItem item, Point screen)
    {
        if (!IsVisible || PresentationSource.FromVisual(list) is null) return false;
        var point = list.PointFromScreen(screen);
        if (!new Rect(list.RenderSize).Contains(point) || Environment.TickCount64 - lastDragScroll < 70) return false;
        var scroll = FindScroll(list); if (scroll is null) return false;
        var previous = scroll.VerticalOffset;
        if (point.Y < 30) QuickScroll.StepItem(scroll, -1);
        else if (point.Y > list.ActualHeight - 30) QuickScroll.StepItem(scroll, 1);
        else return false;
        lastDragScroll = Environment.TickCount64; list.UpdateLayout();
        return Math.Abs(scroll.VerticalOffset - previous) > .1 && SetCardPreview(item, DropBefore(point));
    }
    public void Repaint() { var values = items.ToArray(); items.Clear(); foreach (var item in values) items.Add(item); }
    public void RefreshCards(System.Collections.Generic.IReadOnlyCollection<Guid> ids)
    {
        InvalidateAnimation();
        foreach (var item in items.OfType<WorkItem>().Where(x => ids.Contains(x.Id) && !outgoing.ContainsKey(x.Id)))
            FindChild<TaskCard>(list.ItemContainerGenerator.ContainerFromItem(item))?.Rebuild();
    }
    internal TaskCard? RenderedCard(Guid id)
    {
        var item = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == id);
        return item is null ? null : FindChild<TaskCard>(list.ItemContainerGenerator.ContainerFromItem(item));
    }
    public TaskCard? Locate(Guid id)
    {
        var item = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == id); if (item is null) return null;
        list.ScrollIntoView(item); list.UpdateLayout();
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
        return FindChild<TaskCard>(container);
    }
    private void ResetFooter()
    {
        footer.Children.Clear();
        footer.Children.Add(Ui.DashedButton("＋ 新建任务", () => { if (Lane == Lane.Inspiration && controller.Engine.State.Preferences.InspirationQuickEntry) InlineNew(); else controller.OpenNewEntry(Lane, Definition.BuiltinLane is null ? Definition.Id : null); }));
    }
    internal void RefreshEntryMode() { if (finishInline is not null) finishInline(); else ResetFooter(); }
    internal void RevealNewEntry() { list.ScrollIntoView(footer); list.UpdateLayout(); }
    internal bool NewEntryIsLast => ReferenceEquals(items.LastOrDefault(), footer) && VisualTreeHelper.GetParent(footer) is not null;
    internal bool IsTaskCardAt(Point screenPoint) => IsVisible &&
        Ancestor<TaskCard>(Root.InputHitTest(Root.PointFromScreen(screenPoint)) as DependencyObject) is { ArchivedPreview: false };
    internal bool IsOutsideDismissArea(Point screenPoint)
    {
        if (!IsVisible) return false;
        var hit = Root.InputHitTest(Root.PointFromScreen(screenPoint)) as DependencyObject;
        return hit is not null && Ancestor<TaskCard>(hit) is null && !SurfaceWindow.IsInteractive(hit)
            && Ancestor<System.Windows.Controls.Primitives.ScrollBar>(hit) is null;
    }
    public void PrepareHide()
    {
        flushInline?.Invoke();
        foreach (var item in items.OfType<WorkItem>())
        {
            if (RenderedCard(item.Id) is { } card)
            {
                card.CloseReminderPreview();
                card.SetMarquees(false);
            }
        }
        InvalidateAnimation();
    }
    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    { for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is T value) return value; return null; }
    private static ScrollViewer? FindScroll(DependencyObject source) => FindChild<ScrollViewer>(source);
    internal static T? FindChild<T>(DependencyObject? source) where T : DependencyObject
    {
        if (source is null) return null;
        if (source is T value) return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        { var found = FindChild<T>(VisualTreeHelper.GetChild(source, i)); if (found is not null) return found; }
        return null;
    }
}

internal sealed class LaneTemplateSelector(DataTemplate task, DataTemplate entry) : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item is WorkItem ? task : entry;
}

internal sealed partial class TaskCard : Border
{
    public static readonly DependencyProperty ControllerProperty = DependencyProperty.Register(nameof(Controller), typeof(BoardController), typeof(TaskCard),
        new PropertyMetadata(null, (d, _) => ((TaskCard)d).Build()));
    public BoardController Controller { get => (BoardController)GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }
    public bool ArchivedPreview { get; set; }
    public bool CompactArchivedPreview { get; set; }
    private Point start;
    private bool pressed;
    private long flashGeneration;
    private Popup? reminderPopup;
    private System.Windows.Threading.DispatcherTimer? reminderPollTimer;
    private long reminderOutsideSince;
    private FrameworkElement? timerDotTarget;
    internal int BuildCount { get; private set; }
    private TaskPresentationSnapshot? presentationKey;
    private sealed record StagePresentationSnapshot(Guid Id, string Name, string Text, bool? IsRunning, AiPlatform? Platform, string? ThreadId, string? ThreadName, bool Attention, bool Expanded);
    private sealed record OptionPresentationSnapshot(string Name, string Color);
    private sealed class TaskPresentationSnapshot
    {
        public required string DisplayTitle { get; init; }
        public required Lane Lane { get; init; }
        public required int StageCount { get; init; }
        public required bool TimerUnread { get; init; }
        public required string[] PendingReasons { get; init; }
        public required bool ArchivedPreview { get; init; }
        public required bool CompactArchivedPreview { get; init; }
        public required bool ReminderEnabled { get; init; }
        public required Repeat ReminderRepeat { get; init; }
        public required TimeSpan ReminderTime { get; init; }
        public required DateTime ReminderNextLocal { get; init; }
        public required bool EllipsizeStageDetails { get; init; }
        public required string ThemeId { get; init; }
        public required StagePresentationSnapshot[] Stages { get; init; }
        public required OptionPresentationSnapshot[] Ai { get; init; }
        public required OptionPresentationSnapshot? Priority { get; init; }

        public bool Matches(TaskPresentationSnapshot other)
        {
            if (Lane != other.Lane || StageCount != other.StageCount || TimerUnread != other.TimerUnread
                || ArchivedPreview != other.ArchivedPreview || CompactArchivedPreview != other.CompactArchivedPreview
                || ReminderEnabled != other.ReminderEnabled || ReminderRepeat != other.ReminderRepeat
                || ReminderTime != other.ReminderTime || ReminderNextLocal != other.ReminderNextLocal
                || EllipsizeStageDetails != other.EllipsizeStageDetails
                || !string.Equals(ThemeId, other.ThemeId, StringComparison.Ordinal)
                || !string.Equals(DisplayTitle, other.DisplayTitle, StringComparison.Ordinal)
                || PendingReasons.Length != other.PendingReasons.Length
                || Stages.Length != other.Stages.Length
                || Ai.Length != other.Ai.Length
                || Priority != other.Priority)
                return false;
            for (var i = 0; i < PendingReasons.Length; i++)
                if (!string.Equals(PendingReasons[i], other.PendingReasons[i], StringComparison.Ordinal)) return false;
            for (var i = 0; i < Stages.Length; i++)
                if (Stages[i] != other.Stages[i]) return false;
            for (var i = 0; i < Ai.Length; i++)
                if (Ai[i] != other.Ai[i]) return false;
            return true;
        }
    }
    private TaskPresentationSnapshot PresentationKey(WorkItem item)
    {
        var visibleStages = ArchivedPreview ? BoardEngine.CardStages(item) : Controller.VisibleCardStages(item);
        var stageSnaps = new StagePresentationSnapshot[visibleStages.Count];
        for (var i = 0; i < visibleStages.Count; i++)
        {
            var s = visibleStages[i];
            stageSnaps[i] = new StagePresentationSnapshot(s.Id, s.Name, s.Text, s.IsRunning, s.Codex?.Platform, s.Codex?.ThreadId, s.Codex?.ThreadName, ConversationJumpButton.Attention(item, s), !ArchivedPreview && Controller.Engine.IsStageDetailsExpanded(item.Id, s.Id));
        }
        var aiSnaps = Controller.Engine.State.AiOptions.Where(x => item.AiIds.Contains(x.Id)).Select(x => new OptionPresentationSnapshot(x.Name, x.Color)).ToArray();
        var priority = Controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == item.PriorityId);
        return new TaskPresentationSnapshot
        {
            DisplayTitle = item.DisplayTitle,
            Lane = item.Lane,
            StageCount = item.Stages.Count,
            TimerUnread = item.TimerUnread,
            PendingReasons = item.PendingReasons.ToArray(),
            ArchivedPreview = ArchivedPreview,
            CompactArchivedPreview = CompactArchivedPreview,
            ReminderEnabled = item.Reminder.Enabled,
            ReminderRepeat = item.Reminder.Repeat,
            ReminderTime = item.Reminder.Time,
            ReminderNextLocal = item.Reminder.NextLocal,
            EllipsizeStageDetails = Controller.Engine.State.Preferences.EllipsizeStageDetails,
            ThemeId = ThemePalette.Current.Id,
            Stages = stageSnaps,
            Ai = aiSnaps,
            Priority = priority is null ? null : new OptionPresentationSnapshot(priority.Name, priority.Color)
        };
    }
    public TaskCard()
    {
        TaskCardLayout.Frame(this); Cursor = Cursors.Hand;
        DataContextChanged += (_, _) => { CloseReminderPreview(); RestoreAfterRemoval(); Build(); };
        Unloaded += (_, _) => { CloseReminderPreview(); RestoreAfterRemoval(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseReminderPreview(); };
        MouseLeftButtonDown += (_, e) => { if (SurfaceWindow.IsInteractive(e.OriginalSource as DependencyObject)) return; start = e.GetPosition(this); pressed = true; CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed || DataContext is not WorkItem item) return;
            if ((e.GetPosition(this) - start).Length < 7) return;
            pressed = false; ReleaseMouseCapture();
            if (ArchivedPreview) return;
            var drag = new CardDragSession(Controller, this, item, start); Controller.CardDrag = drag;
            GiveFeedbackEventHandler feedback = (_, args) => { drag.Move(); args.UseDefaultCursors = false; Mouse.SetCursor(Cursors.Arrow); args.Handled = true; };
            GiveFeedback += feedback;
            try { DragDrop.DoDragDrop(this, new DataObject("Taskboard.Task", item.Id), DragDropEffects.Move); }
            finally { GiveFeedback -= feedback; drag.Dispose(); Controller.CardDrag = null; Mouse.SetCursor(null); }
        };
        MouseLeftButtonUp += (_, e) => { var click = pressed && IsMouseOver; pressed = false; ReleaseMouseCapture(); if (click && DataContext is WorkItem item) Controller.ToggleTaskEditor(item.Id); e.Handled = true; };
        LostMouseCapture += (_, _) => pressed = false;
        MouseLeave += (_, e) => { if (e.LeftButton != MouseButtonState.Pressed) pressed = false; };
    }
    private void Build()
    {
        CloseReminderPreview();
        timerDotTarget = null;
        BuildCount++;
        if (collapseScopes > 0) return;
        if (DataContext is not WorkItem item || Controller is null) return;
        presentationKey = PresentationKey(item);
        stageSections.Clear();
        if (ArchivedPreview && CompactArchivedPreview)
        {
            var preview = new StackPanel();
            var caption = Ui.Label(item.DisplayTitle, 13, Ui.Accent); caption.FontWeight = FontWeights.SemiBold;
            caption.Tag = "TaskTitle"; caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis; caption.ToolTip = item.DisplayTitle;
            var nameRow = new DockPanel(); var stageCount = Ui.Label(item.Stages.Count + " 个阶段", 11, Ui.Muted);
            stageCount.Margin = new Thickness(8, 4, 0, 4); DockPanel.SetDock(stageCount, Dock.Right); nameRow.Children.Add(stageCount); nameRow.Children.Add(caption);
            preview.Children.Add(TaskCardLayout.Title(nameRow));
            var excerpt = string.Join(" · ", item.Stages.Select(x => x.Text.Trim()).Where(x => x.Length > 0)).Replace('\r', ' ').Replace('\n', ' ');
            if (excerpt.Length > 0)
            {
                var details = Ui.Label(excerpt, 12, Ui.Muted); details.TextWrapping = TextWrapping.NoWrap; details.TextTrimming = TextTrimming.CharacterEllipsis;
                details.Tag = "ArchiveExcerpt"; preview.Children.Add(details);
            }
            ToolTip = "点击查看或编辑归档任务详情";
            Child = preview;
            var archiveMenu = new ContextMenu(); var restore = new MenuItem { Header = "取消归档" };
            restore.Click += (_, _) => Controller.UnarchiveTask(item.Id); archiveMenu.Items.Add(restore); ContextMenu = archiveMenu;
            return;
        }
        var stack = new StackPanel(); var title = Ui.Label(item.DisplayTitle, 13, Ui.Accent); title.FontWeight = FontWeights.SemiBold;
        title.Tag = "TaskTitle"; title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxHeight = 42;
        var titleRow = Heading(new MarqueeText(title) { HorizontalAlignment = HorizontalAlignment.Left }, item, item.Stages.Count == 1 ? item.Stages[0] : null);
        stack.Children.Add(TaskCardLayout.Title(titleRow));
        var stages = ArchivedPreview ? BoardEngine.CardStages(item) : Controller.VisibleCardStages(item); var showNames = item.Stages.Count > 1;
        if (stages.Count > 0) stack.Children.Add(new CardSeparator(Ui.Accent, overlay: true) { Tag = "TaskTitleSeparator" });
        CardHeading? firstHeading = null; Border? firstPill = null; StageDetailsText? firstDetails = null;
        foreach (var stage in stages)
        {
            var section = new StackPanel { Tag = "TaskStageSection", DataContext = stage, Opacity = stage.IsRunning == true ? 1 : .45, ClipToBounds = true };
            if (showNames)
            {
                if (stage != stages[0]) section.Children.Add(new CardSeparator(Ui.Line) { Tag = "TaskStageSeparator" });
                var name = Ui.Pill(stage.Name, ThemePalette.Current.MutedHex); name.HorizontalAlignment = HorizontalAlignment.Left;
                name.Tag = "TaskStageName";
                var label = (TextBlock)name.Child; label.FontSize = 12; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
                name.Child = null; name.Child = new MarqueeText(label);
                var heading = Heading(name, item, stage); section.Children.Add(heading);
                if (stage == stages[0]) { firstHeading = heading; firstPill = name; }
            }
            var details = ConversationJumpButton.Details(Controller, item, stage, 14, Ui.Text,
                Controller.Engine.State.Preferences.EllipsizeStageDetails, showNames ? 42 : 64, ArchivedPreview,
                adaptiveEllipsize: true,
                userExpanded: !ArchivedPreview && Controller.Engine.IsStageDetailsExpanded(item.Id, stage.Id),
                onToggleExpanded: ArchivedPreview ? null : next => { Controller.Engine.SetStageDetailsExpanded(item.Id, stage.Id, next); Controller.Persist(); });
            var text = (StageDetailsText)details.Children[0];
            if (stage == stages[0]) firstDetails = text;
            section.Children.Add(details);
            stageSections[stage.Id] = section; stack.Children.Add(section);
        }
        var pills = new WrapPanel();
        foreach (var option in Controller.Engine.State.AiOptions.Where(x => item.AiIds.Contains(x.Id))) pills.Children.Add(Ui.Pill(option.Name, option.Color));
        var priority = Controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == item.PriorityId);
        if (priority is not null) pills.Children.Add(Ui.Pill(priority.Name, priority.Color));
        stack.Children.Add(pills);
        if (item.PendingReasons.Count > 0) stack.Children.Add(Ui.Label(string.Join(" · ", item.PendingReasons), 11, Ui.Warn));
        Compact(stack); var content = new Grid(); content.Children.Add(stack);
        if (firstHeading is not null && firstPill is not null && firstDetails is not null)
        {
            void MatchGap() => TaskCardLayout.MatchTitleGap(firstHeading, firstPill, firstDetails);
            MatchGap(); content.Loaded += (_, _) => MatchGap();
        }
        if (item.TimerUnread)
        {
            var dotHost = new Grid
            {
                Width = 16, Height = 16,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Background = Ui.HitSurface,
                Cursor = Cursors.Hand,
                Tag = "TaskTimerDot"
            };
            dotHost.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = Ui.Danger,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                IsHitTestVisible = false
            });
            if (!ArchivedPreview)
            {
                timerDotTarget = dotHost;
                dotHost.MouseEnter += (_, _) => { reminderOutsideSince = 0; OpenReminderPreview(item, dotHost); };
                dotHost.PreviewMouseLeftButtonDown += (_, e) => { reminderOutsideSince = 0; OpenReminderPreview(item, dotHost); e.Handled = true; };
                dotHost.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
            }
            content.Children.Add(dotHost);
        }
        Child = content;
        if (ArchivedPreview)
        {
            var historyMenu = new ContextMenu(); var restore = new MenuItem { Header = "取消归档" };
            restore.Click += (_, _) => Controller.UnarchiveTask(item.Id); historyMenu.Items.Add(restore); ContextMenu = historyMenu; Cursor = Cursors.Arrow; return;
        }
        var menu = new ContextMenu(); var delete = new MenuItem { Header = "删除任务（可撤销）" };
        delete.Click += (_, _) => Controller.DeleteTask(item.Id); menu.Items.Add(delete);
        if (!ArchivedPreview)
        {
            var archive = new MenuItem { Header = "归档任务" }; archive.Click += (_, _) => Controller.ArchiveTask(item.Id); menu.Items.Add(archive);
        }
        if (item.PendingReasons.Contains("定时提醒到期"))
        {
            var acknowledge = new MenuItem { Header = "已查看提醒" };
            acknowledge.Click += (_, _) => { item.TimerUnread = false; item.PendingReasons.Remove("定时提醒到期"); Controller.Engine.Notify([item.Id]); Build(); };
            menu.Items.Add(acknowledge);
        }
        ContextMenu = menu;
    }
    public void Rebuild()
    {
        if (DataContext is WorkItem item && Controller is not null && presentationKey is not null && presentationKey.Matches(PresentationKey(item))) return;
        Build();
    }
    internal void SetMarquees(bool restart)
    {
        Visit(this);
        void Visit(DependencyObject node)
        {
            if (node is MarqueeText text) { if (restart) text.Restart(); else text.Pause(); return; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
    }
    private CardHeading Heading(FrameworkElement label, WorkItem item, Stage? stage)
    {
        var row = new CardHeading(label);
        if (stage is null || item.Lane == Lane.Inspiration) return row;
        {
            var binding = stage.Codex;
            var text = Ui.Label(binding is null ? "未绑定" : string.IsNullOrWhiteSpace(binding.ThreadName) ? "未命名会话" : binding.ThreadName, 11, Ui.Muted);
            text.TextAlignment = TextAlignment.Right;
            var name = new MarqueeText(text, conversation: true) { Tag = "TaskConversationName", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right, ToolTip = binding is null ? null : text.Text + " · " + AiState.Name(binding.Platform) + " · " + binding.ThreadId, DataContext = stage };
            row.SetConversation(!ArchivedPreview ? new ConversationDragButton(Controller, item, stage, name) : name);
        }
        if (item.Stages.Count > 1)
        {
            row.SetStateButton(StageButton(item, stage));
        }
        return row;
    }
    private Button StageButton(WorkItem item, Stage stage)
    {
        var running = stage.IsRunning == true;
        var button = Ui.Button("", () => { });
        button.Click += (_, _) =>
        {
            if (!Controller.SetStageRunningByUser(item.Id, stage.Id, stage.IsRunning != true)) { Ui.Hint(button, BoardEngine.LastRunningStageMessage, !Controller.Smoke, this); return; }
            Controller.Persist();
        };
        button.Tag = "TaskStageRun"; button.DataContext = stage; button.Content = new StageRunIcon(running, !ArchivedPreview);
        button.Width = button.Height = 24; button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 0, 0);
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.IsEnabled = !ArchivedPreview;
        button.ToolTip = ArchivedPreview ? "阶段状态（归档预览）" : running ? "运行中，点击改为未运行" : "未运行，点击改为运行中";
        System.Windows.Automation.AutomationProperties.SetName(button, stage.Name + "：" + (running ? "运行中" : "未运行"));
        return button;
    }
    private static void Compact(DependencyObject root)
    {
        if (root is TextBlock text) text.Margin = new Thickness(text.Margin.Left, text.Margin.Top * .6, text.Margin.Right, text.Margin.Bottom * .6);
        if (root is Border pill && pill.Child is TextBlock or MarqueeText) pill.Margin = new Thickness(pill.Margin.Left, pill.Margin.Top * .6, pill.Margin.Right, pill.Margin.Bottom * .6);
        // Newly constructed card visuals only; no shared editor styles change.
        if (root is Panel panel) foreach (UIElement child in panel.Children) Compact(child);
        else if (root is Decorator decorator && decorator.Child is not null) Compact(decorator.Child);
    }
    public void Flash()
    {
        StopFlash(); var generation = ++flashGeneration;
        var brush = new SolidColorBrush(Colors.Transparent); BorderBrush = brush;
        var animation = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(800) };
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.IndianRed, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.IndianRed, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
        animation.Completed += (_, _) => { if (generation == flashGeneration) BorderBrush = Ui.Line; }; brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
    private static bool IsTimerDot(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is FrameworkElement { Tag: "TaskTimerDot" or "DismissReminderBubble" }) return true;
        return false;
    }
    internal void CloseReminderPreview()
    {
        reminderPollTimer?.Stop();
        reminderOutsideSince = 0;
        if (reminderPopup is not null) { reminderPopup.IsOpen = false; reminderPopup.Child = null; reminderPopup = null; }
    }
    internal bool IsReminderPreviewOpenForVerification => reminderPopup is not null && (reminderPopup.IsOpen || Controller?.Smoke == true);
    internal static bool IsPointerInReminderCorridor(Rect anchorScreen, Rect popupScreen, Point pointerScreen)
    {
        if (anchorScreen.IsEmpty || popupScreen.IsEmpty) return false;
        var paddedAnchor = anchorScreen;
        paddedAnchor.Inflate(4, 4);
        var paddedPopup = popupScreen;
        paddedPopup.Inflate(2, 2);
        return HoverCorridor.Contains(paddedAnchor, paddedPopup, pointerScreen);
    }
    internal bool PollReminderHoverForVerification(Point pointerScreen, long nowTickMs, Rect anchorScreen, Rect popupScreen)
    {
        if (reminderPopup is null) return false;
        if (IsPointerInReminderCorridor(anchorScreen, popupScreen, pointerScreen))
        {
            reminderOutsideSince = 0;
            return true;
        }
        if (reminderOutsideSince == 0)
        {
            reminderOutsideSince = nowTickMs;
            return true;
        }
        if (nowTickMs - reminderOutsideSince >= 120)
        {
            CloseReminderPreview();
            return false;
        }
        return true;
    }
    private void StartReminderHoverPolling()
    {
        reminderOutsideSince = 0;
        if (reminderPollTimer is null)
        {
            reminderPollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            reminderPollTimer.Tick += OnReminderPollTick;
        }
        reminderPollTimer.Stop();
        reminderPollTimer.Start();
    }
    private void OnReminderPollTick(object? sender, EventArgs e)
    {
        if (reminderPopup is not { IsOpen: true } popup || !IsVisible || timerDotTarget is null || !timerDotTarget.IsLoaded
            || PresentationSource.FromVisual(timerDotTarget) is null
            || popup.Child is not FrameworkElement child || !child.IsLoaded
            || PresentationSource.FromVisual(child) is not System.Windows.Interop.HwndSource { Handle: var popupHandle } || popupHandle == IntPtr.Zero)
        {
            CloseReminderPreview();
            return;
        }
        var anchorTopLeft = timerDotTarget.PointToScreen(new Point(0, 0));
        var anchorBottomRight = timerDotTarget.PointToScreen(new Point(timerDotTarget.ActualWidth, timerDotTarget.ActualHeight));
        var anchorBounds = new Rect(anchorTopLeft, anchorBottomRight);
        var popupBounds = Native.ScreenBounds(popupHandle);
        var cursor = System.Windows.Forms.Cursor.Position;
        var pointer = new Point(cursor.X, cursor.Y);
        var now = Environment.TickCount64;
        if (IsPointerInReminderCorridor(anchorBounds, popupBounds, pointer))
        {
            reminderOutsideSince = 0;
            return;
        }
        if (reminderOutsideSince == 0) reminderOutsideSince = now;
        else if (now - reminderOutsideSince >= 120) CloseReminderPreview();
    }
    internal static string FormatReminderTime(Reminder reminder, DateTime now)
    {
        var timeText = reminder.Time.ToString(@"hh\:mm");
        if (reminder.Enabled && reminder.Repeat == Repeat.Daily) return $"每天 {timeText}";
        if (reminder.Enabled && reminder.Repeat == Repeat.Weekly)
            return $"每{new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)reminder.Weekday]} {timeText}";
        var when = reminder.LastOccurrence ?? reminder.NextLocal;
        if (when.Date == now.Date) return $"今天 {when:HH:mm}";
        if (when.Date == now.Date.AddDays(1)) return $"明天 {when:HH:mm}";
        if (when.Date == now.Date.AddDays(-1)) return $"昨天 {when:HH:mm}";
        return $"{when:M月d日 HH:mm}";
    }
    internal Popup? OpenReminderPreviewForVerification()
    {
        if (DataContext is not WorkItem item || timerDotTarget is null) return null;
        return OpenReminderPreview(item, timerDotTarget);
    }
    private Popup OpenReminderPreview(WorkItem item, FrameworkElement anchor)
    {
        timerDotTarget = anchor;
        if (reminderPopup is { IsOpen: true }) { reminderOutsideSince = 0; return reminderPopup; }
        var body = new StackPanel { Margin = new Thickness(0) };
        var timeLabel = new TextBlock
        {
            Text = FormatReminderTime(item.Reminder, DateTime.Now),
            FontSize = 11,
            Foreground = Ui.Warn,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0),
            Tag = "ReminderPreviewTime"
        };
        body.Children.Add(timeLabel);
        var dismiss = Ui.Button("关闭提醒", () =>
        {
            CloseReminderPreview();
            Controller.DismissTaskReminder(item.Id);
        });
        dismiss.Tag = "DismissReminderBubble";
        dismiss.FontSize = 12;
        dismiss.MinHeight = 28;
        dismiss.Margin = new Thickness(0, 5, 0, 0);
        dismiss.Padding = new Thickness(10, 4, 10, 4);
        dismiss.HorizontalAlignment = HorizontalAlignment.Left;
        dismiss.ToolTip = "清除本次到期标记并关闭此任务的定时提醒";
        body.Children.Add(dismiss);
        var frame = new Border
        {
            Background = Ui.HintBackground,
            BorderBrush = Ui.ButtonBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 8, 6),
            Child = body
        };
        frame.MouseEnter += (_, _) => reminderOutsideSince = 0;
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 3,
            HorizontalOffset = -48,
            StaysOpen = false,
            AllowsTransparency = true,
            Tag = "TimerReminderPreviewPopup",
            Child = frame
        };
        popup.Closed += (_, _) =>
        {
            reminderPollTimer?.Stop();
            reminderOutsideSince = 0;
            if (ReferenceEquals(reminderPopup, popup)) { reminderPopup.Child = null; reminderPopup = null; }
        };
        reminderPopup = popup;
        if (!Controller.Smoke)
        {
            popup.IsOpen = true;
            StartReminderHoverPolling();
        }
        return popup;
    }
    public void StopFlash() { flashGeneration++; if (BorderBrush is SolidColorBrush brush && !brush.IsFrozen) brush.BeginAnimation(SolidColorBrush.ColorProperty, null); BorderBrush = Ui.Line; }
}
