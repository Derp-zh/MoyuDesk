# 共享代码职责、影响范围与回归选择

当前模块与验证程序位置见[IMPLEMENTATION](../IMPLEMENTATION.md)。改共享代码前列出消费者、原操作范围、本次改变和回归选择。历史版本实施与旧路线见[更新日志](../history/CHANGELOG.md)。

## 阅读方法

先找所改入口，再选下表相关行。对每次变更记录：本次改变、必须保持的动作/范围、受影响消费者、所选检查及未验收项。只读相邻依赖，不通读全部源码或历史证据。`partial`拆文件仍共享一个类的字段，不能把文件数当成独立模块数。

## 现有职责和依赖

| 共享入口与真实源码 | 当前职责、消费者和依赖 | 修改必须保持的边界 |
| --- | --- | --- |
| [BoardEngine](../../src/Taskboard.Core/BoardEngine.cs)、[Models](../../src/Taskboard.Core/Models.cs)、[History](../../src/Taskboard.Core/BoardEngine.History.cs) | 任务/草稿、Submit/Merge、运行阶段约束、撤销/归档；被编辑器、列表、后台更新共用 | 用户字段合并与后台状态分别归属；编辑副本不覆盖最新状态；至少一个运行阶段；不在规则层操作原生窗口 |
| [BoardController](../../src/Taskboard.Desktop/BoardController.cs)及同名分文件 | 组合窗口、Engine/Store、监听服务与UI线程。SetExpanded协调球/列/草稿/编辑/辅助窗；OnChanged协调刷新及会话watch；SubmitSession负责提交与Flush | 窗口显隐不等于提交/放弃；先验证保存结果再关闭；整组恢复保留输入及层次；局部显示变更不随意扩大状态通知 |
| [SurfaceWindow](../../src/Taskboard.Desktop/SurfaceWindow.cs)、[LiveScroll](../../src/Taskboard.Desktop/SurfaceWindow.LiveScroll.cs)、[LiveBall](../../src/Taskboard.Desktop/SurfaceWindow.LiveBall.cs)、[LiveBackdrop](../../src/Taskboard.Desktop/LiveBackdrop.cs)、[SurfaceContentRoot](../../src/Taskboard.Desktop/SurfaceContentRoot.cs) | 共用原生窗口、父子内容宿主、背景/轮廓/裁切与开合；列表、球、悬浮窗及辅助窗口共用或覆写 | 背景视觉、系统命中、WPF内部命中分别验证；固定真实画布不扩大外部范围；禁止普通开合恢复截图交接；子宿主释放与旧回调保护 |
| [ColumnSurface](../../src/Taskboard.Desktop/ColumnSurface.cs)、[Inline](../../src/Taskboard.Desktop/ColumnSurface.Inline.cs)、[CardDragSession](../../src/Taskboard.Desktop/CardDragSession.cs)、[ColumnLayout](../../src/Taskboard.Desktop/BoardController.ColumnLayout.cs) | 列表、卡片复用、alpha1透明交互面、灵感快捷录入、拖动排序/两种缩放与恢复 | 列表空白也能右键/拖动；整组22×22矩形全区可操作；新建整轮廓；卡片手势与空白外点关闭分开 |
| [CardHeading](../../src/Taskboard.Desktop/CardHeading.cs)、[MarqueeText](../../src/Taskboard.Desktop/MarqueeText.cs)、[TaskCardLayout](../../src/Taskboard.Desktop/TaskCardLayout.cs)、[CardSeparator](../../src/Taskboard.Desktop/CardSeparator.cs) | 名称行Measure/Arrange、先状态后会话再阶段余宽、溢出滚动/像素线；列卡片及相关预览使用 | 会话3至5中文字宽；同卡缩放重新预算、极窄装饰裁切；不因视觉改动改变根背景或卡片点击/拖动 |
| [EditorSurface](../../src/Taskboard.Desktop/EditorSurface.cs)、StageDetailsEditor、StageReorderGesture、[DraftStackSurface](../../src/Taskboard.Desktop/DraftStackSurface.cs)、[DraftSwitchMotion](../../src/Taskboard.Desktop/DraftSwitchMotion.cs) | 编辑副本、SaveAndClose/取消/暂存、单宿主堆叠与抽牌、ClearClosed延迟清理 | 操作表[M04](../current/M04-新建编辑与草稿.md)；真实关闭结束后才清理、快速新建取消旧清理；暂存全字段与正式保存分开 |
| [OutsideClicks](../../src/Taskboard.Desktop/BoardController.OutsideClicks.cs)、[GameInput](../../src/Taskboard.Desktop/BoardController.GameInput.cs)、[GlobalHotkey](../../src/Taskboard.Desktop/GlobalHotkey.cs)、[StageConversationPicker](../../src/Taskboard.Desktop/StageConversationPicker.cs) | 捕获物理按下/前台归属、钩子及原始输入去重、系统阈值双击、菜单/阶段区边界；分派到保存、暂存或整组隐藏 | 只实际外部点击收起，不因失焦/Alt+Tab；列表卡片不当外点；不吞其他应用输入；阶段内外收起层级不同 |
| [CardPresentation](../../src/Taskboard.Desktop/BoardController.CardPresentation.cs)、[AutomaticMoves](../../src/Taskboard.Desktop/BoardController.AutomaticMoves.cs)、[TaskCardMotion](../../src/Taskboard.Desktop/TaskCardMotion.cs)、[WorkspaceRevealBatch](../../src/Taskboard.Desktop/WorkspaceRevealBatch.cs) | 暂留阶段及人为操作整理、自动移列的退出/进入动画、共同开合准备屏障 | 人为整理与后台自动更新不同；自动状态/保存不等180ms动画；取消/反向/隐藏恢复不留重复/零高卡片；准备超时不锁死组 |
| [CodexState](../../src/Taskboard.Core/CodexState.cs)、[Observer](../../src/Taskboard.Desktop/CodexObserver.cs)、[FileActivity](../../src/Taskboard.Desktop/CodexFileActivity.cs)、[行为读取](../../src/Taskboard.Desktop/CodexBehaviorSource.cs) | 本机结构来源/准确轮次、结构化请求、前台SidebarSource/Observer.Sidebar原生控件及新执行核对、聚合、代次/epoch和重连保护；ICodexActivitySource给出只读文件投影（轮次/投影进度/rollout字节与写时间）；Controller分派事件、通知卡片与保存 | 来源验证不在控件里猜；已确认等待不被Unknown/另一运行盖过；真实恢复整卡才清红点；不扩大只读操作 |
| [安装发现](../../src/Taskboard.Desktop/CodexInstallationDiscovery.cs)、[初始化流程](../../src/Taskboard.Desktop/CodexInitialization.cs)、[控制器入口](../../src/Taskboard.Desktop/BoardController.AiInitialization.cs)、[设置控件](../../src/Taskboard.Desktop/SettingsAiInitialization.cs) | 首次一次提示、本机目录检查、动态登记与路径身份、升级路径修复备份、只读关联聊天测试；消费者为启动、设置、Observer及阶段目录 | 原服务不清空、不自动选聊天/发消息、不重启Codex；测试不发任务事件、旧绑定结果丢弃；分项能力和原桌面生命周期分开；六列、共享背景/命中/动画不改 |
| [ReminderClock](../../src/Taskboard.Core/ReminderClock.cs)、[Archives](../../src/Taskboard.Desktop/BoardController.Archives.cs)、[AtomicStore](../../src/Taskboard.Core/AtomicStore.cs)、[ConfigurationBackup](../../src/Taskboard.Core/ConfigurationBackup.cs)、[DataPaths](../../src/Taskboard.Desktop/DataPaths.cs) | 日历到期、完成天数/归档上限、串行写入/备份、配置隔离/路径互斥；由Controller协调定时和Flush | 到期原因和周期配置分开；未提交编辑保护；配置不含任务；数据写入不依赖动画完成；路径切换不自动合并/覆盖 |
| [NotificationListeningService](../../src/Taskboard.Desktop/NotificationListeningService.cs)、[NotificationSoundLease](../../src/Taskboard.Desktop/NotificationSoundLease.cs)、[StartupLinks](../../src/Taskboard.Desktop/StartupLinks.cs) | 外部通知/原音租约和系统启动入口，涉及真实电脑环境 | 与纯UI、隔离数据测试分开；不误改真实声音、自启或用户Codex；真实系统效果需专项证据 |

