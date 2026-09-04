# 窗口启动器 — 票 15：Testcontainers L1 矩阵

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 15 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/15-secret-provider-testcontainers-l1.md
- .scratch/architecture-recovery/issues/15-secret-provider-testcontainers-l1.md
- .scratch/architecture-recovery/spec.md（Phase 3 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 首步必须验证 CI runner 的 Docker 可用性（本票唯一未就地验证假设，Linux 容器 runner 优先）。
- 镜像钉扎见 handoff 现状勘察；7 个 Skip 挂载转真后端，每 provider 至少一条冒烟。
- 全程无云凭据；secrets 不进注册表红线不因模拟器而放宽。
- 如需进一步调研决策，可依赖 atomcode-research（串行、一次一个在途）；权威结论见 research/next-wave-patterns.md + research/secret-provider-patterns.md + spec Phase 3。
- 交付报告落盘 .scratch/architecture-recovery/reports/15-secret-provider-testcontainers-l1.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
