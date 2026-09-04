# 窗口启动器 — 票 12：写路径状态机模型测试

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 12 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/12-write-path-state-machine-tests.md
- .scratch/architecture-recovery/issues/12-write-path-state-machine-tests.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 选库 CsCheck 优先；FsCheck 的 Machine API 标注 Experimental、无 semver（详见 handoff 现状勘察）。
- "先删后写"人为注入验红灯是强制验收形态；广播时机断言"apply 仅实际写入广播 1 次"。
- 模型必须与引擎同步推进并收缩到最小反例，非随机乱跑。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/12-write-path-state-machine-tests.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
