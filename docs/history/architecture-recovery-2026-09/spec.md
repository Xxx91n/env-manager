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

---

## Phase 2 — SecretProvider 单文件拆分 + 契约测试套件（2026-09-02）

> 由 atomcode 深度调研驱动（source: "atomcode"，五条独立证据线：gocloud.dev drivertest / Dapr components-contrib conformance / EF Core Specification Tests / WopiHost PR #411 / Arcus.Security）。摘要落盘 .scratch/architecture-recovery/research/secret-provider-patterns.md。

### Problem Statement

src/SecretProvider.cs 是约 1900 行单文件上帝对象，8 个 ISecretProvider 实现（DPAPI / Windows Credential Manager / PowerShell SecretManagement / HashiCorp Vault KV2 / sops / Azure Key Vault / 1Password / AWS Secrets Manager）与 SecretEnvelope、JSON 序列化上下文、SecretProviderManager 共处一文件。改一个 provider 需要跨过另外 7 个的实现；且除 fail-closed 解密路由外，没有"同一接口的所有实现共享同一套行为断言"的契约测试——新增 provider 或改其行为时没有自动回归网。

### Solution

把单文件按 provider 拆为独立模块（一 provider 一文件），接口/信封/管理器归位；再在 xUnit 工程落地共享契约测试套件：抽象契约基类 + harness 工厂缝，每 provider 一个挂载子类自动继承同一套行为断言，并以反射合规闸门保证"每个实现恰好挂一个契约子类"。对外行为零变化，不做 NuGet 分包（单 exe CLI）。

### User Stories

1. As an agent, I want each secret provider in its own file, so that changing one provider never requires scrolling past the other seven.
2. As an agent, I want one shared contract-test suite over ISecretProvider, so that adding a ninth provider inherits the same behavior assertions by writing one subclass.
3. As a maintainer, I want a compliance gate that fails the build when an ISecretProvider implementation lacks a contract-test subclass, so that coverage cannot silently rot.
4. As a user, I want provider behavior (fail-closed decrypt, round-trip, stable typed errors) asserted uniformly, so that a regression in any provider is caught before merge.
5. As a maintainer, I want the DPAPI provider covered on a real backend in CI, so that the local secret path is tested without cloud credentials.

### Implementation Decisions

- 拆分目标：一 provider 一文件；ISecretProvider + SecretEnvelope + JsonSerializerContext + SecretProviderManager 各自归位；"SecretProvider.cs" 单文件退役（类型名保留，退役的是单文件形态）。
- 契约套件采用"抽象 xUnit 基类 + CreateHarness() 工厂缝"形态（WopiHost LockProviderConformanceTests 式）；每个 provider 写一个 sealed 子类 + 一个 harness 夹具。
- 不用 [Theory]+[MemberData] 反射枚举所有实现跑一遍——各实现装配/清理逻辑不同、失败定位依赖参数名（社区已否定的做法）。
- harness 是中立后端夹具：CreateProviderAsync / SeedSecretAsync（绕过 SUT 布数据）/ ReadRawSecretAsync（绕过 SUT 验落盘），防读写对称 bug 互相抵消。
- 合规闸门：反射断言每个 ISecretProvider 实现恰好映射一个契约子类，新增未挂即红（EF ComplianceTest 式）。
- 测试分层：L0 内存 fake / 真实本地后端（DPAPI）每 PR；L1 模拟器（Vault dev server / Azurite / Testcontainers）每 PR 有条件；L2 真实云服务定时/发布管道、凭据 env 注入。

### Testing Decisions

- 好测试只断言外部行为（fail-closed、往返、稳定错误码），不碰私有实现。
- 契约断言只经 harness 的中立操作表达，与具体后端无关。
- 现有 fail-closed 钉住测试（ProfileSeamValidationTests 的 Decrypt fail-closed 路由）评估迁入契约或保留原位，不重复。
- 前端门禁测试若 readFileSync SecretProvider.cs 路径，随拆分同步重指向。

### Out of Scope

- 不做 NuGet 分包 / 契约套件对外分发（Arcus 式多项目形态）。
- 不改任何 provider 的对外行为或支持矩阵；不新增 provider。
- 不做 Pact 式 consumer-driven contract（服务间契约，与本场景无关）。
- 不把 provider 特有行为（secret 版本/轮转 KV2 语义）纳入共享契约（首版只断言通用行为）。

### Further Notes

- 调研坦承无"8 provider + 契约套件"现成 .NET 成品，本方案是 Arcus（多 provider）+ WopiHost（契约套件）两先例的拼装，构件全部来自成熟先例。
- 版本策略：契约套件与实现同 PR lockstep（不对外分发包，无 semver 问题）。

---

## Phase 3 — 测试心智模型升级：差分 + 模型化 + 变异（2026-09-03）

> 由 atomcode 深度调研驱动（source: "atomcode"，~21 查询 / 15 全文核验 / 3 引擎）。摘要落盘 `.scratch/architecture-recovery/research/next-wave-patterns.md`。

### Problem Statement

