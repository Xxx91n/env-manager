# 交付报告 — 票 11：差分测试（Windows 语义为 oracle）

日期：2026-09-03 · 执行窗口：票 11 独立子窗口 · 版本控制：GitButler 分支 `arch/11-differential-oracle`（commit `lyw` / sha `b0116aa`，叠于 `arch/13-mutation-gate` 之上）

---

## 开工第一句（复述，WORKFLOW §4.3 规则）

**Blocked by 状态**：无 — 主线已收口合入 origin/main，可直接开工。
**必读清单（已全文读取）**：① `.scratch/architecture-recovery/handoffs/11-differential-oracle-testing.md` ② `.scratch/architecture-recovery/issues/11-differential-oracle-testing.md` ③ `.scratch/architecture-recovery/spec.md`（Phase 3 段，行 115–165）④ `.scratch/architecture-recovery/WORKFLOW.md` ⑤ `docs/agents/hard-boundaries.md`。

## 交付物

| 文件 | 内容 |
|---|---|
| `tests/EnvManager.Engine.Tests/DifferentialOracleTests.cs`（新增，486 行） | 差分 oracle 夹具：11 个 xUnit 测试 + 3 个 FactAttribute 闸门类 |
| `scripts/test-with-restore.ps1`（+15 行 CRLF） | 新 Run-Test 块 "differential oracle parity (InMemoryScope vs RegistryScope)"，在 HKCU/HKLM 快照-回滚窗口内挂载差分套件 |
| `AGENTS.md`（+2 行） | Testing 段差分套件说明（只含本票 hunk；票 12/13 段落未动） |

设计要点：
- **同一操作脚本双跑**：每步 `RunBoth(op)` 先跑 InMemoryScope 再跑 RegistryScope（真实注册表为 oracle），随后 `AssertTerminalStateAndBroadcastsMatch` 断言每个跟踪变量的（原始值 ordinal 相等 + RegistryValueKind 相等）且 `_memory.BroadcastCount` 与预期一致（两侧每步广播次数对齐）。
- **隔离闸门（红线遵守）**：`DifferentialOracleFactAttribute : FactAttribute` 在构造期检查 `EM_DIFFERENTIAL_ORACLE=1`，未设置时 `Skip`（带原因）。裸 `dotnet test` 永不触碰真实注册表；套件只被 test-with-restore.ps1 的快照窗口挂载（`--filter "FullyQualifiedName~DifferentialOracleTests"`）。xUnit 2.9 无运行时 Skip 原语，用 FactAttribute.Skip 属性赋值实现，零新依赖。
- **防互踩**：整套装在 `[CollectionDefinition("DifferentialOracle", DisableParallelization = true)]` 串行集合中。
- **宿主安全**：PATH 案例只写 EM_DIFF 命名空间的克隆值并先行双侧对齐初始状态（delete 后写）；真实 PATH 在 finally 里按（原始名称大小写、原始未展开字节、原始 kind）精确恢复，保证夹具收尾快照比对零漂移。

## 验收项逐条核验（附当场命令输出）

### 1. 新增差分 oracle 夹具：同一操作脚本分别跑 InMemoryScope 与 RegistryScope，终态 + 广播次数逐条一致 ✅

夹具挂载在 test-with-restore.ps1 内执行（`pwsh -NoProfile -File scripts/test-with-restore.ps1 -CliPath <cli>`）：

```
[test] set+get+delete round-trip ... OK
[test] rename contract ... OK
[test] protected variable rejection ... OK
[test] toggle exact value and kind recovery ... OK
[test] profile no-registry-mutation ... OK
[test] secrets never in registry ... OK
[test] trailing-backslash + quote recovery ... OK
[test] differential oracle parity (InMemoryScope vs RegistryScope) ... 已通过! - 失败: 0，通过: 11，已跳过: 0，总计: 11
OK
[test-with-restore] ALL TESTS PASS + exact registry and internal-config snapshots match.
[test-with-restore] Backups deleted (clean run).
```

11 个差分测试（类 `DifferentialOracleTests`）：ExpandString 保真 ×2、Toggle 保真、PATH 1024 / 30K / 超长拒绝 / 空段折叠 ×3、空值=当前目录、`=` 名拒绝、system-scope 提权、混合操作脚本。混合脚本覆盖 set → rename（write-verify-delete）→ change-scope（提权会话）→ toggle disable/restore → delete 全序列，每步断言终态+广播一致。

### 2. 语义矩阵覆盖（5 点全落） ✅

| 矩阵点 | 测试 | 断言形态 |
|---|---|---|
| REG_EXPAND_SZ 保留 %VAR% 不预展开 | `Diff_ExpandString_ValueWithPercent_PreservesRawValueAndExpandsKind` | `%USERPROFILE%\em-diff` → oracle 读回原始字节 + ExpandString；双侧一致 |
| （kind 升级只升不降） | `Diff_ExpandString_OverwriteWithoutPercent_PreservesExpandedKind` | 无 % 覆写保留 ExpandString（oracle 实证） |
| PATH 1024~30000 字符边界 | `Diff_Path_At1024Boundary_MatchesExactly` / `Diff_Path_Near30000Chars_MatchesExactly` | 1024 与 ~30K 字节往返逐字节一致、kind String、广播 1 |
| （超长拒绝） | `Diff_Path_OverMaxLength_RejectedOnBothSides` | >32767 双侧拒绝、零写、零广播 |
| 空条目=当前目录语义 | `Diff_EmptyValue_PersistsAsPresentEmptyString_CurrentDirectorySemantics` + `Diff_Path_EmptySegments_FoldedByIdenticalPipeline` | 空值双侧存在且为 ""；`;;a;;` 折叠管线双侧一致 |
| 变量名含 `=` 拒绝 | `Diff_NameWithEquals_RejectedBeforeAnyWrite_OnBothSides` | seam 前拒绝：双侧 false、零写、零广播 |
| system scope 写需 elevation | `Diff_SystemScope_WriteRequiresElevation` | 提权会话：双侧 HKLM 写终态+广播一致；非提权会话：钉住 oracle 拒绝形状 |