## 已核实的关键链路

0. **阶段删除与设置排版**：EditorSurface圆形X→BoardEngine.RemoveStage，仅改副本，保留最后阶段和至少一个运行，当前指针转向剩余运行阶段，经原SubmitSession链验证/合并/落盘。BoardColumns保护全部内置角色，Preferences读取旧删三列恢复隐藏，Validation保留有效剩余指针、不再要求阶段0永远存在。SettingsLayout组织通用页，归档页显式构建自身分区和紧凑预览；原始控件处理和归档身份保护保持。SettingsSurface仅显示真实自动保存状态，原Flush失败保留并定时重试；无顶栏手动保存。BallSurface.EdgeHide的ApplyEdgeBadgeFrame同帧更新红点/数字，预布局避免结束帧重排，不改100ms位移、几何或输入。

1. **正式保存**：EditorSurface.SaveAndClose → BoardController.SubmitSession/SubmitSessionLocked → 复制会话和状态、临时BoardEngine.Submit验证候选 → Store.SaveAsync先落盘 → 正式Engine.Submit合并/通知 → 成功才关闭。普通脏数据另经OnDirty/Flush延迟保存。不能把正式保存误写成“先改正式内存、等待动画后再Flush”；失败需保留窗口及输入。
2. **外点操作**：OutsideClickObserver/原始输入 → 物理时间及输入归属/去重 → ObserveWorkspaceOutsidePress或ObserveOutsidePress → 整组隐藏、草稿暂存、编辑SaveAndClose、设置隐藏。不是统一“窗外关闭”，也不是焦点变化自动隐藏。
3. **Codex后台变化**：只读记录/行为观察器与前台补充核对准确会话及轮次 → Controller.AttachCodex → CodexState.Apply → NotifyCodexChange → Engine.Notify/OnChanged/保存通知 → 自动移列动画。NotifyCodexChange先记录状态与顺序，再启动装饰动画。
4. **时间提醒**：Controller.TickReminders → ReminderClock.Tick → Engine.Notify → 保存及显示 → 动画。Codex恢复清红点由真实整卡状态决定，不能由通知/动画完成决定。
5. **草稿生命周期**：关闭整个宿主 → 暂存编辑并布局 → DraftStackSurface.ClearClosed延迟清理；Layout/快速重开取消旧清理。抽牌的合成快照保留，不把它和普通开合真实裁切混用。
6. **归档编辑层级**：EditorSurface.RefreshArchiveCaption按归档状态调整TaskEditorWindow：归档为2，与设置同层按激活先后显示；活动任务为1。构造及Controller.OnChanged刷新。SurfaceWindow排序、草稿/绑定/球不改；影响与真实命中、保存/取消归档及普通编辑/草稿回归见[桌面验收](../ACCEPTANCE-DESKTOP-0.9.42.md)。

