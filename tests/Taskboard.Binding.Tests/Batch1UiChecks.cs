using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class Batch1UiChecks
{
    internal static int Run(string output)
    {
        Exception? failure = null;
        string? summary = null;
        var thread = new Thread(() =>
        {
            try { summary = RunOnSta(output); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Batch1UiChecks failed: " + failure.Message, failure);
        Console.WriteLine(summary);
        return 0;
    }

    private static string RunOnSta(string output)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var theme = new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) };
        app.Resources.MergedDictionaries.Add(theme);

        // Hidden offscreen HwndSource (no WS_VISIBLE) gives a real WPF PresentationSource
        // and Keyboard.Focus support without showing any window on the desktop.
        var hostParams = new HwndSourceParameters("Batch1OffscreenHost")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0x08000000, // WS_EX_NOACTIVATE
            PositionX = -20000,
            PositionY = -20000,
            Width = 820,
            Height = 760
        };
        using var host = new HwndSource(hostParams);
        var stageHost = new Grid { Width = 820, Height = 760 };
        host.RootVisual = stageHost;

        var checks = new List<string>();
        void Assert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("失败：" + name);
            checks.Add(name);
        }

        var controller = new BoardController(output, isolated: true, smoke: true);
        try
        {
            // #01: Settings 3-way option + auto focus to new stage details on click
            var settings = new SettingsSurface(controller);
            Layout(settings.Root, 760, 680);
            var radios = Descendants<RadioButton>(settings.Root).Where(x => Equals(x.Tag, "NewStageDefaultState")).ToArray();
            Assert(radios.Length == 3 && radios[0].IsChecked == true, "#01 设置页显示新建阶段默认状态三选一且默认选中选项A");
            radios[1].IsChecked = true;
            radios[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(controller.Engine.State.Preferences.NewStageDefaultState == NewStageDefaultState.AlwaysRunning, "#01 设置页点击选项B立即更新偏好");
            radios[2].IsChecked = true;
            radios[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(controller.Engine.State.Preferences.NewStageDefaultState == NewStageDefaultState.AlwaysInactive, "#01 设置页点击选项C立即更新偏好");
            radios[0].IsChecked = true;
            radios[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(controller.Engine.State.Preferences.NewStageDefaultState == NewStageDefaultState.ContextDependent, "#01 设置页恢复默认选项A");
            settings.Destroy(false);

            // #01 auto-focus + #10 running stay expanded + #27 fake bottom space & bottom click + #18 right-click + #11 initial drag + #21 stage name caret + #08 arrows
            var session = controller.Engine.NewSession(Lane.Running);
            session.Working.Title = "标题文本";
            session.Working.Stages[0].Text = string.Join("\n", Enumerable.Range(1, 14).Select(i => $"运行中第{i}行内容用于测试展开不自动收起"));
            var editor = new EditorSurface(controller, session);
            stageHost.Children.Add(editor);
            Layout(editor, 430, EditorSurface.DefaultHeight);

            var addStageBtn = Descendants<Button>(editor).Single(x => Equals(x.Tag, "AddStage"));
            addStageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(editor, 430, EditorSurface.DefaultHeight);
            var stageEditors = Descendants<StageDetailsEditor>(editor).ToArray();
            Assert(stageEditors.Length == 2, "点击＋新阶段新增第2个阶段");
            Assert(ReferenceEquals(FocusManager.GetFocusedElement(stageHost), stageEditors[1].Input) || stageEditors[1].Input.IsFocused, "#01 点击新建阶段后光标自动聚焦到新阶段详情框");

            // #10: Running stage stays completely expanded even without focus!
            var runningEditor = stageEditors[0];
            Assert(!runningEditor.Input.IsKeyboardFocusWithin, "运行中阶段此时无键盘焦点");
            Assert(double.IsPositiveInfinity(runningEditor.Input.MaxHeight) && runningEditor.ActualHeight > 220, "#10 运行中多行阶段在无焦点时也保持完整展开，不自动收起或截断");

            // #10: Collapsed inactive stage ellipsis (multiline & long single line)
            var inactiveEditor = stageEditors[1];
            inactiveEditor.Input.Text = "第一行文本\n第二行被收起内容";
            addStageBtn.Focus();
            Layout(editor, 430, EditorSurface.DefaultHeight);
            var previewTextBlock = inactiveEditor.Children.OfType<TextBlock>().Single();
            Assert(!inactiveEditor.Input.IsKeyboardFocusWithin && previewTextBlock.Visibility == Visibility.Visible && previewTextBlock.Text == "第一行文本…", "#10 未运行多行阶段收起态显示首行+省略号");
            inactiveEditor.Input.Text = new string('测', 80);
            Layout(editor, 430, EditorSurface.DefaultHeight);
            Assert(previewTextBlock.TextTrimming == TextTrimming.CharacterEllipsis && previewTextBlock.TextWrapping == TextWrapping.NoWrap, "#10 未运行单行超长阶段启用 CharacterEllipsis 省略号");

            // #07: Removed noisy tooltips
            var stageNames = Descendants<TextBox>(editor).Where(x => Equals(x.Tag, "StageName")).ToArray();
            var laneSelector = Descendants<ComboBox>(editor).First(x => x.Tag is null);
            var stageSearch = Descendants<TextBox>(editor).FirstOrDefault(x => Equals(x.Tag, "StageConversationSearch"));
            Assert(runningEditor.Input.ToolTip is null && inactiveEditor.Input.ToolTip is null, "#07 阶段详情输入框无挡视野Tooltip");
            Assert(stageNames.Length == 2 && stageNames.All(x => x.ToolTip is null), "#07 阶段名称输入框无挡视野Tooltip");
            Assert(laneSelector.ToolTip is null, "#07 当前状态下拉框无复读Tooltip");
            Assert(stageSearch is null || stageSearch.ToolTip is null, "#07 会话搜索框无遮挡Tooltip");
            Assert(Descendants<Button>(editor).Where(x => Equals(x.Tag, "StageRun") || Equals(x.Tag, "StageRemove")).All(x => x.ToolTip is not null), "#07 有信息价值的操作按钮Tooltip保留");

            // #21: Stage name capsule stretches as a single native TextBox filling column 0
            var stageNameBox = stageNames[0];
            stageNameBox.Text = "阶段0";
            Layout(editor, 430, EditorSurface.DefaultHeight);
            Assert(stageNameBox.HorizontalAlignment == HorizontalAlignment.Stretch && stageNameBox.ActualWidth >= 100 && stageNameBox.ActualWidth <= 155,
                $"#21 阶段名称圆角胶囊整体即为完整拉满的单一TextBox(width={stageNameBox.ActualWidth:F1})");
            var presenter = ColumnSurface.FindChild<ScrollContentPresenter>(stageNameBox)!;
            var insideRightOfText = new Point(Math.Min(stageNameBox.ActualWidth - 16, 80), stageNameBox.ActualHeight / 2);
            Assert(new Rect(presenter.RenderSize).Contains(stageNameBox.TranslatePoint(insideRightOfText, presenter)),
                "#21 胶囊内文本右侧空白区直接处于原生ScrollContentPresenter命中范围内");
            var rightClickPoint = new Point(stageNameBox.ActualWidth - 4, stageNameBox.ActualHeight / 2);
            var rightCaret = RoundedInput.ResolveCaretIndex(stageNameBox, rightClickPoint);
            Assert(rightCaret == 3, $"#21 点击阶段名称最右侧圆角边光标准确落在\"阶段0\"末尾(实际={rightCaret})");
            var leftClickPoint = new Point(4, stageNameBox.ActualHeight / 2);
            var leftCaret = RoundedInput.ResolveCaretIndex(stageNameBox, leftClickPoint);
            Assert(leftCaret == 0, $"#21 点击阶段名称左侧圆角边光标准确落在\"阶段0\"开头(实际={leftCaret})");

            // #11: Clicking collapsed stage details suppresses initial drag selection
            inactiveEditor.Input.Text = "第一行内容比较长用于测试点击展开不误选\n第二行内容\n第三行内容\n第四行内容";
            stageNameBox.Focus();
            Layout(editor, 430, EditorSurface.DefaultHeight);
            Assert(!inactiveEditor.Input.IsKeyboardFocusWithin, "#11 测试前未运行阶段处于收起失焦态");
            var handled = inactiveEditor.HandleCollapsedExpandClick(new Point(40, 12), 1);
            Assert(handled && (ReferenceEquals(FocusManager.GetFocusedElement(stageHost), inactiveEditor.Input) || inactiveEditor.Input.IsFocused) && inactiveEditor.Input.SelectionLength == 0, "#11 点击收起态阶段详情展开进入编辑时抑制拖拽选区且精准聚焦");

            // #18: Selecting text and opening ContextMenu does NOT collapse StageDetailsEditor; custom dark rounded ContextMenu exists
            inactiveEditor.Input.Select(0, 6);
            var textMenu = inactiveEditor.Input.ContextMenu;
            Assert(textMenu is not null && textMenu.Items.OfType<MenuItem>().Count() == 5, "#18 输入框已挂载自定义中文暗色圆角右键菜单(5项)");
            Assert(inactiveEditor.Input.IsInactiveSelectionHighlightEnabled, "#18 启用失焦选区高亮保持");
            var openMethod = typeof(StageDetailsEditor).GetMethod("OnInputContextMenuOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var closeMethod = typeof(StageDetailsEditor).GetMethod("OnContextMenuClosed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cmArgs = (ContextMenuEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ContextMenuEventArgs));
            openMethod.Invoke(inactiveEditor, [inactiveEditor.Input, cmArgs]);
            stageNameBox.Focus(); // Simulate WPF moving focus out of Input while menu is open
            Layout(editor, 430, EditorSurface.DefaultHeight);
            Assert(inactiveEditor.ActualHeight > 60 && inactiveEditor.Input.SelectionLength == 6, $"#18 选中文本打开右键菜单期间阶段详情保持展开不自动收起且保留选区(h={inactiveEditor.ActualHeight:F1}, sel={inactiveEditor.Input.SelectionLength})");
            closeMethod.Invoke(inactiveEditor, [textMenu, new RoutedEventArgs()]);
            Layout(editor, 430, EditorSurface.DefaultHeight);

            // #27: No fake blank space at bottom of StageDetailsEditor, while outer editor ScrollViewer remains focusable so clicking outside inputs leaves focus
            runningEditor.Input.Text = "第1行\n第2行\n第3行\n第4行无尾部空行";
            Layout(editor, 430, EditorSurface.DefaultHeight);
            var lastRect = runningEditor.Input.GetRectFromCharacterIndex(runningEditor.Input.Text.Length);
            var diff = runningEditor.ActualHeight - (lastRect.Bottom + runningEditor.Input.Padding.Bottom);
            Assert(!lastRect.IsEmpty && diff <= 4.0 && diff >= -1.0, $"#27 多行阶段详情底部无虚假留空(差值={diff:F2}px)");
            var editorScroll = Descendants<ScrollViewer>(editor).First(x => x.TemplatedParent is null);
            Assert(editorScroll.Focusable, "#27 编辑器外层ScrollViewer保持可聚焦，点击输入框外空白可正常退出焦点");

            // #08: Up on first line -> 0, Down on last line -> Text.Length; middle line untouched; IME untouched
            var multi = inactiveEditor.Input;
            multi.Text = "第一行abc\n第二行def\n第三行ghi";
            Layout(editor, 430, EditorSurface.DefaultHeight);
            multi.Focus();
            multi.CaretIndex = 3; // first line middle
            var upFirst = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            multi.RaiseEvent(upFirst);
            Assert(upFirst.Handled && multi.CaretIndex == 0 && multi.SelectionLength == 0, "#08 多行输入框第一行按向上键跳至最开头");

            multi.CaretIndex = 10; // middle line
            var upMiddle = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            multi.RaiseEvent(upMiddle);
            Assert(!upMiddle.Handled, "#08 多行输入框中间行按向上键不越级拦截");

            multi.CaretIndex = multi.Text.Length - 3; // last line middle
            var downLast = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            multi.RaiseEvent(downLast);
            Assert(downLast.Handled && multi.CaretIndex == multi.Text.Length && multi.SelectionLength == 0, "#08 多行输入框最后一行按向下键跳至最末尾");

            multi.CaretIndex = 3;
            var imeKey = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.ImeProcessed) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            multi.RaiseEvent(imeKey);
            Assert(!imeKey.Handled && multi.CaretIndex == 3, "#08 中文IME选词期间(Key.ImeProcessed)不干扰不拦截");

            // Single-line stage name arrow test
            stageNameBox.Focus();
            stageNameBox.CaretIndex = 1;
            var singleDown = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            stageNameBox.RaiseEvent(singleDown);
            Assert(singleDown.Handled && stageNameBox.CaretIndex == 3, "#08 单行输入框按向下键跳至最末尾");
            stageNameBox.CaretIndex = 2;
            var singleUp = new KeyEventArgs(Keyboard.PrimaryDevice, host, 0, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            stageNameBox.RaiseEvent(singleUp);
            Assert(singleUp.Handled && stageNameBox.CaretIndex == 0, "#08 单行输入框按向上键跳至最开头");

            // #28: StageReorderGesture glyph uses unified Fill (1.5 width, softened color), Arrow cursor, and IsMouseOver accent trigger
            var reorderThumb = Descendants<Thumb>(editor).First(x => Equals(x.Tag, "StageReorder"));
            var thumbPath = Descendants<System.Windows.Shapes.Path>(reorderThumb).Single();
            Assert(thumbPath.Fill is not null && thumbPath.Stroke is null, "#28 排序图标使用无自相交合并轮廓单次填充(Fill!=null, Stroke==null)");
            Assert(reorderThumb.Cursor == Cursors.Arrow, "#28 鼠标悬停排序按钮不改变鼠标指针样式(保持Arrow)");
            var hasHoverAccentTrigger = reorderThumb.Template.Triggers.OfType<Trigger>().Any(t =>
                t.Property == UIElement.IsMouseOverProperty && Equals(t.Value, true) &&
                t.Setters.OfType<Setter>().Any(s => s.Property == Border.BorderBrushProperty));
            Assert(hasHoverAccentTrigger, "#28 排序按钮Thumb模板包含IsMouseOver青绿色边框高亮触发器");
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(reorderThumb), null, new Rect(0, 0, 24, 24));
            var bmp = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var pixels = new byte[24 * 24 * 4];
            bmp.CopyPixels(pixels, 24 * 4, 0);
            byte MaxBrightnessInRows(int yStart, int yEnd)
            {
                byte max = 0;
                for (var y = yStart; y <= yEnd; y++)
                for (var x = 8; x <= 15; x++)
                {
                    var idx = (y * 24 + x) * 4;
                    max = Math.Max(max, Math.Max(pixels[idx], Math.Max(pixels[idx + 1], pixels[idx + 2])));
                }
                return max;
            }
            var topPeak = MaxBrightnessInRows(5, 7);
            var midPeak = MaxBrightnessInRows(10, 13);
            var botPeak = MaxBrightnessInRows(16, 18);
            Assert(topPeak >= 140 && midPeak >= 140 && botPeak >= 140 && topPeak < 235 && Math.Abs(topPeak - midPeak) <= 12 && Math.Abs(botPeak - midPeak) <= 12,
                $"#28 排序图标1.5线宽柔化后上中下段像素亮度均匀且与相邻按钮协调(top={topPeak}, mid={midPeak}, bot={botPeak})");

            // #10: ColumnSurface header ellipsis + StageDetailsText ellipsis
            var column = new ColumnSurface(controller, Lane.Running);
            stageHost.Children.Remove(editor);
            editor.Destroy();
            Layout(column.Root, 80, 300);
            var colHeading = Descendants<TextBlock>(column.Root).First(x => x.Text == column.Definition.Name);
            Assert(colHeading.TextTrimming == TextTrimming.CharacterEllipsis && colHeading.TextWrapping == TextWrapping.NoWrap, "#10 列表列标题窄宽启用单行省略号");
            column.Destroy(false);

            var detailsText = new StageDetailsText("第一行内容\n第二行内容\n第三行被省略内容", 14, Ui.Text, true, 42);
            Layout(detailsText, 200, 42);
            Assert(detailsText.DisplayedLines == 2, "#10 StageDetailsText 开启省略时稳定截断在2行");
        }
        finally
        {
            controller.Dispose();
        }

        var report = new { passed = checks.Count, checks, offscreen = true, visibleWindows = 0, mouseMoved = false };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "batch1-ui-report.json"), json);
        return JsonSerializer.Serialize(new { passed = checks.Count, offscreen = true, visibleWindows = 0 });
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
