# 复核 18 — 变异测试幸存者分诊（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 18 验收） | 证据（仓库实物 / CI） | 结论 |
|---|---|---|---|
| 1 | 登记文件逐条含位置/类别/判定/理由/LLM 预留字段 | reports/18-survivor-registry.json：14 条（weak-assertion 13 + equivalent 1），字段 id/file/line/column/mutator/original/replacement/category/verdict/killedByTest/rationale/llmDetection | 证实 |
| 2 | 非等价幸存者补测试后重跑 kill 上升、survived 仅余等价 | 补杀测试 MutationSurvivorTriageTests.cs + MutationSurvivorTriageStdoutTests.cs 存在；**但 MutationSurvivorTriageTests.cs:46/:63 用 JsonSerializer 而缺 using System.Text.Json → CI verify 编译红（run 33944493443: error CS0103 ×2）**；CI 重跑从未成功 | **证伪（返修）** |
| 3 | 阈值与 ignore 不变、不追 100% | git diff d71b30d -- stryker-config.json 为空；等价者 #4369 无补杀测试（报告自述） | 证实 |
| 4 | Stryker 可经 CI 执行 + 模块分算报告 | build.yml:438 stryker job（workflow_dispatch、ubuntu、45min、EM_DIFFERENTIAL_ORACLE/EM_L1_MATRIX 置空）；scripts/stryker-module-scores.mjs 存在并接入；**但该 job 的「ls -t … | head -1」(:460) 触发 actionlint SC2012 → Workflow Lint 红（run 33944493448）** | 半证实（**lint 返修**） |
| 5 | 趋势记录（基线 vs 重跑） | 基线 96/78/14/4/40.00% 已录 registry；重跑数字空缺（README 亦注「待大脑 CI 重跑」） | 未完成（待 CI） |

## 总结论：🔧 返修。登记/补杀测试/CI job 三件套本身证实，但新测试不编译（CS0103 ×2）且 job 的 ls|head 触发 lint 红——arch/25 PR 上的 CI/CD 与 Workflow Lint 双红皆源于本票。返修项见 prompts/18-mutation-survivor-triage-fix.md。

## 返修复核（2026-09-05 大脑）

| # | 返修项声明 | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | 补 using System.Text.Json 消除 CS0103 | MutationSurvivorTriageTests.cs:2 using 在位，:47/:64 两处 JsonSerializer 用法可解析；StdoutTests 无 Json 用法不受影响；返修 amend 进 arch/18 tip（2877d8e），diff 仅 2 文件 3 增 1 删 | 证实 |
| 2 | build.yml ls|head 改 find 消除 SC2012 | build.yml:461 find StrykerOutput -type f -name mutation-report.html | sort | tail -1 + :463 test -n 守卫；全文件 ls -t 零命中 | 证实 |
| 3 | 报告含再质检 + 自质检 + 声明→证据→结论 | 报告 :7-11 再质检表、:34-42 自检表 | 证实 |
| 4 | CI 证据（verify 绿 / Workflow Lint 绿 / stryker 重跑） | 报告自述为待办；gh run list 无返修后新 run | 待大脑 CI 阶段 |

**返修复核结论：✅ 源码修复通过；登记 done 的剩余前置 = 全栈 CI 编译绿 + stryker workflow_dispatch 重跑数字回填（见 README Frontier）。**
> 终验（2026-09-05）：PR #45 全栈绿（run 33963823146），本票 ✅ done。
