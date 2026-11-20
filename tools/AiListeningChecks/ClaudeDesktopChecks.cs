using System.IO;
using System.Text.Json;
using Taskboard.Desktop;

internal static class ClaudeDesktopChecks
{
    internal static async Task Observe(string root, Action<bool, string> check)
    {
        var home = Path.Combine(root, "stable-desktop");
        var org = Path.Combine(home, "claude-code-sessions", "account", "org");
        Directory.CreateDirectory(org);
        const string desktop = "local_stable";
        var events = Path.Combine(root, "stable-events");
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        void Metadata(string engine) => File.WriteAllText(Path.Combine(org, desktop + ".json"), JsonSerializer.Serialize(new { sessionId = desktop, cliSessionId = engine, title = "稳定绑定" }));
        var binding = new Taskboard.Core.CodexBinding { Platform = Taskboard.Core.AiPlatform.ClaudeCode, ThreadId = desktop, BoundAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        var stage = new Taskboard.Core.Stage { IsRunning = true, Codex = binding };
        var item = new Taskboard.Core.WorkItem { Title = "稳定身份", Lane = Taskboard.Core.Lane.Running, Stages = [stage], CurrentStageId = stage.Id };
        Metadata(first);
        using var observer = new AiPlatformObserver(events, onlyPlatform: Taskboard.Core.AiPlatform.ClaudeCode, claudeDesktopRoot: home);
        observer.Changed += (_, id, bind, generation, value) => { lock (item) Taskboard.Core.AiState.Apply(item, id, bind, generation, value); };
        observer.Watch([item]); observer.Start();
        async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until) { lock (item) if (condition()) return; await Task.Delay(40); }
            throw new Exception("桌面稳定身份观察超时：" + observer.Status);
        }
        void Emit(string engine, string kind, string request = "", string tool = "") => ClaudeHook.Write(events, new(engine, "", 0, DateTime.UtcNow, kind, engine, request, tool));
        Emit(first, "UserPromptSubmit"); Emit(first, "PreToolUse", "question", "AskUserQuestion");
        await Wait(() => binding.Phase == Taskboard.Core.CodexPhase.WaitingForInput);
        check(item.Lane == Taskboard.Core.Lane.Action, "桌面稳定ID经真实观察器接收执行ID提问并移列");
        Metadata(second);
        await Wait(() => binding.Phase == Taskboard.Core.CodexPhase.Unknown);
        check(binding.AwaitingUserAction && item.Lane == Taskboard.Core.Lane.Action, "底层执行ID变化且尚无新事件时保留等待");
        Emit(first, "Stop");
        await Task.Delay(900);
        check(binding.Phase == Taskboard.Core.CodexPhase.Unknown, "旧执行ID迟到终态不再作用于当前桌面绑定");
        Emit(second, "UserPromptSubmit");
        await Wait(() => binding.Phase == Taskboard.Core.CodexPhase.Running && item.Lane == Taskboard.Core.Lane.Running);
        check(binding.ThreadId == desktop, "新执行源真实开始恢复运行且桌面绑定身份保持");
        Emit(second, "Stop");
        await Wait(() => binding.Phase == Taskboard.Core.CodexPhase.ReadyForReview);
        var state = new Taskboard.Core.BoardState(); state.Tasks.Add(item);
        var store = new Taskboard.Core.AtomicStore(Path.Combine(root, "stable-board"));
        await store.SaveAsync(state);
        check(store.Load().Tasks.Single().Stages.Single().Codex?.ThreadId == desktop, "稳定桌面身份及结束状态落盘回读");
    }
    internal static void Run(string root, Action<bool, string> check)
    {
        var home = Path.Combine(root, "desktop-catalog");
        var org = Path.Combine(home, "claude-code-sessions", "account", "org");
        Directory.CreateDirectory(org);
        var engine = Guid.NewGuid().ToString();
        void Row(string name, object value) => File.WriteAllText(Path.Combine(org, name + ".json"), JsonSerializer.Serialize(value));
        Row("local_desktop", new { sessionId = "local_desktop", cliSessionId = engine, title = "桌面 Code 标题", isRunning = true });
        var rows = ClaudeDesktopCatalog.Load(home);
        check(rows is [{ DesktopId: "local_desktop", Title: "桌面 Code 标题" }] && rows[0].EngineId == engine, "桌面ID映射到Hook执行ID且保留标题");
        check(Taskboard.Core.AiState.NormalizeSessionId(Taskboard.Core.AiPlatform.ClaudeCode, "local_desktop") == "local_desktop" &&
            Taskboard.Core.AiState.NormalizeSessionId(Taskboard.Core.AiPlatform.Codex, "local_desktop") is null, "桌面身份只对Claude有效，Codex约束保持");
        check(ClaudeDesktopCatalog.ResolveEngine("local_desktop", rows) == engine, "稳定桌面ID精确解析当前执行ID");
        var incoming = new AiRawEvent(engine, "epoch", 0, DateTime.UtcNow, "UserPromptSubmit");
        var mapped = ClaudeDesktopCatalog.MapEvent(incoming, "local_desktop", engine);
        check(mapped.SessionId == "local_desktop" && mapped.Epoch.StartsWith(engine + ":"), "事件映射保留独立执行来源代次");
        var catalog = ClaudeCatalog.Load(Path.Combine(root, "absent-cli"), Path.Combine(root, "absent-events"), home);
        check(catalog.Length == 1 && catalog[0].Id == "local_desktop", "选择器绑定稳定桌面ID，无需CLI历史目录");
        Row("local_remote", new { sessionId = "local_remote", cliSessionId = Guid.NewGuid().ToString(), sshConfig = new { host = "example" } });
        Row("local_wsl", new { sessionId = "local_wsl", cliSessionId = Guid.NewGuid().ToString(), wslConfig = new { distro = "test" } });
        Row("local_cloud", new { sessionId = "local_cloud", cliSessionId = Guid.NewGuid().ToString(), movedToCloud = new { cloudSessionId = "cloud_source" } });
        Row("local_archived", new { sessionId = "local_archived", cliSessionId = Guid.NewGuid().ToString(), isArchived = true });
        Row("local_wrong", new { sessionId = "local_different", cliSessionId = Guid.NewGuid().ToString() });
        Row("local_no_engine", new { sessionId = "local_no_engine", title = "尚未启动" });
        File.WriteAllText(Path.Combine(org, "local_broken.json"), "{");
        check(ClaudeDesktopCatalog.Load(home).Length == 1, "SSH/WSL/云端/归档/缺ID/错误身份/损坏文件均不冒充本机可监听会话");
        Directory.CreateDirectory(Path.Combine(org, "waiting-input"));
        File.WriteAllText(Path.Combine(org, "waiting-input", "secret.json"), "BODY_MUST_NOT_BE_PARSED");
        check(ClaudeDesktopCatalog.Load(home).Length == 1, "不扫描waiting-input正文及子目录");
        Row("local_duplicate", new { sessionId = "local_duplicate", cliSessionId = engine, title = "重复映射" });
        check(ClaudeDesktopCatalog.Load(home).Length == 0, "同执行ID映射多个桌面行时拒绝猜选");
        File.Delete(Path.Combine(org, "local_duplicate.json"));
        File.Delete(Path.Combine(org, "local_desktop.json"));
        check(ClaudeDesktopCatalog.Load(home).Length == 0, "删除或账号目录变化不使用旧目录缓存");
        Row("local_empty", new { sessionId = "local_empty", cliSessionId = Guid.Empty.ToString() });
        Row("local_", new { sessionId = "local_", cliSessionId = Guid.NewGuid().ToString() });
        check(ClaudeDesktopCatalog.Load(home).Length == 0, "空执行GUID及非法桌面ID不会进入可绑定目录");
        File.Delete(Path.Combine(org, "local_empty.json"));File.Delete(Path.Combine(org, "local_.json"));
        var external = Path.Combine(root, "linked-catalog-target");Directory.CreateDirectory(external);
        var targetOrg = Path.Combine(external, "account", "org");Directory.CreateDirectory(targetOrg);
        File.WriteAllText(Path.Combine(targetOrg,"local_link.json"),JsonSerializer.Serialize(new{sessionId="local_link",cliSessionId=Guid.NewGuid().ToString()}));
        var linkedHome=Path.Combine(root,"linked-catalog-home");Directory.CreateDirectory(linkedHome);
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"/c","mklink","/J",Path.Combine(linkedHome,"claude-code-sessions"),external})start.ArgumentList.Add(arg);
        using(var process=System.Diagnostics.Process.Start(start)!){process.WaitForExit();if(process.ExitCode!=0)throw new IOException("验收隔离junction创建失败："+process.StandardError.ReadToEnd());}
        check(ClaudeDesktopCatalog.Load(linkedHome).Length==0,"不穿越会话目录根junction读取其他位置");
        // #03 超过 2000 文件时保留已读有效会话而不全清空，以及单匿名 PermissionRequest/Notification 结清
        var manyHome = Path.Combine(root, "many-desktop");
        var manyOrg = Path.Combine(manyHome, "claude-code-sessions", "acc", "org");
        Directory.CreateDirectory(manyOrg);
        for (var i = 0; i < 2005; i++)
        {
            var sid = $"local_item_{i:D4}";
            File.WriteAllText(Path.Combine(manyOrg, sid + ".json"), JsonSerializer.Serialize(new { sessionId = sid, cliSessionId = Guid.NewGuid().ToString(), title = "会话" + i }));
        }
        var capped = ClaudeDesktopCatalog.Load(manyHome);
        check(capped.Length == 2000, "超过2000个桌面会话文件时按上限保留有效会话而非清空全部");
        var permOnly = new AiEventState(Taskboard.Core.AiPlatform.ClaudeCode, "local_perm");
        permOnly.Apply(new("local_perm", "e1", 0, DateTime.UtcNow, "UserPromptSubmit", "t1"));
        permOnly.Apply(new("local_perm", "e1", 1, DateTime.UtcNow, "PermissionRequest", "t1", "", "Bash"));
        check(permOnly.Snapshot.Pending is [{ Approval: true }], "未收到PreToolUse的匿名权限请求正常进入等待批准");
        permOnly.Apply(new("local_perm", "e1", 2, DateTime.UtcNow, "PostToolUse", "t1", "tool-bash", "Bash"));
        check(permOnly.Snapshot.Pending.Length == 0 && permOnly.Snapshot.Executing, "单一同名匿名权限请求在工具完成后可精准结清并恢复执行");
        var noticeOnly = new AiEventState(Taskboard.Core.AiPlatform.ClaudeCode, "local_notice");
        noticeOnly.Apply(new("local_notice", "e1", 0, DateTime.UtcNow, "UserPromptSubmit", "t1"));
        noticeOnly.Apply(new("local_notice", "e1", 1, DateTime.UtcNow, "Notification", "t1", "", "", "", "permission_prompt"));
        check(noticeOnly.Snapshot.Pending is [{ Approval: true }], "Notification权限提示回退进入等待批准");
        noticeOnly.Apply(new("local_notice", "e1", 2, DateTime.UtcNow, "PostToolUse", "t1", "tool-read", "Read"));
        check(noticeOnly.Snapshot.Pending.Length == 0 && noticeOnly.Snapshot.Executing, "单一Notification回退提示在后续工具完成后可精准结清");
    }
}
