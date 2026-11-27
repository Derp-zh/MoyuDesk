# 待开发需求与问题总表（Backlog）

更新时间：2026-11-27
说明：本表汇总了用户确认后的 **30 项新需求与问题修复**。任何接手开发的新会话在完成其中条目后，**必须自动更新本表状态**及对应的 `docs/current/`、`ARCHITECTURE.md`、`STATUS.md`、`HANDOFF.md` 与 `CHANGELOG.md`。

> **重要业务规则提醒**：任务的“阶段”**只有“运行中”和“未运行”两种状态**，不存在“已完成”状态；点击关联会话弹出的弹窗在项目沟通中称为**“绑定会话专属窗口”**。

---

## 本轮已定稿待开发清单（可直接执行，不含长线规划与未拍板项）

> 若用户要求“完成本轮待开发需求清单”，请直接按本节已定稿规则实施并验收；长线需求（`#05` 手机与电脑共用）与尚待用户拍板的条目（`#02`、`#03` 路线确认、`#13`、`#16`、`#17`、`#19`、`#22`、`#26`）不得擅自脑补动工。

| 编号 | 状态 | 已定稿需求与实施规则 | 涉及模块 | 处理方式 |
| --- | --- | --- | --- | --- |
| **#04** | [x] 已完成（0.9.54；0.9.55 增强默认对齐） | **时间提醒方案重设计**：<br>1. 高频场景以**一次性临时定个点**为主，点击“定时提醒”整行文字亦可切换开关，保留快捷相对时间按钮；**0.9.55 增强**：新建提醒与从未勾选切勾选开启提醒时自动对齐**当前日期及下一个小时整点**（如 `03:40 → 当日 04:00`、`14:00 → 当日 15:00`、`23:40 → 次日 00:00`），已有启用提醒打开编辑严格保留原时间。<br>2. 编辑框里的时间选择器改为**小时（00–23）、分钟（00–59）各自独立的滚动选择列表**，点选即时更新生效，废弃旧24小时方格矩阵二步确认。<br>3. 提醒到期时，**卡片严格留在原本所在列表的原位不动（不再自动移入“待操作”列），仅在原位亮起醒目红点与提示**。 | `M10` / `M03` (`Models`, `ReminderEditor`, `ReminderClock`, `ColumnSurface`, `BoardEngine`) | 已实施并增强 |
| **#14** | [x] 已验收通过（0.9.54） | **彻底修复点击悬浮球展开时搜索框闪烁**：<br>改用全新一步到位定宽方案：展开前一次性将窗口切至 `ExpandedWidth` 并保持静止（动画帧零 `SetWindowPos` 缩窗/移位、无截图交接），仅通过单层胶囊裁切（`LiveBackdrop` 就地更新圆角几何零重建 + `Root.Clip` 同步）从 `58px` 平滑伸展，同时搜索框 `expanded` 以 `Opacity` 平滑淡入淡出，收起完成后恢复 `58×58`，并保持胶囊外透明区 `WM_NCHITTEST` 返回 `HTTRANSPARENT`。 | `M01` / `M09` / `SURFACE` (`BallSurface`, `SurfaceWindow.LiveBall`, `LiveBackdrop`) | 用户实机验收通过 |
| **#25** | [x] 已完成（0.9.54；0.9.55–0.9.56 图标与滚轮精修） | **新增“待操作列常驻 + 鼠标穿透”合一功能**：<br>1. **仅内置“待操作”列**的独立窗口标题栏右侧新增 **26×26 原创矢量图钉正圆按钮**（`ActionPinIcon`，0.9.56 去掉顶帽左右凸起改为平滑圆润端头并放大图钉占画布约 85%），开启后置顶常驻桌面 + 应用独立透明度 + 系统级 `WS_EX_TRANSPARENT` 鼠标穿透。<br>2. **透明度可调**：`Preferences.PinnedActionTransparency` 默认 **70%**，设置“窗口”页提供独立滑块并联动 `#19`（0% 纯色不透明跳过实时模糊）。<br>3. **局部可点 + 悬停滚轮手感统一（0.9.55–0.9.56）**：在图钉按钮正上方同步维护轻量置顶解除浮窗（`WS_EX_TOOLWINDOW`、无 `WS_EX_TRANSPARENT`），整列其余区域 100% 鼠标穿透点击；低级鼠标钩子滚轮接管在溢出常驻待操作列内滚动滑轮，0.9.56 消除滚动期间多余浮窗/Z-order 同步、对齐修饰键过滤与 `DispatcherPriority.Normal`，实现与正常滑动完全一致的 `QuickScroll` 90ms 缓动与连滚累加体验。<br>4. **快捷键与状态记忆**：外部点击/切离外部应用收起其他列表时保持待操作列常驻；按全局快捷键隐藏/最小化时一并隐藏，**再次按快捷键唤起时严格保持常驻 + 半透明 + 鼠标穿透 + 仅解除按钮可点状态**。 | `M01` / `M02` / `M11` / `SURFACE` (`SurfaceWindow`, `ActionPinIcon`, `BoardController.PinnedAction`, `ColumnSurface`, `SettingsSurface`) | 已实施并精修 |
| **#28** | [x] 已完成（0.9.54） | **优化上下拖动排序小圆按钮图标质感与悬停发光**：<br>1. 合并轮廓图标线宽调细为 `1.5` 并做像素中心对齐与柔和着色，与相邻“删除 `×`”协调，0.9.56 同步接入实时主题画刷。<br>2. 鼠标悬停保持默认箭头 `Cursors.Arrow` 不改变指针样式。<br>3. 补齐 `Thumb` 控件 `IsMouseOver` 触发样式，悬停时外圈边框亮起主题强调色高亮（`AccentBrush`）。 | `M04` / `M05` (`StageReorderGesture`, `Theme`) | 已微调完成 |
| **按钮形状、比例与高对比控件统一优化** | [x] 已完成（0.9.55–0.9.56） | **去掉纯椭圆按钮 + 比例协调 + 全控件高对比度补齐**：<br>1. 全局常规文字按钮与关闭按钮采用圆角矩形（`CornerRadius="9"`），固定正方小图标按钮保持正圆，虚线新建入口圆角 `12DIP` 且保留非零 Alpha 全区命中，常规输入框/下拉框 `10DIP`（阶段名胶囊 `14DIP`）。<br>2. 0.9.56 精修悬浮球展开横框（搜索框 `36` 高、`×` `28×28` 正圆、`设置` `44×36` 平齐）以及提醒日历/时间/今天/完成、关闭提醒、解除绑定等文字按钮宽高比；新增 `CheckSurfaceBrush`/`CheckBorderBrush`/`CheckCheckedFillBrush`/`CheckMarkBrush`/`InputBorderBrush`/`SliderTrackBrush`/`ComboArrowBrush` 全面提亮勾选框、单选框、滑轨、输入框与下拉箭头。 | `SURFACE` / `Rendering` (`Theme.xaml`, `Ui`, `ThemePalette`, `RoundedInput`, `EditorSurface`, `BallSurface`, `ColumnSurface`, `SettingsSurface`, `StageReorderGesture`, `ActionPinIcon`) | 已实施并精修 |
| **官方猫咪摸鱼圆标换装** | [x] 已完成（0.9.56） | 嵌入并启用官方猫咪圆标资源（`Assets/orb.png` 与 `Assets/orb.ico`）覆盖悬浮球、托盘与可执行程序图标，保留无资源矢量兜底，保证待操作计数红圈与边缘吸附红点严格位于图标上方清晰可读。 | `M01` / `Application` (`Brand`, `BallSurface`, `Taskboard.Desktop.csproj`) | 已实施 |
| **全新“外观”分页与全局实时深浅色/多配色主题引擎** | [x] 已完成（0.9.56） | 设置导航在“常规”后新增独立“外观”分页：支持**深夜模式（默认，青+深色）**、**白天模式**与**跟随系统**（监听 Windows 系统深浅色切换实时联动）三选一；内置 5 套深色预设（深夜青蓝·默认、墨夜琥珀、深林松翠、星夜紫罗、石墨珊瑚）与 4 套浅色预设（晴昼青雾·默认白天、暖纸杏棕、云杉薄荷、雾岚紫霞），全控件 `DynamicResource`、实时毛玻璃/`LiveBackdrop` RGB、DWM 深浅属性、`ActionPinIcon` 与 `OrbMenuRenderer` 均无缝实时联动并自动持久化/备份恢复。 | `M11` / `SURFACE` / `Rendering` / `Core` (`Models`, `Validation`, `ConfigurationBackup`, `ThemePalette`, `Theme.xaml`, `Ui`, `SurfaceWindow`, `LiveBackdrop`, `BoardController`, `SettingsSurface`, `SettingsLayout`, `OrbMenuRenderer`) | 已实施 |
| **#03-撤回补丁** | [x] 已撤回（0.9.54） | **已撤回 `AiEvents.cs` 中 `Notification`“唯一一条等待才猜着消”及无 `PreToolUse` 匿名猜测结清补丁**，恢复严谨事件配对语义（`#03` 整体是否改为纯被动读 Claude 桌面本地日志仍待用户进一步确认）。 | `C03` (`AiEvents`) | 已撤回 |

