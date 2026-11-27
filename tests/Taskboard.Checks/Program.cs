using System.Diagnostics;
using System.Text.Json;
using Taskboard.Core;

try
{
if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("测试必须指定 --data-dir 隔离目录");
var root = Path.GetFullPath(args[1]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new ArgumentException("请使用新的空测试目录");
Directory.CreateDirectory(root);
var checks = new List<string>();
void Assert(bool value, string name) { if (!value) throw new Exception("失败：" + name); checks.Add(name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { checks.Add(name); return; } catch (JsonException) { checks.Add(name); return; } throw new Exception("未拒绝：" + name); }
foreach (var brand in new[] { "摸鱼台", "MoyuDesk", "MoyuDesk.exe", "摸鱼台 · MoyuDesk", "即时任务看板" })
    Assert(NotificationSoundRules.IsSelf(brand), "通知监听会过滤本程序：" + brand);
var engine = new BoardEngine(new BoardState());
var unassignedEngine = new BoardEngine(new BoardState());
var unassigned = unassignedEngine.NewSession(Lane.Refining); unassigned.StatusSelected = false;
unassigned.Working.Title = "无预选状态草稿"; unassignedEngine.StageSession(unassigned);
Assert(unassignedEngine.State.Sessions.Count == 1 && unassignedEngine.State.Tasks.Count == 0, "未选择状态的非空内容可暂存");
try { unassignedEngine.Submit(unassigned); throw new Exception("未选择状态仍被正式保存"); }
catch (InvalidOperationException error) { Assert(error.Message.Contains("当前状态"), "未选择状态保存时提示先选择"); }
var roundTrip = Copies.Of(unassigned);
Assert(!roundTrip.StatusSelected, "未选择状态跨序列化保留");
var unassignedColumn = new BoardColumn { Name = "未占用自定义列表" };
unassignedEngine.State.Preferences.Columns.Add(unassignedColumn);
Assert(BoardColumns.IsEmpty(unassignedEngine.State, unassignedColumn), "未选择状态的共享草稿不占用任何列表");
BoardColumns.Delete(unassignedEngine.State, unassignedColumn);
var unassignedStore = new AtomicStore(Path.Combine(root, "unassigned-column")); await unassignedStore.SaveAsync(unassignedEngine.State);
Assert(!unassignedStore.Load().Sessions.Single().StatusSelected, "删除未占用列表后无状态草稿仍可保存并重启读取");
var oldSessionJson = JsonSerializer.Serialize(unassigned).Replace("\"StatusSelected\":false,", "");
Assert(JsonSerializer.Deserialize<EditSession>(oldSessionJson)!.StatusSelected, "旧草稿未含新字段时保留原已选状态");
var prefs = new Preferences();
Assert(prefs.FloatingTransparency == 10 && prefs.GlassTransparency == 20 && prefs.BallTransparency == 40, "悬浮窗口、多列表、球默认透明度分别为10%、20%、40%");
prefs.FloatingTransparency = 37;
Assert(Copies.Of(prefs).FloatingTransparency == 37 && prefs.GlassTransparency == 20, "悬浮窗口透明度独立持久化");
var blank = engine.NewSession(Lane.Running); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0 && engine.State.Tasks.Count == 0, "空内容不形成草稿或任务");
blank.Working.Stages[0].Name = "改名"; engine.AddStage(blank); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0, "仅改阶段名和新增空阶段不暂存");
blank.Working.AiIds.Add(engine.State.AiOptions[0].Id); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0, "仅选AI不暂存");
blank.Working.Stages[0].Text = "只有详情\n第二行"; engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 1 && engine.State.Tasks.Count == 0 && !engine.Search("详情").Any(), "非空草稿暂存但不参与正式列表与搜索");
var first = engine.Submit(blank);
Assert(first.DisplayTitle == "只有详情" && engine.State.Tasks.Count == 1 && engine.State.Sessions.Count == 0, "无名称可以正式保存并以详情首行展示");
var initialId = first.Id;
var session = engine.Edit(first.Id); session.Working.Title = "未提交";
engine.StageSession(session);
Assert(engine.State.Tasks[0].Title == "", "编辑副本不修改正式任务");
Assert(ReferenceEquals(session, engine.Edit(first.Id)), "同一任务仅一份编辑会话");
engine.Discard(session);
Assert(engine.State.Tasks[0].Title == "" && engine.State.Sessions.Count == 0, "X放弃已建任务未提交修改");
session = engine.Edit(first.Id);
for (var i = 0; i < 5; i++) engine.AddStage(session);
session.Working.Stages[1].Name = "已改名";
session.Working.CurrentStageId = session.Working.Stages[2].Id;
foreach (var stage in session.Working.Stages) stage.IsRunning = stage.Number is 0 or 2 or 5;
Assert(session.Working.Stages[^1].Number == 6, "阶段改名不影响编号递增");
Assert(session.Working.Stages.Count(x => x.IsRunning == true) == 3, "阶段运行标记独立，不限制并行数量");
session.Working.Stages[5].Text = "隐藏阶段检索词"; first = engine.Submit(session);
Assert(first.Id == initialId && BoardEngine.CardStages(first).Select(x => x.Number).SequenceEqual(new[] { 0, 2, 5 }), "多阶段卡片仅显示全部运行阶段，不展示暂无进程阶段");
Assert(engine.Search("隐藏阶段检索词").Single().Id == first.Id, "搜索覆盖非当前阶段正文");
engine.Move(first.Id, Lane.Inspiration, null);
Assert(BoardEngine.CardStages(first).Select(x => x.Number).SequenceEqual(new[] { 0, 2, 5 }) && first.Stages.Count == 7, "灵感库同样只展示运行阶段，全部阶段信息仍保留");
engine.Move(first.Id, Lane.Refining, null); engine.Undo(); engine.Undo();
Assert(engine.State.Tasks[0].Lane == Lane.Running, "跨列移动可连续撤销");
first = engine.State.Tasks[0]; session = engine.Edit(first.Id);
session.Working.Title = "人工修改";
first.PendingReasons.Add("后台结果"); first.Reminder.LastOccurrence = DateTime.Today;
first.Reminder.NextLocal = DateTime.Today.AddDays(3); first.Lane = Lane.Action;
first = engine.Submit(session);
Assert(first.Title == "人工修改" && first.PendingReasons.Contains("后台结果") && first.Reminder.NextLocal == DateTime.Today.AddDays(3) && first.Lane == Lane.Action, "提交标题保留后台提醒及列表变化");
engine.Undo(); first = engine.State.Tasks[0];
Assert(first.Title == "" && first.PendingReasons.Contains("后台结果") && first.Lane == Lane.Action, "字段撤销保留后台更新");
var secondSession = engine.NewSession(Lane.Action); secondSession.Working.Title = "第二任务"; var second = engine.Submit(secondSession);
var thirdSession = engine.NewSession(Lane.Action); thirdSession.Working.Title = "第三任务"; var third = engine.Submit(thirdSession);
engine.Move(first.Id, Lane.Action, null);
Assert(engine.State.Tasks.Where(x => x.Lane == Lane.Action).Select(x => x.Id).SequenceEqual(new[] { second.Id, third.Id, first.Id }), "同列向后移动不发生偏移");
engine.Undo();
Assert(engine.State.Tasks.Where(x => x.Lane == Lane.Action).First().Id == first.Id, "撤销排序恢复原位置");
engine.Delete(second.Id); Assert(!engine.State.Tasks.Any(x => x.Id == second.Id), "删除任务"); engine.Undo();
Assert(engine.State.Tasks[1].Id == second.Id, "撤销删除恢复内容与顺序");
var optionId = engine.State.AiOptions[0].Id; var priorityId = engine.State.PriorityOptions[0].Id;
first = engine.State.Tasks[0]; first.AiIds = [optionId]; first.PriorityId = priorityId;
session = engine.Edit(first.Id); session.Working.Title = "修改选项前打开的编辑";
engine.DeleteOption(optionId, true); engine.DeleteOption(priorityId, false);
Assert(first.AiIds.Count == 0 && first.PriorityId is null && session.Working.AiIds.Count == 0 && session.Original.PriorityId is null, "删除选项清空任务及编辑引用");
engine.Submit(session); engine.Undo(); Validation.Check(engine.State); checks.Add("删除选项后保存撤销不恢复无效引用");
first = engine.State.Tasks[0];
first.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Daily, Time = TimeSpan.FromHours(9), NextLocal = new DateTime(2026, 10, 1, 9, 0, 0) };
first.Lane = Lane.Paused;
var now = new DateTime(2026, 10, 13, 10, 0, 0);
var due = ReminderClock.Tick(engine.State, now);
Assert(due.Count == 1 && first.Lane == Lane.Paused && first.TimerUnread && first.PendingReasons.Contains("定时提醒到期") && first.Reminder.NextLocal == new DateTime(2026, 10, 14, 9, 0, 0), "#04 睡眠错过多个周期只补一次，卡片留在原位不移列仅亮起提醒");
Assert(ReminderClock.Tick(engine.State, now).Count == 0, "同一次到期不重复提醒");
first.Lane = Lane.Done; first.Reminder.NextLocal = now.AddDays(-1);
Assert(ReminderClock.Tick(engine.State, now).Count == 0, "已完成任务停止提醒");
first.Lane = Lane.Running; first.Reminder.Repeat = Repeat.Once;
Assert(ReminderClock.Tick(engine.State, now).Count == 1 && first.Lane == Lane.Running && !first.Reminder.Enabled, "#04 一次性提醒只执行一次且保持原列不动");
var weekly = new Reminder { Repeat = Repeat.Weekly, Weekday = DayOfWeek.Monday, Time = TimeSpan.FromHours(9) };
Assert(ReminderClock.Next(weekly, new DateTime(2026, 10, 12, 9, 0, 0)) == new DateTime(2026, 10, 19, 9, 0, 0), "每周提醒严格推进到未来");
Assert(Reminder.DefaultNextLocal(new DateTime(2026, 11, 27, 3, 40, 25)) == new DateTime(2026, 11, 27, 4, 0, 0), "#04 定时提醒默认时间对齐当日下一个小时整点(03:40->04:00)");
Assert(Reminder.DefaultNextLocal(new DateTime(2026, 11, 27, 14, 0, 0)) == new DateTime(2026, 11, 27, 15, 0, 0), "#04 定时提醒整点时刻推进到下一个小时整点(14:00->15:00)");
Assert(Reminder.DefaultNextLocal(new DateTime(2026, 11, 27, 23, 40, 0)) == new DateTime(2026, 11, 28, 0, 0, 0), "#04 定时提醒跨午夜对齐次日00:00(23:40->次日00:00)");
Assert(Reminder.DefaultNextLocal(new DateTime(2026, 12, 31, 23, 59, 59)) == new DateTime(2027, 1, 1, 0, 0, 0), "#04 定时提醒跨年对齐次年00:00");
{
    var beforeDefault = DateTime.Now;
    var freshReminder = new Reminder();
    var afterDefault = DateTime.Now;
    var expectedA = Reminder.DefaultNextLocal(beforeDefault);
    var expectedB = Reminder.DefaultNextLocal(afterDefault);
    Assert((freshReminder.NextLocal == expectedA || freshReminder.NextLocal == expectedB)
        && freshReminder.Time == freshReminder.NextLocal.TimeOfDay
        && freshReminder.Weekday == freshReminder.NextLocal.DayOfWeek,
        "#04 新建 Reminder 默认值对齐当前日期与下一个小时整点");
    var legacySavedJson = "{\"Enabled\":true,\"Repeat\":0,\"NextLocal\":\"2026-12-05T18:30:00\",\"Weekday\":6,\"Time\":\"18:30:00\",\"LastOccurrence\":null}";
    var deserialized = JsonSerializer.Deserialize<Reminder>(legacySavedJson)!;
    Assert(deserialized.Enabled && deserialized.NextLocal == new DateTime(2026, 12, 5, 18, 30, 0)
        && deserialized.Time == new TimeSpan(18, 30, 0) && deserialized.Weekday == DayOfWeek.Saturday,
        "#04 反序列化既有 Reminder JSON 完整保留原保存时间不被默认值覆盖");
}
session = engine.Edit(first.Id); session.Working.Title = "退出后恢复的未提交修改"; engine.StageSession(session);
var draft = engine.NewSession(Lane.Refining); draft.Working.Title = "持久新建草稿"; draft.Visible = false; engine.StageSession(draft);
var store = new AtomicStore(Path.Combine(root, "persistence")); await store.SaveAsync(Copies.Of(engine.State));
var reloaded = store.Load();
Assert(reloaded.Sessions.Any(x => x.TaskId == first.Id && x.Working.Title == "退出后恢复的未提交修改") && reloaded.Tasks[0].Title != "退出后恢复的未提交修改", "未提交编辑跨进程恢复但不变成正式修改");
Assert(reloaded.Sessions.Any(x => x.TaskId is null && x.Working.Title == "持久新建草稿"), "关闭新建草稿跨进程保留");
await store.SaveAsync(Copies.Of(engine.State));
File.WriteAllText(store.MainPath, "{bad"); reloaded = store.Load();
Assert(reloaded.Tasks.Count == 3 && store.RecoveryMessage is not null && Directory.GetFiles(store.DirectoryPath, "*.damaged-*").Length == 1, "损坏主文件从有效备份恢复且保留原件");
File.WriteAllText(store.MainPath, "{}"); File.WriteAllText(store.BackupPath, "[]");
var badPrimary = File.ReadAllText(store.MainPath); Reject(() => store.Load(), "主文件备份都坏时拒绝启动");
Assert(File.ReadAllText(store.MainPath) == badPrimary, "坏数据不会被空看板覆盖");
var bad = Copies.Of(engine.State); bad.Tasks[0].Stages = [null!]; Reject(() => Validation.Check(bad), "拒绝空阶段元素");
bad = Copies.Of(engine.State); bad.Tasks[0].CurrentStageId = Guid.NewGuid(); Reject(() => Validation.Check(bad), "拒绝无效当前阶段");
bad = Copies.Of(engine.State); bad.Tasks.Add(Copies.Of(bad.Tasks[0])); Reject(() => Validation.Check(bad), "拒绝重复任务编号");
bad = Copies.Of(engine.State); bad.Tasks[0].AiIds = [Guid.NewGuid()]; Reject(() => Validation.Check(bad), "拒绝未知选项引用");
bad = Copies.Of(engine.State); bad.Preferences.Positions["bad"] = new WindowPosition { Width = double.NaN, Height = 100 }; Reject(() => Validation.Check(bad), "拒绝非法窗口尺寸");
bad = Copies.Of(engine.State); bad.AiOptions[0].Color = "not-color"; Reject(() => Validation.Check(bad), "拒绝非法颜色");
bad = Copies.Of(engine.State); bad.Sessions.Add(new EditSession()); Reject(() => Validation.Check(bad), "拒绝空持久草稿");
var failStore = new AtomicStore(Path.Combine(root, "failure"));
await failStore.SaveAsync(Copies.Of(engine.State)); var untouched = File.ReadAllBytes(failStore.MainPath);
using (var locked = new FileStream(failStore.MainPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    try { await failStore.SaveAsync(Copies.Of(engine.State)); throw new Exception("未识别写入失败"); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { checks.Add("被占用的主文件使写入明确失败"); }
}
Assert(File.ReadAllBytes(failStore.MainPath).SequenceEqual(untouched), "写入失败不破坏旧文件");
var limitEngine = new BoardEngine(new BoardState()); var limitDraft = limitEngine.NewSession(Lane.Refining); limitDraft.Working.Title = "0"; var limitTask = limitEngine.Submit(limitDraft);
for (var i = 1; i <= 25; i++) { var edit = limitEngine.Edit(limitTask.Id); edit.Working.Title = i.ToString(); limitEngine.Submit(edit); }
for (var i = 0; i < 20; i++) limitEngine.Undo();
Assert(!limitEngine.CanUndo && limitEngine.State.Tasks[0].Title == "5", "撤销仅保留本次运行最近20次提交");
var noUndo = limitEngine.Edit(limitTask.Id); noUndo.Working.Title = "灵感自动输入"; limitEngine.Submit(noUndo, false);
Assert(!limitEngine.CanUndo, "灵感自动输入保存不计入字段撤销步数");
var configState = Copies.Of(engine.State); configState.Preferences.ConnectionConfigs["codex"] = "保留的接入配置"; configState.Preferences.ConnectionConfigs["飞书"] = "待专项接入";
var configuration = ConfigurationBackup.Capture(configState, Path.Combine(root, "configured-data"));
var configurationFile = Path.Combine(root, "configuration.json"); configuration.Write(configurationFile);
using (var document = JsonDocument.Parse(File.ReadAllBytes(configurationFile)))
    Assert(!document.RootElement.TryGetProperty("Tasks", out _) && !document.RootElement.TryGetProperty("Sessions", out _) && !File.ReadAllText(configurationFile).Contains("退出后恢复的未提交修改"), "配置备份不含任务、阶段内容或草稿");
var restoredConfiguration = ConfigurationBackup.Read(configurationFile);
Assert(restoredConfiguration.Preferences.ConnectionConfigs["codex"] == "保留的接入配置" && restoredConfiguration.Preferences.GlassTransparency == 20, "平台配置及默认20%透明度可往返恢复");
var tasksBeforeConfig = JsonSerializer.Serialize(configState.Tasks); var sessionsBeforeConfig = JsonSerializer.Serialize(configState.Sessions);
restoredConfiguration.AiOptions.Clear(); restoredConfiguration.PriorityOptions.Clear();
var referenced = configState.AiOptions[0].Id; configState.Tasks[0].AiIds.Add(referenced);
tasksBeforeConfig = JsonSerializer.Serialize(configState.Tasks);
var configured = restoredConfiguration.Apply(configState);
Assert(JsonSerializer.Serialize(configured.Tasks) == tasksBeforeConfig && JsonSerializer.Serialize(configured.Sessions) == sessionsBeforeConfig, "恢复配置不更改任务、提醒、草稿或未提交输入");
Assert(configured.AiOptions.Any(x => x.Id == referenced), "恢复配置保留现有任务所引用选项");
var oldBackupFile = Path.Combine(root, "old-full-data.json"); File.WriteAllText(oldBackupFile, JsonSerializer.Serialize(configState));
Reject(() => ConfigurationBackup.Read(oldBackupFile), "配置导入拒绝旧任务全量备份");
var unexpectedConfig = File.ReadAllText(configurationFile).TrimEnd(); File.WriteAllText(Path.Combine(root, "unexpected-config.json"), unexpectedConfig[..^1] + ",\"Tasks\":[]}");
Reject(() => ConfigurationBackup.Read(Path.Combine(root, "unexpected-config.json")), "配置包夹带任务字段时拒绝导入");
var invalidTransparency = Copies.Of(engine.State); invalidTransparency.Preferences.GlassTransparency = 101;
Reject(() => Validation.Check(invalidTransparency), "拒绝超范围透明度");
invalidTransparency.Preferences.GlassTransparency = double.NaN;
Reject(() => Validation.Check(invalidTransparency), "拒绝非有限透明度");
var legacyPreferences = JsonSerializer.Serialize(engine.State).Replace("\"GlassTransparency\":20,", "");
Assert(JsonSerializer.Deserialize<BoardState>(legacyPreferences)!.Preferences.GlassTransparency == 20, "旧版本缺少透明度字段时兼容默认20%");
var oldBallPreferences = JsonSerializer.Serialize(engine.State).Replace("\"BallTransparency\":40,", "");
Assert(JsonSerializer.Deserialize<BoardState>(oldBallPreferences)!.Preferences.BallTransparency == 40, "旧数据缺少球透明度时独立采用40%，普通窗口仍为20%");
var invalidBall = Copies.Of(engine.State); invalidBall.Preferences.BallTransparency = double.PositiveInfinity;
Reject(() => Validation.Check(invalidBall), "拒绝无限大的球透明度，防止合成画面失效");
var ballConfiguration = ConfigurationBackup.Capture(engine.State, root); ballConfiguration.Preferences.BallTransparency = 55;
Assert(ballConfiguration.Apply(engine.State).Preferences.BallTransparency == 55 && engine.State.Preferences.BallTransparency == 40, "球透明度随配置备份恢复，源数据保持不变");
await IntegrationRules.Run(root, Assert);
var draftClock = new BoardEngine(new BoardState()); var clockSession = draftClock.NewSession(Lane.Action);
Assert(clockSession.CreatedUtc is { Kind: DateTimeKind.Utc }, "新建草稿记录独立UTC创建时间");
clockSession.Working.Title = "创建时间保存样本"; clockSession.CreatedUtc = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc); draftClock.StageSession(clockSession);
var clockStore = new AtomicStore(Path.Combine(root, "draft-clock")); await clockStore.SaveAsync(draftClock.State);
Assert(clockStore.Load().Sessions.Single().CreationTime == clockSession.CreatedUtc, "草稿创建时间跨保存和重启保留");
clockSession.CreatedUtc = null;
Assert(clockSession.CreationTime == clockSession.Original.UpdatedUtc, "旧草稿缺少创建字段时使用原始时间，切换不改创建先后");
var perf = new BoardEngine(new BoardState());
var lists = new BoardEngine(new BoardState());
var inspirationColumn = lists.State.Preferences.Columns.First(x => x.BuiltinLane == Lane.Inspiration);
inspirationColumn.Visible = false; inspirationColumn.Name = "随手记录";
Assert(lists.State.Preferences.Columns.Count(x => x.Visible && !x.Deleted) == 5 && inspirationColumn.Contains(new WorkItem { Lane=Lane.Inspiration }),"灵感隐藏不计数，改名保持角色");
foreach (var protectedColumn in lists.State.Preferences.Columns.Where(x => x.Protected))
{ try { BoardColumns.Delete(lists.State,protectedColumn); throw new Exception("保护列表被删除"); } catch (InvalidOperationException) { checks.Add("保护列表禁止删除："+protectedColumn.Name); } }
BoardColumns.SetVisibleCount(lists.State.Preferences,7);
Assert(lists.State.Preferences.Columns.Count(x=>x.Visible && !x.Deleted)==7,"数量增加可恢复隐藏列并新增自定义列");
var custom = lists.State.Preferences.Columns.Last(); var customSession = lists.NewSession(Lane.Refining); custom.Assign(customSession.Working); custom.Assign(customSession.Original);
customSession.Working.Title="自定义列任务"; var customTask = lists.Submit(customSession);
Assert(custom.Contains(customTask) && !lists.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Refining).Contains(customTask),"自定义列任务不混入完善中");
try { BoardColumns.Delete(lists.State,custom); throw new Exception("非空列被删除"); } catch (InvalidOperationException) { checks.Add("正式任务阻止删除列表"); }
var configWithLists = ConfigurationBackup.Capture(lists.State,root);
configWithLists.Preferences.Columns.Remove(custom); var appliedLists = configWithLists.Apply(lists.State);
Assert(appliedLists.Preferences.Columns.Any(x=>x.Id==custom.Id && !x.Deleted) && JsonSerializer.Serialize(appliedLists.Tasks)==JsonSerializer.Serialize(lists.State.Tasks),"配置导入保留现有任务引用的列表且任务不变");
lists.Delete(customTask.Id); BoardColumns.Delete(lists.State,custom); lists.Undo();
Assert(!custom.Deleted && custom.Visible && custom.Contains(lists.State.Tasks.Single()),"空列表删除后撤销任务会恢复列表身份");
lists.Move(customTask.Id,Lane.Running,null); lists.Undo(); Assert(custom.Contains(lists.State.Tasks.Single()),"自定义列移动撤销恢复准确列表");
var draftColumn = lists.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Paused); var columnDraft=lists.NewSession(Lane.Paused); columnDraft.Working.Title="非空草稿"; lists.StageSession(columnDraft);
try { BoardColumns.Delete(lists.State,draftColumn); throw new Exception("草稿列被删除"); } catch (InvalidOperationException) { checks.Add("非空草稿阻止删除列表"); }
BoardColumns.SetVisibleCount(lists.State.Preferences,0); Validation.Check(lists.State);
Assert(lists.State.Preferences.Columns.All(x=>!x.Visible) && lists.State.Tasks.Count==1,"所有列可隐藏而不丢任务");
var noColumnsJson = JsonSerializer.SerializeToNode(new BoardState())!; noColumnsJson["Preferences"]!.AsObject().Remove("Columns");
Assert(noColumnsJson.Deserialize<BoardState>()!.Preferences.Columns.Count==6,"旧数据无列配置时兼容六列");
await StageRules.Run(root, Assert);
await StageDeletionRules.Run(root, Assert);
await ArchivePolicyRules.Run(root, Assert);
await ArchiveEditingRules.Run(root, Assert);
await CodexRecoveryRules.Run(root, Assert);
await CodexStopRules.Run(root, Assert);
await CodexQuickStopRules.Run(root, Assert);
var codexTask = new WorkItem { Title="关联检查",Lane=Lane.Running,TimerUnread=true };
codexTask.Stages[0].Codex = new CodexBinding { ThreadId="thread-1" };
long eventSequence=0;
JsonElement Message(string method,object value) => JsonSerializer.SerializeToElement(new { method,@params=value });
bool Apply(string method,object value,bool live=true,int generation=1,string epoch="source-1") => CodexState.Apply(codexTask,Message(method,value),++eventSequence,generation,epoch,live);
Assert(!Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-1"}},false) && codexTask.CurrentCodex!.Phase==CodexPhase.Unknown,"历史来源不能宣称运行或自动流转");
Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-1"}});
Assert(codexTask.CurrentCodex!.Phase==CodexPhase.Running && codexTask.Lane==Lane.Running,"可信开始事件确认运行");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=new[]{"waitingOnUserInput"}}});
Assert(codexTask.Lane==Lane.Action && codexTask.CurrentCodex!.Phase==CodexPhase.WaitingForInput,"等待回答进入待操作");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=new[]{"waitingOnApproval"}}});
Assert(codexTask.CurrentCodex!.Phase==CodexPhase.WaitingForApproval,"权限等待独立显示");
Apply("serverRequest/resolved",new {threadId="thread-1",requestId=7}); Assert(codexTask.Lane==Lane.Action,"单项请求消失不冒充恢复执行");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=Array.Empty<string>()}});
Assert(codexTask.Lane==Lane.Running && !codexTask.TimerUnread,"可信恢复回进行中且清除时间红点");
Assert(!Apply("turn/completed",new {threadId="other",turn=new {id="turn-1",status="completed"}}),"别的会话事件不能污染关联");
Assert(!Apply("turn/completed",new {threadId="thread-1",turn=new {id="older-turn",status="completed"}}),"旧轮次结束不能覆盖当前运行");
Assert(!Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="completed"}},generation:2),"旧关联代次不能修改状态");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="completed"}});
Assert(codexTask.CurrentCodex!.Phase==CodexPhase.ReadyForReview && codexTask.Lane==Lane.Action,"正常结束进入待操作但不完成任务");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="failed"}}); Assert(codexTask.CurrentCodex!.Phase==CodexPhase.Failed,"失败单独记录");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="interrupted"}}); Assert(codexTask.CurrentCodex!.Phase==CodexPhase.Interrupted,"中断单独记录");
Apply("probe/disconnected",new {threadId="thread-1"}); Assert(codexTask.CurrentCodex!.Phase==CodexPhase.Unknown && codexTask.Lane==Lane.Action,"断线待确认且不误报结束");
codexTask.Lane=Lane.Done; Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-2"}}); Assert(codexTask.Lane==Lane.Done,"AI事件不拉回已完成");
codexTask.Lane=Lane.Action; codexTask.ColumnId=custom.Id; Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-3"}}); Assert(codexTask.Lane==Lane.Action && codexTask.ColumnId==custom.Id,"自定义列不参与AI自动流转");
Assert(!CodexState.Apply(codexTask,Message("probe/disconnected",new {threadId="thread-1"}),0,1,"source-1",true),"乱序信号拒绝");
var bindingOriginal=Copies.Of(codexTask); var bindingEdited=Copies.Of(codexTask); bindingEdited.Title="新标题";
codexTask.CurrentCodex!.Phase=CodexPhase.WaitingForApproval;
Assert(BoardEngine.Merge(bindingOriginal,bindingEdited,codexTask).CurrentCodex!.Phase==CodexPhase.WaitingForApproval,"标题提交保留监听最新状态");
bindingEdited.Stages[0].Codex=new CodexBinding {ThreadId="thread-2"}; var rebound=BoardEngine.Merge(bindingOriginal,bindingEdited,codexTask);
Assert(rebound.CurrentCodex!.Generation==2 && rebound.CurrentCodex.Phase==CodexPhase.Unknown && rebound.CurrentCodex.PreviousThreads.Contains("thread-1"),"改绑提升代次并保留旧关联历史");

