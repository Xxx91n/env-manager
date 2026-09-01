# 大脑复验 — 票 06（EnvFeatures.cs 五域分家并退役）

日期：2026-09-02　复核人：大脑会话（2 个独立子代理并行取证：测试门 / 结构与分支拓扑）
复核对象：reports/06-envfeatures-domain-split.md（双分支：arch/06 叠 arch/05、arch/06-b 叠 arch/05-b）

## 结论：**通过，票 06 收口。8/8 票全部完成，进入合并收口阶段。**

## 1. 声明 → 证据 → 结论 对照表

### 验收项 1：五域拆分、文件删除 — ✅

| 声明 | 子代理实物证据 | 结论 |
|---|---|---|
| EnvFeatures.cs 已删除 | `ls` 确认不存在；tzu(4685226) 提交含 EnvFeatures.cs -865 行 | 一致 |
| 成员落位 11 处抽查 | ProfileSetInherits→ProfileCommand.cs:1431、LocalFree→NativeMethods.cs:96、AcquireMutationLock/CaptureEnvironmentSnapshot→Program.cs:372/391、DpapiHelper→DpapiHelper.cs:7、RunBulkCommand→BulkCommand.cs:22、RunExpand→ExpandCommand.cs:14、LoadAuditHistory→AuditCommand.cs:126、NormalizePathEntry→PathCommand.cs、ValidateVariableInput→VariableWrite.cs:248、ProtectionDefaults→ProtectionCommand.cs:250 | 11/11 命中 |
| 行数声称 | 8 文件 wc 实测全部 = 声称 -1（尾换行容差，票 03 起既有惯例） | 一致 |

### 验收项 2：EnvFeatures 名字退役 — ✅

全仓 rg 仅 11 处历史出处注释 + AGENTS.md:86 退役说明行；零类型/文件/符号活引用。

### 验收项 3：测试门 — ✅（全部当场复跑）

| 门 | 声称 | 实测 |
|---|---|---|
| dotnet test | 86/86 | **86/86** |
| Release 构建 | 0 错误 8 预存警告 | 0 错误 8 警告（同基线清单） |
| run-ci-tests 四套件 | 6/6+9/9+4/4+7/7 + CI tier PASSED + 快照精确匹配 | 逐项一致（含 "Backups deleted (clean run)"） |
| vitest | 40 文件 398/398 | **398/398** |
| cargo test 双侧 | 11/11、15/15 | 11/11、15/15 |
| 只读冒烟 | — | list/expand/agents --json 全 exit 0 |

注：测试二进制新鲜度（票 04 B1 防线）——子代理先把 Release 4 产物刷进 release/cli-only（mtime 2026-09-02 01:19）再跑集成，测的是当前代码。

### 验收项 4：前端门禁同步 — ✅

v0.7-secrets.test.ts 指向 src/DpapiHelper.cs（xun 提交）；vitest 全绿。

### 验收项 5：文档同步 — ✅

architecture.md:225 DpapiHelper 新指针；hard-boundaries.md ProfileSetInherits→:85、LoadAuditHistory→:165；AGENTS.md 票 06 内容在 parked 工作区实存（EnvFeatures retired :86、shared runtime infra :52/:66）。

### 验收项 6：codegraph sync — ✅

`Index is up to date`（139 文件 / 2104 节点 / 6293 边）。

## 2. 版本控制合规 — ✅

- 双分栈拓扑与 but status 实测完全一致：qrw(1870b65) 叠 arch/05；wxr→tzu→xun→myq 叠 arch/05-b。逐提交 --stat 抽查与报告改动清单一致，零他人票面混入。
- **防线遵从确认**：本票未使用 but move/undo 历史改写（票 03/05 教训生效），跨栈依赖直接走 sibling 分支；提交前落盘 diff 盘点。
- 未 push；conventional-commit 全部合规。
- parked hunks：AGENTS.md 4 hunk + .gitignore 1 行，与披露一致。

## 3. 过程违规 — 无

无检查点越权、无他人 hunk 混入、无裸 git 写。报告数字全部经当场复跑回填。这是 8 票中第一份全程零事故报告。

## 4. 遗留清单（进合并期）

1. 合并顺序：stack1 侧先行——每票先合 -b 再合主（05-b→05，06-b→06），票 05/06 一致建议。
2. 合并期 fold：AGENTS.md 4 parked hunk（含票 01/03/04 原有 parked 内容经票 05/06 合法 fold）+ .gitignore +.scratch/ 行。
3. Program.cs 441 行（>400 的表述已在 AGENTS.md 按实态改写）；如需 <400 可后续另开 CliRuntime 拆票，不在本轮范围。
4. 注册表残留观察项 `EM_TEST_DST=v1`（票 05 复验时已登记，非泄漏，用户自行清理）。
5. 横切登记未动：CLI `profile create --help` 解析缺失（先于全部票存在）。

## 5. 收口宣告

architecture-recovery 8 票全部收口（01/02/03/04/05/06/07/08 ✅）。下一步为合并阶段：11 条分支（arch/01…06 系列含双栈与 03-seam-ext）按序合入 + parked hunk fold + 全量守门复跑 + $code-review 双轴评审（WORKFLOW §2 收口段）。
