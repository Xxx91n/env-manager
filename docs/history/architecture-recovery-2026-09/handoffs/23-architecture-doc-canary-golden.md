# Handoff 23 — architecture.md 补 canary/golden 段（文档）

## 目标

architecture.md 增补 canary 零泄漏断言网与 golden/快照层描述段，文档指针同步；无代码行为变化。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/23-architecture-doc-canary-golden.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）
- 现状参照：tests/canary-redaction.Tests.ps1、CanaryRedactionTests、CliOutputSnapshotTests + 17 个 .verified.txt、docs/schemas 的 IPC golden

## 现状勘察（开场用 rg/grep 盘点）

- architecture.md 现有章节结构（IPC 桥、race、安全加固段）；canary 网实物（三 sink 扫描 + <encrypted>/<revealed> 占位断言）与 golden 层实物（CLI 快照 + IPC golden）位置。

## 检查点

A 定位插入点 → B 写段（内容与测试实物一致，不臆造）→ C docs/agents/reference-index.md 与 AGENTS.md 相关句同步 → D 交大脑触发 CI（doc-sync 检查绿）。
验证纪律（CI-only）：同票 17 检查点的验证纪律句。

## 完成定义

issues/23 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/23-architecture-doc-canary-golden.md，每条验收附证据。
