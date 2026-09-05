# 报告 — 票 19：预检验证两级降级（error/warn + --strict + 退出码 2）

日期：2026-09-05 · 执行窗口：票 19 子窗口 · 分支：arch/19-preflight-two-tier（GitButler，提交 tlt / sha 445a0d9，未 push）

## 落地内容

### 两级 preflight 核心（src/ProfileEffective.cs，+130 行）

- `PreflightResult`（internal sealed class）：`Errors` / `Warnings` 两个清单 + `HasErrors` / `HasWarnings`。
- `CollectPreflightWarnings`：warn 档三项检查——
  1. 变量值含未定义 `%VAR%`（对照 user/system 注册表 + 本 profile 解析集）→ `"Variable '<name>' references undefined %VAR%: %X% (expands literally)"`；
  2. PATH 条目展开后不存在（`FastDirectoryExists`，200ms 超时防 UNC 阻塞）→ `"...(stale entry)"`；
  3. Launch profile 的 targetExecutable 文件缺失 → `"...(dangling launch target)"`。
- `RunProfilePreflightDetailed(profile, allProfiles, strict)`：error 档为 `RunProfilePreflight` 的逐条移植（Global-inherits-Launch、未声明继承 secret、空名/超长名/含 `=`/受保护变量/secret 名冲突/PATH 片段非法、InvalidDataException 归入 Errors）；error 档非空时直接返回，warn 档只在其余可执行时收集（拒绝不被警告噪音淹没）。
- `EmitPreflightWarnReport`：stderr 一行人读行（含 strict 时 "refusing" / 默认 "continuing"），stdout 一份 JSON 结构化报告（字段 `preflight:"warn"` / `command` / `profile` / `strict` / `warnings[]`，`JsonOptsIndented`，可解析）。
- 原 `RunProfilePreflight` 布尔签名与全部现有调用点/测试零改动。

### 命令接线（src/ProfileCommand.cs，+70 行）

- 路由：`"apply" => ProfileApply(args, name)`，用法串升为 `profile apply <name> [--strict]`。
- `ProfileApply`：解析 `--strict`；error 档拒绝文案与票前逐字一致（并在其后附 `- <error>` 明细行）；warn 档默认打印报告后继续 apply，最终 `return applyWarned ? 2 : 0`；strict 下打印报告后 `return 1`（不写）。
- `ProfileLaunch`：`--strict` 时对悬空 launch target 预检拒绝（exit 1 + 同款报告）；默认行为不变（spawn 本身会大声失败）。
- `ShowProfileHelp`：apply/launch 两行更新 `--strict` 用法与退出码语义。

### 连带面修复（票 04 教训①检索到的同款形态）

- `src/Program.cs`：环境审计快照门 `exitCode == 0` → `exitCode is 0 or 2`（退出码 2 的 apply 写了注册表，审计连续性必须保留）。
- `frontend/src-tauri/src/main.rs`：`run_cli` 把退出码 2 映射为 `success: true`（写已发生，GUI 不得报失败 toast）；JSON 警告报告在 stdout `data` 内，stderr 警告行进日志（`warn!` + scrub_stderr）。watchdog/shutdown IPC golden 不受影响。
- main.rs `is_read_only` 按 subcommand 分类，`--strict` 是附加参数不参与分类——`profile launch` 保持 read 锁，无需改动（核验记录）。

### 文档全链（9 处，同 commit）

1. `docs/cli-commands.md`：命令表 apply/launch 行 + `--strict`；Error handling 段退出码契约 `0/1/2`；Profiles 段新增两级验证 bullet。
2. `docs/architecture.md`：GUI/CLI 对齐表 Profile apply 行注明两级 preflight + Rust shell 退出码 2→GUI success。
3. `AGENTS.md`：命令速查 `Exit 0/1` 句扩展为 `0/1；apply/launch 另有 2=warn`。
4. `AGENTS.cli.md`（随二进制分发）：两处 Exit codes 行 + apply 签名 + warn 报告说明（保留原 CRLF 行尾与既有孤立 CR 字节）。
5. `docs/agents/hard-boundaries.md`：新增 `v0.9.31 Two-tier preflight validation + exit code 2` 红线条目（紧跟 Launch apply 硬边界之后）：error 档 MUST 硬拒；warn 档 MUST 允许写；升档须以结构化警告清单证明稀有（MongoDB validationAction 收紧纪律）。
6. `docs/backup-and-profiles.md`：安全清单新增两级验证条目。
7. `README.md`：Profiles & config 特性 bullet 追加两级验证句。
8. `docs/i18n/README.zh_CN.md`：CLI 模式 bullet 追加两级验证句（中文）。
9. `ShowProfileHelp`（CLI 内置帮助，随二进制）。

