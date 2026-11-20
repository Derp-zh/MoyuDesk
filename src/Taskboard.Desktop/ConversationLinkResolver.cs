using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record ConversationMenuCommand(object Handle, string Name);
internal readonly record struct ConversationClipboardRead(uint Sequence, string Text, bool SourceMatches);

// The native adapter and the deterministic replay use this same transaction.
// An element identifies the target; the pointer is never a source of identity here.
internal interface IConversationLinkMenu : IDisposable
{
    ConversationDropTarget Target { get; }
    bool Valid { get; }
    bool ForegroundOwned { get; }
    ConversationMenuCommand[] Commands();
    void Open();
    void Expand(ConversationMenuCommand command);
    void Invoke(ConversationMenuCommand command);
    void CloseOwnedMenu();
}
internal interface IConversationLinkClipboard : IDisposable
{
    uint Sequence { get; }
    uint Capture();
    ConversationClipboardRead Read();
    bool Restore(uint expectedSequence);
}

internal static class ConversationLinkResolver
{
    internal static string LastFailure { get; private set; } = "";
    internal static ConversationDropTarget Run(IConversationLinkMenu menu, IConversationLinkClipboard clipboard,
        CancellationToken cancellation, Action<int>? delay = null)
    {
        delay ??= Thread.Sleep;
        LastFailure = "";
        uint original = 0, owned = 0;
        var invoked = false;
        bool? restored = null;
        var platform = menu.Target.Platform; var platformName = AiState.Name(platform);
        bool IdentityCommand(string name) => platform == AiPlatform.Dsh ? name is "复制 Session ID" or "Copy Session ID" : CodexConversationMenu.IsDeeplinkAction(name);
        var result = ConversationDropTarget.Unavailable("暂时没找到 " + platformName + " 这个会话的链接入口（可能是客户端刚更新界面或该区域暂不支持），原绑定已为您保留。建议直接在右侧绑定窗口搜索并点击绑定");
        var clock = Stopwatch.StartNew();
        void Check(bool foreground = true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.ElapsedMilliseconds > 4000) throw new TimeoutException();
            if (!menu.Valid || foreground && !menu.ForegroundOwned)
                throw new InvalidOperationException("目标会话或前台已变化");
        }
        ConversationMenuCommand[] Wait(Func<ConversationMenuCommand[], bool> ready)
        {
            var wait = Stopwatch.StartNew();
            do
            {
                Check();
                var commands = menu.Commands();
                if (ready(commands)) return commands;
                delay(20);
            } while (wait.ElapsedMilliseconds < 1200);
            return [];
        }
        ConversationDropTarget Execute()
        {
            Check(false);
            if (menu.Commands().Length != 0)
                return ConversationDropTarget.Unavailable(platformName + " 客户端当前正打开着菜单，原绑定已保留；请先点击空白处关闭菜单后再重新拖动");
            original = clipboard.Capture();
            Check(false);
            menu.Open();
            var commands = Wait(items => items.Length > 0);
            var links = commands.Where(item => IdentityCommand(item.Name)).ToArray();
            if (links.Length == 0)
            {
                var copy = commands.Where(item => item.Name is "复制" or "Copy").ToArray();
                if (copy.Length != 1) return result;
                Check(); menu.Expand(copy[0]);
                commands = Wait(items => items.Any(item => IdentityCommand(item.Name)));
                links = commands.Where(item => IdentityCommand(item.Name)).ToArray();
            }
            if (links.Length != 1) return result;
            Check();
            if (clipboard.Sequence != original)
                return ConversationDropTarget.Unavailable("拖动期间检测到您刚复制了新内容，为保护剪贴板已停止读取，原绑定保留；请稍后再拖一次");
            // Mark before calling: an Invoke may act and then throw or return late.
            // Never invoke it a second time, including after a timeout.
            invoked = true; menu.Invoke(links[0]);
            var copyWait = Stopwatch.StartNew();
            do
            {
                Check();
                var read = clipboard.Read();
                if (read.Sequence != original)
                {
                    if (!read.SourceMatches) return ConversationDropTarget.Unavailable("剪贴板内容被其他程序改写，为保护您的数据已暂停读取，原绑定保留；请重新拖动试试");
                    owned = read.Sequence;
                    var id = platform == AiPlatform.Dsh ? DshConversationTarget.ParseSessionId(read.Text) : CodexConversationDropTarget.ParseThreadUri(read.Text);
                    if (id is null) return ConversationDropTarget.Unavailable(platformName + " 未返回有效的本机会话链接（可能会话还在新建或暂不支持直连），原绑定已保留；建议改用右侧绑定窗口选择");
                    Check();
                    result = new(id, menu.Target.Name, "已从原 " + platformName + " 会话定向菜单取得编号", false, menu.Target.Method, Platform: platform);
                    break;
                }
                delay(10);
            } while (copyWait.ElapsedMilliseconds < 1000);
            return result;
        }
        try { result = Execute(); }
        catch (OperationCanceledException) { LastFailure = "cancelled"; result = ConversationDropTarget.Unavailable("已取消本次拖动，原绑定已为您保留"); }
        catch (TimeoutException error)
        { LastFailure = error.GetType().Name + ":" + error.Message; result = ConversationDropTarget.Unavailable("读取 " + platformName + " 会话等待超时（可能是会话正在加载或客户端响应较慢），原绑定已为您保留。建议稍等片刻再拖，或改用右侧绑定窗口点击选择"); }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException
            or ArgumentException or System.ComponentModel.Win32Exception)
        { LastFailure = error.GetType().Name + ":" + error.Message; result = ConversationDropTarget.Unavailable("这次没能顺利读取到 " + platformName + " 目标会话（可能是会话加载中、鼠标切换过快或客户端版本更新所致），原绑定已为您保留。建议停顿片刻后再拖一次，或直接在右侧绑定窗口点击选择"); }
        finally
        {
            // Recover even when Invoke acted before throwing/cancellation. A newer
            // copy owned by another application must never be overwritten.
            try
            {
                if (invoked && owned == 0)
                {
                    var read = clipboard.Read();
                    if (read.Sequence != original && read.SourceMatches) owned = read.Sequence;
                }
                if (owned != 0) restored = clipboard.Restore(owned);
            }
            catch (Exception) { restored = false; }
            try { menu.CloseOwnedMenu(); } catch (Exception) { /* No speculative Escape or focus recovery. */ }
        }
        if (cancellation.IsCancellationRequested) result = ConversationDropTarget.Unavailable("操作已取消，原绑定保留");
        if (restored == false && !result.CanBind)
            result = result with { Reason = result.Reason + "；剪贴板未确认恢复，已有新内容时不会覆盖" };
        return result with { ClipboardRestored = restored };
    }
}
