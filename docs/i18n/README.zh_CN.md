<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../../docs/assets/logo-dark-theme.png">
  <source media="(prefers-color-scheme: light)" srcset="../../docs/assets/logo-light-theme.png">
  <img src="../../docs/assets/logo.png" alt="Env Manager 标志" width="120" height="120">
</picture>
<p align="center">
  <img src="../../docs/assets/brand/hero.gif" alt="Env Manager mini hero" width="100%">
</p>


# Env Manager

现代、轻量的 **Windows 环境变量管理器**——支持 CLI 和 GUI 双模式。灵感来自 Microsoft PowerToys，独立开发，追求速度、简洁与自动化友好。

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**语言:** [English](../../README.md) · **简体中文** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## 演示

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI 演示：agents、path health 和 get 命令" width="100%">
</p>

演示展示了只读 CLI 命令的实际效果：`agents --summary`、`path health`、`get PATH` 和 `agents --json`。使用 `vhs docs/assets/demo.tape` 重新生成。


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


## AI Agent 专区

Env Manager 专为 LLM Agent 设计，不仅面向人类：

```bash
env-manager-cli agents            # 打印内嵌的 agent 手册
env-manager-cli agents --path     # 手册文件路径（AGENTS.cli.md）
env-manager-cli agents --summary  # 单行机器可读规格
env-manager-cli agents --json     # 完整命令表结构化 JSON
```

- [AGENTS.md](../../AGENTS.md) — 仓库级 agent 指南（架构、硬边界、测试）。
- [AGENTS.cli.md](../../AGENTS.cli.md) — 随 CLI 二进制分发，任何 agent 可在运行时发现契约。
- **能力范围限定的 agent 接口** — `secret-providers.json` 上的 `agentCapabilities` 白名单让部署方可以拒绝 agent 的并行 set/delete 调用。


## 安全

> [!WARNING]
> Env Manager **未经代码签名**。Windows SmartScreen 可能会在首次启动时显示“未知应用”警告。点击“更多信息”然后选择“仍要运行”即可继续。代码签名计划在未来版本中实现。

受保护的环境变量和 PATH 条目在删除前会被禁用，恢复时进行精确的注册表值类型验证。机密值通过各提供者专用机制加密（DPAPI、CredMan、Vault、SOPS、Azure KV、1Password、AWS SM）— 明文绝不持久化到磁盘或日志。命名管道 IPC 使用防劫持标志和输入验证（最大 64 参数，32767 字符上限，拒绝空字节）。漏洞报告请参见 [SECURITY.md](../../SECURITY.md)。


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

> 启动目标位于 Windows 系统目录（System32）内时会在 profile 保存/启动时被拒绝，以防止 system32 劫持。

完整命令参考请见 [docs/cli-commands.md](../../docs/cli-commands.md)。

### GUI

运行 `env-manager.exe`。GUI 提供实时变量列表（搜索、范围筛选、内联编辑）、PATH 编辑器（拖拽排序、健康徽章）、配置文件管理、机密提供者选择、服务控制面板、审计历史查看器和 10 语言国际化。


## 架构

四层架构：

1. **CLI 后端**（`src/`）— C# .NET 10 控制台应用，直接读写 Windows 注册表，编译为 `env-manager-cli.exe`；`src/Program.cs` 为薄分发入口（<400 行），各命令域（profile/path/service/audit/agents/update 等）各自独立模块文件
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

详见 [docs/secret-architecture-blueprint.md](../../docs/secret-architecture-blueprint.md) 和 [docs/secret-architecture-decision-summary.md](../../docs/secret-architecture-decision-summary.md)。


## 文档

| 文档 | 内容 |
|---|---|
| [CHANGELOG.md](../../CHANGELOG.md) | 版本历史（keepachangelog 格式） |
| [docs/cli-commands.md](../cli-commands.md) | 完整 CLI 命令参考 |
| [docs/architecture.md](../architecture.md) | 深度架构 |
| [docs/backup-and-profiles.md](../backup-and-profiles.md) | 备份、还原、配置文件语义 |
| [docs/secret-providers-guide.md](../secret-providers-guide.md) | 机密提供者设置 |
| [docs/build-and-release.md](../build-and-release.md) | 构建和发布流程 |
| [docs/adr/](../adr/) | 架构决策记录 |
| [AGENTS.md](../../AGENTS.md) / [AGENTS.cli.md](../../AGENTS.cli.md) | Agent 指南 |


## 维护者

[@Xxx91n](https://github.com/Xxx91n)


## 贡献

欢迎贡献！请参阅 [CONTRIBUTING.md](../../.github/CONTRIBUTING.md) 了解开发设置、测试和 PR 流程。Bug 报告和功能请求请使用 [Issue 模板](https://github.com/Xxx91n/env-manager/issues)。安全报告请参见 [SECURITY.md](../../SECURITY.md)。


## 许可证

Apache-2.0 — 详见 [LICENSE](../../LICENSE)。
