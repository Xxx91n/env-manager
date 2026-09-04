# 票 12 交付报告 — 写路径状态机模型测试

> 子窗口回报 · 2026-09-03 · 遵循 WORKFLOW §4.2（GitButler 提交；已推 origin，用户授权，2026-09-04 收口修正）
> 全部证据为当场命令输出回填，非口头完成。

## 检查点核验

### A. 选库定案 — CsCheck 4.8.0 ✅

- `dotnet package search CsCheck --take 5 --format json` → latestVersion **4.8.0**（nuget.org，767,277 downloads）。
- 已加入 `tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`：
  `<PackageReference Include="CsCheck" Version="4.8.0" />`，`dotnet restore` 成功。
- **API 勘误（handoff 勘察段的现状修正）**：CsCheck 4.8.0 的 stateful API 形态是
  `GenOperation<Actual, Model>` + `Gen<(Actual,Model)>.SampleModelBased(...)`
  （从包内 `lib/net8.0/CsCheck.xml` 成员清单实证：28 个 GenOperation 成员，无 Machine 基类）。
  这就是 ticket 文案所指 Machine 模式的当代载体；spec Phase 3 的 "Machine<EngineState, ModelState>" 语义
  （模型与引擎同步推进 + 最小反例收缩）完全保留。FsCheck Machine API 标注 Experimental、无 semver
  的调研备注维持有效，不采用 FsCheck。

### B. Machine 骨架 + 6 操作 ✅

`tests/EnvManager.Engine.Tests/WritePathStateMachineTests.cs`（新文件）：

- 引擎侧：`TraceScope`（包装 InMemoryScope，逐 seam 操作记账，同 WritePathSeamTests.RecordingScope 形态），
  经 **RunSet / RunDelete / RunRename / RunChangeScope 命令核**（seam 参数化，合成保护谓词）与
  **GetPathEntriesCore / SetPathEntriesCore**（PathCommand add/remove 的语义镜像，经 seam 核）驱动。
- 模型侧：`ModelState` = `Dictionary<(Scope,string), string>`（大小写不敏感键）+ 广播计数。
- 6 操作 = Set / Delete / Rename / ChangeScope / PathAdd / PathRemove，
  `Gen.Frequency<WriteOp>` 加权生成（4/3/3/3/2/2），变量名 3 枚举 × 值 3 枚举 × scope 2 枚举，
  小字母表保证反例可读、收缩可达最小。

### C. 模型同步 + 最小反例收缩 ✅

- 每步操作同时推进引擎与模型；每步结束即比对（exit code + 终态 + 广播计数 + seam 操作序）；
  任何偏离抛异常，CsCheck 自动收缩初始状态 + 操作序列至最短最简。
- 绿基线（当场输出）：
  ```
  dotnet test ... --filter "FullyQualifiedName~WritePathStateMachine"
  已通过! - 失败: 0，通过: 1，已跳过: 0，总计: 1，持续时间: 646 ms - EnvManager.Engine.Tests.dll (net10.0)
  ```

### D. 人为"先删后写"验红灯（强制验收形态）✅

在 `src/VariableRename.cs` 把 write-verify-delete 精确反转为 delete-then-write
（语句块 splice，EOL 保留，事前全文备份 OS temp）：

- **红灯（当场输出，990 迭代预算内收敛，909 skipped）**：
  ```
  CsCheck.CsCheckException : Set seed: "dKqVynzqrI_4" or -e CsCheck_Seed=dKqVynzqrI_4 to reproduce (3 shrinks, 909 skipped, 1,000 total).
  Operations: [Set(...), ..., Rename(user:EMA->EMB,ow:False), ...]
  Exception: System.InvalidOperationException: rename write-verify-delete order violated for Rename(user:EMA->EMB,ow:False):
  expected 'write:EMB@user' before 'delete:EMA@user', seam ops were [read:EMA@user, read:EMB@user, delete:EMA@user, write:EMB@user, read:EMB@user, broadcast]
  ```
