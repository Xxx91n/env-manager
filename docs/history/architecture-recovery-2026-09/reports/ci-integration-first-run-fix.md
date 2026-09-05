# 报告 — CI 集成首跑红联合返修（票 22+24）：set+get+delete round-trip set failed

> 启动器：prompts/ci-integration-first-run-fix.md · 版本控制：WORKFLOW §4.2（GitButler，不推送）
> 验证纪律：CI-only——本窗口零本地构建/测试运行；证据 = CI run 日志（gh api 拉取）+ 仓库实码静态核对；再质检由大脑推送后 PR #42/#43 复跑提供。

## 开工前置复述

Blocked by：无（大脑 CI 复核发现集成首跑红）。必读清单：reviews/22、reviews/24、handoffs/22、handoffs/24、spec.md（Phase 4 段）、WORKFLOW.md、docs/agents/hard-boundaries.md——全部读毕。

## 一、大脑三条已证事实的再质检（属实，附实物）

| # | 大脑事实 | 再质检证据 | 结论 |
|---|---|---|---|
| ① | Invoke-Cli 吞 stderr（test-with-restore.ps1:266-271，`2>$null`） | 改前工作区实码：`& $CliPath @CliArgs 2>$null \| Out-Null; return [int]$LASTEXITCODE`——stderr 定向丢弃，失败零输出 | 属实 |
| ② | 失败仅发生在固定名 EM_TEST_FOO；带戳名同类 set 通过 | PR#43 日志 09:12:20.02 `[test] set+get+delete round-trip ... FAIL: set failed`，随后 09:12:20.62 `rename contract ... OK`（其 set EM_TEST_SRC_$Stamp 通过）——同 run、同 seam、同写路径 | 属实 |
| ③ | EM_TEST_FOO 在 HKCU 预存（前序套件同 run 写入，快照日志 predate this run） | PR#43 日志 09:12:19.78：`Note: 1 pre-existing EM_TEST_* value(s) in HKCU predate this run (left untouched): EM_TEST_FOO` | **属实，但"前序套件写入"一词需修正**（见三、2——全库唯一写入者是 harness 自身的 round-trip；写入时机属镜像残留谱系，非本次 job 内前序套件） |

## 二、返修项 2 —— 根因定位（声明 → 证据 → 结论）

### 1. set 非零退出的机制（已实锤）

- **声明**：`set EM_TEST_FOO bar123 --scope user` 在"名字已预存且值 ≠ bar123 且无 `--overwrite`"时被 CLI 拒绝，exit 1，stderr = `Error: Variable already exists with a different value; use --overwrite`。
- **证据**：
  - src/VariableWrite.cs:197-199（RunSet）：`string? existing = engine.ReadValue(args[1], scope)?.Value; if (existing != null && existing != args[2] && !args.Contains("--overwrite")) return ArgError("Error: Variable already exists with a different value; use --overwrite");` ——这是有效、未保护名在 set 路径上**唯一的名称相关非零退出**；其余退出（空名/内部备份名/超长/`=`/受保护/值超长/写验证失败/作用域不可开）对带戳名同样适用，而带戳 set 通过。
  - PR#43 快照（09:12:19.78）证明 set 执行前 EM_TEST_FOO 已在 HKCU。
  - PR#43 的 residue-zero 断言通过（09:12:33.01），且 reconcile 对预存值"left untouched"（test-with-restore.ps1:288-292 注释与快照对账语义）→ 预存值在 set 时仍在。
- **结论**：机制成立——固定名 + 预存异值 + 无 `--overwrite` = 确定性拒绝。排除了写路径 value-kind 策略与票 24 seam 下审计/保护存储路径：同 run 的带戳 set 在完全相同的 seam 与存储状态下通过（差分证明），且 L197-199 的拒绝发生在任何审计/保护存储写入之前（纯读比较）。

### 2. 预存值的来源（机制外origin；可证部分与未证部分分开陈述）

