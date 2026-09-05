# Handoff 18 — 变异测试幸存者分诊 + 登记 + 模块化报告（CI 可跑）

## 目标

对 Stryker 基线做结构化幸存者分诊：归类、登记、补断言、模块分算、CI 可跑、趋势记录；不追 100%。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/18-mutation-survivor-triage.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）+ research/round4-closeout-patterns.md（C 节）
- 闸门：stryker-config.json（mutate 四红线文件、thresholds 85/70/60、ignore string/logical——不得改动）

## 现状勘察（开场用 rg/grep 盘点）

- 基线：96 受测 / 78 kill / 14 survived / 4 timeout / 40.00%（大脑 2026-09-04 当场重跑；较票 13 报告 76/94 增长源于测试套件 131 增长）。
- 缺失断言幸存者 16 条已在横切登记；.config/dotnet-tools.json（dotnet-stryker 4.16.0）；tests/EnvManager.Engine.Tests 现有 seam/差分/状态机套件为补断言先例。

## 检查点

A 复现当前输出并逐条导出幸存者清单 → B 每条归类（no coverage / weak assertion / equivalent）→ C 登记文件落盘（含 LLM 检测预留字段）→ D 非等价者补缺失断言（边界条件优先）→ E Stryker 接 CI（workflow_dispatch 短跑 job 或等效，输出模块分算）→ F 交大脑触发 CI 重跑，趋势对比落盘。
验证纪律（CI-only）：同票 17 检查点的验证纪律句；Stryker 重跑也走 CI，窗口不本地自证。

## 完成定义

issues/18 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/18-mutation-survivor-triage.md，登记表与两次跑分数字附证据。
