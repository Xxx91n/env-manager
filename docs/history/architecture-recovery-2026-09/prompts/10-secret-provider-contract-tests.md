# 窗口启动器 — 票 10：SecretProvider 契约测试套件

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 10 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/10-secret-provider-contract-tests.md
- .scratch/architecture-recovery/issues/10-secret-provider-contract-tests.md
- .scratch/architecture-recovery/spec.md（Phase 2 段）
- .scratch/architecture-recovery/WORKFLOW.md
- .scratch/architecture-recovery/research/secret-provider-patterns.md
- tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs

## 本票 delta

- Blocked by：09（须已收口，reviews/09）。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 契约断言只经 harness 表达，不 import 任何生产 provider 实现；DPAPI 子类走真实后端。
- 已有 fail-closed 钉住测试在 ProfileSeamValidationTests.cs：评估迁入契约或保留原位，勿重复。
- 交付报告落盘 .scratch/architecture-recovery/reports/10-secret-provider-contract-tests.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by（09）是否已收口 + 上面必读清单的标题，确认无阻塞后再动手。
