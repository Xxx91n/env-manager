# 复核 19 — 预检验证两级降级（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 19 验收） | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | 悬空 launch target 降 warn，error 四档仍拒绝 | src/ProfileEffective.cs:188-232 RunProfilePreflightDetailed（error 档 :194-227：Global-inherits-Launch/未声明继承 secret/含=/受保护变量/32767 截断）；CollectPreflightWarnings :150-180（未定义 %VAR%、stale entry、dangling :178）；elevation 拒绝保留 Program.cs:119-122 | 证实 |
| 2 | 默认 warn 写操作照常且退出码 2；--strict 拒绝 | ProfileCommand.cs:1072-1077（applyWarned 继续 ApplyProfile；strict return 1）、:1111（applyWarned?2:0）；**ProfileLaunch（:681-792）无任何 exit-2 路径（仅 0/1）** | apply 证实；**launch 无 2 码语义** |
| 3 | warn 输出结构化 | ProfileEffective.cs:240-252 EmitPreflightWarnReport：stdout JSON（preflight/command/profile/strict/warnings[]）+ stderr 人读行 | 证实 |
| 4 | 退出码契约全链文档化 | docs/cli-commands.md:95、docs/architecture.md:161、AGENTS.md:114、hard-boundaries.md:126 均更新；**但 cli-commands.md:95、AGENTS.md:114、main.rs:498 注释把 exit 2 写进「profile apply/launch」，与代码（仅 apply）矛盾** | 半证实（**3 处过度声明，返修**） |
| 5 | ProfileSeamValidationTests 扩展两级断言 + 退出码断言 | 实物 9 个新 Fact（含 Detailed_Strict_PromotesWarningToRefusal、ApplyCommand_WarnOnlyProfile_Exit2_Default_StrictExit1）；**报告写「8 个新 Fact」** | 证实（报告计数 8→9） |
| 附 | 本票无 CI run（分支未推送） | gh run list --branch arch/19-preflight-two-tier 为空；报告自述「验证交由大脑推送 CI 验证分支」 | 待全栈 CI |

## 总结论：🔧 返修（小）。代码主体证实；返修 = 文档链 3 处收窄为「profile apply」+ 报告计数修正记录。见 prompts/19-preflight-two-tier-validation-fix.md。

## 返修复核（2026-09-05 大脑）

| # | 返修项声明 | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | exit-2 文档链三处收窄为仅 profile apply | cli-commands.md:95、AGENTS.md:114、main.rs:498 均为「profile apply only」；architecture.md:161 对齐表、hard-boundaries.md:126 一致无残留 | 证实 |
| 2 | ProfileLaunch 行为零变化 | ProfileLaunch(:681-792) 仍仅 0/1；return applyWarned?2:0 仅 apply(:1111)；-S 检查无后续改动 | 证实 |
| 3 | 报告 8→9 更正 + 修正记录 | reports/19:65 已更正为 9、:97 修正记录段在 | 证实 |
| 附 | 报告「main.rs 仅注释行」 | 提交 vwr(25d9c62) main.rs 实为 7 增 1 删（含 exit-2 映射 hunk，与原实现同 hunk 归位）——净行为零变化，但表述不准 | 报告小失实（已补修正记录） |
| 附 | 报告引用提交 sha 过期（3e24a2f→25d9c62） | 票 20 rework 重建所致，同消息同内容 | 流程预期演进，非缺陷 |

**返修复核结论：✅ 返修通过；剩余前置 = 全栈 CI（doc-sync + 文档门禁绿）。**

## 二次返修复核（2026-09-05 大脑）

| # | 声明（fix2 返修项） | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | defined 判定补齐进程环境 | src/ProfileEffective.cs:165-168：Environment.GetEnvironmentVariable(refName) != null \|\| user/system 注册表 \|\| profile 自有变量；注释说明 SystemRoot 为内核提供、不在两 hive，并锚定 run 33953937157 | 证实 |
| 2 | 测试去机器依赖 | ProfileSeamValidationTests.cs:216-217 用具名 EM_T19_DEFINED_REF（Process 作用域）+ try/finally SetEnvironmentVariable(null) 清理；undefined 态测试沿用 EM_T19_UNDEF_VAR | 证实 |
| 3 | 报告含诊断再质检 + 对照表 + 自检 | 报告 46 行：开工再质检 / 返修执行（两项各一小节）/ 修复后自检 / 提交证据 | 证实 |
| 4 | 分支落位 | but status：pr 分支 owu(2e3b829) 位于 vwr(25d9c62) 之上、arch/19 顶端；新 commit 未改写历史 | 证实 |
| 5 | CI 绿（复跑） | 窗口未推送（遵循纪律）；长链/全栈复跑为大脑动作，尚未执行 | 待大脑 CI 复跑 |

**二次返修复核结论：✅ 修复通过（代码层）；登记 done 的剩余前置 = 长链（PR #42）与全栈（PR #43）CI 复跑绿。**
> 终验（2026-09-05）：PR #45 全栈绿（run 33963823146），本票 ✅ done。
