# 大脑复验 — 票 05（Program.cs 命令域拆模块，gh pkg/cmd 形态）

日期：2026-09-01　复核人：大脑会话（3 个独立子代理并行取证：测试门 / 结构与分支拓扑 / 行为零变化）
复核对象：reports/05-command-module-extraction.md（双分支交付：arch/05-command-module-extraction-b 叠 arch/04 + arch/05-command-module-extraction 叠 arch/03-seam-ext）

## 结论：**通过，票 05 收口。Wave 5（票 06）解锁。**

---

## 1. 声明 → 证据 → 结论 对照表

### 验收项 1：Program.cs 缩减为薄入口（<400 行）且 Main 只做分发 — ✅

| 声明 | 子代理实物证据 | 结论 |
|---|---|---|
| src/Program.cs = 347 行 | wc -l 实测 346（无尾换行，实际 347 行） | 一致 |
| src/ 30 个 .cs 共 9260 行 | 实测 30 文件；合计 9230 = 9260 - 30（每文件尾换行计数差），逐文件抽查同规律 | 一致 |
| Main 只做分发 | 子代理全文通读：仅 crash-dialog 禁用/LenientArgs/--debug/ValidCommands/互斥锁+快照/switch 分发/RecordSnapshotDiff；文件内 `Registry.` 零匹配 | 一致 |

### 验收项 2：每个命令域一个文件，域内聚合其静态状态 — ✅

- `IsProtectedVariable` 在 src/ProtectionCommand.cs:162；`SetProfilesFilePathForTests` 在 src/ProfileStorage.cs:126；`RunSet/RunDelete/RunToggle` 在 src/VariableWrite.cs:176/203/237 —— 全部命中。
- vnk 提交（5355da6）：+2899 行纯新增 10 个域模块；rxn（3160c32）：删根 Program.cs(-3292) + 新增 src/Program.cs(+346) + 14 文件纯改名移入——与报告改动清单一致。

### 验收项 3：迁移前后全部测试绿灯 — ✅（最强一项：全部当场复跑）

| 门 | 报告声称 | 子代理当场实测 | 结论 |
|---|---|---|---|
| dotnet test（rm -rf bin obj 干净重建后） | 86/86 | 失败: 0，通过: 86 | 一致 |
| 前端 vitest 全量 | 40 文件 398/398（含 review-regressions 转绿） | 40 passed / 398 passed；单跑 review-regressions 15/15 绿 | 一致 |
| Tauri cargo test --locked | 11/11 | 11 passed | 一致 |
| service cargo test --locked | （基线 15/15） | 15 passed | 一致（无回归） |
| run-ci-tests 四套件 | 6/6+9/9+4/4+7/7 + CI tier PASSED | 逐套件亲见：[LaunchInjection] 6、[Canary] 9、INHERITANCE 4/4、test-with-restore 7 OK + 快照吻合 + "=== CI test tier PASSED ===" | 一致 |
- 注：子代理发现 release/cli-only exe 陈旧（先于 src/ 迁移），先用 dotnet build -c Release 产物刷新 release/cli-only 再跑集成——测的确实是 src/ 新代码，方法正确。
- 附注：子代理发现注册表 user 域有 `EM_TEST_DST=v1` 残留（早于本次运行的既有遗留），快照吻合说明非本次泄漏；如实记录，待用户自行清理。

### 验收项 4：对外 CLI 帮助文本与退出码无变化 — ✅（独立重建参照现场复验）

- 迁移前参照树仍存在于 OS temp（envman-ticket05-preref，单文件 3292 行 Program.cs 形态确认）。子代理两侧独立 `dotnet build -c Release`，spawnSync 比对 stdout+stderr+退出码：help / list / agents / agents --json / profile list / path list --scope user / expand / get PATH / unknowncmd / service status **10/10 IDENTICAL**（含 2 条 exit 1 路径）。
- 附加抽查：`profile show <不存在>` exit 1 错误文案正常；`--debug` stderr 输出 [debug] 日志正常。
- 报告声称 19 命令全比对，子代理独立复验其中 10 条全覆盖 zero-diff——**最强验收形态成立**。

### 验收项 5：codegraph sync — ✅

`codegraph status .` 正常（137 文件 2095 节点）；`codegraph query RunSet` 命中 src/VariableWrite.cs:176，索引指向新布局。

