---
name: dev-and-release-custody
description: >-
  自动托管开发、代码模块归属校验、现行文档同步与新版本便携包发布归档流水线。
  当在项目中开发新需求、修复 Bug、更新模块结构或打包新便携版本时自动启用本技能。
---

# 全托管开发与版本归档流水线

本技能确保每次开发与发版都自动维护好代码模块索引、需求规则文档与版本状态日志，**无需用户手动提醒**。

## 1. 动手前的准备

1. 阅读 [docs/HANDOFF.md](../../docs/HANDOFF.md)、[docs/STATUS.md](../../docs/STATUS.md) 与 [docs/BACKLOG.md](../../docs/BACKLOG.md)。
2. 在 [docs/current/README.md](../../docs/current/README.md) 中定位目标功能所属模块文档（如 `M01`–`M12`、`C01`–`C03`、`SURFACE`），按需阅读对应规则。
3. 若涉及毛玻璃、透明度、命中测试、动画或焦点/窗口层次修改，必读 [docs/architecture/SHARED-CODE.md](../../docs/architecture/SHARED-CODE.md) 与 [docs/architecture/VERIFICATION.md](../../docs/architecture/VERIFICATION.md)。
4. **先方案后代码原则**：凡是在 `docs/BACKLOG.md` 中标记为“需先给方案/排查原因供用户选择”的条目，先用通俗中文向用户列出原因与备选方案，收到用户拍板后再修改代码。

## 2. 开发与自测规范

1. **严禁触碰正式环境**：不得读写 `%LOCALAPPDATA%\InstantTaskboard`，所有测试必须传入 `test-output/` 下的新 `--data-dir`。
2. **桌面操作先询问**：任何会弹出可见窗口、移动鼠标、发送按键、切换前台窗口或读写剪贴板的验证，必须先问用户“现在是否方便进行桌面操作？”，获明确同意后才可运行。
3. **模块归属同步**：新增或删除 `.cs` 文件时，同步更新 `src/Taskboard.Core/Modules/*.props` 或 `src/Taskboard.Desktop/Modules/*.props`，并运行：
   ```powershell
   python tools/check_project.py
   ```

## 3. 完成修改或打包后的自动归档清单（必做）

每次完成功能开发或生成新便携包（`tools/Build-Portable.ps1 -Output release/Taskboard-<version>`）后，在回复用户前**自动**执行以下更新：

1. **更新模块规则**：若交互或业务规则发生变更，原位更新 `docs/current/` 下对应的模块文档。
2. **更新架构字典**：若新增/移动了源码文件或模块职责，更新 [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md)。
3. **更新待办总表**：在 [docs/BACKLOG.md](../../docs/BACKLOG.md) 中将已解决的条目状态勾选为已完成，并标注完成版本号。
4. **更新版本与交接索引**（打包新版本时）：
   - 在 [CHANGELOG.md](../../CHANGELOG.md) 顶部新增本次版本的通俗改动说明与验证证据路径；
   - 更新 [docs/STATUS.md](../../docs/STATUS.md) 与 [docs/HANDOFF.md](../../docs/HANDOFF.md) 中的当前版本号与最新构建包路径。
