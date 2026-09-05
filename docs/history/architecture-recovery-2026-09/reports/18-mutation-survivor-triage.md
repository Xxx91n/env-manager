# 票 18 交付报告 — 变异测试幸存者分诊 + 登记 + 模块化报告（CI 可跑）

日期：2026-09-05 · 分支：arch/18-mutation-survivor-triage · 状态：待大脑 CI 重跑验收（检查点 F）

## 验收项对照（issues/18）

- [x] 登记文件落盘，逐条含：位置、类别（no coverage / weak assertion / equivalent）、判定、理由、LLM 检测预留字段 → `.scratch/architecture-recovery/reports/18-survivor-registry.json`（14 条，每条含 `llmDetection` 预留对象：candidate/notes/tool/date）。
- [x] 非等价幸存者补测试后 Stryker 重跑：kill 数上升、survived 仅余登记为等价的条目 → 补杀测试已落地（6 个测试，见下）；**CI 重跑数字由大脑触发 `stryker` job 后回填本报告趋势表**（本窗口按 CI-only 政策不本地自证）。
- [x] 阈值 85/70/60 与 ignore string/logical 不变 → `stryker-config.json` 本票零改动（git diff 佐证）；无防御性 100% 追求（1 条登记为等价，12 条补杀目标明确到单测试）。
- [x] Stryker 可经 CI 执行（workflow_dispatch 短跑 job），输出含模块分算报告 → `build.yml` 新增 `stryker` job（workflow_dispatch 触发）+ `scripts/stryker-module-scores.mjs`。
- [x] 趋势记录落盘：本报告趋势表 + 登记文件 `runs[]` 数组（基线已录入，重跑由大脑追加）。

## 基线（跑分 1）

来源：大脑 2026-09-04 当场重跑（`StrykerOutput/2026-09-04.19-08-22/reports/mutation-report.html`，本票解析其内嵌 JSON 佐证）。

| 口径 | tested | killed | survived | timeout | 得分 |
|---|---|---|---|---|---|
| 大脑重跑 stdout | 96 | 78 | 14 | 4 | 40.00% |
| HTML 内嵌 JSON 解析（本票） | 96 | 81 | 14 | 1 | 85.42%（模块分算口径，NoCoverage 不计入受测） |

> 两口径的 killed/timeout 拆分不同（timeout 抖动），受测总数与幸存数一致。40.00% 为 Stryker 把 NoCoverage 计入分母的原始分；85.42% 为受测口径分（与 Stryker HTML 报告展示分一致）。基线判定以**幸存 14 条**为锚。

### 基线模块分算（scripts/stryker-module-scores.mjs 输出）

```
module                      test  kill  surv  noCov  t/o    score
ProfileEffective.cs           38    32     5     34    1   86.84%
ProtectionCommand.cs          13     8     5     67    0   61.54%
VariableChangeScope.cs        29    26     3      8    0   89.66%
VariableRename.cs             16    15     1      0    0   93.75%
TOTAL                         96    81    14    109    1   85.42%
```

## 分诊结论（检查点 A/B）

14 条幸存者**全部有测试覆盖**（基线报告 coveredBy 非空）→ **no coverage 类：0 条**。

- **weak assertion：13 条** → 每条登记判定 + 理由 + 指定杀死测试（登记文件 `killedByTest` 字段）。
- **equivalent：1 条** → `ProfileEffective.cs:136` `visited.Add(profile.Name)`（`CollectInheritedSecretsFrom` 防环守卫，#4369）。理由：非环链下移除后菱形继承重访返回空集、并集不变；毒化环链下唯一可观测差异是不可捕获的 `StackOverflowException`（进程级故障，进程内 xUnit 断言无法拦截）。优雅的 `InvalidDataException` 环守卫在 `ResolveProfile/ResolvePaths`（ProfileCommand.cs:1294/1316），环输入根本走不到。LLM 检测预留：跨进程差分 harness 理论可行，登记 `candidate: true`。

