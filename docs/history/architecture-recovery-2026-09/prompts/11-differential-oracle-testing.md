# 窗口启动器 — 票 11：差分测试（Windows 语义为 oracle）

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 11 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/11-differential-oracle-testing.md
- .scratch/architecture-recovery/issues/11-differential-oracle-testing.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 差分夹具复用 scripts/test-with-restore.ps1；真实注册表操作必须经它隔离，禁裸 registry 写（红线）。
- 语义矩阵点位见 handoff「现状勘察」；每步断言"终态 + 广播次数"双一致。
- 人为漂移验红灯是强制验收形态（红灯可反证，非可选）。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/11-differential-oracle-testing.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
