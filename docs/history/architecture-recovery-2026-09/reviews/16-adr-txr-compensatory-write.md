# 复核报告 — 票 16：ADR 禁止 TxR/TxF，制度化补偿式写入

日期：2026-09-04 · 复核方式：独立子代理只读取证（含 MS Learn 原文现场核验）· 结论：✅ 可验收（一处证据归属修正）

## 声明 → 证据 → 结论

| 声明（子窗口报告） | 证据（仓库实物） | 结论 |
|---|---|---|
| docs/adr/0014 存在、弃用依据+替代范式+制度化三件套 | 文件存在（5297 字节）；两条 MS Learn 引语与 transactional-ntfs-portal / deprecation-of-txf 现文**逐字核对属实**（子代理现场 fetch）；Decision 五支柱（验证+回滚/write-verify-delete/备份保留/三层锁/审计 ledger）；Adoption gate | 属实 |
| hard-boundaries.md 同步引用 | rg "0014" → 127-128 行两条新红线（No TxR/TxF、Mutation/model test first targets），在提交 1fa0c7d 内（+2 行） | 属实 |
| AGENTS.md 同步引用 | AGENTS.md:122 "Registry mutations are compensatory-write only: TxR/TxF are non-goals (ADR 0014...)"，在提交 1fa0c7d 内 | 属实 |
| rename/apply 列为首批靶点（引用不重复实现） | ADR 0014 靶点节指向 WritePathSeamTests / ProfileSeamValidationTests 与 spec Phase 3，两测试文件均存在；本票提交零代码/测试 | 属实 |
| "18/18 + Doc sync check PASSED" | scripts/check-doc-sync.ps1 存在，子代理实跑 PASSED(exit 0)；**但该脚本只做文件存在性/版本号/链接检查，不覆盖 ADR 0014 内容**——18 项是报告人工核验自述，仓库无对应脚本 | 内容属实、**证据归属弱**（已披露） |
| 分支 Applied + 提交 zus 只改文档 | but status：dr [arch/16-adr-txr-txf-ban] Applied；git show 1fa0c7d --name-status = 5 文件全文档（AGENTS/CONTEXT/adr-0014 新增/hard-boundaries/reference-index） | 属实 |

## 附注（已披露，不追认）

- 报告称"不 push、不建 PR"，但 `remotes/origin/arch/16-adr-txr-txf-ban` 存在且 tip=1fa0c7d。授权面见大脑总报。
- EOL/BOM 自述基本可证：AGENTS.md 保留 BOM（EF BB BF）；reference-index.md 唯一 CR 在既有 v0.9.12 段内，非本票触碰。

## 结论

6 项声明的交付物内容全部属实；唯一弱点是"18/18 脚本核验"的证据归属（脚本通过为真但不覆盖本票），合入说明中注明 18 项为人工核验即可，不构成返工。✅ 可验收。
