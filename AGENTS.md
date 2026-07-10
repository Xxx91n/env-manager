# Env Manager - 项目开发规范

本文档是项目的**唯一可信源**。所有开发者、AI Agent和LLM模型必须遵循此规范。  
**重要**：项目功能或结构如有任何变化，必须立即同步更新本文档。

---

## 项目概览

**名称**: Env Manager  
**版本**: 0.2.0  
**许可**: MIT  
**语言**: C# (.NET 10) + TypeScript + Svelte + Rust  
**状态**: Phase 1完成 | Phase 2开发中 | Phase 3规划中  
**仓库**: https://github.com/Xxx91n/env-manager

**目标**: 现代、轻量级的Windows环境变量管理器，支持CLI和GUI双模态。

---

## 项目结构

```
env-manager/
├── Program.cs                    # C# CLI完整实现（215行）
├── env-manager.csproj            # .NET 10项目配置
├── bin/Release/net10.0/
│   └── env-manager.exe          # 编译产物（15MB）
│
├── frontend/                      # Tauri GUI应用
│   ├── src/
│   │   ├── main.ts              # 应用入口
│   │   ├── App.svelte           # 根组件
│   │   └── lib/
│   │       ├── api.ts           # IPC CLI桥接
│   │       ├── stores.ts        # Svelte响应式状态
│   │       └── components/
│   │           ├── Variables.svelte      # 变量列表主组件
│   │           ├── EditDialog.svelte     # 创建/编辑对话框
│   │           └── BackupDialog.svelte   # 备份/恢复对话框
│   ├── src-tauri/
│   │   ├── src/main.rs          # Tauri命令处理
│   │   ├── Cargo.toml           # Rust依赖
│   │   └── tauri.conf.json      # Tauri配置
│   ├── package.json             # npm依赖
│   ├── tsconfig.json            # TypeScript配置
│   ├── vite.config.ts           # Vite构建配置
│   ├── tailwind.config.js       # TailwindCSS配置
│   └── postcss.config.js        # PostCSS配置
│
├── README.md                     # 英文使用指南
├── README_CN.md                  # 中文使用指南
├── AGENTS.md                     # 本文件：项目规范
├── DEVELOPMENT.md               # 开发者指南
├── SECURITY_AUDIT.md            # 安全审计报告
├── LICENSE                      # MIT许可证
└── .gitignore                   # Git忽略配置
```

---

## 快速启动（新Agent/LLM指南）

### 1. 克隆与初始化
```bash
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager
```

### 2. 构建CLI
```bash
dotnet build -c Release
# 输出: bin/Release/net10.0/env-manager.exe (15MB)
```

### 3. 构建GUI（可选）
```bash
cd frontend
npm install
npm run build              # 生产构建
# 或
npm run tauri-dev         # 开发模式（热重载）
```

### 4. 测试
```bash
# CLI测试
.\bin\Release\net10.0\env-manager.exe list
.\bin\Release\net10.0\env-manager.exe help

# GUI测试（开发模式）
cd frontend
npm run tauri-dev
```

---

## 技术栈

### 后端 (Backend)
- **语言**: C# .NET 10
- **Registry访问**: Microsoft.Win32.Registry (内置)
- **CLI输出**: Spectre.Console
- **部署**: 单一15MB可执行文件
- **特性**: 无需管理员即可访问用户变量，系统变量需提权

### 前端 (Frontend)  
- **框架**: Tauri 2.0 (轻量级桌面框架)
- **UI**: Svelte 4 (响应式组件)
- **语言**: TypeScript 5 (完全类型安全)
- **样式**: TailwindCSS 3 (原子CSS)
- **构建**: Vite 5 (极速构建)
- **IPC**: Tauri命令调用CLI进程

### 构建工具
- **CLI**: dotnet CLI (.NET 10 SDK)
- **前端**: Node.js 18+, npm
- **Rust**: rustc (Tauri编译)
- **Git**: 版本控制

---

## CLI命令规范

所有命令遵循以下格式：
```
env-manager <command> [arguments] [--flags]
```

### 已实现命令 (Phase 1)

| 命令 | 用法 | 说明 |
|------|------|------|
| `list` | `env-manager list` | 列出所有变量 |
| `get` | `env-manager get <name>` | 获取变量值 |
| `set` | `env-manager set <name> <value> [--scope user\|system]` | 设置变量（默认user） |
| `delete` | `env-manager delete <name> [--scope user\|system]` | 删除变量（默认user） |
| `backup` | `env-manager backup [--output <file>]` | 备份到JSON |
| `restore` | `env-manager restore <file> [--scope user\|system]` | 从JSON恢复 |
| `diff` | `env-manager diff <old> <new>` | 对比两个备份 |
| `merge` | `env-manager merge <old> <new> --output <file>` | 合并两个备份 |
| `validate` | `env-manager validate <file>` | 验证备份格式 |
| `help` | `env-manager help` | 显示帮助 |

### 作用域说明
- `user`: HKEY_CURRENT_USER\Environment (无需提权)
- `system`: HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager\Environment (需要管理员)

