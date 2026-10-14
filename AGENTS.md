# Env Manager — Agent Instructions

> Single source of truth pointer file. Details live in docs/ — keep this file short; link out instead of inlining.

## 项目定位

Windows 环境变量管理器：CLI（C# .NET 10，直读写注册表）+ Tauri/Rust shell + Svelte 4 GUI + 可选 Rust service（密钥挂载生命周期）。Apache-2.0，仓库 `Xxx91n/env-manager`，当前 0.12.x。

## 怎么跑

- 全量构建：`node scripts/build.mjs --arch x64`（`--skip-gui/--skip-msi/--skip-cli` 可分段）；产物在 gitignored `release/`。
- 前端单测：Vitest（`*.test.ts` 与源码同位）。引擎单测：`dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`（不碰真实注册表）。
- 注册表写操作冒烟一律走 `scripts/test-with-restore.ps1`；裸跑写命令=红线。

## 技术栈

C# .NET 10 / Rust（Tauri 2 + service crate）/ TypeScript + Svelte 4 + Tailwind / WiX v3.14.1 MSI / svelte-i18n（10 语）。

## 目录约定

- `src/` C# 全部源码（一域一模块 + CliRuntime 共享层）；`src/Audit`、`src/Secrets` 为两个 bounded context。
- `frontend/` Tauri GUI（src / src-tauri / tests）；`service/` Rust service crate；`tests/` xUnit 引擎测试。
- `docs/` 权威文档区：`adr/`（难逆转决策）、`agents/`（agent 参考）、`history/`（过程档案骨架）、`i18n/`（9 语 README）。
- `scripts/` 构建/测试/门禁脚本；`scripts/demo/` demo 资产再生管线（docs/assets 动图唯一来源）。
- `.scratch/` 本地未追踪区（一次性工件与治理档案，非产品面，不入库）。

## Issue tracker

- 产品级未决问题：`.scratch/gov-audit/backlog.md`（本地唯一合法题源）+ GitHub issues（`gh issue list`，roadmap 见 #28/#29）。

## 领域文档指针

- 决策依据：[docs/adr/](docs/adr/)（ADR 0001-0014，accepted 后 append-only）。
- 词汇唯一权威：[CONTEXT.md](CONTEXT.md)（只收领域术语+_Avoid_ 对；决策叙事不入此文件）。
- 红线全集：[docs/agents/hard-boundaries.md](docs/agents/hard-boundaries.md)（279 行，改码前必读）。顶格三条：受保护变量/PATH 不可写删改；写操作三层锁（mutex+CLI_RWLOCK+writeChain）不可绕；rename/scope-change 必须 write+verify 后删源，禁 TxR/TxF（ADR 0014）。
- 命令表 [docs/cli-commands.md](docs/cli-commands.md)；架构/IPC/对齐表 [docs/architecture.md](docs/architecture.md)；构建/发布/CodeGraph [docs/build-and-release.md](docs/build-and-release.md)；密钥架构 [docs/secret-architecture-blueprint.md](docs/secret-architecture-blueprint.md)；CLI 随件手册 [AGENTS.cli.md](AGENTS.cli.md)。

## 协作与 VCS 纪律

- 版本控制一律 `but`（GitButler）；未经用户明示不 push、不 merge、不动 tag/release/secret/远端分支（发行版动作须用户显式授权，附外部可验证证据）。
- 输出给用户的路径一律写完整绝对路径。
- 写文件用 Node.js（禁 BOM、保尾行）；读/搜/大 grep 优先 ctx_* 工具，代码探索先 `codegraph_explore`（projectPath=仓库根），改码后 `codegraph sync .`。
- 交付物命名/语义与执行单不符时上报裁决，不静默吸收。
- 项目变更时同 commit 更新相关 docs；README 面向读者的区块改一处，9 语 i18n 保持同节镜像（canonical/sync 戳惯例见 CONTEXT.md，校验 `scripts/check-readme-i18n.ps1`）。
