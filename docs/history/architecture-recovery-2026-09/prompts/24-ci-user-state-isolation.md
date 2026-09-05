# 窗口启动器 — 票 24：CI 用户态隔离（LOCALAPPDATA 重定向 + 快照语义纪律）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 24 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/24-ci-user-state-isolation.md
- .scratch/architecture-recovery/issues/24-ci-user-state-isolation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/research/round4-closeout-patterns.md（A 节）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：票 17（CI verify 内嵌 CLI 资源 staging）——先确认其完成（verify 绿）再开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点。
- 调研决策可依赖 research/round4-closeout-patterns.md（A 节）。
- 交付报告落盘 .scratch/architecture-recovery/reports/24-ci-user-state-isolation.md。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
