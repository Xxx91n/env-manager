# 窗口启动器 — 测试基建联合返修：静态 seam 跨集合并行竞态（票 14/18/19）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张联合返修票的独立执行窗口，只对本次测试基建串行化返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/reviews/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/handoffs/18-mutation-survivor-triage.md
- .scratch/architecture-recovery/handoffs/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md（测试纪律段）

## 本票 delta（联合返修项）

- Blocked by：无 — 大脑 CI 复核发现全栈首跑红。
- 失败证据：PR #44 修正链 CI（run 33961788030）→ Run C# engine unit tests → ProfileSeamValidationTests.Preflight_GlobalInheritsPlainGlobal_Accepted [FAIL]（ProfileSeamValidationTests.cs:140，System.InvalidOperationException: Sequence contains no matching element）。
- 大脑根因诊断（开工先再质检复核，属实才动手）：Program.SetProfilesFilePathForTests 是静态全局指针，被 MutationSurvivorTriageTests(:28/:34)、ProfileSeamValidationTests(:34/:42)、CliOutputSnapshotTests(:436/:443) 三个类并发翻转；其中仅 CliOutputSnapshotTests 与 ProfileCreateHelpTests 挂 "CliSnapshotSerial"（DisableParallelization 只串行化集合内部），18/19 两集合外裸奔——SeedStore 写入的路径被并行类覆盖，LoadProfiles 读错文件。此前 PR #42 未带票 20 且调度巧合未爆，全栈齐跑即暴露（时序型缺陷）。
- 返修项 1：rg 全量枚举所有调用 SetProfilesFilePathForTests / SetAppDataDirectoryForTests 的测试类，把全部缝互斥类纳入同一串行集合（单一 CollectionDefinition + DisableParallelization = true；可与现有 CliSnapshotSerial 合并或新建）。
- 返修项 2：保持每类自己的 per-test 临时目录与 finally 置 null 恢复纪律不变。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不扩大任何原票验收边界、不改被测代码行为。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；修复后回报大脑，由大脑推送触发 PR #44 复跑取绿，窗口不本地自证、不自行推送。
- 修复报告落盘 .scratch/architecture-recovery/reports/engine-test-seam-serialization-fix.md：含「声明 → 证据 → 结论」对照表（诊断再质检 + 返修项逐条）+ 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