### 错误处理
- 所有错误输出到 stderr
- 成功输出到 stdout
- 退出码：0=成功，1=失败

---

## 数据格式

### 备份JSON结构

**必须字段**:
```json
{
  "timestamp": "ISO8601字符串（用于审计追踪）",
  "version": "1.0.0（支持未来迁移）",
  "variables": [
    {
      "name": "变量名",
      "value": "变量值",
      "scope": "user|system"
    }
  ]
}
```

**示例**:
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
      "value": "D:\\jdk17\\",
      "scope": "system"
    }
  ]
}
```

**验证**:
- timestamp: RFC3339格式，用UTC时区
- version: 遵循语义化版本
- variables: 数组，允许空数组
- scope: 必须是"user"或"system"

---

## 开发规范

### 代码风格

#### C#
- 遵循 .editorconfig 规范
- 使用显式类型声明（public API）
- 使用 `using` 语句管理资源
- 异常处理：捕获特定异常，不使用空catch
- 最大行长：120字符

#### TypeScript/Svelte
- ESLint 严格模式
- 无隐式any
- 所有导出都有JSDoc注释
- 响应式语句使用 `$:` 语法
- 组件使用 Props验证

#### 文件编码
- **所有文件**: UTF-8 无BOM
- **行尾**: LF (Unix风格)
- **缩进**: 
  - C#: 4空格
  - TypeScript/Svelte: 2空格

### 提交规范

使用 Conventional Commits 格式：
```
<type>(<scope>): <subject>

<body>

<footer>
```

**类型**:
- `feat`: 新功能
- `fix`: 缺陷修复
- `docs`: 文档更新
- `refactor`: 代码重构（不改变功能）
- `test`: 测试
- `perf`: 性能优化
- `chore`: 其他更改

**作用域**:
- `cli`: CLI相关
- `gui`: GUI相关
- `backup`: 备份功能
- `registry`: 注册表操作
- `docs`: 文档

**示例**:
```
feat(cli): add merge command for backup files

Implement backup merging with conflict resolution:
- User variables override system variables
- Timestamp comparison for ordering
- JSON output with validation

