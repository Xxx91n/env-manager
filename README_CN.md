# Env Manager

现代、轻量级的 Windows 环境变量管理器，同时支持 CLI 命令行和 GUI 图形界面。受 Microsoft PowerToys 启发，独立构建，追求速度与简洁。

**[简体中文](README_CN.md)** | **[English](README.md)**

**支持的 GUI 语言**（在设置下拉框中切换，重启后保留）：简体中文、English、日本語、한국어、Deutsch、Français、Español、Português、Русский、العربية。

---

## 功能特性

### CLI 命令行模式
- 18 个命令，完全掌控环境变量管理
- 支持多配置同时启用、配置继承、冲突预览、PATH 片段和安全的逆序恢复
- PATH 编辑器：支持添加、删除、排序、重复项和失效目录检测
- **启动型配置文件**：GUI 与 CLI 都可在一次事务中直接创建 Global 或 Launch 配置。Launch 以隔离环境块（`env_clear` + 注入）启动指定程序，永不写注册表、永不广播 `WM_SETTINGCHANGE`。所有配置文件名称全局唯一，避免按名称调用 CLI 时产生歧义。
- **v0.6.0 PATH 健康检测**：`path health [--fix] [--dry-run]` 一次性检测重复 AND 失效（不存在的）PATH 条目；`--fix` 仅删非保护项，受保护项永远保留。
- **v0.7.0 DPAPI 密钥**：`profile add-secret`/`edit-secret`/`remove-secret`/`reveal-secret` —— 为配置文件中的变量值加密（Windows DPAPI 当前用户）。明文仅驻存在进程内存；`profile launch` 启动时解子进程注入；`reveal-secret` 是唯一输出明文到 stdout 的路径。审计仅记录名，绝不记录值。
- **机密提供者**：8 个后端（DPAPI 当前用户、Windows 凭据管理器、PowerShell SecretManagement、HashiCorp Vault KV v2、SOPS、Azure Key Vault、1Password CLI、AWS Secrets Manager）。各提供者的前置条件、一次性配置、激活错误信息与精确的修复步骤见 [docs/secret-providers-guide.md](docs/secret-providers-guide.md)。提供者激活错误会在配置文件编辑器中提供者选择器下方以内联琥珀色横幅直接显示。
- **v0.7.0 GUI**：PATH 健康徽章（healthy/dead/duplicate/duplicate+dead）+ 一键移除失效项；Launch 配置文件类型徽章 + 启动按钮 + 创建栏类型选择 + 原生文件选择器；变量搜索高亮 + `%VAR%` 展开预览；设置中的 `.env`/CSV 批量导入导出（原生文件选择器）。
- **v0.8.0 SecretMount schema v2**：机密变量引用独立的 `secretMount.json` 文件中的 `SecretMount` 条目。原子写入顺序（先 mount 后 profile）配合 fsync 消除撕裂写入损坏。一次性迁移从内联信封到 mount 引用。
- **v0.9.11 env-manager-service**：独立 Rust 服务二进制（`env-manager-service.exe`），通过命名管道 IPC 管理机密 mount 生命周期。RuntimeMode（Service/Background/Cli）由 `--mode` argv 解析。调和循环（300秒周期扫描），幂等单项处理器。防 squatting 管道标志防止管道劫持。GUI 设置中的服务控制面板（Ping/Reload/Shutdown + mount 健康列表）。
- **v0.9.11 Phase D 证书引导**：Vault AppRole 和 Azure SP 证书认证，消除长期令牌（`VAULT_TOKEN`、`AZURE_CLIENT_SECRET`）。短期令牌仅缓存在内存中。
- **v1.0.0 Phase E 审计账本**：追加式哈希链审计账本（`audit-ledger.jsonl`），100MB 轮转，篡改检测，DPAPI 加密的生存套件导出。迁移脚本将旧 `audit.json` 转换为账本格式。
- 用户和系统作用域支持
- JSON 备份与恢复、差异对比、合并、变更历史和安全撤销
- 支持 `.env`、CSV、JSON 批量导入导出及冲突预览
- 用户作用域无需管理员权限
- 单一 158KB 可执行文件，无运行时依赖

### GUI 图形界面
- 基于 Tauri 2.0 的原生桌面应用 (WebView2)
- 实时变量列表，支持搜索高亮、作用域筛选和 `%VAR%` 展开预览
- 内联增删改查，删除前确认
- 界面内备份与恢复
- 10 种语言国际化：英语、中文、日语、韩语、德语、法语、西班牙语、葡萄牙语、俄语、阿拉伯语

