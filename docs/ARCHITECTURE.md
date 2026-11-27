# 代码架构与编译测试说明

源码版本：`0.9.56`，目标框架：`net8.0-windows10.0.19041.0`（WPF + WinForms + Direct2D/D3D11 + UIAutomation）。

## 1. 模块划分

生产源码位于 `src/Taskboard.Core` 与 `src/Taskboard.Desktop`，通过各项目下 `Modules/*.props` 显式归属每个 `.cs` 文件（关闭了默认通配编译 `EnableDefaultCompileItems=false`）。新增或删除源码文件时，必须同步更新对应的 `.props` 文件并运行 `python tools/check_project.py` 校验。

| 模块 | 职责与核心文件 |
| --- | --- |
| `Core/Domain` | `Models`、`Validation`、`BoardHotkey`、`OptionPalette`：领域模型、默认配色迁移与校验 |
| `Core/Tasks` | `BoardEngine`（各 partial）、`BoardColumns`：任务、阶段、草稿、历史、归档与会话绑定核心逻辑 |
| `Core/Persistence` | `AtomicStore`、`ConfigurationBackup`、`StartupCatalog`：原子写入、备份与启动目录管理 |
| `Core/AiState` | `CodexState`、`AiState`、`AiAttention`：会话轮次与等待状态保护、多阶段聚合、恢复清点 |
| `Core/Notifications` | `ReminderClock`、`NotificationListening`：定时提醒与通知监听核心状态 |
| `Desktop/Application` | `Program`、`DataPaths`、`StartupLinks`、`AppUpdater`、`DeviceOnboarding`、`Brand`：启动入口（含 `--startup` 容错重试）、开机自启诊断、GitHub Releases 零服务器检查/SHA256校验升级、数据隔离、首次引导记录、单实例互斥 |
| `Desktop/Workspace` | `BoardController`（各 partial 含 `TaskSwitcher` 前景收起控制、`PinnedAction`）、`FocusLayout`、`ColumnSurface`、`OnboardingSurface`、`ImeWindowFilter`：主控制器、零任务栏窗口约束与前景切离自动收起、待操作列常驻鼠标穿透及同步迷你解除浮窗、六列窗口与专注模式布局、悬浮球、拖卡、快捷键、IME 免疫与外点/全屏输入协调 |
| `Desktop/Editing` | `EditorSurface`、`ReminderEditor`、`StageDetailsEditor`、`StageReorderGesture`、`DraftStackSurface`：新建/编辑窗口、提醒选择器、阶段拖动排序、多草稿堆叠与动画 |
| `Desktop/Binding` | `StageConversationPicker`、`ConversationNavigator`、`ConversationJumpButton`：会话选择/绑定窗、跨应用拖动绑定、一键跳转至原会话 |
| `Desktop/Settings` | `SettingsSurface`、`SettingsHelp`：设置界面（含“外观”深浅色/配色页）、真实截图帮助、平台初始化与连接状态 |
| `Desktop/Codex` | `CodexObserver`、`CodexBehaviorSource`、`SidebarSource`、`LocalCatalog`：Codex 本机 SQLite 目录、后台行为日志与前台只读 UIA 控件观察 |
| `Desktop/AiPlatforms` | `AiPlatformObserver`、`AiPlatformSetup`、`DshConversationTarget`、`ClaudeDesktopCatalog`：DSH 原生 v4 日志监听、Claude Hook 与桌面稳定会话 ID 映射 |
| `Desktop/Notifications` | `NotificationCapture`、`ListeningService`、`Audio`、`SoundLease`：Windows 通知捕获、自定义提示音与原系统声音恢复 |
| `Desktop/Rendering` | `ThemePalette`、`SurfaceWindow`、`LiveBackdrop`、`AdaptiveCardPanel`、`ColorPickerPanel`、`ActionPinIcon`、`OrbMenuRenderer`、`Ui`：全局深浅色/9套多配色实时主题引擎、透明毛玻璃背景、帧动画、虚拟多列卡片布局、原创图钉图标与高对比圆角控件体系 |

修改共享表面（背景、透明度、命中测试、动画、输入）前，请先阅读 [SHARED-CODE.md](architecture/SHARED-CODE.md) 与 [VERIFICATION.md](architecture/VERIFICATION.md)。

## 2. 编译与打包

本机离线 NuGet 缓存位于 `test-output/nuget-packages`，构建时可指定 `-p:RestorePackagesPath` 避免联网依赖：

```powershell
# 编译主程序
dotnet build src/Taskboard.Desktop/Taskboard.Desktop.csproj -p:RestorePackagesPath="$PWD/test-output/nuget-packages"

# 构建独立便携包（输出到新的 release 子目录，绝不覆盖已有目录）
powershell -ExecutionPolicy Bypass -File tools/Build-Portable.ps1 -Output release/Taskboard-new
```

## 3. 测试与检查工具

所有测试必须显式传入**新的隔离目录** `--data-dir`，禁止使用正式数据目录（`%LOCALAPPDATA%\InstantTaskboard`）。

| 工具 / 测试项目 | 用途与运行方式 |
| --- | --- |
| `python tools/check_project.py` | 检查所有生产源码的 `Modules/*.props` 归属完整性，以及是否混入已退役的旧管道/RPC/订阅代码 |
| `tests/Taskboard.Checks` | 核心业务规则、状态流转、归档与持久化测试（离屏）：<br>`dotnet run --project tests/Taskboard.Checks -- --data-dir test-output/checks-run` |
| `tests/Taskboard.Binding.Tests` | 绑定事务与目标解析测试（离屏）：<br>`dotnet run --project tests/Taskboard.Binding.Tests` |
| `tools/CodexPassiveChecks` | Codex 被动观察与行为日志回归（离屏）：`--daily` 或 `--behavior` |
| `tools/AiModulesChecks` | AI 平台配置、目录、恢复与绑定检查（默认离屏；涉及桌面模式需先征得用户同意） |
| `tools/AiListeningChecks` | DSH / Claude 状态聚合与存储检查（离屏） |
| `tools/DesktopVerification` | WPF 离屏/受控组件验证 |
| `tools/PackageAudit` | 检查打包后便携目录的程序集、资源与运行时完整性（离屏只读） |

> **桌面操作红线**：任何会弹出可见窗口、移动鼠标、发送按键、切换前台窗口或读写系统剪贴板的测试（如 `DesktopAcceptance`、`RenderProbe` 或带 `desktop`/`native` 参数的工具），**执行前必须先询问用户“现在是否方便进行桌面操作？”并获得明确同意**。
