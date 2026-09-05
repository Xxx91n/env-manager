# 返修报告 — 测试基建串行化：静态 seam 跨集合并行竞态（票 14/18/19 联合返修）

- 窗口：联合返修票独立执行窗口（launcher: .scratch/architecture-recovery/prompts/engine-test-seam-serialization-fix.md）
- 日期：2026-09-05
- Blocked by：无。大脑 CI 复核发现全栈首跑红（run 33961788030）。
- 开工第一动作：先再质检大脑诊断，属实后动手。结论：属实，动手。修复完成，单一提交落 GitButler 分支，未 push。

## 1. 大脑三条已证事实的再质检（声明 → 证据 → 结论）

| # | 大脑声明 | 本窗口当场证据（只读复验） | 结论 |
|---|---|---|---|
| 1 | MutationSurvivorTriageTests.cs 与 ProfileSeamValidationTests.cs 均无 [Collection]，集合外裸奔 | 两文件类声明逐行读：MutationSurvivorTriageTests.cs:18 `public class MutationSurvivorTriageTests : IDisposable` 之上仅有 XML 注释、无任何属性；ProfileSeamValidationTests.cs:19 同。`rg -n "Collection" tests/ --type cs` 对两文件零命中（仅命中 DifferentialOracle/L1Container/CliSnapshotSerial/LocalAppDataRedirectSerial 集合成员） | 证实 |
| 2 | Program.SetProfilesFilePathForTests 调用类清单 = MutationSurvivorTriageTests(:28/:34)、ProfileSeamValidationTests(:34/:42)、CliOutputSnapshotTests(:436/:443)，且仅 CliOutputSnapshotTests 挂集合 | `rg -n "SetProfilesFilePathForTests|SetAppDataDirectoryForTests" tests/ --type cs`（编辑前）：SetProfilesFilePathForTests 调用点恰为上述 3 类 6 处；SetAppDataDirectoryForTests 仅 MutationSurvivorTriageTests(:27/:33)。src 侧定义确认：`static string? _profilesFilePathOverride`（src/ProfileStorage.cs:122-129 区域，partial class Program）+ `static string? _appDataDirectoryForTests`（src/CliRuntime.cs:262-271 区域）——确为静态全局指针 | 证实（补充：LocalAppDataRedirectTests.cs:16 仅注释引用不调用 seam，自带 "LocalAppDataRedirectSerial"（DisableParallelization=true）集合，不在本票范围；SetAudit*PathForTests 仅 CliOutputSnapshotTests 的 TempProfileDir 调用，随本票一并被集合覆盖） |
| 3 | CI 失败证据 run 33961788030 可用 gh run view 只读查看 | `gh run view 33961788030 --json status,conclusion,headBranch` → conclusion "failure"，headBranch "arch/ci-integration-first-run-fix"，title "fix(test): preserve CLI stderr on failure and stamp round-trip name (issue 22+24)"。`gh run view 33961788030 --log-failed` → verify/Run C# engine unit tests 步骤：`ProfileSeamValidationTests.Preflight_GlobalInheritsPlainGlobal_Accepted [FAIL]`、`System.InvalidOperationException : Sequence contains no matching element`、`at ...ProfileSeamValidationTests.cs:line 140`；行 140 实物 = `var child = Program.LoadProfiles().First(p => p.Name == "EM_T04_global_child")`（LoadProfiles 读错文件 → 列表无该 profile） | 证实 |

### 诊断附注（不改变返修范围与结论，仅记录精度）

1. 大脑"其中仅 CliOutputSnapshotTests 与 ProfileCreateHelpTests 挂 CliSnapshotSerial"漏列 MutationSurvivorTriageStdoutTests（:14 同为成员）——小失实，无碍诊断。
2. 大脑"DisableParallelization 只串行化集合内部"表述不精确：xUnit v2 源码（github.com/xunit/xunit 分支 v2，src/xunit.execution/Sdk/Frameworks/Runners/XunitTestAssemblyRunner.cs）将 DisableParallelization=true 的集合全部收集进 nonParallel 列表，逐个 `await taskRunner(task)` 串行跑完，之后才并行启动其余集合——即此类集合与其它任何集合都绝不并发。根因（18/19 两集合外裸奔并发翻转静态指针）不受影响；该语义反而保证本修复是彻底的（串行集合运行期间无任何并发类可翻转 seam，串行阶段结束后 seam 已被各集合 Dispose 置 null）。
3. 项目 xUnit 为 2.9.3（tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj:12），v2 语义适用。

## 2. 返修项逐条对照表（改了什么、为何）

| 返修项 | 改动内容 | 位置 |
|---|---|---|
| 1：把所有调用 SetProfilesFilePathForTests / SetAppDataDirectoryForTests 的测试类全部纳入同一串行集合（单一 CollectionDefinition + DisableParallelization=true；可与现有 CliSnapshotSerial 合并或新建） | **并入现有 "CliSnapshotSerial"（选择合并而非新建）**：在 ProfileSeamValidationTests 与 MutationSurvivorTriageTests 类声明前各插入 `[Collection("CliSnapshotSerial")]` 一行；CliOutputSnapshotTests 本已在集合内，无需迁移，仅将 CollectionDefinition 的 summary 注释由"Serializes Console redirection across the snapshot suite."扩写为覆盖全部进程全局静态翻转类（Console 重定向 + Program 静态 seam）并注明 issue 14+18+19 | ProfileSeamValidationTests.cs:19（新行，类声明原 :19 → :20）；MutationSurvivorTriageTests.cs:18（新行，类声明原 :18 → :19）；CliOutputSnapshotTests.cs:425-429（注释块 1 行 → 5 行，CollectionDefinition 本体 :430-431 未动） |
| 2：保持每类自己的 per-test 临时目录与 finally 置 null 纪律不变 | 未触碰任何 ctor / Dispose / 测试方法：两类的临时目录创建与 Dispose 置 null 原样保留 | 零改动 |
| 3：不改被测代码行为 | src/ 下任何文件零改动；测试逻辑零改动（仅类属性与注释） | — |
| 4：版本控制只走 GitButler，提交到新分支 arch/engine-test-seam-serialization-fix | 单一提交 mtk，只含本票 3 个测试文件；未 push、未建 PR、未触碰其它分支与 git index 中他人在途状态（fuzz 重命名 D/?? 等原样保留） | 见 §4 |

