# 定时提醒日期与时间选择：一手资料调研

查阅日期：2026-11-13。范围：Microsoft Windows 控件指南、WPF 官方文档和源码、Apple HIG、Material 官方 Android 文档与源码、Microsoft To Do 官方使用说明。本文是本轮重构的研究材料，推荐不直接成为现行产品规则。只读项目文件及公开资料，未操作桌面、正式数据或用户进程，未引入框架、修改生产代码或发布版本。

## 结论

推荐“可输入日期 + 弹出日历；24 小时小时/分钟可编辑选择 + 少量快捷项”的组合。鼠标用户能直接选，知道具体时间的用户能直接输入；日期提供星期和月份上下文。一次、每天、每周继续使用现有提醒模型，只有一次提醒显示日期，只有每周显示星期。

适合当前 WPF 的优先落地方式是：保留系统 DatePicker 的日历和键盘能力，单独调整提醒区域样式；把时间做成视觉连续的 `小时 : 分钟` 两个可编辑选择框，提供 00—23、00—59 的合法候选，仍允许精确到任意一分钟。不要将整个一天变成 1440 项长列表。常用分钟可以额外放 `00 / 15 / 30 / 45` 快捷项，但不能把其他分钟裁掉。这是结合以下资料与本项目约束作出的方案推导，不是“所有用户已经验证偏好如此”的调查结论。

## 官方依据及可借鉴机制

