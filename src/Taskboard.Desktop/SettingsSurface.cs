using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface : SurfaceWindow
{
    protected override bool ScrollEnabled => true;
    private readonly BoardController controller;
    private CheckBox? ballEdgeHideCheck;
    internal void RefreshBallEdgeAutoHide() { if (ballEdgeHideCheck is not null) ballEdgeHideCheck.IsChecked = controller.Engine.State.Preferences.BallEdgeAutoHide; }
    private readonly Grid content = new();
    private readonly Dictionary<string, ScrollViewer> pages = new();
    private readonly Dictionary<string, Button> navigation = new();
    internal string CurrentSection { get; private set; } = "主设置";
    internal static readonly string[] Sections = ["主设置", "窗口", "监听", "归档", "数据与备份", "AI平台接入情况"];
    internal static string SectionCaption(string section) => section switch
    {
        "主设置" => "常规", "窗口" => "窗口与列表", "监听" => "通知监听", "AI平台接入情况" => "AI平台监听", _ => section
    };
    public SettingsSurface(BoardController controller) : base(760, 680)
    {
        this.controller = controller; Title = Brand.Name + "设置";
        System.Windows.Input.InputManager.Current.PostProcessInput += DismissHelpOnOutsideInput;
        Closed += (_, _) => System.Windows.Input.InputManager.Current.PostProcessInput -= DismissHelpOnOutsideInput;
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) }); Root.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Margin = new Thickness(22, 12, 20, 10), Background = Brushes.Transparent };
        var close = Ui.Button("×", Hide); close.Tag = "SettingsClose"; close.ToolTip = "关闭设置"; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        close.Width = 48; close.Height = 40; close.Padding = new Thickness(0); close.VerticalAlignment = VerticalAlignment.Center;
        close.Background = Ui.Brush("#C04B5B70"); close.BorderBrush = Brushes.White; close.Foreground = Brushes.White;
        close.Style = (Style)Application.Current.FindResource("EditorCloseButton");
        saveStatus.VerticalAlignment = VerticalAlignment.Center; saveStatus.Tag = "SettingsSaveStatus";
        DockPanel.SetDock(saveStatus, Dock.Right); header.Children.Insert(1, saveStatus);
        RefreshSaveStatus(controller.SettingsSaveStatus);
        var heading = Ui.Label("设置", 21); heading.FontWeight = FontWeights.SemiBold; heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading);
        Root.Children.Add(header); MoveWith(header);
        var body = new Grid { Margin = new Thickness(16, 0, 18, 18) }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        sidebar.Children.Add(Ui.Label("偏好设置", 11, Ui.Muted));
        foreach (var section in Sections)
        {
            if (section == "AI平台接入情况") { var group = Ui.Label("平台连接", 11, Ui.Muted); group.Margin = new Thickness(0, 20, 0, 4); sidebar.Children.Add(group); }
            var button = Ui.Button(SectionCaption(section), () => SelectSection(section));
            button.Tag = "SettingsNavigation"; button.DataContext = section;
            button.FontSize = 13; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 3, 0, 3); button.Padding = new Thickness(12, 10, 8, 10);
            navigation.Add(section, button); sidebar.Children.Add(button);
        }
        body.Children.Add(sidebar); Grid.SetColumn(content, 1); body.Children.Add(content); Grid.SetRow(body, 1); Root.Children.Add(body); SelectSection(CurrentSection);
        BeforeOpen += () => { if (controller.IsRestoringWorkspace) return; archiveAllLoaded = false; pages.Remove("归档"); if (CurrentSection == "归档") SelectSection("归档"); };
        BeforeHide += () => { helpImagePreview?.Destroy(false); helpImagePreview = null; if (System.Windows.Input.Keyboard.FocusedElement is TextBox box && Root.IsAncestorOf(box)) System.Windows.Input.Keyboard.ClearFocus(); };
    }
    private void DismissHelpOnOutsideInput(object sender, System.Windows.Input.ProcessInputEventArgs e)
    {
        if (helpImagePreview is not { IsVisible: true } viewer ||
            e.StagingItem.Input is not System.Windows.Input.MouseButtonEventArgs { ButtonState: System.Windows.Input.MouseButtonState.Pressed } mouse ||
            mouse.OriginalSource is not DependencyObject source) return;
        if (!ReferenceEquals(source, viewer.Root) && !viewer.Root.IsAncestorOf(source)) viewer.Destroy(false);
    }
    private readonly TextBlock saveStatus = Ui.Label("更改会自动保存", 11, Ui.Muted);
    internal void RefreshSaveStatus(string value)
    { saveStatus.Text = value; saveStatus.Foreground = value.StartsWith("保存失败", StringComparison.Ordinal) ? Ui.Brush("#F28F98") : Ui.Muted; }
    internal void RefreshSection() { pages.Clear(); SelectSection(CurrentSection); }
    internal void SelectSection(string name)
    {
        if (!navigation.ContainsKey(name)) return;
        if (name == "归档" && CurrentSection != "归档") { archiveAllLoaded = false; pages.Remove("归档"); }
        CurrentSection = name;
        foreach (var (section, button) in navigation)
        {
            button.Background = section == name ? Ui.Brush("#36574F") : Brushes.Transparent;
            button.Foreground = section == name ? Ui.Accent : Ui.Text;
            button.BorderBrush = section == name ? Ui.Brush("#668B80") : Brushes.Transparent;
            button.FontWeight = section == name ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (!pages.TryGetValue(name, out var page))
        {
            var panel = name switch { "主设置" => MainSettings(), "窗口" => WindowSettings(), "监听" => ListeningSettings(), "归档" => ArchiveSettings(), "数据与备份" => DataSettings(), _ => PlatformSettings() };
            OrganizeSettingsPage(name, panel);
            AttachHelp(panel);
            page = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 0, 4, 10), Tag = "SettingsPage", DataContext = name }; pages.Add(name, page);
        }
        content.Children.Clear(); content.Children.Add(page);
    }
    private StackPanel MainSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("主设置", 18));
        var tutorial = Ui.Button("重看新手教程", controller.ShowOnboarding); tutorial.Tag = "ShowOnboarding";
        Button? resetTutorial = null;
        resetTutorial = Ui.Button("重置新手指引", () =>
        {
            var result = controller.ResetOnboarding(showImmediately: true);
            if (resetTutorial is not null) Ui.Hint(resetTutorial, result.Message, !controller.Smoke, Root);
        });
        resetTutorial.Tag = "ResetOnboarding";
        resetTutorial.ToolTip = "仅清除本机首次指引标记以便反复验收，不会修改任何任务或设置";
        var tutorialRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        tutorialRow.Children.Add(tutorial); tutorialRow.Children.Add(resetTutorial);
        panel.Children.Add(tutorialRow);
        panel.Children.Add(Ui.Label("“重置新手指引”仅恢复首次引导状态并立即弹出教程，不影响任何任务、草稿或偏好设置。", 12, Ui.Muted));
        var quickEntry = new CheckBox { Content = "灵感库使用快捷新建卡片", Tag = "InspirationQuickEntry", IsChecked = controller.Engine.State.Preferences.InspirationQuickEntry, Margin = new Thickness(0, 8, 0, 12) };
        quickEntry.Click += (_, _) =>
        {
            controller.Engine.State.Preferences.InspirationQuickEntry = quickEntry.IsChecked == true;
            foreach (var column in controller.Columns.Where(x => x.Lane == Lane.Inspiration)) column.RefreshEntryMode();
            controller.Engine.Touch();
        };
        panel.Children.Add(quickEntry);
        var edgeHide = ballEdgeHideCheck = new CheckBox { Content = "悬浮球吸附到桌面边缘并自动隐藏", Tag = "BallEdgeAutoHide", IsChecked = controller.Engine.State.Preferences.BallEdgeAutoHide, Margin = new Thickness(0, 8, 0, 4) };
        edgeHide.Click += (_, _) =>
        {
            controller.SetBallEdgeAutoHide(edgeHide.IsChecked == true);
        };
        panel.Children.Add(edgeHide);
        panel.Children.Add(Ui.Label("悬浮球靠最近的左右边缘停放；鼠标离开后收起，移上恢复完整球。", 12, Ui.Muted));
        var ellipsize = new CheckBox { Content = "省略多列表阶段详情的超出文本", Tag = "EllipsizeStageDetails",
            IsChecked = controller.Engine.State.Preferences.EllipsizeStageDetails, Margin = new Thickness(0, 8, 0, 8),
            ToolTip = "关闭后完整显示阶段详情，卡片随内容增高；不改变编辑窗口的阶段折叠。" };
        ellipsize.Click += (_, _) =>
        {
            controller.Engine.State.Preferences.EllipsizeStageDetails = ellipsize.IsChecked == true;
            controller.Engine.Notify(controller.Engine.State.Tasks.Select(x => x.Id).ToArray());
        };
        panel.Children.Add(ellipsize);
        var collapse = new CheckBox { Content = "未运行阶段的详情只显示一行", Tag = "CollapseInactiveStageDetails",
            IsChecked = controller.Engine.State.Preferences.CollapseInactiveStageDetails, Margin = new Thickness(0, 8, 0, 8),
            ToolTip = "开启时未运行阶段至少保留一行，点击详情可完整编辑；关闭后至少显示两行。" };
        collapse.Click += (_, _) =>
        {
            controller.Engine.State.Preferences.CollapseInactiveStageDetails = collapse.IsChecked == true;
            foreach (var editor in controller.Editors) editor.RefreshStageDetailsPolicy();
            controller.Engine.Touch();
        };
        panel.Children.Add(collapse);
        panel.Children.Add(NewStageDefaultStateSettings());
        panel.Children.Add(HotkeySettings());
        var startup = new CheckBox { Content = "开机启动", Tag = "SettingsAutoStart", IsChecked = controller.Engine.State.Preferences.AutoStart, Margin = new Thickness(0, 10, 0, 14), IsEnabled = !controller.Isolated };
        startup.Click += (_, _) =>
        {
            try { StartupLinks.SetAutoStart(startup.IsChecked == true); controller.Engine.State.Preferences.AutoStart = startup.IsChecked == true; controller.Engine.Touch(); }
            catch (Exception e) { Ui.Error("设置开机启动失败：" + e.Message); startup.IsChecked = controller.Engine.State.Preferences.AutoStart; }
        };
        panel.Children.Add(startup);
        var shortcut = Ui.Button("创建桌面快捷方式", () => Try(StartupLinks.Shortcut)); shortcut.IsEnabled = !controller.Isolated; panel.Children.Add(shortcut);
        var repair = Ui.Button("修复开机启动路径", () => Try(() => StartupLinks.SetAutoStart(controller.Engine.State.Preferences.AutoStart))); repair.IsEnabled = !controller.Isolated; panel.Children.Add(repair);
        if (!controller.Isolated && StartupLinks.PathChanged()) panel.Children.Add(Ui.Label("文件夹位置已变化，可重新创建快捷方式并修复启动路径。", 12, Ui.Muted));
        if (!controller.Isolated)
        {
            panel.Children.Add(Ui.Label(StartupLinks.AutoStartStatus(), 12, Ui.Muted));
            panel.Children.Add(Ui.Button("Windows启动应用设置", () => Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }))));
        }
        panel.Children.Add(Ui.Label(Brand.Name + " " + typeof(Program).Assembly.GetName().Version!.ToString(3), 11, Ui.Muted)); return panel;
    }
    private StackPanel NewStageDefaultStateSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4), Tag = "NewStageDefaultStateSection" };
        var label = Ui.Label("新建阶段默认状态", 14); label.Margin = new Thickness(0, 0, 0, 4); panel.Children.Add(label);
        var current = controller.Engine.State.Preferences.NewStageDefaultState;
        foreach (var (option, caption) in new[]
        {
            (NewStageDefaultState.ContextDependent, "默认：灵感库/完善中任务编辑及所有新建任务为“未运行”，其余（如进行中编辑）为“运行中”"),
            (NewStageDefaultState.AlwaysRunning, "全部初始默认“运行中”"),
            (NewStageDefaultState.AlwaysInactive, "全部初始默认“未运行”")
        })
        {
            var radio = new RadioButton
            {
                GroupName = "NewStageDefaultState",
                Tag = "NewStageDefaultState",
                DataContext = option,
                IsChecked = current == option,
                Margin = new Thickness(0, 4, 0, 4),
                Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap }
            };
            radio.Click += (_, _) =>
            {
                if (radio.IsChecked != true) return;
                controller.Engine.State.Preferences.NewStageDefaultState = option;
                controller.Engine.Touch();
            };
            panel.Children.Add(radio);
        }
        return panel;
    }
    private StackPanel WindowSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("窗口", 18));
        panel.Children.Add(FocusViewSettings());
        panel.Children.Add(ListSettings());
        var caption = Ui.Label($"多列表窗口透明度 {controller.Engine.State.Preferences.GlassTransparency:0}%", 14); panel.Children.Add(caption);
        panel.Children.Add(Ui.Label("数值越大越透明；0%为暗色遮罩，100%仍保留背景模糊。默认20%。", 12, Ui.Muted));
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.GlassTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        slider.ValueChanged += (_, _) => { caption.Text = $"多列表窗口透明度 {slider.Value:0}%"; controller.SetGlassTransparency(slider.Value); }; panel.Children.Add(slider);
        var floatingCaption = Ui.Label($"悬浮窗口透明度 {controller.Engine.State.Preferences.FloatingTransparency:0}%", 14); panel.Children.Add(floatingCaption);
        panel.Children.Add(Ui.Label("新建、编辑任务、设置等悬浮窗口独立调节，默认10%；数值越大越透明，背景模糊始终保留。", 12, Ui.Muted));
        var floatingSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.FloatingTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        floatingSlider.ValueChanged += (_, _) => { floatingCaption.Text = $"悬浮窗口透明度 {floatingSlider.Value:0}%"; controller.SetFloatingTransparency(floatingSlider.Value); }; panel.Children.Add(floatingSlider);
        var ballCaption = Ui.Label($"悬浮球透明度 {controller.Engine.State.Preferences.BallTransparency:0}%", 14); panel.Children.Add(ballCaption);
        var ballSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.BallTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        ballSlider.ValueChanged += (_, _) => { ballCaption.Text = $"悬浮球透明度 {ballSlider.Value:0}%"; controller.SetBallTransparency(ballSlider.Value); }; panel.Children.Add(ballSlider);
        var resize = new CheckBox { Content = "显示各列右下角独立尺寸入口", Tag = "SettingsResize", IsChecked = controller.Engine.State.Preferences.ResizeCorner, Margin = new Thickness(0, 8, 0, 14) };
        resize.Click += (_, _) => { controller.Engine.State.Preferences.ResizeCorner = resize.IsChecked == true; controller.ApplySettings(); controller.Engine.Touch(); }; panel.Children.Add(resize);
        panel.Children.Add(Ui.Button("恢复窗口默认", controller.RestoreDefaults)); panel.Children.Add(Ui.Label("恢复整组的默认大小和位置。列表右键可只恢复该窗口，并保留当前最大范围的缩放。", 12, Ui.Muted)); return panel;
    }
    private StackPanel ListSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("列表显示与名称",15));
        var countRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Ui.Input(controller.ConfiguredColumns.Length.ToString()); number.Width = 80; number.ToolTip = "显示的窗口数量（隐藏不计入）";
        var countCaption = Ui.Label("常规模式窗口数", 12); countCaption.VerticalAlignment = VerticalAlignment.Center; countRow.Children.Add(countCaption);
        number.Margin = new Thickness(8, 0, 8, 0);
        var note = Ui.Label("",11,Ui.Accent);
        void ApplyCount(int count)
        {
            var limit = Math.Max(1,(int)(controller.WorkArea(this).Width*7/8/80));
            if (count<0 || count>limit) { note.Text = $"当前屏幕可并排显示0至{limit}个列表（每列至少80宽）。"; return; }
            BoardColumns.SetVisibleCount(controller.Engine.State.Preferences,count); controller.ColumnsChanged(); RefreshSection();
        }
        var apply = Ui.Button("应用数量",() => { if (int.TryParse(number.Text,out var count)) ApplyCount(count); else note.Text = "请输入非负整数。"; });
        countRow.Children.Add(number); countRow.Children.Add(apply); panel.Children.Add(countRow);
        panel.Children.Add(Ui.Label("隐藏不删除任务；六个内置列表可改名、隐藏，不能删除。",12,Ui.Muted));
        foreach (var column in controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var remove = Ui.Button(column.Protected ? "内置" : "删除",() =>
            { try { BoardColumns.Delete(controller.Engine.State,column); controller.ColumnsChanged(); RefreshSection(); } catch (InvalidOperationException e) { note.Text = e.Message; } });
            remove.Tag = "ListDelete"; remove.DataContext = column; remove.MinWidth = 54;
            remove.IsEnabled = !column.Protected; remove.ToolTip = column.Protected ? "此列表可改名或隐藏，不能删除" : "有任务或非空草稿时不能删除";
            DockPanel.SetDock(remove,Dock.Right); row.Children.Add(remove);
            var visible = new CheckBox { IsChecked = column.Visible, Content = "显示", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
            visible.Click += (_,_) => { column.Visible = visible.IsChecked == true; controller.ColumnsChanged(); RefreshSection(); };
            DockPanel.SetDock(visible,Dock.Right); row.Children.Add(visible);
            var name = Ui.Input(column.Name); name.Tag = "ListName"; name.DataContext = column; name.ToolTip = column.BuiltinLane is Lane role ? "原列表："+Names.Lanes[(int)role] : "自定义列表";
            name.MinHeight = 30; name.Margin = new Thickness(0, 2, 0, 2); name.Padding = new Thickness(9, 4, 9, 4);
            void CommitName() { if (string.IsNullOrWhiteSpace(name.Text)) { name.Text = column.Name; return; } column.Name = name.Text.Trim(); controller.ColumnsChanged(false); }
            name.LostKeyboardFocus += (_,_) => CommitName();
            name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { CommitName(); e.Handled = true; } };
            row.Children.Add(name); panel.Children.Add(row);
        }
        panel.Children.Add(Ui.Button("＋ 新增列表",() =>
        { var limit=Math.Max(1,(int)(controller.WorkArea(this).Width*7/8/80)); if(controller.ConfiguredColumns.Length>=limit) {note.Text="当前屏幕已达到并排显示数量，请先隐藏其他列表。";return;} controller.Engine.State.Preferences.Columns.Add(new BoardColumn()); controller.ColumnsChanged(); RefreshSection(); }));
        panel.Children.Add(note); return panel;
    }
    private StackPanel DataSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("数据与备份", 18));
        var path = Ui.Label(controller.Store.DirectoryPath, 12, Ui.Muted); panel.Children.Add(path); var status = Ui.Label("", 12, Ui.Accent);
        panel.Children.Add(Ui.Button("切换数据路径", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择本地数据目录", InitialDirectory = controller.Store.DirectoryPath }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var target = dialog.FolderName; var populated = File.Exists(Path.Combine(target, "board.json")) || File.Exists(Path.Combine(target, "board.json.bak"));
                var message = populated ? "目标目录已有数据，切换后打开那份数据，不自动合并；当前输入会先保存，原目录保留。是否切换？" : "当前任务、草稿和配置会复制到新目录，原目录保留。是否切换？";
                if (!await Ui.Confirm(message, "切换路径")) return;
                await controller.ChangeDataDirectory(target); path.Text = controller.Store.DirectoryPath; status.Text = "数据路径已切换，原目录保留。";
            }
            catch (Exception e) { Ui.Error("路径未切换：" + e.Message); }
        }));
        panel.Children.Add(Ui.Label("配置备份包含窗口、选项及平台接入配置，不包含任务、草稿和阶段详情。", 12, Ui.Muted));
        panel.Children.Add(Ui.Label("配置保存路径", 14));
        panel.Children.Add(Ui.Label(controller.Store.ConfigurationDirectory ?? "当前任务目录内", 12, Ui.Muted));
        panel.Children.Add(Ui.Button("切换配置路径", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择配置保存目录" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (File.Exists(Path.Combine(dialog.FolderName, "settings.json")) && !await Ui.Confirm("目标已有程序配置，采用这些配置并保留当前任务及归档。是否切换？", "切换配置")) return;
                await controller.ChangeConfigurationDirectory(dialog.FolderName); RefreshSection();
            }
            catch (Exception e) { Ui.Error("配置路径未切换：" + e.Message); }
        }));
        panel.Children.Add(Ui.Button("导出配置备份", async () =>
        {
            if (!await controller.Flush()) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = Brand.Name + "配置|*.json", FileName = Brand.Name + "配置-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" }; if (dialog.ShowDialog(this) != true) return;
            try { ConfigurationBackup.Capture(controller.Engine.State, controller.Store.DirectoryPath).Write(dialog.FileName); status.Text = "配置已导出，不含任务数据。"; }
            catch (Exception e) { Ui.Error("不能导出配置：" + e.Message); }
        }));
        panel.Children.Add(Ui.Button("导入配置备份", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Brand.Name + "配置|*.json" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var configuration = ConfigurationBackup.Read(dialog.FileName);
                if (!await Ui.Confirm("恢复程序配置，保留当前任务和草稿。是否导入？", "导入配置")) return;
                await controller.ImportConfiguration(configuration);
                if (!string.Equals(configuration.DataDirectory, controller.Store.DirectoryPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(configuration.DataDirectory) &&
                    await Ui.Confirm("备份中的数据路径可用，是否也切换到该路径？目标目录的任务不会与当前任务混合。", "采用备份路径")) await controller.ChangeDataDirectory(configuration.DataDirectory);
                RefreshSection(); Ui.Error("配置已导入，当前任务和草稿已保留。数据路径：" + controller.Store.DirectoryPath);
            }
            catch (Exception e) { Ui.Error("不能导入配置：" + e.Message); }
        }));
        panel.Children.Add(status); return panel;
    }
    private StackPanel PlatformSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("AI平台监听", 18));
        {
            var autoBinding = new CheckBox { Content = new TextBlock { Text = "拖动绑定会话时自动展开右侧内置任务列表窗口", TextWrapping = TextWrapping.Wrap }, Tag = "AutoOpenConversationBindingWindow",
                ToolTip = "开始拖动绑定会话时，自动展开任务面板右侧的会话列表，方便选择。关闭后仍可拖到AI客户端绑定。",
                IsChecked = controller.Engine.State.Preferences.AutoOpenConversationBindingWindow, Margin = new Thickness(0, 4, 0, 12) };
            autoBinding.Click += (_, _) => { controller.Engine.State.Preferences.AutoOpenConversationBindingWindow = autoBinding.IsChecked == true; controller.Engine.Touch(); };
            panel.Children.Add(autoBinding);
            var autoCreate = new CheckBox { Content = "会话开始执行时自动新建任务", Tag = "AutoCreateAiTasks",
                IsChecked = controller.Engine.State.Preferences.AutoCreateAiTasks, Margin = new Thickness(0, 0, 0, 6) };
            autoCreate.Click += (_, _) => { controller.Engine.State.Preferences.AutoCreateAiTasks = autoCreate.IsChecked == true;
                controller.Engine.Touch(); controller.RefreshAiCreation(); };
            panel.Children.Add(autoCreate);
            panel.Children.Add(Ui.Label("仅监控已连接的平台。未绑定的会话开始执行后，在进行中新建同名任务；已有会话先建立基线，已绑定任务或草稿不会重复创建。默认关闭。", 12, Ui.Muted));
            aiCreationStatus = Ui.Label(controller.AiCreationStatus, 11, Ui.Muted); aiCreationStatus.Tag = "AiCreationStatus";
            panel.Children.Add(aiCreationStatus);
            BuildAiSubpages(panel);
        }
        return panel;
    }
    private TextBlock? codexStatus;
    private TextBlock? aiCreationStatus;
    internal void RefreshAiCreationStatus() { if (aiCreationStatus is not null) aiCreationStatus.Text = controller.AiCreationStatus; }
    private TextBlock? codexNotificationStatus;
    internal void RefreshCodexStatus()
    {
        if (codexStatus is not null)
        {
            codexStatus.Text = "Codex监听";
            codexStatus.Foreground = Ui.Text;
        }
        RefreshAiInitialization();
        if (codexNotificationStatus is not null) codexNotificationStatus.Text = controller.CodexNotificationStatus;
    }
    private Border CodexSettings()
    {
        var body = new StackPanel(); codexStatus = Ui.Label("Codex监听", 16); codexStatus.FontWeight = FontWeights.SemiBold; body.Children.Add(codexStatus);
        var notifications = new CheckBox { Content = "补充识别 Codex 的待处理提醒", Tag = "CodexBehaviorSupplement",
            IsChecked = controller.Engine.State.Preferences.CodexBehaviorSupplement, Margin = new Thickness(0, 10, 0, 8) };
        notifications.Click += async (_, _) =>
        {
            controller.Engine.State.Preferences.CodexBehaviorSupplement = notifications.IsChecked == true;
            controller.Engine.Touch(); await controller.RestartCodex(); RefreshCodexStatus();
        };
        body.Children.Add(notifications);
        body.Children.Add(Ui.Label("请先在 Codex 客户端中开启通知权限。开启此项后，提问、批准或执行结束时可更及时提醒你；回答或批准并继续运行后自动恢复。读取的是客户端本机记录，不代答或批准。前台同时识别侧栏和当前聊天中的控件。", 12, Ui.Muted));
        codexNotificationStatus = Ui.Label(controller.CodexNotificationStatus, 12, Ui.Muted);
        body.Children.Add(codexNotificationStatus);
        body.Children.Add(AiInitializationSettings());
        return SettingsCard(body);
    }
    private static void Try(Action action) { try { action(); } catch (Exception e) { Ui.Error(e.Message); } }
}