---

## 第一批：编辑与输入体验修复（共 9 项，需求已全部明确，可直接开发）

| 编号 | 状态 | 需求/问题描述（已对齐细节） | 涉及模块 | 处理方式 |
| --- | --- | --- | --- | --- |
| **#01** | [x] 已完成（0.9.53） | **新建阶段默认状态三选一 + 光标自动聚焦**：<br>1. 点击“新建阶段”时，输入光标自动移至新阶段的“阶段详情”输入框以便直接编辑。<br>2. 在设置中提供三个可选项（默认第 1 项）：<br>  - 选项 A（默认）：灵感库/完善中任务编辑时、以及所有状态的新建任务时，新建阶段默认“未运行”；其他情况（如进行中任务编辑时）默认“运行中”。<br>  - 选项 B：全部初始默认“运行中”。<br>  - 选项 C：全部初始默认“未运行”。 | `M03` / `M04` / `M11` (`BoardEngine`, `EditorSurface`, `SettingsSurface`) | 直接实施 |
| **#07** | [x] 已完成（0.9.53） | **清理“阶段详情”等挡视野的无意义气泡提示（Tooltip）**：<br>移除鼠标悬停在阶段详情处弹出的“阶段详情”文字气泡，并全面排查清理全程序其他挡视野的无意义 Tooltip。 | `H01` / `M04` / `M05` | 全面排查并清理 |
| **#08** | [x] 已完成（0.9.53） | **全程序所有输入框：首尾行方向键跳至最开头/最末尾**：<br>在**所有输入框**（阶段详情、任务标题、阶段名称、灵感录入框等）中：在最后一行（非最末尾）按 `⬇️` 键光标跳到最末尾；在第一行（非最开头）按 `⬆️` 键光标跳到最开头。 | `M04` / `M06` (`StageDetailsEditor`, `EditorSurface` 等) | 直接实施 |
| **#10** | [x] 已完成（0.9.53） | **修复编辑任务窗口“运行中”阶段自动收起 & 补齐所有收起态省略号**：<br>1. 阶段只有“运行中”和“未运行”两种状态；在“编辑任务”窗口中，**所有“运行中”的阶段默认应保持展开（不自动收起）**，仅“未运行”的阶段可收起。<br>2. 修复收起态缺少省略号的问题，并全面检查全程序其他收起/截断但未显示省略号的位置一并补齐。 | `M04` / `M05` (`EditorSurface`, `StageDetailsEditor`) | 直接修复 |
| **#11** | [x] 已完成（0.9.53） | **修复点击阶段详情展开编辑时误选中多行文本**：<br>点击阶段详情展开进入编辑时，因鼠标左键尚未松开叠加高度变化导致误选中多行文本。需在展开瞬间抑制初始拖拽选区，仅准确落定光标。 | `M04` (`StageDetailsEditor`) | 直接修复 |
| **#18** | [x] 已完成（0.9.53） | **修复选中文本后右键自动收起 + 重制暗色圆角右键菜单**：<br>1. 修复编辑阶段详情选中文本后点右键导致输入框自动收起的 Bug。<br>2. 替换系统默认白底灰字右键菜单，重制为符合当前风格的圆角矩形暗色菜单（评估毛玻璃透明开销：性能开销小则带透明毛玻璃，否则使用纯暗色圆角）。 | `M04` / `SURFACE` (`StageDetailsEditor`, `SurfaceWindow`) | 评估透明开销后直接实施 |
| **#21** | [x] 已完成（本轮彻底重构） | **重构阶段名称编辑区域为完整单一胶囊输入框**：<br>阶段名称圆角胶囊整体即为单一拉满（`Stretch`，最小 64 / 最大 155 DIP）的原生 `TextBox`，胶囊内任意位置原生可点击/可拖选/自动落定光标，不再使用窄左段+右段补丁。 | `M04` / `M05` (`EditorSurface`) | 已重构完成 |
| **#27** | [x] 已完成（本轮焦点恢复） | **修复编辑阶段详情底部虚假留空及焦点行为**：<br>保留消除阶段详情底部虚假留空及点击详情底部不误收起的修复；撤回外层 `ScrollViewer` `Focusable=false`，点击输入框外空白可正常离开焦点收起光标。 | `M04` (`StageDetailsEditor`, `EditorSurface`) | 已修复完成 |
| **#28** | [x] 已完成（本轮微调完成） | **优化上下拖动排序小圆按钮图标质感与悬停发光**：<br>1.5 线宽像素对齐合并轮廓 + 柔和着色、悬停保持默认箭头指针、补齐 `Thumb` `IsMouseOver` 青绿色边框高亮。 | `M04` / `M05` (`StageReorderGesture`, `EditorSurface`) | 已修复完成 |

