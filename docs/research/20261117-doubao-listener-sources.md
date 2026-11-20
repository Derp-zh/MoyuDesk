# 豆包现有会话状态监听：公开一手资料核查

- 核查日期：2026-11-17。
- 用户场景：优先监听用户已经使用的 Windows 豆包电脑版，网页版仅作备选。
- 本次范围：公开官方文档、开源仓库实际源码及维护者亲自记录的复现。未启动、停止或连接用户豆包；未读取账号、Cookie、正式配置、私人会话；未占用鼠标、键盘、前台或剪贴板。
- 证据目录：`test-output/E03-doubao-public-sources/`；23 个源码快照、Git blob/SHA256 清单及 `minimum-verification.json`。

## 结论

**Windows 桌面有值得继续验证的监听路线，最明确的是给特定客户端版本开启本机 CDP 后，观察其聊天 renderer 的网络流及界面状态。公开源码已证明 Windows 豆包存在这种接入实现，但这些源码没有交付一个已验证、完整区分“工作中／等待提问／等待批准／本轮结束”的通用监听器。**

尤其要区分正常启动和调试启动：有 Windows 开源实现通过重新启动豆包并添加调试参数接入；也有维护者在正常启动的 Windows 豆包工作版本上明确复现“没有标准 CDP”。这两种结果对应不同版本和启动条件，并不矛盾。不能从“都是 Chromium/Electron 一类应用”推导用户当前打开的窗口可直接旁听。[Windows 适配实现](https://github.com/starsstreaming/beautiCode/blob/c6a4f770c898badb52fbb7b785743c347f07cc55/docs/host-adapter-cursor-doubao.md)、[调试启动参数](https://github.com/starsstreaming/beautiCode/blob/c6a4f770c898badb52fbb7b785743c347f07cc55/packages/adapter-desktop-cdp/src/launch.ts#L196-L211)、[正常启动失败的维护者复现](https://github.com/Javis603/token-monitor/issues/586)

本次研究能确定路线、接口的真正范围和下一步真实客户端验收要求；不把公开实现的静态核查写成用户原桌面已接通。

## 证据等级

| 标记 | 本文含义 |
| --- | --- |
| 官方支持 | 官方拥有的文档明确承诺的能力；仍须遵循其准入及作用域 |
| 源码证明 | 已读取具体开源实现；证明该代码做了什么，不等于本轮运行用户客户端成功 |
| 工程推断 | 从协议或平台机制推导的实现方向，尚无当前客户端端到端结果 |
| 待客户端验证 | 必须在明确产品、版本、启动方式、账号与操作场景下采样确认 |

## 一、官方能力：确有插件与模型 API，但作用域不同

### 1. 豆包应用插件不是全局会话旁听接口

**官方支持。** 豆包存在应用插件平台，通过 MCP、Skill、对话内卡片及端侧工具提供开发者自己的服务。官方介绍的 FAQ 写明当前采用邀请制；这不应被描述为普通 Windows 用户立即可用的本地 Hook。[应用插件介绍](https://open.doubao.com/apps-plugin/docs/overview/introduction.md)

服务端 API 包括登录、支付、创建／更新进行中任务、服务通知和刷新卡片。`X-DB-SESSION-TOKEN` 来自豆包调用**该开发者插件的 MCP 工具**，用于把开发者任务关联到当前会话。任务接口由开发者创建、维护自身业务状态，不能据此读取或订阅该账号任意已有会话的总状态。[服务端 API 总览](https://open.doubao.com/apps-plugin/docs/server-api/overview.md)、[多任务管理接入](https://open.doubao.com/apps-plugin/docs/backend-development/multi-task.md)

`onAgentEvent`／`useAgentEvent` 也是已声明业务事件的插件内部投递链：事件先由 `events.json` 定义并经 Skill 暴露，再由宿主投递给插件 App／Page。它不保存或重放历史 payload；不是任意程序订阅豆包宿主所有提问、批准和完成事件的通用 Hooks。[Agent 与端侧交互](https://open.doubao.com/apps-plugin/docs/client-framework/agent-interaction.md)

**检索结论。** 在本次读到的官方文档范围内，未发现“外部本地程序订阅用户所有现有豆包对话”的公开契约，也未发现用于本场景的官方本地任务日志／全局 Webhook 规范。这是有边界的检索结果，不宣称这些能力在所有内测、内部接口或未来版本绝对不存在。

### 2. 火山方舟、扣子能管理自己发起的运行

**官方支持。** 火山方舟快速入门是使用方舟 API Key、模型 ID 和 API 请求发起模型调用。它提供另一个应用调用模型的方式；文档没有把该 API Key 授权为豆包消费端账号已有聊天的旁听凭证。[方舟快速入门](https://docs.volcengine.com/docs/ark/quick-start?lang=zh)、[鉴权与地址](https://docs.volcengine.com/docs/ark/base-url-and-authentication?lang=en)

扣子 `/v3/chat` 有 `in_progress`、`completed`、`requires_action` 等明确状态，但请求需要扣子智能体的 `bot_id`，状态属于该 API 所管理的对话。`requires_action` 指需要调用方继续提交工具结果，也不能直接等同豆包客户端弹出的人工批准。[扣子发起对话](https://docs.coze.cn/developer_guides_chat_v3)、[提交工具结果](https://docs.coze.cn/developer_guides_chat_submit_tool_outputs)

**工程判断。** 如果看板将来另做一个由自己发起的模型／智能体工作入口，这些 API 的状态很好用；它们不满足本轮“继续使用原豆包电脑版，观察它的真实状态”的同一要求。

## 二、Windows 桌面：CDP 有源码路径，必须核对版本及启动方式

### 1. 能确认的实现

**源码证明。** `starsstreaming/beautiCode` 的豆包适配明确以 Windows 为范围，记录基线 2.29.12；宿主规格指定 `Doubao.exe` 和 `doubao://doubao-chat/chat`，默认候选调试端口从 9342 开始。启动函数显式添加 `--remote-debugging-address=127.0.0.1` 和 `--remote-debugging-port=<port>`。这不是 macOS AppleScript，也不是只操作 Chrome 网页的同名命令。[宿主规格](https://github.com/starsstreaming/beautiCode/blob/c6a4f770c898badb52fbb7b785743c347f07cc55/packages/adapter-doubao/src/spec.ts)、[启动代码](https://github.com/starsstreaming/beautiCode/blob/c6a4f770c898badb52fbb7b785743c347f07cc55/packages/adapter-desktop-cdp/src/launch.ts)、[Windows 适配说明](https://github.com/starsstreaming/beautiCode/blob/c6a4f770c898badb52fbb7b785743c347f07cc55/docs/host-adapter-cursor-doubao.md)

它的用途是背景注入，证明特定 Windows 豆包 renderer 曾被代码接入，不证明其已实现任务状态语义。文档还明确写了在特定启动窗口内受控重启原客户端以加上 CDP 参数；本轮没有安装、执行或照搬这个启动器。

**维护者实测记录。** `token-monitor` 的 Issue #586 记录 Windows 11、Doubao Work `147.0.7727.149` 正常启动时 9333 未监听，其他动态端口也不提供 `/json`、`/json/version` 或 `/json/list`，进程未带调试参数。它是该环境的具体失败场景，失败原因是没有外部标准 CDP 入口；不能扩大成所有 Windows 豆包都不可接入。[维护者原始记录](https://github.com/Javis603/token-monitor/issues/586)

**待客户端验证。** 用户安装的是“豆包”还是另一个“豆包工作”发行形态、精确版本／可执行文件、是否本来暴露标准 CDP，都须先核对。不得只用产品名、进程名或搜索结果中的“Electron”判断。

### 2. UI Automation 是候选回退，不是豆包官方状态 SDK

**官方支持的平台能力。** Windows UI Automation 可订阅控件属性、结构和其他 UI 事件，但微软明确要求客户端不能假设提供者会发出所有可能事件。[UIA 概览](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)、[订阅事件及限制](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsforclients)

**工程推断。** 若豆包当前版本暴露停止生成按钮、确认／取消控件、明确的提问卡和会话标识，UIA 可在不抢输入的方式下读取；若控件没有可访问名称、任务在不可见页面运行或状态只在私有 runtime 中存在，UIA 会缺失信息。本次没有取得豆包真实 UIA 树，不能将微软通用能力写成豆包已支持。

## 三、两个容易被误读的开源项目

### 1. AutoCLI：`status` 是连接状态，`ask` 是轮询启发式

已固定读取 `nashsu/AutoCLI` 提交 `c0969e2c83b29a7528452b1ba555085deca8e00d`：

| 文件／命令 | 实际代码 | 可支持的结论 |
| --- | --- | --- |
| `doubao/status.yaml` | 导航网页，读取登录标识、URL、标题 | 页面和登录可达性，不是正在工作 |
| `doubao/read.yaml` | 查询 message-list、union_message 等 DOM | 当前已渲染消息，不是全账号状态流 |
| `doubao/ask.yaml` | 发消息，约每 2 秒读末条文字，两次稳定后返回 | 文字稳定启发式，长思考或异步空档会误判 |
| `doubao-app/status.yaml` | 直接返回 Connected、当前 URL、标题 | CDP／页面连接，不是运行语义 |
| `doubao-app/read.yaml` | 读 DOM，跳过 indicator，单条截断 2000 字 | 当前 renderer 可见文本，不含正在生成部分 |
| `doubao-app/ask.yaml` | 发消息，看到无 indicator 的非空回复即返回 | 有回复文本，不代表工作任务完成 |

[网页 status](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/adapters/doubao/status.yaml)、[网页 ask](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/adapters/doubao/ask.yaml)、[桌面 status](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/adapters/doubao-app/status.yaml)、[桌面 read](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/adapters/doubao-app/read.yaml)、[桌面 ask](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/adapters/doubao-app/ask.yaml)

其 CDP 模块可以连接提供给它的 WebSocket endpoint，但以上 YAML 没有自行证明 Windows 当前原进程的端口已存在，也没有会话级等待／批准事件模型。因此命令名、Windows CLI 安装包和“Desktop”标签都不等于这项监听已可交付。[CDP 传输实现](https://github.com/nashsu/AutoCLI/blob/c0969e2c83b29a7528452b1ba555085deca8e00d/crates/autocli-browser/src/cdp.rs)

### 2. dsh-connect-doubao：独立登录并发 HTTP，不能冒充原桌面旁听

当前提交 `58bcc89710f9cbe7192db43462bc02e6c6cc86e8` 的 README 明确改为独立 Chromium 登录，经 CDP 取 Cookie 并使用 Windows DPAPI 本地密封，后续纯 HTTP。它不要求用户安装／登录原豆包桌面。[当前 README](https://github.com/Wahwayhale/dsh-connect-doubao/blob/58bcc89710f9cbe7192db43462bc02e6c6cc86e8/README.md)

认证源码说明旧的桌面 Cookies 读取路线已移除：运行中的数据库独占锁导致读失败，回退旧副本后又出现会话轮换过期。该源码证明开发者遇到并处理过这个具体失败链，不代表本轮读取了用户凭据。[认证源码](https://github.com/Wahwayhale/dsh-connect-doubao/blob/58bcc89710f9cbe7192db43462bc02e6c6cc86e8/src/doubao-auth.ts#L1-L29)

该项目对 `/chat/completion` 的流解析有可借鉴之处：从 `SSE_ACK` 提取 conversation／question ID，处理 `STREAM_ERROR`、消息块和 `SSE_REPLY_END`；流在语义结束帧前断开会报告截断，避免把网络关闭当完成。[流解析源码](https://github.com/Wahwayhale/dsh-connect-doubao/blob/58bcc89710f9cbe7192db43462bc02e6c6cc86e8/src/doubao-upstream.ts#L1483-L1658)

但“工作任务”应答器会主动调用 ack、上传 command result、提交 complete，甚至自行构造 operation success 返回；这是参与执行的代码，不是旁听器，不应原样借用来观察用户原任务。[工作任务应答器](https://github.com/Wahwayhale/dsh-connect-doubao/blob/58bcc89710f9cbe7192db43462bc02e6c6cc86e8/src/doubao-upstream.ts#L1051-L1152)

## 四、网页版备选：已有真正观察原网页流的源码

**源码证明。** `FatMii/sse-devtools-panel` 在浏览器扩展的 MAIN world、`document_start` 安装捕获器。它调用原始 fetch，并观察网页消费的 response reader，而不是为了取得结果重新向 AI 提问。[扩展声明](https://github.com/FatMii/sse-devtools-panel/blob/34fe418afa9fd26b955039d956fdf0e4db559e9c/manifest.json)、[fetch 捕获](https://github.com/FatMii/sse-devtools-panel/blob/34fe418afa9fd26b955039d956fdf0e4db559e9c/src/content/inject/patch-fetch.ts)、[reader 包装](https://github.com/FatMii/sse-devtools-panel/blob/34fe418afa9fd26b955039d956fdf0e4db559e9c/src/content/inject/stream.ts)

其豆包专用解析器处理 `STREAM_MSG_NOTIFY`、`STREAM_CHUNK`、`CHUNK_DELTA`，根据 block_type／父子块区分思考、正文及搜索结果，并在 `SSE_REPLY_END` 且 `end_type=1` 时记录结束。这比“末条文字几秒没变化”更接近协议事实；仍不能把流结束扩大成异步业务任务完成。[豆包解析器](https://github.com/FatMii/sse-devtools-panel/blob/34fe418afa9fd26b955039d956fdf0e4db559e9c/src/shared/ai-merge/doubao.ts)

**作用边界。** 它的声明覆盖浏览器页面，不自动安装到 Windows 豆包专用 renderer；未注入时已经开始的流不在采集窗口内；页面关闭、worker／其他传输、协议变更及扩展重载都应单独验证。源码没有给出通用人工批准／提问等待事件映射。

历史读取也有源码：`5201213/doubao-free-api` 实现 `/im/chain/recent_conv`、`/im/conversation/info`、`/im/chain/single` 的请求／导出及浏览器回退。这属于非官方私有协议，且依赖登录态；读到历史消息不能证明某轮仍在工作、正在等人或任务已完成。[IM 请求实现](https://github.com/5201213/doubao-free-api/blob/0240ee33f5156d26da72c0eb4b093389e9ca9ad4/im_api.py)、[导出实现](https://github.com/5201213/doubao-free-api/blob/0240ee33f5156d26da72c0eb4b093389e9ca9ad4/exporter.py)

## 五、工作／深度研究／PPT：不能套用普通聊天结束条件

**源码证明的例外。** `doubao2api` 的视频生成实现会从第一段响应中提取 `fin_reason.async_task.id`，再以 task ID 等待结果。它足以说明豆包存在“第一段回复结束后，业务仍需继续取结果”的协议形态；它是视频代码，不能据此臆造 PPT 或深度研究的同名状态字段。[异步视频实现](https://github.com/wangchuxiaoji-oss/doubao2api/blob/95beb0788338ba268e13ca4c80890ceeae6ff055/doubao2api/browser_client.py#L1200-L1288)

“工作任务”还有本地工具命令／agent_finished 链，和普通聊天的文字块不同。上述 DSH 实现证明这种协议分层，不能证明其工具处理本身等价于官方工作客户端行为。[任务协议实现](https://github.com/Wahwayhale/dsh-connect-doubao/blob/58bcc89710f9cbe7192db43462bc02e6c6cc86e8/src/doubao-upstream.ts#L1051-L1152)

**待客户端验证。** 深度研究、PPT／文档生成至少分别采集：任务接受、初始文本结束、后台运行、追问／授权暂停、恢复、产物就绪、取消和失败。没有取得对应实际终态信号前，不能由文案含“完成”、进度 100%、文件名出现、停止按钮消失或单条 SSE 结束自动认定业务完成。

## 六、对即时任务看板的具体建议

| 看板需要的状态 | 候选客观信号 | 本次证据与仍需做的验证 |
| --- | --- | --- |
| 工作中 | 该会话新请求／ACK、持续流事件或明确任务 running | 普通网页流与协议已有源码；Windows 原 renderer 的取得方式需按当前版本验证 |
| 等待用户回答 | 明确 question／input 卡或结构化中断，带可关联请求 ID | 本次所读源码没有通用豆包契约；不能根据助手问句或问号判断 |
| 等待批准 | 明确批准控件／授权请求和恢复事件 | 插件登录／支付能力不等于全局批准监听；需真实 Windows 控件与关联 ID 证据 |
| 本轮结束 | 明确回复终止帧，并确认没有活跃子任务或待处理人工请求 | SSE_REPLY_END 有源码；普通聊天、工作及异步任务应分别验收 |
| 失败／断连 | STREAM_ERROR、取消、网络中断、客户端退出 | 断连单列，不能降为成功或等待用户 |

优先研究顺序：先只读辨认当前 Windows 产品／版本／可观测入口；有现成标准 CDP 时验证被动采样；没有时评估下一次用户允许的调试启动方案及 UIA 回退。开始占用桌面输入、切换前台或改变启动方式前，仍须按项目约定取得本次方便确认；本报告没有代替该同意。

采集层应保留 conversation／message／task／request 的原始关联及信号来源，遇到无法分类的事件保留 unknown，不能把“没有看见正在工作”改写成“已完成”。跨重连、多会话、相同问题重复出现、后台不可见任务和断流恢复均须作为真实客户端验收项。

## 七、本次已执行的最小验证与失败原因

1. 通过 GitHub 插件实际读取关键源码，固定 6 个项目的提交及 23 个文件。已落盘快照逐个按 Git blob 格式重新计算 SHA1，**23/23 与 GitHub blob SHA 一致**；同时保存 SHA256、来源链接、提交、路径和字节数。详见 `source-manifest.json`。
2. 实际执行研究者写的 6 项静态字符串断言，确认 AutoCLI status 范围、Windows 调试参数、扩展注入／原 fetch 路径、DSH 截断判定及认证路线变更，**6/6 通过**。这不是运行被研究产品或原实现的单元测试。
3. 最初实际解析公开豆包 fixture 的 JSON 并列举其 5 个事件；fixture 本身不含 SSE_REPLY_END。首次执行原解析器的尝试失败：node_repl 不允许 data: 模块导入，随后内存编译又因禁止字符串代码生成失败。此处是运行工具限制，不能写成协议失败，也没有写成通过；初始失败记录保存在 `minimum-verification.json`。
4. **后续重验已解决执行限制。** 主任务改用宿主 Node.js v24.14.0，将下载的原 TypeScript 解析器去除类型、补齐一个 `.mjs` 导入后实际执行，未改解析逻辑。7 项离屏协议重放通过，覆盖上游公开 fixture 的思考／正文、未收到终态、end_type=1、end_type=0、缺字段、坏 JSON 和分块输入。公开 fixture 与补充协议边界样本均非用户数据；这只证明公开解析器的行为，不证明已经捕获真实 Windows 豆包或识别人机等待。证据为 `docs/tasks/E03-workbuddy-doubao/DOUBAO-PARSER-REPLAY.json`，复现脚本为同目录 `run_doubao_fixture.mjs`。
5. 首次尝试用 node fetch 下载 raw GitHub 快照时全部得到 `TypeError: fetch failed`。随后使用先前已成功读取的 GitHub 插件原文保存，并通过 Git blob 复核解决来源一致性。
6. 官方下载页可打开但正文提取为空；部分猜测的官方 Markdown 路由返回不可访问。相关结论采用已成功取得的官方介绍／总览／Agent 交互／多任务页，不把页面解析失败当能力不存在的证据。

本次研究的完成条件是：区分官方接入与消费端旁听；核实至少一条 Windows 实现及其启动限制；核实原网页被动流采集源码；列出四态与异步任务的准确验收缺口。上述四项已有可追溯一手证据。**产品监听器开发和真实 Windows 接入验收尚未在本次执行。**
