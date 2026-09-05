# 窗口启动器 — 票 18：变异测试幸存者分诊 + 登记 + 模块化报告（CI 可跑）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 18 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/issues/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/research/round4-closeout-patterns.md（C 节）
- stryker-config.json
- .config/dotnet-tools.json
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；Stryker 重跑也走 CI，本机不跑。
- 调研决策可依赖 research/round4-closeout-patterns.md（C 节）；如需补充调研用 $atomcode-research 串行单发。
- stryker-config.json 的 mutate 范围与 thresholds 不得改动。
- 交付报告落盘 .scratch/architecture-recovery/reports/18-mutation-survivor-triage.md，登记表与两次跑分附证据。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
