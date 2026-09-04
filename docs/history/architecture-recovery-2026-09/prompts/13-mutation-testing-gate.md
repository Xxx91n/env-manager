# 窗口启动器 — 票 13：变异测试闸门（Stryker.NET）

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 13 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/13-mutation-testing-gate.md
- .scratch/architecture-recovery/issues/13-mutation-testing-gate.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 本票交付是"本地/PR 辅助闸门"，不上 CI 硬门（.NET 10 管线摩擦，详见 handoff 现状勘察）。
- mutate 范围收敛到红线四文件；存活变异人工审查分类（等价变异 / 缺失断言）是核心交付，非跑分。
- 追 100% 变异分是被明确否定的反模式（调研已核验）。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/13-mutation-testing-gate.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
