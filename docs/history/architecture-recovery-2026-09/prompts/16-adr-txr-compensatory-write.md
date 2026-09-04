# 窗口启动器 — 票 16：ADR 禁止 TxR/TxF，制度化补偿式写入

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 16 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/16-adr-txr-compensatory-write.md
- .scratch/architecture-recovery/issues/16-adr-txr-compensatory-write.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 新 ADR 编号 0014；弃用依据 + 替代范式 + 补偿式写入制度化（详见 handoff 现状勘察）。
- 靶点清单是"引用"红线，不重复实现；不做任何代码变更。
- 三层文档一致（CONTEXT.md / docs/adr/ / 代码现状）是验收硬项。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/16-adr-txr-compensatory-write.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
