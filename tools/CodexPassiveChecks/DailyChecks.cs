using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class DailyChecks
{
    private sealed class Records : ICodexActivitySource
    {
        public IReadOnlyDictionary<string,CodexActivity> Read(IReadOnlyCollection<string> ids) => ids.ToDictionary(id=>id,
            _=>new CodexActivity("daily-turn", "inProgress", 1, true, 0, 0, 0, DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds()));
    }
    internal static async Task<bool> Run(string root)
    {
        var checks = new List<string>(); string? failure = null;
        try
        {
            var path = Path.Combine(root,"already-waiting.log");
            await File.WriteAllTextAsync(path,DateTimeOffset.UtcNow.AddSeconds(-30).ToString("O")+
                " info [electron-message-handler] [desktop-notifications] received approval conversationId=daily-chat kind=commandExecution requestId=17\n");
            var item = new WorkItem { Title="先有批准等待、后开启看板", Lane=Lane.Running };
            item.Stages[0].Codex=new(){ThreadId="daily-chat"};
            var source=new CodexBehaviorSource(()=>[path],0);
            await using var observer=new CodexObserver(behaviorEnabled:true,behaviorSource:source,activitySource:()=>new Records());
            var watch=new CodexWatch(item.Id,item.Stages[0].Id,item.CurrentCodex!.BindingId,"daily-chat",1);
            observer.Event+=(w,m,s,e,l)=>CodexState.Apply(item,w.StageId,m,s,w.Generation,e,l);
            observer.Watch([watch]); observer.Start();
            var deadline=DateTime.UtcNow.AddSeconds(4);
            while(item.CurrentCodex!.Phase!=CodexPhase.WaitingForApproval && DateTime.UtcNow<deadline) await Task.Delay(25);
            if(item.Lane!=Lane.Action || item.CurrentCodex!.Phase!=CodexPhase.WaitingForApproval)
                throw new Exception($"先有批准等待、后开启看板：应为待操作／等待批准，实际{item.Lane}／{item.CurrentCodex!.Phase}");
            checks.Add("先有批准等待、后开启看板自动进入待操作");
            await Additional(root,checks);
            await SavedTerminal(root,checks);
            await PipelineGaps(root,checks);
        }
        catch(Exception error){failure=error.Message;}
        var passed=failure is null;
        await File.WriteAllTextAsync(Path.Combine(root,"daily-report.json"),JsonSerializer.Serialize(new {passed,checks,failure,fixture=true,originalDesktop=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{passed,checks=checks.Count,failure}));return passed;
    }
    private sealed class MutableRecords : ICodexActivitySource
    {
        internal CodexActivity Value = new("saved-turn","completed",7,true,0,0);
        public IReadOnlyDictionary<string,CodexActivity> Read(IReadOnlyCollection<string> ids)=>ids.ToDictionary(x=>x,_=>Value);
    }
    private static async Task SavedTerminal(string root,List<string> checks)
    {
        var item=new WorkItem{Lane=Lane.Action,TimerUnread=true};
        item.Stages[0].Codex=new(){ThreadId="saved-chat",TurnId="saved-turn",AwaitingUserAction=true,Phase=CodexPhase.WaitingForInput};
        var records=new MutableRecords();
        await using var observer=new CodexObserver(activitySource:()=>records);
        var watch=new CodexWatch(item.Id,item.Stages[0].Id,item.CurrentCodex!.BindingId,"saved-chat",1);
        observer.Event+=(w,m,s,e,l)=>CodexState.Apply(item,w.StageId,m,s,w.Generation,e,l);
        observer.Watch([watch],new HashSet<Guid>{watch.BindingId},new Dictionary<Guid,string>{{watch.BindingId,"saved-turn"}});observer.Start();
        var end=DateTime.UtcNow.AddSeconds(3);
        while(item.CurrentCodex!.Phase!=CodexPhase.ReadyForReview&&DateTime.UtcNow<end)await Task.Delay(20);
        if(item.CurrentCodex!.Phase!=CodexPhase.ReadyForReview||item.Lane!=Lane.Action||!item.TimerUnread)
            throw new Exception("看板关闭期间原等待轮次结束，重开后必须补齐准确终态且保留提醒红点");
        checks.Add("看板关闭期间原等待轮次已结束，重开补齐准确终态并保留红点");
        await observer.DisposeAsync();
        foreach(var mode in new[]{"different-turn","new-binding"})
        {
            var other=new WorkItem{Lane=Lane.Running,TimerUnread=true};
            other.Stages[0].Codex=new(){ThreadId="saved-chat",TurnId=mode=="different-turn"?"another-turn":null,AwaitingUserAction=mode=="different-turn"};
            await using var isolated=new CodexObserver(activitySource:()=>records);
            var w=new CodexWatch(other.Id,other.Stages[0].Id,other.CurrentCodex!.BindingId,"saved-chat",1);
            isolated.Event+=(a,m,s,e,l)=>CodexState.Apply(other,a.StageId,m,s,a.Generation,e,l);
            isolated.Watch([w],mode=="different-turn"?new HashSet<Guid>{w.BindingId}:null,
                mode=="different-turn"?new Dictionary<Guid,string>{{w.BindingId,"another-turn"}}:null);
            isolated.Start();await Task.Delay(1200);
            if(other.CurrentCodex!.Phase==CodexPhase.ReadyForReview||other.Lane!=Lane.Running||!other.TimerUnread)
                throw new Exception("无关历史终态不得接管另一个等待轮次或新绑定");
            checks.Add(mode=="different-turn"?"其他轮次终态不能覆盖保存等待":"新绑定不继承旧终态保护");
        }
    }
    private static async Task PipelineGaps(string root,List<string> checks)
    {
        var path=Path.Combine(root,"pipeline-gap.log");await File.WriteAllTextAsync(path,"");string[] files=[path];
        var records=new MutableRecords{Value=new("timer-turn","inProgress",1,true,0,0,0,DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds())};
        var item=new WorkItem{Lane=Lane.Running};item.Stages[0].Codex=new(){ThreadId="timer-chat"};
        await using var observer=new CodexObserver(behaviorEnabled:true,behaviorSource:new CodexBehaviorSource(()=>files,0),activitySource:()=>records);
        var w=new CodexWatch(item.Id,item.Stages[0].Id,item.CurrentCodex!.BindingId,"timer-chat",1);
        observer.Event+=(a,m,s,e,l)=>CodexState.Apply(item,a.StageId,m,s,a.Generation,e,l);
        observer.Watch([w]);observer.Start();
        async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(5);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("隔离流水线等待超时");await Task.Delay(20);}}
        await Until(()=>item.CurrentCodex!.Phase==CodexPhase.Running);
        item.Lane=Lane.Action;item.TimerUnread=true;
        files=[];await Task.Delay(1100);files=[path];await Task.Delay(1200);
        if(item.Lane!=Lane.Action||!item.TimerUnread)throw new Exception("日志暂失恢复不应移动时间提醒卡或清点");
        checks.Add("完整观察器到卡片：日志暂失恢复不误移提醒卡或清点");
        records.Value=records.Value with{ProjectionCaughtUp=false};await Task.Delay(3200);
        await Until(()=>item.CurrentCodex!.Phase==CodexPhase.Unknown);
        records.Value=records.Value with{ProjectionCaughtUp=true};await Until(()=>item.CurrentCodex!.Phase==CodexPhase.Running);
        if(item.Lane!=Lane.Action||!item.TimerUnread)throw new Exception("投影追平只能恢复标签，不能伪造恢复并移列清点");
        checks.Add("完整观察器到卡片：投影追平恢复标签但保留提醒卡和红点");
    }
    private static async Task Additional(string root,List<string> checks)
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        var started=DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        var activity=new CodexActivity("turn-current","inProgress",1,true,0,0,0,started);
        var records=new Dictionary<string,CodexActivity>{{"a",activity}};
        string Line(string text,int delta=0)=>DateTimeOffset.UtcNow.AddSeconds(-30).AddMilliseconds(delta).ToString("O")+
            (text.StartsWith("received")?" info [electron-message-handler] [desktop-notifications] ":" info [host-app-server-manager] ")+text+"\n";
        var net=Path.Combine(root,"net.log");
        await File.WriteAllTextAsync(net,Line("received question conversationId=a requestId=1")+Line("Sending server response id=1 method=item/tool/requestUserInput response={}",1)+Line("Reasoning summary item completed summaryPartCount=0 threadId=a turnId=turn-current",2));
        var reader=new CodexBehaviorSource(()=>[net],0);
        var events=reader.Read(records);
        Check(!events.Any(e=>e.Method=="item/tool/requestUserInput")&&reader.Flags("a").Length==0,"已回答且恢复的历史问题不重放等待");
        var lag=Path.Combine(root,"lag.log");await File.WriteAllTextAsync(lag,Line("received question conversationId=a requestId=2"));
        var delayed=new CodexBehaviorSource(()=>[lag],0); records["a"]=activity with {ProjectionCaughtUp=false}; delayed.Read(records);
        records["a"]=activity;
        Check(delayed.Read(records).Any(e=>e.Method=="item/tool/requestUserInput"&&e.Reconciled),"先有等待、记录稍后追平也能补齐当前请求");
        var handledLag=Path.Combine(root,"handled-lag.log");File.Copy(net,handledLag);
        var delayedHandled=new CodexBehaviorSource(()=>[handledLag],0);records["a"]=activity with{ProjectionCaughtUp=false};delayedHandled.Read(records);records["a"]=activity;
        Check(!delayedHandled.Read(records).Any(e=>e.Method=="item/tool/requestUserInput"),"追平后只恢复净状态，不闪现已处理等待");
        var late=Path.Combine(root,"late.log");await File.WriteAllTextAsync(late,Line("received approval conversationId=a kind=fileChange requestId=3"));
        string[] files=[];var discovery=new CodexBehaviorSource(()=>files,0);discovery.Read(records);files=[late];
        Check(discovery.Read(records).Any(e=>e.Method=="item/fileChange/requestApproval"),"日志晚出现仍能接手已有批准等待");
        var disappear=new CodexBehaviorSource(()=>files,0);disappear.Read(records);
        await File.AppendAllTextAsync(late,Line("Sending server response id=3 method=item/fileChange/requestApproval response={}",1)+Line("Reasoning summary item completed summaryPartCount=0 threadId=a turnId=turn-current",2));disappear.Read(records);
        files=[];disappear.Read(records);files=[late];
        Check(!disappear.Read(records).Any(e=>!e.Reconciled),"日志暂时未发现后恢复不重放旧等待和恢复");
        var coldAnswered=Path.Combine(root,"answered.log");await File.WriteAllTextAsync(coldAnswered,Line("received question conversationId=a requestId=4")+Line("Sending server response id=4 method=item/tool/requestUserInput response={}",1));
        var responseOnly=new CodexBehaviorSource(()=>[coldAnswered],0);var waiting=responseOnly.Read(records);
        Check(waiting.Any(e=>e.Method=="item/tool/requestUserInput")&&!waiting.Any(e=>e.Method=="thread/status/changed"),"冷启动只收到原回答仍等待真实执行证据");
        var history=new CodexBehaviorSource(()=>[lag],0);records["a"]=activity with{StartedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()};
        Check(!history.Read(records).Any(e=>e.Method=="item/tool/requestUserInput"),"旧轮次的等待不污染当前新轮次");
    }
}
