using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Edits the existing reminder draft. Raw input and popup candidates are kept
// separate from the last valid value; the clock and persistence remain in Core.
internal sealed class ReminderEditor : StackPanel
{
    private readonly Reminder reminder;
    private readonly Action changed;
    private readonly Func<DateTime> now;
    private readonly CheckBox enabled;
    private readonly StackPanel fields = new(), dateRow = new(), weekRow = new(), relative = new();
    private readonly TextBox time, date;
    private readonly ComboBox repeat, weekday;
    private readonly TextBlock error = Ui.Label("", 11, Ui.Brush("#F28F98"));
    private readonly TextBlock summary = Ui.Label("", 12, Ui.Accent);
    private readonly Popup timePopup = new(), datePopup = new();
    private bool setting;
    internal ReminderEditor(Reminder reminder, Action changed, Func<DateTime>? clock = null)
    {
        this.reminder = reminder; this.changed = changed; now = clock ?? (() => DateTime.Now); Tag = "ReminderEditor";
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        enabled = new CheckBox { Tag = "ReminderToggle", Width = 22, Height = 22, IsChecked = reminder.Enabled,
            VerticalAlignment = VerticalAlignment.Center, Clip = new EllipseGeometry(new Point(11, 11), 11, 11) };
        System.Windows.Automation.AutomationProperties.SetName(enabled, "定时提醒");
        heading.Children.Add(enabled); heading.Children.Add(Ui.Label("定时提醒")); Children.Add(heading); Children.Add(fields);
        repeat = new ComboBox { ItemsSource = new[] { "一次", "每天", "每周" }, SelectedIndex = (int)reminder.Repeat,
            MinHeight = 34, Margin = new Thickness(0, 0, 0, 8), Tag = "ReminderRepeat" };
        System.Windows.Automation.AutomationProperties.SetName(repeat, "重复方式"); fields.Children.Add(repeat);
        date = Ui.Input(reminder.NextLocal.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)); date.MaxLength = 32; date.Tag = "ReminderDate";
        date.ToolTip = "输入日期，例如2026/11/14，或打开日历选择";
        time = Ui.Input(reminder.Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture)); time.MaxLength = 8; time.Tag = "ReminderTime";
        RoundedInput.SetEdgeArrowNavigation(time, false);
        time.ToolTip = "24小时制，例如09:05；可输入、选择，或用上下键调整分钟";
        System.Windows.Automation.AutomationProperties.SetName(date, "提醒日期"); System.Windows.Automation.AutomationProperties.SetName(time, "提醒时间，24小时制");
        dateRow.Children.Add(Ui.Label("日期", 11, Ui.Muted));
        dateRow.Children.Add(Field(date, "日历", "ReminderDateOpen", () => OpenDate()));
        var dates = new WrapPanel();
        foreach (var offset in new[] { 0, 1 }) { var day = offset; var b = Ui.Button(day == 0 ? "今天" : "明天", () => { date.Text = now().Date.AddDays(day).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture); Update(false); }); b.Tag = day == 0 ? "ReminderToday" : "ReminderTomorrow"; dates.Children.Add(b); }
        dateRow.Children.Add(dates); fields.Children.Add(dateRow);
        weekday = new ComboBox { ItemsSource = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" },
            SelectedIndex = (int)reminder.Weekday, MinHeight = 34, Margin = new Thickness(0, 0, 0, 8), Tag = "ReminderWeekday" };
        System.Windows.Automation.AutomationProperties.SetName(weekday, "提醒星期"); weekRow.Children.Add(weekday); fields.Children.Add(weekRow);
        fields.Children.Add(Ui.Label("时间 · 24小时制", 11, Ui.Muted)); fields.Children.Add(Field(time, "选择时间", "ReminderTimeOpen", () => OpenTime()));
        var shortcuts = new WrapPanel();
        foreach (var minutes in new[] { 15, 30, 60 }) { var delay = minutes; var b = Ui.Button(delay == 60 ? "1小时后" : delay + "分钟后", () => SetRelative(delay)); b.Tag = "ReminderAfter"; b.DataContext = delay; shortcuts.Children.Add(b); }
        relative.Children.Add(shortcuts); fields.Children.Add(relative); summary.Tag = "ReminderSummary"; error.Tag = "ReminderError"; fields.Children.Add(summary); fields.Children.Add(error);
        enabled.Click += (_, _) => { ClosePopups(); Update(false); };
        repeat.SelectionChanged += (_, _) => { ClosePopups(); Update(false); }; weekday.SelectionChanged += (_, _) => Update(false);
        foreach (var box in new[] { time, date })
        {
            box.TextChanged += (_, _) => { if (!setting) Update(false); };
            box.LostKeyboardFocus += (_, _) => Update(true);
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { if (ReferenceEquals(box, time)) OpenTime(); else OpenDate(); e.Handled = true; }
                else if (key == Key.Enter) { TryCommit(); e.Handled = true; }
                else if (ReferenceEquals(box, time) && key is Key.Up or Key.Down && TryTime(time.Text, out var value))
                { var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 60 : 1; time.Text = TimeSpan.FromMinutes((value.TotalMinutes + (key == Key.Up ? delta : -delta) + 1440) % 1440).ToString(@"hh\:mm", CultureInfo.InvariantCulture); time.SelectAll(); e.Handled = true; }
            };
        }
        Unloaded += (_, _) => ClosePopups(); RefreshVisibility(); RefreshSummary();
    }
    private static Grid Field(TextBox input, string caption, string tag, Action open)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(input); var button = Ui.Button(caption, open); button.Tag = tag; button.Margin = new Thickness(8, 4, 0, 8); Grid.SetColumn(button, 1); row.Children.Add(button); return row;
    }
    private void RefreshVisibility()
    {
        fields.Visibility = enabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        dateRow.Visibility = relative.Visibility = repeat.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        weekRow.Visibility = repeat.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string Normalize(string text) => new(text.Trim().Select(c => c is >= '０' and <= '９' ? (char)('0' + c - '０') : c == '：' ? ':' : c).ToArray());
    internal static bool TryTime(string raw, out TimeSpan value)
    {
        value = default; var text = Normalize(raw);
        if (!text.Contains(':') && text.Length is 3 or 4 && text.All(char.IsAsciiDigit)) text = text.Insert(text.Length - 2, ":");
        var pieces = text.Split(':');
        if (pieces.Length != 2 || pieces.Any(x => x.Length is < 1 or > 2 || !x.All(char.IsAsciiDigit)) ||
            !int.TryParse(pieces[0], out var hour) || !int.TryParse(pieces[1], out var minute) || hour > 23 || minute > 59) return false;
        value = new TimeSpan(hour, minute, 0); return true;
    }
    internal static bool TryDate(string raw, out DateTime value) => DateTime.TryParseExact(Normalize(raw),
        new[] { "yyyy/M/d", "yyyy-MM-dd", "yyyy.M.d", "yyyy年M月d日", CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern },
        CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    internal bool TryCommit()
    {
        var valid = Update(true);
        if (!valid) { if (!TryTime(time.Text, out _)) time.Focus(); else date.Focus(); }
        return valid;
    }
    private bool Update(bool showError)
    {
        if (setting) return true; RefreshVisibility();
        if (enabled.IsChecked != true) { reminder.Enabled = false; error.Text = ""; error.Visibility = Visibility.Collapsed; changed(); return true; }
        var validTime = TryTime(time.Text, out var parsed); var validDate = TryDate(date.Text, out var selected);
        var message = !validTime ? "请输入00:00至23:59之间的时间，例如09:05。" : repeat.SelectedIndex == 0 && !validDate ? "请选择日期，或输入有效日期，例如2026/11/14。" : "";
        error.Text = showError ? message : ""; error.Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (message.Length > 0) { summary.Text = "请选择有效的日期和时间"; return false; }
        reminder.Enabled = true; reminder.Repeat = (Repeat)Math.Max(0, repeat.SelectedIndex); reminder.Weekday = (DayOfWeek)Math.Max(0, weekday.SelectedIndex); reminder.Time = parsed;
        reminder.NextLocal = reminder.Repeat == Repeat.Once ? selected.Date.Add(parsed) : ReminderClock.Next(reminder, now());
        if (showError) { setting = true; time.Text = parsed.ToString(@"hh\:mm", CultureInfo.InvariantCulture); if (reminder.Repeat == Repeat.Once) date.Text = selected.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture); setting = false; }
        RefreshSummary(); changed(); return true;
    }
    private void RefreshSummary()
    {
        summary.Text = reminder.Repeat switch { Repeat.Daily => "每天 " + reminder.Time.ToString(@"hh\:mm"), Repeat.Weekly => "每" + new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)reminder.Weekday] + " " + reminder.Time.ToString(@"hh\:mm"), _ => reminder.NextLocal.ToString("M月d日 dddd HH:mm") };
        error.Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void SetRelative(int minutes)
    {
        var target = now().AddMinutes(minutes); setting = true; date.Text = target.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture); time.Text = target.ToString("HH:mm", CultureInfo.InvariantCulture); setting = false; Update(false);
    }
    private static void ShowPopup(Popup popup, FrameworkElement target, UIElement body, Action onClose)
    {
        popup.IsOpen = false; popup.PlacementTarget = target; popup.Placement = PlacementMode.Bottom; popup.VerticalOffset = 4; popup.StaysOpen = false; popup.AllowsTransparency = true;
        var frame = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = body };
        frame.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; onClose(); e.Handled = true; } };
        popup.Child = frame; popup.IsOpen = true;
    }
    private void OpenDate()
    {
        if (datePopup.IsOpen) { datePopup.IsOpen = false; return; } timePopup.IsOpen = false;
        var selected = TryDate(date.Text, out var current) ? current : reminder.NextLocal;
        var calendar = new System.Windows.Controls.Calendar { SelectedDate = selected.Date, DisplayDate = selected.Date, FirstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, Tag = "ReminderCalendar" };
        calendar.CalendarItemStyle = CalendarItemStyle();
        calendar.SelectedDatesChanged += (_, _) => { if (calendar.SelectedDate is not DateTime value) return; date.Text = value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture); datePopup.IsOpen = false; Update(false); date.Focus(); };
        ShowPopup(datePopup, date, calendar, () => date.Focus()); calendar.Focus();
    }
    private static Style CalendarItemStyle()
    {
        // CalendarItem reads its weekday template from ControlTemplate.Resources,
        // not the Calendar's resources. Clone locally so other date pickers keep
        // their existing template and input behavior.
        var original = (Style)Application.Current.FindResource(typeof(CalendarItem));
        var template = (ControlTemplate)original.Setters.OfType<Setter>().Single(x => x.Property == Control.TemplateProperty).Value;
        var local = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(template));
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        label.SetValue(FrameworkElement.TagProperty, "ReminderWeekdayTitle"); label.SetValue(TextBlock.ForegroundProperty, Ui.Muted); label.SetValue(TextBlock.FontSizeProperty, 11.0);
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 5));
        local.Resources[CalendarItem.DayTitleTemplateResourceKey] = new DataTemplate { VisualTree = label };
        var style = new Style(typeof(CalendarItem), original); style.Setters.Add(new Setter(Control.TemplateProperty, local)); return style;
    }
    private void OpenTime()
    {
        if (timePopup.IsOpen) { timePopup.IsOpen = false; return; } datePopup.IsOpen = false;
        var value = TryTime(time.Text, out var typed) ? typed : reminder.Time; var hour = value.Hours; var minute = value.Minutes;
        var body = new StackPanel { Width = 276 }; body.Children.Add(Ui.Label("选择时间", 15)); var preview = Ui.Label("", 22, Ui.Accent); preview.Tag = "ReminderTimeCandidate"; body.Children.Add(preview);
        var options = new System.Collections.Generic.List<(Button Button, bool Hour, int Value)>();
        void Refresh() { preview.Text = $"{hour:00} : {minute:00}"; foreach (var option in options) { var selected = option.Value == (option.Hour ? hour : minute); option.Button.Background = selected ? Ui.Accent : Ui.Card; option.Button.Foreground = selected ? Ui.Brush("#102033") : Ui.Text; option.Button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal; } }
        void AddGrid(bool hours)
        {
            body.Children.Add(Ui.Label(hours ? "小时" : "分钟", 11, Ui.Muted)); var grid = new UniformGrid { Columns = 6 };
            foreach (var n in hours ? Enumerable.Range(0, 24) : Enumerable.Range(0, 12).Select(x => x * 5))
            {
                var number = n; var button = Ui.Button(n.ToString("00"), () => { if (hours) hour = number; else minute = number; Refresh(); });
                button.Tag = hours ? "ReminderHour" : "ReminderMinute"; button.DataContext = n; button.Padding = new Thickness(2, 5, 2, 5); button.MinWidth = 0;
                System.Windows.Automation.AutomationProperties.SetName(button, n.ToString("00") + (hours ? "时" : "分")); options.Add((button, hours, n)); grid.Children.Add(button);
            }
            body.Children.Add(grid);
        }
        AddGrid(true); AddGrid(false); body.Children.Add(Ui.Label("任意分钟也可直接在时间框输入", 11, Ui.Muted));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("取消", () => { timePopup.IsOpen = false; time.Focus(); }); cancel.Tag = "ReminderTimeCancel"; actions.Children.Add(cancel);
        var apply = Ui.Button("确定", () => { time.Text = $"{hour:00}:{minute:00}"; timePopup.IsOpen = false; Update(false); time.Focus(); }, true); apply.Tag = "ReminderTimeApply"; actions.Children.Add(apply); body.Children.Add(actions); Refresh();
        ShowPopup(timePopup, time, new ScrollViewer { Content = body, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, () => time.Focus());
        options.First(x => x.Hour && x.Value == hour).Button.Focus();
    }
    internal void ClosePopups() { timePopup.IsOpen = false; datePopup.IsOpen = false; }
}
