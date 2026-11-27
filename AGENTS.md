# 项目协作约定

## 1. 沟通与需求

- **默认中文**；用户不是程序员，用通俗简洁的话解释逻辑与效果，少用晦涩术语。
- 重要且不明确的需求先提供简洁方案供用户选择；用户已明确授权的修改直接实施。
- 新功能开发或行为修改前，先阅读 [docs/HANDOFF.md](docs/HANDOFF.md)、[docs/STATUS.md](docs/STATUS.md) 及 `docs/current/` 下对应的模块规则；修改后同步更新对应文档与测试。

## 2. 桌面操作与数据安全（硬性红线）

- **桌面操作前必须先询问**：任何会移动鼠标、发送键盘按键、弹出/切换前台窗口或临时读写系统剪贴板的操作，开始前必须先询问用户**“现在是否方便进行桌面操作？”**，简述操作内容与预计影响范围，**收到用户明确同意后才可开始**。仅提示“即将开始”、等待未回复、或沿用之前的授权均不算同意。同一连续授权范围内无需逐动作重复问，范围扩大或中断后重新开始需再次询问。纯文件读写、编译、离屏测试可正常直接运行。
- **严禁触碰正式数据与运行中环境**：
  - 用户正式数据与配置在 `%LOCALAPPDATA%\InstantTaskboard`，历史数据备份在 `preserved-data/` 和 `archive/早期版本数据/`。禁止在开发或测试中读写、修改或删除它们。
  - 所有测试必须显式指定 `test-output/` 下的新隔离 `--data-dir`。
  - 不要关闭或重启用户正在运行的 Codex、DSH 或正式版任务看板进程；不要修改系统开机自启项；打包输出使用新的目录，不覆盖正在运行的旧包。

## 3. 代码修改与验证

- 用户未要求改变的触发范围、操作方式、菜单位置和窗口层次保持不变。
- 修改共享背景、透明度、原生命中、动画交接或输入处理前，先读 [docs/architecture/SHARED-CODE.md](docs/architecture/SHARED-CODE.md) 和 [docs/architecture/VERIFICATION.md](docs/architecture/VERIFICATION.md)，确认受影响的消费者并跑对应回归。
- 新增或删除生产源码文件时，同步更新 `src/*/Modules/*.props` 并运行 `python tools/check_project.py`。
- 区分离屏测试与真实客户端实测结果：未在真实客户端实测的场景如实标注，不把模拟夹具当作真实客户端全流程通过。

## 4. 全自动托管：索引、文档与版本日志（无需用户提醒）

AI 必须主动承担代码库整理、功能归档与日志更新职责，**禁止等用户开口提醒才更新文档**：

- **开始任务前**：自动查阅 [docs/HANDOFF.md](docs/HANDOFF.md)、[docs/STATUS.md](docs/STATUS.md)、待办总表 [docs/BACKLOG.md](docs/BACKLOG.md) 及 [docs/current/README.md](docs/current/README.md) 中对应的模块规则。
- **区分“直接改”与“先给方案”**：对于 [docs/BACKLOG.md](docs/BACKLOG.md) 或用户需求中标记为“先给方案/排查原因供选择”的条目，先用大白话列出原因和 2–3 个清晰方案供用户拍板，**严禁未经确认直接脑补动工**；已明确的需求则直接实现。
- **每次修改代码后（结束回复前必做）**：
  1. 自动运行 `python tools/check_project.py`，确保所有生产源码 `.cs` 文件均已登记在 `src/*/Modules/*.props` 中（由 `.agents/hooks.json` 自动门禁兜底监督）。
  2. 若新增或改变了模块职责，自动同步更新 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)；若改变了功能规则，自动原位更新 `docs/current/` 下对应的模块文档。
  3.若完成了 [docs/BACKLOG.md](docs/BACKLOG.md) 中的事项，自动将该事项状态更新为已完成并注明版本号。
- **每次构建新便携版本后（结束回复前必做）**：
  1. 自动将新版本变更写入 [CHANGELOG.md](CHANGELOG.md)。
  2. 自动更新 [docs/HANDOFF.md](docs/HANDOFF.md) 和 [docs/STATUS.md](docs/STATUS.md) 中的“当前源码版本”与“最新本地构建包”路径。
  3. **在最终回复末尾附上可打开路径**：新版本开发或打包任务完成后，必须在回答最后清晰附上新版便携包目录及可执行程序（`.exe`）的打开路径（以及建议的隔离测试 `--data-dir` 启动方式），方便用户直接打开进行验收与体验测试。

## 5. 版本与发布

- 普通本地修改只保存在本地，**不自动上传 GitHub**。
- 仅在用户明确要求更新 GitHub / 发布新版本时，才同步远端仓库或创建 GitHub Release（附便携 ZIP 与 SHA256 校验）。
