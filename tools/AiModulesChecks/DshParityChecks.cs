using System.IO;
using System.Windows;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class DshParityChecks
{
    internal static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var clipped = DshConversationTarget.ClipBounds(new Rect(10, 10, 100, 40), [new Rect(0, 30, 200, 100)]);
        Check(!clipped.Contains(new Point(20, 20)) && clipped.Contains(new Point(20, 35)), "DSH只允许行在滚动视口内的可见部分绑定，裁掉隐藏行覆盖的工具栏");
        Check(DshConversationTarget.ClipBounds(new Rect(10, -50, 100, 30), [new Rect(0, 20, 200, 100)]).IsEmpty, "DSH完全滚出视口的行不成为落点");
        Check(DshConversationTarget.ClipBounds(new Rect(0, 0, 200, 200), [new Rect(20, 20, 80, 80), new Rect(40, 0, 100, 70)]) == new Rect(40, 20, 60, 50), "DSH嵌套滚动视口与原窗口边界共同裁切");
        Check(DshConversationTarget.IsConversationRegion(["_727C8a_scrollBody", "_727C8a_body", "_727C8a_root"]), "DSH当前聊天须同时出现同一组件的内容与滚动区域");
        Check(!DshConversationTarget.IsConversationRegion(["dialog_body", "workspace_root"]), "其他设置弹窗的body类不冒充聊天区域");
        Check(!DshConversationTarget.IsConversationRegion(["modal_body", "chat_scrollBody"]), "不同组件的body与scrollBody不能拼接成聊天来源");
        foreach (var id in new[] { "session-native-one", "4313f30e-aa45-4c79-9821-bbe4a6e27161", "native_2" })
            Check(DshConversationTarget.ParseSessionId(id) == id, "DSH接受准确原生编号：" + id);
        foreach (var id in new[] { "", "用户会话标题", " native", "native\n", "codex://threads/id", "https://host/id", "../native" })
            Check(DshConversationTarget.ParseSessionId(id) is null, "DSH不把名称/其他链接/路径当编号：" + id.Trim());
        var clipboard = new Clipboard(); var menu = new Menu(clipboard);
        var target = ConversationLinkResolver.Run(menu, clipboard, CancellationToken.None, _ => { });
        Check(target is { CanBind: true, Platform: AiPlatform.Dsh, ThreadId: "session-native-one", ClipboardRestored: true }, "DSH原菜单事务取得原生ID并保留平台身份及剪贴板恢复");
        Check(menu.Invokes == 1 && clipboard.Text == "original" && menu.Selected == "复制 Session ID", "DSH只调用一次准确复制ID动作，不复制正文或误触归档");
        var english = new Clipboard(); var englishMenu = new Menu(english) { English = true };
        Check(ConversationLinkResolver.Run(englishMenu, english, CancellationToken.None, _ => { }).CanBind, "DSH英文Copy Session ID菜单同样可用");
        var foreign = new Clipboard { Foreign = true }; var foreignMenu = new Menu(foreign);
        Check(!ConversationLinkResolver.Run(foreignMenu, foreign, CancellationToken.None, _ => { }).CanBind && foreign.Text == "session-native-one", "外部剪贴板变化拒绝绑定且不覆盖外部新内容");
        var changed = new Clipboard(); var changedMenu = new Menu(changed) { ChangeOnOpen = true };
        Check(!ConversationLinkResolver.Run(changedMenu, changed, CancellationToken.None, _ => { }).CanBind && changedMenu.Invokes == 0 && changed.Text == "original", "DSH菜单期间会话或目标作用域变化，不得复制或沿用旧编号");
        var codexClipboard = new Clipboard { CopyText = "codex://threads/4313f30e-aa45-4c79-9821-bbe4a6e27161" };
        var codexMenu = new Menu(codexClipboard) { Platform = AiPlatform.Codex };
        Check(ConversationLinkResolver.Run(codexMenu, codexClipboard, CancellationToken.None, _ => { }) is { Platform: AiPlatform.Codex, ThreadId: "4313f30e-aa45-4c79-9821-bbe4a6e27161", ClipboardRestored: true } && codexMenu.Invokes == 1,
            "共用事务保持原Codex深度链接取号和一次复制/恢复行为");
        foreach (var platform in new[] { AiPlatform.Codex, AiPlatform.Dsh })
        {
            var duplicate = new Clipboard(); var duplicateMenu = new Menu(duplicate) { Platform = platform, Duplicate = true };
            Check(!ConversationLinkResolver.Run(duplicateMenu, duplicate, CancellationToken.None, _ => { }).CanBind && duplicateMenu.Invokes == 0,
                platform + "存在多个复制身份菜单时拒绝猜选，不调用任何一项");
        }
        var partial = new Clipboard(); var partialMenu = new Menu(partial) { ThrowAfterCopy = true };
        Check(!ConversationLinkResolver.Run(partialMenu, partial, CancellationToken.None, _ => { }).CanBind && partialMenu.Invokes == 1 && partial.Text == "original",
            "DSH复制已生效后菜单异常，也恢复本次剪贴板且不二次复制");
        using var duringCopy = new CancellationTokenSource(); var copyCancelled = new Clipboard();
        var copyCancelledMenu = new Menu(copyCancelled) { AfterCopy = duringCopy.Cancel };
        Check(!ConversationLinkResolver.Run(copyCancelledMenu, copyCancelled, duringCopy.Token, _ => { }).CanBind && copyCancelled.Text == "original",
            "DSH复制过程中取消仍恢复本次剪贴板并拒绝提交");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = new Clipboard(); var cancelledMenu = new Menu(cancelled);
        Check(!ConversationLinkResolver.Run(cancelledMenu, cancelled, cancellation.Token, _ => { }).CanBind && cancelledMenu.Invokes == 0, "DSH取消时不打开复制流程、不改变绑定");

        using var controller = new BoardController(Path.Combine(root, "dsh-parity-board"), true, true);
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "DSH功能对齐";
        var first = session.Working.Stages[0]; first.Codex = new() { Platform = AiPlatform.Codex, ThreadId = "4313f30e-aa45-4c79-9821-bbe4a6e27161", AwaitingUserAction = true, PendingRequests = ["old"] };
        var second = controller.Engine.AddStage(session); second.IsRunning = true;
        second.Codex = new() { Platform = AiPlatform.Dsh, ThreadId = "other-dsh", AwaitingUserAction = true, Phase = CodexPhase.WaitingForApproval };
        var item = controller.Engine.Submit(session); var before = item.Stages[0].Codex!;
        var result = await controller.CompleteConversationBinding(item.Id, first.Id, before.BindingId, target);
        var saved = controller.Store.Load().Tasks.Single(x => x.Id == item.Id);
        Check(result.StartsWith("已绑定") && saved.Stages[0].Codex is { Platform: AiPlatform.Dsh, ThreadId: "session-native-one", AwaitingUserAction: false }, "DSH原菜单结果经真实Controller绑定与Flush独立落盘，跨平台旧保护重置");
        Check(item.Stages[1].Codex!.AwaitingUserAction && saved.Stages[1].Codex!.ThreadId == "other-dsh", "DSH改绑不改变另一运行阶段的等待与编号");
        var stale = await controller.CompleteConversationBinding(item.Id, first.Id, before.BindingId, target with { ThreadId = "wrong-late" });
        Check(stale.StartsWith("未绑定") && item.Stages[0].Codex!.ThreadId == "session-native-one", "迟到DSH拖动不得覆盖新绑定");
        var environment = new EnvironmentFixture(Path.Combine(root, "retry-home"));
        controller.SetAiEnvironmentForChecks(AiPlatform.Dsh, environment);
        controller.AiModule(AiPlatform.Dsh).Report = new(AiConnectionState.MissingRuntime, "读取组件暂缺", "等待自动发现");
        controller.StartOtherAiRecovery(TimeSpan.FromMilliseconds(50));
        var until = DateTime.UtcNow.AddSeconds(2);
        while (controller.AiModule(AiPlatform.Dsh).Report.State != AiConnectionState.Ready && DateTime.UtcNow < until) await Task.Delay(20);
        Check(environment.Probes > 0 && controller.AiModule(AiPlatform.Dsh).Report.State == AiConnectionState.Ready, "DSH晚启动或缺组件恢复后后台自动重检，无需再次点初始化");
        var initCount = environment.Initializations;
        await controller.ReconnectOtherAi(AiPlatform.Dsh);
        Check(controller.AiModule(AiPlatform.Dsh).Report.State == AiConnectionState.Ready && environment.Initializations == initCount, "DSH独立重新连接不触发外部初始化或重启其他平台");
    }
    private sealed class Clipboard : IConversationLinkClipboard
    {
        internal string Text = "original", CopyText = "session-native-one"; internal bool Foreign; public uint Sequence { get; private set; } = 10;
        public uint Capture() => Sequence;
        internal void Copy() { Sequence++; Text = CopyText; }
        public ConversationClipboardRead Read() => new(Sequence, Text, !Foreign);
        public bool Restore(uint expected) { if (Sequence != expected || Foreign) return false; Text = "original"; Sequence++; return true; }
        public void Dispose() { }
    }
    private sealed class Menu(Clipboard clipboard) : IConversationLinkMenu
    {
        private bool open; internal bool English, ChangeOnOpen, Duplicate, ThrowAfterCopy; internal int Invokes; internal string Selected = "";
        internal AiPlatform Platform = AiPlatform.Dsh; internal Action? AfterCopy;
        public ConversationDropTarget Target => new(null, "准确目标行", "", true, Platform == AiPlatform.Dsh ? "dsh-element-menu" : "codex-element-menu", Platform: Platform);
        public bool Valid => !(open && ChangeOnOpen); public bool ForegroundOwned => true;
        public ConversationMenuCommand[] Commands()
        {
            if (!open) return [];
            var label = Platform == AiPlatform.Dsh ? English ? "Copy Session ID" : "复制 Session ID" : "复制深度链接";
            return Duplicate ? [new("copy", label), new("other-copy", label)] : [new("copy", label), new("archive", "归档会话"), new("body", "复制正文")];
        }
        public void Open() => open = true;
        public void Expand(ConversationMenuCommand command) => throw new Exception("不应展开子菜单");
        public void Invoke(ConversationMenuCommand command) { Invokes++; Selected = command.Name; clipboard.Copy(); AfterCopy?.Invoke(); if (ThrowAfterCopy) throw new InvalidOperationException("fixture-after-copy"); }
        public void CloseOwnedMenu() => open = false;
        public void Dispose() { }
    }
    private sealed class EnvironmentFixture(string home) : IAiListeningEnvironment
    {
        internal int Probes, Initializations;
        public Task<AiConnectionProbe> Probe(AiPlatformOptions value, CancellationToken cancellation)
        { Probes++; return Task.FromResult(new AiConnectionProbe(new(AiPlatform.Dsh, "fixture-dsh.exe", home, "fixture-node.exe", ""), new(AiConnectionState.Ready, "就绪", "", Installed: true, Configured: true, Catalog: true))); }
        public void Initialize(AiPlatformOptions value) => Initializations++;
        public bool Running(string executable) => true;
        public void Open(string executable) => throw new Exception("不能打开真实应用");
    }
}
