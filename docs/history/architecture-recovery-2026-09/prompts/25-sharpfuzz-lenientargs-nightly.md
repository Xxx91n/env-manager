# 窗口启动器 — 票 25：SharpFuzz 夜间模糊（参数解析面 + corpus 入库）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 25 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/25-sharpfuzz-lenientargs-nightly.md
- .scratch/architecture-recovery/issues/25-sharpfuzz-lenientargs-nightly.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/research/round4-closeout-patterns.md（B 节）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：票 18（变异测试幸存者分诊）——先确认其完成再开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；模糊跑只经 CI。
- 调研决策可依赖 research/round4-closeout-patterns.md（B 节）；工具链细节如需补充用 $atomcode-research 串行单发。
- 交付报告落盘 .scratch/architecture-recovery/reports/25-sharpfuzz-lenientargs-nightly.md。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