---

## 第二批：窗口焦点、输入法、全屏游戏与性能修复（共 7 项，涉及共享层）

> **注意**：本批涉及 `SURFACE` 共享层，开发前必读 [SHARED-CODE.md](architecture/SHARED-CODE.md) 与 [VERIFICATION.md](architecture/VERIFICATION.md)。

| 编号 | 状态 | 需求/问题描述（已对齐细节） | 涉及模块 | 处理方式 |
| --- | --- | --- | --- | --- |
| **#13** | [x] 已完成（0.9.54 方案 A；0.9.55 TSF 根治） | **修复玻璃子窗点击/聚焦导致输入法 `Ctrl` 切换中英文失效**：<br>1. 0.9.54 按方案 A 在 `SurfaceWindow` 与 `GlassContentHost` 补齐文本框聚焦/点击时的顶层母窗激活与 `WM_SETFOCUS` → `SetFocus(ContentHandle)` 同步。<br>2. **0.9.55 根治纯 TSF 输入法（微信 WeType / 微软拼音）单按 `Ctrl` 失效**：外层 `SurfaceWindow` 收到 `WM_SETFOCUS` 转发焦点后显式设 `handled = true`，防止外层空壳把 TSF 焦点退回 `EmptyDim`；并新增 `ComponentDispatcher.ThreadFilterMessage` 的 `VK_CONTROL` TSF 桥接，将原始 `VK_CONTROL (0x11)` 与真实扫描码直达 `ITfKeystrokeMgr`，避开 WPF 将其改写为 `VK_LCONTROL/VK_RCONTROL` 导致的漏识别，同时保留 `Ctrl+A/C/V/Z` 组合键、`WM_CHAR` 中文上屏、`SettingsHotkey` 录制框与 `#29` IME 免疫。 | `M01` / `SURFACE` (`SurfaceWindow`, `LiveBackdrop`) | 已根治完成 |
| **#14** | [x] 已验收通过（0.9.54） | **彻底修复点击悬浮球展开时搜索框闪烁**：<br>见上方本轮已定稿清单（窗口一步到位定宽 + 单层胶囊就地伸展 + 搜索框 Opacity 淡入淡出，彻底抛弃截图交接与逐帧缩窗），用户实机已验收通过。 | `M01` / `M09` / `SURFACE` (`BallSurface`, `SurfaceWindow.LiveBall`, `LiveBackdrop`) | 用户实机验收通过 |
| **#15** | [x] 已按用户拍板收口（0.9.56 放弃 `Alt+Tab` 代理，保证任务栏绝对零窗口） | **确保 Windows 底部任务栏绝对无窗口（按用户拍板暂放弃 `Alt+Tab` 切回）**：<br>1. Windows 11 Build 26200 下 `Alt+Tab` 与任务栏图标存在系统级耦合，按用户最终决策放弃 `Alt+Tab` 代理窗，彻底删除 `AppSwitcherWindow` 及 `ShowInTaskSwitcher`/`RaiseTaskSwitcherToTop`/`HideTaskbarTab`。<br>2. 全程序所有窗口统一保持 `ShowInTaskbar = false` + `WS_EX_TOOLWINDOW`，仅留系统托盘 + 悬浮球 + 全局快捷键入口；完整保留前景切离自动收起、IME 与原生对话框免疫及内部关窗保护。 | `M01` / `M02` / `SURFACE` (`SurfaceWindow`, `BoardController.TaskSwitcher`) | 已按决策收口 |
| **#19** | [x] 已完成（方案 A，0.9.54） | **100% 不透明时跳过桌面实时模糊采样**：<br>在 `LiveBackdrop` / `SurfaceWindow` 同架构内，透明度为 `0%`（100% 不透明）时跳过挂接 `HostBackdropBrush`，改用实心暗色 `CompositionColorBrush` 绘制；调回 `>0%` 平滑恢复模糊，不销毁重建窗口；并在设置页注明省性能说明。 | `SURFACE` / `M11` (`SurfaceWindow`, `LiveBackdrop`, `SettingsSurface`) | 已按方案 A 实施 |
| **#20** | [x] 已完成（本轮真实 UI 端到端彻底修复） | **长文本任务卡片性能优化（打开、保存、拖拽卡顿）**：<br>在 0.9.53 数据/渲染优化基础上，进一步实测剖析定位真实 `TaskEditorWindow` 打开卡顿根因（WPF 无限高 `TextBox` 同步 TSF/DirectWrite 排版 + 重复 `Measure` + `ScrollViewer.Auto` 双向重排 + `RevealLiveScroll` 预显示空排版与 2 帧/80ms 死等），改用未聚焦长阶段轻量 `TextBlock` 视口首帧展示 + 空闲/滚动展开 + 点击/聚焦无缝水合（清撤销栈、100% 不丢字），端到端打开耗时从 1731–3897ms 降至 202–360ms（降幅 8.5×–14×）。 | `M04` / `M05` / `M07` / `Rendering` (`StageDetailsEditor`, `EditorSurface`, `SurfaceWindow.LiveScroll`) | 已彻底修复 |
| **#29** | [x] 已完成（本轮补全国内外主流 IME） | **输入法（IME）选词框点击免疫**：<br>已全面扩展国内外最新主流输入法（豆包输入法含主进程悬浮条特征放行与聊天主窗排除、微信 WeType、智谱 AI、讯飞、搜狗、百度、QQ、手心、小狼毫/Rime、PIME、微软中日韩宿主、Google Japanese Input、Mozc、ATOK）并保留严格防误杀验证。 | `M04` / `SURFACE` (`ImeWindowFilter`, `BoardController`, `ColumnSurface.Inline`) | 已扩展补全 |
| **#30** | [x] 已完成（0.9.53；真机全屏游戏待实测） | **修复全屏游戏中打开面板后点击窗口外无法关闭**：<br>排查并修复在全屏游戏中呼出面板后，点击游戏画面（窗口外）不能触发面板正常关闭的问题。 | `M01` / `M02` / `SURFACE` (`BoardController`, `SurfaceWindow`) | 排查原因并修复 |

