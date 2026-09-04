# 复核报告 — 票 13：变异测试闸门（Stryker.NET，本地/PR 辅助）

日期：2026-09-04 · 复核方式：独立子代理只读取证 + 大脑会话当场重跑 stryker · 结论：✅ 可验收（口径变化见下）

## 声明 → 证据 → 结论

| 声明（子窗口报告） | 证据（仓库实物） | 结论 |
|---|---|---|
| stryker-config.json 内容 | 存在且逐字一致：mutate 红线四文件、ignore string/logical、thresholds 85/70/60、reporters html/progress | 属实 |
| .config/dotnet-tools.json 含 dotnet-stryker 4.16.0 | 存在，`isRoot: true` | 属实 |
| 运行期数字自洽（76/94=80.85%、37.07% 口径） | 76/94=0.8085；76/(94+111)=0.3707；变异流水账 6678−6584=94、76+18=94 全部闭合 | 属实（报告时点快照） |
| 本票未改任何 C# 代码 | commit 64d4db5（=nqt）只含 .config/dotnet-tools.json、AGENTS.md、docs/build-and-release.md、stryker-config.json，0 个 .cs；`git diff 64d4db5 HEAD -- <四红线文件>` 为空 | 属实 |
| 分支 Applied + 提交 nqt | but status：`mu [arch/13-mutation-gate]` Applied，提交 nqt | 属实 |

## 大脑当场重跑（2026-09-04，套件已增长后的新基线）

`dotnet tool restore && dotnet stryker` → 96 个变异受测（报告时点 94），**Killed 78 / Survived 14 / Timeout 4 / Errors 0**，最终分 **40.00%**，低于 break 60 按设计退出（本地辅助闸门、非 CI 硬门）。数字与报告（76/18）不同是**套件增长**所致（121→151 用例，票 11/12/14/15 落库后），非回归：工具在 net10 全流程跑通、配置生效、闸门行为符合票级决定。

## 过程发现（已披露，不追认）

- issue/13 五个验收勾选项磁盘上**全部未勾**（报告声称五项全达成）——追踪文件未同步（本次复核已代为勾选）。
- 报告称"不 push"，但 `remotes/origin/arch/13-mutation-gate` 存在且 tip=64d4db5（全部六票远端分支均存在）——与报告措辞矛盾，授权面见大脑总报。

## 结论

5 项声明属实；重跑证实 net10 可用性与本地闸门定位成立。✅ 可验收（数字以本复核重跑为新基线）。