- **续收缩（喂 seed 复跑，1 shrinks，937 skipped）**，反例缩至 5 操作，违规步孤立于单个 Rename：
  ```
  Operations: [Rename(user:EMB->EMA,ow:True), Set(EMB=v2,system,ow:True), Set(EM_LOCKED_VAR=v2,user,ow:True), ChangeScope(EMA,system->system,ow:True), Rename(system:EMB->EMA,ow:False)]
  seam ops were [read:EMB@system, read:EMA@system, delete:EMB@system, write:EMA@system, read:EMA@system, broadcast]
  ```
  该窗口唯一可能的成因是 delete 先于 write —— 终态与广播计数无法区分此变异，seam 操作序窗口是唯一捕手（正合 issue 验收形态）。
- 复原：从备份整写回，`restored === backupContent` 字节一致，write 索引 < delete 索引复核通过；
  复跑同 filter 绿（778 ms）。备份临时文件已删除。

### E. 广播时机断言 ✅

- 机器每操作内联断言 `AssertBroadcastDelta`：实际写 → delta=1；拒绝/无操作 → delta=0；
  delete 不存在变量仍广播 1 次（registry 机制对齐，模型同规则）。
- rename/change-scope 的"单次广播在写序完成后"由成功路径 delta==1 + 操作序窗口联合钉住。
- 保护变量拒写（set/delete/rename/change-scope 四路）与保护 PATH 条目拒删均被模型域覆盖
  （EM_LOCKED_VAR / C:\em-locked-dir 注入生成器）。
- CI：verify job 已有 `dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release --nologo`
  （build.yml:91），新测试文件经 csproj 默认 glob 自动纳入 —— 无需改 CI。

## issue/12 验收单逐条

1. ✅ 状态机模型测试：6 操作（Rename/ChangeScope/Set/Delete/PathAdd/PathRemove），
   模型=字典 + 广播计数，CsCheck GenOperation/SampleModelBased（4.8 Machine 形态，见检查点 A 勘误）。
2. ✅ "先删后写"注入红灯 ≤1e3 迭代（909 skipped 收敛）+ 最小反例序列（收缩至 5 操作）。
3. ✅ 广播时机断言（apply/写路径：实际写广播恰 1 次，拒绝 0 次）；保护变量拒写四路 +
   rename write-verify-delete 顺序均被模型覆盖。
4. ✅ dotnet test 引擎测试套件全绿（新测试 + 既有测试；本票新增测试自身绿——见下方"并行工作区备注"）；
   新测试在 CI verify job（无需改动，glob 纳入）。
5. ✅ 本报告落盘（即本文件），每条验收附当场命令输出。

## 验证命令汇总（当场）

| 验证 | 命令 | 结果 |
|---|---|---|
| 还原+编译 | `dotnet build tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release` | 已成功生成 |
| 状态机绿 | `dotnet test ... --filter FullyQualifiedName~WritePathStateMachine` | 通过 1/1（646 ms；复绿 778 ms）|
| 突变红 | 同上（VariableRename.cs 注入 delete-then-write）| 失败 1/1（909 skipped；seed dKqVynzqrI_4；续收缩 seed 2gnC4yzYIlL2）|
| 全套件（票11落库前快照） | `dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release` | 106 通过 / 25 跳过 / 1 失败（失败=票 11 当时未提交的 DRIFT-INJECTED 变异所致，已由票 11 修复落库）|
| 全套件（票11落库后复跑，终态） | `dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release --nologo --filter "FullyQualifiedName!~CliOutputSnapshotTests"` | **已通过! 失败: 0，通过: 107，已跳过: 25，总计: 132（958 ms）**；归属见「终态复跑与失败归属」节 |
| 本票测试 + 票11回归靶（Toggle 原失败用例） | `dotnet test ... --filter "FullyQualifiedName~WritePathStateMachine\|FullyQualifiedName~WritePathSeam\|FullyQualifiedName~DifferentialOracle"` | 已通过! 失败: 0，通过: 24，已跳过: 11（含 WritePathSeamTests.Toggle_DisableThenRestore_ExactValueAndKind 复绿）|
| 空白检查 | `git diff --check` | clean |
| 产物构建 | `node scripts/build.mjs --arch x64 --skip-gui --skip-msi` | release/cli-only/env-manager-cli.exe 存在，[build] Done |
| CodeGraph | `codegraph sync .` | Done |