// #12: Multiple stages / tasks bound to same session clear orange-red attention together
{
    var sharedThread = Guid.NewGuid().ToString("D");
    var multiEngine = new BoardEngine(new BoardState());
    var s1 = multiEngine.NewSession(Lane.Action);
    s1.Working.Title = "同任务多阶段同会话";
    var st1 = s1.Working.Stages[0]; st1.IsRunning = true; st1.Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = sharedThread };
    var st2 = multiEngine.AddStage(s1); st2.IsRunning = true; st2.Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = sharedThread };
    var t1 = multiEngine.Submit(s1);
    st1 = t1.Stages[0]; st2 = t1.Stages[1];
    var s2 = multiEngine.NewSession(Lane.Action);
    s2.Working.Title = "跨任务同会话";
    var t2 = multiEngine.Submit(s2);
    var st3 = t2.Stages[0]; st3.IsRunning = true;
    Assert(multiEngine.BindStageConversation(t2.Id, st3.Id, null, sharedThread.ToUpperInvariant(), "同会话"), "#12 跨任务绑定规范化同一会话成功");
    var askMsg = JsonSerializer.SerializeToElement(new { method = "item/tool/requestUserInput", id = "req-1", @params = new { threadId = sharedThread, turnId = "turn-shared" } });
    CodexState.ApplySource(t1, st1.Id, st1.Codex!.BindingId, askMsg, 1, st1.Codex.Generation, "ep-1", true);
    CodexState.ApplySource(t1, st2.Id, st2.Codex!.BindingId, askMsg, 1, st2.Codex.Generation, "ep-1", true);
    CodexState.ApplySource(t2, st3.Id, st3.Codex!.BindingId, askMsg, 1, st3.Codex.Generation, "ep-1", true);
    Assert(AiAttention.IsPending(st1.Codex) && AiAttention.IsPending(st2.Codex) && AiAttention.IsPending(st3.Codex), "#12 绑定同一会话的三个阶段初始均为橙红待处理");
    var notifiedIds = new List<Guid>();
    multiEngine.Changed += ids => notifiedIds.AddRange(ids);
    Assert(multiEngine.AcknowledgeStageAttention(t1.Id, st1.Id, st1.Codex.BindingId), "#12 右键确认阶段1成功");
    Assert(!AiAttention.IsPending(st1.Codex) && !AiAttention.IsPending(st2.Codex) && !AiAttention.IsPending(st3.Codex), "#12 确认其中一阶段后，同任务及跨任务绑定同一会话的所有阶段同步消除橙红提醒");
    Assert(notifiedIds.Contains(t1.Id) && notifiedIds.Contains(t2.Id), "#12 联动消除通知覆盖所有受影响任务");
    var askMsg2 = JsonSerializer.SerializeToElement(new { method = "item/tool/requestUserInput", id = "req-2", @params = new { threadId = sharedThread, turnId = "turn-shared" } });
    CodexState.ApplySource(t1, st1.Id, st1.Codex.BindingId, askMsg2, 2, st1.Codex.Generation, "ep-1", true);
    CodexState.ApplySource(t2, st3.Id, st3.Codex.BindingId, askMsg2, 2, st3.Codex.Generation, "ep-1", true);
    Assert(AiAttention.IsPending(st1.Codex) && AiAttention.IsPending(st3.Codex), "#12 同一会话产生新请求时重新亮起橙红");
    multiEngine.Move(t1.Id, Lane.Running, null);
    Assert(!AiAttention.IsPending(st1.Codex) && !AiAttention.IsPending(st3.Codex), "#12 将任务1移回进行中时跨任务绑定同一会话的阶段也同步消除橙红");
}

