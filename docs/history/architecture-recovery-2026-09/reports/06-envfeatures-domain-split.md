# 票 06 交付报告 — EnvFeatures.cs 五域分家并退役该名字

日期：2026-09-02　窗口：票06 子窗口　版本控制遵循 WORKFLOW §4.2（GitButler，未 push、未建 PR）

## 开工复述（prompts/06 要求）

- Blocked by：05。已收口（.scratch/architecture-recovery/reviews/05-command-module-extraction.md 在盘），本票 ready-for-agent 成立。
- 必读清单 6/6 已读：handoffs/06、issues/06、spec.md、WORKFLOW.md、docs/agents/hard-boundaries.md、reports/05-command-module-extraction.md。

## 验收项逐条核验（issues/06，全部当场复跑）

### 1. EnvFeatures.cs 按域拆分为独立命名模块（audit/history、expand、bulk、dpapi；native-methods 核对无遗留）— PASS

src/EnvFeatures.cs（866 行）全部成员按域搬迁，文件删除。落点（成员逐字搬迁，行为零变化）：

| 目标模块 | 搬入成员 |
|---|---|
| src/AuditCommand.cs（audit/history 域，119→232 行） | class AuditEntry、MaxAuditEntries、AuditFilePath、LoadAuditHistory、RunHistoryCommand、RecordSnapshotDiff |
| src/ExpandCommand.cs（新建，32 行） | ExpandPattern、RunExpand |
| src/BulkCommand.cs（新建，160 行） | class BulkVariable、RunBulkCommand、ReadScopeVariables、ValidateInterchangePath、ReadBulkFile、ParseEnvLine、ParseCsvLine、ParseCsvFields、WriteBulkFile、QuoteEnv、Csv |
| src/DpapiHelper.cs（新建，99 行） | 整个 internal static partial class DpapiHelper（DATA_BLOB、CryptProtectData/CryptUnprotectData、EncryptSecret、DecryptSecret） |
| src/NativeMethods.cs（91→98 行） | internal static partial class NativeMethods{ LocalFree }（票05 native-methods 域无回流，反向归并 DpapiHelper 所需的 LocalFree interop） |
| src/ProfileCommand.cs（1260→1552 行） | ValidateLaunchTarget、ResolveProfileVariables/ResolveProfile/ResolveProfilePaths/ResolvePaths/ResolveProfilePathsWithScopes/ResolvePathsWithScopes/ResolveProfilePathsWithSource/ResolvePathsWithSource/ResolveProfileVariablesWithSource/ResolveProfileWithSource、ProfilePreview、HasInheritanceCycle、ProfileSetInherits、ProfileAddPath、ProfileRemovePath、ValidatePathFragment |
| src/PathCommand.cs（488→507 行） | NormalizePathEntry、StripVerbatimPrefix |
| src/VariableWrite.cs（246→256 行） | ValidateVariableInput |
| src/AuditLedgerMigration.cs（358→371 行） | SafeSlice（唯一消费者同文件归并） |
| src/ProtectionCommand.cs（182→255 行） | class ProtectionDefaults、BuiltinProtectedVarsFile、BuiltinProtectedPathsFile、LoadProtectionDefaults、LoadBuiltinProtectedVars、LoadBuiltinProtectedPaths |
| src/Program.cs（347→441 行） | AppDataDirectory、IsWriteInvocation、AcquireMutationLock、CaptureEnvironmentSnapshot、CaptureScope、AtomicWriteJson、WriteAtomicUtf8（Main 分发共享运行时基础设施，留根模块） |

现场证据（报告落盘前当场复跑）：

```
$ ls src/EnvFeatures.cs  → 不存在（fs.existsSync = false）
$ 行数盘点: Program.cs 441 / ProfileCommand.cs 1552 / PathCommand.cs 507 / AuditCommand.cs 232 /
  ProtectionCommand.cs 255 / VariableWrite.cs 256 / AuditLedgerMigration.cs 371 / DpapiHelper.cs 99 /
  ExpandCommand.cs 32 / BulkCommand.cs 160 / NativeMethods.cs 98
$ 关键片段探针: ProfileSetInherits→ProfileCommand true / LocalFree→NativeMethods true /
  AcquireMutationLock→Program.cs true
```