**为何合并而非新建集合**：现有 CliSnapshotSerial 已含 CliOutputSnapshotTests（其 TempProfileDir 本身就是 SetProfilesFilePathForTests/SetAudit*PathForTests 的调用者）与两个 stdout 捕获类，集合实质即"进程全局静态翻转类"串行桶；新建集合需把 CliOutputSnapshotTests 迁出、留下一大一小两个并行集合，diff 更大且无任何隔离收益（xUnit v2 下所有 DisableParallelization 集合均逐个串行、互相绝不并发）。合并仅 2 行属性 + 1 处注释，改动最小、语义自洽。

## 3. 修复后自检记录

- **rg 全量枚举（编辑后，当场输出）**：
  `rg -n "SetProfilesFilePathForTests|SetAppDataDirectoryForTests" tests/ --type cs` → 调用类 = ProfileSeamValidationTests(:35/:43)、CliOutputSnapshotTests(:440/:447)、MutationSurvivorTriageTests(:28/:29/:34/:35)；LocalAppDataRedirectTests(:16) 仅注释。
  `rg -n "CliSnapshotSerial" tests/ --type cs` → 单一 CollectionDefinition（CliOutputSnapshotTests.cs:430-431，DisableParallelization=true）+ 5 个成员：CliOutputSnapshotTests(:21)、ProfileSeamValidationTests(:19)、MutationSurvivorTriageTests(:18)、MutationSurvivorTriageStdoutTests(:14)、ProfileCreateHelpTests(:16)。
  **计数结论：seam 调用类 3/3 全部在同一串行集合内；集合定义恰 1 个。**
- **字节/格式完整性（编辑前后比对，node 脚本当场输出）**：三文件编辑前后 cr=0（纯 LF）、无 BOM（EF BB BF 不存在）、无截断（ProfileSeamValidationTests.cs 18199→18233 B、MutationSurvivorTriageTests.cs 6208→6242 B、CliOutputSnapshotTests.cs 18009→18335 B，增量与 diff 一致）；`git diff --check` 输出 "diff-check OK"；diff 内容 = 两行属性插入 + 一处注释替换，无任何越界改动。
- **CI-only 纪律**：本窗口未运行 dotnet build/test、vitest、cargo、stryker 等任何构建或测试；全部验证为 rg/git/gh/od 等只读静态核验。

## 4. 提交证据（but 输出实录）

依赖解析（首次一次性提交被原子拒绝，未产生任何提交/分支）：

```
$ but commit -b arch/engine-test-seam-serialization-fix -m "test(engine): serialize static-seam test classes into one parallelization-disabled collection (issue 14+18+19)" sm:3 oz:4 ss:e
Error: Cannot commit: 1 change could not be applied:
  tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs
    line 18 depends on arch/18-mutation-survivor-triage (uro)
Hint: ... but branch new arch/engine-test-seam-serialization-fix --anchor arch/18-mutation-survivor-triage
```

按技能依赖冲突流程创建锚定分支（sibling 叠于依赖根 arch/18）：

```
$ but branch new arch/engine-test-seam-serialization-fix --anchor arch/18-mutation-survivor-triage
Created branch 'arch/engine-test-seam-serialization-fix' above branch 'arch/18-mutation-survivor-triage'
```

提交成功（单一提交，只含本票 3 文件）：

```
$ but commit -b arch/engine-test-seam-serialization-fix -m "test(engine): serialize static-seam test classes into one parallelization-disabled collection (issue 14+18+19)" sm:3 oz:4 ss:e
Created commit mtk on branch 'arch/engine-test-seam-serialization-fix'
```

```
$ but show mtk
Commit:    dfa54b22f94532e1d517018a5807ca82356cfccd
Change-ID: mtkwvkkszspxznzpttmyqwknnrqyumzm
test(engine): serialize static-seam test classes into one parallelization-disabled collection (issue 14+18+19)
Files changed:
  M tests/EnvManager.Engine.Tests/CliOutputSnapshotTests.cs
  M tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs
  M tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs
```

but status 复核：新分支 en [arch/engine-test-seam-serialization-fix]（单提交 mtk）位于 mu [arch/18] 之上、pr [arch/19] 之下，栈内其它分支提交序列未变；zz 未提交区仅剩他窗口的 .zcode/plans/plan-sess_46e8b7d3…md（未触碰）；git status --short -- tests/EnvManager.Engine.Tests/ 为空（本票 3 文件已全部入提交，无残留）。

## 5. 剩余前置与交接

- 全栈 CI 复跑（PR #44 修正链）由大脑触发取绿；本窗口遵循 CI-only 纪律不本地自证、不 push。
- 分支未推送（WORKFLOW §4.2）。
