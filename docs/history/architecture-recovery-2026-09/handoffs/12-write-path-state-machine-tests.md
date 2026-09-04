# Handoff 12 — 写路径状态机模型测试

## 目标

对写路径核心（rename/change-scope/set/delete/PATH add/remove）落地状态机模型测试：模型与引擎同步推进、随机 1000 步、收缩到最小反例，钉住 write-verify-delete 顺序、保护变量拒写与广播时机。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/12-write-path-state-machine-tests.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段模型测试决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（A2）
- 红线：`docs/agents/hard-boundaries.md`（rename write-verify-delete、保护变量拒写、广播时机）

## 现状勘察（开场用 rg 盘点）

- `src/VariableRename.cs` / `src/VariableChangeScope.cs` / `src/VariableWrite.cs`：写路径核心。
- `src/InMemoryScope.cs`：字典双域模型 + 广播计数，是模型的理想参照。
- `src/ProtectionCommand.cs`：IsProtectedVariable / IsProtectedPathEntry 保护判定。
- `tests/EnvManager.Engine.Tests/WritePathSeamTests.cs`：现有手写 seam 用例，作红线对照。
- 选库：CsCheck（C# 原生，stateful+parallel）优先；FsCheck 的 Machine API 标注 Experimental、无 semver 承诺（调研已核验）。

## 检查点

A 选库定案（CsCheck 优先，记录 FsCheck Experimental 备注）→ B Machine 骨架 + 6 操作（Run 更新模型 / Check 断言模型==终态）→ C 模型同步 + 最小反例收缩 → D 人为"先删后写"验红灯 → E 广播时机断言 + CI。

## 完成定义

issues/12 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/12-write-path-state-machine-tests.md`，每条验收附当场命令输出。
