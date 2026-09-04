# architecture-recovery 收口摘要 — Round 3（2026-09-04，票 11–16）

> 母体文件：`.scratch/architecture-recovery/` 的 gitignored 工作副本；本目录（docs/history/architecture-recovery-2026-09/）是其 git 跟踪的权威归档。本摘要为 Round 3 的收口记录。

## 范围与结果

6 票全部收口并大脑复核通过（reviews/11..16）：差分测试（11）、写路径状态机模型测试（12）、变异测试闸门（13）、CLI 输出快照化（14）、Testcontainers L1 矩阵（15）、ADR 0014 禁止 TxR/TxF（16）。主线内容 = spec Phase 3 的「差分 + 模型化 + 变异」测试心智模型三件套 + 两处补强（快照 / L1 矩阵）+ 一处制度化（ADR 0014），由 atomcode 调研驱动（research/next-wave-patterns.md）。

## 终验留证（收口日当场复跑）

| 门 | 命令 | 结果 |
|---|---|---|
| 引擎全套件 | `dotnet test tests/EnvManager.Engine.Tests/ -c Release --nologo` | 131 通过 / 20 跳过 / 0 失败（151 总计） |
| 差分 oracle 夹具 | `pwsh -NoProfile -File scripts/test-with-restore.ps1` | 差分块 11/11 通过 + exact registry snapshots match + clean run |
| 前端 | `npx vitest run`（frontend/） | 40 文件 430 通过 |
| Rust | `cargo test --locked`（service / src-tauri） | 15/15、11/11 |
| 变异闸门 | `dotnet tool restore && dotnet stryker` | net10 跑通；96 受测，78 kill / 14 survived / 4 timeout，40.00%，低于 break 60 按设计退出（本地辅助闸门） |
| L1 CI 外部闭环 | `gh pr checks 39` | verify-l1 PASS（1m36s）；verify 失败步 = main 预存 Tauri 资源路径问题（run 33670504746 同一步，非本波引入） |

## 过程审计结论

- 交叉核对：README / reviews / reports 三方 6 票状态、判词、测试数字链全部一致；发现并如实记录 5 处数字/措辞漂移（票 14 叶子 key 439→456、字节数 236,899→276,727；票 15 YAML timeout 30→45；票 11 行数 486→485；票 13 基线 76/94→96 受测 78 kill 时效漂移）。
- 三层文档一致性：CONTEXT.md 补登 round 3（issues 11-16）一段；ADR 0014 已在 CONTEXT.md 索引 / hard-boundaries.md / AGENTS.md 三处引用；docs/architecture.md 已有 L1 Emulator Matrix 小节；代码实物锚点（write-verify-delete、三层锁、审计 ledger）经子代理逐处核验为真。
- 过程事故均呈报不追认：①六支分支远端 ref 全部存在，但票 12/13/14/16 报告自称「不 push/未 push」——授权面矛盾；②票 15 两个 parked hunks（ws AGENTS.md、uq csproj）驻留，uq 与已提交 tests/Directory.Build.props 重复声明同组包（fold 时以 Directory.Build.props 为唯一事实源）。**收口后修正（2026-09-04）**：用户确认五支分支的推送授权，reports/12/13/15/16 的「不 push / 未 push」措辞已就地修正，远端 arch 分支与 draft PR #39 已关闭删除。；③票 16「18/18 脚本核验」证据归属夸大（check-doc-sync.ps1 不覆盖 ADR 0014 内容）；④issue 追踪字段卫生（13 未勾、Status 字段 5/6 未同步，大脑已统一登记）。

## 合并拓扑（but land 序列，待用户授权 push）

- Stack A：`but land arch/13-mutation-gate --yes` → `but land arch/11-differential-oracle --yes`（11 叠于 13）
- Stack B：`but land arch/12-write-path-state-machine-tests --yes`（12 为 15 的下层）
- 独立：`but land arch/14-cli-output-snapshot-testing --yes`、`but land arch/16-adr-txr-txf-ban --yes`
- 最后：`but land arch/15-testcontainers-l1 --yes`（前置条件：12/14 合入后 fold ws/uq 两个 parked hunks，uq 去重）
- target = origin/main；`but land` 对远程 target 是「合入+推送一步到位」，无「只合不推」分离命令——所以全部 land 在用户明确授权后一次执行；合前已 `but pull`（无新上游）。

## Backlog（待用户决定是否立票）

1. CLI `profile create --help` 解析缺失（把 --help 当 profile 名落库）。
2. Program.cs 441 行 CliRuntime 拆出。
3. ValidateProfiles 悬空 launch target 硬阻断一切 profile 写 → 评估降级 warning + 隔离。
4. CI 用户态隔离（集成测试独立 LOCALAPPDATA 防污染用户 profiles.json）。
5. architecture.md 补 canary/golden 段（现权威在 AGENTS.md Testing 节）。
6. 用户侧注册表残留 `EM_TEST_DST=v1`（用户自清，非泄漏）。
7. 票 13 存活变异 16 条「缺失断言」的覆盖补强（报告 13 遗留建议）。
8. main verify job Tauri `bin\env-manager-cli.exe` 资源路径预存失败（本波 gh 复核新确认，挡全绿）。
