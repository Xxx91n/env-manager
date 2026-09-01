# Spec — Env Manager C# 引擎架构恢复（architecture-recovery）

生成日期：2026-08-31 · 来源：架构巡检报告 + 两轮 atomcode 业界调研 · 遵循 `$to-spec` 模板
（2026-08-31 重建版：原文件随 .scratch 树外部清理丢失，由大脑会话按原始内容恢复）
领域词汇遵循 CONTEXT.md；相关 ADR：docs/adr/0010（测试金字塔，需修订扩展）、docs/agents/hard-boundaries.md。

## Problem Statement

Env Manager 的 C# 引擎（改 PATH、管 secret、写注册表的核心）是仓库里风险最高、测试最薄弱的部分：Program.cs 3585 行内联 25 个命令的分发与实现，注册表/P/Invoke 调用没有注入点，xUnit 工程不存在，唯一保护是真实注册表 + 备份回滚的集成脚本。改 engine 的行为没有快速反馈环，launch 注入的 secret 是否生效缺乏系统性验证，维护者与 agent 都只能靠 grep 跨越多个文件的静态共享状态来理解一条命令路径。

## Solution

以"接口即测试面"为主线做架构恢复：给引擎建立 IEnvironmentScope seam（生产走 Registry+P/Invoke，测试走内存实现），在其上落地 xUnit 测试金字塔（含 launch 注入 golden 断言与 canary redaction 负断言），再按 gh CLI pkg/cmd 模板把命令域从 Program.cs 搬出为深模块，最后以契约测试收敛三套 IPC 客户端。保持 CLI 外部行为与自建 ArgTokenizer 解析不变；System.CommandLine 迁移明确推迟。

## User Stories

1. As a maintainer, I want engine logic behind an IEnvironmentScope interface, so that command behavior can be tested without touching the real registry.
2. As a maintainer, I want an InMemoryScope test double, so that unit tests run in milliseconds and are parallel-safe.
3. As a maintainer, I want a RegistryScope production implementation, so that all registry/P-Invoke side effects live in one thin adapter.
4. As an agent, I want `dotnet test` wired into CI, so that engine regressions are caught before merge.
5. As an agent, I want pure functions (argument tokenizing, secret scrubbing, PATH parsing) under unit test, so that refactors are protected from day one.
6. As a user, I want set/toggle/delete/rename/change-scope behavior unchanged after seam migration, so that the CLI contract stays stable.
7. As a user, I want protected-variable and rename write-verify-delete hard boundaries enforced by tests, so that regressions of documented incidents cannot recur.
8. As a maintainer, I want profile and secret-provider flows migrated through the seam, so that the highest-risk write paths are test-covered.
9. As a maintainer, I want ADR 0010 amended to extend the test pyramid to the C# engine, so that the pyramid is policy, not a GUI-only exception.
10. As an agent, I want profile/path/service/audit/agents command logic in dedicated command modules, so that understanding one command never requires reading a 3585-line file.
11. As an agent, I want Program.cs reduced to a thin Main dispatch, so that new commands follow one obvious pattern.
12. As an agent, I want EnvFeatures.cs split into named domain modules, so that audit/expand/bulk/dpapi concerns are findable by name.
13. As a user, I want profile launch variable injection verified by golden assertions and probe-process echo checks, so that "secrets actually took effect" is proven, not assumed.
14. As a user, I want canary-secret negative tests scanning all output sinks, so that secret plaintext can never reach logs/stderr silently.
15. As a maintainer, I want one IPC schema with contract tests across the C# CLI, Tauri frontend, and Rust service clients, so that protocol drift fails CI instead of corrupting state.

## Implementation Decisions

- Introduce **IEnvironmentScope** as the single engine seam: enumerate/read/write/delete/toggle operations plus the change-broadcast signal. Highest viable seam; exactly one new seam, per $to-spec guidance.
- **RegistryScope**: production implementation owning all Microsoft.Win32.Registry calls and the broadcast P/Invoke. **InMemoryScope**: dictionary-backed test double preserving scope semantics (user vs system).
- Command logic depends only on IEnvironmentScope; partial-class cross-file statics are consolidated into the command modules during extraction.
- Command-module shape follows gh CLI pkg/cmd: one module per command domain (profile, path, service, audit, agents, update), thin Main dispatch, **ArgTokenizer retained** — System.CommandLine migration is an explicit non-goal this round.
- EnvFeatures.cs splits into audit/history, expand, bulk import-export, DPAPI helper, and native-methods modules; the "EnvFeatures" name is retired.
- Launch verification adopts the four-layer industry pattern: golden env assertions, probe-process echo, and canary redaction negative tests across all output sinks (stderr, audit log, GUI toast paths).
- IPC convergence: Rust service owns the schema; C# and TS clients validated by contract tests asserting payload compatibility.
- ADR 0010 decision 6 is amended to extend the test pyramid from GUI-only to the C# engine.
- Hard boundaries (docs/agents/hard-boundaries.md) are load-bearing: rename write-verify-delete order, protected entries, mutex layering, and secrets-never-in-registry become executable tests where feasible.

## Testing Decisions

- Good tests assert external behavior only (CLI exit codes, emitted JSON/text, registry-visible outcomes via the seam), never private implementation details.
- Test layers: xUnit unit tests against InMemoryScope (bulk); probe-process/golden tests for launch injection (few); test-with-restore.ps1 demoted to top-of-pyramid smoke on 3–10 critical paths (fewest).
- Prior art: service crate process_guard.rs already has 12 unit tests (the repo's only healthy pyramid sample); frontend Vitest mockIPC pattern; Pester launch-env-injection test is the probe-pattern seed to upgrade.
- Redaction testing follows the canary pattern: unique fake secret injected, all sinks scanned for zero occurrence, plus positive assertions that masking placeholders appear.

## Out of Scope

- System.CommandLine or Spectre.Console.Cli migration (revisit after the test net exists).
- RegLoadAppKey isolated-hive harness as a main test facility.
- Rust service crate restructuring; GUI feature changes; i18n string work beyond what module moves touch.
- Pushing branches, opening PRs, or any release activity.

## Further Notes

- Ticket breakdown, handoffs, launchers, and parallel waves live under `.scratch/architecture-recovery/` per WORKFLOW.md.
- Industry evidence backing these choices was produced via two atomcode research runs (2026-08-31): .NET CLI 拆分与注册表测试模式、launch 注入验证与 redaction 测试模式。
