# Env Manager

现代、轻量级的 Windows 环境变量管理工具，同时支持 CLI 和 GUI 界面。

**[简体中文](README_CN.md)** | **[English](README.md)**

## 快速开始

```bash
# 下载
# 来自: https://github.com/Xxx91n/env-manager/releases

# 列出所有变量
env-manager.exe list

# 获取变量值
env-manager.exe get PATH

# 设置变量
env-manager.exe set MY_VAR "my_value"

# 删除变量
env-manager.exe delete MY_VAR

# 备份所有变量
env-manager.exe backup --output backup.json

# 从备份恢复
env-manager.exe restore backup.json

# 比对两个备份
env-manager.exe diff old.json new.json

# 合并两个备份
env-manager.exe merge old.json new.json --output merged.json
```

## 功能特性

### CLI 命令行模式
- **9 个命令**，完全掌控环境变量管理
- **用户/系统作用域** - 同时管理当前用户和系统级环境变量
- **备份/恢复** - 导出和导入为 JSON 文件
- **对比/合并** - 比较和合并备份文件
- **验证** - 检查备份文件格式有效性
- **无需管理员** - 用户作用域无需提权（系统作用域需要）

### GUI 图形界面
- **现代 Tauri 桌面应用** - 轻量级(40MB)、快速响应
- **实时变量列表** - 一览所有环境变量
- **搜索/筛选** - 按名称或值快速查找
- **作用域切换** - 用户/系统作用域一键切换
- **增删改查** - 直接在 GUI 中管理变量
- **备份/恢复界面** - 通过界面导出导入备份

## 安装

### 从发布版本安装（推荐）

1. 下载 `env-manager-v0.3.0.exe`(CLI) 或 `env-manager-v0.3.0.msi`(GUI 安装程序)
2. 运行或直接执行
3. CLI: 复制到 PATH 路径中的目录，或随处运行
4. GUI: MSI 安装程序会创建开始菜单快捷方式

### 从源代码编译

```bash
# 需要: .NET 10 SDK、Node.js 20+、Rust stable

# 编译 CLI
dotnet build -c Release
# 输出: bin/Release/net10.0/env-manager.exe

# 编译 GUI
cd frontend
npm install
npm run tauri-build
# 输出: dist/(网页资源) + MSI 安装程序
```

## 命令参考

| 命令 | 用法 | 说明 |
|------|------|------|
| `list` | `env-manager list` | 列出所有变量 |
| `get` | `env-manager get NAME` | 获取变量值 |
| `set` | `env-manager set NAME VALUE [--scope user\|system]` | 创建/更新变量（默认: user） |
| `delete` | `env-manager delete NAME [--scope user\|system]` | 删除变量（默认: user） |
| `backup` | `env-manager backup [--output FILE]` | 导出所有变量到 JSON |
| `restore` | `env-manager restore FILE [--scope user\|system]` | 从 JSON 备份导入 |
| `diff` | `env-manager diff OLD NEW` | 比对两个备份文件 |
| `merge` | `env-manager merge OLD NEW --output FILE` | 合并两个备份文件 |
| `validate` | `env-manager validate FILE` | 验证备份格式 |
| `help` | `env-manager help` | 显示帮助 |

## GUI 使用方式

多种方式打开 GUI:

1. **开始菜单** - MSI 安装后，搜索 "Env Manager"
2. **直接启动** - 运行生成的桌面快捷方式
3. **Web 页面** - 开发时直接打开 `dist/index.html` 到浏览器

### GUI 功能

- **搜索栏** - 实时按名称或值筛选变量
- **作用域下拉菜单** - 在用户/系统/全部作用域间切换
- **变量表格** - 整齐排列: 名称、作用域、值、操作
- **添加按钮** - 打开对话框创建新变量
- **编辑按钮** - 修改现有变量（当前作用域）
- **删除按钮** - 删除变量（需确认）
- **备份/恢复按钮** - 导出或导入 JSON 备份

## 备份文件格式

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32",
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

## 系统要求

- **系统**: Windows 10 21H2 或更高版本（推荐 Windows 11）
- **CLI**: .NET Runtime 10.0+
- **GUI**: 无需额外运行时（Tauri 内置打包）

## 项目结构

