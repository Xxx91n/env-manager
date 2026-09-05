# 复核 25 — SharpFuzz 夜间模糊（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 25 验收） | 证据（仓库实物 / CI） | 结论 |
|---|---|---|---|
| 1 | harness + 异常二分纪律 | tests/EnvManager.Fuzz/Program.cs（Fuzzer.LibFuzzer.Run；预期异常吞、NRE/OOM/StackOverflow/AV 当 crash）；csproj PublishReadyToRun=false（:13） | 证实 |
| 2 | 种子 corpus 入库 | tests/EnvManager.Fuzz/Corpus/ 27 个种子文件 | 证实 |
| 3 | 夜间 workflow + PR 短跑非阻塞 | .github/workflows/fuzz.yml：cron '30 18 * * *'、PR 短跑 300s continue-on-error、workflow_dispatch max_total_time=1800 | 证实 |
| 4 | ReadyToRun 前提验证 | 报告检查点 B 证据链：build.mjs/csproj/workflows 零 R2R 命中 + fuzz 链路自身 PublishReadyToRun=false | 证实 |
| 5 | 首次跑证据 | gh run 33943975560（Fuzz, arch/25 PR）：success，301s/6.5M execs/0 crash；driver SHA256 已 pin（srk 提交） | 证实 |
| 附 | 本票 CI/CD 红 | arch/25 PR 的 CI/CD（33943975584/33944493443）与 Workflow Lint（33944493448）双红——根因 = 票 18 的 CS0103 ×2 与 build.yml:460 SC2012（连坐，非本票文件）；报告已如实标注 | 连坐（非本票缺陷） |
| 附 | 检查点 A | 报告称「票 18 已交付…分诊清理完…前提已满足」——但 18 当时未过大脑核验且其测试编译不过，「完成」定义未满足即开工 | 过程违规（见 SUMMARY） |

## 总结论：✅ 本票通过（reviews/25）。全部 5 项验收证实 + 首跑绿；栈级 CI 待 18 返修后复验；PR #42 维持 draft；AGENTS.md 两 hunk parked 待合并期 fold。