# Claude 桌面 Code 监听：交接给主 agent

状态：离线开发分支可审查，尚未达到完整原客户端验收，不能直接作为已验收版本发布。用户明确：只要桌面 Code 页签；未安装 Claude、没有账号，暂不方便桌面操作。不要要求用户为本次验证付费或代建账号。

## 分支和合并方式

本仓库位于原工作区的 `test-output/claude-desktop-dev`，功能分支 `codex/claude-desktop-listening`，基线分支 `baseline` 是本轮开始时现行 0.9.50 文件的独立快照。原仓库 master 仍是旧工程、现行文件大量未提交，因此没有切换、暂存或提交原仓库。

交付到原工作区 `test-output/claude-desktop-delivery/`：`feature.patch` 为 baseline 到功能提交的差异；`claude-desktop.bundle` 含两条分支；`manifest.json` 记录提交及文件哈希。主 agent 应先比较 patch 涉及的现行文件与 baseline，执行 `git apply --check --ignore-space-change <feature.patch>`，确认无并发冲突后用 `git apply --ignore-space-change <feature.patch>` 应用；不要将独立 snapshot 基线整体并入原旧 master，也不要在原工作区 `git add -A`。也可以在 bundle 克隆中审阅功能提交的差异。没有上传 GitHub。

## 本轮改动

- 新增 `ClaudeDesktopCatalog`：只读桌面元数据路径 `userData/claude-code-sessions/account/org/session.json`，区分桌面 `sessionId` 和 Hook 执行 `cliSessionId`。选择器/观察器绑定稳定桌面 sessionId，读取时解析当前 cliSessionId 并隔离来源代次，以便复用原有监听状态机。
- `ClaudeCatalog.Load` 补充桌面目录标题，原 CLI 目录与事件记录入口保留。目录文件的 `isRunning` 与时间戳不作为执行或移列证据。
- 安装发现补充当前用户 MSIX `Claude` 包的 `app/Claude.exe`。目录读取补充 MSIX `LocalCache/Roaming/Claude`；不注册、启动或安装包。
- 本地目录排除 SSH、WSL、转云及归档项；错误编号、同执行ID的多桌面归属、损坏文件不会猜选；拒绝符号链接目录/文件，不扫描 waiting-input 正文。全局元数据读取最多 2000 文件。
- 新增 14 项桌面目录/身份/换执行源检查。原 Hook 安装/隐私白名单、序号与增量读取、AiState、运行阶段、多平台等待优先、改绑/红点/保存、自动建卡继续复用。

本轮没有改共享背景、命中、动画、输入、Codex连接或保存实现；Core 只对 Claude 放宽稳定 local_ 身份白名单，Codex/DSH 身份校验保持；生产新增文件已登记 AiPlatforms.props。没有新增第三方运行依赖，没有替换/删除旧执行链。原 Claude 精确跳转与外部拖到原侧栏绑定仍没有已验证实现；现行 C03 要求的会话选择/保存入口保持，不把 Codex/DSH 的外部菜单取号方案直接硬套给 Claude。

## REA 实际对象及结论

从官方部署文档提供的 MSIX 下载接口取得桌面 5.1231.2，未安装、未运行。MSIX SHA256 `49fbf2fc335a2cc19bbc8c7d22016f5effd5fb3d90f3f0ee60f5a912961ac97b`；app.asar SHA256 `f749c5bd615f907ed6cc9f8b3f9ba2fe068d8501d555399f510b992c8f75468e`。

REA 全包 parent Evidence `ev_49e266da805bd3e18809e665193dc05fb30a4502d3e3beaea148ecbf1c550ea2`：964 个相关文件、753 个 JS 文件、无语法解析失败；全包语义图达到资源/节点上限，覆盖 partial。随后按模块定点读取原始已发货 JS，并分别分析 session manager 和 persistence；汇总在 REA-EVIDENCE.json。不要把全包 partial 解释成已恢复全部调用语义。一次对 Xo 的 API seed trace 返回 no-match，不作为函数不存在的证据。

关键原模块：`.vite/build/index.chunk-DII0hfnr.js`，manager 的 `getStorageDir/getSessionFilePath/writeSessionToDisk`（字符偏移约 1951005/1951530/1969423），明确按 userData/baseDir/account/org 落盘并调用 `n.c(e)`；movedToCloud 已按反序列化的 cloudSessionId 对象排除。其导入 n 对应 `index.chunk-B6SI_2Bh.js`，导出 c 对应 Xo，返回 `sessionId/cliSessionId/title/sshConfig/wslConfig/movedToCloud` 等元数据。`index.chunk-C5rj_9J2.js` 定义 Code 基目录 `claude-code-sessions`。manager 本地执行选项包含 `settingSources:[user,project,local]`，复用原生 Hook 方案有静态依据；实际 Hook 是否在各权限/客户端版本中产生完整事件仍需实机。

