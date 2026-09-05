# 窗口启动器 — 票 20 返修：src 修复 hunk 归位到本票分支

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张返修票的独立执行窗口，只对票 20 返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/20-profile-create-help-parsing.md（大脑复核结论，返修依据）
- .scratch/architecture-recovery/handoffs/20-profile-create-help-parsing.md
- .scratch/architecture-recovery/issues/20-profile-create-help-parsing.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta（返修项）

- Blocked by：无 — 大脑复核已给出返修清单。
- 开工第一动作（再质检）：git log --all -S IsProfileCreateHelp -- src/ProfileCommand.cs 复验（应为仅 445a0d9「feat(preflight)…(issue 19)」），并核对 src/ProfileCommand.cs:855-870 实况，确认属实后才动手。
- 返修项 1（唯一）：把 IsProfileCreateHelp 及接线（src/ProfileCommand.cs 相关 hunk）从票 19 的提交拆出，归位到票 20 分支提交——使 arch/20 完整自含「src 修复 + 回归测试」；不触碰票 19 其它改动；行为零变化；操作仅经 GitButler CLI，禁裸 git 写命令（参见上方版本控制行）。。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不扩大原验收边界。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；由大脑推 CI 验证分支取绿，窗口不本地自证。
- 修复报告落盘 .scratch/architecture-recovery/reports/20-profile-create-help-parsing-fix.md：含「声明 → 证据 → 结论」对照表（返修项 + 归位前后分支状态）+ 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
