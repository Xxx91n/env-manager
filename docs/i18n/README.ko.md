<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../../docs/assets/logo-dark-theme.png">
  <source media="(prefers-color-scheme: light)" srcset="../../docs/assets/logo-light-theme.png">
  <img src="../../docs/assets/logo.png" alt="Env Manager 标志" width="120" height="120">
</picture>
<p align="center">
  <img src="../../docs/assets/brand/hero.svg" alt="Env Manager mini hero" width="100%">
</p>


# Env Manager

现代、轻量的 **Windows 环境变量管理器**——支持 CLI 和 GUI 双模式。灵感来自 Microsoft PowerToys，独立开发，追求速度、简洁与自动化友好。

[![Release](https://img.shields.io/badge/Release-v0.9.26-blue)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **한국어** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---

## 安全

> [!WARNING]
> Env Manager **未经代码签名**。Windows SmartScreen 可能会在首次启动时显示“未知应用”警告。点击“更多信息”然后选择“仍要运行”即可继续。代码签名计划在未来版本中实现。

受保护的环境变量和 PATH 条目在删除前会被禁用，恢复时进行精确的注册表值类型验证。机密值通过各提供者专用机制加密（DPAPI、CredMan、Vault、SOPS、Azure KV、1Password、AWS SM）— 明文绝不持久化到磁盘或日志。命名管道 IPC 使用防劫持标志和输入验证（最大 64 参数，32767 字符上限，拒绝空字节）。漏洞报告请参见 [SECURITY.md](../../SECURITY.md)。

## 目录

- [安全](#安全)
- [背景](#背景)
- [安装](#安装)
- [用法](#用法)
- [功能](#功能)
- [架构](#架构)
- [机密提供者](#机密提供者)
- [服务模式](#服务模式)
- [维护者](#维护者)
- [贡献](#贡献)
- [许可证](#许可证)

## 背景

Windows 内置的环境变量编辑器笨重且易出错。Env Manager 提供了一个现代、快速的替代方案：C# CLI 用于脚本和自动化，原生 Tauri/Svelte GUI 用于交互式编辑。它增加了配置文件继承、PATH 健康诊断、8 种机密提供者后端、Launch 配置文件隔离、独立机密生命周期服务、审计账本和 10 语言国际化的能力 — 超越 PowerToys、RapidEE 等同类工具。

## 安装

### 便携版

从 [GitHub Releases](https://github.com/Xxx91n/env-manager/releases) 下载。解压 ZIP 后直接运行 `env-manager.exe`，无需安装。

### 前置要求

> [!IMPORTANT]
> **便携版**和**仅 CLI** 版本为框架依赖部署：目标机器需要预装 **.NET 10 Desktop Runtime**。
>
> 从 .NET 官方下载页获取对应架构的运行时：<https://dotnet.microsoft.com/download/dotnet/10.0>
>
> | 构建类型 | 架构 | .NET 10 运行时 |
> |---------|------|---------------|
> | 便携版 / 仅 CLI | x64 | .NET 10 Desktop Runtime x64 |
> | 便携版 / 仅 CLI | x86 | .NET 10 Desktop Runtime x86 |
> | 便携版 / 仅 CLI | ARM64 | .NET 10 Desktop Runtime ARM64 |
>
> **MSI 安装包**在安装时自动检测 .NET 10 并提示安装。
>
> **WebView2 运行时**（GUI 所需）已预装于 Windows 11，Windows 10 21H2+ 可从 <https://developer.microsoft.com/microsoft-edge/webview/> 下载。

可选的外部机密提供者工具（SOPS、1Password CLI、Vault CLI、AWS CLI、Azure CLI、PowerShell 7）的下载链接和配置说明，请参阅 [机密提供者指南](../../docs/secret-providers-guide.md)。

### MSI 安装包

运行 `.msi` 文件。自动创建开始菜单快捷方式。支持 x64、x86、ARM64 架构。

### 仅 CLI

下载 CLI-only ZIP 用于无头或脚本场景：`env-manager-cli.exe` 加 `.dll` 文件。无 GUI，不依赖 WebView2。

### winget

> [!NOTE]
> winget 分发已计划但尚未上线。请通过 GitHub Issues 关注更新。

### 从源码构建

```bash
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager
cd frontend && npm ci && cd ..
node scripts/build.mjs --arch x64
```

需要 .NET 10 SDK、Node.js 20+、Rust 稳定版（MSVC 目标）。详见 [docs/build-and-release.md](../../docs/build-and-release.md)。

## 用法

### CLI

```bash
# 列出所有变量
env-manager-cli.exe list

# 获取变量
env-manager-cli.exe get PATH

# 设置变量（默认用户范围）
env-manager-cli.exe set MY_VAR "my_value"
env-manager-cli.exe set JAVA_HOME "D:\jdk17" --scope system

# 删除变量
env-manager-cli.exe delete MY_VAR

# 备份所有变量到 JSON
env-manager-cli.exe backup --output backup.json

# 从备份恢复
env-manager-cli.exe restore backup.json

# PATH 健康检查
env-manager-cli.exe path health

# 创建 Launch 配置文件并以隔离环境启动
env-manager-cli.exe profile create dev --type launch --target python.exe
env-manager-cli.exe profile add-secret dev API_KEY "sk-xxx"
env-manager-cli.exe profile launch dev

# 服务控制
env-manager-cli.exe service status
env-manager-cli.exe service ping

# 状态导出/导入（容灾恢复）
env-manager-cli.exe export-state --output state.dpapi
env-manager-cli.exe import-state --input state.dpapi

# 审计账本
env-manager-cli.exe audit migrate-audit
env-manager-cli.exe audit verify-ledger
```

完整命令参考请见 [docs/cli-commands.md](../../docs/cli-commands.md)。

### GUI

运行 `env-manager.exe`。GUI 提供实时变量列表（搜索、范围筛选、内联编辑）、PATH 编辑器（拖拽排序、健康徽章）、配置文件管理、机密提供者选择、服务控制面板、审计历史查看器和 10 语言国际化。

## 功能

### CLI 模式

- 18+ 命令，完整环境变量管理
- 配置文件支持继承、冲突预览、PATH 片段、安全逆序回滚
- PATH 编辑器：重复项和失效目录诊断
- **Launch 配置文件**：隔离环境块（`env_clear` + 注入），绝不写注册表，绝不广播 `WM_SETTINGCHANGE`
- **PATH 健康检查**：`path health [--fix] [--dry-run]` 检测重复和失效条目
- **机密提供者**：8 种后端，激活预检和内联错误引导
- **备份/恢复**：JSON 备份、diff、合并、验证、审计历史和受保护的撤销
- **批量导入/导出**：`.env`、CSV、JSON，支持 dry-run 冲突预览
- **状态导出/导入**：`export-state`/`import-state` — DPAPI 加密的全状态归档，用于容灾恢复
- **审计账本**：`audit migrate-audit` / `verify-ledger` / `export-survival-kit` / `recover-from-ledger`
- **服务控制**：`service status` / `ping` / `refresh` / `rotate` / `reload` / `shutdown`
- **v0.9.8 工业级日志**：`tracing` + `tracing-appender` 后端，日志轮转，7 天保留，跨进程 `request_id` 用于 CLI/Rust/Service 调试关联
- **v0.9.9 Schema 迁移**：`SchemaMigration.cs` 注册表式顺序迁移框架（profiles v0->v1->v2）
- **v0.9.10 审计账本**：`AuditLedgerMigration.cs` 实现 `audit migrate-audit`（audit.json 转 `audit-ledger.jsonl` 哈希链）、`verify-ledger`（SHA256 链防篡改）、`export-survival-kit`、`recover-from-ledger`
- **v0.9.11 对齐**：PS5/pwsh 兼容性审计（BOM 安全编码、深度安全 JSON），GitHub Actions `workflow_dispatch` 手动发布门控（含 `create_release` + 版本输入），多架构构建（x64/x86/arm64），按架构独立清理
- **v0.9.13 安全硬化**：进程安全与生命周期加固 — Rust zeroize/secrecy（cert_bootstrap、reveal-secret stdout），C# SecretString（作用域退出时清零），命名管道 DACL（仅 BA/SY/OW），二进制哈希自校验 + 调试器检测 + WER/崩溃转储禁用 + DLL 注入枚举 + VirtualLock（服务），审计文件 NTFS ACL，audit.json AES-256-GCM 静态加密（EncryptAuditContent/DecryptAuditContent 已接入读写），export-state 双层加密（AES-GCM 载荷 + DPAPI 包裹 DEK + HMAC-SHA256 完整性，v1 向后兼容），reconcile 循环 TOCTOU mutex，provider 二进制哈希校验（sops/op 首次使用 SHA256 记录）
- **v0.9.12 最终对齐**：敏感数据脱敏（22 模式统一脱敏，覆盖 CLI/GUI/服务三层），export-state/import-state 全状态 DPAPI 加密备份，.NET 10 运行时检测 + i18n 缺失键修复，GitHub 发布就绪 Phase 1（社区文件、README 重写、CSP 加固）
- **v0.9.14 输入对话框 + 便利贴 + Material 3 标签栏**：`InputDialog.svelte` 全局替换 `window.prompt()`（重命名、添加变量/配置文件输入）。变量便利贴注释存储于 `var-notes.json`。Material 3 ARIA 标签栏 + roving tabindex。PATH scope-aware apply/unapply 修复（配置文件启用时 PATH 条目未生效的根因）。编译先于打包顺序强制执行 — 源码修改后 `build.mjs` 不得使用 skip 标志。
- **v0.9.16 ProfileShow 源感知 + path scope 徽章 + ServicePage i18n**：`profile show` 输出包含每个变量/路径的源配置文件名。ProfilePage 显示"来自 {profile}"徽章。ServicePage 全部 10 语言本地化。IPC `mount_id` 参数在 Rust/C# 间统一 snake_case。
- **v0.9.17 PathEditor 反向索引 + ProfilePage pathScopes 徽章**：`PathEditor.svelte` 使用反向索引查找进行移动操作。ProfilePage 为 PATH 条目显示 per-entry scope 徽章（用户/系统）。IPC `mount_id` 字段完全 snake_case 对齐。
- **v0.9.18 服务轮换 + WebView2 加固**：`reconcile.rs` 中 `call_cli_rotate()` 设置 `CREATE_NO_WINDOW` 标志防止控制台窗口闪现。`ServicePage.svelte` 使用 `toLocaleString()` 格式化时间戳。WebView2 原生右键菜单完全屏蔽（`oncontextmenu preventDefault` + CSS `user-select: none`，clash-verge-rev 模式）。
- **v0.9.19 SWR 缓存 + 预加载**：所有 5 个读 API（listVariablesRaw、listPathEntries、listHistory、listProtection、listProfiles）使用 stale-while-revalidate 模式，TTL 15 秒。`listProfiles` 添加缓存（此前完全无缓存，为最大延迟源）。ProtectionPage 4 路并发 IPC。启动时预加载相邻页面缓存。
- **v0.9.20 UI 设计系统 — 双轴主题令牌**：6 色主题系统（slate/blue/violet/rose/cyan/amber），使用 `data-theme` + `data-theme-style` 属性选择器。移除所有 `dark:` Tailwind 前缀；暗色模式由 `[data-theme="dark"]` 驱动。所有内联 SVG 替换为 `lucide-svelte`。作用域 CSS 过渡（`.theme-changing` 类）。WebView2 右键菜单屏蔽。Tab 指示器宽度 0px 初始状态修复。
- **v0.9.20 轻量模式 + 托盘修复**：`CheckMenuItem.set_enabled(true)` 在 `set_text` 后调用以修复 muda HMENU 重建 bug。`close` = 最小化到托盘；退出仅通过托盘。每次 `update_lightweight_check` 同步勾选状态。
- **v0.9.21 性能 Phase 2 — 进程生命周期 + 懒加载 + 遥测**：6 个 tab 从静态 import 改为动态 `import()` + Vite 代码分割（懒加载 `loadComponent`/`preloadComponent` 已激活，不再是死代码）。进程泄露防护 + IPC 连接计数器 + 排水验证。静默性能遥测（视图切换计时 + `run_cli` 耗时）。机密提供者超时 + 长会话内存安全。标题栏按钮工具提示 + 清理无用 i18n 键。主题样式 allow-list 修复（此前过期，重启时阻止非 slate 主题）。
- 用户和系统范围支持，用户范围无需管理员权限

### GUI 模式

- 基于 Tauri 2.0 的原生桌面应用（WebView2）
- 实时变量列表：高亮搜索、范围筛选、`%VAR%` 展开预览
- PATH 编辑器：暂存移动（Apply 按钮）、健康徽章、批量删除失效条目
- 配置文件管理：Global 和 Launch 类型、继承、机密变量、提供者选择器带内联错误横幅
- 机密提供者选择器：激活预检，内联琥珀色错误横幅
- 服务管理面板（Ping/Reload/Shutdown + 挂载健康列表）
- 审计历史查看器：全命令级操作标签
- 设置：暗色模式、字体缩放、CLI 加入 PATH 开关、DR 导出/导入、i18n 语言
- Edge 风格悬浮滚动条（浮于内容之上，零布局空间）
- 10 语言国际化：English, 简体中文, 日本語, 한국어, Deutsch, Français, Español, Português, Русский, العربية

## 架构

四层架构：

1. **CLI 后端**（`Program.cs`）— C# .NET 10 控制台应用，直接读写 Windows 注册表，编译为 `env-manager-cli.exe`
2. **Tauri 外壳**（`frontend/src-tauri/`）— Rust 应用，内嵌 CLI 为打包资源，生成 CLI 子进程，通过 Tauri IPC 返回 JSON
3. **Svelte 前端**（`frontend/src/`）— TypeScript + Svelte 4 + TailwindCSS，运行于 WebView2，仅通过 `invoke('run_cli', ...)` 调用 Rust
4. **服务 crate**（`service/`）— Rust 独立二进制（`env-manager-service.exe`），通过命名管道 IPC 管理机密挂载生命周期

详见 [docs/architecture.md](../../docs/architecture.md)。

## 机密提供者

8 种提供者后端，均带激活预检 — 失败时在配置文件编辑器中直接显示内联琥珀色横幅：

| 提供者 | 认证方式 | 定期刷新 | 文档 |
|---|---|---|---|
| DPAPI CurrentUser | Windows DPAPI | 否（用户绑定） | [指南](../../docs/secret-providers-guide.md) |
| Windows Credential Manager | CredMan + DPAPI | 否（用户绑定） | [指南](../../docs/secret-providers-guide.md) |
| PowerShell SecretManagement | SecretStore 保险库 | 尽力而为 | [指南](../../docs/secret-providers-guide.md) |
| HashiCorp Vault KV v2 | VAULT_TOKEN / AppRole 证书 | 是 | [指南](../../docs/secret-providers-guide.md) |
| SOPS | Age / PGP / KMS | 是 | [指南](../../docs/secret-providers-guide.md) |
| Azure Key Vault | SP 证书 / 托管标识 | 是 | [指南](../../docs/secret-providers-guide.md) |
| 1Password CLI | OP_SERVICE_ACCOUNT_TOKEN | 是 | [指南](../../docs/secret-providers-guide.md) |
| AWS Secrets Manager | SigV4 + 访问密钥 | 是 | [指南](../../docs/secret-providers-guide.md) |

详见 [docs/secret-providers-guide.md](../../docs/secret-providers-guide.md)。

## 服务模式

`env-manager-service.exe` 是独立 Rust 二进制，通过命名管道 IPC 管理机密挂载生命周期：

- **运行模式**：Service（SCM 管理，开机启动）、Background（用户启动）、Cli（一次性网关）
- **协调循环**：300s 周期全量扫描，幂等逐项处理，30s 首次延迟
- **证书引导**（Phase D）：Vault AppRole 和 Azure SP 证书认证，消除长期令牌
- **审计账本**（Phase E）：追加式哈希链 `audit-ledger.jsonl`，100MB 轮转，防篡改
- **IPC**：防劫持管道标志，65536 字节请求上限，换行分隔 JSON 协议
- **v0.9.6 看门狗**：双层恢复 — SCM 自动重启（Service 模式）+ GUI 30s ping 看门狗（Background 模式）
- **v0.9.7 快速失败**：服务探测 2s 快速失败（之前 18s）

详见 [docs/secret-architecture-blueprint.md](../../docs/secret-architecture-blueprint.md) 和 [docs/secret-architecture-decision-summary.md](../../docs/secret-architecture-decision-summary.md)。

## 维护者

[@Xxx91n](https://github.com/Xxx91n)

## 贡献

欢迎贡献！请参阅 [CONTRIBUTING.md](../../.github/CONTRIBUTING.md) 了解开发设置、测试和 PR 流程。Bug 报告和功能请求请使用 [Issue 模板](https://github.com/Xxx91n/env-manager/issues)。安全报告请参见 [SECURITY.md](../../SECURITY.md)。

## 许可证

Apache-2.0 — 详见 [LICENSE](../../LICENSE)。