检查点纪律：A（DPAPI+NativeMethods）→ B（expand）→ C（bulk）→ D（audit/history）→ E（protection）→ F1（profile/path/variable-write/ledger 归并）→ F2（infra 归并+删文件），每检查点 dotnet build + dotnet test 全绿才继续，各点均 86/86、8 条预存警告（基线一致）。中途两次红灯（BulkCommand 缺 using 三连、SafeSlice 重复定义 CS0111）均当场修复后复绿；EnvFeatures.cs 搬运前原文已备份 OS temp（envman-ticket06-preref/EnvFeatures.cs.preA，45299 字节）。

### 2. "EnvFeatures" 名字代码内退役（类型/文件名清零，rg 验证）— PASS

```
$ rg -n "EnvFeatures" src docs frontend/src AGENTS.md README.md
→ 仅剩 11 处历史出处注释（"moved verbatim from EnvFeatures.cs" 类）与 AGENTS.md 结构树一行
  "# EnvFeatures.cs retired (issue 06 split)" 说明文字；
  类型/文件/符号引用清零（ProtectionCommand.cs 原 "see EnvFeatures.cs" 活指针已改为 see ProtectionCommand.cs）
```

### 3. 全部 dotnet test 与集成脚本绿灯 — PASS

```
$ dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj
已通过! - 失败: 0，通过: 86，已跳过: 0，总计: 86（基线 86/86 → 收尾复跑 86/86）
$ dotnet build -c Release -v q   → 8 个警告（预存基线），0 个错误
$ 刷新 release/cli-only 4 产物（exe/dll/deps.json/runtimeconfig.json，mtime 校验一致）
$ pwsh -NoProfile -File scripts/run-ci-tests.ps1 -CliExe release/cli-only/env-manager-cli.exe
launch-env-injection: Tests Passed: 6, Failed: 0
canary-redaction:     Tests Passed: 9, Failed: 0
inheritance-protection: PASS (4/4)
test-with-restore:    ALL TESTS PASS + exact registry and internal-config snapshots match（7 项）
=== CI test tier PASSED ===（退出码 0；完整日志 OS temp ticket06-run-ci-tests.log）
```

### 4. 前端门禁测试引用路径同步 — PASS

```
frontend/src/lib/v0.7-secrets.test.ts:79 readFileSync 路径 src/EnvFeatures.cs → src/DpapiHelper.cs
frontend/src/lib/components/ProfilePage.svelte:199 注释 EnvFeatures.ValidateLaunchTarget → ProfileCommand.ValidateLaunchTarget
$ cd frontend && npx vitest run
Test Files  40 passed (40)
     Tests  398 passed (398)
```

### 5. 文档同步 — PASS（AGENTS.md 部分内容在工作区，见"parked hunk 披露"）

- AGENTS.md：结构树 EnvFeatures.cs 行退役并新增 ExpandCommand.cs / BulkCommand.cs / DpapiHelper.cs 行；Program.cs 行改"Thin Main dispatch + shared runtime infra"；AuditCommand/PathCommand 行补 issue 06 成员；"CLI backend" 小节与 "<400 lines" 表述按实态改写（Program.cs 现 441 行，共享运行时基础设施留根模块，不再虚标 <400）。
- docs/architecture.md：DpapiHelper 活指针 src/EnvFeatures.cs → src/DpapiHelper.cs；Phase 2+ provider interface 展望指向同步。
- docs/agents/hard-boundaries.md：三条活指针更新（ProfileSetInherits → src/ProfileCommand.cs；ResolveProfilePathsWithScopes/ResolvePathsWithScopes → src/ProfileCommand.cs；LoadAuditHistory → src/AuditCommand.cs）。守卫语义原文未动。
- docs/agents/reference-index.md：rg 核对无 EnvFeatures 引用，无需改。