## 验收项逐条核验（issues/19）

### [x] 悬空 launch target 不再硬阻断 profile 写（降 warn）；error 档四类仍拒绝

- 证据：`RunProfilePreflightDetailed` 中悬空 target 只进 `Warnings`（`CollectPreflightWarnings` 第 3 项）；error 档四类（32767 截断/含 `=`/受保护/elevation 缺失经 `UnauthorizedAccessException` 路径）保持在 error 分支。注意：`ValidateProfiles`（SaveProfiles 路径）的 `ValidateLaunchTarget` 对 set-launch/create 仍硬拒——本票按 handoff 划界只降 **apply/launch preflight** 的阻断；profile 写盘入口的防悬空校验是数据卫生而非 apply 闸门，保留原状（报告「边界说明」节）。
- 新测试：`Detailed_DanglingLaunchTarget_IsWarning`（warn）、`Detailed_ProtectedVariable_IsErrorNotWarning`、`Detailed_NameWithEquals_IsError_UnderBothModes`（error 档两模式均拒）。

### [x] 默认 warn 时写操作照常执行且退出码 2；--strict 下 warn 档拒绝且退出码 1

- 证据：`ProfileApply` 返回 `applyWarned ? 2 : 0`；strict 分支 `return 1` 且不执行 ApplyProfile。
- 新测试：`ApplyCommand_WarnOnlyProfile_Exit2_Default_StrictExit1`（端到端经 `RunProfileCommand`：默认 exit 2 且 `IsEnabled=true`；strict exit 1 且保持 disabled）、`ApplyCommand_CleanProfile_Exit0`（干净 profile 无行为漂移）。

### [x] warn 输出结构化（可解析、含被降级项清单）

- 证据：`EmitPreflightWarnReport` 输出 JSON：`{"preflight":"warn","command":"profile apply","profile":"...","strict":false,"warnings":[...]}`，`warnings[]` 即被降级项清单，stdout 可解析；stderr 人读行同步。

### [x] 退出码契约全链文档化：CLI 文档、GUI 对齐表、AGENTS.md 命令表、hard-boundaries.md 相关句

- 证据：上述「文档全链（9 处）」清单；每处均含 `2 = success with preflight warnings` 语义 + `--strict` 语义。AGENTS.cli.md 为 agent 契约面，hard-boundaries.md 为红线面，均已落。

### [x] 测试扩展 ProfileSeamValidationTests 式两级断言 + 退出码断言（CI 验证）

- 证据：`tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs` +122 行、9 个新 Fact（两档归属 5 个、strict 提升 1 个、端到端退出码 2 个、干净路径 1 个；原报告误写 8，漏计 `Detailed_NameWithEquals_IsError_UnderBothModes`，已更正——见下方修正记录）（不触真实注册表/真实 profiles.json）。
- CI-only 纪律（2026-09-04 用户令）：本地未运行 dotnet test/build；验证交由大脑推送 CI 验证分支触发 build.yml verify job（`dotnet test` 门）裁决。本报告所有静态验证为片段探针 + 括号平衡 + EOL/BOM 复核（12 文件全部 LF/预期 BOM，探针全绿）。

## 边界说明（诚实记录）

1. `ValidateProfiles`（`profile create/set-launch/add-var` 等写盘入口）仍经 `ValidateLaunchTarget` 硬拒悬空 target：该处是保存时数据卫生（拒绝写入坏数据），不是 apply 闸门；research D 节与 handoff 划界针对的是「预检验证（ValidateProfiles / ProfileEffective pre-flight）」的 apply/launch 门。若大脑判断 create 时也应降 warn，属新决策，留登记。
2. warn 档 %VAR% 检查对每个值调用 `GetVariableValue`（注册表直读）——仅 apply/launch 时的单次 preflight 开销，非热路径；PATH/变量遍历为 O(值数 × 引用数)。
3. GUI 对退出码 2 的呈现：Rust shell 已映射为 success（不弹失败 toast），警告详情在返回 data（JSON 报告）与日志中；GUI 内嵌警告徽章属产品增强，不在本票范围。
4. strict 参数在 `RunProfilePreflightDetailed` 内未参与计算（gate 决策在命令层）：保留在签名中以稳定 API 形态，报告如实记录。

