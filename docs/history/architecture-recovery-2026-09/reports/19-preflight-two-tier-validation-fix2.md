# 返修报告 — 票 19 二次返修：%VAR% 判定去机器依赖（CI 首跑红）

日期：2026-09-05 · 执行窗口：票 19 二次返修子窗口 · 依据：reviews/19（返修复核段）+ prompts/19-preflight-two-tier-validation-fix2.md · 失败证据：PR #42 长链 CI run 33953937157

## 开工再质检（大脑根因诊断逐条复验）

| 声明（大脑诊断） | 证据（仓库实物 / CI） | 结论 |
|---|---|---|
| CollectPreflightWarnings（ProfileEffective.cs:159）defined 判定只查 user/system 真实注册表 + profile 自有变量 | L159 实文确无 `Environment.GetEnvironmentVariable`（全文件 0 处）；VariableQuery.cs:239 `GetVariableValue` 仅 OpenSubKey 读注册表 | 属实 |
| 测试 Detailed_DefinedVarReference_NoWarning 用 %SYSTEMROOT% 期望无警告 | 测试 L208-215 实文 `Value = "%SYSTEMROOT%\\bin"` + `Assert.False(result.HasWarnings)` | 属实 |
| 违反 hard-boundaries 测试纪律（不碰真实注册表、不依赖机器环境状态） | 判定面 = 真实注册表两配置单元；%SystemRoot% 为内核提供变量，HKCU\\Environment 与 HKLM Session Manager\\Environment（其中只有 windir）均无该值名 → CI 上判定为 undefined → 误报警告 → 断言红 | 属实 |
| CI 首跑红：PR #42 run 33953937157 → ProfileSeamValidationTests.Detailed_DefinedVarReference_NoWarning [FAIL]（Assert.False()） | `gh run view 33953937157` conclusion=failure；--log-failed 命中 `[FAIL] ...Detailed_DefinedVarReference_NoWarning` / `Assert.False() Failure` @ ProfileSeamValidationTests.cs:line 215 | 属实（本窗口用 gh 只读核验） |

诊断全部属实后执行返修。

## 返修执行

### 返修项 1：%VAR% defined 判定补齐进程环境（src/ProfileEffective.cs）

`CollectPreflightWarnings` 的 defined 判定面增加 `Environment.GetEnvironmentVariable(refName) != null`（置于首位），并附注释记录根因（%SystemRoot% 内核提供、两配置单元缺失、CI run 33953937157 红）。对外语义 = **展开可解析即不警告**：进程环境块在 spawn 时合并 system+user，凡进程环境可解析的引用对消费者同样可解析。error 档与 --strict 契约零改动。

### 返修项 2：测试去机器依赖（tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs）

- `Detailed_DefinedVarReference_NoWarning` 重写：改用具名变量 `EM_T19_DEFINED_REF`，`EnvironmentVariableTarget.Process` set（try）→ 清（finally），defined 态经进程环境命中 → 无警告；断言 `HasErrors==false && HasWarnings==false` 不变。
- `Detailed_UndefinedVarReference_IsWarning` 补一行显式 `SetEnvironmentVariable("EM_T19_UNDEF_VAR", null, Process)`：返修项 1 把判定面扩到进程环境后，undefined 态须自控进程环境半边才真正 hermetic（EM_T19_ 测试前缀名在真实注册表两单元亦无条目）。
- undefined/defined 两态各一条 Fact 钉住，符合任务书「钉住 defined/undefined 两态各一条」。

## 修复后自检

- [x] 实现探针：`Environment.GetEnvironmentVariable(refName) != null` 在 defined 判定首位，注释含根因与 CI run 号。
- [x] 测试探针：`EM_T19_DEFINED_REF` Process set/finally 清；`EM_T19_UNDEF_VAR` 显式清；`%SYSTEMROOT%` 已从代码移除（仅存于回归原因文档注释）。
- [x] 大括号平衡 0；两文件 CRLF=0（与 .gitattributes *.cs → LF 一致）。
- [x] 行为边界：error 档检查、--strict 契约、EmitPreflightWarnReport 输出形态零改动；本返修仅 widened defined 判定面 + 两测试。
- [x] CI-only 纪律：本地未运行 build/test；回报大脑推送/触发 CI（PR #42 长链复跑）取绿。

## 变更文件清单（2）

src/ProfileEffective.cs · tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs

## 提交证据（GitButler）

- 分支 `arch/19-preflight-two-tier`，提交 `owu`：恰好 2 个文件 3 个 hunk——src/ProfileEffective.cs（kv:6，defined 判定 +5 行含根因注释）、tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs（ss:6 undefined 态显式清 + ss:f defined 测试重写）。
- 提交前按 hunk 内容逐个核验纯属本票；同区他票改动（AGENTS.md sm:2/sm:e、.zcode ou:f）未纳入、未触碰。
- 未 push、未建 PR（遵循 WORKFLOW §4.2）；回报大脑推送/触发 CI（PR #42 长链复跑 + 全栈复跑）取绿。
- 本报告备份：`%TEMP%/em-t19-fix2-report-backup.md`（WORKFLOW §6 教训③防线）。
