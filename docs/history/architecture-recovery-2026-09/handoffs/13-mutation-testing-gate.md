# Handoff 13 — 变异测试闸门（Stryker.NET）

## 目标

落地 Stryker.NET 变异测试，收敛 mutate 范围到红线代码，产出存活变异清单并人工审查分类，验证"保护红线的测试真的能杀死变异"。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/13-mutation-testing-gate.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段变异测试决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（A3）
- 红线：`docs/agents/hard-boundaries.md`（红线清单是存活变异审查的对应表）

## 现状勘察（开场用 rg 盘点）

- 红线四文件：`src/VariableRename.cs` / `src/VariableChangeScope.cs` / `src/ProfileEffective.cs` / `src/ProtectionCommand.cs`。
- 红线测试：`tests/EnvManager.Engine.Tests/WritePathSeamTests.cs` + `ProfileSeamValidationTests.cs`。
- 工具链：`.NET 10`（`dotnet --version` 确认）；Stryker v5 起要求 dotnet10 runtime，管线有 #3351/#3367 式摩擦（调研已核验）。

## 检查点

A stryker-config 落地（mutate 四文件、ignore string/logical、thresholds high85/low70/break60、reporters html/progress）→ B 本地跑通 + 存活变异清单 → C 人工审查分类（等价变异 / 缺失断言）→ D 摩擦记录 + 结论（本地/PR 辅助，不上 CI 硬门）。

## 完成定义

issues/13 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/13-mutation-testing-gate.md`，每条验收附当场命令输出。
