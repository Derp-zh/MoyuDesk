using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Taskboard.Desktop;

internal sealed class ConversationCapsuleGesture : IDisposable
{
    private readonly Button button;
    private readonly BoardController controller;
    private readonly Func<ConversationBindingDrag> begin;
    private readonly Action click;
    private Point start;
    private bool pressed;
    private ConversationBindingDrag? drag;
    internal ConversationCapsuleGesture(Button button, BoardController controller, Func<ConversationBindingDrag> begin, Action click)
    {
        this.button = button; this.controller = controller; this.begin = begin; this.click = click;
        // ButtonBase handles bubbling MouseMove while captured; preview must decide the drag first.
        button.PreviewMouseLeftButtonDown += Down; button.PreviewMouseLeftButtonUp += Up; button.PreviewMouseMove += Move;
        button.LostMouseCapture += Lost; button.PreviewMouseRightButtonDown += Right; button.PreviewKeyDown += Key;
        button.PreviewMouseWheel += Wheel;
    }
    private void Down(object sender, MouseButtonEventArgs e)
    { pressed = true; start = e.GetPosition(button); if (!button.CaptureMouse()) pressed = false; controller.TraceBindingGesture("capsule-down pressed=" + pressed); e.Handled = true; }
    private void Move(object sender, MouseEventArgs e)
    {
        if (pressed && e.LeftButton == MouseButtonState.Pressed && drag is null && (e.GetPosition(button) - start).Length >= 7)
        { controller.TraceBindingGesture("capsule-begin"); drag = begin(); controller.TraceBindingGesture("capsule-started pressed=" + pressed); }
        if (pressed) e.Handled = true;
    }
    private async void Up(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; if (!pressed) return;
        controller.TraceBindingGesture("capsule-up drag=" + (drag is not null));
        pressed = false; var finished = drag; drag = null; var inside = new Rect(button.RenderSize).Contains(e.GetPosition(button));
        var cursor = System.Windows.Forms.Cursor.Position; button.ReleaseMouseCapture();
        if (finished is null) { if (inside) click(); return; }
        try { await finished.Complete(new Point(cursor.X, cursor.Y)); }
        catch (Exception) { finished.Dispose(); Ui.Hint(button, "拖动未完成，请检查当前绑定及保存状态", !controller.Smoke); }
    }
    private void Lost(object sender, MouseEventArgs e) { controller.TraceBindingGesture("capsule-lost pressed=" + pressed); Cancel(); }
    private void Right(object sender, MouseButtonEventArgs e) { if (pressed || drag is not null) { Cancel(); e.Handled = true; } }
    private void Key(object sender, KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape && pressed) { Cancel(); e.Handled = true; } }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        if (drag is not null && controller.BindingWindow.ScrollAt(new Point(cursor.X, cursor.Y), e.Delta)) e.Handled = true;
    }
    internal void Cancel()
    { pressed = false; var old = drag; drag = null; old?.Dispose(); if (button.IsMouseCaptured) button.ReleaseMouseCapture(); }
    public void Dispose()
    {
        button.PreviewMouseLeftButtonDown -= Down; button.PreviewMouseLeftButtonUp -= Up; button.PreviewMouseMove -= Move;
        button.LostMouseCapture -= Lost; button.PreviewMouseRightButtonDown -= Right; button.PreviewKeyDown -= Key; button.PreviewMouseWheel -= Wheel;
        Cancel();
    }
}