红点当前显式源码入口：BoardEngine.Edit打开编辑、Submit正式保存、SetStageRunning列表运行标记、Move移列会清点；CodexState.Apply仅可信实际恢复且整卡回进行中清点。此处是实现定位，不把所有鼠标点击一概解释为清点，也不因整理而改这些入口。

以上链路由源码直接核对，非运行验收。外部信号生命周期、鼠标路由、实时背景仍需实际检查。

## 按变更选择回归

| 修改范围 | 必须覆盖的控件/操作 | 可定位的已有检查入口 | 仍需人工或专门环境 |
| --- | --- | --- | --- |
| 背景/透明度/窗口模板 | 列表空白右键、标题拖动、22×22整组/单列缩放、卡片、新建、滚动条；新建与编辑首开/重开/设置响应/多稿/抽牌 | Ux0914Checks、Ux0915RenderChecks、Ux0917Checks、VisualChecks | 同背景黑/白/细纹理透底和模糊；展开后1至6秒及异常帧；真实系统+内部命中+操作 |
| 球形状/边缘隐藏 | 自由/吸附矩形左右键、拖动、透明角、菜单8DIP位置、1/4条带/数字红点、快速反向 | Ux0913Checks、Ux0913RenderChecks、Ux0914DesktopChecks、球LiveBall | 当前旧离屏动画失败不可跳过；混合DPI/相邻屏幕裁切/真实鼠标 |
| 普通开合/关闭/清理 | 新建/编辑/设置首开、第一次外点、第二次保存/暂存、关闭开始/结束、最后草稿、快速重开、关联区/保存中 | Ux0914ClosingChecks、Ux0915Checks、Ux0917Checks、SharedMotionChecks、WorkspaceChecks | 冷帧/忙碌帧像素与输入；不能以单个模拟Click通过代替 |
| 名称行布局/滚动 | 3/4/5/6中文、英文混合，短长阶段，同卡窄宽变化，双文本滚动，单阶段/灵感/未绑定例外 | Ux0920Checks、Ux0910Checks、Ux0914Checks | 最新布局离屏结果不代替鼠标/多屏；卡片操作范围也检查 |
| 编辑/草稿/保存 | M04动作表全部分支、全字段暂存、未选状态、空草稿、重复保存、写入失败、后台并发、重启独立回读 | Taskboard.Checks、StageFeatureChecks、DraftDealChecks、FloatingInteractionChecks | 新建/已建分别检查，真实双击需协调 |
| 阶段/拖动/归档 | 至少一运行、多运行暂留/全局整理、后台不整理、拖动取消、20步撤销恢复、完成天数及40条确认、未提交编辑保护 | StageRules、ArchivePolicyRules、Ux098Checks、CardPresentation、CardDragSession | 真实手势和列表滚动，不滚用户列表凑动画 |
| Codex/提醒/合并 | 多阶段等待优先、跨重连、旧终态隔离、改绑/归档恢复、ID更换、整卡恢复清点、编辑合并、实际卡片/保存 | CodexRecoveryRules/CodexStopRules/CodexQuickStopRules、CodexTurnTrackerChecks、CodexObserverChecks、CodexDesktopLifecycleChecks | 获准原桌面完整生命周期，真实failed/额度仍缺；不耗尽额度、不重启当前Codex |
| AI初始化/设置/连接保存 | 首次/稍后/重开、重检、原路径/服务、目录可读但观察不可用、未知版本、端口冲突、选择器及配置落盘 | `--ai-init-verify`离屏夹具；`--ai-init-environment-verify`实际登记和CLI版本检查；[本地验收](../ACCEPTANCE-0.9.21-ai-init.md) | 不将离屏事件当物理鼠标；不将准备好当具体聊天生命周期；用户确认后实际打开及原桌面问答/批准仍待协调 |
| 数据/配置/启动/外部声音 | 路径互斥、损坏拒绝、配置无任务/引用、恢复、防旧版降级、原音租约恢复 | Taskboard.Checks、IntegrationChecks、NotificationListeningChecks | 正式自启登录、真实通知替换需专项，不碰正式数据 |

表中“入口”是选择和定位用，不承诺任一入口完整覆盖该行。阅读Program.cs确认参数及副作用，始终明确隔离`--data-dir`；会占用桌面输入、切换前台或临时使用剪贴板的工具，必须按根AGENTS.md先询问用户是否方便，收到明确同意后才开始，不能只提示就执行。合成规则、离屏控件、真实鼠标/像素、原桌面生命周期分别记录，历史检查数量不能计作本次通过。


## 当前共享变更与回归选择

