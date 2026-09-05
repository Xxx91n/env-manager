# architecture-recovery — 总控

重建日期：2026-08-31（原文件随 .scratch 树外部清理丢失，此为重建版；全套 issues/handoffs/prompts/reviews/spec/巡检报告已于同日恢复完整）。
（2026-09-01：票05 子窗口因 undo 链回滚事故二次恢复本表；票05 状态按子窗口回报更新。）
（2026-09-02：追加 Wave 6/7 —— SecretProvider 拆分 + 契约测试套件，由 atomcode 调研驱动。）
（2026-09-03：票 09/10 大脑会话复核通过，登记 done；追加 Wave 8 —— 测试心智模型升级三件套 + 补强，atomcode 调研驱动，依据 research/next-wave-patterns.md。）
（2026-09-04：Wave 8 六票（11–16）大脑会话复核通过，登记 done；reviews/11..16 全量落盘；分支合入 origin/main 待用户授权。）
（2026-09-05：追加 Wave 9/10 —— 收尾补强九票（17–25），由 atomcode 收尾调研驱动，依据 research/round4-closeout-patterns.md；横切登记 backlog 全部立票，SharpFuzz 自推迟项转立票。）
（2026-09-05 大脑复核：九票子窗口已交付；reviews/17..25 落盘。17/21 通过，25 本票通过（栈级待 18），18/19/20 返修，22/23/24 待全栈 CI。）

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
| Wave 9 | **17** CI verify 内嵌 CLI 资源 staging（main 推送每次变绿） | ✅ done（reviews/17；PR #45 全栈绿 run 33963823146：verify/verify-l1/verify-arch×2/package 全 success） |
| Wave 9 | **18** 变异测试幸存者分诊 + 登记 + 模块化报告（CI 可跑） | ✅ done（reviews/18 两次返修复核；PR #45 全绿；stryker CI 回填 run 33966698434：131 受测/118 kill/13 survived/46.83%——break 阈值红属设计；新种群 13 survivors 二次分诊列 backlog①） |
| Wave 9 | **19** 预检验证两级降级（error/warn + --strict + 退出码 2） | ✅ done（reviews/19 二次返修复核；PR #45 全绿） |
| Wave 9 | **20** profile create --help 解析修复（help 不当 profile 名落库） | ✅ done（reviews/20 返修复核；PR #45 全绿） |
| Wave 9 | **21** CliRuntime 441 行拆出（纯搬迁，行为零变化） | ✅ done（reviews/21；PR #45 全绿） |
| Wave 9 | **22** 集成测试残留卫生（补偿式清理 + 用户自清文档化） | ✅ done（reviews/22 + 联合返修复核；PR #45 全绿） |
| Wave 9 | **23** architecture.md 补 canary/golden 段（文档） | ✅ done（reviews/23；PR #45 全绿含 doc-sync 门禁） |
| Wave 10 | **24** CI 用户态隔离（LOCALAPPDATA 重定向 + 快照语义纪律） | ✅ done（reviews/24；PR #45 全绿含 Assert user-state isolation 步骤） |
| Wave 10 | **25** SharpFuzz 夜间模糊（参数解析面 + corpus 入库） | ✅ done（reviews/25；PR #45 全绿 + Fuzz 短跑绿 33963823152） |

## Frontier（当前可开工）

- **无在库 ready-for-agent 票**：17–25 全部 ✅ done（PR #45 全栈绿 run 33963823146 为终验证据），联合返修与串行化返修亦闭环。
- **进入合并期（等用户 land 指令）**：合入顺序自底向上 = 17 → 21 → 18 → 19 → 24 → 25 → 22 → 23 → 20 → 联合返修(ci-fix) → 串行化返修(ser-fix)；合入 main 后 dispatch stryker 回填 18 趋势数字；证据 lane PR #45（#42/#43/#44 被取代，收口时清理）。
- **合并前置风险**：违规清单第 7 条——git 暂存区旧化内容须在合并前刷新/丢弃（3 个文档文件修复前文本 + fuzz 文件 D+?? 双态，来源待认定）。

## 本轮违规登记（大脑呈报，未追认）

1. 票 20 的 src 修复装在票 19 提交 445a0d9（git log -S 证据）——§4.2「只提交本票改动」违规，arch/20 不完整。
2. 票 25 检查点 A 把「已交付」当「完成」——18 当时未过大脑核验且其测试编译不过。
3. 票 19/20/22/23 零 CI run、分支未推送——CI-only 验证环节未闭环。
4. README 波次表 18/25 两行被非大脑写入——§4.1 违规，待认定。
5. PR #40/#41/#42 与 5 个远端 arch 分支——§4.2 默认不 push/不建 PR；票 17 报告称「用户授权推送」，本会话无授权记录，不追认。
6. 票 17 报告 §4 与 §1.C 自相矛盾；票 19 报告 Fact 计数 8 vs 实物 9（均已补修正记录）。
7. 返修期：git 暂存区含票 19 三个文档文件的修复前文本（git diff --cached 旧化），来源待认定（若为窗口裸 git 暂存则违规）——合并前必须刷新。
8. 票 19-fix 报告「main.rs 仅注释行」与提交实物（7 增 1 删）不符，已补修正记录。
9. 票 19 测试+实现违反仓库测试纪律（fix2 已修：defined 判定补进程环境 + 测试 hermetic，红点移出）。
10. CI 集成首跑红：Pester round-trip「set failed」（run 33956994940/33957010102）——固定名 EM_TEST_FOO 与预存值碰撞 + Invoke-Cli 丢弃 stderr 无诊断；联合返修已复核通过（reviews/22 联合返修复核段）。
11. 全栈首跑暴露静态 seam 竞态：SetProfilesFilePathForTests 被 18/19/14 三类并发翻转、无共享串行集合——ProfileSeamValidationTests.cs:140 Sequence contains no matching element（run 33961788030）；串行化返修已发（prompts/engine-test-seam-serialization-fix.md）。
12. GitButler 新分支真实父链默认在 main（虚拟栈顶≠真实 DAG）：PR #43/#44 两次以「内容=main+单票」触发 CI 复现 Tauri 资源路径红——新分支必须 but move --above 实叠后再建 PR（已修，a7324c5）。
## 横切登记（未立项 backlog，独立决策，不并入本波）

- 原始 8 项 backlog 已全部立票并实施（17–25 + 联合返修；详见波次表）。
- 用户侧操作（非立票项）：HKCU Environment 残留 EM_TEST_DST=v1 的实际删除由用户自清（命令见 docs/build-and-release.md Test residue hygiene 节）。
- 调研推迟项（不立票，见 spec Phase 3/4 Further Notes）：Coyote 并发 spike、GUI E2E 升级、fnox 式 profile export、flake 隔离仓注册表、变异等价 LLM 自动检测。（SharpFuzz 已立票 25 并实施，不在此列。）

波次规则：同波内可并行开窗；一票 DoD 达成（大脑核验）后才解锁下游票。