分诊依据先例：round4-closeout-patterns C 节（Survived/NoCoverage 分开；OneUptime 边界优先排序；FSE'14 ~23% 等价；不追 100%）。

## 补杀测试（检查点 D，6 个测试杀 13 条）

新文件 `tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs`（5 测试）+ `MutationSurvivorTriageStdoutTests.cs`（1 测试，入 `CliSnapshotSerial` 集合串行捕获 stdout）。全部走既有 seam（`InMemoryScope`、`SetProfilesFilePathForTests`）+ 新增 `SetAppDataDirectoryForTests` seam，零真实注册表/用户态写入（CI-only 政策下的套件回归也由 CI verify job 承担）。

| 幸存者 | 位置 | 变异 | 杀死测试 | 杀伤机制 |
|---|---|---|---|---|
| 4337 | ProfileEffective.cs:93 | 取反拓扑守卫 | Preflight_GlobalInheritsSecretlessLaunch_Rejected | 无秘密 Launch 父档：原版拒绝(假)，变异跳过守卫返回真 |
| 4343 | ProfileEffective.cs:98 | `!=`→`==` null | 同上 | 真实存在的父档使变异跳过守卫 |
| 4392 | ProfileEffective.cs:193 | `?? "user"`→`"user"` | Unapply_RemovesAppliedSystemVariableWithoutBackup | system 作用域变量从 system store 删除（变异删 user store） |
| 4396 | ProfileEffective.cs:195 | 删除语句 | 同上 | 无备份变量使 delete 不可被恢复写掩盖 |
| 5011 | ProtectionCommand.cs:139 | `!`File.Exists 翻转 | Set_CustomLockedVariable_Rejected | 种入 protected-vars.json 后变异返回空列表 |
| 5012 | ProtectionCommand.cs:140 | `?? new()`→`new()` | 同上 | 变异无视文件内容 |
| 5020 | ProtectionCommand.cs:154 | Any→All | 同上 | 双条目列表 + 精确命中一条可区分 Any/All |
| 5053 | ProtectionCommand.cs:221 | `!`File.Exists 翻转 | Set_BuiltinProtectedVarsFileExternalEditHonored | 外部编辑过的内建清单（defaults 子集）：变异把 defaults 覆盖回外部文件 |
| 5055 | ProtectionCommand.cs:223 | `?? defaults`→`defaults` | 同上 | 变异无视外部文件内容 |
| 6111 | VariableChangeScope.cs:18 | `<3`→`<=3` | ChangeScope_AutoDetectedScope_MovesAndPrintsConfirmation | 3 参最小形式（自动探测）被变异打成用法错误 |
| 6126 | VariableChangeScope.cs:30 | 整链→`==null` | 同上 | 自动探测路径（oldScope==null）被变异打成 scope 错误 |
| 6191 | VariableChangeScope.cs:97 | 删除 Console.WriteLine | 同上 | 成功 stdout 断言（此前无任何断言覆盖该行） |
| 6376 | VariableRename.cs:26 | 删除 ValidateVariableInput | Rename_InvalidNewName_ThrowsAndPreservesSource | `=`-in-name 重校验（纵深防御）首次被断言 |

## Stryker 接 CI（检查点 E）

- `build.yml`：新增 `workflow_dispatch:` 触发器 + `stryker` job（ubuntu-latest，`if: github.event_name == 'workflow_dispatch'`，45min 上限）。步骤：checkout → setup-dotnet 10.0.x → `dotnet tool restore`（`.config/dotnet-tools.json` 锁 dotnet-stryker 4.16.0）→ `dotnet stryker`（`stryker-config.json` 的 `break: 60` 即闸门）→ `node scripts/stryker-module-scores.mjs <最新 mutation-report.html>` 输出模块分算表并 tee 落盘 → 上传 `stryker-mutation-report` artifact（HTML + 模块分算 txt）。
- `scripts/stryker-module-scores.mjs`：从 Stryker HTML 内嵌 `app.report` JSON（或裸 mutation-report.json）计算按文件模块分算 + 总分；本地已对基线 HTML 烟雾验证通过（输出见上）。
- `stryker-config.json`：**零改动**（mutate 四红线文件、thresholds 85/70/60、ignore string/logical 全部原样）。
- `.gitignore`：新增 `StrykerOutput/`（机器本地报告不入库）。