列表/球预览继续共用StageDetailsText末行避让、禁用命中表面及两行下限；编辑/快捷详情焦点展开和失焦收起、原输入实例及排序实时让位保留。本轮排序Thumb仅改矢量墨迹居中，不改24DIP输入范围、捕获或提交。

AiAttention集中管理事件身份、本地确认与来源状态的区别，消费者为两平台状态汇总、任务移列和逐阶段跳转颜色。确认不删除准确请求，跨未知/重连保留，下一请求/新执行后待处理重新生效；计时红点独立。BoardController.AiCreation负责候选先落盘后提交、作用域/迟到/并发保护；ConversationDiscoverySource隐藏三平台结构源与历史基线，单会话错误隔离，默认不开启。快速拖动复用原准确目标解析和同一创建事务。

WorkspaceRevealBatch及SurfaceWindow三个实时裁切入口用于六列、球、编辑/草稿/设置和辅助窗。准备后登记Start，全部由同一次Normal回调同步启动，避免最后参与者在Render中内联、其余Input等待。原背景、控件、100ms、超时、层次及快速反向保持；外点/球复用同一入口。真实回归含原生裁切独立进程采样、画面、标题拖动/空白右键/单列与22DIP整组缩放、新建轮廓、球透明角及取消/重开，不能只凭时钟或截图采样宣布同步。

归档正文编辑与操作继续使用原入口；仅归档页显式启用CompactArchivedPreview，隐藏活动卡片的阶段占位和跳转按钮，保留完整任务数据。日期/来源和恢复/删除包在同一外卡边界，确认及记录身份保护保持。全部活动列菜单显式允许归档；非AI球菜单恢复原层级/标签，AI统一连接保持，菜单位置仍8DIP。消费者为卡片、设置、球/托盘；具体本轮依据见[0.9.46验收](../ACCEPTANCE-0.9.46.md)。原0.9.45菜单描述已移入[历史快照](../history/20261109-0946/ARCHITECTURE-SHARED-CODE.md)。

颜色组件ColorPickerPanel/ColorSpectrum由Rendering拥有，平台/优先级下拉及管理入口共用；候选与正式色分离，Controller核对数据/选项身份后提交并刷新引用，最近12色随偏好保存。OptionPalette只在加载和配置导入升级旧标准色及Claude默认项，复制副本不触发隐式升级。活动卡片、共享背景/动画/原命中范围不变。

专注视图：Workspace/BoardController.FocusLayout协调有效可见角色及独立几何；ColumnSurface.View管理标题AI/完成开关、完成显示排序和多列落点；Rendering/AdaptiveCardPanel仅用于专注进行中/待操作，原单列VSP保留。SurfaceWindow对专注已完成选择原live-scroll左锚裁切，无泛光，其他列表竖向裁切保持；IsInteractive识别ButtonBase，勾选标题不拖窗。消费者为普通/专注列表、编辑及所有共享标题；原0.9.48回执只保留历史身份。本轮仅改变Done独立预留摆放及同层排序，主双窗与卡片不重排；SettingsHelp圆内命中、44DIP关闭和ColumnSurface的18DIP青色缩放图标均为局部模板，不改共享背景或输入基类。球/托盘新增同一偏好入口，不改变菜单位置。0.9.49另验41点真实输入，含多点问号、原生裁切、拖放、菜单、标题/组缩放/新建/滚轮；非零滚动420像素和快速反向由实际便携程序集专项补验。


## U15共享影响与本轮回归选择

Workspace的BallPlacement统一球、搜索/预览/菜单的左右和上下工作区策略；BallSurface负责位置/拖动/隐藏/持久化。Rendering仅给球增加左右固定画布锚点和FlatLeft默认关闭参数，其他列表/编辑/设置/草稿继续原裁切及层次。菜单仅目标行绘制开关，其他行操作与托盘定位保持。实机回归选择空白右键、标题拖动、22DIP组缩放、单列18DIP缩放、新建轮廓、滚动，以及两宽一窄原位开合。

SettingsHelp只给七类复杂选项提供统一双图窗口；原照片字节保持，以CroppedBitmap裁区域，Root接收Escape，不修改共享背景。CardDragSession只负责跟随/会话，ColumnSurface管理唯一占位/逻辑后继，AdaptiveCardPanel保留视口虚拟化与按任务缓存高度；数据在Drop提交，取消不覆盖后台变更。

Codex目录/亲缘归Codex模块，名称同步归Binding，自动发现归AiPlatforms，事件聚合与阶段确认归Core/AiState、提交/编辑合并归Tasks、来源形状校验归Domain。来源和阶段身份隔离、等待优先、重连/改绑、编辑合并、实际保存及旧事件确认均需回归，其他平台配置与事件格式保持。


## U16共享影响与回归选择

Editing/ReminderEditor替代EditorSurface旧内联日期/时间区，持有原编辑副本，只经原保存链提交；原22DIP唯一启用范围、一次/每日/每周及ReminderClock/到期/草稿排除保持。StageDetailsEditor仅把未运行空白下限恢复一行，180ms、焦点完整展开及输入实例保持。StageDetailsText消费者为列表和球预览，仅补硬换行截断的真实省略号，原逐行宽度、末行图标避让、两行下限及原文保持。

