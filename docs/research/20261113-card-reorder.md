# 任务卡片拖动时自动让位：一手源码调研

查阅日期：2026-11-13。范围：Android AOSP Launcher3、gong-wpf-dragdrop 项目源码、Microsoft WPF 官方资料，以及本项目四处指定源码。本文是本轮需求的研究材料；实现和验收由所属任务继续记录。未操作桌面、正式数据或用户进程，未引入第三方依赖，未修改生产代码。

## 结论与适用边界

采用“源卡退出展示序列，目标展示序列插入一个占位，其他卡片按落下后的顺序重新排布”的方案，最适合本项目。业务任务及持久顺序仍只在有效 Drop 时提交。用户本轮要求的是让位后的位置关系，没有把平移动画设为强制要求；因此正确的布局和取消恢复先满足，动画可以单独评估，不能为了动画改变既有触发区域、鼠标操作或窗口层次。

Android 主屏本身支持自由二维格子和不同跨度组件，不能直接等同于本项目按行阅读的任务序列。Launcher3 文件夹的“空位沿序列传播”更接近本需求；主屏实现则提供临时布局、预览保持、取消回退和释放提交的设计参考。以下“推荐”均为本项目方案推导，不是引用项目已经具备的功能。

## 一手资料及核对到的机制

### 1. Launcher3 主屏的重排求解

