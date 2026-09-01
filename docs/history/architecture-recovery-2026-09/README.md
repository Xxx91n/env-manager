# architecture-recovery — 总控

重建日期：2026-08-31（原文件随 .scratch 树外部清理丢失，此为重建版；全套 issues/handoffs/prompts/reviews/spec/巡检报告已于同日恢复完整）。
（2026-09-01：票05 子窗口因 undo 链回滚事故二次恢复本表；票05 状态按子窗口回报更新。）

## 波次与状态（由 issue Blocked by 推导 + 大脑验收结论）

| 波次 | 票 | 状态 |
|---|---|---|
| Wave 1 | **01** 引擎 seam（expand） | ✅ done（reviews/01，报告两处数字失实已修正） |
| Wave 1 | **02** xUnit 测试泳道 | ✅ done（reviews/02，18/18 绿） |
| Wave 2 | **03** 写路径迁移到 seam | ✅ done（reviews/03；dotnet test 71/71 绿） |
| Wave 2 | **07** launch 注入生效验证 | ✅ done（reviews/07 复验通过：工作在库＋补档报告逐项实物复核成立） |
| Wave 2 | **08** IPC schema 契约 | ✅ done（reviews/08 复验通过：CI 步骤已补、flake 已修、四侧全绿） |
| Wave 3 | **04** profile/secret 迁移 + ADR 0010 修订 | ✅ done（reviews/04 二轮复验通过：B1 消除，7/7+4/4+86/86 当场复跑全绿，B1 修复独立提交 828af15） |
| Wave 4 | **05** Program.cs 命令域拆模块 | ✅ done（reviews/05：行为零变化经参照树 10/10 逐字节 diff 复验；测试门全绿；双分支交付，合并先 -b 后主；提交期 undo 事故呈报，.scratch 二次损毁已由大脑恢复） |
| Wave 5 | **06** EnvFeatures 五域分家 | ✅ done（reviews/06：11/11 成员落位抽查、退役 rg 零活引用、全部门当场复跑绿、双分栈合规零违规——8 票中首份零事故报告） |

## Frontier（当前可开工）

- **全部 8 票收口，无下一波可派票。** 进入合并收口阶段（WORKFLOW §2 收口 + §4.4 终验）：11 条 arch/* 分支（8 票 + 03-seam-ext + 05-b + 06-b）按序合入（每票双栈先 -b 后主）、fold parked hunks（AGENTS.md 4 块 + .gitignore 1 行）、全量守门复跑（86/86 + 四套件 + 398/398 + cargo 双 crate）、$code-review 双轴评审
- 横切登记（合入后另行评估）：CLI `profile create --help` 解析缺失；注册表残留 `EM_TEST_DST=v1`（用户侧，非泄漏）

波次规则：同波内可并行开窗；一票 DoD 达成（大脑核验）后才解锁下游票。