- **已证**：全库唯一写 EM_TEST_FOO 至真实注册表的代码 = harness round-trip 自身（旧 L445；rg 全库核验）；PR#43 job 内 harness 之前的四个套件（launch-env-injection/canary/inheritance）的 CLI 调用全部为带戳名或 profile 域操作，无注册表写入（逐文件核验）；两 PR 的 xUnit 工程无任何真实 RegistryScope 构造（差分套件受 EM_DIFFERENTIAL_ORACLE 门控且用 EM_DIFF_ 带戳名 + finally 清理）。
- **未证（如实标注）**：hosted runner 逐 job 全新 VM 与"快照即有 EM_TEST_FOO"并存，唯一自洽解释是该 runner 镜像谱系携带了历史 harness run 的残留（同镜像谱系残留亦与两 run 的 `ISSUE24_REAL_DIR_EXISTED=True` 吻合——真实 `%LOCALAPPDATA%\EnvManager` 在 Pester 步骤开始前即存在）。GitHub 不暴露 runner 镜像谱系，此项无法从日志进一步实锤；返修项 1 的 stderr 输出将在复跑中提供终验（若复跑 stderr 出现 "already exists"，机制定论；若复跑全绿——带戳名本就不受预存态影响——origin 问题按本报告口径归档）。
- **PR#42 的 note 缺席解释（已实锤）**：从 PR#42 head SHA（83b0096）拉取其检出的 harness 实码：无 "predate this run" note 代码、无 residue-zero 断言（票 22 改动未入该分支的 harness 版本；PR#43 head 996c320 的 harness 含全部票 22 代码）——故 PR#42 日志无 note ≠ 快照无 EM_TEST_FOO，只是无可观测手段；两 PR 失败同型（首个 set 失败、后续写全过）。

### 3. 反模式定性

固定名 + 无 `--overwrite` 的 round-trip 假设了"处女名"。无论预存值来自镜像谱系、用户机器残留（AGENTS.md 已载 EM_TEST_DST=v1 先例）还是未来新增套件，该假设都不成立——属启动器第 ③ 项命名的"固定名与残留碰撞反模式"。

## 三、返修实现（scripts/test-with-restore.ps1，+36/−8 行，CRLF 保真）

### 返修项 1：Invoke-Cli 失败保留并打印 stderr

- `Invoke-Cli` 改为：stderr 重定向到临时文件 → exit 0 静默（契约不变：仍只返回 int 退出码）→ exit ≠ 0 时以红字打印 `CLI failed (exit N). args: ...` + 完整 stderr（空 stderr 也打印 fallback 行，保证可观测）→ finally 删除临时文件。
- **同反模式顺带修复（scope 延伸，提请大脑复核）**：toggle 测试内的 `& $CliPath list 2>$null | ConvertFrom-Json`（旧 L505）同属"失败零诊断"——改为 `2>&1` 捕获，非零退出即 throw 并携带捕获文本。若大脑认为超范围可单独 revert，不影响其余修复。

### 返修项 3：round-trip 改带戳名

- round-trip 三个操作（set/get/delete）统一改用 `$roundTripName = "EM_TEST_RT_$Stamp"`（与 rename 契约的 `EM_TEST_SRC_$Stamp` 同型）；名称由该测试创建并删除，pre/post 快照 diff 仍为空。
- **residue-zero 断言与 pre-existing note 逻辑零改动**（逐字节核对：两段代码原样保留）。
- 修复后的失败模式：若未来仍红，stderr 将带出 CLI 原文（返修项 1 保障），不再出现无声 "set failed"。

## 四、自质检记录

| 检查 | 方法 | 结果 |
|---|---|---|
| 编辑保真（CRLF 文件） | 字节级替换脚本（.codex-tmp/fix-harness-crlf.py，已删）：needle 唯一性断言 ×2、写后 CRLF 计数 >400、逐字节 lone-LF 扫描 =0 | 通过 |
| 片段探针 | `EM_TEST_RT_$Stamp`×1、`2>$stderrFile`×1、注释中 EM_TEST_FOO×1（历史说明）、`predate this run`/`Residue-zero` 原样 | 通过 |
| PowerShell 语法 | `[System.Management.Automation.Language.Parser]::ParseInput`（pwsh -NoProfile，静态解析非运行） | 0 错误 |
| git diff --check | 行尾/空白门 | 通过 |
| Invoke-Cli 契约回归 | 全部调用方仍为 `-ne 0` 比较，返回单值 int 契约未变（逐调用点核对） | 通过 |
| 全库残留检查 | `rg "EM_TEST_FOO"` 仅剩注释 1 处；harness 内其余测试名全部带戳 | 通过 |
| 本地运行 | **未运行**（CI-only 政策）；行为证据全部移交 CI 复跑 | — |

## 五、移交大脑（复跑与收口清单）

1. 推送含本修复的分支并触发 PR #42/#43 复跑。
2. 预期一：round-trip 全绿（带戳名不受任何预存态影响），Pester 四套件绿，verify 全绿。
3. 预期二（若仍红）：run 日志将出现 `[test-with-restore] CLI failed (exit N). args: ...` + CLI stderr 原文——按报告二、1 的机制表逐项对号，不再需要盲猜。
4. 本票不改动票 22/24 原验收边界：residue-zero、pre-existing note、ENVMANAGER_LOCALAPPDATA 隔离、Assert user-state isolation 步骤全部原样。
