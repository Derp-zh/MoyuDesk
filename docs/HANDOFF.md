# 摸鱼台接手入口

欢迎接手本项目。开始工作前请先阅读本文件与根目录 [AGENTS.md](../AGENTS.md)。

## 1. 文档导航

| 你需要了解什么 | 看哪里 |
| --- | --- |
| 项目是什么、怎么用 | [README.md](../README.md) |
| 历次版本做了什么、旧证据怎么查 | [CHANGELOG.md](../CHANGELOG.md)（完整历史快照保存在原本地仓库的 Git 标签 `pre-cleanup-0.9.52`） |
| 当前能力边界、已知问题、待办事项 | [STATUS.md](STATUS.md) |
| **用户最新 30 项待开发需求与分批计划（Backlog）** | **[BACKLOG.md](BACKLOG.md)** |
| 代码模块划分、怎么编译和跑测试 | [ARCHITECTURE.md](ARCHITECTURE.md)、[tools/README.md](../tools/README.md) |
| 各功能模块的详细现行需求规则 | [current/README.md](current/README.md)（按需只读目标模块） |
| 共享界面/动画/命中修改影响与回归要求 | [architecture/SHARED-CODE.md](architecture/SHARED-CODE.md)、[architecture/VERIFICATION.md](architecture/VERIFICATION.md) |
| WorkBuddy、豆包、飞书、Claude 桌面调研记录 | [research/](research/) |
| 常用术语定义 | [GLOSSARY.md](GLOSSARY.md) |

## 2. 当前版本与运行包现状

- **当前源码版本**：`0.9.53`
- **保留的本地运行包**（位于 `release/`）：
  - `release/instant-taskboard-0.9.51-guided-reminders-r4-win-x64`：**用户当前开机自启正在使用的正式包**，请勿删除或覆盖。
  - `release/Taskboard-0.9.52-claude-regression-r1`：0.9.52 本地构建包。
  - `release/MoyuDesk-0.9.53-portable-win-x64`：最新本地构建包，入口为 `MoyuDesk.exe`。
- **最新 GitHub 公开版**：[摸鱼台 0.9.53](https://github.com/Derp-zh/MoyuDesk/releases/tag/v0.9.53)。本地修改默认不上传 GitHub，除非用户明确要求发布。

## 3. 核心安全约束（必读）

1. **保护正式数据**：用户正式任务与配置存储在 `%LOCALAPPDATA%\InstantTaskboard`，早期备份保存在 `preserved-data/` 和 `archive/早期版本数据/`。任何测试或开发都**不得读写、覆盖或清理**这些目录，测试一律使用 `test-output/` 下的新 `--data-dir`。
2. **不打扰正在运行的环境**：不要关闭或重启用户正在运行的 Codex、DSH 或正式版任务看板进程；不要修改系统开机自启项。
3. **桌面操作前必须询问**：运行任何会占用鼠标/键盘、弹出前台窗口或使用剪贴板的程序前，必须先向用户询问“现在是否方便进行桌面操作？”，说明预计影响范围，收到用户明确同意后才可执行。
