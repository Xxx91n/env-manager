# 窗口启动器 — 票 19 返修：exit-2 文档链收窄 + 报告计数修正

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张返修票的独立执行窗口，只对票 19 返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/19-preflight-two-tier-validation.md（大脑复核结论，返修依据）
- .scratch/architecture-recovery/handoffs/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/issues/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta（返修项）

- Blocked by：无 — 大脑复核已给出返修清单。
- 开工第一动作（再质检）：对照 reviews/19 每条「证据」回仓库实物复验（ProfileLaunch 无 exit-2 路径、三处文档行号），确认属实后才修复。
- 返修项 1：exit 2 语义收窄为仅 profile apply——docs/cli-commands.md:95、AGENTS.md:114、frontend/src-tauri/src/main.rs:498 注释三处「profile apply/launch」改为「profile apply」；docs/architecture.md 对齐表若有同类表述一并核对；不改代码行为。
- 返修项 2：报告 reports/19-preflight-two-tier-validation.md 的「8 个新 Fact」改为 9 并附修正记录段。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不扩大原验收边界。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；doc-sync 与文档门禁由大脑推 CI 验证分支取绿，窗口不本地自证。
- 修复报告落盘 .scratch/architecture-recovery/reports/19-preflight-two-tier-validation-fix.md：含「声明 → 证据 → 结论」对照表 + 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
