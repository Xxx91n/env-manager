# 窗口启动器 — 票 14：CLI 输出快照化（Verify）

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 14 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/14-cli-output-snapshot-testing.md
- .scratch/architecture-recovery/issues/14-cli-output-snapshot-testing.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- i18n 走 ICU：单引号是转义符，快照渲染时勿把 {placeholder} 包进单引号（见 handoff 现状勘察）。
- scrubber 清 PID/时间戳等易变字段；canary 输出格式 <encrypted>/<revealed> 是快照目标。
- 目标是"人读契约"层，与 IPC golden 互补，不是替代。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/14-cli-output-snapshot-testing.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