```
env-manager/
├── Program.cs                    # CLI 实现 (215 行，C#)
├── env-manager.csproj            # .NET 项目配置
├── bin/Release/net10.0/          # 编译后的 CLI 二进制
│   └── env-manager.exe
├── frontend/                      # GUI 应用 (Tauri + TypeScript + Svelte)
│   ├── src/                      # 前端组件
│   │   ├── App.svelte            # 根组件
│   │   └── lib/components/       # 可复用组件
│   ├── src-tauri/                # Tauri 后端 (Rust)
│   │   └── src/main.rs           # IPC 命令处理器
│   └── package.json              # Node.js 依赖
├── dist/                         # 构建后的前端资源
├── .github/workflows/            # GitHub Actions CI/CD
│   └── build.yml                 # 完整的构建管道
└── AGENTS.md                     # 项目规范文档
```

## 开发

详见 `AGENTS.md` 包含的内容:
- 项目规范和架构设计
- 开发指南和代码标准
- CLI 命令实现细节
- GUI 组件结构
- CI/CD 管道说明
- 发布和测试流程

### 快速开发设置

```bash
# 验证环境
dotnet --version          # 验证 .NET 10
node --version            # 验证 Node.js 20+

# CLI 开发
dotnet build -c Release

# GUI 开发 (热重载)
cd frontend
npm install
npm run tauri-dev

# 运行测试
dotnet test
cd frontend && npm run test
```

## 安全性

- **无外部依赖** - CLI 核心功能无第三方依赖
- **本地 Registry 访问** - 仅本地操作，无远程传输
- **输入验证** - 所有用户输入经过验证（32KB Windows 限制）
- **作用域隔离** - 用户和系统作用域完全隔离
- **安全审计**: Semgrep 通过✅ (0 个发现)

详见 `SECURITY_AUDIT.md` 的完整安全分析。

## 技术栈

### 后端
- **语言**: C# .NET 10
- **注册表**: Microsoft.Win32.Registry (原生 Windows API)
- **CLI 输出**: Spectre.Console (美化输出)
- **部署**: 单一 158KB 可执行文件

### 前端
- **框架**: Tauri 2.0 (轻量级桌面应用)
- **UI**: Svelte 4 (响应式组件)
- **语言**: TypeScript 5 (类型安全)
- **样式**: TailwindCSS 3 (原子式 CSS)
- **构建**: Vite 5 (快速打包)

## CI/CD 管道

GitHub Actions 工作流: `.github/workflows/build.yml`

**阶段**:
1. **Lint** - Semgrep 安全扫描
2. **Build-CLI** - .NET 编译 + 制品上传
3. **Build-GUI** - Tauri 编译 + MSI 生成
4. **Test** - 集成测试
5. **Release** - 版本标签自动发布 GitHub Release

**触发条件**:
- 推送到 main: 运行 lint、build、test
- 推送标签 (v*): 运行完整管道 + 发布

## 常见问题

### 如何管理系统变量？
使用 `--scope system` 标志。需要管理员权限:

```bash
# 以管理员身份运行
env-manager.exe set SYSTEM_VAR "value" --scope system
```

### 能否备份和恢复？
当然! JSON 格式人类可读、跨平台兼容:

```bash
env-manager.exe backup --output my-backup.json
# 如需编辑 my-backup.json
env-manager.exe restore my-backup.json
```

### GUI 无需安装就能用吗？
可以! 开发时可直接打开网页版:

```bash
start .\dist\index.html
```

开发模式热重载:
```bash
cd frontend && npm run tauri-dev
```

### 如何更新？
Tauri GUI 内置自动更新支持。CLI: 下载新 .exe 替换即可。

## 故障排除

| 问题 | 解决方案 |
|------|--------|
| "拒绝访问" (系统作用域) | 以管理员身份运行 |
| 变量未立即显示 | 重启应用（环境变量缓存） |
| GUI 无法启动 | 检查浏览器控制台 (F12) 的错误信息 |
| 备份文件无效 | 手动验证 JSON 格式或使用 `validate` 命令 |

## 许可证

MIT - 自由用于个人和商业项目

详见 [LICENSE](LICENSE)

## 贡献

这是一个开源项目。如有问题、功能需求或拉取请求:

1. 查看 GitHub Issues
2. 报告 bug 时提供:
   - Windows 版本
   - CLI/GUI 版本号
   - 精确重现步骤
   - 截图 (GUI 问题)

## 更新日志

详见 [CHANGELOG.md](CHANGELOG.md) 了解版本历史和发布说明。

## 联系与支持

- **问题报告**: GitHub Issues
- **讨论**: GitHub Discussions
- **安全问题**: 私密报告

---

**版本**: 0.3.0  
**许可**: MIT  
**状态**: 生产就绪
