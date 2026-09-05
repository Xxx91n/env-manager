# 复核 17 — CI verify 内嵌 CLI 资源 staging（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 17 验收） | 证据（仓库实物 / CI） | 结论 |
|---|---|---|---|
| 1 | verify job 在 cargo 测试前置 staging step（五文件 fail-closed） | build.yml:91 步骤名「Stage CLI artifacts into Tauri resource dir (issue 17)」，五文件循环 :104/:113，fail-closed exit 1 :119-123；位置在 Build CLI(:88-89) 之后、全部 cargo 编译之前 | 证实 |
| 2 | main push verify 全绿、package/release 恢复执行 | gh run 33907568349（PR, head=arch/17-verify-cli-staging）：verify/verify-l1/package/verify-arch(x86+arm64) 全 success；此前失败 run 33880367303 的唯一失败步骤恰为 cargo 资源报错 | 证实 |
| 3 | build.mjs 职责不变、tauri.conf 清单不动 | tauri.conf.json:47-53 仍五文件；本地 prebuild.mjs 未被本票改动（报告 §0 与实物一致） | 证实 |
| 4 | 不动 cargo 断言与资源清单 | git show 提交 rwm/wsy 文件面仅 build.yml + main.rs + AGENTS.md + docs/build-and-release.md | 证实 |
| 5 | docs 同步 | docs/build-and-release.md:299 staging 段、AGENTS.md:53 Tauri 条目已更新 | 证实 |
| 附 | 附加改动 frontendDist 占位 + generate_context! | build.yml:125-131 占位 seed 存在；generate_context! 在 main.rs:1415（报告写 1409，差 6 行，微漂移） | 证实（行号微差） |
| 附 | 报告 §4「未 push、未建 PR」 | 与同报告 §1.C「已推 origin、PR #40」自相矛盾；实物=origin/arch/17-verify-cli-staging 存在、PR #40 OPEN | 报告自相矛盾，已补修正记录 |

## 总结论：✅ 通过（reviews/17）。代码与 CI 证据全部坐实；唯一问题是报告 §4 遗留过期文案（已由大脑补修正记录）。