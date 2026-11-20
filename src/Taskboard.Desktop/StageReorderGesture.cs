using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// Reordering is a transaction: only a completed in-viewport drop changes the
// editor's working copy. Text controls and binding pickers stay alive in flight.
internal sealed class StageReorderGesture
{
    private static readonly Geometry ReorderGlyph = CreateReorderGlyph();
    private static Geometry CreateReorderGlyph()
    {
        var raw = Geometry.Parse("M3,5 L6,2 L9,5 M6,2 L6,14 M3,11 L6,14 L9,11");
        var pen = new Pen(Brushes.White, 2.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var unified = raw.GetWidenedPathGeometry(pen).GetOutlinedPathGeometry();
        unified.Freeze();
        return unified;
    }

    internal static Thumb Create(StackPanel stages, Border source, Func<bool> enabled, Action<int> commit)
    {
        var handle = new Thumb { Width = 24, Height = 24, Margin = new Thickness(0, 3, 4, 3),
            Cursor = Cursors.SizeNS, ToolTip = "按住拖动调整阶段顺序", Tag = "StageReorder", Focusable = true };
        var circle = new FrameworkElementFactory(typeof(Border));
        circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        circle.SetValue(Border.BackgroundProperty, Ui.Card); circle.SetValue(Border.BorderBrushProperty, Ui.Line);
        circle.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        circle.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        circle.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);
        var label = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        label.SetValue(System.Windows.Shapes.Path.DataProperty, ReorderGlyph);
        label.SetValue(System.Windows.Shapes.Path.FillProperty, Ui.Text);
        label.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.None);
        label.SetValue(FrameworkElement.WidthProperty, 12.0); label.SetValue(FrameworkElement.HeightProperty, 16.0);
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        circle.AppendChild(label); handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = circle };
        System.Windows.Automation.AutomationProperties.SetName(handle, "拖动调整阶段顺序");
        var index = -1; var active = false; var originalOpacity = source.Opacity;
        Border[] originals = [];
        Transform[] transforms = [];
        StagePreview? ghost = null;
        var grab = new Point();
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
        ConversationBindingInput? escape = null;
        void Preview()
        {
            if (!active) return;
            var point = Mouse.GetPosition(stages);
            var others = originals.Where(x => !ReferenceEquals(x, source)).ToList();
            double top = 0; index = 0;
            foreach (var frame in others)
            {
                var height = frame.ActualHeight + frame.Margin.Top + frame.Margin.Bottom;
                if (point.Y - grab.Y > top + height / 2) index++;
                top += height;
            }
            others.Insert(index, source); top = 0;
            foreach (var frame in others)
            {
                var offset = VisualTreeHelper.GetOffset(frame);
                frame.RenderTransform = new TranslateTransform(0, top + frame.Margin.Top - offset.Y);
                top += frame.ActualHeight + frame.Margin.Top + frame.Margin.Bottom;
            }
            ghost?.Move(stages.PointToScreen(new Point(point.X - grab.X, point.Y - grab.Y)));
        }
        void Scroll()
        {
            for (DependencyObject? node = stages; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollViewer scroll)
                {
                    var y = Mouse.GetPosition(scroll).Y;
                    if (y < 24) scroll.LineUp(); else if (y > scroll.ActualHeight - 24) scroll.LineDown();
                    break;
                }
            Preview();
        }
        timer.Tick += (_, _) => Scroll();
        handle.DragStarted += (_, _) =>
        {
            if (!enabled()) { handle.CancelDrag(); return; }
            try { escape = new ConversationBindingInput(observeMouse: false, includeInjectedEscape: true); }
            catch (InvalidOperationException) { handle.CancelDrag(); return; }
            handle.Focus();
            stages.UpdateLayout(); grab = Mouse.GetPosition(source);
            originals = stages.Children.OfType<Border>().ToArray(); transforms = originals.Select(x => x.RenderTransform).ToArray();
            ghost = new StagePreview(source);
            active = true; originalOpacity = source.Opacity; source.Opacity = .2; Preview(); timer.Start();
        };
        handle.DragDelta += (_, _) =>
        {
            if (active) Scroll();
        };
        handle.DragCompleted += (_, e) =>
        {
            if (!active) return; active = false; timer.Stop();
            ghost?.Dispose(); ghost = null;
            for (var i = 0; i < originals.Length; i++) originals[i].RenderTransform = transforms[i];
            source.Opacity = originalOpacity;
            var cancelled = e.Canceled || escape?.Token.IsCancellationRequested == true;
            escape?.Dispose(); escape = null;
            if (!cancelled && enabled() && new Rect(stages.RenderSize).Contains(Mouse.GetPosition(stages)) && index >= 0) commit(index);
        };
        handle.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && active) { handle.CancelDrag(); e.Handled = true; } };
        handle.PreviewMouseRightButtonDown += (_, e) => { if (active) { handle.CancelDrag(); e.Handled = true; } };
        handle.Unloaded += (_, _) => { if (active) handle.CancelDrag(); };
        return handle;
    }
    private sealed class StagePreview : IDisposable
    {
        private readonly Window window;
        private readonly int width, height;
        private IntPtr handle;
        internal StagePreview(FrameworkElement source)
        {
            var size = source.RenderSize;
            var dpi = VisualTreeHelper.GetDpi(source);
            width = Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX));
            height = Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Use the brush's default content bounds to normalize the stack
            // offset. An absolute local viewbox clips later stages to blank.
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(source) { Stretch = Stretch.Fill }, null, new Rect(size));
            bitmap.Render(visual); bitmap.Freeze();
            // Match the existing task-card drag: a separate click-through,
            // non-activating window stays visible above the native content host.
            window = new Window { Width = size.Width, Height = size.Height, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true, IsHitTestVisible = false,
                Title = "阶段拖动预览", Left = -18000, Top = -18000,
                Content = new Image { Source = bitmap, Stretch = Stretch.Fill, Opacity = .85 } };
            window.SourceInitialized += (_, _) =>
            {
                handle = new WindowInteropHelper(window).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
            };
            window.Show();
        }
        internal void Move(Point point) => Native.SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(point.X), (int)Math.Round(point.Y), width, height, 0x0010 | 0x0200);
        public void Dispose() => window.Close();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    }
}
