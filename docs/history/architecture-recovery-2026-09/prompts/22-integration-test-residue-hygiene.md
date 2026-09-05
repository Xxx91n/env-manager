# 窗口启动器 — 票 22：集成测试残留卫生（补偿式清理 + 用户自清文档化）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张实施票的独立执行窗口，只对票 22 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/22-integration-test-residue-hygiene.md
- .scratch/architecture-recovery/issues/22-integration-test-residue-hygiene.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；涉及注册表夹具的实跑只经 CI 的 test-with-restore 路径。
- 本票不执行用户机器上的实际删除（用户侧操作）。
- 交付报告落盘 .scratch/architecture-recovery/reports/22-integration-test-residue-hygiene.md。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