Workspace/FocusLayout区分手动保持和拖动临时状态，450ms反馈后侧向收起沿原100ms实时控件裁切，双宽窗位置/卡宽/多列/非零滚动保持；CardDragSession退出清理临时状态。只选择Done侧向开合，不改其他表面背景/输入/层次。Settings关闭局部复用草稿48×40模板，帮助44DIP保持；OrbMenuRenderer按实际字体与DPI测量目标文字/开关/箭头，球与托盘共用，原8DIP定位及菜单动作保持。DeviceOnboarding独立本机原子记录，Workspace协调五步浮窗，不自动建卡或发消息。

本轮回归：列表空白右键、标题拖动、22DIP组和18DIP单列缩放、卡片/新建/滚动；新建/已建保存失败/取消/切换/重启回读，提醒核心到期与错过周期；完成窄窗拖放/取消/固定/反向/模式及组隐藏，原生裁切和无白条；关闭整块命中、帮助整圆和完整双图；菜单完整文字/右侧空白/键盘及四角；教程首次/迁移/跨目录/损坏与步骤。当前桌面证据写入U16验收，不借用U15检查数。提醒日历仅局部复制模板补星期资源，不修改全局Theme；根球/托盘菜单只同步窗/行横向尺寸，按原生布局取得行高，子菜单沿原生操作。Done中间帧读取实际GetWindowRgn区域，画面按截屏前后区域包络核对，不能把父窗保持原宽误判为无动画。

## 0.9.53 批次1共享影响与回归选择

`RoundedInput` 统一承接全程序 `TextBox` 的圆角留白精确光标定位（基于行字符边界而非原始字符索引）、首尾行方向键跳转（跳过 `IsReadOnly`、`Key.ImeProcessed` 与通过 `EdgeArrowNavigation="False"` 关闭的时间框，检索框仅在结果为空或向上时复用）以及暗色圆角右键菜单（#EE1E2A3C 轻量半透明圆角弹窗，不引入独立 `DesktopWindowTarget` 合成树）。`StageDetailsEditor` 取消运行中阶段180DIP失焦自动收起，平衡底部内边距并在展开点击瞬间抑制初始拖选、打开右键菜单期间维持展开；`EditorSurface` 外层 `ScrollViewer` 设为 `Focusable=false` 防止点击编辑器底部抢焦点；`StageReorderGesture` 仅将 `↕` 矢量路径改为 `GetWidenedPathGeometry().GetOutlinedPathGeometry()` 单次填充以消除重叠发白，不改变24DIP命中与拖动提交契约。`Theme.xaml` 仅补充 `RadioButton` 圆角暗色样式、`ContextMenu`/`MenuItem` 快捷键列及 `StageBodyInput` 底部内边距，不改变 `SurfaceWindow` 背景或外部命中边界。

## 0.9.53 批次3共享影响与回归选择

- **悬浮球展开闪烁修复（#14）**：`BallSurface` 在展开准备期直接以完整可见态（`Opacity = 1`）随 `ClipHorizontal` 原生裁切展开，不再在 `Width > 64` 处突变透明度，并缓存停靠几何；`SurfaceWindow.LiveBall` 使用 `SWP_NOCOPYBITS` 并紧跟 `SetWindowPos` 更新 `ClipHorizontal` 与子画布；`LiveBackdrop.Update` 在仅平移区域坐标时就地更新 `ContainerVisual.Offset` 并请求提交，消除逐帧销毁重建毛玻璃视觉树引发的搜索框闪烁。
- **Alt+Tab 唯一窗口与切离/切回（#15）**：`BoardController.TaskSwitcher` 引入透明代理窗 `AppSwitcherWindow` 作为本程序在任务栏/`Alt+Tab` 的唯一入口；所有 `SurfaceWindow` 维持工具窗不入切换器。切离外部应用时（排除 IME、任务切换器浮层、内部主动关窗保护期、拖拽与原生对话框）收起到悬浮球；通过 `Alt+Tab` 切回代理窗时调用 `ActivateFromTaskSwitcher` 自动展开并激活多列表/浮窗；主动关闭活动浮窗时平稳把焦点交回看板可见窗，不影响全局快捷键与多弹窗内部交互。
- **IME 选词框免疫与全屏外点收起（#29 / #30）**：新增 `ImeWindowFilter` 按 Win32 类名、进程名（区分 `TextInputHost` 与普通 UWP 应用、排除搜狗浏览器等非输入法进程）、`WS_EX_NOACTIVATE` 及属主进程识别输入法候选框/状态栏，在 `OutsideClickObserver`、`ColumnSurface.Inline` 失焦与前台切离判断中均予免疫；同时修复 `OutsideClickObserver.CapturePress` 32 位 tick 回绕与无指针回退、补充全屏区域限位检测（`GetClipCursor`）并结合合法前台切离事件实现全屏游戏点外部关闭。
- **长文本性能优化（#20）**：`Models` 为 `WorkItem.DisplayTitle` 移除全量换行 `Split`、为 `Copies.Of/Same` 增加复用不可变字符串的强类型深拷贝/比较（保留 `OnDeserialized` 全暂停阶段与默认列修复等价性）；`StageDetailsText` 增加了宽度/DPI 布局缓存与省略模式预截断；`TaskCardLayout.DetailsInkTop` 缓存首行墨迹测量；`StageDetailsEditor` 收起态跳过全量测量并缓存展开测量结果；`AdaptiveCardPanel.OnItemsChanged` 对增删改做增量容器维护而非 `RemoveAll()`；`TaskCard` 以无 JSON 结构快照代替序列化，`ColumnSurface` 拖动占位复用 `CardDragSession` 冻结快照并跳过无关列刷新。
- **回归入口**：`python tools/check_project.py`、`Taskboard.Checks`、`Taskboard.Binding.Tests --batch3-surface`、`AiModulesChecks`、`AiListeningChecks` 均已离屏通过；真机 `Alt+Tab`、真实中文输入法选词框点击、真实独占/无边框全屏游戏及 `RenderProbe` 视觉帧率需后续真机桌面实测确认。

