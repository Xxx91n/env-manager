# Handoff 22 — 集成测试残留卫生（补偿式清理 + 用户自清文档化）

## 目标

测试 harness 补偿式清理其写入的注册表值；新增残留自检命令；文档化用户自清步骤。本票不执行用户机器上的实际删除。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/22-integration-test-residue-hygiene.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）
- 红线：docs/agents/hard-boundaries.md（注册表变异的测试必须走 test-with-restore 夹具）

## 现状勘察（开场用 rg/grep 盘点）

- 用户侧残留 EM_TEST_DST=v1（HKCU Environment，用户自清，非泄漏）；scripts/test-with-restore.ps1 差分对账块（约 L517–528）为残留归零断言的挂载点；差分套件挂载闸门 EM_DIFFERENTIAL_ORACLE。

## 检查点

A 对账块补「残留归零」断言（前后快照 diff 仅含登记值）→ B 新增残留自检命令/脚本 → C 文档用户自清步骤 → D 交大脑触发 CI → E 报告。
验证纪律（CI-only）：同票 17 检查点的验证纪律句；本票涉及注册表夹具，任何实跑只经 CI 的 test-with-restore 路径。

## 完成定义

issues/22 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/22-integration-test-residue-hygiene.md，每条验收附证据。
