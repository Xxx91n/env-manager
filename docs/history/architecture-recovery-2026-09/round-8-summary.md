# Architecture Recovery — Round 8 Summary（收口 2026-09-14）

> 过程工作区：`.scratch/round8-backlog-grill/`（gitignored，含 decision-ledger.md / spec / issues / handoffs / prompts / reports / WORKFLOW.md 现役副本 / ROUND-8-CLOSEOUT.md 归档标记）。
> Round 8 定位：收尾硬化 + 文档还债（D-002）；六票 47-52 全部 done，A-001..A-013 结算 11 implemented + 2 deferred。

## 票单终态（复核证据 run-id 全部 `gh run view` 可验）

| 票 | 主题 | 覆盖 A-xxx | 终态证据 |
|----|------|-----------|----------|
| 47 | 过程治理（WORKFLOW 现役副本 + §4.2.y 结果不变量条款四要件 + §6 教训 6 条 + Wave 6.1 裁定回填） | —（过程裁定 D-003） | 文档票无 CI；commit lmx/de191e4c；auto-delete 评估=建议启用（移交决定） |
| 48 | 微卫生 + 三项诊断 | A-002, A-010 | run 34767318054：verify/verify-l1/package ✓（run 总红=预存 stryker 基线，已披露）；诊断①required-checks 竞态②actionlint PR 红=base 漂移③#56 esbuild 锁链缺失 |
| 49 | 补证（票 28/29 红先 run-id + 票 33 kill -9） | —（backlog#5） | run 34773105351：kill9-resilience ✓ 2m42s 9/9 ASSERT（启动测活）+ evidence artifact；AC3 红侧 34145172855/34149414639、绿侧 34147964443；SCM 恢复 documented-only（代码事实） |
| 50 | stryker 治理（gate 诚实化 + 幸存者分诊） | A-005, A-012 | run 34773442401 全绿含 stryker ✓：分数 46.83%→**98.08%** 实测（156/153/3），3 残存=恰为登记 reserves；诚实化 4 落点 |
| 51 | 文档手术 | A-001, A-004, A-006, A-009, A-011 | 重复节 2→1、行号引用=0、.scratch 顶层=0（工作区）、Version axes 两轴（L265）、Per-Layer Threat Model（L71）、ARCHIVE.md、AC4 终态括号条；附条件：AGENTS.md parked hunk 5 处合并期 fold |
| 52 | README 安全边界表 | A-007, A-013 | commit rvr/fe4184a4；绝对化句双文件 0 命中；15 行 sink 表 + 威胁模型句（L77/zh L96）；PS SecretStore=EncodedCommand 过境（事实修正 A-007）；8 语言留下一翻译批 |

## 收口时发现并修复

- `PowerShell SecretStore 走临时文件` 为不准确表述——src 实证为 `-EncodedCommand` 参数过境（A-007 账本修正行）；sops tempDir 不变。
- 票 48 三诊断推翻两项初判：auto-merge 分类门本身正确（红因=required-checks 竞态）；actionlint PR 红为 base 漂移非事件面缺陷。
- 票 49 澄清票号歧义：结构 fitness=票 28、认知复杂度护栏=票 29，双线回填。

## 治理决定（decision-ledger D-001..D-009）

- D-003/D-009 为过程裁定双支柱：违规归属（P5a 归票 28 / P5b 结果不变量条款）与大脑角色边界（只复核/派发/裁定/登记，不执行）。
- 票 47 产出现役 WORKFLOW.md（round8 工作区），后续轮次流程基座；§4.2.y 例外条款含退场机制。

## 收尾移交（全部须用户授权/裁定，详见工作区 README 收尾清单）

六分支 land（栈序 47→48→49→50→51→52）+ AGENTS.md parked hunk fold + 发版一次性授权 + 追认两项（票 50 build.yml 扩展 / 票 51 工具面）+ 处置两项（dependabot close 终态 / auto-delete 启用）。