## 本轮批次 B 共享表面层与输入法改动（待打包）

- **悬浮球展开闪烁根治（#14 返工）**：抛弃逐帧 `SetWindowPos` 缩窗移画布与截图交接；`SurfaceWindow.LiveBall` 在动画开始前一次性将窗口设为 `ExpandedWidth` 并保持静止（动画帧零 `SetWindowPos`），每帧仅推进单层胶囊宽度 `CurrentCapsuleWidth`（`LiveBackdrop.Update` 就地更新 `holder.Offset/Size` 与 `CompositionRoundedRectangleGeometry` 零重建 + `Root.Clip` 同步裁切）并平滑过渡搜索栏 `expanded.Opacity`（展开前期透明占位渐入到 1、收起前段渐出到 0），收起结束一次性恢复 `58×58`；`SurfaceWindow` 的 `WM_NCHITTEST` 按 `CurrentCapsuleWidth` 精确计算动态胶囊边界并对透明外区返回 `HTTRANSPARENT`。
- **Alt+Tab MRU 排序与隐藏底部任务栏（#15 重构）**：`AppSwitcherWindow` 移除 `WS_EX_APPWINDOW`（保持无 `WS_EX_TOOLWINDOW` 顶层窗口），通过 COM `ITaskbarList::DeleteTab` 在启动、激活、前景变化与 `TaskbarCreated` 时移除底部任务栏按钮；本程序活跃时调用 `Native.RaiseTaskSwitcherToTop`（`SetWindowPos(HWND_TOP, SWP_NOACTIVATE)`）将代理窗升至普通非 Topmost 窗口 Z-order 顶部以实现 `Alt+Tab` 按 MRU 排序。因自动化环境运行在隔离桌面无法触达用户交互 Explorer，Windows 11 Build 26200 下 `DeleteTab` 是否与 `Alt+Tab` 耦合仍需用户在真实桌面复核。
- **输入法单按 `Ctrl` 切换中英文修复（#13 方案 A）**：`GlassContentHost` 监听子窗口鼠标激活/按下并通知顶层母窗激活，`SurfaceWindow` 在 `WM_SETFOCUS` 与 `Root` 内文本框获得键盘焦点时同步顶层激活与 `SetFocus(ContentHandle)`，同时严格保留 `HasFocusWithinCore() == false`（防吞 `WM_CHAR`）、`WM_NCACTIVATE` 防白框与 `#29` IME 免疫。
- **0% 透明跳过桌面实时模糊（#19 方案 A）**：`LiveBackdrop` 在 `opaqueBackdrop == true`（`GlassTransparency <= 0.001`）时不创建/不挂接 `HostBackdropBrush`，改用实心 `#FF192333` `CompositionColorBrush` 绘制；透明度调回 `> 0%` 时同窗口无缝恢复 `HostBackdropBrush`；`SettingsSurface` 三个透明度滑块同步注明省性能说明。
- **国内外主流输入法扩展（#29）与边缘吸附隐藏悬浮球修复（Item 10）**：`ImeWindowFilter` 补齐豆包（主进程按悬浮条样式特征放行、聊天主窗严格排除）、微信（WeType）、智谱 AI、讯飞、搜狗、百度、QQ、手心、小狼毫、PIME、微软中日韩宿主、Google Japanese Input、Mozc、ATOK；`BoardController.HideBall` 先置 `BallVisible = false` 再调用 `Ball.HideCompletely()` 立即停边缘定时器/边缘动画/展开动画并复位 `IsEdgeHidden`，且所有展开/刷新/菜单关闭入口在 `!IsBallEnabled` 下绝不反弹显示。

## 本轮批次 C 共享表面、编辑器与启动更新改动（待打包）