## 并行工作区备注（不阻塞本票）

- 工作区同时存在票 11（差分测试）与票 13/16 的并行分支工作（`but status` 可见：
  arch/13-mutation-gate、arch/16-adr-txr-txf-ban 已提交分支；未提交区含票 11 的
  DifferentialOracleTests.cs 与 InMemoryScope.cs 的 "DRIFT-INJECTED (ticket 11 checkpoint D)" 变异）。
- 全套件唯一失败 `WritePathSeamTests.Toggle_DisableThenRestore_ExactValueAndKind`（Expected ExpandString / Actual String）
  由该票 11 变异直接导致（%-promotion 被移除），与本票文件无交集；本票按 WORKFLOW §4.2 未触碰他票改动。
- 本票提交仅含：WritePathStateMachineTests.cs、EnvManager.Engine.Tests.csproj（CsCheck 包引用）、
  AGENTS.md（测试清单段落）、.scratch/issues/12 勾选、本报告。

## 终态复跑与失败归属（票 11 落库后，回应验收项 4 的弱验证）

票 11 修复落库（arch/11-differential-oracle 提交 lyw）后，大脑会话要求的终态全套件复跑结果：

- 直接跑 `dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release --nologo`
  → **失败 17 / 通过 108 / 跳过 25 / 总计 150**。
- 17 个失败**全部**是 `CliOutputSnapshotTests.*`（VerifyException：快照 verified.txt 与 received.txt 不一致）。
  该文件及 17 个 .received.txt 在 GitButler 状态中均为**未提交/未跟踪**（`but status`：
  `sm A tests/.../CliOutputSnapshotTests.cs` + 17 个 received.txt；分支 `arch/14-cli-output-snapshot-testing`
  标注 **no commits**）——归属**票 14 在途工作**（快照基线尚未生成），与本票交付物零交集。
- 排除该在途类后的全套件终态：`--filter "FullyQualifiedName!~CliOutputSnapshotTests"`
  → **已通过! 失败: 0，通过: 107，已跳过: 25，总计: 132，958 ms**。
- 本票回归靶 + 票 11 修复复绿靶联合跑：`--filter "FullyQualifiedName~WritePathStateMachine|FullyQualifiedName~WritePathSeam|FullyQualifiedName~DifferentialOracle"`
  → **已通过! 失败: 0，通过: 24，已跳过: 11**；其中票 11 落库前唯一失败的
  `WritePathSeamTests.Toggle_DisableThenRestore_ExactValueAndKind`（ExpandString kind）已复绿通过。
- 本票新增状态机测试在终态复跑中通过（1000 随机步模型同步）。

**结论**：本票验收项 4「dotnet test 全绿」在"本票归属面 + 既有已落库测试"意义上达成（0 失败）；
当前工作区全套件的非零失败全部归属票 14 未提交在途工作（快照测试基线未生成即被 vstf 发现），
按 WORKFLOW §4.2 本票不触碰他票改动，交大脑会话按 §4.4 对票 14 单独核验。

## 已知边界（诚实记录）

- **自改名排除**：`rename X X`（old==new）成功路径当前会经"写目标→验证→删源"把变量删掉（先写同名再删同名）。
  这是既有产品决策（write-verify-delete 字面执行于同一键），不在本票契约域；生成器只产生 old≠new 对。
  已在测试文件头注释与 AGENTS.md 段落记录，建议后续票立项处置。
- change-scope 同 scope 迁移（from==to）走 CLI 的 warning no-op 分支（exit 0，无广播），模型同规则。
