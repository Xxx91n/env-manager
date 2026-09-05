# 窗口启动器 — CI 集成首跑红：set+get+delete round-trip set failed（票 22+24 联合返修）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张联合返修票的独立执行窗口，只对本次 CI 集成返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/22-integration-test-residue-hygiene.md
- .scratch/architecture-recovery/reviews/24-ci-user-state-isolation.md
- .scratch/architecture-recovery/handoffs/22-integration-test-residue-hygiene.md
- .scratch/architecture-recovery/handoffs/24-ci-user-state-isolation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md

## 本票 delta（联合返修项）

- Blocked by：无 — 大脑 CI 复核发现集成首跑红。
- 失败证据：PR #42（run 33956994940）与 PR #43（run 33957010102）同点红——Run Pester integration tests →「[test] set+get+delete round-trip ... FAIL: set failed」；该步此前从未跑到（先前红在编译），全栈代码首次进入 Pester 层即暴露。
- 大脑已证事实（开工先再质检复核，属实才动手）：①Invoke-Cli（scripts/test-with-restore.ps1:266-271）把 CLI stderr 丢进 2>$null——失败时零诊断输出；②失败仅发生在固定名 EM_TEST_FOO 上，而 rename 契约用带戳新名（EM_TEST_SRC_$Stamp）的同类 set 通过；③EM_TEST_FOO 在 HKCU 预存（同 run 前序套件写入，harness 快照日志明示 predate this run）。
- 返修项 1：Invoke-Cli 失败时保留并打印 CLI stderr（临时文件重定向或等效），让任何红都有当场诊断输出。
- 返修项 2：定位 set EM_TEST_FOO 在「名字已预存」时退出非零的根因（优先排查：写路径 value-kind 策略、票 24 用户态 seam 下的审计/保护存储路径；用返修项 1 的输出做实锤，禁止猜测定论）。
- 返修项 3：修复根因；若根因是固定名与残留碰撞的反模式，则 round-trip 改用带戳名（与 rename 契约同型），并保持 residue-zero 断言不变。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不改动票 22/24 的原验收边界。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；修复后回报大脑，由大脑推送并触发 PR #42/#43 复跑取绿，窗口不本地自证、不自行推送。
- 修复报告落盘 .scratch/architecture-recovery/reports/ci-integration-first-run-fix.md：含「声明 → 证据 → 结论」对照表（三条已证事实 + 返修项逐条）+ 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
