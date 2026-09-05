# Handoff 20 — profile create --help 解析修复（help 不当 profile 名落库）

## 目标

profile create 把 --help 及其变体识别为帮助请求，不落库；回归测试钉住。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/20-profile-create-help-parsing.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）
- 红线：docs/agents/hard-boundaries.md（profiles.json 写路径段）

## 现状勘察（开场用 rg/grep 盘点）

- ProfileCommand.cs 的 profile create 参数解析分支（--help 被当 profile 名落库的 bug 路径）；LenientArgs tokenizer；其它命令的 help 契约作为对照。

## 检查点

A 定位解析分支与对照命令的 help 契约 → B 修复（--help / -h 变体）→ C 回归测试（xUnit 或 CLI 快照层）→ D i18n/文档同步 → E 交大脑触发 CI。
验证纪律（CI-only）：同票 17 检查点的验证纪律句。

## 完成定义

issues/20 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/20-profile-create-help-parsing.md，每条验收附证据。
