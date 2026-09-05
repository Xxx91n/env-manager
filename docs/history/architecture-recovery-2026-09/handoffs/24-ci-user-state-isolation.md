# Handoff 24 — CI 用户态隔离（LOCALAPPDATA 重定向 + 快照语义纪律）

## 目标

集成测试在 CI 中以隔离 LOCALAPPDATA 运行，用户态写入落在 job 私有目录；两级隔离纪律与 env-block 快照语义纪律文档化。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/24-ci-user-state-isolation.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）+ research/round4-closeout-patterns.md（A 节）
- 依赖：票 17（先让 verify 变绿，再动同 workflow 的隔离步骤）

## 现状勘察（开场用 rg/grep 盘点）

- build.yml verify job 的 Pester 集成步骤（run-ci-tests.ps1）；profiles.json 的用户态落点（LOCALAPPDATA）；票 17 完成后的 workflow 现状。

## 检查点

A 确认票 17 已完成（verify 绿）→ B Pester 步骤重定向 LOCALAPPDATA（job 私有目录）→ C 纪律文档化（两级隔离 + env-block 快照语义）→ D 交大脑触发 CI 取全绿证据 → E 报告。
验证纪律（CI-only）：同票 17 检查点的验证纪律句。

## 完成定义

issues/24 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/24-ci-user-state-isolation.md，每条验收附证据。
