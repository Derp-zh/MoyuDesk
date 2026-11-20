# WorkBuddy与豆包Windows监听可行性调研

查阅日期：2026-11-17。用户目标是在即时任务看板中监听现有AI客户端的工作状态；豆包优先Windows电脑版。本文交付公开资料和源码研究，不是产品接入验收。未安装目标应用、改变正式配置、启动/重启用户客户端、读取用户聊天或操作桌面。来源快照、版本与离屏核验见[E03进度](../tasks/E03-workbuddy-doubao/PROGRESS.md)。

## 结论

| 对象 | 判断 | 对用户的实际意义 |
| --- | --- | --- |
| WorkBuddy本地桌面任务 | 可行性高，建议优先接入。自身官方文档支持插件Hooks，且已有公开桌面状态监听实现。 | 可以走客户端主动报告事件的路线；闭源不是阻止监听的条件。等待回答、等待批准的逐条结清仍须按真实桌面事件适配。 |
| CodeBuddy Code | 有完整的官方Hooks、SDK及本地HTTP服务文档。 | 可借鉴协议和状态处理；不能用CLI文档直接认定WorkBuddy桌面所有事件相同。 |
| 豆包Windows原客户端 | 存在调试接口观测的实现依据，适合先做客户端专项探针；未找到面向普通用户的任意会话全局监听订阅接口。 | 需要适配客户端启动方式、页面和任务协议；目前不应承诺与WorkBuddy一样的一键完整监听。 |
| 豆包网页版 | 有针对用户原页面的流式回复采集源码，可作为备选入口。 | 不必另开模型应用即可观察网页流；不能自动覆盖Windows桌面客户端。 |

以上是根据下列官方资料和公开实现作出的工程判断。原客户端生命周期验证在后续验收单列；本轮通过的是研究问题与证据核验。

## 1. WorkBuddy并非没有扩展入口

