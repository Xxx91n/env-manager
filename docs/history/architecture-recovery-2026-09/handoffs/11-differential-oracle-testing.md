# Handoff 11 — 差分测试：以真实 Windows 语义为 oracle

## 目标

把 InMemoryScope 的"忠实于 Windows"从假设变成被钉住的事实：同一操作序列分别跑 InMemoryScope 与 RegistryScope，终态 + 广播次数一致。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/11-differential-oracle-testing.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段差分测试决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（A1）
- 红线：`docs/agents/hard-boundaries.md`（真实注册表隔离、system scope 需 elevation）

## 现状勘察（开场用 rg 盘点）

- `src/InMemoryScope.cs`：现有字典双域（user/system）语义实现 + 广播计数。
- `src/RegistryScope.cs`：真实注册表 + WM_SETTINGCHANGE P/Invoke 生产适配器。
- `scripts/test-with-restore.ps1`：apex 夹具（真实注册表 + 备份回滚），差分 oracle 要复用它。
- `tests/EnvManager.Engine.Tests/WritePathSeamTests.cs`：已用 InMemoryScope 的 seam 行为用例，作对照基准。
- 语义矩阵点位（调研已核验）：REG_EXPAND_SZ 保留 %VAR% 不预展开；PATH 1024~30000 字符边界；空条目=当前目录；变量名含 `=` 拒绝；system scope 写需 elevation。

## 检查点

A 盘点语义矩阵点位（对齐 rg 出的当前实现）→ B 差分夹具骨架（同操作脚本双跑）→ C 语义矩阵逐条落地断言 → D 人为注入漂移验红灯 → E CI windows-latest 接驳（隔离真实注册表污染）。

## 完成定义

issues/11 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/11-differential-oracle-testing.md`，每条验收附当场命令输出。