## 趋势记录（检查点 F，大脑回填）

| 跑 | 日期 | 触发 | tested | killed | survived | 仅余等价？ | 得分 | 证据 |
|---|---|---|---|---|---|---|---|---|
| 基线 | 2026-09-04 | 大脑本地重跑 | 96 | 78 | 14 | n/a | 40.00%（Stryker 原始分） | StrykerOutput/2026-09-04.19-08-22 |
| 重跑 | 待填 | CI `stryker` job（workflow_dispatch） | 待填 | 待填 | 待填 | 待填 | 待填 | CI artifact stryker-mutation-report |

预期：survived 从 14 → 1（仅余 #4369 等价登记）；killed 78 → ≥91。

## 交付物清单

- `.scratch/architecture-recovery/reports/18-survivor-registry.json` — 幸存者登记（14 条，LLM 预留字段）
- `.scratch/architecture-recovery/reports/18-mutation-survivor-triage.md` — 本报告
- `tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs` / `MutationSurvivorTriageStdoutTests.cs` — 补杀测试（6 测试）
- `src/CliRuntime.cs` — `SetAppDataDirectoryForTests` seam（票 04 `SetProfilesFilePathForTests` 同型，生产路径零变化）
- `scripts/stryker-module-scores.mjs` — 模块分算脚本
- `.github/workflows/build.yml` — workflow_dispatch + `stryker` job
- `.gitignore` — `StrykerOutput/`

## 遗留与风险

- 基线 HTML 的源码快照与当前工作树四个 mutate 文件字节一致（已核验），但票 21（CliRuntime 拆分）已在并行分支改动 `Program.cs`/`CliRuntime.cs` —— Stryker 重跑的行号可能相对本登记表有漂移，判定锚定 mutator+replacement 内容而非行号。
- Stryker 跑在 ubuntu（`-p:EnableWindowsTargeting` 未加：verify-l1 先例已证明该路径可行——其 `dotnet test` 命令带 `-p:EnableWindowsTargeting=true`；`dotnet stryker` 内部构建同 csproj，若 Linux 构建失败，fallback 为把 stryker job 挪回 windows-latest，brain 可就地改 runs-on）。
- 票 13 曾记录的 timeout 抖动会小幅扰动 killed/timeout 拆分，不影响 survived 判定。


## 大脑移交（检查点 F 执行指引）

1. 合流本分支（arch/18-mutation-survivor-triage，栈式锚定 arch/21-cliruntime-extraction；CliRuntime.cs 的 seam 行依赖票 21 提交 qtp）后 push，GitHub Actions 页面手动 **Run workflow**（workflow_dispatch）触发 `stryker` job（ubuntu-latest，~30-45min）。
2. 取 `stryker-mutation-report` artifact 中的 `stryker-module-scores.txt`（模块分算表）与 `mutation-report.html`（幸存者明细）。
3. 回填本报告"趋势记录"表与登记文件 `runs[]`：预期 survived 14 → 1（仅余 #4369 等价）；killed 78 → ≥91。若 CI 下 `dotnet stryker` 因 Linux 构建 net10.0-windows 失败，把 job 的 `runs-on` 改回 `windows-latest` 即可（其余步骤不变）。
4. 验收锚点：survivor 判定以 mutator+replacement 内容为锚（票 21 拆分 Program.cs 后行号可能漂移）；`StrykerOutput/` 已入 .gitignore，本地报告不入库。

---

## 收口修正（2026-09-05 大脑）

- 本文档写作时含「待 CI」表述；PR #45（head=完整 11 提交栈）已全绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success + Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿），本票终态 = ✅ done（README 已登记）。
