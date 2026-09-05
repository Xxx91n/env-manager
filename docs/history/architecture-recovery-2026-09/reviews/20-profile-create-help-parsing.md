# 复核 20 — profile create --help 解析修复（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 20 验收） | 证据（仓库实物 / git） | 结论 |
|---|---|---|---|
| 1 | --help/-h/-?//? 识别为帮助请求，零写入 | src/ProfileCommand.cs:855-859 IsProfileCreateHelp（--help/-h OrdinalIgnoreCase、-?//? Ordinal），:866-870 前置分支输出 usage + return 0；ProfileCreateHelpTests.cs Theory×6 + 3 Fact 钉住零 profiles.json/audit 写入 | 证实（行为） |
| 2 | 不带名与非法调用不变 | ProfileCreateHelpTests 缺名/未知旗标 Fact 存在 | 证实 |
| 3 | 回归测试钉住 | ProfileCreateHelpTests.cs（101 行，arch/20 提交 893ae8b） | 证实 |
| 4 | i18n / 文档同步 | 无新用户可见字符串（usage 复用既有文本） | 不适用 |
| 5 | **src 修复归属本票** | **git log --all -S IsProfileCreateHelp → 仅 445a0d9「feat(preflight): two-tier…(issue 19)」；arch/20 分支仅含测试文件** | **证伪（归属违规，返修）** |
| 附 | 本票无 CI run（分支未推送） | gh run list --branch arch/20-profile-create-help 为空 | 待全栈 CI |

## 总结论：🔧 返修。功能与测试内容证实，但 src 修复被装在票 19 的提交里，arch/20 不完整——违反 §4.2「只提交本票改动」与波次并行独立性。返修 = hunk 归位（见 prompts/20-profile-create-help-parsing-fix.md）。

## 返修复核（2026-09-05 大脑）

| # | 返修项声明 | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | src hunk 归位票 20 | git log --all -S IsProfileCreateHelp → 唯一 03e3e01「fix(profile)…(issue 20)」，--stat 仅 src/ProfileCommand.cs（19 增 1 删）；arch/20 = zpr + wyn 两提交 | 证实 |
| 2 | 票 19 提交不再含该 hunk | 445a0d9 已成 orphan（任何 ref 不可达，-S 不再列出）；重建后 rxz(032497b) patch 内 grep=0；报告自述处置方案即 orphan，与实况一致 | 证实（orphan 为预期处置） |
| 3 | 行为零变化 | ProfileCommand.cs:853-869（ProfileCreateUsage/IsProfileCreateHelp/前置 help 分支）完好；ProfileCreateHelpTests 9 用例在位 | 证实 |
| 4 | 报告含再质检 + 归位前后状态 + 自检 | 报告 §2 对照表 6 行、§3 前后栈式、§4 自检 4 条 | 证实 |

**返修复核结论：✅ 返修通过；剩余前置 = 全栈 CI 编译绿。**
> 终验（2026-09-05）：PR #45 全栈绿（run 33963823146），本票 ✅ done。
