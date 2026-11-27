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
    internal static readonly string[] Sections = ["主设置", "外观", "窗口", "监听", "归档", "数据与备份", "AI平台接入情况"];
    internal static string SectionCaption(string section) => section switch
    {
        "主设置" => "常规", "外观" => "外观", "窗口" => "窗口与列表", "监听" => "通知监听", "AI平台接入情况" => "AI平台监听", _ => section
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
        if (Application.Current?.TryFindResource("EditorCloseButton") is Style closeStyle) close.Style = closeStyle;
        else { close.Background = Ui.CloseButtonSurface; close.BorderBrush = Ui.CloseButtonBorder; close.Foreground = Ui.CloseButtonText; }
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
            if (Application.Current?.TryFindResource("SettingsNavButton") is Style navStyle) button.Style = navStyle;
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
    { saveStatus.Text = value; saveStatus.Foreground = value.StartsWith("保存失败", StringComparison.Ordinal) ? Ui.Danger : Ui.Muted; }
    internal void RefreshSection()
    {
        var previousOffset = pages.TryGetValue(CurrentSection, out var existing) ? existing.VerticalOffset : 0;
        pages.Clear();
        SelectSection(CurrentSection);
        if (previousOffset > 0 && pages.TryGetValue(CurrentSection, out var rebuilt))
            rebuilt.ScrollToVerticalOffset(previousOffset);
    }
    public override void ApplyTheme()
    {
        base.ApplyTheme();
        RefreshSaveStatus(controller.SettingsSaveStatus);
        RefreshSection();
    }
    internal void SelectSection(string name)
    {
        if (!navigation.ContainsKey(name)) return;
        if (name == "归档" && CurrentSection != "归档") { archiveAllLoaded = false; pages.Remove("归档"); }
        CurrentSection = name;
        foreach (var (section, button) in navigation)
        {
            if (section == name)
            {
                button.Background = ThemePalette.Current.NavSelected;
                button.Foreground = Ui.Accent;
                button.BorderBrush = ThemePalette.Current.NavSelectedBorder;
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.ForegroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
                button.FontWeight = FontWeights.Normal;
            }
        }
        if (!pages.TryGetValue(name, out var page))
        {
            var panel = name switch { "主设置" => MainSettings(), "外观" => AppearanceSettings(), "窗口" => WindowSettings(), "监听" => ListeningSettings(), "归档" => ArchiveSettings(), "数据与备份" => DataSettings(), _ => PlatformSettings() };
            OrganizeSettingsPage(name, panel);
            AttachHelp(panel);
            page = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 0, 4, 10), Tag = "SettingsPage", DataContext = name }; pages.Add(name, page);
        }
        content.Children.Clear(); content.Children.Add(page);
    }
    private StackPanel AppearanceSettings()
    {
        var prefs = controller.Engine.State.Preferences;
        var panel = new StackPanel { Tag = "AppearancePage" };
        panel.Children.Add(Ui.Label("外观", 18));

        // Card 1: Theme Mode (3-way selector, default Dark)
        var modeBody = new StackPanel { Tag = "ThemeModeCard" };
        var modeTitle = Ui.Label("主题模式", 14); modeTitle.FontWeight = FontWeights.SemiBold; modeTitle.Margin = new Thickness(0, 0, 0, 8);
        modeBody.Children.Add(modeTitle);
        var systemIsLight = ThemePalette.IsSystemLightTheme();
        var modeItems = new (ThemeMode Mode, string Title, string Hint)[]
        {
            (ThemeMode.Dark, "深夜模式（默认）", "青 + 深色，柔和护眼暗色调"),
            (ThemeMode.Light, "白天模式", "清爽明亮浅色调"),
            (ThemeMode.System, "跟随系统", $"自动随 Windows 系统深浅色实时切换（当前系统：{(systemIsLight ? "浅色" : "深色")}）")
        };
        foreach (var (mode, caption, hint) in modeItems)
        {
            var labelStack = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            var mainLabel = Ui.Label(caption, 13); mainLabel.FontWeight = FontWeights.SemiBold; mainLabel.Margin = new Thickness(0);
            var subLabel = Ui.Label(hint, 11, Ui.Muted); subLabel.Margin = new Thickness(0, 2, 0, 0);
            labelStack.Children.Add(mainLabel);
            labelStack.Children.Add(subLabel);
            var radio = new RadioButton
            {
                GroupName = "AppearanceThemeMode",
                Tag = "ThemeModeOption",
                DataContext = mode,
                IsChecked = prefs.ThemeMode == mode,
                Margin = new Thickness(0, 4, 0, 6),
                Content = labelStack
            };
            radio.Click += (_, _) =>
            {
                if (radio.IsChecked != true) return;
                controller.SetThemeMode(mode);
                if (!controller.IsOwnedSettings(this)) ApplyTheme();
            };
            modeBody.Children.Add(radio);
        }
        panel.Children.Add(SettingsCard(modeBody));

        // Card 2: Dark Theme Presets (5 cards)
        panel.Children.Add(BuildPresetSection(
            "深色配色方案",
            "用于深夜模式，或跟随系统处于深色时自动选用；点击色卡立即切换至深色预览。",
            ThemePalette.DarkPresets,
            prefs.DarkThemePreset,
            isActiveGroup: ThemePalette.Current.IsDark,
            cardTag: "DarkThemePresetCard",
            sectionTag: "DarkThemePresetsCard",
            onSelect: id => { controller.SetDarkThemePreset(id); if (!controller.IsOwnedSettings(this)) ApplyTheme(); }));

        // Card 3: Light Theme Presets (4 cards)
        panel.Children.Add(BuildPresetSection(
            "浅色配色方案",
            "用于白天模式，或跟随系统处于浅色时自动选用；点击色卡立即切换至浅色预览。",
            ThemePalette.LightPresets,
            prefs.LightThemePreset,
            isActiveGroup: !ThemePalette.Current.IsDark,
            cardTag: "LightThemePresetCard",
            sectionTag: "LightThemePresetsCard",
            onSelect: id => { controller.SetLightThemePreset(id); if (!controller.IsOwnedSettings(this)) ApplyTheme(); }));

        // Bottom lightweight hint + shortcut to Window & Lists transparency
        var footerBody = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var goTransparency = Ui.Button("调节窗口透明度", () => SelectSection("窗口"));
        goTransparency.Tag = "GoToWindowTransparency";
        goTransparency.Margin = new Thickness(12, 0, 0, 0);
        goTransparency.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(goTransparency, Dock.Right);
        footerBody.Children.Add(goTransparency);
        var tip = Ui.Label("各窗口毛玻璃透明度与 0% 纯色不透明模式可在“窗口与列表”页分别调节。", 12, Ui.Muted);
        tip.VerticalAlignment = VerticalAlignment.Center;
        footerBody.Children.Add(tip);
        panel.Children.Add(SettingsCard(footerBody));

        return panel;
    }
    private static Border BuildPresetSection(
        string title,
        string subtitle,
        IReadOnlyList<AppThemePalette> presets,
        string selectedId,
        bool isActiveGroup,
        string cardTag,
        string sectionTag,
        Action<string> onSelect)
    {
        var body = new StackPanel { Tag = sectionTag };
        var heading = Ui.Label(title, 14); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 4);
        body.Children.Add(heading);
        var desc = Ui.Label(subtitle, 12, Ui.Muted); desc.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(desc);
        var grid = new WrapPanel { Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var preset in presets)
        {
            var isSelected = string.Equals(preset.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            var isLive = isSelected && isActiveGroup;

            var cardContent = new StackPanel { Width = 148 };
            var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var swatchColor in new[] { preset.SurfaceColor, preset.PreviewCardColor, preset.PreviewAccentColor })
            {
                dots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = Ui.FreezeColor(swatchColor),
                    Stroke = preset.Line,
                    StrokeThickness = 1,
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }
            topRow.Children.Add(dots);
            if (isSelected)
            {
                var statusPill = Ui.Label(isLive ? "✓ 使用中" : "已选", 10, Ui.Accent);
                statusPill.FontWeight = FontWeights.SemiBold;
                statusPill.Margin = new Thickness(0);
                statusPill.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(statusPill, Dock.Right);
                topRow.Children.Insert(0, statusPill);
            }
            cardContent.Children.Add(topRow);

            var nameText = preset.IsDefault ? preset.Name + " · 默认" : preset.Name;
            var nameLabel = Ui.Label(nameText, 12.5);
            nameLabel.FontWeight = FontWeights.SemiBold;
            nameLabel.Margin = new Thickness(0);
            nameLabel.TextWrapping = TextWrapping.NoWrap;
            nameLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            cardContent.Children.Add(nameLabel);

            var hintLabel = Ui.Label(preset.Description, 10.5, Ui.Muted);
            hintLabel.Margin = new Thickness(0, 2, 0, 0);
            hintLabel.TextWrapping = TextWrapping.NoWrap;
            hintLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            cardContent.Children.Add(hintLabel);

            var cardButton = Ui.Button("", () => onSelect(preset.Id));
            cardButton.Content = cardContent;
            cardButton.Tag = cardTag;
            cardButton.DataContext = preset.Id;
            cardButton.Padding = new Thickness(10, 9, 10, 9);
            cardButton.Margin = new Thickness(4);
            cardButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            cardButton.ToolTip = $"{preset.Name}：{preset.Description}";
            System.Windows.Automation.AutomationProperties.SetName(cardButton, nameText + "，" + preset.Description);
            if (isSelected)
            {
                cardButton.BorderBrush = Ui.Accent;
                cardButton.BorderThickness = new Thickness(isLive ? 2 : 1.5);
                if (isLive) cardButton.Background = ThemePalette.Current.NavSelected;
            }
            grid.Children.Add(cardButton);
        }
        body.Children.Add(grid);
        return SettingsCard(body);
    }
    private StackPanel MainSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("主设置", 18));
        var tutorial = Ui.Button("重新体验新手指引", controller.ShowOnboarding);
        tutorial.Tag = "ShowOnboarding";
        tutorial.HorizontalAlignment = HorizontalAlignment.Left;
        tutorial.ToolTip = "重新打开完整新手指引流程，不影响任何任务、草稿或设置";
        panel.Children.Add(tutorial);
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
            ToolTip = "默认开启；按阶段详情行数自适应省略，可点击末尾气泡展开或收起并记住状态。关闭后完整显示阶段详情。" };
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
        var startup = new CheckBox { Content = "开机启动", Tag = "SettingsAutoStart", IsChecked = controller.Engine.State.Preferences.AutoStart, Margin = new Thickness(0, 10, 0, 8), IsEnabled = !controller.Isolated };
        var autoStartStatus = Ui.Label("", 12, Ui.Muted);
        autoStartStatus.Tag = "AutoStartDiagnosticStatus";
        autoStartStatus.Margin = new Thickness(0, 0, 0, 6);
        var autoStartHint = Ui.Label("", 11, Ui.Warn);
        autoStartHint.Tag = "AutoStartDiagnosticHint";
        autoStartHint.Margin = new Thickness(0, 0, 0, 6);
        autoStartHint.Visibility = Visibility.Collapsed;
        var openWindowsStartup = Ui.Button("打开 Windows 启动应用设置", () => Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true })));
        openWindowsStartup.Tag = "OpenWindowsStartupApps";
        openWindowsStartup.Visibility = Visibility.Collapsed;
        void RefreshAutoStartUi()
        {
            if (controller.Isolated)
            {
                autoStartStatus.Text = "隔离测试模式不读写系统开机启动项。";
                autoStartStatus.Foreground = Ui.Muted;
                autoStartHint.Visibility = Visibility.Collapsed;
                openWindowsStartup.Visibility = Visibility.Collapsed;
                return;
            }
            var diag = StartupLinks.DiagnoseAutoStart();
            autoStartStatus.Text = diag.Summary;
            var warn = diag.Health is AutoStartHealth.WindowsDisabled or AutoStartHealth.PathMissing or AutoStartHealth.PathChanged;
            autoStartStatus.Foreground = warn ? Ui.Warn : diag.Health == AutoStartHealth.Healthy ? Ui.Accent : Ui.Muted;
            autoStartHint.Text = diag.ActionHint ?? "";
            autoStartHint.Visibility = string.IsNullOrEmpty(diag.ActionHint) ? Visibility.Collapsed : Visibility.Visible;
            openWindowsStartup.Visibility = diag.Health == AutoStartHealth.WindowsDisabled ? Visibility.Visible : Visibility.Collapsed;
        }
        startup.Click += (_, _) =>
        {
            try { StartupLinks.SetAutoStart(startup.IsChecked == true); controller.Engine.State.Preferences.AutoStart = startup.IsChecked == true; controller.Engine.Touch(); RefreshAutoStartUi(); }
            catch (Exception e) { Ui.Error("设置开机启动失败：" + e.Message); startup.IsChecked = controller.Engine.State.Preferences.AutoStart; }
        };
        RefreshAutoStartUi();
        panel.Children.Add(startup);
        var shortcut = Ui.Button("创建桌面快捷方式", () => Try(StartupLinks.Shortcut)); shortcut.IsEnabled = !controller.Isolated; panel.Children.Add(shortcut);
        panel.Children.Add(autoStartStatus);
        panel.Children.Add(autoStartHint);
        panel.Children.Add(openWindowsStartup);
        var repair = Ui.Button("修复开机启动路径", () => Try(() => { StartupLinks.SetAutoStart(controller.Engine.State.Preferences.AutoStart); RefreshAutoStartUi(); })); repair.IsEnabled = !controller.Isolated; panel.Children.Add(repair);
        if (!controller.Isolated)
            panel.Children.Add(Ui.Button("Windows启动应用设置", () => Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }))));
        panel.Children.Add(AppUpdateSettings());
        panel.Children.Add(Ui.Label(Brand.Name + " " + typeof(Program).Assembly.GetName().Version!.ToString(3), 11, Ui.Muted)); return panel;
    }
    private StackPanel AppUpdateSettings()
    {
        var panel = new StackPanel { Tag = "AppUpdateSection" };
        panel.Children.Add(Ui.Label("版本与更新", 14));
        panel.Children.Add(Ui.Label($"当前版本：{AppUpdater.CurrentVersion}（固定安装目录更新不改动任务与配置数据）", 12, Ui.Muted));
        var updateStatus = Ui.Label("点击“检查新版本”直接从 GitHub Releases 查询最新版本。", 12, Ui.Muted);
        updateStatus.Tag = "AppUpdateStatus";
        panel.Children.Add(updateStatus);
        var updateNotes = Ui.Label("", 11, Ui.Muted);
        updateNotes.Tag = "AppUpdateNotes";
        updateNotes.Visibility = Visibility.Collapsed;
        panel.Children.Add(updateNotes);

        ReleaseInfo? pendingRelease = null;
        Button? checkButton = null;
        Button? applyButton = null;

        checkButton = Ui.Button("检查新版本", async () =>
        {
            if (checkButton is null || applyButton is null) return;
            checkButton.IsEnabled = false;
            applyButton.Visibility = Visibility.Collapsed;
            updateNotes.Visibility = Visibility.Collapsed;
            updateStatus.Foreground = Ui.Muted;
            updateStatus.Text = "正在从 GitHub Releases 检查最新版本…";
            try
            {
                var result = await AppUpdater.CheckForUpdateAsync();
                updateStatus.Text = result.Message;
                updateStatus.Foreground = !result.Success ? Ui.Warn : result.UpdateAvailable ? Ui.Accent : Ui.Muted;
                pendingRelease = result.UpdateAvailable ? result.Release : null;
                if (pendingRelease is not null)
                {
                    updateNotes.Text = pendingRelease.NotesSummary;
                    updateNotes.Visibility = Visibility.Visible;
                    applyButton.Visibility = !string.IsNullOrWhiteSpace(pendingRelease.ZipDownloadUrl) && !string.IsNullOrWhiteSpace(pendingRelease.ChecksumsDownloadUrl)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            finally { checkButton.IsEnabled = true; }
        });
        checkButton.Tag = "CheckUpdateButton";

        applyButton = Ui.Button("一键下载并升级", async () =>
        {
            if (pendingRelease is null || checkButton is null || applyButton is null) return;
            if (controller.Isolated)
            {
                updateStatus.Text = "隔离测试模式不执行覆盖安装。";
                updateStatus.Foreground = Ui.Warn;
                return;
            }
            checkButton.IsEnabled = false;
            applyButton.IsEnabled = false;
            try
            {
                var (ok, message) = await AppUpdater.DownloadVerifyAndLaunchInstallAsync(
                    pendingRelease,
                    progress => Dispatcher.Invoke(() => { updateStatus.Text = progress; updateStatus.Foreground = Ui.Muted; }));
                updateStatus.Text = message;
                updateStatus.Foreground = ok ? Ui.Accent : Ui.Danger;
            }
            finally
            {
                checkButton.IsEnabled = true;
                applyButton.IsEnabled = true;
            }
        }, true);
        applyButton.Tag = "ApplyUpdateButton";
        applyButton.Visibility = Visibility.Collapsed;

        var openReleases = Ui.Button("打开 GitHub 发布页", () => Try(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pendingRelease?.HtmlUrl ?? AppUpdater.ReleasesPageUrl) { UseShellExecute = true })));
        openReleases.Tag = "OpenReleasesButton";

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        buttons.Children.Add(checkButton);
        buttons.Children.Add(applyButton);
        buttons.Children.Add(openReleases);
        panel.Children.Add(buttons);
        return panel;
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
        panel.Children.Add(Ui.Label("数值越大越透明；0% 为纯色不透明（跳过桌面实时模糊，更省性能），1%–100% 保留背景模糊。默认20%。", 12, Ui.Muted));
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.GlassTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        slider.ValueChanged += (_, _) => { caption.Text = $"多列表窗口透明度 {slider.Value:0}%"; controller.SetGlassTransparency(slider.Value); }; panel.Children.Add(slider);
        var floatingCaption = Ui.Label($"悬浮窗口透明度 {controller.Engine.State.Preferences.FloatingTransparency:0}%", 14); panel.Children.Add(floatingCaption);
        panel.Children.Add(Ui.Label("新建、编辑任务、设置等悬浮窗口独立调节，默认10%；0% 为纯色不透明（跳过桌面实时模糊，更省性能），其余数值保留背景模糊。", 12, Ui.Muted));
        var floatingSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.FloatingTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        floatingSlider.ValueChanged += (_, _) => { floatingCaption.Text = $"悬浮窗口透明度 {floatingSlider.Value:0}%"; controller.SetFloatingTransparency(floatingSlider.Value); }; panel.Children.Add(floatingSlider);
        var ballCaption = Ui.Label($"悬浮球透明度 {controller.Engine.State.Preferences.BallTransparency:0}%", 14); panel.Children.Add(ballCaption);
        panel.Children.Add(Ui.Label("悬浮球独立调节，默认40%；0% 为纯色不透明（跳过桌面实时模糊，更省性能）。", 12, Ui.Muted));
        var ballSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.BallTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18) };
        ballSlider.ValueChanged += (_, _) => { ballCaption.Text = $"悬浮球透明度 {ballSlider.Value:0}%"; controller.SetBallTransparency(ballSlider.Value); }; panel.Children.Add(ballSlider);
        var pinnedActionCaption = Ui.Label($"待操作常驻穿透透明度 {controller.Engine.State.Preferences.PinnedActionTransparency:0}%", 14); panel.Children.Add(pinnedActionCaption);
        panel.Children.Add(Ui.Label("待操作列开启“常驻”穿透模式时的透明度，默认70%；0% 为纯色不透明（跳过桌面实时模糊，更省性能）。", 12, Ui.Muted));
        var pinnedActionSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.PinnedActionTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Tag = "PinnedActionTransparencySlider", Margin = new Thickness(6, 12, 6, 18) };
        pinnedActionSlider.ValueChanged += (_, _) => { pinnedActionCaption.Text = $"待操作常驻穿透透明度 {pinnedActionSlider.Value:0}%"; controller.SetPinnedActionTransparency(pinnedActionSlider.Value); }; panel.Children.Add(pinnedActionSlider);
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
        apply.MinHeight = 34; apply.Padding = new Thickness(12, 6, 12, 6);
        countRow.Children.Add(number); countRow.Children.Add(apply); panel.Children.Add(countRow);
        panel.Children.Add(Ui.Label("隐藏不删除任务；六个内置列表可改名、隐藏，不能删除。",12,Ui.Muted));
        foreach (var column in controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var remove = Ui.Button(column.Protected ? "内置" : "删除",() =>
            { try { BoardColumns.Delete(controller.Engine.State,column); controller.ColumnsChanged(); RefreshSection(); } catch (InvalidOperationException e) { note.Text = e.Message; } });
            remove.Tag = "ListDelete"; remove.DataContext = column; remove.MinWidth = 54; remove.MinHeight = 30; remove.Padding = new Thickness(10, 5, 10, 5);
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

