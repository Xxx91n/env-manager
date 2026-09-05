# 窗口启动器 — 票 19：预检验证两级降级（error/warn + --strict + 退出码 2）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 19 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/issues/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/research/round4-closeout-patterns.md（D 节）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点。
- 调研决策可依赖 research/round4-closeout-patterns.md（D 节）；如需补充调研用 $atomcode-research 串行单发。
- 退出码契约变更必须全链同步（CLI 文档/GUI 对齐表/AGENTS.md），不可只改代码。
- 交付报告落盘 .scratch/architecture-recovery/reports/19-preflight-two-tier-validation.md。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