seam 化已经给了引擎"当状态机来测"的资格，但测试心智模型仍停在"契约/黄金文件"层：InMemoryScope 只被证明"忠实于自身语义"，没有任何测试钉住它是否"忠实于 Windows"；写路径核心（rename/change-scope/set/delete/PATH）的边界行为靠手写用例钉住，无随机化模型测试兜底；"杀死测试的测试"质量（变异测试）从未验证；CLI 文案/i18n/canary 输出无快照锁定；7 个外部 secret provider 的后端依赖断言仍 Skip；TxR/TxF 已官方弃用，补偿式写入范式未写成 ADR。

### Solution

按 ROI 引入测试三件套 + 两处补强：差分测试（Windows 真实语义为 oracle）钉住 InMemoryScope 忠实度；状态机模型测试（CsCheck/FsCheck）对写路径核心随机化；变异测试（Stryker.NET）验证红线测试质量（本地/PR 辅助，非 CI 硬门）；快照测试（Verify）锁定 CLI 人读契约；Testcontainers L1 把 7 个 Skip 后端断言转真；ADR 制度化"禁止 TxR + 补偿式写入"。架构侧不新增范式——service+launch 分层已被 fnox 2026 独立印证，apply/unapply 声明式方向正与 DSC v3 合流。

### User Stories

16. As a maintainer, I want InMemoryScope pinned against real Windows semantics (REG_EXPAND_SZ preservation, PATH length boundaries, empty-entry semantics, `=` rejection, system-scope elevation), so that a faithful test double is proven rather than assumed.
17. As a maintainer, I want randomized state-machine tests over the write-path core, so that rename/change-scope/set/delete/PATH ordering and broadcast timing hold across 1000-step sequences with minimal counter-examples.
18. As a maintainer, I want mutation testing over red-line code, so that the tests protecting hard boundaries genuinely kill mutations instead of silently rotting.
19. As a maintainer, I want CLI help/error/canary output snapshot-locked, so that user-facing text drift appears explicitly in review.
20. As a maintainer, I want the 7 skip-mounted secret providers' backend-dependent assertions running against local emulators, so that round-trip/plaintext-never is proven without cloud credentials.
21. As a maintainer, I want an ADR forbidding TxR/TxF and institutionalizing the compensatory-write paradigm, so that the deprecated-transaction suggestion is permanently rejected with recorded rationale.

### Implementation Decisions

- 差分测试：复用 test-with-restore 夹具；语义矩阵=REG_EXPAND_SZ 保留 %VAR% 不预展开、PATH 值 1024~30000 字符边界、空条目=当前目录语义、变量名含 `=` 拒绝、system scope 写需 elevation；每步断言"终态注册表值 == InMemory 终态 且 广播次数一致"。
- 模型测试：CsCheck（C# 原生，stateful+parallel）`Machine<EngineState, ModelState>`；操作=Rename/ChangeScope/Set/Delete/PathAdd/PathRemove；模型=Dictionary<(Scope,string),string?> + 广播计数；FsCheck Experimental API 已知（无 semver 承诺）。
- 变异测试：Stryker.NET mutate 范围=VariableRename/VariableChangeScope/ProfileEffective/ProtectionCommand；ignore string/logical；thresholds high85/low70/break60；本地/PR 辅助（v5 需 dotnet10 runtime、#3351/#3367 管线摩擦，先不上 CI 硬门）。
- 快照测试：Verify.Xunit 对 help/stdout/错误/canary 输出（<encrypted>/<revealed>）快照；scrubber 清 PID/时间戳；i18n 每 locale 全键渲染快照。
- Testcontainers L1：Azurite 3.24.0 / LocalStack 2.0 / Lowkey Vault（官方模块）+ Vault dev server（通用容器）；Linux 容器 runner 首选，Windows runner Docker 可用性先验证。
- ADR：记录 TxR/TxF 已弃用（可能在未来 Windows 移除），补偿式写入 + 三层锁 + 审计恢复为唯一可持续路线，并对齐 hard-boundaries.md。

### Testing Decisions

- 好测试只断言外部行为（终态、广播计数、输出文本），不碰私有实现。
- 差分 oracle 是新的 apex 夹具；模型测试是 bulk；快照是与 IPC golden 互补的"人读契约"层。
- 先例：WritePathSeamTests（seam 行为）、ProfileSeamValidationTests（可反证门）、IPC golden（schema 契约）。

### Out of Scope

- SharpFuzz LenientArgs 模糊测试（夜间任务，中 ROI，推迟）。
- Coyote 并发模型检查（先 1-2 天 spike 验 net10 兼容，不直接进路线图）。
- GUI E2E 升级（ADR 0010 已覆盖；生态已迁 embedded driver，仅记录不重立）。
- fnox 式"profiles.json 可安全入库 export"（产品特性）。
- System.CommandLine 迁移（仍非目标）。

### Further Notes

- 调研坦承：Windows 环境变量声明式管理无主导者、注册表多值原子性无 OS 原语、Tauri IPC 级 E2E 无成熟样板——三处是本项目可定义范式的空白。
- 已知缺口：Coyote net10 兼容未实测、Windows CI Docker 可用性未核验、Stryker v5 时间线模糊（均已在票内标注为首步验证项）。
- 原始 backlog（profile create --help 解析、ValidateProfiles 悬空 launch target 硬阻断、CliRuntime 拆出、CI 用户态隔离、architecture.md canary/golden 段、注册表残留 EM_TEST_DST）仍未立票，属独立决策，不并入本波。