### 6. codegraph sync — PASS

```
$ codegraph sync .
* Synced 14 changed files
• Added: 3, Modified: 10, Removed: 1 - 322 nodes in 2.0s
$ codegraph status . → [OK] Index is up to date
```

## 版本控制（WORKFLOW §4.2；票03/05 防线执行记录）

按票 05 同款双分支拓扑（跨栈依赖一律 sibling 分支，未使用 but move/undo 类历史改写操作）：

- **arch/06-envfeatures-domain-split**（叠于 arch/05-command-module-extraction，stack2）：
  - qrw refactor(engine): aggregate audit/history, profile-resolution, and PATH helper members into their command modules (issue 06) —— AuditCommand.cs、ProfileCommand.cs、PathCommand.cs、NativeMethods.cs
- **arch/06-envfeatures-domain-split-b**（叠于 arch/05-command-module-extraction-b，stack1）：
  - wxr refactor(engine): add expand, bulk, and DPAPI-helper domain modules (issue 06) —— ExpandCommand.cs、BulkCommand.cs、DpapiHelper.cs
  - tzu refactor(engine): retire EnvFeatures.cs, consolidating dispatch infra and remaining domain members (issue 06) —— EnvFeatures.cs 删除、Program.cs、VariableWrite.cs、ProtectionCommand.cs、AuditLedgerMigration.cs
  - xun test(gui): re-point DPAPI P/Invoke gate to src/DpapiHelper.cs (issue 06) —— v0.7-secrets.test.ts、ProfilePage.svelte
  - myq docs(engine): sync live pointers for EnvFeatures.cs retirement (issue 06) —— architecture.md、hard-boundaries.md

文件→栈归属经 git log 逐文件核验后落位（5355da6=stack2 vnk / 3160c32,c9a08d9,a496f21,e648fd7=stack1）。提交后完整性探针：git status 仅剩 .gitignore+AGENTS.md；关键片段探针全过；build+test 复跑 86/86。

## Parked hunk 披露（票03 防线③，供大脑合并期 fold）

- **AGENTS.md（ws，4 hunk）**：票 05 的 4 个跨栈 parked hunk 仍整体未提交；本票的结构树/小节编辑与 parked hunk 同 hunk 区域交叠（GitButler 无法按更细粒度拆分），按"勿整体强提、勿破坏 parked 内容"整块留工作区，合并期随票 05 parked 内容一并 fold。本票 AGENTS.md 编辑内容以本报告"文档同步"小节为准。
- **.gitignore（pu，1 hunk）**：票 05 parked（+.scratch/），本票未触碰。
- 工作区文件完整性：两文件内容均为票 05 parked + 本票编辑的合并态，未做回滚/改写。

## 遗留风险 / 备注

1. 两分支必须都合入；建议先合 stack1 侧（-b：wxr/tzu/xun/myq）再合 stack2 侧（qrw），与票 05 合并顺序建议一致。
2. Program.cs 441 行超票 05 文档"<400 lines"表述——共享运行时基础设施（互斥锁/快照/原子写）留根模块导致；AGENTS.md 已按实态改写。如需压回 400 以内，可后续独立票拆 CliRuntime 模块（票 05 报告遗留备注 4 的同一选项）。
3. 行为零变化证据链：逐字搬迁 + 86/86 + vitest 398/398 + 集成四套件 6/6+9/9+4/4+7/7 + Release 构建产物探针；未做票 05 那种多命令逐字节 diff（本票为等义成员搬迁，测试面已覆盖四套件+86 单测）。
4. OS temp 备份：envman-ticket06-preref/EnvFeatures.cs.preA（搬运前原文）、ticket06-run-ci-tests.log（集成四套件完整输出）、ticket06-butdiff.txt（提交前 hunk 盘点）。
