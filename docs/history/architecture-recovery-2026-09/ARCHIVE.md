# ARCHIVE — 导读 / Archive Guide

**定位声明**：本目录是**过程档案（process archive）**——记录 architecture-recovery
各轮票单（2026-08 → 2026-09）如何被派发、交付与复核：issues / handoffs /
prompts / reviews / reports / spec / research / WORKFLOW。它**不是**用户文档、
**不是** agent 指令、**不是**入门导读材料。要了解系统现状，请读
`docs/architecture.md`、`AGENTS.md` 与 `docs/adr/`——那是 living document。
本目录记录的是"工作如何做出来、如何被验收"的过程证据，包含已被后续决策取代的
历史表述；条目 append-only，不会回写以匹配系统现状。

## 唯一入口（Single entry point）

按此顺序读：

1. **`SUMMARY.md`** —— Round 1 收口（票 01-08）：范围、结果、合并拓扑。
2. **`SUMMARY-round2-09-10.md` → `SUMMARY-round3-11-16.md` → `SUMMARY-round4-17-25.md`** —— 逐轮收口摘要。
3. **`round-6-7-summary.md`** —— Round 6/7 收口（票 36-45）、Wave 6.1 违规裁定、v0.12.0 发布结果。
4. **`reports/`** —— 逐票交付报告（`NN-name.md`），票级证据索引。
5. **`reviews/`** —— 大脑逐票复核记录；**`issues/` / `handoffs/` / `prompts/`** —— 逐票立项与派发件。
6. **`spec.md` / `spec-round4.md`** —— 各轮立项 spec；**`research/`** —— 驱动后续轮次的 atomcode 调研记录。

## 目录说明

- `README.md` —— 工作区总控表的归档快照（Wave 1-8 + 后续日期回填条目）。
- `README-round4.md` —— Round 4 时点快照。
- `WORKFLOW.md` —— 各轮执行所依据的过程规则（冻结副本；Round 8 现役副本在 round-8 工作区）。
- `RESTORE-NOTE.md` —— `.scratch` 树恢复 provenance 记录。
- `design-review-research.md` / `ui-audit.md` / `readme-display-grill-2026-08-29.md` —— 独立会话记录。
- `architecture-recovery-2026-09/` —— 本目录自身索引的嵌套历史副本（归档自引用，原样保留）。

## 覆盖边界（Coverage boundary）

票级文件（issues/handoffs/prompts/reviews/reports）在本目录跟踪到票 25，
Round 6/7 的票级记录以 `round-6-7-summary.md` 收口。Round 5/6/7 的逐票报告
（票 26-45）仅存于冻结的 `.scratch/architecture-recovery/` 工作区——该目录按
设计被 gitignore（D-006），不随 clone 分发。因此 tracked 顶层文档对这些报告
一律用描述性引用（"the ticket-NN report, architecture-recovery process
archive"）而非路径链接。这些轮次的 tracked 结果记录由上面的收口摘要链承载。