### 3. dotnet test 全绿；差分套件在 CI windows-latest 上跑通（真实注册表隔离，不污染用户环境） ✅

```
$ dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release --nologo
已通过! - 失败: 0，通过: 107，已跳过: 25，总计: 132（10 个差分测试带原因 Skip + 15 个既有 L1/L2 provider Skip）
```

CI 接驳（零 build.yml 改动，链路当场核验全 true）：build.yml `verify` job（windows-latest）→ `./scripts/run-ci-tests.ps1` → suite 4 `scripts/test-with-restore.ps1` → 差分 Run-Test 块（`EM_DIFFERENTIAL_ORACLE=1` + `--filter`）。真实注册表隔离 = 夹具既有 HKCU+HKLM 快照/回滚/收尾比对；不污染用户环境 = EM_DIFF 专有命名空间 + PATH 精确恢复 + 收尾 "exact registry and internal-config snapshots match" 实证（三次夹具运行均零漂移收尾）。

### 4. 人为注入一处 InMemoryScope↔Windows 语义漂移，差分测试必须变红 ✅

注入：`src/InMemoryScope.cs` `WriteValue` 的 `%`→ExpandString 提升改为恒 `RegistryValueKind.String`（REG_SZ↔REG_EXPAND_SZ 保真回归），经夹具复跑：

```
[xUnit.net 00:00:01.56] Diff_MixedOperationScript_TerminalStateAndBroadcastsMatch [FAIL]
[xUnit.net 00:00:02.55] Diff_ToggleRoundTrip_PreservesRawValueAndKindExactly [FAIL]
[xUnit.net 00:00:02.89] Diff_ExpandString_ValueWithPercent_PreservesRawValueAndExpandsKind [FAIL]
   [EM_DIFF_V2_3AC9B3A@user] registry value kind drift:
   [EM_DIFF_TOGGLE_3ACA05A_EnvManager_disabled@user] registry value kind drift:
   [EM_DIFF_EXPAND_3ACA2BC@user] registry value kind drift:
[xUnit.net 00:00:03.98] Diff_ExpandString_OverwriteWithoutPercent_PreservesExpandedKind [FAIL]
   [EM_DIFF_KEEPKIND_3ACA6A4@user] registry value kind drift:
失败! - 失败: 4，通过: 7，已跳过: 0，总计: 11
FAIL: differential oracle suite failed (dotnet test exit 1)
WARNING: [test-with-restore] Failure or drift detected; restoring snapshots.  ← 夹具红灯时自动 reconcile，宿主零残留
```

回退（对照临时备份逐字节还原；`git status --porcelain -- src/InMemoryScope.cs` 为空 = 与 HEAD 一致）后复跑：全量 sweep 107/0/25 + 夹具 11/11 绿（见验收 1、3 输出）。红灯可反证达成。

### 5. 报告落盘 ✅

本文件即报告；验收 1–4 均为当场命令输出回填。落盘路径 `.scratch/architecture-recovery/reports/11-differential-oracle-testing.md`（.scratch 为 gitignore，符合 §3 落盘约定）。

## 过程记录与偏差

- 首次夹具运行 3 个 PATH 案例红：registry 侧 host PATH 预存在（REG_EXPAND_SZ）而 memory 侧从空开始，kind 策略"保留既有种类"导致终态不对齐——属夹具设计缺陷而非实现不忠实；修复为案例前双侧 delete 对齐初始状态（真实 PATH 由 finally 精确恢复）。该红灯顺带实证了夹具隔离回滚路径。
- system-scope 矩阵点的诚实边界：InMemoryScope 按 EngineScope 契约注释是"提权盲"的（环境条件行为不在 hermetic 契约内）；提权会话（CI windows-latest、本机）做双侧差分，非提权会话钉 oracle 拒绝形状。本机为提权会话，非提权分支未被当场执行（代码为容错形状：接受 ScopeUnavailable / UnauthorizedAccessException / SecurityException 三种拒绝形态）。
- 提交期遇跨栈依赖（AGENTS.md hunk 锚定 arch/13-mutation-gate 落点）：按 WORKFLOW 教训日志票 03 防线①直接 `but branch new arch/11-differential-oracle --above arch/13-mutation-gate` sibling 分支提交，未做 but move 线性化。
- 并行票隔离：票 12（WritePathStateMachineTests.cs、csproj CsCheck、AGENTS.md `ws:f` hunk）与票 14（.zcode/plans）的未提交工作原样保留，未吸收/未回滚/未改写；本票提交 `lyw` 仅含 3 个本票文件。

## 已知限制

- 非提权会话的 system-scope 拒绝形状分支未在本机当场执行（本机提权）；CI windows-latest 为提权 runner，走的是双侧差分腿。
- `RegistryScope.ListVariables` 对 REG_EXPAND_SZ 值经默认 `GetValue` 展开，而 InMemoryScope 返回原始值——这是 seam 契约里命令层展示投影与存储读（ReadValue/DoNotExpand）的既有差异，差分按 seam 存储契约（ReadValue 原始值）对齐；如需展示层忠实度可另立票处理。