区别于 DSH/Codex 已解决问题的新增困难：桌面/引擎双编号及重绕/重建引擎导致的映射变化；MSIX 路径与便携看板移动后的 Hook 入口修复；桌面账号/组织切换与后台/SSH/WSL/云执行位置；SDK/内置权限代理是否提供足够事件。并发等待优先、重连、旧事件隔离、红点和实际保存已有共享方案，不应重新列为新架构障碍。

方案采用“桌面元数据负责准确目录和命名，用户显式初始化的原生 Hook 负责结构事件，原 AiState 负责移列/保护”；不轮询 Claude 服务、不调用模型、不代答/批准，不根据窗口文字猜状态。当前新目录读取是 Code 的补充，Chat/Cowork 不属于本轮。

## 验证和原失败

- 从当前分支源码构建 AiListeningChecks、Desktop/Core：0 warning、0 error。恢复 NuGet 使用已缓存包和官方 Interop.UIAutomationClient nupkg（仅缓存于原工作区 test-output）。命令：`dotnet build tools/AiListeningChecks/AiListeningChecks.csproj --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false`。
- 设置 `TASKBOARD_AI_HOOK_EXECUTABLE` 为该构建的 Taskboard.exe，运行测试 DLL，显式新隔离 `--data-dir test-output/E02-delivery-83`；83 项通过。包括 14 个新增目录与稳定身份检查、真实当前 exe stdin→事件文件→观察器→移列→保存回读分支，以及原隔离事件/状态回归。证据随交接放置；这是合成事件输入，不是 Claude 发出的原事件，不是桌面实机。
- 上轮 NU1301 构建失败是缺少本机包且 NuGet 源不可达；本轮通过 Python HTTPS 取得官方缺包，使用本地缓存源恢复成功。默认并行 MSBuild 曾返回失败但无错误详情；单进程、不复用节点、不共享编译器构建成功，不宣称已查明并行失败的内部根因。该日志留在隔离工作目录 build-diagnostic.txt。
- 原正式数据、进程、自启、配置、preserved-data、当前 Codex 未操作。仅隔离 test-output 内生成工件。

## 主 agent 的后续验收（必须保留）

1. 有已登录的 Claude 桌面测试环境并获得本次方便确认后，验证本机 Code 会话目录、真实标题、精确绑定与 Hook 原客户端事件；专门覆盖桌面/引擎 ID 不同、会话 rewind/引擎 ID 变化、账号/组织切换。静态所见路径需按实机版本再核对。
2. 原卡提问、批准/拒绝、取消、Plan、多问题部分结清、回答后的实际执行、无工具的纯文字继续、Stop 被其他 Hook 阻止后继续、整轮失败/停止、子 agent/侧边会话分别走完。必要时扩展 Claude 适配层；共享状态保护不降低。
3. Hook 失效/禁用/未知版本、便携路径变动、看板重开/重连、丢帧和并发事件验证；收到回答不等于执行恢复。现有 PermissionRequest 的工具指纹回退匹配与匿名等待、Notification 回退结清仍需原轨迹核验，不把隔离断言代替协议正确性。
4. 验证自动移列只影响两内置列、多阶段等待优先、改绑与旧回调、红点/手工确认、自动建卡、实际落盘和重启回读；其他平台共享回归继续运行。
5. 补齐原始 A01–A04 后再 complete；当前没有账户无法做这些原客户端动作，不是降低验收为无需实机。合并可先审查源码，产品完整交付/版本发布继续等待原验收。

补充：本轮失败及修复见 evidence/failure-ledger.json；构建文件锁原日志也保留。当前桌面绑定使用稳定 local_ ID，旧GUID绑定仍按原Hook读取兼容；底层ID变化会重新读取新的事件源，旧源迟到终态被隔离。

合并预检查：原工作区4个旧文件使用CRLF、baseline Git blob使用LF；逐文件换行规范化比较内容相同。普通apply检查在这4个文件失败，使用--ignore-space-change检查已通过；不是忽略业务改动，主agent仍应按manifest核对基线。

## 主线合入回执（2026-11-16）

用户明确仍无Claude账号，先合入代码并保留原客户端验收待办。已按八个源码/测试旧文件的换行规范化基线核对合入0.9.52，并补充无效ID与目录根链接防护；见[U17记录](../U17-0952/PROGRESS.md)。此回执不修改原acceptance.json，不把离线结果计作真实客户端A02/A03完成；以上后续五项继续保留。