- **长文本真实 UI 打开性能彻底修复（#20）**：`StageDetailsEditor` 消除 `MeasureOverride` 双重 `Measure` 与构造函数本地 `FontFamily` 触发的 `Loaded` 二次排版；收起态将 `Input` 置为 `Visibility.Collapsed`（零排版成本）；超长（`> 1500` 字符）未聚焦运行中阶段复用单一 `preview` `TextBlock` 先显示首屏切片（360/64 字符）并在空暇/滚动时展开，点击/聚焦/右键时无缝水合至真实 `TextBox` 并清撤销栈（100% 不丢字，严格保持 `#10`/`#27`/`#11`/`#18`）。`EditorSurface` 对超长任务预设 `ScrollViewer` 竖向滚动条避免 `Auto` 双次换行重排并静态缓存按钮测量；`SurfaceWindow.LiveScroll` 跳过首次打开 `ContentHandle==0` 时的无效预布局并将准备期等待压缩为单帧（25ms 兜底），端到端打开耗时由 1731–3897ms 降至 202–360ms。
- **待操作列常驻 + 系统级鼠标穿透（#25）**：`SurfaceWindow` 与 `Native` 新增 `SetClickThrough`（对母窗及 `GlassContentHost` 子窗同步设置/清除 `WS_EX_TRANSPARENT` 并在 `WM_NCHITTEST` 返回 `HTTRANSPARENT`，阻止抢占激活）及 `Native.SetPinnedOverlay` 层次同步；`BoardController.PinnedAction` 管理仅内置 `Lane.Action` 列的常驻状态、独立透明度（`Preferences.PinnedActionTransparency` 默认 70%，联动 `#19` 0% 跳过桌面模糊）以及与“常驻”按钮位置/DPI/显隐严格同步的轻量置顶解除浮窗 `ActionPinOverlayWindow`（`WS_EX_TOOLWINDOW`、无 `WS_EX_TRANSPARENT`）。外点/`Alt+Tab` 收起其他列表时保持待操作列与解除浮窗常驻；全局快捷键隐藏时一并隐藏、再次唤起严格保持常驻穿透态。
- **开机自启容错诊断与固定目录更新体系（#02）**：`StartupLinks.DiagnoseAutoStart` 区分五种健康态并在设置页高亮提醒 Windows `StartupApproved\Run` 禁用；`Program.RunWithStartupRetry` 为 `--startup` 增加瞬时 IO 重试与防死弹窗；`AppUpdater` 接入公开 GitHub Releases API、`SHA256SUMS.txt` 强校验与安全解压，配合 `tools/scripts/install.ps1`/`rollback.ps1`/`uninstall.ps1` 及 `Build-Portable.ps1` 实现 `%LOCALAPPDATA%\MoyuDesk\app` 固定目录原子升级与一键回滚，正式数据 `%LOCALAPPDATA%\InstantTaskboard` 永不触碰。

## 0.9.55 共享表面、输入法、常驻穿透与统一按钮样式改动

- **纯 TSF 输入法单按 `Ctrl` 中英文切换根治（#13）**：`SurfaceWindow` 外层收到 `WM_SETFOCUS` 将焦点转发给 `GlassContentHost` 后显式设 `handled = true`，防止外层 `HwndKeyboardInputProvider` 把 TSF 文档管理器退回 `EmptyDim`；`GlassContentHost` 同步关联默认 IMM32 上下文；`Native.EnsureTsfControlKeyBridgeInstalled()` 通过 `ComponentDispatcher.ThreadFilterMessage` 将 `WM_KEYDOWN/WM_KEYUP (VK_CONTROL = 0x11)` 连同原始扫描码直发 `ITfKeystrokeMgr`，避开 WPF 将其改写为 `VK_LCONTROL/VK_RCONTROL` 导致的漏识别，并严格放行非 `0x11` 组合键（`Ctrl+A/C/V/Z`）及 `IsInputMethodEnabled == false` 控件（如快捷键录制框）。
- **无主不透明实体 `Alt+Tab` 代理窗（#15）**：`BoardController.TaskSwitcher` 的 `AppSwitcherWindow` 改为真正无主（`GW_OWNER == 0`）、原生可见、`AllowsTransparency = false`、`Opacity = 1` 的 `160×90` 离屏实体窗体，保留 COM `ITaskbarList::DeleteTab` 隐藏任务栏按钮与普通 Z-order MRU 置顶。
- **待操作列原创矢量图钉与常驻穿透滚轮（#25）**：新增 `ActionPinIcon`（`Desktop/Rendering`）统一绘制列标题栏与迷你解除浮窗的 24×24 原创矢量图钉正圆按钮；`BoardController.PinnedAction` 与低级鼠标钩子共享原子 `PinnedWheelSnapshot`，悬停溢出常驻待操作列内滚动滑轮即通过 `QuickScroll.ScrollByWheelDelta` 平滑滚动，无溢出、窗外或隐藏时完全放行到底层软件。
- **全局按钮形状与高对比色板升级（反馈 #6）**：`Theme.xaml` 与 `Ui.cs` 新增专用高对比按钮色板（`ButtonBrush`/`ButtonHoverBrush`/`ButtonPressedBrush`/`ButtonBorderBrush`/`ButtonTextBrush` 及 `AccentButton`/`CircularIconButton`/`DashedEntryButton`/`SettingsNavButton` 样式），所有常规文字按钮与关闭钮由 `CornerRadius="20"` 改为 `CornerRadius="9"` 圆角矩形，固定正方小图标按钮保持正圆，虚线新建入口圆角协调为 `12` 且保留非零 Alpha 全区命中，`TextBox`/`ComboBox`/`DatePicker` 与 `RoundedInput.RadiusProperty` 同步收紧为 `10`（`StageName` 保持 `14` 并同步边框与 `Clip`），通过 `Style.Triggers` 消除本地属性遮蔽确保悬停提亮 100% 生效。
- **定时提醒默认对齐（#04 / 反馈 #5）**：`Reminder.DefaultNextLocal(DateTime now)` 统一计算当前日期下一个小时整点，`ReminderEditor` 未启用初始展示与从未勾选→勾选切换时按操作时刻实时对齐，已有启用提醒打开编辑严格保留原日期时间。

## 0.9.56 共享表面、零任务栏、图标与实时深浅主题引擎改动