WorkBuddy自己的[插件说明](https://www.codebuddy.cn/docs/workbuddy/From-Beginner-to-Expert-Guide/Function-Description/Plug-In)把Hooks列为插件组件，与Skills、MCP、快捷指令和Agents并列。[更新日志](https://www.codebuddy.cn/docs/workbuddy/Changelog)的5.3.5条目（2026-07-25）也记录了扩展插件Hook。这是直接针对WorkBuddy的证据，不依赖把CodeBuddy文档标题误读成桌面能力。

用户提到两者“相通”，有三个层次：

- [官方订阅说明](https://www.codebuddy.cn/docs/workbuddy/Pricing)说明同账号共享积分。这是账户权益关系，不是事件接口共享的证明。
- [WorkBuddy项目说明](https://www.codebuddy.cn/docs/workbuddy/From-Beginner-to-Expert-Guide/Function-Description/Project)说明代码开发时读取`.codebuddy/`项目配置，并兼容CodeBuddy规则、Agent、Skill和记忆等目录；官方对这一条的适用条件是代码开发。
- 公开项目[deja-vu安装实现](https://github.com/vshulcz/deja-vu/blob/6cda1006caad46216c2159b7d35529231d957959/cmd/deja/install_codebuddy.go)为WorkBuddy复用CodeBuddy式Hook配置，但使用WorkBuddy自己的配置目录。它的[会话读取源码](https://github.com/vshulcz/deja-vu/blob/6cda1006caad46216c2159b7d35529231d957959/internal/sources/codebuddy.go)分别处理`.workbuddy`、`.workbuddy-ai`和会话JSONL。这是该项目的实现证据，不是腾讯对全部发行版目录的永久保证。

因此，产品关系和协议复用有实际依据；接入时仍应把WorkBuddy作为独立平台发现、配置和诊断。

## 2. WorkBuddy能监听到哪些状态

[codex-pets的WorkBuddy安装器](https://github.com/nudoo/codex-pets/blob/b2f000812eca9991fc80fea959c3ba2a1217e348/hooks/workbuddy-install.js)实际登记8类事件：SessionStart、SessionEnd、UserPromptSubmit、PreToolUse、PostToolUse、Stop、Notification、PreCompact。其[接收器](https://github.com/nudoo/codex-pets/blob/b2f000812eca9991fc80fea959c3ba2a1217e348/hooks/workbuddy-hook.js)按session_id归属会话，缺少身份时丢弃，回传空JSON保持原权限流程；不是按WorkBuddy进程存在与否推断任务在运行。

| 看板所需能力 | 可用依据与拟用机制 | 需要按原桌面验收的具体差异 |
| --- | --- | --- |
| 绑定到准确会话 | Hook的session_id；历史目录可补会话列表。 | 桌面任务ID与底层执行ID关系；重启、恢复、改绑后保持身份；本地/云端任务分别识别。 |
| 正在执行 | 用户提交和实际工具事件形成执行轨迹。 | 提交可能先排队，工具前置可能随后等待批准；不能所有前置事件都当作已执行。 |
| 本轮响应结束 | Stop有现成桌面接收实现。 | 连续追加消息、子任务、后台命令与最终产物是否仍在工作；不能把SubagentStop当主任务结束。 |
| 等待批准 | 公开桌面适配说明权限提醒通过Notification到达。 | 等待请求是否带稳定编号，批准/拒绝/取消如何结清，以及恢复后实际执行的事件。 |
| 等待回答 | CodeBuddy有提问工具、表单/MCP输入事件；WorkBuddy官方移动协同也明确区分问答等待和审批等待。 | WorkBuddy实际工具名、事件字段、普通问句与阻塞问答区别、多个问题的逐条结清。 |
| 多任务后台监听 | 会话编号允许按会话聚合，Hook不需要持续把目标窗口置前。 | 同时运行、同时等待、同名任务、主子代理以及云端任务的覆盖。 |

关键差异：该公开实现的[平台定义](https://github.com/nudoo/codex-pets/blob/b2f000812eca9991fc80fea959c3ba2a1217e348/agents/workbuddy.js)没有启用PermissionRequest审批能力；[开发者说明](https://github.com/nudoo/codex-pets/blob/b2f000812eca9991fc80fea959c3ba2a1217e348/docs/guides/setup-guide.md)记录Windows上有带会话编号的权限Notification，而审批留在WorkBuddy原生界面。这里能借鉴的是提醒来源；本项目不需要代用户批准，也不能假设CLI审批回执会在桌面出现。

WorkBuddy[小程序多端协同官方说明](https://www.codebuddy.cn/docs/workbuddymini/features/Multidevice)也区分运行、输入等待、停止、失败及问答/审批通知。这证明产品本身有相应语义，不代表它向任意第三方公开了同一条订阅接口。

## 3. CodeBuddy能力如何借鉴

[官方Hook参考](https://www.codebuddy.cn/docs/cli/hooks)提供session_id、transcript_path等字段；可用时还提供generation_id关联同一轮提交与结束。[插件参考](https://www.codebuddy.cn/docs/cli/plugins-reference)列出PermissionRequest、Elicitation及结果事件，并区分整会话插件Hooks和只在Skill/子代理作用域内生效的Hooks。

推荐借鉴“整会话事件→本地小型接收器→按会话和轮次维护状态”的机制。不能只写一个提示AI自行调用的Skill来充当完整监听：它未必每轮被选用，且离开Skill作用域可能没有事件。

需要单独处理Windows入口：CLI官方文档描述Git Bash执行命令；公开桌面适配中又出现版本相关的PowerShell直启处理。这说明现有Claude接收器的配置格式、无输出行为及启动参数不能不经核对直接复制到WorkBuddy。

CodeBuddy SDK和`--serve` HTTP API可用于自己运行Agent；仅启动一个新的CodeBuddy服务，不等于旁听用户已经打开的WorkBuddy任务。WorkBuddy内部可能存在类似本机服务，但这应作为真实客户端适配时的独立核查对象。

## 4. 豆包Windows：路线与边界

完整来源分析见[豆包专项调研](20261117-doubao-listener-sources.md)。这里保留与产品决策直接相关的结果。

### 官方插件和模型API

豆包已有[应用插件开发文档](https://open.doubao.com/apps-plugin/docs/overview/introduction.md)，支持通过MCP提供自己的服务，且介绍页说明当前接入采用邀请制。[服务端接口总览](https://open.doubao.com/apps-plugin/docs/server-api/overview)中的任务与通知面向插件业务及其会话授权。它们不能直接等同于“监听用户所有豆包聊天和工作任务”。火山方舟模型API、扣子应用API也需要分清自己的应用运行与既有豆包消费端会话。

### Windows原客户端调试接口

两个公开项目提供了桌面观测的技术依据：[Dream Work Theme](https://github.com/xxxhh336/dream-work-theme)有豆包Windows启动/页面识别及CDP处理，[beautiCode](https://github.com/starsstreaming/beautiCode)也包含豆包客户端适配。可以借鉴的是连接调试接口、找到豆包页面的方式；它们的换肤与注入功能不是任务状态监听的验收结果。

同时，[Token Monitor的豆包工作调研Issue](https://github.com/Javis603/token-monitor/issues/586)记录特定Windows版本正常启动时没有可直接连接的标准CDP入口。该Issue研究额度采集，其失败不能扩大成“任务状态永远不能监听”；但正常启动与通过特定调试参数启动必须分开评估。

建议先验证安装版本能否以支持的调试方式暴露只读观察通道，再监听已有页面的请求与结构状态。具体启动参数、固定端口、工作模式入口和版本差异见专项报告。本轮没有改变用户豆包的启动方式。

### 网页备选与不能混用的案例

[sse-devtools-panel](https://github.com/FatMii/sse-devtools-panel)有豆包专用流解析器，能观察用户原网页消费的流式数据，并识别回复结束。这是“原页面被动观察”路线的源码依据；浏览器扩展不会自动装进Windows豆包的内置页面。

[dsh-connect-doubao](https://github.com/Wahwayhale/dsh-connect-doubao)虽有Windows协议实现，但当前路线用独立Chromium登录及HTTP发起会话。它能帮助理解协议，不能作为“已经监听原桌面”的案例。其工作流命令还涉及执行结果回传，不应原样引入只读监听器。

AutoCLI的`doubao-app status`源码显示的是连接、URL、标题，不是任务运行状态；等待回答结束的逻辑还包含页面指示器轮询。不能因为存在status命令就承诺具备完整状态接口。

| 豆包所需能力 | 本轮判断 |
| --- | --- |
| 会话绑定 | 协议和页面中有会话身份的实现依据；Windows当前客户端的目标页、会话切换与工作会话映射需专项验收。 |
| 正在输出/普通回复结束 | 网页流有直接源码依据；迁移到桌面取决于可连接页面和观察通道。 |
| 深度思考/研究/生成PPT等异步工作 | 必须跟踪对应任务及产物终态；一条回复流结束不代表后台文件生成结束。 |
| 等待回答/批准与逐条结清 | 未找到可直接复用的完整桌面Hook实现；应从真实结构事件或原生交互状态定位，不能猜正文。 |
| 多会话同时后台监听 | 须验证离开页面后的任务是否持续产生可观察事件、是否需要每个目标页独立连接；页面轮询不能自动覆盖所有会话。 |
| 默认启动后零配置监听 | 已有具体版本无标准CDP的公开失败记录；不作为现成能力承诺。 |

## 5. 方案比较与建议

| 路线 | 得到什么 | 代价/局限 | 建议 |
| --- | --- | --- | --- |
| WorkBuddy整会话Hooks | 后台、会话身份、执行与结束事件有现成依据。 | 需一次初始化；桌面审批通知、问答工具和版本差异要适配。 | 第一优先，按独立平台实现。 |
| WorkBuddy日志/目录补充 | 已有会话列表、历史与重连辅助。 | 文件存在或更新时间不能证明正在运行。 | 配合Hooks，不单独判定实时状态。 |
| 豆包Windows调试通道观察 | 有机会保持用户原客户端用法，直接读真实页面/协议状态。 | 版本与启动条件、调试入口、后台任务覆盖需原客户端探针。 | 第二条专项路线，先证明完整生命周期再并入。 |
| 豆包网页扩展 | 已有被动流采集可借鉴，普通回复开始/结束较直接。 | 用户需用网页版；后台工作与等待信号仍需适配。 | 用户愿意使用网页时的备选。 |
| 系统通知或界面观察 | 可以补充提醒，入口要求较低。 | 受通知设置、当前可见会话、控件暴露和版本影响；缺稳定请求号时难逐条结清。 | 仅作明确标注能力的补充来源。 |
| 新建SDK/API会话、提示AI主动报状态 | 便于控制自己启动的任务。 | 改变用户用法，不能自动监听既有消费端任务。 | 不用于替代本次需求。 |

实施建议属于本轮研究结果，尚不是新确认的产品规则。现行M11仍维持已授权平台边界。

## 6. 后续原客户端验收

1. 对每个平台记录准确版本、安装身份、配置目录、事件来源及稳定会话号；建立两个同名会话，确保不串任务。
2. 逐项执行：发起→实际工作→提问等待→回答→继续工作→权限等待→批准/拒绝→本轮结束。记录原始结构字段及编号，仅保存监听必要的白名单，不保留正文和答案。
3. 同时存在两个待处理请求时只结清被处理的一项；补充消息排队、计划审批、取消、失败、子任务结束均不造成提前完成。
4. 切换会话、缩小/后台、断线重连、客户端恢复、看板重启及改绑后仍按当前会话和轮次处理；过期结束事件不能覆盖新轮次。
5. 豆包单列普通聊天、深度思考/研究、PPT/文件生成等实际可用工作类型；取得产物或任务终态，不把HTTP断流/关闭页面当成功。
6. 验证原权限流程不被监听器改变；关闭看板后客户端继续正常使用，停用初始化能只移除本项目登记项。

后续实现可复用本项目`AiPlatformObserver`、`AiEventState`的职责划分，但各平台独立解析与配置。已按Codebase Memory核对项目根及ready状态，再定位符号并用现行源码核查；图的调用追踪未给出可用边，未拿它推断行为。新增平台前仍需按现行流程审查共享状态与验证范围。

## 7. 本轮实际核验

WorkBuddy：获取官方页面及公开仓库固定版本快照，离屏解析Hook配置、会话字段和桌面适配代码；[9项源码/契约检查](../tasks/E03-workbuddy-doubao/WORKBUDDY-SOURCE-CHECK.json)通过。这里没有原客户端往返或桌面测试。

豆包：取得并核对专项报告列出的官方接口和公开源码，23个源码快照重新计算Git blob及SHA256均吻合。实际运行公开SSE/豆包解析器：仅去除TypeScript类型并补一个文件导入后缀，7项离屏协议重放通过，见[回执](../tasks/E03-workbuddy-doubao/DOUBAO-PARSER-REPLAY.json)。输入为上游公开夹具及本地构造的协议边界样本，不是用户原客户端数据。未读取登录Cookie、发送消息或开启调试端口。

本轮新增调研及证据文件，更新当前任务入口；未替换或删除生产实现，未新增数据兼容分支，产品版本与运行包保持。
