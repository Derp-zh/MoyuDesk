# 构建与检查工具

详细模块职责与回归说明见 [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) 与 [docs/architecture/VERIFICATION.md](../docs/architecture/VERIFICATION.md)。

| 工具 | 用途 |
| --- | --- |
| `check_project.py` | 检查 `src/Taskboard.Core` 与 `src/Taskboard.Desktop` 的 `Modules/*.props` 文件归属完整性，并确认生产源码未混入已退役的旧管道/RPC/订阅代码 |
| `Build-Portable.ps1` | 将当前源码发布为独立的自包含 Windows x64 便携目录（拒绝覆盖已有目录） |
| `package_current.py` | 对便携目录打包生成 ZIP 与 SHA256 校验文件并逐文件回读核验 |
| `PackageAudit` | 离屏只读检查便携目录的程序集、嵌入资源与运行时完整性 |
| `CodexPassiveChecks` | Codex 被动观察与行为日志回归检查（`--daily` / `--behavior`） |
| `AiModulesChecks` | AI 平台设置、目录、恢复与绑定检查（默认离屏；涉及桌面或剪贴板模式须先征得用户同意，Codex 原客户端探针使用 `TASKBOARD_TEST_CODEX_THREAD_ID` 指定测试会话） |
| `AiListeningChecks` | DSH / Claude 状态聚合与存储检查 |
| `DesktopVerification` | 独立 WPF 组件验证程序 |
| `DesktopAcceptance` | 受控隔离进程的真实输入与 UIA 读取驱动（**执行前必须按 AGENTS.md 征得用户同意**） |
| `RenderProbe` | 共享表面渲染探针（可能显示窗口/采样屏幕，**执行前必须按 AGENTS.md 征得用户同意**） |
| `CodebaseMemoryClient.py` | Codebase Memory 图索引辅助客户端 |

> 历史版本专用的单轮验收工具与一次性脚本已归档在 Git 标签 `pre-cleanup-0.9.52` 中，不在日常工作区保留。