[ReorderAlgorithm.java，固定提交 d9229faff292b0b7cad7a1de0dabee89d069b118](https://android.googlesource.com/platform/packages/apps/Launcher3/+/d9229faff292b0b7cad7a1de0dabee89d069b118/src/com/android/launcher3/celllayout/ReorderAlgorithm.java)。核对函数：`calculateReorder`、`findReorderSolutionRecursive`、`rearrangementExists`、`pushViewsToTempLocation`、`attemptPushInDirection`。

它先找最近目标格，收集与目标矩形相交的项目，再依次尝试方向性推挤、整组搬移、逐个寻找空位。推挤把接触前缘的项目并入移动群；不可移动项目或越界使该方向失败并恢复临时方案。可缩放组件还可能缩小跨度；最终在重排、邻近空位及原地方案间选择。推挤遍历具有确定顺序，避免依赖对象哈希。它解决的是有限网格内的占据关系，并不是“列表中把某项插到另一项之前”。

本项目借鉴：求解与展示分离、结果确定、失败可丢弃。无需引入自由空位、卡片缩放或二维坐标持久化。

### 2. Launcher3 的临时坐标、预览与提交

[CellLayout.java，master 查阅快照](https://android.googlesource.com/platform/packages/apps/Launcher3/+/master/src/com/android/launcher3/CellLayout.java)。核对函数：`performReorder`、`commitTempPlacement`、`revertTempState`、`animateChildToPosition`。

`MODE_DRAG_OVER` 使用临时坐标；Drop 模式才把临时位置并入正式位置并调用数据库写入。`revertTempState` 恢复正式坐标。`animateChildToPosition` 取消旧动画并从现有预览偏移继续。`performReorder` 在一次重排周期内复用先前方案，使提示、布局和 Drop 对应同一结果；仅在新提示周期重算。模式还包括只检查可接收、不提交或播放动画的 `MODE_ACCEPT_DROP`。

本项目借鉴：指针停住时保持当前目标；只在目标改变时更新预览；取消直接清除临时展示，不对业务状态做“撤销式回滚”。后者尤其重要，因为拖动期间后台 AI 或提醒可能合法改变任务。

[Workspace.java，main 查阅快照](https://android.googlesource.com/platform/packages/apps/Launcher3/+/refs/heads/main/src/com/android/launcher3/Workspace.java)。核对 `onDragOver`、`cleanupReorder`、`mReorderAlarm`：目标格改变且进入目标半径时先展示提示，再安排 `REORDER_TIMEOUT = 650` 毫秒的重排；切换或退出会取消待执行重排。该等待与手机触摸、建文件夹等行为有关，不应照搬为桌面每次让位必须等待 650 毫秒。

### 3. Launcher3 文件夹的空位传播

[FolderPagedView.java，固定提交 3333b0ced8e6743c41909f6f6b916f1f9ec5a004](https://android.googlesource.com/platform/packages/apps/Launcher3/+/3333b0ced8e6743c41909f6f6b916f1f9ec5a004/src/com/android/launcher3/folder/FolderPagedView.java)。核对 `realTimeReorder(empty, target)`、`completePendingPageChanges`。

它用线性 rank 映射行列；目标在空位前或后时，仅沿区间把邻项搬入空位，空位随之移动。当前页播放位移动画，其他页直接调整，开始新重排前结清旧跨页动画。这是有序网格中自动让位的直接参考。注意该函数在预览时会修改运行中 `ItemInfo.rank`；本项目不能直接照抄这部分，因为本项目要求预览期间业务顺序不变。

### 4. gong-wpf-dragdrop 的能力与差异

[DefaultDropHandler.cs，develop](https://raw.githubusercontent.com/punker76/gong-wpf-dragdrop/develop/src/GongSolutions.WPF.DragDrop/DefaultDropHandler.cs)：`DragOver` 选择插入/高亮装饰器并设置 Move/Copy 效果；`Drop` 才移除、插入或移动集合项，并处理同集合源项删除后的索引修正。其默认路径没有构造周围卡片让位的临时展示序列。可借鉴提交边界和索引归一化；仅换用此默认处理器不会得到本需求效果。

[DropInfo.cs，develop](https://raw.githubusercontent.com/punker76/gong-wpf-dragdrop/develop/src/GongSolutions.WPF.DragDrop/DropInfo.cs)：`Update` 对竖向项目用 Y 中点，横向项目用 X 中点判 before/after，另有树节点子项语义。它按面板方向计算，不能直接代替本项目混合高度、先选行再比 X 的行优先网格判定。`UnfilteredInsertIndex` 也表明显示顺序与原集合索引可能不同，Drop 应明确映射。

[DropTargetInsertionAdorner.cs，develop](https://raw.githubusercontent.com/punker76/gong-wpf-dragdrop/develop/src/GongSolutions.WPF.DragDrop/DropTargetInsertionAdorner.cs)：`OnRender` 查插入位置对应容器后画插入线；容器不存在时依设置退回控件或退出绘制。它提供目标标识，不改变邻项布局，也不保证未实现的虚拟项有几何。

[DragDrop.cs，develop](https://raw.githubusercontent.com/punker76/gong-wpf-dragdrop/develop/src/GongSolutions.WPF.DragDrop/DragDrop.cs)：`Scroll` 按指针与 ScrollViewer 四边距离调用行滚动，且由 DragOver 路径调用。可借鉴边缘区和越界限制；本项目如需“鼠标停在边缘也持续滚动”，应由现有拖动会话计时驱动并在滚动后重算目标，不能只依靠鼠标移动事件。

### 5. WPF 虚拟化的约束

[Microsoft：Optimizing performance — Controls](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls) 明确区分数据与 UI 虚拟化：UI 只为可见部分生成容器，数据仍可完整保留。因而未实现容器是正常情况，候选目标必须由任务身份/逻辑序列表达，不能把“找不到视觉容器”解释为“整个列表没有后继”。回收容器的状态也需要跟随任务身份更新。

[Microsoft：Layout](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout) 建议在适用时用 `RenderTransform` 处理视觉变换，以减少布局成本。如果以后加平移动画，应先完成真实目标布局，再将已实现卡片从旧屏幕位置平移到新位置；判断落点使用目标布局，避免移动中的动画矩形反过来改变落点。

版本说明：固定提交仅表示本次确切读到的算法样本，未宣称这些文件属于同一可编译版本；`master`、`main`、`develop` 是可变引用，应按查阅日期理解。未把二手文章、搜索摘要或第三方镜像作为算法依据。

## 本项目定点源码核对

已按仓库约定调用 `index_status`：项目 `Instant-taskboard`，根 `D:/Derp/Documents/0renwumianban`，状态 ready，索引时点 2026-11-12，报告 3 处部分解析及部分未索引文件；继续调用架构、符号搜索、调用路径和代码片段，并读取指定真实文件核对。本节是 2026-11-13 研究时的同轮工作区快照，主任务可能继续修改，不能当最终交付回执。

| 入口 | 核对到的职责 | 建议延续/补充 |
| --- | --- | --- |
| `src/Taskboard.Desktop/CardDragSession.cs`：构造、Preview、Leave、Dispose | 只生成一次 ghost，计时移动，持有当前目标并负责清理 | 将“当前列 + before 身份”作为稳定落点；退出列/取消时清理唯一占位，最终全部恢复视图 |
| `src/Taskboard.Desktop/ColumnSurface.cs`：Refresh、BeginCardDrag、EndCardDrag | 本轮工作区已从展示项目中过滤 draggedTask，结束后恢复展示 | 过滤只作用于展示集合，不动 Engine.State；后台 Refresh 也持续遵守该过滤 |
| 同文件：SetCardPreview、InsertPreview、ClearCardPreview | 目标改变时移动一个临时 Grid，末尾占位在新建 footer 前 | 维持“一个占位”；不把 Grid 当 WorkItem 候选；占位内容按目标宽度自然测量 |
| 同文件：DropBefore | 普通列表按卡片中点判目标；研究时已加入指针落在占位内部时保留当前目标的判断 | 该保持区是防自激抖动的关键；占位移动后指针未离开有效区不能立刻重新跳回 |
| `src/Taskboard.Desktop/ColumnSurface.View.cs`：GridDropBefore | 先分视觉行再判断 X，符合混合高度行优先顺序 | 行尾后继从完整展示逻辑序列取；不要只在已实现 cards 中取下一项 |
| `src/Taskboard.Desktop/AdaptiveCardPanel.cs` | 每行取最高卡片，按视口生成/回收；项目变动清行高和容器映射 | 保留虚拟化；深滚动时验证占位移动触发行高重估是否改变滚动锚点 |

最后两项是源码推导出的测试风险，本研究没有将其写成已重现的产品故障。`GridDropBefore` 目前从已实现 cards 查行尾后继，下一逻辑项未实现时可能得到 null；`OnItemsChanged` 把未知行估值恢复到 160，深滚动中的占位移动可能导致视口偏移变化。是否触发及如何修复，以所属任务的隔离实验为准。

## 推荐方案与放置位置

### 展示序列与业务提交分开

继续扩展 `ColumnSurface` 现有展示集合，比另外制造一套二维占据模型更合适。对每个列表从最新业务状态得到显示序列，排除拖动身份；仅当前目标列插入一个占位。源列拖出后自然合拢，目标列自然让开，普通列表和自适应网格共享相同序列约定。

示例：原序列 `[A, B, C, D]`，拖 B，候选为“在 D 前”。展示为 `[A, C, 占位, D]`，业务仍是原序列。若拖往另一列，源视图为 `[A, C, D]`，目标视图增加占位。Drop 再调用既有移动/排序入口一次；取消则清拖动身份和占位，由最新业务状态重建视图。不要用启动拖动时的完整状态覆盖回去，否则可能丢失后台事件。

占位应按目标列宽度和目标卡片样式测量，不能把源卡高度当所有目标环境的固定高度。跨宽窄列时标题折行、阶段按钮和正文行数均可能改变。若目标完成列启用完成时间排序，沿用现行 M07-11 的显示规则，不把暂时的手动落点写成新的完成排序规则。

### 几何决定候选，身份保持目标

`DropBefore` 和 `GridDropBefore` 返回稳定任务 ID，null 只表示真正列尾。候选集合只含真实 WorkItem，始终排除源任务、占位和 footer。指针位于当前占位有效矩形内时保持当前目标；离开后再按实际卡片中心/行中心切换。

网格坚持先判逻辑行，再判行内 X 的阅读顺序。长短卡片使用整行的高度边界，不能因某张短卡下方留白就提前掉进下一行。行尾意味着下一逻辑项之前；最后一行未填满的剩余区域才可能表示真正追加。用展示序列找后继，让虚拟化只影响测量，不影响业务候选身份。

若保持区仍不足，可加方向性滞回或短暂驻留：例如新目标需跨过旧/新边界一小段才切换。具体距离和时间应由隔离实验确定，本文不把示例参数设为产品规则。无需照搬 AOSP 650 毫秒；也不要每次 DragOver 都先移除占位，再基于变化后的几何重新插入，形成反馈环。

### 滚动与虚拟化

滚动坐标统一换算到 list/目标面板的实际视口；滚动之后重新读取该帧几何。自动边缘滚动若实施，应在计时器上运行，列改变/非法落点/取消/Dispose 都停止，末端不继续累计偏移。

占位的插入、移位及移除继续通过现有 ItemsControl/面板处理，不关闭虚拟化，不为整列所有任务强行生成 TaskCard。深滚动若重估导致跳动，可按任务 ID 与目标宽度缓存已测高度，并按新的行成员重新合成最大行高；缓存不能仅沿旧行号沿用。另一可行方式是保存视口顶部任务身份及其像素偏移，重排后恢复该锚点。二者应按实测需要采用，避免把未测量的远端任务当作精确矩形。

### 动画作为独立层

本轮不强制动画。若后续需要更接近手机观感，可在布局正确后对已实现卡片做短时平移，并用任务 ID 而非回收容器作为动画身份；中途目标改变时接续当前显示位置，完成/回收/取消时清理残余变换。它不应修改落点算法，也不应引入新的数据提交。

## 可行路线比较

| 路线 | 效果与成本 | 判断 |
| --- | --- | --- |
| 当前展示集合排除源卡 + 一个目标占位 | 自动呈现真正的落下后位置；共享两种面板；业务状态隔离，范围最小 | 推荐，先满足位置关系 |
| 只对旧布局卡片做位移特效 | 看上去让开，但布局占据/行高/虚拟化仍可能保持旧状态，需要维护第二套几何 | 单独采用无法满足真实位置关系，不作为本轮路线 |
| 移植 AOSP 主屏完整二维求解 | 支持自由空位、跨度、二维推挤；要求新的持久坐标与产品规则 | 与本项目线性顺序和行优先规则不匹配 |
| 直接使用 gong 默认插入处理器 | 可得到拖动和插入线；不会自动形成邻项预览布局 | 不足以替代本轮实现；不新增依赖 |

## 边界与验收矩阵

本表为本需求建议验证项；全部属于待所属任务执行的验收输入，不表示本研究已运行。

| 场景 | 应观察的结果 | 需额外核对 |
| --- | --- | --- |
| 同列首/中/尾向前及向后 | 源位置合拢，只有目标空位，邻项位置等于 Drop 后布局 | 业务顺序在释放前不变，无一次偏一项 |
| 跨列、窄→宽、宽→窄 | 源列少一张展示卡，目标按自身宽度准确让位 | ghost 一次快照；占位高度按目标环境测量 |
| 源列只一张、目标为空 | 源视图无残余空洞；目标占位在新建入口前 | 空列仍可接受和取消 |
| 混合高度与同一行短卡下方 | 按整行及行内 X 选目标，不跳读顺序 | 至少覆盖两列/三列与退回单列 |
| 完整行右侧、非完整末行空白 | 完整行后取下一逻辑项；末行剩余区可追加 | 下一行容器未实现仍不误判列尾 |
| 指针停在占位内；边界轻微往返 | 保持目标，无来回移除/重插造成的抖动 | 记录目标 ID 变化次数，不只看截图 |
| 快速跨多张/多行、连续换列 | 只保留最新目标，旧列占位清除 | 不累积多个占位，不留下高度为零的旧卡 |
| 顶/底边缘与鼠标静止 | 若启用边缘滚动，持续且有界；滚动后目标正确 | 取消/离列即停，滚动坐标不滞后一帧 |
| 深滚动处移动占位 | 视口任务及像素锚点稳定，已实现数量保持视口量级 | 验证行高估值重置、容器回收及行尾后继 |
| Esc、非法落点、窗间缝、释放到外面 | 清除全部临时展示，最新真实任务恢复 | 无保存/撤销记录，无补发编辑点击 |
| 有效 Drop 后重启隔离实例 | 只提交一次移动/排序并正确回读 | 状态、列表身份、顺序、撤销/恢复同时核对 |
| 拖动中后台状态改变/自动移列/删除 | 临时展示尊重新状态；失效源或目标不会重建旧数据 | Drop 前重新校验身份与列，不用旧快照覆盖 |
| 完成列时间排序开/关 | 保持现行时间显示排序；关闭可恢复手动序列 | 不把显示排序误写成业务手动顺序 |
| 两种显示模式、原按钮及会话名称拖动 | 原触发区域、专用按钮、绑定拖动和窗口层次保持 | 让位只改变任务排序/跨列的展示关系 |

## 研究过程与验证范围

已完成：一手源码函数核对、方案比较、本项目指定源码阅读及边界矩阵。未在本子任务运行产品构建、行为测试或真实桌面测试；这些由主任务按固定原验收执行，本文不替代功能交付。

读取来源时，PowerShell `Invoke-WebRequest` / `Invoke-RestMethod` 对 AOSP 与 GitHub API 报 `Authentication failed, see inner exception`。随后使用可用的网页工具成功直接打开 AOSP Gitiles 与项目 raw 源码完成研究；Gitiles 个别分支/路径返回 Internal Error 时改读能直接访问的固定提交及已打开的官方路径。未依据不可达页面推断机制，也未把失败访问写成已读取。资料直接链接均已在本轮打开。