---

## 第三批：AI 会话监听、绑定与橙红提醒联动（共 5 项）

| 编号 | 状态 | 需求/问题描述（已对齐细节） | 涉及模块 | 处理方式 |
| --- | --- | --- | --- | --- |
| **#03** | [~] 待确认纯被动路线（猜测补丁已撤回） | **确保新版本完整包含并完善 Claude 监听模块**：<br>主线 `0.9.52` 已合入 `codex/claude-desktop-listening` 的桌面目录 + 本地 Hook 架构；本轮已按反馈撤回 `AiEvents.cs` 中 `Notification` 与匿名权限请求的猜测结清补丁；是否整体改为纯被动读 Claude 桌面本地日志仍待用户确认。 | `C03` (`AiPlatforms`) | 妥协补丁已撤回，纯被动路线待确认 |
| **#09** | [x] 已完成（0.9.53） | **Codex 拖动绑定失败排查 + 客服化提示 + 绑定会话专属窗口拖放解绑**：<br>1. 排查当前拖到 Codex 提示“未绑定：读取会话未完成或目标已变化，原绑定保留”的原因。<br>2. 将该报错文案改得直观友好，像客服一样用非程序员大白话给出专业解释与建议（如提示可能是会话加载中或 Codex 更新所致，建议稍后重试或等待更新修复）。<br>3. 支持在拖动绑定时，拖到**绑定会话专属窗口**上原有的**“解除绑定”按钮区域**并松手即可解除绑定（交互逻辑与拖到上方会话条目松手绑定保持一致）。 | `X01` / `M09` / `C01` (`StageConversationPicker`, `CodexObserver`, `BoardController`) | 排查原因 + 直接实施文案与解绑 |
| **#12** | [x] 已完成（0.9.53） | **多阶段重复绑定同一会话时，橙红色提醒联动消失**：<br>多个阶段重复绑定同一个会话时，橙红色待处理提醒视为同一个待处理项；用户处理过后，绑定该会话的其他阶段上的橙红色提醒同步消失。 | `C01` / `C02` / `C03` (`Core/AiState`, `BoardEngine`) | 直接实施 |
| **#23** | [x] 已完成（0.9.53） | **未绑定会话的任务卡片隐藏“跳转到会话”按钮**：<br>未绑定会话的任务卡片直接不显示“跳转到会话”入口。 | `M05` / `M09` (`ConversationJumpButton`, `ColumnSurface`) | 直接修改 |
| **#26** | [x] 已完成（方案 A，0.9.54） | **左键点击“跳转到会话”且跳转成功时自动消除橙红提醒**：<br>左键点击跳转并成功后调用 `AcknowledgeStageAttention` 自动消除本阶段及同会话绑定阶段（联动 `#12`）的橙红提醒，卡片继续留在“待操作”列不变；跳转失败时不误消。 | `C01` / `C02` / `C03` (`ConversationJumpButton`, `BoardEngine`) | 已按方案 A 实施 |

