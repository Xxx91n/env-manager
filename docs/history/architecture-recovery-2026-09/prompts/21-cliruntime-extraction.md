# 窗口启动器 — 票 21：CliRuntime 441 行拆出（纯搬迁，行为零变化）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 21 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/21-cliruntime-extraction.md
- .scratch/architecture-recovery/issues/21-cliruntime-extraction.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点。
- 文件搬运纯重构（票 05/06/09 同型）：字符串切片搬运、写前备份 OS temp、写后校验片段探针；禁 split/join 重组（WORKFLOW §6 教训）。
- 交付报告落盘 .scratch/architecture-recovery/reports/21-cliruntime-extraction.md。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