## 检查点回执（handoff A–F）

- A 定位校验清单与两档划分点：`RunProfilePreflight`（ProfileEffective.cs）/ `ProfileApply`（ProfileCommand.cs:996）/ `ValidateProfiles`（ProfileStorage.cs:64）——完成。
- B warn 档 + 结构化报告：`CollectPreflightWarnings` + `EmitPreflightWarnReport`——完成。
- C --strict 与退出码 2：ProfileApply/ProfileLaunch + Program.cs 审计门 + main.rs GUI 映射——完成。
- D 文档全链同步：9 处——完成。
- E 测试扩展：8 Fact——完成（CI 裁决待大脑推 CI）。
- F 交大脑触发 CI：本报告即交付物；分支 `arch/19-preflight-two-tier` 已提交（未 push，遵循 WORKFLOW §4.2）。

## 变更文件清单（13 文件已提交 tlt，本报告 + 备份落 .scratch，gitignored）

src/ProfileEffective.cs · src/ProfileCommand.cs · src/Program.cs · frontend/src-tauri/src/main.rs · AGENTS.cli.md · AGENTS.md · README.md · docs/i18n/README.zh_CN.md · docs/cli-commands.md · docs/architecture.md · docs/backup-and-profiles.md · docs/agents/hard-boundaries.md · tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs
## 提交证据（GitButler）

- 分支 `arch/19-preflight-two-tier`（创建于 common base d71b30d 之上），提交 `tlt`（sha `445a0d9`）：13 个文件（12 变更 + 1 新测试文件）。
- 误提交并已纠正：票 20 的 `ProfileCreateHelpTests.cs` 一度被带入 tlt，经 `but uncommit tlt:wt` 移回未提交区，最终提交仅含本票 13 文件（`but status -fv` 复核：count 13，逐文件清单见上）。
- 未提交区剩余：`.zcode/plans/*`（票 14 窗口产物）、`scripts/test-with-restore.ps1`（他票改动）、票 20 的 ProfileCreateHelpTests.cs —— 均未纳入本票提交。
- 未 push、未建 PR（遵循 WORKFLOW §4.2）。
- 报告备份：`%TEMP%/em-t19-report-backup-19-preflight-two-tier-validation.md`（WORKFLOW §6 教训③防线）。

---

## 修正记录（2026-09-05 大脑复核）

- 新增 ProfileSeamValidationTests Fact 实数为 9（报告写 8）。
- 文档链把 exit 2 语义写进「profile apply/launch」，但 ProfileLaunch 无 exit-2 路径（仅 0/1）——docs/cli-commands.md:95、AGENTS.md:114、main.rs:498 三处需收窄为仅 profile apply；见 reviews/19 与 prompts/19-preflight-two-tier-validation-fix.md。
### 子窗口返修执行记录（2026-09-05，prompts/19-preflight-two-tier-validation-fix.md）

- 上述两条均已执行完毕：Fact 计数已在本证据行更正为 9；exit-2 文档链三处已收窄为「profile apply」——docs/cli-commands.md:95、AGENTS.md:114、frontend/src-tauri/src/main.rs:498 注释（仅注释，无代码行为改动）。
- 全库补扫描：其余 `apply/launch` 字样仅为票 04 历史注释（ProfileEffective.cs:80、ProfileSeamValidationTests.cs:8）与本报告历史记录，均非 exit-2 声明；architecture.md 对齐表 L161 与 hard-boundaries.md L126 原本就限定为 profile apply，无需改动。

---

## 收口修正（2026-09-05 大脑）

- 本文档写作时含「待 CI」表述；PR #45（head=完整 11 提交栈）已全绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success + Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿），本票终态 = ✅ done（README 已登记）。
