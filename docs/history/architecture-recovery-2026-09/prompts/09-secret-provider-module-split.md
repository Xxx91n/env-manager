# 窗口启动器 — 票 09：SecretProvider.cs 按 provider 拆模块

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 09 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/09-secret-provider-module-split.md
- .scratch/architecture-recovery/issues/09-secret-provider-module-split.md
- .scratch/architecture-recovery/spec.md（Phase 2 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta

- Blocked by：无 — 主线已收口合入 origin/main，可直接开工。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。
- 本票是文件搬迁纯重构（票 05/06 同型）：node 字符串切片搬运、写前备份 OS temp、写后校验片段探针。
- 前端 4 个门禁测试文件持 SecretProvider.cs 读路径：拆分前 rg 列全量、随拆同步重指向。
- 集成脚本跑前必须刷新 release/cli-only 产物（票 04 B1 教训）。
- 交付报告落盘 .scratch/architecture-recovery/reports/09-secret-provider-module-split.md，验收项逐条附当场命令输出。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