---

## 第四批：面板交互增强与先给方案事项（共 9 项）

| 编号 | 状态 | 需求/问题描述（已对齐细节） | 涉及模块 | 处理方式 |
| --- | --- | --- | --- | --- |
| **#02** | [x] 已完成（0.9.54） | **开机自启修复 + 仿 Agent Monitor HUD 固定目录更新回滚脚本 + 开源免服务器 GitHub Releases 检查更新**：<br>1. 开机自启诊断增强（设置页直接高亮提示 Windows `StartupApproved\Run` 禁用状态并引导打开系统启动应用设置）+ `Program.cs` `--startup` 早期瞬时挂载/IO 异常重试与静默防死弹窗。<br>2. 新增 `tools/scripts/install.ps1`、`rollback.ps1`、`uninstall.ps1`、`安装或升级摸鱼台.cmd` 并由 `Build-Portable.ps1` 随便携包打包，固定安装至 `%LOCALAPPDATA%\MoyuDesk\app`（正式数据 `%LOCALAPPDATA%\InstantTaskboard` 永不修改），自动备份旧版 `.rollback-<ver>` 支持一键回滚、固定快捷方式与自启路径不断链。<br>3. 新增 `AppUpdater.cs` 直连 GitHub Releases API 检查新版、SHA256 校验下载并一键拉起安装升级，并在常规设置页新增“版本与更新”卡片。 | `M12` (`StartupLinks`, `Program`, `AppUpdater`, `SettingsSurface`, `Build-Portable.ps1`, `tools/scripts/`) | 已实施 |
| **#04** | [x] 已完成（0.9.54；0.9.55 默认对齐增强，联动 #22） | **时间提醒方案重设计**：<br>一次性临时定个点为主（整行可切开关；0.9.55 新建及从未勾选切勾选开启时自动对齐当前日期与下一个小时整点，已启用提醒打开编辑保留原值）+ 小时（00–23）/分钟（00–59）独立滚动选择列表即时更新 + 到期卡片留在原位不移列仅亮红点与托盘提醒。 | `M10` (`Models`, `ReminderEditor`, `ReminderClock`, `BoardController`) | 已实施并增强 |
| **#22** | [x] 已完成（方案 A 精修，0.9.54） | **定时提醒关闭气泡精修 + 3 Bug 根除（联动 #04）**：<br>1. 悬停预览窗紧凑化，仅显示提醒时间与“关闭提醒”按钮。<br>2. 新增 80ms 屏幕指针轮询（覆盖红点、气泡及通道，移出 120ms 关闭）与窗体隐藏/卸载清理，彻底根除悬停窗常驻关不了 Bug。<br>3. 移除点击卡片/定位/打开编辑/保存未改提醒时误清 `TimerUnread` 的逻辑，仅显式关闭/已查看/重设提醒时消红点。 | `M10` (`ColumnSurface`, `BoardController`, `BoardEngine`) | 已按方案 A 精修完成 |
| **#05** | [ ] 长线规划（暂不列入本轮） | **探索多端共用方案（一台固定电脑 + 一部手机）**：<br>明确边界为单台电脑绑定路径与本机 AI 会话 + 手机端共用通用任务；目前仅处于思路讨论阶段，暂不开发，留待后续启动手机端项目时再定具体同步方案。 | `M12` (`Persistence`, `DataPaths`) | 长线规划，暂不开发 |
| **#06** | [x] 已完成（已改为“重新体验新手指引”，0.9.54） | **重新体验新手指引入口**：<br>已按反馈撤回“重置新手指引”入口、`DeviceOnboarding.Reset()` 与 `--reset-onboarding` 参数，保留原本“首次使用自动弹出新手指引”机制；常规设置页提供“重新体验新手指引”按钮，随时重看完整指引且不擦除首用标记、不改任务与设置。 | `M11` (`SettingsSurface`, `DeviceOnboarding`, `BoardController.Onboarding`) | 已完成 |
| **#16** | [x] 已完成（方案 A，0.9.54） | **修复鼠标滚轮滚动幅度过大（滚轮步距统一）**：<br>废弃按“平均卡片高度 × WheelScrollLines”计算导致一格跳整页的旧逻辑，普通多列表与专注模式统一改为固定舒适步距（`80 DIP/格`，且不超过 `ViewportHeight * 0.25`），保留 90ms 缓动动画。 | `M02` / `M05` (`QuickScroll`) | 已按方案 A 实施 |
| **#17** | [x] 已完成（0.9.54） | **重构“省略多列表阶段详情的超出文本”**：<br>1. 默认开启（`EllipsizeStageDetails = true`）。<br>2. 按可用宽度下总行数等比例自适应：`<= 4` 行不省略，`> 4` 行按 `Clamp(Round(totalLines * 0.4), 3, 10)` 显示且严格小于总行数。<br>3. 末行末尾（避开跳转图标）提供样式 2-A 紧凑圆角气泡 `[…]` 点击展开并持久化记住展开状态（`Preferences.ExpandedStageDetails`，任务/阶段删除自动防膨胀），展开态末尾提供 `[▴]` 收起气泡恢复省略，点击气泡不触发卡片编辑或拖动。 | `M05` / `M11` (`Models`, `Validation`, `BoardEngine`, `StageDetailsText`, `ConversationJumpButton`, `ColumnSurface`, `SettingsSurface`) | 已实施 |
| **#24** | [x] 已完成（0.9.53） | **左下角调节最大范围按钮核对 + 按住 Shift 移动最大范围**：<br>核对 `0.9.52` 左下角按钮已正常显示，并实现**按住 `Shift` 键拖拽即可整体平移六列最大范围框位置**的功能。 | `M02` (`FocusLayout`, `BoardController`) | 直接实施 |
| **#25** | [x] 已完成（0.9.54；0.9.55–0.9.56 图标与滚轮精修） | **新增“待操作列常驻 + 鼠标穿透”合一功能（规则已定稿）**：<br>见上方本轮已定稿清单（仅内置待操作列标题栏 26×26 去凸起原创矢量图钉正圆按钮 + 独立 70% 默认透明度滑块联动 0% 纯色跳过模糊 + 系统级 `WS_EX_TRANSPARENT` 整列穿透 + 同步迷你可点解除浮窗 + 与正常态手感完全一致的悬停滚轮上下滚动 + 外点保持常驻 + 全局快捷键隐藏再唤起严格保持常驻穿透态）。 | `M01` / `M02` / `M11` / `SURFACE` (`SurfaceWindow`, `ActionPinIcon`, `BoardController.PinnedAction`, `ColumnSurface`, `SettingsSurface`) | 已实施并精修 |