| 一手资料 | 实际核对内容 | 本项目适用差异 |
| --- | --- | --- |
| [Microsoft：Time picker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/time-picker) | 同一控件支持鼠标、键盘和触摸；可指定 24HourClock；MinuteIncrement 可减少可视分钟候选；未设置与 00:00 是不同状态。展开层覆盖其他 UI。 | 借鉴 24 小时、小时/分钟分项和“未输入不等于零点”。WinUI 控件不能直接当 WPF 控件引入，不迁移框架。 |
| [Microsoft：Calendar date picker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/calendar-date-picker) | 适合有星期、月历上下文的预约类日期；已知日期且不需要上下文时可用日期分项选择；日历在入口附近展开。 | 提醒通常在近期日期中选择，月历比年/月/日三个长列表更合适。 |
| [Apple HIG：Pickers](https://developer.apple.com/design/human-interface-guidelines/pickers.md) | 候选按可预期顺序排列；选择器靠近正在编辑的字段；分钟可按整除 60 的间隔展示。macOS 明确区分节省空间、适合精确输入的文本式和适合浏览日期的图形式。 | 借鉴近场展开、文本/图形互补。无需照搬手机滚轮的外观和触摸交互。 |
| [Material：TimePicker 官方文档](https://github.com/material-components/material-components-android/blob/master/docs/components/TimePicker.md) | 有表盘和键盘输入两种方式；24 小时模式的当前文档默认使用键盘输入；模型小时为 0—23、分钟为 0—59。 | 保留直接输入入口；桌面不必以模拟钟面作为唯一入口。 |
| [Material：DatePicker 官方文档](https://github.com/material-components/material-components-android/blob/master/docs/components/DatePicker.md) | 区分停靠日历、模态日历、文字输入；支持日期约束、默认展示月份和有意义的控件标题。 | 近期单日期用现有下拉日历，不增加整屏选择流程。 |
| [Microsoft：WPF DatePicker](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/datepicker) | 同时支持文本输入和 Calendar；无效转换有 DateValidationError；模板有 PART_TextBox、PART_Button、PART_Popup 等约定部件。 | 保留原生行为，通过局部样式改善可读性。日期错误应进入本编辑器统一验证，不能只依赖 SelectedDate。 |
| [Microsoft To Do：Add due dates and reminders](https://support.microsoft.com/en-gb/todo/add-due-dates-and-reminders-in-microsoft-to-do?nochrome=true) | 提醒入口并列提供“今天稍后、明天、下周、自选日期时间”。 | 说明“少量相对时间快捷项 + 完整自选”是已存在的任务产品模式；不能据此推断本用户需要相同的默认时刻。 |

### 一手源码核对

- [Material TimeModel.java，master 查阅快照](https://raw.githubusercontent.com/material-components/material-components-android/master/lib/java/com/google/android/material/timepicker/TimeModel.java)：小时、分钟及当前编辑分项由模型统一维护；`getHourForDisplay` 负责 12/24 小时显示差异，`formatText` 负责两位补零。借鉴“结构化值”和“输入显示”分离，不把显示文字作为唯一状态；有效范围仍需输入层检查。
- [Material TimePickerTextInputPresenter.java，master 查阅快照](https://raw.githubusercontent.com/material-components/material-components-android/master/lib/java/com/google/android/material/timepicker/TimePickerTextInputPresenter.java)：分别监听小时/分钟文本，检查两位长度及 23/59 上界，有字段错误状态、可访问错误播报和 `hasError`；程序回填时临时移除监听，避免反馈循环。该实现有 Android 特有的震动/声音，以及内部分钟取模行为；本项目只借鉴双字段验证和回填隔离，不照抄这些行为。
- [Material TimePickerTextInputKeyController.java，master 查阅快照](https://raw.githubusercontent.com/material-components/material-components-android/master/lib/java/com/google/android/material/timepicker/TimePickerTextInputKeyController.java)：处理小时至分钟的 Next 动作；有效输入两位小时可切分钟；分钟空白时退格可回小时；有重入保护。桌面优先使用 Tab/Shift+Tab，不必照搬手机自动跳焦，尤其不能打断选中替换、粘贴和修正。
- [dotnet/wpf DatePicker.cs，main 查阅快照](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/DatePicker.cs)：`OnApplyTemplate` 接好内置 Calendar 和模板部件；`ProcessDatePickerKey` 支持 Alt+Down 展开和 Enter 解析；失焦会解析文本；`ParseText` 报验证错误，`SetTextBoxValue` 在无效输入后可能恢复上次有效日期。因此“SelectedDate 非空”不足以证明用户刚输入的日期有效。新验证应记录当前输入及错误，并在保存前明确检查。

上述分支链接记录的是查阅时内容，不是固定提交；实施时借鉴机制并自主编写 WPF 代码，不复制整套 Android 或 WinUI 控件。

## 路线比较

| 路线 | 优点 | 具体局限 | 判断 |
| --- | --- | --- | --- |
| 单个 HH:mm 文本框 + 日期框 | 熟练用户输入快、占地小 | 鼠标用户必须手打时间；格式和有效范围不可见；当前每次击键都会报错 | 可作为输入能力保留，单独使用不足以解决易操作要求 |
| 小时/分钟可编辑选择 + 日期日历 | 支持手输和鼠标选择，合法候选清楚；可用现有 WPF 控件自主实现 | 60 项分钟列表需要可视高度和滚动限制；不能让弹层被编辑区裁切 | 推荐 |
| 只显示每 5/15 分钟的时间选项 | 常见整点/刻钟选择快 | 无法设置 09:07 等精确时间，改变现有能力 | 不采用为唯一入口；可作快捷项 |
| 模拟钟面或手机滚轮 | 图形感强，适合触摸 | 鼠标要切小时/分钟；24 小时及精确分钟更复杂；增加面积和交互状态 | 本轮不优先 |
| 今天/明天/稍后等快捷按钮 | 常见近期开提醒可少操作 | 默认具体几点、跨零点、重复提醒语义必须清楚；不能替代任意日期时间 | 作为补充，显示将得到的具体时刻 |
| 直接引入第三方日期时间框架 | 成品控件多 | 引入主题、弹层、键盘和窗口归属差异；本任务不需要替换 UI 框架 | 不采用，借鉴机制自主实现 |

## 与当前项目的适配

已按 Codebase Memory 核对：项目 `Instant-taskboard`，根 `D:/Derp/Documents/0renwumianban`，ready，索引时间 2026-11-12T18:34:07Z。目标 EditorSurface 路径存在且实际源码与图中片段相符；部分解析仅出现在其他测试工具。图中没有 ReminderEditor 符号，实际提醒 UI 内联于 [EditorSurface.cs](../../src/Taskboard.Desktop/EditorSurface.cs) 92—128 行，已用 rg 和直接源码补证。图调用关系还包含部分不准确的同名解析，本文不以其推断行为，行为结论均来自目标源码。

当前行为：时间仅接受 `h:mm` / `hh:mm`，每次 TextChanged 调用 Schedule；无效文字时保留上次有效提醒值，保存靠 error 文本阻止；日期未选时以今天兜底。当前日期框有全局模板 [Theme.xaml](../../src/Taskboard.Desktop/Theme.xaml) 117—118 行。重构应把提醒控件和解析留在编辑模块，尽量用局部样式，避免为了改提醒而修改全部输入框、下拉框或日历消费者。

现行约束来自 [M10](../current/M10-提醒与预览.md) 和 [M04](../current/M04-新建编辑与草稿.md)：22DIP 勾选圆仍是唯一鼠标启用入口，旁边文字和整行空白不触发；只修改编辑副本，草稿不启动计时；正式保存后生效；保存失败保留输入；日历及菜单视为编辑器内部；保存验证与实际落盘仍走原链。UI 优化不能改动这些语义。

### 推荐的控件和输入约定

1. 一次提醒显示“日期”和“时间”；每天只显示时间；每周显示星期和时间。以一句可读预览确认结果，例如“2026年11月14日 星期六 09:30”或“每周一 09:30”，不用用户自行推算两个字段的组合。
2. 日期保留手输与日历；给出明确格式提示及星期。可加“今天、明天”日期快捷项，它们只改日期，保留已输入时间。若加入“10分钟后、30分钟后、1小时后”，只放一次提醒模式，并显示实际目标日期/时间；在用户点击时计算，不反复随时钟变化。这些具体按钮是候选设计，不是文档确认过的固定名单。
3. 时间的小时/分钟均可直接输入或选择；回填时按两位显示。保留原 `9:05` / `09:05` 的输入能力，可对粘贴的全角数字、全角冒号做明确归一化；不做含糊的自然语言时间推断。分钟快捷项只改分钟，不能静默四舍五入已有 09:07。
4. 编辑中的原始文本、最后一次合法值、错误状态分开。用户暂时删空一个分项时允许继续输入；完成该字段或保存时显示具体错误，并聚焦错误字段。验证不应把空字段当 00、空日期当今天，也不能以旧合法日期冒充本次无效文本。合法更改才更新工作副本，但只要当前文本无效就必须阻止正式保存。
5. Tab / Shift+Tab 按字段顺序移动；数字、选择替换和粘贴可用；上下键在选择器中调整候选；Alt+Down 展开；Enter 只确认当前选择/字段，Esc 关闭弹层后焦点回入口。键盘操作不能意外保存整个任务、改变启用状态或触发外部点击收起。优先沿用原生 WPF 行为并核对实际控件。
6. 小时、分钟、日期、星期都要有明确可访问名称及可见焦点。选中、今天、错误不能只靠颜色区分。弹层在字段附近，内部滚动可用，靠近编辑器底部或屏幕边缘时不能裁切，也不能变成编辑器外部点击。

一次提醒选到过去时间后究竟“立即按已到期处理”还是“拒绝保存”，属于调度规则，不由控件指南决定。现行 M10 有错过提醒补一次规则，不能为了 UI 验证擅自取消或改变；本轮先保留现有调度语义，并用明确预览提示时间结果。每天/每周仍由 ReminderClock 计算下一次，不把 UI 快捷项另做一套调度器。

## 实施补充：星期标题

2026-11-13复核日历截图发现，旧CalendarItem模板未提供星期模板，月历首行没有星期名。[WPF官方CalendarItem源码](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/CalendarItem.cs)的OnApplyTemplate只在ControlTemplate.Resources读取DayTitleTemplateResourceKey；PopulateGrids在缺模板时生成空ContentControl，日期更新仅给其DataContext赋星期名。提醒组件因此局部复制既有模板并补星期资源及绑定，不改其他消费者的全局模板。新增实际生成七个可见星期标题的检查。

## 实施验证建议与研究回执

研究核对完成：官方控件能力、两套一手源码的输入/验证机制、当前 EditorSurface 适配点、可行路线比较。Material M3 网页只返回 JavaScript 提示，已改读同一官方仓库的文档和源码；Apple 网页提供官方 Markdown 入口，已实际读取。未以不可读取的网页内容作依据。

本研究没有运行功能实验，也不声明产品重构已通过。所属实现任务应至少覆盖：00:00/23:59、9:05/任意分钟、小时24/分钟60/空白/非法粘贴、非法日期及闰日、键盘全流程、一次/每天/每周切换、快捷项跨日、草稿切换与重启恢复、保存失败和隔离磁盘回读。桌面实测继续依根 AGENTS 先取得本次方便确认；纯解析及离屏布局检查可先执行。