- **彻底移除 `Alt+Tab` 代理窗（#15 / 反馈 #1）**：删除 `BoardController.TaskSwitcher` 中 `AppSwitcherWindow` 及 `SurfaceWindow` 中 `ShowInTaskSwitcher`/`RaiseTaskSwitcherToTop`/`HideTaskbarTab`，确保全程序 0 任务栏窗口（仅留系统托盘 + 悬浮球 + 全局快捷键），同时完整保留前景切离自动收起、IME/对话框免疫与内部关窗保护。
- **常驻图钉造型与尺寸优化 + 穿透滚轮手感统一（#25 / 反馈 #2 + 追加项）**：`ActionPinIcon` 删去顶帽左右凸起改为平滑圆润端头、按钮放大至 `26×26`、`Viewbox` 放大至 `18×18` 并占满约 85% 画布，并消除全部 `static readonly Brush` 改为实时读取 `ThemePalette.Current`；`ColumnSurface` 与 `BoardController.PinnedAction` 将 `ScrollChanged` 解耦为轻量的可滚状态变化通知并对 `pinToggle.LayoutUpdated` 做位置变化去重，彻底消除滚动动画期间每帧两次触发 `SyncActionPinOverlay` / `Native.RaiseBall` / `EnumWindows` 引发的卡顿；`OutsideClickObserver` 同步加入修饰键过滤（对齐 `QuickScroll.Wheel`）并提升调度优先级至 `DispatcherPriority.Normal`，使常驻穿透态与正常态滚轮滑动走同一 `QuickScroll` 90ms 缓动曲线与连滚累加行为。
- **按钮宽高比适配与高对比控件补齐（反馈 #3）**：`BallSurface` 展开横框搜索框对齐 36 高、`×` 调整为 `28×28` 正圆、`设置` 调整为 `44×36` 与搜索框平齐，并同步补齐提醒编辑/气泡/解绑/归档等文字按钮比例；`Theme.xaml` 与 `Ui.cs` 提取 `CheckSurfaceBrush`/`CheckHoverSurfaceBrush`/`CheckBorderBrush`/`CheckCheckedFillBrush`/`CheckMarkBrush`/`InputBorderBrush`/`SliderTrackBrush`/`ComboArrowBrush` 语义资源全面提亮 `CheckBox`、`RadioButton`、`Slider`、`TextBox`、`ComboBox`、`DatePicker`。
- **正式猫咪圆标接入（反馈 #4）**：`Taskboard.Desktop.csproj` 嵌入 `Assets/orb.png`，`Brand.AppIconImage()`/`Brand.OrbGlyph()` 高画质渲染正式圆标并保留无 `Application.Current` 安全矢量兜底，`BallSurface` 确保计数红圈与边缘吸附红点严格位于图标上方清晰可读。
- **全局实时深浅色与多配色主题引擎 + 全新“外观”设置分页（反馈 #5）**：
  - Core 层（`Models`/`Validation`/`ConfigurationBackup`/`AtomicStore`）新增 `ThemeMode`（`Dark`/`Light`/`System`，默认 `Dark`）及 `DarkThemePreset`（默认 `midnight-cyan`）/`LightThemePreset`（默认 `daylight-mist`），支持 `Validation.Normalize` 安全回退与配置导入导出保留。
  - 新增 `ThemePalette`（`Desktop/Rendering`）内置 5 套深色与 4 套浅色高对比预设、`IsSystemLightTheme()` 只读注册表查询及 `Resolve`/`Apply` 实时资源刷新；`Theme.xaml` 全量切换为 `{DynamicResource}` 消除残留硬编码暗色；`Ui.cs` 改为动态读取 `ThemePalette.Current` 并对标签/输入框/面板做资源引用绑定及浅色高对比标签色自适应。
  - `LiveBackdrop.Update` 接收并比对 `surfaceColor`（仅颜色变化时刷新实色画刷与列染色层，100% 保留 `#14` 悬浮球就地零重建更新与 `#19` 0% 透明跳过 `HostBackdropBrush`）；`SurfaceWindow.GlassTintBrush`、`ColumnSurface.ApplyGlassTint`（`#01RRGGBB`）、`BallSurface.ApplyGlassTint` 及 `Native.TryGlass` / `UpdateWindowTheme`（`DwmSetWindowAttribute(20, IsDark ? 1 : 0)`）全面联动当前主题底色；`OrbMenuRenderer`（托盘/球右键菜单）、`StageReorderGesture`、`EditorSurface`、`StageDetailsEditor`、`StageConversationPicker`、`DraftStackSurface`、`ActionPinOverlay` 与 `SettingsSurface` 同步接入 `ApplyTheme()`，由 `BoardController` 订阅 `SystemEvents.UserPreferenceChanged` 实现实时响应与全局无刷新切换。
  - `SettingsSurface.Sections` 在 `"主设置"` 后新增 `"外观"` 分页（三模式单选 + 5 深色 / 4 浅色 3 圆点预览色卡 + 透明度页快捷跳转）。
  - 回归入口：`python tools/check_project.py`（14 模块 133 文件）、`Taskboard.Checks`（408 项）、`Taskboard.Binding.Tests`（18 + 125 + 150 + 38 项）、`AiModulesChecks`（134 项）、`AiListeningChecks`（84 项）均已离屏通过；真实桌面深浅色切换视觉观感与系统主题变更事件联动仍待真机验收。
