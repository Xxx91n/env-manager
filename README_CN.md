# Env Manager
[English](README.md) | 中文

一款现代、轻量级的Windows环境变量管理器，提供快速CLI和优雅的桌面GUI。

灵感来自Microsoft PowerToys，专为效率而设计。开源MIT协议。

---

## 特性

### CLI命令行工具

- 列出用户和系统作用域的所有变量
- 支持作用域控制的获取、设置、删除操作
- 以JSON格式备份和恢复环境快照
- 对比和合并多个备份以追踪变更
- 使用Spectre.Console提供美观的表格输出
- 基于.NET 10的快速原生性能

### GUI桌面应用（第2阶段）

- 实时环境变量编辑器
- 跨所有作用域的搜索和筛选
- 备份导出和导入管理
- 支持深色模式的响应式设计
- CLI与桌面应用的IPC同步
- 使用Tauri实现轻量级原生性能

---

## 快速开始

### 安装

**选项1：下载二进制文件**

从GitHub Releases下载：
https://github.com/Xxx91n/env-manager/releases

复制到PATH路径或直接使用：
```powershell
.\\env-manager.exe list
```

**选项2：从源代码编译**

```powershell
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager
dotnet build -c Release

# 二进制文件位置：bin/Release/net10.0/env-manager.exe
```

### CLI使用

```powershell
# 列出所有变量
env-manager list

# 获取特定变量
env-manager get PATH

# 设置变量（用户作用域）
env-manager set MY_VAR "my_value"

# 设置系统变量（需要管理员权限）
env-manager set MY_VAR "my_value" --scope system

# 删除变量
env-manager delete MY_VAR

# 备份当前状态
env-manager backup --output my_backup.json

# 从备份恢复
env-manager restore my_backup.json

# 对比两个备份
env-manager diff old_backup.json new_backup.json

# 合并备份
env-manager merge old.json new.json --output merged.json

# 显示帮助
env-manager help
```

---

## 项目阶段

### 第1阶段：CLI后端（已完成）

- 快速CRUD操作环境变量
- 用户和系统作用域支持
- 最小化依赖项（仅Spectre.Console）
- 直接调用Windows Registry API
- 在Windows 10和11上完全测试

### 第2阶段：桌面GUI（开发中）

- 基于Tauri的跨平台应用
- TypeScript和Svelte前端
- 实时环境变量编辑器
- 备份导出和导入UI
- CLI IPC桥接

### 第3阶段：完善和分发（计划中）

- MSI安装程序便捷安装
- GitHub Releases自动下载功能
- 自动更新机制
- 完整文档和指南

---

## 架构

### 后端（C# .NET 10）

Program.cs包含：
- Registry API包装器
- 变量CRUD操作
- 备份和恢复逻辑
- CLI命令路由
- 错误处理

**技术栈**：
- 语言：C# .NET 10
- Registry访问：Microsoft.Win32.Registry（内置）
- CLI输出：Spectre.Console
- 交付：单一可执行文件，约15MB（包含运行时）

### 前端（Tauri + TypeScript/Svelte）

```
src/
├── App.svelte              (根组件)
├── lib/
│   ├── api.ts              (IPC桥接到CLI)
│   ├── components/         (UI组件)
│   └── stores/             (状态管理)
└── styles/                 (TailwindCSS样式)
```

**技术栈**：
- 框架：Tauri 2.0
- UI：TypeScript和Svelte
- 样式：TailwindCSS
- 构建：Vite

### 数据格式

**备份JSON结构**：
```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\\\Windows\\\\System32;...",
      "scope": "user"
    },
    {
      "name": "JAVA_HOME",
      "value": "D:\\\\jdk17\\\\",
      "scope": "system"
    }
  ]
}
```

---

## 与其他工具对比

| 特性 | Env Manager | PowerToys | setx（内置） |
|------|------------|----------|-------------|
| GUI图形界面 | 规划中 | 有 | 无 |
| CLI命令行 | 有 | 无 | 有（有限） |
| 备份/恢复 | 有 | 无 | 无 |
| 差异/合并 | 有 | 无 | 无 |
| 开源 | 是（MIT） | 是（MIT） | 是 |
| .NET轻量 | 是 | 否（C++） | N/A |
| 跨作用域 | 是 | 是 | 是（有限） |

---

## 开发

### 环境配置（Windows）

**前置条件**：
- .NET 10 SDK
- Node.js 18或更高版本（用于GUI）
- Tauri CLI
- Git

**克隆和编译**：
```powershell
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager

# 编译CLI
dotnet build -c Release

# 编译GUI（第2阶段）
cd frontend
npm install
npm run tauri-dev
```

### 项目结构

```
env-manager/
├── Program.cs              # C# CLI实现
├── env-manager.csproj      # .NET项目文件
├── bin/Release/            # 编译输出
│   └── net10.0/
│       └── env-manager.exe
│
├── frontend/               # Tauri GUI（第2阶段）
│   ├── src/
│   │   ├── App.svelte
│   │   ├── lib/
│   │   └── main.ts
│   ├── src-tauri/          # Rust后端
│   ├── package.json
│   └── tsconfig.json
│
├── AGENTS.md               # 开发规范
├── DEVELOPMENT.md          # 开发指南
├── README.md               # 英文版本
├── README_CN.md            # 中文版本
├── LICENSE                 # MIT许可证
└── .gitignore
```

### 测试

**CLI冒烟测试**：
```powershell
.\\bin\\Release\\net10.0\\env-manager.exe list
```

**备份测试**：
```powershell
.\\bin\\Release\\net10.0\\env-manager.exe backup --output test.json
.\\bin\\Release\\net10.0\\env-manager.exe validate test.json
```

### 贡献

欢迎贡献。详见DEVELOPMENT.md了解配置和工作流程。

**代码风格**：
- C#：遵循.editorconfig规范
- TypeScript：ESLint严格模式
- 提交：遵循Conventional Commits格式

---

## 许可证

MIT许可证。版权所有(c) 2026 Env Manager 贡献者。

详见LICENSE文件。

---

## 文档

- README.md（英文版）
- README_CN.md（中文版，本文件）
- AGENTS.md（开发规范）
- DEVELOPMENT.md（开发者指南）
- ARCHITECTURE.md（系统设计）
- CHANGELOG.md（发布说明）

---

## 社区

- 在GitHub Issues上报告bug
- 在GitHub Discussions上提问
- 提交PR进行改进
- 通过Issues分享想法（标记为enhancement）

---

## 发展路线

- v0.2（第2阶段）：Tauri GUI与备份命令
- v0.3（第3阶段）：MSI安装程序和自动更新
- v1.0：稳定版本，支持Windows应用商店
- v1.1及更高版本：社区驱动的增强功能

---

## 为什么选择Env Manager？

- 现代：使用最新的.NET和Tauri技术构建
- 快速：单一可执行文件的原生性能
- 轻量：CLI约15MB，依赖项最少
- 安全：直接Registry访问，全面错误处理
- 精美：清爽的CLI输出和优雅的GUI设计
- 免费：MIT开源许可证
- 社区驱动：您的贡献塑造未来

---

仓库地址：https://github.com/Xxx91n/env-manager
