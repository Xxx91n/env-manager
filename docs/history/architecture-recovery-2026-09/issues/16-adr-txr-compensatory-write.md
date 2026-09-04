# 16 — ADR：禁止 TxR/TxF，制度化补偿式写入范式

**What to build:** 新增 ADR，把 TxR/TxF 定为非目标（官方弃用、可能在未来 Windows 移除、替代清单无注册表多值事务原语），并把"补偿式写入 + 三层锁 + 审计恢复"制度化为唯一可持续路线，同步对齐红线文档。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/16-adr-txr-compensatory-write.md)

- [x] 新增 `docs/adr/0014-*.md`：TxR/TxF 非目标（官方弃用依据 + 替代范式）+ 补偿式写入/三层锁/审计恢复制度化
- [x] `docs/agents/hard-boundaries.md` 与 AGENTS.md 相关段同步引用新 ADR
- [x] rename write-verify-delete、apply 备份保留列为变异/模型测试首批靶点（引用不重复实现）
- [x] 文档三层一致性：CONTEXT.md / docs/adr/ / 代码现状对齐
- [x] 报告落盘 `.scratch/architecture-recovery/reports/16-adr-txr-compensatory-write.md`，每条验收附当场命令输出
