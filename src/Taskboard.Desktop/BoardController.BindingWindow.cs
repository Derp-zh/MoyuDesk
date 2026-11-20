namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private ConversationBindingWindow? bindingWindow;
    internal ConversationBindingWindow BindingWindow => bindingWindow ??= new(this);
}
