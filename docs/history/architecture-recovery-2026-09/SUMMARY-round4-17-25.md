# Round 4 收口摘要（2026-09-05，票 17–25 + 三轮返修）

> 权威细节引用 reviews/17..25、reports/、README 波次表与违规登记；本文档只保留收口结论。

## 一句话结论

architecture-recovery Round 4 九票（17–25）全部 ✅ done：PR #45（head=完整 11 提交栈 17→21→18→19→24→25→22→23→20→ci-fix→ser-fix）全栈 CI 绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success；Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿）。合入 origin/main 待用户 land 授权。

## 九票终态（README 已登记 ✅ done）

| 票 | 内容 | 终验 |
|---|---|---|
| 17 | CI verify 内嵌 CLI staging（每次推送变绿） | PR #45 绿 |
| 18 | 变异幸存者分诊 + 登记 + CI stryker job | PR #45 绿；stryker 重跑数字留合入后 dispatch |
| 19 | 预检验证两级降级（error/warn + --strict + exit 2=profile apply） | PR #45 绿 |
| 20 | profile create --help 修复 | PR #45 绿 |
| 21 | CliRuntime 拆出 | PR #45 绿 |
| 22 | 集成测试残留卫生 | PR #45 绿（Pester 四套件） |
| 23 | architecture.md canary/golden 段 | PR #45 绿（doc-sync） |
| 24 | CI 用户态隔离（LOCALAPPDATA seam） | PR #45 绿（Assert user-state isolation） |
| 25 | SharpFuzz 夜间模糊（corpus + cron） | PR #45 绿 + Fuzz 短跑绿 |

## 三轮返修（全部复核通过）

1. 19-fix2：%VAR% defined 判定补进程环境、测试 hermetic（首跑红因=直读注册表+依赖机器 SYSTEMROOT）。
2. 22+24 联合：Invoke-Cli stderr 捕获 + round-trip 带戳名（首跑红因=固定名与预存值碰撞触发 VariableWrite.cs:198 --overwrite 拒绝）。
3. 14+18+19 串行化：静态 seam（SetProfilesFilePathForTests 等）调用类全部并入 DisableParallelization 集合（首跑红因=跨集合并行竞态，ProfileSeamValidationTests.cs:140）。

## CI 演进（每次红→定位→修复→复跑）

编译红（CS0103/SC2012）→ C# 首跑红（SYSTEMROOT 依赖）→ Pester 红（set failed）→ 时序红（seam 竞态）→ PR #45 全绿。过程中修掉两个流程缺陷：GitButler 新分支真实父链默认在 main（须 but move --above 实叠）、gh run watch 管道吞退出码（换带重试轮询）。

## 违规登记（12 条，README 全文）

1-12 条涵盖：跨票 hunk 归属、Blocked-by 检查点形同虚设、零 CI run、README 被非大脑写入、越权 PR/推送、报告失实、git 暂存区旧化、测试违反仓库纪律、静态 seam 竞态、GitButler 父链陷阱。均不追认，合并期处置。

## Backlog（收口后遗留，待用户决定是否立票）

1. stryker workflow_dispatch 重跑数字回填（须合入 main 后，dispatch 触发需存在于默认分支）。
2. git 暂存区旧化内容刷新/丢弃（3 文档文件修复前文本 + fuzz 文件 D+?? 双态；来源待认定）。
3. 证据 PR 清理：#42/#43/#44 被 #45 取代，收口时关闭；远端 arch 分支 land 后删除。
4. runner 镜像谱系残留调查（EM_TEST_FOO 预存来源未实锤，联合返修报告已标注未证）。
5. 调研推迟项（spec Phase 3/4 Further Notes）：Coyote 并发 spike、GUI E2E 升级、fnox 式 profile export、flake 隔离仓注册表、变异等价 LLM 自动检测。
6. 用户侧操作：HKCU Environment 残留 EM_TEST_DST=v1 自清（命令在 docs/build-and-release.md Test residue hygiene 节）。
