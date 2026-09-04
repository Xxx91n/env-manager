# Handoff 16 — ADR：禁止 TxR/TxF，制度化补偿式写入

## 目标

新增 ADR，把 TxR/TxF 定为非目标（官方弃用、可能在未来 Windows 移除、替代清单无注册表多值事务原语），并把"补偿式写入 + 三层锁 + 审计恢复"制度化为唯一可持续路线，同步对齐红线文档。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/16-adr-txr-compensatory-write.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段 ADR 决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（B3）
- 红线：`docs/agents/hard-boundaries.md`（rename write-verify-delete、apply 备份保留、三层锁）

## 现状勘察（开场用 rg 盘点）

- `docs/adr/`：现有 0001–0013，新 ADR 编号 0014。
- `docs/agents/hard-boundaries.md`：红线清单（rename write-verify-delete、apply 备份保留、三层锁、审计恢复）。
- TxR/TxF 弃用依据：MS《Alternatives to using Transactional NTFS》（调研已核验，替代=ReplaceFile 式整写替换 + 安装器式协调）。

## 检查点

A ADR 草稿（弃用依据 + 替代范式 + 补偿式写入制度化）→ B hard-boundaries/AGENTS 同步引用 → C 靶点清单（变异/模型首批，引用不重复实现）→ D 三层文档一致（CONTEXT.md / docs/adr/ / 代码现状）。

## 完成定义

issues/16 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/16-adr-txr-compensatory-write.md`，每条验收附当场命令输出。