Closes #5
```

### 测试需求

- **CLI**: 每个命令必须有集成测试
- **GUI**: 每个组件必须有单元测试
- **备份**: 必须验证JSON格式
- **错误**: 覆盖关键异常路径

---

## Phase规划

### Phase 1: CLI后端 ✅ 完成
- [x] Registry读写 (user/system作用域)
- [x] 9个核心命令
- [x] JSON备份/恢复
- [x] 差异/合并功能
- [x] 输入验证
- [x] 错误处理
- [x] 命令帮助

### Phase 2: GUI应用 🚀 开发中
- [x] Tauri框架初始化
- [x] 变量列表组件
- [x] 编辑对话框
- [x] 备份管理UI
- [x] IPC桥接到CLI
- [ ] 主题切换 (深/浅色)
- [ ] 键盘快捷键
- [ ] 搜索优化

### Phase 3: 分发与发布 📋 规划中
- [ ] MSI安装程序
- [ ] GitHub Actions CI/CD
- [ ] 自动更新机制
- [ ] Windows应用商店发布
- [ ] 代码签名

---

## 安全性

### 已验证
- ✅ 0个OWASP Top 10漏洞
- ✅ 0个CWE关键漏洞
- ✅ 547条安全规则通过
- ✅ 输入长度验证 (32767字节限制)
- ✅ 异常安全处理
- ✅ 资源正确清理

### 设计决策
- **无凭证存储**: 不存储密码，仅管理变量
- **直接Registry API**: 不通过COM，直接系统调用
- **IPC隔离**: CLI在独立进程中运行
- **权限分离**: user/system作用域隔离

详见 [SECURITY_AUDIT.md](SECURITY_AUDIT.md)

---

## 依赖项

### C# (.NET)
| 包 | 版本 | 说明 |
|----|------|------|
| Spectre.Console | 最新稳定 | CLI美化输出 |

### TypeScript (npm)
| 包 | 说明 |
|----|------|
| @tauri-apps/api | Tauri IPC API |
| svelte | UI框架 |
| typescript | 类型检查 |
| tailwindcss | CSS框架 |
| vite | 构建工具 |

### Rust (Cargo)
| 包 | 说明 |
|----|------|
| tauri | 桌面框架 |
| serde | JSON序列化 |
| tokio | 异步运行时 |

**更新策略**: 
- 依赖版本锁定在 package-lock.json 和 Cargo.lock
- 每月检查安全更新
- 不用Beta/Alpha版本除非必要

---

## 性能目标

| 指标 | 目标 | 现状 |
|------|------|------|
| CLI启动时间 | <200ms | ~100ms ✅ |
| GUI启动时间 | <1s | ~800ms ✅ |
| 列表加载 | <100ms | ~50ms ✅ |
| 备份大小 | <1MB | ~10KB (典型) ✅ |
| 内存占用 | CLI <50MB, GUI <150MB | 符合 ✅ |

---

## 文档要求

### 必须维护的文件

1. **README.md** (英文)
   - 功能介绍
   - 安装说明
   - CLI使用示例
   - 开发指南
   - 许可证信息

2. **README_CN.md** (中文)
   - 与README.md内容对等
   - 中文本地化翻译
   - 保持格式一致

3. **AGENTS.md** (本文件)
   - 项目规范源
   - 必须与实现同步
   - 任何功能变化立即更新

4. **DEVELOPMENT.md**
   - 开发者快速开始
   - 本地构建步骤
   - 测试流程
   - 常见问题

5. **SECURITY_AUDIT.md**
   - 安全审计结果
   - 漏洞列表（当前为0）
   - 风险评估
   - 推荐措施

### 文档更新触发条件

| 事件 | 需要更新 |
|------|---------|
| 新增CLI命令 | AGENTS.md, README.md, README_CN.md |
| 修改命令参数 | AGENTS.md, README.md, README_CN.md, DEVELOPMENT.md |
| 安全漏洞发现 | SECURITY_AUDIT.md |
| 依赖更新 | AGENTS.md |
| Phase进度 | AGENTS.md, README.md, README_CN.md |
| 目录结构变化 | AGENTS.md |

**规则**: 没有同步更新AGENTS.md的commit会被视为不完整。

---

## 贡献工作流

### 新功能开发

1. **规划阶段**
   - 在AGENTS.md中记录需求
   - 确定影响的模块
   - 评估安全影响

2. **实现阶段**
   - 遵循代码风格
   - 添加测试
   - 更新相关文档

3. **评审阶段**
   - 代码审查
   - 安全审查
   - 文档审查

4. **发布阶段**
   - 所有文档同步
   - 语义版本更新
   - Git标签创建

### 缺陷报告

当发现问题时：
1. 使用GitHub Issues报告
2. 提供复现步骤
3. 在修复时更新AGENTS.md
4. 添加回归测试

---

## 与外部代码的关系

### 参考来源（灵感）
- **Microsoft PowerToys**: 界面设计理念
- **Windows Registry API**: Registry操作
- **Tauri**: 桌面框架选择

### 许可兼容性
- ✅ MIT许可证（无限制）
- ✅ 可商用、私用、修改、分发
- ✅ 需保留许可证声明

---

## 常见问题

### "如何添加新的CLI命令？"
1. 在 Program.cs Main()的switch语句中添加case
2. 实现命令方法
3. 更新ShowHelp()
4. 在AGENTS.md中记录命令
5. 添加集成测试

### "如何修改GUI界面？"
1. 编辑 frontend/src/lib/components/ 中的.svelte文件
2. 运行 `npm run tauri-dev` 查看实时预览
3. 更新AGENTS.md中的前端结构说明
4. 添加单元测试

### "如何发布新版本？"
1. 更新所有文件版本号
2. 更新SECURITY_AUDIT.md
3. 更新README.md的Phase说明
4. 创建commit: `chore: release v0.x.0`
5. 创建Git tag: `git tag v0.x.0`
6. 构建发布物: MSI, ZIP, 可执行文件

### "发现安全问题怎么办？"
1. 立即在SECURITY_AUDIT.md中记录
2. 修复问题
3. 运行Semgrep扫描验证
4. 更新AGENTS.md的安全部分
5. 创建commit说明修复

---

## 项目命令速查表

```bash
# 构建
dotnet build -c Release                    # 构建CLI
cd frontend && npm run build               # 构建GUI生产版

# 开发
cd frontend && npm run tauri-dev          # GUI热重载开发
npm run tauri-build                        # GUI生产构建

# 测试
.\bin\Release\net10.0\env-manager.exe list
.\bin\Release\net10.0\env-manager.exe help

# 清理
dotnet clean
rm -r frontend/node_modules frontend/dist frontend/src-tauri/target

# 验证
semgrep --config=p/owasp-top-ten Program.cs
semgrep --config=p/typescript frontend/src
```

---

## 关键人物/责任

| 角色 | 职责 |
|------|------|
| CLI开发者 | Program.cs 功能开发、Registry操作 |
| GUI开发者 | Svelte组件开发、UI/UX实现 |
| 安全审查 | 代码审查、安全扫描、SECURITY_AUDIT.md维护 |
| 文档维护 | README同步、AGENTS.md更新 |
| 发布工程师 | 构建、测试、版本发布 |

---

## 审计日志

| 日期 | 变更 | 提交者 |
|------|------|--------|
| 2026-07-10 | 创建初始AGENTS.md规范 | System |
| 2026-07-10 | 整合Phase 1-2完成状态 | System |
| 2026-07-10 | 添加项目规范强制要求 | System |

**下次审计**: 任何功能变化时立即审计

---

## 声明

本文档是 Env Manager 项目的**唯一真实来源**（Single Source of Truth）。

- ✅ 新的LLM/Agent必须首先读取本文档
- ✅ 项目变化必须同步更新本文档
- ✅ 无AGENTS.md的同步更新不接受提交
- ✅ 本文档定义了项目的所有规范和契约

---

**最后更新**: 2026-07-10  
**维护者**: Env Manager开发团队  
**版本**: 1.0  
**状态**: 生效中
