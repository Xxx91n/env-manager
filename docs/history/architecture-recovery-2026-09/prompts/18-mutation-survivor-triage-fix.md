# 窗口启动器 — 票 18 返修：编译错误 + workflow lint

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张返修票的独立执行窗口，只对票 18 返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/18-mutation-survivor-triage.md（大脑复核结论，返修依据）
- .scratch/architecture-recovery/handoffs/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/issues/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta（返修项）

- Blocked by：无 — 大脑复核已给出返修清单。
- 开工第一动作（再质检）：对照 reviews/18 每条「证据」回仓库实物复验（rg/读文件/gh run 日志），确认属实后才修复；发现复核结论有误则停下回报，不擅自改范围。
- 返修项 1：tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs 缺 using System.Text.Json（CI 报 CS0103 ×2 于 :46/:63，run 33944493443 日志）。
- 返修项 2：.github/workflows/build.yml:460 stryker job「Per-module mutation scores」步骤 ls -t … | head -1 触发 actionlint SC2012 → 改 find 等效实现（run 33944493448 日志）。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不扩大原验收边界。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；修复后回报大脑，由大脑推 CI 验证分支取绿（verify + Workflow Lint 双绿，随后 stryker workflow_dispatch 重跑回填趋势数字），窗口不本地自证。
- 修复报告落盘 .scratch/architecture-recovery/reports/18-mutation-survivor-triage-fix.md：含「声明 → 证据 → 结论」对照表（每条返修项 + 原始 issue 验收项）+ 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
