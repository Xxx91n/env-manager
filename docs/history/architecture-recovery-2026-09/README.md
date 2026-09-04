# architecture-recovery — 总控

重建日期：2026-08-31（原文件随 .scratch 树外部清理丢失，此为重建版；全套 issues/handoffs/prompts/reviews/spec/巡检报告已于同日恢复完整）。
（2026-09-01：票05 子窗口因 undo 链回滚事故二次恢复本表；票05 状态按子窗口回报更新。）
（2026-09-02：追加 Wave 6/7 —— SecretProvider 拆分 + 契约测试套件，由 atomcode 调研驱动。）
（2026-09-03：票 09/10 大脑会话复核通过，登记 done；追加 Wave 8 —— 测试心智模型升级三件套 + 补强，atomcode 调研驱动，依据 research/next-wave-patterns.md。）
（2026-09-04：Wave 8 六票（11–16）大脑会话复核通过，登记 done；reviews/11..16 全量落盘；分支合入 origin/main 待用户授权。）

## 波次与状态（由 issue Blocked by 推导 + 大脑验收结论）

| 波次 | 票 | 状态 |
|---|---|---|
| Wave 1 | **01** 引擎 seam（expand） | ✅ done（reviews/01） |
| Wave 1 | **02** xUnit 测试泳道 | ✅ done（reviews/02） |
| Wave 2 | **03** 写路径迁移到 seam | ✅ done（reviews/03） |
| Wave 2 | **07** launch 注入生效验证 | ✅ done（reviews/07 复验通过） |
| Wave 2 | **08** IPC schema 契约 | ✅ done（reviews/08 复验通过） |
| Wave 3 | **04** profile/secret 迁移 + ADR 0010 修订 | ✅ done（reviews/04 二轮复验通过） |
| Wave 4 | **05** Program.cs 命令域拆模块 | ✅ done（reviews/05 复验通过） |
| Wave 5 | **06** EnvFeatures 五域分家 | ✅ done（reviews/06 复验通过） |
| Wave 6 | **09** SecretProvider.cs 按 provider 拆模块 | ✅ done（reviews/09） |
| Wave 7 | **10** SecretProvider 契约测试套件 | ✅ done（reviews/10） |
| Wave 8 | **11** 差分测试（Windows 语义为 oracle） | ✅ done（reviews/11：差分 oracle 11/11 夹具实跑 + 5 语义矩阵点 + 红灯反证，当场复验） |
| Wave 8 | **12** 写路径状态机模型测试 | ✅ done（reviews/12：6 操作 Machine + 1000 步模型同步 + delete-then-write 红灯反证，当场复验） |
| Wave 8 | **13** 变异测试闸门（Stryker.NET） | ✅ done（reviews/13：stryker 4.16.0 net10 重跑 96 受测 78 kill；本地/PR 辅助闸门定位成立） |
| Wave 8 | **14** CLI 输出快照化（Verify） | ✅ done（reviews/14：17 快照 + 10 locale i18n 快照；vitest 430 通过；两处报告数字漂移已记录） |
| Wave 8 | **15** Testcontainers L1 矩阵（7 个 Skip 转真） | ✅ done（reviews/15：verify-l1 CI PASS 经 gh 闭环；合入期 fold 2 hunks，uq 与 Directory.Build.props 去重） |
| Wave 8 | **16** ADR 禁止 TxR/TxF，制度化补偿式写入 | ✅ done（reviews/16：ADR 0014 引语与 MS Learn 现文逐字核对；18/18 证据归属修正） |

## Frontier（当前可开工）

- **无在库 ready-for-agent 票**：11–16 全部收口（大脑核验通过），下一波候选须从「横切登记」新立票（17+），是否立票由用户决定。
- **合并期（待用户明确指令）**：arch/13→11（11 叠于 13）、arch/12、arch/14、arch/16、arch/15 六支均未合入 origin/main（tip 仍 fb9c065）；票 15 的两处 parked hunks（AGENTS.md L1 段 ws、csproj 包块 uq）须在 arch/12、arch/14 合入后 fold，且 uq 与已提交的 tests/EnvManager.Engine.Tests/Directory.Build.props 重复声明同组包——fold 时保留 Directory.Build.props 为唯一事实源、丢弃 uq。

## 横切登记（未立项 backlog，独立决策，不并入本波）

- CLI `profile create --help` 解析缺失；Program.cs 441 行 CliRuntime 拆出；ValidateProfiles 悬空 launch target 硬阻断降级评估；CI 用户态隔离；architecture.md canary/golden 段；用户侧注册表残留 `EM_TEST_DST=v1`。
- 调研推迟项（不立票，见 spec Phase 3 Further Notes）：SharpFuzz LenientArgs 模糊、Coyote 并发 spike、GUI E2E 升级、fnox 式 profile export。

波次规则：同波内可并行开窗；一票 DoD 达成（大脑核验）后才解锁下游票。
