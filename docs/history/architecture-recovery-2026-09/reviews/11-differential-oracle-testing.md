# 复核报告 — 票 11：差分测试（Windows 语义为 oracle）

日期：2026-09-04 · 复核方式：独立子代理只读取证 + 大脑会话当场复跑测试门 · 结论：✅ 可验收

## 声明 → 证据 → 结论

| 声明（子窗口报告） | 证据（仓库实物） | 结论 |
|---|---|---|
| DifferentialOracleTests.cs 约 486 行、11 测试 + 隔离闸门 | 文件存在，实为 485 行；11 个 `Diff_*` 全挂 `[DifferentialOracleFact]`（构造期查 `EM_DIFFERENTIAL_ORACLE`，否则 Skip，行 25-34） | 属实（行数差 1，计数口径） |
| test-with-restore.ps1 新增差分 Run-Test 块 | scripts/test-with-restore.ps1:517-528：`EM_DIFFERENTIAL_ORACLE=1` + `--filter "FullyQualifiedName~DifferentialOracleTests"` + finally 清除 | 属实 |
| 语义矩阵 5 点全落 | REG_EXPAND_SZ 保留 %VAR%（行 141）、PATH 1024/29999/超 32767 拒绝（208/219/272）、空条目+`;;`折叠（307/326）、`=` 名拒绝（337）、system elevation（354） | 属实 |
| 红灯反证：注入漂移 4 FAIL、回退复绿 | `git status --porcelain -- src/InMemoryScope.cs` 为空（注入已回退干净）；src/InMemoryScope.cs:80-114 保留正确 kind 提升逻辑；b0116aa 不含该文件（注入未入库） | 属实（静态链完整） |
| 隔离与宿主安全 | `[CollectionDefinition("DifferentialOracle", DisableParallelization = true)]`（行 40/67）；EM_DIFF_ 命名空间 + CapturePathOriginal/RestorePathOriginal 精确恢复（452-484） | 属实 |
| AGENTS.md +2 行 | `git show b0116aa -- AGENTS.md` = +2；工作树 AGENTS.md:174 差分段落完好 | 属实 |
| 分支 arch/11 叠于 arch/13 之上 | but status：`di [arch/11-differential-oracle]`（lyw=b0116aa）下挂 `mu [arch/13-mutation-gate]`（nqt=64d4db5），父提交一致 | 属实 |

## 大脑当场复跑（本票最硬证据）

- `pwsh -NoProfile -File scripts/test-with-restore.ps1` → 差分 oracle 块 **11/11 通过**，`ALL TESTS PASS + exact registry and internal-config snapshots match`，`Backups deleted (clean run)`。闭合了子代理因禁跑约束未能独立复验的"红灯→复绿"运行证据（EM_DIFFERENTIAL_ORACLE=1 实跑）。
- `dotnet test -c Release` → 131 通过 / 20 跳过 / 0 失败（差分 11 例带原因 Skip，符合隔离闸门设计）。

## 结论

7 项关键声明全部有实物支撑，运行期声明由大脑当场实跑补证。✅ 可验收。
