# 13 — 变异测试闸门（Stryker.NET，本地/PR 辅助）

**What to build:** 落地 Stryker.NET 变异测试配置，把 mutate 范围收敛到红线代码，产出存活变异清单并人工审查分类，验证"保护红线的测试真的能杀死变异"。因 .NET 10 管线摩擦，作本地/PR 辅助闸门而非 CI 硬门。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/13-mutation-testing-gate.md)

- [x] 落地 stryker 配置：mutate 红线四文件、ignore string/logical、thresholds high85/low70/break60、reporters html/progress
- [x] 在 net10 本地跑通一次变异分析，产出存活变异清单并人工审查分类（等价变异 / 缺失断言两类）
- [x] 红线测试（WritePathSeamTests / ProfileSeamValidationTests）对存活变异的 kill 率与红线清单一一对应
- [x] 记录 .NET 10 管线摩擦（#3351/#3367 式）并写结论：本地/PR 辅助，不上 CI 硬门
- [x] 报告落盘 `.scratch/architecture-recovery/reports/13-mutation-testing-gate.md`，每条验收附当场命令输出
