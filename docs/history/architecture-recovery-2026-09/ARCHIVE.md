# ARCHIVE — 导读 / Archive Guide

**定位声明：**本目录是**过程档案（process archive）**的入口层。它记录 architecture-recovery
各轮票单（2026-08 → 2026-09）的沉淀结论与单入口导读；它**不是**用户文档、不是 agent 指令、不是入门导读材料。要了解系统现状，请读
`docs/architecture.md`、`AGENTS.md` 与 `docs/adr/`——那是 living document。

**2026-10-14 治理清理边界：**票级过程散件（issues / handoffs / prompts / reviews / 逐票 reports 与原始捕获）已退役删除，结论均己沉淀于下面的 SUMMARY 链与 `docs/adr/`；原文在 git 历史中永久可查（tag `cleanup-baseline`）。保留的是入口层与仍被活文档具名引用的报告件。

## 唯一入口（Single entry point）

1. **`SUMMARY.md`** —— Round 1 收口（票 01-08）：范围、结果、合并拓扑。
2. **`SUMMARY-round2-09-10.md` → `SUMMARY-round3-11-16.md` → `SUMMARY-round4-17-25.md`** —— 逐轮收口摘要。
3. **`round-6-7-summary.md` / `round-8-summary.md`** —— 票 36-45 与 Round 8（47-52）收口。
4. **`spec.md` / `spec-round4.md`** —— 各轮立项 spec；**`WORKFLOW.md`** —— 过程规则冻结副本。
5. **`README.md` / `README-round4.md` / `RESTORE-NOTE.md`** —— 工作区总控表快照与恢复记录。

## 仍被活文档具名引用的保留件

- `reports/12-write-path-state-machine-tests.md`、`reports/13-mutation-testing-gate.md`、`reports/18-survivor-registry.json`、`reports/50-survivor-registry.md`（2026-10-14 从 .scratch 迁入，被 stryker-config/测试注释引用）。

## 趆盖边界（Coverage boundary）

票级文件的结论由 SUMMARY 链承载；需查逐票明细时用 git 历史（`git log cleanup-baseline -- docs/history`）。本目录不再接受新票级文件——新过程记录的沉淀出口是上述收口文档与 ADR。
