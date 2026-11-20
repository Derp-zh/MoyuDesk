using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class StageConversationPicker : IDisposable
{
    private readonly BoardController controller;
    private readonly Stage stage;
    private readonly Action changed;
    private readonly bool snapshot;
    private readonly ListBox results = new() { Background = Ui.Surface, Foreground = Ui.Text, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock note = Ui.Label("", 11, Ui.Muted);
    private readonly TextBlock empty = Ui.Label("没有匹配的会话名称", 12, Ui.Muted);
    private readonly TextBlock heading = Ui.Label("", 12, Ui.Muted);
    private readonly Button close;
    private readonly Button unlink;
    private readonly System.Windows.Media.Brush unlinkDefaultBackground;
    private readonly System.Windows.Media.Brush unlinkDefaultBorder;
    private readonly System.Windows.Media.Brush unlinkDefaultForeground;
    private bool unlinkHovered;
    private readonly Dictionary<AiPlatform, Button> platformButtons = new();
    private readonly Grid sidebar = new() { Margin = new Thickness(8, 0, 0, 0), Tag = "StageAiPlatformSidebar" };
    private readonly List<System.Windows.Shapes.Rectangle> platformDividers = new();
    private readonly HashSet<AiPlatform> availablePlatforms = new();
    private readonly DispatcherTimer availabilityTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    internal AiPlatform[] AvailablePlatforms => platformButtons.Keys.Where(availablePlatforms.Contains).ToArray();
    private bool HasPlatforms => availablePlatforms.Count > 0;
    internal AiPlatform SelectedPlatform { get; private set; }
    private AiPlatform? hoverPlatform;
    private long hoverStarted;
    internal CodexThread[] CatalogSnapshot => catalog;
    private readonly ConversationCapsuleGesture? gesture;
    private CodexThread[] catalog = [];
    private bool disposed, loading, dragMode;
    private long loadGeneration;
    internal bool HasCatalog => catalog.Length > 0;
    internal EditorSurface? Editor { get; }
    internal bool Disposed => disposed;
    internal bool WindowAvailable => !disposed && Editor is not null && controller.Editors.Contains(Editor)
        && Editor.Host?.ShouldRestore == true && Editor.Session.Working.Stages.Contains(stage)
        && (Editor.Session.TaskId is not null || controller.ActiveDraft == Editor.Session.Id);
    internal Grid Pane { get; } = new() { Margin = new Thickness(10) };
    internal TextBox Input { get; }
    internal Button Trigger { get; }
    internal FrameworkElement? StageScope { get; set; }
    internal TextBlock Placeholder { get; } = new() { Foreground = Ui.Muted, Margin = new Thickness(10, 12, 9, 8), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
    internal CodexThread[] Matches => HasPlatforms ? catalog.Where(x => x.Platform == SelectedPlatform && x.DisplayName.Contains(Input.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray() : [];
    internal StageConversationPicker(BoardController controller, Stage stage, Action changed, bool snapshot, EditorSurface? editor = null)
    {
        this.controller = controller; this.stage = stage; this.changed = changed; this.snapshot = snapshot; Editor = editor;
        Pane.Tag = "StageBinding-" + stage.Id;
        Pane.ColumnDefinitions.Add(new ColumnDefinition());
        Pane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Pane.RowDefinitions.Add(new RowDefinition());
        Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Trigger = Ui.Button("", () => { if (!snapshot && !disposed) controller.BindingWindow.Toggle(this); });
        Trigger.Tag = "StageCodex";
        if (editor is not null && !snapshot)
            gesture = new ConversationCapsuleGesture(Trigger, controller,
                () => new ConversationBindingDrag(controller, editor, stage),
                () => controller.BindingWindow.Toggle(this));
        Input = Ui.Input(); Input.Tag = "StageConversationSearch";
        RoundedInput.SetEdgeArrowNavigation(Input, false);
        Input.Padding = new Thickness(9); Input.Margin = new Thickness(0, 0, 0, 6);
        var header = new DockPanel(); close = Ui.Button("×", () => Collapse()); close.Width = 25; close.Height = 25; close.Padding = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(heading); Pane.Children.Add(header);
        var plate = new FrameworkElementFactory(typeof(Border));
        plate.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        plate.SetValue(Border.BorderBrushProperty, Ui.Line); plate.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        var caption = new FrameworkElementFactory(typeof(ContentPresenter));
        caption.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        caption.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        caption.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty)); plate.AppendChild(caption);
        var platformStyle = new Style(typeof(Button));
        platformStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = plate }));
        foreach (var value in new[] { AiPlatform.Codex, AiPlatform.Dsh, AiPlatform.ClaudeCode })
        {
            var button = Ui.Button(value == AiPlatform.ClaudeCode ? "Claude" : AiState.Name(value), () => SwitchPlatform(value));
            button.Tag = "StageAiPlatform-" + value; button.FontSize = 10; button.Margin = new Thickness(0); button.Padding = new Thickness(0);
            button.Style = platformStyle; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
            sidebar.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(button, sidebar.RowDefinitions.Count - 1);
            button.ToolTip = AiState.Name(value) + " · 单击切换；拖动时悬停切换";
            System.Windows.Automation.AutomationProperties.SetName(button, AiState.Name(value));
            platformButtons[value] = button; sidebar.Children.Add(button);
        }
        for (var divider = 0; divider < 2; divider++)
        {
            var line = new System.Windows.Shapes.Rectangle { Height = 1, Fill = Ui.Brush("#8094A8C2"),
                VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, SnapsToDevicePixels = true };
            Grid.SetRow(line, divider); sidebar.Children.Add(line); platformDividers.Add(line);
        }
        Grid.SetColumn(sidebar, 1); Grid.SetRowSpan(sidebar, 5); Pane.Children.Add(sidebar);
        SelectedPlatform = stage.Codex?.Platform ?? AiPlatform.Codex; PresentPlatforms();
        var field = new Grid(); field.Children.Add(Input); field.Children.Add(Placeholder); Grid.SetRow(field, 1); Pane.Children.Add(field);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(CodexThread.DisplayName))); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); row.AppendChild(name);
        var number = new FrameworkElementFactory(typeof(TextBlock)); number.SetBinding(TextBlock.TextProperty, new Binding(nameof(CodexThread.Id))); number.SetValue(TextBlock.FontSizeProperty, 10.0); number.SetValue(TextBlock.ForegroundProperty, Ui.Muted); row.AppendChild(number);
        results.ItemTemplate = new DataTemplate { VisualTree = row }; VirtualizingPanel.SetIsVirtualizing(results, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(results, ScrollBarVisibility.Auto);
        var list = new Grid(); list.Children.Add(results); list.Children.Add(empty); Grid.SetRow(list, 2); Pane.Children.Add(list);
        unlink = Ui.Button("解除绑定", () => { if (!CanSelectFromWindow) return; stage.Codex = null; Present(); changed(); Collapse(); });
        unlink.Tag = "StageConversationUnlink"; unlink.HorizontalAlignment = HorizontalAlignment.Left; unlink.Padding = new Thickness(12, 3, 12, 3);
        unlinkDefaultBackground = unlink.Background; unlinkDefaultBorder = unlink.BorderBrush; unlinkDefaultForeground = unlink.Foreground;
        Grid.SetRow(unlink, 3); Pane.Children.Add(unlink);
        Grid.SetRow(note, 4); Pane.Children.Add(note);
        Input.TextChanged += (_, _) => { Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Collapse(); e.Handled = true; }
            else if (CanSelectFromWindow && e.Key == Key.Down && results.Items.Count > 0) { results.SelectedIndex = 0; results.Focus(); e.Handled = true; }
            else if (CanSelectFromWindow && e.Key == Key.Enter && Matches.FirstOrDefault() is { } first) { Select(first); e.Handled = true; }
            else RoundedInput.HandleEdgeArrow(Input, e);
        };
        results.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragMode && ReferenceEquals(controller.BindingWindow.Current, this) && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(results, source) is ListBoxItem { DataContext: CodexThread selected }) Select(selected);
        };
        results.PreviewKeyDown += (_, e) =>
        {
            if (CanSelectFromWindow && e.Key == Key.Enter && results.SelectedItem is CodexThread selected) { Select(selected); e.Handled = true; }
            else if (e.Key == Key.Escape) { Collapse(); e.Handled = true; }
        };
        availabilityTimer.Tick += (_, _) => RefreshAvailability();
        RefreshAvailability(false); Present();
    }
    private void Present()
    {
        var name = stage.Codex is null ? "绑定会话" : string.IsNullOrWhiteSpace(stage.Codex.ThreadName) ? "未命名会话" : stage.Codex.ThreadName;
        Trigger.Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        Trigger.ToolTip = name + " · " + AiState.Name(stage.Codex?.Platform ?? AiPlatform.Codex) + " · 单击选择会话，或拖到绑定窗口的准确会话行；保存任务后生效";
        PresentPlatforms();
        heading.Text = "绑定会话 · " + stage.Name;
        heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
    }
    internal bool HasBinding => stage.Codex is not null;
    internal Button UnlinkButton => unlink;
    internal bool IsOverUnlink(Point screen)
    {
        try
        {
            if (!unlink.IsVisible || unlink.ActualWidth <= 0 || unlink.ActualHeight <= 0) return false;
            return new Rect(unlink.PointToScreen(new Point()), unlink.PointToScreen(new Point(unlink.ActualWidth, unlink.ActualHeight))).Contains(screen);
        }
        catch (InvalidOperationException) { return false; }
    }
    internal void SetUnlinkHover(bool active)
    {
        if (unlinkHovered == active) return;
        unlinkHovered = active;
        if (active)
        {
            unlink.Background = Ui.Brush("#595184B9");
            unlink.BorderBrush = Ui.Accent;
            unlink.Foreground = Ui.Accent;
        }
        else
        {
            unlink.Background = unlinkDefaultBackground;
            unlink.BorderBrush = unlinkDefaultBorder;
            unlink.Foreground = unlinkDefaultForeground;
        }
    }
    internal void PrepareWindow(bool drag, Action dismiss)
    {
        RefreshAvailability(false); availabilityTimer.Start();
        loadGeneration++; loading = false; dragMode = drag; SetUnlinkHover(false); Input.Text = ""; results.SelectedIndex = -1; Present(); Filter();
    }
    internal void SetDragMode(bool value) { dragMode = value; ResetPlatformHover(); if (!value) SetUnlinkHover(false); }
    internal void ResetPlatformHover() => hoverPlatform = null;
    private void PresentPlatforms()
    {
        Placeholder.Text = HasPlatforms ? "搜索 " + AiState.Name(SelectedPlatform) + " 会话" : "请先初始化 AI 平台";
        foreach (var (value, button) in platformButtons)
        {
            button.Background = Ui.Brush(value == SelectedPlatform ? "#595184B9" : "#202C3D53");
            button.Foreground = value == SelectedPlatform ? Ui.Accent : Ui.Text;
            button.FontWeight = value == SelectedPlatform ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
    internal void RefreshAvailability(bool load = true)
    {
        if (disposed) return;
        var before = AvailablePlatforms;
        foreach (var value in platformButtons.Keys)
        {
            var available = controller.ConversationPlatformAvailable(value);
            if (available == true) availablePlatforms.Add(value);
            else if (available == false) availablePlatforms.Remove(value);
        }
        var values = AvailablePlatforms;
        var row = 0;
        foreach (var (value, button) in platformButtons)
        {
            button.Visibility = availablePlatforms.Contains(value) ? Visibility.Visible : Visibility.Collapsed;
            if (button.Visibility == Visibility.Visible) Grid.SetRow(button, row++);
        }
        for (var i = 0; i < sidebar.RowDefinitions.Count; i++)
            sidebar.RowDefinitions[i].Height = i < row ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        for (var i = 0; i < platformDividers.Count; i++)
            platformDividers[i].Visibility = i < row - 1 ? Visibility.Visible : Visibility.Collapsed;
        sidebar.Visibility = HasPlatforms ? Visibility.Visible : Visibility.Collapsed;
        Pane.ColumnDefinitions[1].Width = new GridLength(HasPlatforms ? 48 : 0);
        Input.IsEnabled = HasPlatforms;
        if (!before.SequenceEqual(values))
        {
            note.Text = "";
            ResetPlatformHover();
            if (!availablePlatforms.Contains(SelectedPlatform))
            {
                loadGeneration++; loading = false; catalog = []; Input.Text = ""; results.SelectedIndex = -1;
                if (HasPlatforms) SelectedPlatform = values[0];
                if (load && HasPlatforms && IsDropdownOpen) LoadForWindow();
            }
            else if (before.Length == 0 && load && IsDropdownOpen) LoadForWindow();
        }
        PresentPlatforms(); Filter();
    }
    internal void SwitchPlatform(AiPlatform value, bool load = true)
    {
        if (disposed || !availablePlatforms.Contains(value) || SelectedPlatform == value) return;
        loadGeneration++; loading = false; SelectedPlatform = value; catalog = []; Input.Text = "";
        results.SelectedIndex = -1; note.Text = ""; PresentPlatforms(); Filter();
        if (load && !snapshot && !controller.Smoke) LoadForWindow();
    }
    internal void RestorePlatform(AiPlatform value, CodexThread[] values)
    {
        RefreshAvailability(false);
        loadGeneration++; loading = false;
        if (availablePlatforms.Contains(value)) { SelectedPlatform = value; catalog = values; }
        else catalog = [];
        note.Text = "";
        ResetPlatformHover(); PresentPlatforms(); Filter();
    }
    internal AiPlatform? PlatformAt(Point screen)
    {
        foreach (var (value, button) in platformButtons)
            if (button.IsVisible && button.ActualWidth > 0 && new Rect(button.PointToScreen(new Point()),
                button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight))).Contains(screen)) return value;
        return null;
    }
    internal void HoverPlatform(Point screen, long now)
    {
        var candidate = PlatformAt(screen);
        if (!dragMode || candidate is null || candidate == SelectedPlatform) { ResetPlatformHover(); return; }
        if (hoverPlatform != candidate) { hoverPlatform = candidate; hoverStarted = now; return; }
        if (now - hoverStarted >= 180) { SwitchPlatform(candidate.Value); ResetPlatformHover(); }
    }
    internal string DropHint => !HasPlatforms ? "暂无已接入的本地 AI，请到设置的 AI 平台接入情况初始化"
        : loading ? "正在加载 " + AiState.Name(SelectedPlatform) + " 会话，请稍候…"
        : Matches.Length == 0 ? "暂无 " + AiState.Name(SelectedPlatform) + " 匹配会话，可悬停右侧切换平台"
        : "移到具体会话行松开绑定；右侧悬停切换平台";
    internal void WindowDetached() { availabilityTimer.Stop(); loadGeneration++; loading = false; ResetPlatformHover(); SetUnlinkHover(false); Input.Text = ""; results.SelectedIndex = -1; }
    internal async void LoadForWindow() { if (!snapshot && !controller.Smoke) await Load(); }
    internal async Task Load(Func<Task<CodexThread[]>>? readForCheck = null)
    {
        if (loading || disposed || !HasPlatforms) return; loading = true; var generation = loadGeneration;
        note.Text = "正在加载本地会话…"; Filter();
        try
        {
            var values = await (readForCheck?.Invoke() ?? controller.LoadPlatformConversations(SelectedPlatform)); if (disposed || generation != loadGeneration) return;
            catalog = values.Where(x => x.Platform == SelectedPlatform).ToArray();
            if (stage.Codex is { } binding && binding.Platform == SelectedPlatform && catalog.FirstOrDefault(x => x.Id == binding.ThreadId) is { } known && known.Name.Length > 0 && known.Name != binding.ThreadName)
            { binding.ThreadName = known.Name; Present(); changed(); }
            note.Text = catalog.Length == 0 ? "暂无可选会话，可切换平台或重新打开重试。" : "";
        }
        catch { if (!disposed && generation == loadGeneration) note.Text = "会话目录暂不可用，请重新打开重试。"; }
        finally { if (!disposed && generation == loadGeneration) { loading = false; Filter(); } }
    }
    internal void SetCatalog(CodexThread[] values) { catalog = values; Filter(); }
    private bool CanSelectFromWindow => !disposed && !dragMode && ReferenceEquals(controller.BindingWindow.Current, this)
        && (Editor is null || WindowAvailable);
    private void Filter()
    {
        var values = Matches; results.ItemsSource = values;
        empty.Text = HasPlatforms ? "暂无 " + AiState.Name(SelectedPlatform) + " 匹配会话" : "暂无已接入的本地 AI\n请到设置 → AI平台接入情况\n完成初始化后再绑定会话";
        if (!HasPlatforms) note.Text = "已有绑定保持不变。";
        empty.Visibility = !loading && values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void Select(CodexThread thread)
    {
        if (disposed || dragMode || !availablePlatforms.Contains(thread.Platform) || thread.Platform != SelectedPlatform || Editor is not null && !Editor.Session.Working.Stages.Contains(stage)) return;
        if (stage.Codex?.ThreadId != thread.Id || stage.Codex?.Platform != thread.Platform) stage.Codex = new CodexBinding { Platform = thread.Platform, ThreadId = thread.Id, ThreadName = thread.Name };
        else stage.Codex.ThreadName = thread.Name;
        Present(); changed(); Collapse();
        Ui.Hint(Trigger, "已选择 · " + thread.DisplayName + "，保存任务后生效", !controller.Smoke, Editor?.Root);
    }
    internal void RefreshBinding() => Present();
    internal void Close() => Collapse();
    internal void Collapse() { gesture?.Cancel(); controller.BindingWindow.Close(this); }
    internal bool IsDropdownOpen => ReferenceEquals(controller.BindingWindow.Current, this);
    internal ListBox Results => results;
    internal void CollapseUnlessContains(DependencyObject? source)
    {
        for (var node = source; node is not null;)
        {
            if (ReferenceEquals(node, Trigger) || ReferenceEquals(node, Pane)) return;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        controller.BindingWindow.Close(this);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; availabilityTimer.Stop(); gesture?.Dispose(); controller.BindingWindow.Close(this);
    }
}