## 2. 顺带修复裁决（报告"供大脑裁决"项）

- **票 03 遗留 review-regressions 2 红修复 — 裁决：批准。** 子代理核对：两个用例改为断言 src/VariableWrite.cs / src/RegistryScope.cs 中的真实现存符号（SetPathEntriesCore L149、WriteVariableCore("PATH"...) L174、RegistryValueKind backup L298、Toggle recovery L285），非删断言放水，守卫语义等价。README 横切阻塞随之解除。
- **6 个前端门禁文件路径重指向**：旧路径零残留（rg 反向验证），一致。

## 3. 分支拓扑与提交合规 — ✅

- but status 实测双分栈与报告完全一致：arch/05-command-module-extraction（vnk 5355da6 + rky 48fe2f3，叠 arch/03-seam-ext）；arch/05-command-module-extraction-b（rxn 3160c32 + qvt e648fd7 + nls a496f21 + trk c9a08d9，叠 arch/04 栈顶）。qvt 恰为 6 个 frontend 测试文件。
- 工作区脏文件仅 AGENTS.md（4 hunk parked，内容为 src/ 布局文档更新，与报告自披露一致）+ .gitignore（+.scratch/ 行）。无他人票面混入。

## 4. 过程违规 — 1 项重大过程事故（已自行记录，呈报不替我追认，但后果已由本复核确认修复完整）

1. **提交期 undo 链回滚事故（报告已自披露）**：窗口对跨栈依赖先尝试 but move 线性化（明知票 03 防线①要求直接 sibling 分支），触发 merge-bases 引擎缺陷后 undo 越界回滚，**把未提交工作连同 gitignored 的 .scratch 整树从磁盘清除**。→ 属"检查点防线已知却未执行"，记为过程违规教训（WORKFLOW §6 已由其本人追加两条，防线内容合格）。
2. 事故恢复质量经本复核覆盖性验证：src/ 30 文件与参照树逐字节等价（行为 diff 10/10 + 全部测试门绿即证明）；.scratch 树只恢复了该窗口读过的文件——**reviews/01/02/03/04/07/08、reports/01/03/04/07/08、issues/handoffs/prompts 的 01-04/06-08 全部丢失**，已由本大脑会话恢复（见 §5）。
3. 恢复说明中报告写"冒烟输出落盘 reports/05-smoke/"——实际路径是 .scratch/architecture-recovery/reports/05-smoke/（仓根无 reports/ 目录），文件本身存在且内容有效。轻微路径表述不精确，不影响验收。
4. 无越权提交他人改动；无未等确认执行检查点（本次无人工检查点条款）。

## 5. .scratch 二次损毁恢复记录（大脑会话 2026-09-01 执行）

票 05 事故致 .scratch 树二次清空。恢复分级：
- **逐字恢复（本会话上下文持有全文）**：reviews/04、reports/01、reports/03、reports/04、reports/07、reports/08、README 状态表。
- **权威重建（不可逐字恢复，由大脑按 spec/README 的已验收结论重写摘要版）**：reviews/01、reviews/02、reviews/03、reviews/07、reviews/08（验收结论与既证实录，以"重建版"头部标注，不冒充原文）。
- **从打印机/交接种重建**：issues/06、handoffs/06、prompts/06（票 06 开工必需，按 spec.md 决策 + 既有 8 票模板重生成，标注重建）。
- **不可恢复**：架构巡检原始 HTML（architecture-review.html，OS temp 无备份）；issues/handoffs/prompts 01-04/07/08 原文（票已收口，仅留 README 结论行，不再重建）。教训防御不变：重要结论必须同时存在于 git 跟踪文件或提交信息里。

## 6. 总体评价

票 05 是整个工程里规模最大的一刀（-3292 行单文件 → 30 个域模块 + 347 行薄入口），验收形态也是迄今为止最强的：不依赖报告自述，大脑侧用迁移前参照树独立构建做了逐字节行为 diff，10/10 IDENTICAL。事故本身遗憾，但恢复链（OS temp 检查点）与最终交付物经受住了独立复验。遗留：双分支合并顺序（先 -b 后主）、AGENTS.md 4 hunk 合并期 fold、.gitignore/.scratch 行、EnvFeatures.cs 未拆（票 06 范围）。
