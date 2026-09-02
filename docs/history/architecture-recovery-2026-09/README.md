# architecture-recovery — 总控

重建日期：2026-08-31（原文件随 .scratch 树外部清理丢失，此为重建版；全套 issues/handoffs/prompts/reviews/spec/巡检报告已于同日恢复完整）。
（2026-09-01：票05 子窗口因 undo 链回滚事故二次恢复本表；票05 状态按子窗口回报更新。）
（2026-09-02：追加 Wave 6/7 —— SecretProvider 拆分 + 契约测试套件，由 atomcode 调研驱动。）
（2026-09-03：票 09/10 大脑会话复核通过，登记 done；下一波无票可派。）

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
| Wave 6 | **09** SecretProvider.cs 按 provider 拆模块 | ✅ done（reviews/09：dotnet test 86 基线、门禁 3 文件 50/50、8 提交、旧文件删除、引用清零，全部当场复验） |
| Wave 7 | **10** SecretProvider 契约测试套件 | ✅ done（reviews/10：dotnet test 106 通过+14 跳过=120、DPAPI L0 全绿、7 Skip 挂载、合规闸门，全部当场复验；过程违规 1 条见 reviews/10） |

## Frontier（当前可开工）

- **全部 10 票收口，无下一波可派票。**
- 剩余为未立项 backlog（待用户决定是否立票）：① CLI `profile create --help` 解析缺失；② Program.cs 441 行 CliRuntime 拆出；③ ValidateProfiles 悬空 launch target 硬阻断降级评估；④ CI 用户态隔离；⑤ architecture.md canary/golden 段；⑥ 用户侧注册表残留 `EM_TEST_DST=v1`。
- 合并期（待用户指令）：arch/09 与 arch/10 两分支按序合入主线并 push（遵循 WORKFLOW §4.2，`but` 唯一写入口）。

波次规则：同波内可并行开窗；一票 DoD 达成（大脑核验）后才解锁下游票。