// #09 UnbindStageConversation & #22 DismissReminder
{
    var coreEngine = new BoardEngine(new BoardState());
    var sess = coreEngine.NewSession(Lane.Running);
    sess.Working.Title = "解绑与关闭提醒测试";
    var thread = Guid.NewGuid().ToString("D");
    sess.Working.Stages[0].Codex = new CodexBinding { Platform = AiPlatform.Codex, ThreadId = thread, ThreadName = "待解绑" };
    sess.Working.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Daily, Time = TimeSpan.FromHours(9), NextLocal = DateTime.Today.AddHours(9) };
    var item = coreEngine.Submit(sess);
    var stage = item.Stages[0];
    Assert(!coreEngine.UnbindStageConversation(item.Id, stage.Id, Guid.NewGuid()), "#09 预期BindingId不匹配时拒绝解绑");
    Assert(coreEngine.UnbindStageConversation(item.Id, stage.Id, stage.Codex!.BindingId) && stage.Codex is null, "#09 UnbindStageConversation成功解除绑定");
    coreEngine.Undo();
    item = coreEngine.State.Tasks.Single();
    Assert(item.Stages[0].Codex?.ThreadId == thread, "#09 解绑支持撤销恢复原绑定");

    var dueIds = ReminderClock.Tick(coreEngine.State, DateTime.Today.AddHours(10));
    Assert(dueIds.Contains(item.Id) && item.Lane == Lane.Running && item.TimerUnread && item.PendingReasons.Contains("定时提醒到期"), "#04/#22 到期后卡片留在原列，红点与原因标记存在");

    // #22 Edit / Submit without changing reminder preserves TimerUnread; changing reminder clears it
    var editNoReminderChange = coreEngine.Edit(item.Id);
    Assert(item.TimerUnread, "#22 打开编辑会话不误消提醒红点");
    editNoReminderChange.Working.Title = "改标题不改提醒";
    item = coreEngine.Submit(editNoReminderChange);
    Assert(item.TimerUnread && item.PendingReasons.Contains("定时提醒到期"), "#22 保存未改动提醒的编辑时保留提醒红点");
    coreEngine.Move(item.Id, Lane.Paused, null);
    Assert(item.TimerUnread && item.Lane == Lane.Paused, "#22 移动卡片不误消提醒红点");

    // #04 Custom column task also stays in place on reminder due
    var col = new BoardColumn { Name = "定时原位自定义列" };
    coreEngine.State.Preferences.Columns.Add(col);
    var cs = coreEngine.NewSession(Lane.Refining); col.Assign(cs.Working); col.Assign(cs.Original);
    cs.Working.Title = "自定义列定时任务";
    cs.Working.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Once, Time = TimeSpan.FromHours(9), NextLocal = DateTime.Today.AddHours(9) };
    var customTimed = coreEngine.Submit(cs);
    var customDue = ReminderClock.Tick(coreEngine.State, DateTime.Today.AddHours(10));
    Assert(customDue.Contains(customTimed.Id) && customTimed.ColumnId == col.Id && customTimed.TimerUnread, "#04 自定义列定时到期保持原列不变仅亮起红点");

    // Edit that modifies reminder clears TimerUnread
    var editReminderChange = coreEngine.Edit(customTimed.Id);
    editReminderChange.Working.Reminder.Enabled = true;
    editReminderChange.Working.Reminder.Time = TimeSpan.FromHours(15);
    editReminderChange.Working.Reminder.NextLocal = DateTime.Today.AddDays(1).AddHours(15);
    customTimed = coreEngine.Submit(editReminderChange);
    Assert(!customTimed.TimerUnread && !customTimed.PendingReasons.Contains("定时提醒到期"), "#22 编辑中重设提醒并保存后清除旧到期红点");

    Assert(coreEngine.DismissReminder(item.Id), "#22 DismissReminder成功关闭并清除提醒");
    Assert(!item.TimerUnread && !item.PendingReasons.Contains("定时提醒到期") && !item.Reminder.Enabled, "#22 关闭提醒后红点、待处理原因和重复提醒全部清除");
    Assert(ReminderClock.Tick(coreEngine.State, DateTime.Today.AddDays(1).AddHours(10)).Count == 0, "#22 关闭后后续周期不再重新触发提醒");

    // #17 EllipsizeStageDetails defaults to true + ExpandedStageDetails persistence & pruning
    Assert(new Preferences().EllipsizeStageDetails, "#17 省略多列表阶段详情默认开启");
    var expandedStage = item.Stages[0];
    Assert(coreEngine.SetStageDetailsExpanded(item.Id, expandedStage.Id, true) && coreEngine.IsStageDetailsExpanded(item.Id, expandedStage.Id), "#17 记录阶段展开状态");
    var expStore = new AtomicStore(Path.Combine(root, "expanded-stage"));
    await expStore.SaveAsync(Copies.Of(coreEngine.State));
    var expLoaded = expStore.Load();
    Assert(expLoaded.Preferences.ExpandedStageDetails.Contains(BoardEngine.StageDetailsKey(item.Id, expandedStage.Id)), "#17 阶段展开状态持久化跨重启保留");
    var cfgExp = ConfigurationBackup.Capture(coreEngine.State, root);
    Assert(cfgExp.Preferences.ExpandedStageDetails.Count == 0, "#17 配置备份不夹带任务阶段展开键");
    Assert(cfgExp.Apply(coreEngine.State).Preferences.ExpandedStageDetails.Contains(BoardEngine.StageDetailsKey(item.Id, expandedStage.Id)), "#17 导入配置保留本地阶段展开状态");
    coreEngine.Delete(item.Id);
    Assert(!coreEngine.State.Preferences.ExpandedStageDetails.Contains(BoardEngine.StageDetailsKey(item.Id, expandedStage.Id)), "#17 删除任务自动清理阶段展开记录防止膨胀");
}
// 0.9.56 Appearance theme preferences: defaults, normalization, Copies.Of/Same, backup & AtomicStore persistence
{
    var themePrefs = new Preferences();
    Assert(themePrefs.ThemeMode == ThemeMode.Dark
        && themePrefs.DarkThemePreset == "midnight-cyan"
        && themePrefs.LightThemePreset == "daylight-mist",
        "#Theme 主题偏好默认深夜模式(midnight-cyan)与默认浅色(daylight-mist)");

    themePrefs.ThemeMode = (ThemeMode)999;
    themePrefs.DarkThemePreset = "non-existent-dark";
    themePrefs.LightThemePreset = "non-existent-light";
    Validation.Normalize(themePrefs);
    Assert(themePrefs.ThemeMode == ThemeMode.Dark
        && themePrefs.DarkThemePreset == "midnight-cyan"
        && themePrefs.LightThemePreset == "daylight-mist",
        "#Theme Validation.Normalize 将非法 ThemeMode 回退 Dark、未知配色回退默认值");

    themePrefs.ThemeMode = ThemeMode.System;
    themePrefs.DarkThemePreset = "OBSIDIAN-AMBER";
    themePrefs.LightThemePreset = "WARM-PAPER";
    Validation.Normalize(themePrefs);
    Assert(themePrefs.DarkThemePreset == "obsidian-amber" && themePrefs.LightThemePreset == "warm-paper",
        "#Theme Validation.Normalize 规范化合法配色 ID 大小写");

    var clonedPrefs = Copies.Of(themePrefs);
    Assert(Copies.Same(themePrefs, clonedPrefs)
        && clonedPrefs.ThemeMode == ThemeMode.System
        && clonedPrefs.DarkThemePreset == "obsidian-amber"
        && clonedPrefs.LightThemePreset == "warm-paper",
        "#Theme Copies.Of 与 Copies.Same 完整保留并比较主题模式与深浅色预设");
    clonedPrefs.LightThemePreset = "mint-breeze";
    Assert(!Copies.Same(themePrefs, clonedPrefs), "#Theme Copies.Same 准确识别浅色预设差异");

    var themeState = new BoardState();
    themeState.Preferences.ThemeMode = ThemeMode.Light;
    themeState.Preferences.DarkThemePreset = "forest-jade";
    themeState.Preferences.LightThemePreset = "lavender-day";
    var themeStore = new AtomicStore(Path.Combine(root, "theme-persistence"));
    await themeStore.SaveAsync(themeState);
    var themeReloaded = themeStore.Load();
    Assert(themeReloaded.Preferences.ThemeMode == ThemeMode.Light
        && themeReloaded.Preferences.DarkThemePreset == "forest-jade"
        && themeReloaded.Preferences.LightThemePreset == "lavender-day",
        "#Theme AtomicStore 保存并重载完整保留主题模式与深浅色预设");

    var themeBackup = ConfigurationBackup.Capture(themeState, root);
    var themeBackupFile = Path.Combine(root, "theme-config.json");
    themeBackup.Write(themeBackupFile);
    var themeBackupRead = ConfigurationBackup.Read(themeBackupFile);
    var appliedTheme = themeBackupRead.Apply(new BoardState());
    Assert(appliedTheme.Preferences.ThemeMode == ThemeMode.Light
        && appliedTheme.Preferences.DarkThemePreset == "forest-jade"
        && appliedTheme.Preferences.LightThemePreset == "lavender-day",
        "#Theme ConfigurationBackup 导出与导入保留主题设置");

    var legacyNoThemeJson = JsonSerializer.Serialize(new BoardState())
        .Replace("\"ThemeMode\":0,", "")
        .Replace("\"DarkThemePreset\":\"midnight-cyan\",", "")
        .Replace("\"LightThemePreset\":\"daylight-mist\",", "");
    var legacyLoaded = JsonSerializer.Deserialize<BoardState>(legacyNoThemeJson)!;
    Assert(legacyLoaded.Preferences.ThemeMode == ThemeMode.Dark
        && legacyLoaded.Preferences.DarkThemePreset == "midnight-cyan"
        && legacyLoaded.Preferences.LightThemePreset == "daylight-mist",
        "#Theme 旧版无主题字段数据反序列化默认采用深夜青蓝");

    var futureUnknownThemeJson = JsonSerializer.Serialize(new BoardState())
        .Replace("\"ThemeMode\":0", "\"ThemeMode\":77")
        .Replace("\"DarkThemePreset\":\"midnight-cyan\"", "\"DarkThemePreset\":\"future-neon\"")
        .Replace("\"LightThemePreset\":\"daylight-mist\"", "\"LightThemePreset\":\"future-cloud\"");
    var futureLoaded = JsonSerializer.Deserialize<BoardState>(futureUnknownThemeJson)!;
    Validation.Check(futureLoaded);
    Assert(futureLoaded.Preferences.ThemeMode == ThemeMode.Dark
        && futureLoaded.Preferences.DarkThemePreset == "midnight-cyan"
        && futureLoaded.Preferences.LightThemePreset == "daylight-mist",
        "#Theme 未知主题配置 JSON 反序列化自动安全回退并通过校验");
}
for (var i = 0; i < 300; i++)
{
    var s = perf.NewSession((Lane)(i % 6)); s.Working.Title = "任务" + i;
    for (var j = 0; j < 4; j++) { if (j > 0) perf.AddStage(s); s.Working.Stages[j].Text = "性能任务详情 " + i + " 第" + j + "阶段"; }
    perf.Submit(s);
}
for (var i = 0; i < 10; i++) { var s = perf.NewSession(Lane.Refining); s.Working.Title = "草稿" + i; perf.StageSession(s); }
var watch = Stopwatch.StartNew(); for (var i = 0; i < 100; i++) _ = perf.Search("详情").ToArray(); watch.Stop();
var searchMs = watch.Elapsed.TotalMilliseconds / 100;
Assert(searchMs < 200, "300任务全部阶段搜索反馈低于200ms");
watch.Restart(); var perfStore = new AtomicStore(Path.Combine(root, "performance")); await perfStore.SaveAsync(Copies.Of(perf.State)); watch.Stop(); var saveMs = watch.Elapsed.TotalMilliseconds;
watch.Restart(); _ = perfStore.Load(); watch.Stop(); var loadMs = watch.Elapsed.TotalMilliseconds;
var result = new { passed = checks.Count, checks, performance = new { tasks = 300, stagesPerTask = 4, drafts = 10, searchMs, saveMs, loadMs } };
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { result.passed, result.performance }));
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    Environment.ExitCode = 1;
    if (args.Length == 2 && args[0] == "--data-dir")
    {
        Directory.CreateDirectory(args[1]);
        File.WriteAllText(Path.Combine(args[1], "failure.txt"), e.ToString());
    }
}