---

## 快速开始

### 下载

从 [GitHub Releases](https://github.com/Xxx91n/env-manager/releases) 获取最新版本。

- **便携版**：解压 ZIP 后直接运行 `env-manager.exe`，无需安装。
- **MSI 安装包**：运行 `.msi` 文件，自动创建开始菜单快捷方式。

### CLI 使用

```bash
# 列出所有变量
env-manager-cli.exe list

# 获取变量值
env-manager-cli.exe get PATH

# 设置变量（默认用户作用域）
env-manager-cli.exe set MY_VAR "my_value"
env-manager-cli.exe set JAVA_HOME "D:\jdk17" --scope system

# 删除变量
env-manager-cli.exe delete MY_VAR

# 备份所有变量到 JSON
env-manager-cli.exe backup --output backup.json

# 从备份恢复
env-manager-cli.exe restore backup.json

# 比对两个备份
env-manager-cli.exe diff old.json new.json

# 合并两个备份
env-manager-cli.exe merge old.json new.json --output merged.json

# 验证备份文件
env-manager-cli.exe validate backup.json
```

```bash
# 配置文件管理
env-manager-cli.exe profile list
env-manager-cli.exe profile create dev-profile
# 为单个程序创建隔离环境配置
env-manager-cli.exe profile create tool-run --type launch --target "C:\Tools\tool.exe"
env-manager-cli.exe profile add-var dev-profile JAVA_HOME "D:\jdk17"
# 作用域默认为用户；--scope system 在 apply 时写入系统 HKLM
env-manager-cli.exe profile add-path dev-profile "C:\Tools\bin" --scope user
env-manager-cli.exe profile apply dev-profile
env-manager-cli.exe profile unapply dev-profile
env-manager-cli.exe profile delete dev-profile

# PATH 编辑器
env-manager-cli.exe path list --scope user
env-manager-cli.exe path add "C:\MyTools\bin" --scope user
env-manager-cli.exe path move-up 2 --scope user
env-manager-cli.exe path remove "C:\OldTools\bin" --scope user

# 保护名单管理（锁定变量 / PATH 条目以禁止修改）
env-manager-cli.exe protection list
env-manager-cli.exe protection add-var JAVA_HOME
env-manager-cli.exe protection remove-var JAVA_HOME
env-manager-cli.exe protection add-path "C:\MyTools\bin"
env-manager-cli.exe protection remove-path "C:\MyTools\bin"
```

### GUI 使用

从便携版目录或开始菜单启动 `env-manager.exe`。GUI 通过 Tauri IPC 调用 CLI 后端，两种模式始终操作同一状态。

---

## 安装

### 从发布版本安装

1. 前往 [Releases](https://github.com/Xxx91n/env-manager/releases)
2. 下载便携版 ZIP 或 MSI 安装包
3. 便携版：解压后运行 `env-manager.exe`
4. MSI：运行安装程序，然后从开始菜单启动

### 从源代码编译

**前提条件**：.NET 10 SDK、Node.js 18+、Rust 工具链（GNU 或 MSVC 均可）

```bash
# 编译 CLI
dotnet build -c Release
# 输出：bin/Release/net10.0-windows/env-manager-cli.exe

# 编译 GUI（开发模式，热重载）
cd frontend
npm install
npm run tauri-dev

# 一键构建所有发行版
node scripts/build.mjs --arch x64
# 输出：
#   release/portable/  - GUI + CLI 平铺目录，可直接运行
#   release/cli-only/  - 仅 CLI 包（无 GUI）
#   release/msi/       - Windows MSI 安装包
```

---

## 命令参考

| 命令 | 用法 | 说明 |
|------|------|------|
| `list` | `list` | 列出所有变量（用户和系统） |
| `get` | `get <name>` | 获取变量值 |
| `set` | `set <name> <value> [--scope user\|system]` | 创建或更新变量（默认：user） |
| `delete` | `delete <name> [--scope user\|system]` | 删除变量（默认：user） |
| `backup` | `backup [--output <file>]` | 导出所有变量到 JSON |
| `restore` | `restore <file> [--scope user\|system]` | 从 JSON 导入变量 |
| `diff` | `diff <old> <new>` | 比较两个备份文件 |
| `merge` | `merge <old> <new> --output <file>` | 合并两个备份文件 |
| `validate` | `validate <file>` | 验证备份文件格式 |
| `help` | `help` | 显示帮助 |
| `rename` | `rename <old> <new> [--scope] [--overwrite]` | 原子重命名变量 |
| `history` | `history list [--limit N]` / `history undo <id>` | 查看或撤销审计变更 |
| `bulk` | `bulk import\|export <file> [--scope]` | 导入导出 JSON、.env 或 CSV |
| `expand` | `expand <value>` | 递归展开 `%VARIABLE%` 引用 |
| `profile preview` | `profile preview <name>` | 预览冲突和 PATH 影响 |
| `profile set-inherits` | `profile set-inherits <name> [parent ...]` | 设置无环配置继承 |
| `profile add-path` | `profile add-path <name> <dir>` | 向配置添加 PATH 片段 |
| `profile list` | `profile list` | 列出所有配置文件 |
| `profile create` | `profile create <name> [--type global|launch] [--target <exe>]` | 原子创建全局或隔离启动配置文件 |
| `profile apply` | `profile apply <name>` | 应用配置文件（备份现有变量） |
| `profile unapply` | `profile unapply <name>` | 取消应用（恢复原始变量） |
| `profile add-var` | `profile add-var <profile> <name> <val>` | 向配置文件添加变量 |
| `profile add-path` | `profile add-path <profile> <dir> [--scope user\|system]` | 向配置文件添加 PATH 条目 |
| `path list` | `path list [--scope]` | 列出 PATH 条目 |
| `path add` | `path add <dir> [--scope]` | 添加目录到 PATH |
| `path remove` | `path remove <dir> [--scope]` | 从 PATH 移除目录 |
| `path move-up` | `path move-up <index> [--scope]` | 上移 PATH 条目 |
| `path move-down` | `path move-down <index> [--scope]` | 下移 PATH 条目 |

### 作用域

- `user`：`HKEY_CURRENT_USER\Environment`（无需提权）
- `system`：`HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`（需要管理员权限）

---

## 备份文件格式

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32;...",
      "scope": "user"
    },
    {
      "name": "JAVA_HOME",
      "value": "D:\\jdk17",
      "scope": "system"
    }
  ]
}
```

---

## 系统要求

- Windows 10 21H2 或更高版本（推荐 Windows 11）
- CLI 独立运行：.NET Runtime 10.0+
- GUI：WebView2 运行时（Windows 11 预装，Windows 10 可下载）

---

## 技术栈

**后端**：C# .NET 10、Spectre.Console、Microsoft.Win32.Registry
**前端**：Tauri 2.0、Svelte 4、TypeScript 5、TailwindCSS 3、Vite 5
**原生层**：Rust、serde、tokio、tauri-plugin-log

---

## 项目结构

```
env-manager/
├── Program.cs                    # CLI 实现（C#）
├── env-manager.csproj            # .NET 10 项目（AssemblyName: env-manager-cli）
├── frontend/                     # Tauri GUI 应用
│   ├── src/                      # Svelte 前端
│   │   ├── App.svelte            # 根组件
│   │   ├── lib/
│   │   │   ├── api.ts            # Tauri IPC 桥接
│   │   │   ├── stores.ts         # Svelte 状态管理
│   │   │   ├── i18n.ts           # 国际化配置
│   │   │   ├── components/       # UI 组件
│   │   │   └── translations/     # 10 种语言文件
│   ├── src-tauri/                # Rust 后端
│   │   ├── src/main.rs           # Tauri 命令处理
│   │   ├── tauri.conf.json       # 打包配置
│   │   └── Cargo.toml            # Rust 依赖
│   └── scripts/
│       ├── prebuild.mjs          # 编译 CLI 并复制到 src-tauri/bin/
│       └── build.mjs             # 统一构建脚本（跨平台，--arch x64|x86|arm64）
├── release/                      # 构建产物（已 gitignore）
│   ├── portable/                 # GUI + CLI 便携版
│   ├── cli-only/                 # 仅 CLI 包
│   └── msi/                      # MSI 安装包
├── AGENTS.md                     # 项目规范
└── LICENSE                       # Apache-2.0
```

---

## 安全性

- 不存储凭证，仅管理环境变量
- 直接通过 `Microsoft.Win32.Registry` 访问注册表，不走 COM
- IPC 隔离，CLI 作为独立子进程由 GUI 调起
- 输入验证，变量名和值限制 32767 字节
- 用户和系统作用域权限分离

---

## 常见问题

**如何管理系统变量？**

使用 `--scope system` 标志，需要管理员权限：

```bash
env-manager-cli.exe set SYSTEM_VAR "value" --scope system
```

**能否备份和恢复？**

可以。JSON 格式人类可读、可移植：

```bash
env-manager-cli.exe backup --output my-backup.json
env-manager-cli.exe restore my-backup.json
```

**GUI 需要本地 Web 服务器吗？**

不需要。生产环境下，Tauri 将前端作为静态资源通过 `tauri://` 自定义协议嵌入，不依赖 localhost 服务器，也不需要网络。开发模式下，Vite 在 `localhost:5173` 提供热重载。

