using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class EditorSurface : Grid
{
    internal const double DefaultHeight = 650 * 11.0 / 13;
    public Grid Root => this;
    public SurfaceWindow? Host { get; internal set; }
    public bool IsActive => Host?.IsActive == true && (Session.TaskId is not null || controller.ActiveDraft == Session.Id);
    private readonly BoardController controller;
    private readonly bool snapshotMode;
    public EditSession Session { get; }
    private readonly StackPanel stages = new();
    private readonly ReminderEditor reminderEditor;
    private readonly System.Collections.Generic.Dictionary<Guid, TextBox> stageThreads = new();
    private readonly System.Collections.Generic.Dictionary<Guid, StageDetailsEditor> stageDetailsEditors = new();
    internal readonly System.Collections.Generic.Dictionary<Guid, StageConversationPicker> StagePickers = new();
    private readonly TextBlock status;
    private readonly Button aiButton;
    private readonly Button priorityButton;
    private readonly OptionMenuToggle aiMenu;
    private readonly OptionMenuToggle priorityMenu;
    private readonly DispatcherTimer autosave;
    private bool submitting;
    private bool closing;
    private bool archiveRequested;
    private Button? archiveButton;
    private readonly Func<System.Threading.Tasks.Task<bool>> saveAndClose;
    internal System.Threading.Tasks.Task<bool> SaveAndClose() => saveAndClose();
    private readonly Button closeButton;
    private readonly Border draftNumber;
    private readonly TextBlock numberText;
    private readonly TextBlock taskHeader;
    internal int DraftNumber { get; private set; }
    internal void SetDraftPresentation(bool front, int number)
    {
        DraftNumber = number; numberText.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        closeButton.Visibility = front ? Visibility.Visible : Visibility.Collapsed;
        draftNumber.Visibility = front ? Visibility.Collapsed : Visibility.Visible;
    }
    public EditorSurface(BoardController controller, EditSession session, bool snapshotMode = false)
    {
        this.controller = controller; Session = session; this.snapshotMode = snapshotMode;
        autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        Root.RowDefinitions.Add(new RowDefinition()); Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, Margin = new Thickness(14, 4, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        taskHeader = Ui.Label("", 13); taskHeader.Tag = "TaskEditorHeading"; taskHeader.TextWrapping = TextWrapping.NoWrap; taskHeader.TextTrimming = TextTrimming.CharacterEllipsis; header.Children.Add(taskHeader); RefreshArchiveCaption();
        numberText = new TextBlock { FontSize = 12 };
        Ui.BindBrush(numberText, TextBlock.ForegroundProperty, "CloseButtonTextBrush", Ui.CloseButtonText);
        var badgeDiameter = DraftBadgeDiameter();
        draftNumber = new Border { Tag = "DraftOrdinal", Width = badgeDiameter, Height = badgeDiameter, Margin = new Thickness(3),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(badgeDiameter / 2), IsHitTestVisible = false,
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8, 4, 8, 4), Child = numberText }, Visibility = Visibility.Collapsed };
        Ui.BindBrush(draftNumber, Border.BackgroundProperty, "CloseButtonBrush", Ui.CloseButtonSurface);
        Ui.BindBrush(draftNumber, Border.BorderBrushProperty, "CloseButtonBorderBrush", Ui.CloseButtonBorder);
        closeButton = Ui.Button(session.TaskId is null ? "×" : "取消保存", CloseEditor); closeButton.Tag = "EditorClose"; closeButton.Width = session.TaskId is null ? 48 : 84; closeButton.Height = 40; closeButton.Padding = new Thickness(0);
        if (Application.Current?.TryFindResource("EditorCloseButton") is Style closeStyle) closeButton.Style = closeStyle;
        else { closeButton.Background = Ui.CloseButtonSurface; closeButton.BorderBrush = Ui.CloseButtonBorder; closeButton.Foreground = Ui.CloseButtonText; }
        var closeSlot = new Grid { VerticalAlignment = VerticalAlignment.Center }; closeSlot.Children.Add(closeButton); closeSlot.Children.Add(draftNumber);
        Grid.SetColumn(closeSlot, 1); header.Children.Add(closeSlot); Root.Children.Add(header);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (Host is null || e.ButtonState != System.Windows.Input.MouseButtonState.Pressed || SurfaceWindow.IsInteractive(e.OriginalSource as DependencyObject)) return;
            Host.DragMove(); controller.EditorMoved(this); e.Handled = true;
        };
        var body = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
        body.Children.Add(Ui.Label("任务名称", 12, Ui.Muted));
        var title = Ui.Input(session.Working.Title); title.TextChanged += (_, _) => { session.Working.Title = title.Text; Changed(); }; body.Children.Add(title);
        body.Children.Add(Ui.Label("任务详情", 12, Ui.Muted)); body.Children.Add(stages); RenderStages();
        var newStage = Ui.Button("＋ 新阶段", () =>
        {
            var added = controller.Engine.AddStage(session);
            RenderStages();
            Changed();
            FocusStageDetails(added.Id);
        });
        newStage.Tag = "AddStage"; newStage.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(newStage);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        for (var i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
        var statePanel = new StackPanel(); statePanel.Children.Add(Ui.Label("当前状态", 12, Ui.Muted));
        var lane = laneSelector = new ComboBox { ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(), SelectedItem = session.StatusSelected ? BoardColumns.For(controller.Engine.State.Preferences,session.Working) : null, Margin = new Thickness(0, 3, 5, 0), MinHeight = 32 };
        lane.SelectionChanged += (_, _) => { if (lane.SelectedItem is BoardColumn definition) { definition.Assign(session.Working); session.StatusSelected = true; Changed(); } };
        statePanel.Children.Add(lane); row.Children.Add(statePanel);
        var aiPanel = new StackPanel(); aiPanel.Children.Add(Ui.Label("平台（多选）", 12, Ui.Muted));
        aiButton = Ui.Button("选择", () => ShowOptions(true)); aiButton.MinHeight = 32; aiButton.Margin = new Thickness(0, 3, 5, 0); aiButton.Padding = new Thickness(10, 6, 10, 6); aiPanel.Children.Add(aiButton); Grid.SetColumn(aiPanel, 1); row.Children.Add(aiPanel);
        var priorityPanel = new StackPanel(); priorityPanel.Children.Add(Ui.Label("优先级", 12, Ui.Muted));
        priorityButton = Ui.Button("选择", () => ShowOptions(false)); priorityButton.MinHeight = 32; priorityButton.Margin = new Thickness(0, 3, 0, 0); priorityButton.Padding = new Thickness(10, 6, 10, 6); priorityPanel.Children.Add(priorityButton); Grid.SetColumn(priorityPanel, 2); row.Children.Add(priorityPanel);
        aiMenu = new OptionMenuToggle(aiButton); priorityMenu = new OptionMenuToggle(priorityButton);
        body.Children.Add(row); body.Children.Add(optionManager); RefreshOptionLabels();
        reminderEditor = new ReminderEditor(session.Working.Reminder, Changed);
        body.Children.Add(reminderEditor);
        var expectScroll = session.Working.Stages.Any(s => s.Text.Length > StageDetailsEditor.DeferredHydrationThreshold);
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = expectScroll ? ScrollBarVisibility.Visible : ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.ScrollChanged += (_, e) => { if (e.VerticalOffset > 0.5) HydrateVisibleLongStages(scroll); };
        Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        var bottom = new Grid { Margin = new Thickness(12, 6, 12, 12) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        status = Ui.Label(session.TaskId is null ? "未保存到任务列表" : "修改尚未提交", 11, Ui.Muted); Grid.SetColumnSpan(status, 2); bottom.Children.Add(status);
        var delete = Ui.Button(session.TaskId is null ? "删除任务" : "归档任务", async () =>
        {
            if (closing || submitting || snapshotMode) return;
            if (Session.TaskId is null) { autosave.Stop(); controller.DeleteDraft(this); return; }
            archiveRequested = true;
            try { await SaveAndClose(); } finally { archiveRequested = false; }
        }); delete.Tag = session.TaskId is null ? "DeleteDraft" : "ArchiveTask";
        if (session.TaskId is not null) archiveButton = delete;
        RefreshArchiveCaption();
        delete.Height = closeButton.Height; delete.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.BindBrush(delete, Control.ForegroundProperty, "DangerBrush", Ui.Danger); Grid.SetRow(delete, 1); bottom.Children.Add(delete);
        saveAndClose = async () =>
        {
            if (submitting || closing || snapshotMode) return false;
            if (!Session.StatusSelected) { status.Text = "请先选择当前状态，再保存任务。"; laneSelector.Focus(); laneSelector.IsDropDownOpen = true; return false; }
            if (!reminderEditor.TryCommit()) return false;
            submitting = true;
            Root.IsHitTestVisible = false;
            try
            {
                autosave.Stop(); controller.Engine.StageSession(Session);
                if (await controller.SubmitSession(Session, archiveRequested)) { controller.RemoveEditor(this); Destroy(); return true; }
                return false;
            }
            finally { submitting = false; Root.IsHitTestVisible = true; }
        };
        var save = Ui.Button("保存任务", async () => await SaveAndClose(), true);
        save.Tag = "SaveTask";
        if (session.TaskId is not null) closeButton.SetBinding(FrameworkElement.WidthProperty,
            new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = save });
        save.Height = closeButton.Height; Grid.SetColumn(save, 1); Grid.SetRow(save, 1); bottom.Children.Add(save); Grid.SetRow(bottom, 2); Root.Children.Add(bottom);
        autosave.Tick += async (_, _) => { autosave.Stop(); if (await controller.Flush() && !submitting && !closing) status.Text = Session.TaskId is null ? Session.Working.HasContent ? "草稿已暂存" : "未保存到任务列表" : "修改已暂存，正式任务仍保持原值"; };
        // Block edits during persistence without triggering the disabled theme's
        // global dimming/focus change just before the closing snapshot.
        Root.PreviewKeyDown += (_, e) => { if (submitting || closing) e.Handled = true; };
        Root.PreviewTextInput += (_, e) => { if (submitting || closing) e.Handled = true; };
        Root.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler((_, e) => { if (submitting || closing) e.Handled = true; }));
        Root.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler((_, e) => { if (submitting || closing) e.Handled = true; }));
        DataObject.AddPastingHandler(Root, (_, e) => { if (submitting || closing) e.CancelCommand(); });
        Root.PreviewMouseDown += (_, e) => { foreach (var picker in StagePickers.Values) picker.CollapseUnlessContains(e.OriginalSource as DependencyObject); };
    }
    private void Changed()
    {
        if (snapshotMode) return;
        if (status is null) return;
        controller.Engine.StageSession(Session); status.Text = Session.TaskId is null ? Session.Working.HasContent ? "正在暂存…" : "未保存到任务列表" : "修改尚未提交";
        autosave.Stop(); autosave.Start();
    }
    private static double cachedBadgeDiameter;
    private static double DraftBadgeDiameter()
    {
        if (cachedBadgeDiameter > 0) return cachedBadgeDiameter;
        var legacyClose = Ui.Button("×", () => { }); legacyClose.FontFamily = new FontFamily("Microsoft YaHei UI");
        legacyClose.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return cachedBadgeDiameter = Math.Min(legacyClose.DesiredSize.Width, legacyClose.DesiredSize.Height) - 6;
    }
    private static double stageBindingWidth;
    private static double StageBindingWidth()
    {
        if (stageBindingWidth > 0) return stageBindingWidth;
        var probe = Ui.Button("", () => { });
        probe.Content = StageRunIcon.Caption(false, false);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return stageBindingWidth = (probe.DesiredSize.Width - probe.Margin.Left - probe.Margin.Right) * 1.6;
    }
    private void RenderStages()
    {
        foreach (var existing in stageDetailsEditors.Values) existing.CancelIdleHydration();
        foreach (var picker in StagePickers.Values) picker.Dispose(); StagePickers.Clear(); stages.Children.Clear(); stageThreads.Clear(); stageDetailsEditors.Clear();
        var bindingWidth = StageBindingWidth();
        var expandedLongCount = 0;
        var collapseInactive = controller.Engine.State.Preferences.CollapseInactiveStageDetails;
        foreach (var stage in Session.Working.Stages)
        {
            var panel = new StackPanel();
            var isExpanded = stage.IsRunning == true || !collapseInactive;
            var sliceLimit = isExpanded && stage.Text.Length > StageDetailsEditor.DeferredHydrationThreshold
                ? (expandedLongCount++ == 0 ? StageDetailsEditor.PrimaryInitialSliceChars : StageDetailsEditor.SecondaryInitialSliceChars)
                : StageDetailsEditor.PrimaryInitialSliceChars;
            var details = new StageDetailsEditor(stage.Text, stage.IsRunning == true, collapseInactive, sliceLimit, snapshotMode);
            stageDetailsEditors[stage.Id] = details;
            var heading = new DockPanel { Margin = new Thickness(7, 5, 7, 0) };
            var current = Ui.Button("", () => { }); current.Tag = "StageRun"; current.DataContext = stage;
            current.Width = current.Height = 24; current.Padding = new Thickness(0);
            current.VerticalAlignment = VerticalAlignment.Center;
            Ui.ApplyCircularStyle(current);
            void PresentRunState()
            {
                current.Content = new StageRunIcon(stage.IsRunning == true, !snapshotMode) { Opacity = stage.IsRunning == true ? 1 : .5 };
                current.ToolTip = stage.IsRunning == true ? "点击设为未运行" : "点击设为运行中";
                System.Windows.Automation.AutomationProperties.SetName(current, stage.IsRunning == true ? "运行中，点击切换为未运行" : "未运行，点击切换为运行中");
            }
            PresentRunState(); current.Click += (_, _) =>
            {
                var running = stage.IsRunning != true;
                if (!BoardEngine.CanSetStageRunning(Session.Working, stage.Id, running)) { Ui.Hint(current, BoardEngine.LastRunningStageMessage, !controller.Smoke); return; }
                stage.IsRunning = running; Changed(); PresentRunState(); details.SetRunning(running);
            };
            var bindingPanel = StageBindingEditor(stage, out var bindingButton);
            bindingButton.Width = bindingWidth;
            DockPanel.SetDock(bindingButton, Dock.Right); heading.Children.Add(bindingButton);
            DockPanel.SetDock(current, Dock.Right); heading.Children.Add(current);
            var name = Ui.Input(stage.Name); name.Tag = "StageName"; name.MinHeight = 28; name.MinWidth = 64; name.MaxWidth = 155; name.Padding = new Thickness(9, 0, 9, 0);
            name.Margin = new Thickness(0, 3, 3, 3); name.HorizontalAlignment = HorizontalAlignment.Stretch;
            Ui.BindBrush(name, Control.ForegroundProperty, "AccentBrush", Ui.Accent);
            name.VerticalContentAlignment = VerticalAlignment.Center; name.VerticalAlignment = VerticalAlignment.Center;
            RoundedInput.SetRadius(name, 14);
            name.TextChanged += (_, _) => { stage.Name = name.Text; Changed(); };
            var remove = Ui.Button("×", () => { }); remove.Tag = "StageRemove"; remove.DataContext = stage;
            remove.Width = remove.Height = 24; remove.Padding = new Thickness(0); remove.Margin = new Thickness(1, 3, 4, 3);
            remove.VerticalAlignment = VerticalAlignment.Center; remove.ToolTip = "删除此阶段";
            remove.HorizontalContentAlignment = HorizontalAlignment.Center;
            Ui.ApplyCircularStyle(remove);
            System.Windows.Automation.AutomationProperties.SetName(remove, "删除阶段：" + stage.Name);
            remove.Click += (_, _) =>
            {
                if (snapshotMode || submitting || closing) return;
                if (!controller.Engine.RemoveStage(Session, stage.Id, out var message))
                { if (message is not null) Ui.Hint(remove, message, !controller.Smoke); return; }
                RenderStages(); Changed();
            };
            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 155 });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(remove, 1);
            nameRow.Children.Add(name); nameRow.Children.Add(remove); heading.Children.Add(nameRow);
            panel.Children.Add(heading); panel.Children.Add(bindingPanel);
            details.TextValueChanged += value => { stage.Text = value; Changed(); }; panel.Children.Add(details);
            var frame = Ui.Panel(panel, Ui.StagePanelSurface, 0); frame.CornerRadius = new CornerRadius(20); frame.DataContext = stage;
            var reorder = StageReorderGesture.Create(stages, frame,
                () => !snapshotMode && !submitting && !closing,
                index => { if (controller.Engine.ReorderStage(Session, stage.Id, index)) { RenderStages(); Changed(); } });
            Grid.SetColumn(reorder, 2); nameRow.Children.Add(reorder);
            StagePickers[stage.Id].StageScope = frame;
            panel.IsKeyboardFocusWithinChanged += (_, _) =>
                Ui.BindBrush(frame, Border.BorderBrushProperty, panel.IsKeyboardFocusWithin ? "AccentBrush" : "LineBrush", panel.IsKeyboardFocusWithin ? Ui.Accent : Ui.Line);
            stages.Children.Add(frame);
        }
    }
    private StackPanel StageBindingEditor(Stage stage, out Button button)
    {
        var picker = new StageConversationPicker(controller, stage, Changed, snapshotMode, this);
        StagePickers[stage.Id] = picker; stageThreads[stage.Id] = picker.Input; button = picker.Trigger;
        return new StackPanel { Visibility = Visibility.Collapsed };
    }
    private void FocusStageDetails(Guid stageId)
    {
        if (snapshotMode || !stageDetailsEditors.TryGetValue(stageId, out var editor)) return;
        void FocusInput()
        {
            editor.EnsureHydrated(preserveCaret: false);
            editor.BringIntoView();
            editor.Input.Focus();
            editor.Input.CaretIndex = editor.Input.Text.Length;
            editor.Input.SelectionLength = 0;
        }
        UpdateLayout();
        FocusInput();
        if (!editor.Input.IsKeyboardFocused)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusInput));
    }
    internal void RefreshStageDetailsPolicy()
    {
        foreach (var details in SnapshotChildren<StageDetailsEditor>(stages))
            details.SetCollapseInactive(controller.Engine.State.Preferences.CollapseInactiveStageDetails);
    }
    internal string ApplyConversationDrop(Stage source, Guid? expectedBinding, ConversationDropTarget target)
    {
        if (target.IsUnbind)
        {
            if (closing || submitting || snapshotMode || !Session.Working.Stages.Contains(source) || source.Codex?.BindingId != expectedBinding
                || Session.TaskId is null && controller.ActiveDraft != Session.Id)
                return "未解绑：阶段或原绑定已变化，请重新拖动";
            if (source.Codex is null) return "未解绑：当前阶段尚未绑定会话，无需解除";
            source.Codex = null;
            if (StagePickers.TryGetValue(source.Id, out var unlinkedPicker)) unlinkedPicker.RefreshBinding();
            Changed();
            return "已解除绑定，保存任务后生效";
        }
        if (!target.CanBind) return "未绑定：" + target.Reason;
        if (closing || submitting || snapshotMode || !Session.Working.Stages.Contains(source) || source.Codex?.BindingId != expectedBinding
            || Session.TaskId is null && controller.ActiveDraft != Session.Id)
            return "未绑定：阶段或原绑定已变化，请重新拖动";
        if (source.Codex?.Platform != target.Platform || source.Codex?.ThreadId != target.ThreadId) source.Codex = new CodexBinding { Platform = target.Platform, ThreadId = target.ThreadId!, ThreadName = target.Name };
        else source.Codex!.ThreadName = target.Name;
        if (StagePickers.TryGetValue(source.Id, out var picker)) picker.RefreshBinding();
        Changed();
        return "已选择 · " + target.Name + "，保存任务后生效" + (target.ClipboardRestored == false ? "；剪贴板未确认恢复" : "");
    }
    internal void RefreshArchiveCaption()
    {
        var archived = Session.TaskId is not null && controller.Engine.State.Archives.Any(x => x.Item.Id == Session.TaskId);
        if (archiveButton is not null) { archiveButton.Content = archived ? "已归档" : "归档任务"; archiveButton.IsEnabled = !archived; }
        taskHeader.Text = Session.TaskId is null ? "新建任务 · 自动暂存" : archived ? "已归档 · 编辑任务" : "编辑任务 · 保存后生效";
        // Archive cards are opened from settings (layer 2). Keep their editor
        // in that activation band so it can appear above its invoking window.
        if (Host is TaskEditorWindow) Host.WindowLayer = archived ? 2 : 1;
    }
    public void RefreshOptionLabels()
    {
        var selected = controller.Engine.State.AiOptions.Where(x => Session.Working.AiIds.Contains(x.Id)).Select(x => x.Name).ToArray();
        var caption = selected.Length == 0 ? "未选择 ▾" : selected[0] + (selected.Length > 1 ? " +" + (selected.Length - 1) : "");
        aiButton.Content = new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis };
        aiButton.ToolTip = selected.Length == 0 ? null : string.Join("、", selected);
        var p = controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == Session.Working.PriorityId);
        priorityButton.Content = new TextBlock { Text = p?.Name ?? "未选择 ▾", TextTrimming = TextTrimming.CharacterEllipsis };
        priorityButton.ToolTip = null;
        if (p is null) priorityButton.ClearValue(Control.ForegroundProperty);
        else priorityButton.Foreground = Ui.ReadableTagBrush(p.Color, Ui.ButtonSurface);
        foreach (var colorButton in SnapshotChildren<Button>(optionManager).Where(x => Equals(x.Tag, "OptionColor")))
            if (colorButton.DataContext is FieldOption option) colorButton.Foreground = Ui.Brush(option.Color);
    }
    internal void ApplyTheme()
    {
        RefreshOptionLabels();
        foreach (var picker in StagePickers.Values) picker.ApplyTheme();
        foreach (var frame in stages.Children.OfType<Border>())
        {
            var focused = frame.IsKeyboardFocusWithin;
            Ui.BindBrush(frame, Border.BorderBrushProperty, focused ? "AccentBrush" : "LineBrush", focused ? Ui.Accent : Ui.Line);
        }
    }
    private void ShowOptions(bool ai)
    {
        var toggle = ai ? aiMenu : priorityMenu;
        if (toggle.CloseOrConsume()) return;
        (ai ? priorityMenu : aiMenu).Close();
        var button = ai ? aiButton : priorityButton; var menu = new ContextMenu();
        var clear = new MenuItem { Header = "清空选择" };
        clear.Click += (_, _) => { if (ai) Session.Working.AiIds.Clear(); else Session.Working.PriorityId = null; Changed(); RefreshOptionLabels(); }; menu.Items.Add(clear);
        foreach (var option in ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var select = new CheckBox { Content = option.Name, Foreground = Ui.ReadableTagBrush(option.Color, Ui.MenuSurface), VerticalAlignment = VerticalAlignment.Center,
                IsChecked = ai ? Session.Working.AiIds.Contains(option.Id) : Session.Working.PriorityId == option.Id, MinWidth = 140 };
            select.Click += (_, _) =>
            {
                if (ai) { if (select.IsChecked == true) { if (!Session.Working.AiIds.Contains(option.Id)) Session.Working.AiIds.Add(option.Id); } else Session.Working.AiIds.Remove(option.Id); }
                else { Session.Working.PriorityId = select.IsChecked == true ? option.Id : null; menu.IsOpen = false; }
                Changed(); RefreshOptionLabels();
            };
            row.Children.Add(select);
            var color = Ui.Button("●", () => controller.ColorMenu(option, button)); color.Tag = "OptionColor"; color.DataContext = option; color.ToolTip = "更改“" + option.Name + "”的颜色"; color.Foreground = Ui.Brush(option.Color); color.Width = color.Height = 28; color.Padding = new Thickness(0); Ui.ApplyCircularStyle(color); row.Children.Add(color);
            menu.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true });
        }
        menu.Items.Add(UiMenu("＋ 管理选项", () => ShowOptionManager(ai)));
        toggle.Open(menu);
    }
    private static MenuItem UiMenu(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => click(); return item; }
    private async void CloseEditor()
    {
        if (closing || submitting) return;
        _ = controller.CleanupRetainedStages();
        if (Session.TaskId is null) { controller.CloseDraftWindow(); return; }
        closing = true; Root.IsHitTestVisible = false;
        try
        {
            autosave.Stop(); controller.Engine.Discard(Session);
            if (!await controller.Flush()) { Session.Visible = true; controller.Engine.StageSession(Session); return; }
            controller.RemoveEditor(this); Destroy();
        }
        finally { closing = false; Root.IsHitTestVisible = true; }
    }
    internal void CloseOptionMenus() { aiMenu.Close(); priorityMenu.Close(); reminderEditor.ClosePopups(); }
    public void PrepareHide() { foreach (var picker in StagePickers.Values) picker.Collapse(); CloseOptionMenus(); controller.CloseColorMenu(); autosave.Stop(); controller.Engine.StageSession(Session); }
    internal void Suspend()
    {
        if (closing || submitting) return;
        PrepareHide(); Session.Visible = false; controller.Engine.StageSession(Session);
        controller.RemoveEditor(this); Destroy();
    }
    public void Show() => controller.ShowEditor(this, false);
    public void Activate() => controller.ShowEditor(this, true);
    internal void EnsureAllStagesHydrated()
    {
        foreach (var details in stageDetailsEditors.Values) details.EnsureHydrated(preserveCaret: details.Input.IsKeyboardFocusWithin);
    }
    private void HydrateVisibleLongStages(ScrollViewer scroll)
    {
        if (snapshotMode) return;
        foreach (var details in stageDetailsEditors.Values)
        {
            if (details.IsHydrated) continue;
            try
            {
                var top = details.TranslatePoint(new Point(0, 0), scroll).Y;
                if (top <= scroll.ViewportHeight + 400) details.EnsurePreviewExpanded();
            }
            catch (InvalidOperationException) { }
        }
    }
    public void Destroy() { foreach (var details in stageDetailsEditors.Values) details.CancelIdleHydration(); reminderEditor.ClosePopups(); foreach (var picker in StagePickers.Values) picker.Dispose(); aiMenu.Dispose(); priorityMenu.Dispose(); autosave.Stop(); Host = null; }
    private void ShowOptionManager(bool ai)
    {
        optionManager.Children.Clear(); optionManager.Visibility = Visibility.Visible;
        var heading = new DockPanel(); var close = Ui.Button("×", () => optionManager.Visibility = Visibility.Collapsed); close.Width = close.Height = 28; close.Padding = new Thickness(0); Ui.ApplyCircularStyle(close); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(Ui.Label(ai ? "平台选项" : "优先级选项", 14)); optionManager.Children.Add(heading);
        foreach (var option in ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions)
        {
            var row = new DockPanel(); var remove = Ui.Button("删除", () => { controller.DeleteOption(option.Id, ai); ShowOptionManager(ai); }); remove.MinHeight = 32; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var color = Ui.Button("●", () => controller.ColorMenu(option, optionManager)); color.Tag = "OptionColor"; color.DataContext = option; color.ToolTip = "更改“" + option.Name + "”的颜色"; color.Foreground = Ui.Brush(option.Color); color.Width = color.Height = 28; color.Padding = new Thickness(0); Ui.ApplyCircularStyle(color); DockPanel.SetDock(color, Dock.Right); row.Children.Add(color);
            var name = Ui.Input(option.Name); name.LostKeyboardFocus += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { name.Text = option.Name; return; } option.Name = name.Text.Trim(); controller.OptionsChanged(); }; row.Children.Add(name); optionManager.Children.Add(row);
        }
        optionManager.Children.Add(Ui.Button("＋ 新增选项", () => { var values = ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions; values.Add(new FieldOption { Name = "新选项" }); controller.OptionsChanged(); ShowOptionManager(ai); }));
    }
    private readonly ComboBox laneSelector;
    internal EditorSurface CreateSnapshot()
    {
        var width = double.IsNaN(Width) ? ActualWidth : Width;
        var height = double.IsNaN(Height) ? ActualHeight : Height;
        var copy = new EditorSurface(controller, Copies.Of(Session), snapshotMode: true) { Width = width, Height = height };
        QuickScroll.SetVerticalBarWidth(copy, QuickScroll.GetVerticalBarWidth(this));
        QuickScroll.SetThumbMargin(copy, QuickScroll.GetThumbMargin(this));
        copy.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, GetValue(System.Windows.Documents.TextElement.FontFamilyProperty));
        copy.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, GetValue(System.Windows.Documents.TextElement.ForegroundProperty));
        copy.SetDraftPresentation(closeButton.Visibility == Visibility.Visible, DraftNumber); copy.status.Text = status.Text;
        foreach (var pair in StagePickers)
            if (copy.StagePickers.TryGetValue(pair.Key, out var counterpart)) counterpart.Pane.Visibility = pair.Value.Pane.Visibility;
        copy.Measure(new Size(width, height)); copy.Arrange(new Rect(0, 0, width, height)); copy.UpdateLayout();
        foreach (var pair in SnapshotChildren<TextBox>(this).Zip(SnapshotChildren<TextBox>(copy))) pair.Second.Text = pair.First.Text;
        foreach (var pair in SnapshotChildren<ScrollViewer>(this).Zip(SnapshotChildren<ScrollViewer>(copy)))
        { pair.Second.ScrollToVerticalOffset(pair.First.VerticalOffset); pair.Second.ScrollToHorizontalOffset(pair.First.HorizontalOffset); }
        copy.UpdateLayout();
        foreach (var pair in SnapshotChildren<StageRunIcon>(this).Zip(SnapshotChildren<StageRunIcon>(copy))) pair.Second.FreezeAt(pair.First.Angle);
        foreach (var pair in SnapshotChildren<TextBox>(this).Zip(SnapshotChildren<TextBox>(copy)))
            if (pair.First.Template.FindName("frame", pair.First) is Border sourceFrame && pair.Second.Template.FindName("frame", pair.Second) is Border copyFrame)
                copyFrame.BorderBrush = sourceFrame.BorderBrush;
        foreach (var pair in stages.Children.OfType<Border>().Zip(copy.stages.Children.OfType<Border>())) pair.Second.BorderBrush = pair.First.BorderBrush;
        foreach (var pair in SnapshotChildren<Button>(this).Zip(SnapshotChildren<Button>(copy)))
        {
            if (!Equals(Convert.ToString(pair.First.Content), Convert.ToString(pair.Second.Content))) continue;
            var sourceFrame = pair.First.Template.FindName("border", pair.First) as Border ?? pair.First.Template.FindName("frame", pair.First) as Border;
            var copyFrame = pair.Second.Template.FindName("border", pair.Second) as Border ?? pair.Second.Template.FindName("frame", pair.Second) as Border;
            if (sourceFrame is not null && copyFrame is not null) { copyFrame.BorderBrush = sourceFrame.BorderBrush; copyFrame.Background = sourceFrame.Background; }
        }
        return copy;
    }
    private static System.Collections.Generic.IEnumerable<T> SnapshotChildren<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in SnapshotChildren<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    public void RefreshColumns() { var selected = Session.StatusSelected ? BoardColumns.For(controller.Engine.State.Preferences,Session.Working) : null; laneSelector.ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(); laneSelector.SelectedItem = selected; }
    private readonly StackPanel optionManager = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) };
}

internal sealed class TaskEditorWindow : SurfaceWindow
{
    protected override bool ScrollEnabled => true;
    public EditorSurface View { get; }
    public TaskEditorWindow(EditorSurface view) : base(430, EditorSurface.DefaultHeight)
    { View = view; WindowLayer = 1; Title = "编辑任务"; Root.Children.Add(view); view.Host = this; view.RefreshArchiveCaption(); Closed += (_, _) => view.Destroy(); }
    public override void ApplyTheme() { base.ApplyTheme(); View.ApplyTheme(); }
}