**如何添加新语言？**

在 `frontend/src/lib/translations/` 中添加 JSON 文件，在 `frontend/src/lib/i18n.ts` 中注册。详见 AGENTS.md 的 i18n 工作流。

---

## 故障排除

| 问题 | 解决方案 |
|------|--------|
| 系统作用域"拒绝访问" | 以管理员身份运行 |
| 变量未立即显示 | 重启应用 |
| GUI 空白 | 确认已安装 WebView2 |
| 备份文件无效 | 运行 `validate` 命令或检查 JSON 格式 |

---

## 开发

详见 [AGENTS.md](AGENTS.md) 了解完整的项目规范，包括架构设计、编码标准、构建系统、i18n 规则和发布流程。

### 快速配置

```bash
dotnet build -c Release         # 编译 CLI
cd frontend && npm install      # 安装 GUI 依赖
npm run tauri-dev               # 启动 GUI 热重载
```

---

## 许可证

Apache-2.0 - 可自由用于个人和商业项目。详见 [LICENSE](LICENSE)。

---

### v0.9.11

- **SecretMount schema v2**：机密变量引用独立的 `secretMount.json` 文件，原子写入顺序（先 mount 后 profile）配合 fsync。一次性迁移。
- **env-manager-service**：独立 Rust 服务二进制，命名管道 IPC 管理机密 mount 生命周期。RuntimeMode（Service/Background/Cli），调和循环（300秒周期扫描），防 squatting 管道标志。GUI 服务控制面板。
- **Phase D 证书引导**：Vault AppRole 和 Azure SP 证书认证，消除长期令牌。
- **Phase E 审计账本**：追加式哈希链账本，轮转，篡改检测，DPAPI 加密生存套件导出。
- **MSI 安装包**：WiX ServiceInstall 注册 env-manager-service，ProgramData 目录存储机器级机密 mount 文件。
- **构建流水线**：跨平台 `scripts/build.mjs` 编排器，多架构支持（x64/x86/arm64），CLI 版本验证防止陈旧二进制部署。
- **安全**：audit encrypt-file 路径验证（50MB 上限 + 系统目录阻止），audit 命令纳入 Rust ALLOWED_COMMANDS，audit list/encrypt-file 读写分类。

### v0.7.1

- 修复 Windows argv 解析器的一个隐患:以反斜杠结尾的带引号 PATH 值(例如 `"C:\Program Files\PowerShell\7\"`)会把后面的 `--scope` 参数吞进值里。CLI 现在会在启动时检测该特征并惰性重新分词,GUI/Tauri 路径传入的干净 argv 不会被改动。
- 新增会话级主机环境快照脚本 `scripts/snapshot-host-env.ps1`,并将实机冒烟测试升级为精确的注册表/内部配置快照与回滚验证,防止测试残留静默修改既有变量。
- Rust 与前端诊断日志不再持久化 CLI 输出；启动错误页仅显示安全的通用状态；注册表写入会在成功前验证并在失败时回滚；GitHub Actions 已固定到不可变提交 SHA。

## 贡献

开源项目。如有问题、功能需求或拉取请求，请前往 [GitHub 仓库](https://github.com/Xxx91n/env-manager)。

---

**版本**：0.9.11 | **许可**：Apache-2.0 | **状态**：积极开发中


### 安全与性能

受保护变量和 PATH 条目会在 GUI 中直接灰化，CLI 仍以同一规则做最终拒绝。关闭变量会保留原始注册表值类型，并且只有在精确校验恢复成功后才删除备份。GUI 使用有界的 5 秒缓存、代际失效和 single-flight IPC 读取，减少大规模环境变量场景中的重复进程，同时防止旧读取结果覆盖新状态。
