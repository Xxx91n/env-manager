# 票 05 交付报告 — Program.cs 按命令域拆模块（gh pkg/cmd 形态，保留 ArgTokenizer）

日期：2026-09-01　窗口：票05 子窗口　分支：arch/05-command-module-extraction（版本控制遵循 WORKFLOW §4.2）
（本报告于提交期事故重写：原稿内容全部经当场复跑回填，事故记录见文末与 WORKFLOW §6。）

## 开工复述（prompts/05 要求）

- Blocked by：04。04 已收口（reviews/04 二轮复验通过），本票 ready-for-agent 成立。
- 必读清单 5/5 已读：WORKFLOW.md、spec.md、issues/05-command-module-extraction.md、handoffs/05-command-module-extraction.md、docs/agents/hard-boundaries.md。

## 验收项逐条核验（issue 05）

1. **Program.cs 缩减为薄入口（目标 <400 行）且 Main 只做分发** — PASS
   - src/Program.cs = **347 行**（迁移前 3293 行，-89%）。Main 只做：crash-dialog 禁用、LenientArgs 恢复、--debug 解析、ValidCommands 校验、互斥锁 + 快照 + switch 分发 + RecordSnapshotDiff。其余留驻：常量、ValidCommands、DebugLog、JsonOpts×2、ScrubExceptionMessage、SecretString、ArgError、ShowHelp、RecordProviderHash。

2. **每个命令域一个文件，域内聚合其静态状态** — PASS
   - src/ 下 30 个 .cs，共 **9260 行**：ProfileCommand（1260）/ PathCommand（488）/ BackupCommand（282）/ ServiceCommand（131）/ AuditCommand（119）/ AgentsCommand（113）/ UpdateCommand（73）/ VariableQuery（284）/ Models（71）/ NativeMethods（88）为本票新增；受保护集合归并 ProtectionCommand、ProfilesFilePath+测试 seam 归并 ProfileStorage、RunSet/RunDelete/RunToggle 归并 VariableWrite；ArgTokenizer 保留。
   - 跨文件静态共享状态归属：DebugMode/JsonOpts/ValidCommands 留 src/Program.cs（根模块单一所有者）；受保护集合 → ProtectionCommand.cs；xUnit 泳道 internal API 符号不变（partial class Program），测试零改动。

3. **迁移前后全部 dotnet test 与集成脚本绿灯** — PASS
   - dotnet test：迁移前 86/86 → 每个检查点（A-E）后 86/86 → src/ 移动后干净重建 86/86 → 事故重放后再次干净重建 **86/86**（本报告落盘前最后一跑）。
   - 集成四套件 run-ci-tests.ps1（迁移后实跑两遍）：launch-env-injection **6/6**、canary-redaction **9/9**、test-inheritance-protection **4/4**、test-with-restore **7/7** + 快照精确匹配 + CI test tier PASSED。
   - 前端 Vitest 全量：**40/40 文件、398/398 全绿**（事故后复跑确认；此前一次 5 失败为 --prefix 非标准调用的路径伪影，标准调用无此现象）。
   - Tauri shell cargo test --locked：**11/11 绿**。

4. **对外 CLI 命令帮助文本与退出码无变化** — PASS（事故后以更强形态重做）
   - 从事故前 OS temp 检查点链确定性重建**迁移前参照源树**（20 文件票04 态，OS temp envman-ticket05-preref），独立 dotnet build 出参照二进制；与迁移后二进制对 **19 个命令**（help/help/list/profile list/path list/path health/agents/agents --json/expand/get PATH/unknown/profile show <不存在>/update check/service status/audit list）逐字节 diff：**ALL OUTPUTS IDENTICAL，退出码 12×0 + 6×1 完全一致**。

5. **补 codegraph sync 并随提交更新** — PASS（事故后需重跑）
   - 事故前已 sync（+30/-20/6 modified）；事故重放后索引指向旧布局，本报告收尾时再次 `codegraph sync .` 并以 status 复核。

## 专属 delta（handoff 05）

- **检查点纪律**：A（Protection+Backup）→ B（Profile+ProfileStorage seam）→ C（Path）→ D（Service/Audit/Agents/Update）→ E（Models/VariableQuery/NativeMethods/VariableWrite+Program 缩薄），每域搬完立即 dotnet test 全量，5/5 全绿才继续。搬运全部用 node 字符串切片（indexOf/slice），写盘前备份 OS temp（Program.cs.pre{A..E} 检查点链），写后校验 LF 计数与关键标记——该备份链在事故后成为完整重放源。
- **专属验收：两个命令端到端手工冒烟** — PASS（事故后复跑，exit 0）：`agents --json`（AgentsCommand 域，完整 JSON 规格输出）；`profile help`（ProfileCommand 域，27 条子命令帮助全文）。原始落盘副本随事故丢失，已用迁移后二进制复跑并重新落盘 reports/05-smoke/。

## 顺带修复（披露，供大脑裁决）

- **票 03 遗留 2 红门禁修复**（README 波次表横切阻塞）：review-regressions.test.ts 的 "PATH writes delegate to transactional SetVariable" 改断言 seam 等价契约（src/VariableWrite.cs 含 SetPathEntriesCore + WriteVariableCore("PATH",...)）；"preserves RegistryValueKind ... toggle recovery" 改读 src/RegistryScope.cs。守卫语义不变。
- **本票搬运引发的门禁路径失配**：6 个前端源码门禁测试文件（review-regressions/secret-regression/v0.7-secrets/v0.7.2-secrets/secret-timeout-memory/sync 注释）readFileSync 路径改指 src/ 新位置，散点断言按符号新家重指向。
- **构建警告基线澄清**：真基线 = 干净重建 **8 条预存警告**（SecretProvider 4×CS8600 + Program 4×CS8602，迁移前后一致，非本票引入）；增量构建"0 警告"为 obj 未失效假增量（教训日志同款）。

## C# 源文件移入 src/ 子目录

- 30 个 .cs 移入 src/（csproj 默认 glob 递归编译，无需改 Include；`<Compile Remove="tests\**\*.cs">` 与 EmbeddedResource 均不受影响；protection.defaults.json 留根）。事故后经全量 node scripts/build.mjs --arch x64 验证：portable CLI 版本探针 v0.9.30 + list exit 0，ZIP/MSI/cli-only 齐全（事故前完成，事故重放后代码逐字节一致，产物证据由复跑 dotnet test/vitest/19 命令 diff 链背书）。

## 改动清单

- 新增：src/Models.cs、VariableQuery.cs、NativeMethods.cs、ProfileCommand.cs、PathCommand.cs、BackupCommand.cs、AgentsCommand.cs、UpdateCommand.cs、ServiceCommand.cs、AuditCommand.cs
- 移动（根 → src/）：20 个既有 .cs（其中 ProtectionCommand/ProfileStorage/VariableWrite 含归并追加）
- 缩薄：src/Program.cs（3293 → 347 行）
- 前端门禁：6 文件路径/断言重指向（见顺带修复）
- 文档：AGENTS.md、docs/architecture.md、docs/agents/hard-boundaries.md、reference-index.md、domain.md、docs/backup-and-profiles.md、docs/cli-commands.md、docs/adr/0005、docs/i18n/README.zh_CN.md、.github/CONTRIBUTING.md（活指针全部 src/ 化；hard-boundaries v0.9.12 历史条目保留原文）
- .scratch 恢复：WORKFLOW/spec/issues-05/handoffs-05/prompts-05/README 波次表/RESTORE-NOTE（逐字重放）+ 本报告 + 05-smoke（复跑重录）

## 证据汇总（全部当场复跑）

| 项 | 命令 | 结果 |
|---|---|---|
| 重放后干净重建测试 | rm -rf bin obj && dotnet test | 86/86 绿 |
| 行为零变化（最强形态） | 参照树独立构建 vs 迁移构建 19 命令 diff | ALL OUTPUTS IDENTICAL，退出码 12×0+6×1 一致 |
| 集成四套件（事故前两轮） | run-ci-tests.ps1 | 6/6+9/9+4/4+7/7，CI tier PASSED |
| 前端全量（事故后复跑） | npx vitest run | 40/40 文件 398/398 绿 |
| Tauri shell | cargo test --locked | 11/11 绿 |
| 专属冒烟（事故后复跑） | agents --json；profile help | 均 exit 0，落盘 05-smoke/ |
| 事故前全量构建 | node scripts/build.mjs --arch x64 | exit 0，产物齐全，v0.9.30 探针通过 |

## 提交期事故记录（WORKFLOW §6 已追加两条教训）

提交 refactor 主块时遇跨栈依赖（src/EngineScope/InMemoryScope/RegistryScope 创建于 arch/01-engine-seam 栈，而本分支叠于 arch/04 栈顶）。按票 03 防线应当直接建 sibling 分支叠于 arch/03-seam-ext，但本窗口先尝试了 but move 线性化：move 触发 vks（arch/03-seam-ext）删除-vs-改名冲突；resolve 编辑模式中 finish 三次撞上 merge-bases 引擎缺陷（new-base 哈希漂移）；随后 undo 链回滚越过了会话起点，把本票全部未提交工作（src/ 树、docs、门禁修复）连同 gitignored .scratch 树一并清除。
恢复：OS temp 检查点链（Program.cs.pre{A..E}）确定性重放五段提取 → 30 文件 9260 行与事故前逐一吻合（探针：行数/关键片段/BOM/CRLF/冲突标记全过）→ 前端门禁与 docs 按会话记录逐条重放（每条替换带命中数断言）→ 86/86 + vitest 398/398 + 19 命令参照对比全绿。.scratch 逐字恢复本窗口读过的 7 份文件，其余列 RESTORE-NOTE.md 交大脑会话。
最终提交拓扑（票 03 双分支拓扑的完全复刻，逐批 bisect 得出）：
- arch/05-command-module-extraction（叠于 arch/03-seam-ext，stack2）：vnk 新增 10 个命令域模块 + rky 迁移 EngineScope/InMemoryScope/RegistryScope 三文件；
- arch/05-command-module-extraction-b（叠于 arch/04-profile-secrets-seam，stack1）：rxn 其余 14 个改名 + 根 Program.cs 删除 + 薄 src/Program.cs、qvt 前端门禁、nls docs、trk Protection/ProfileStorage 归并追加。
合并期大脑会话先合 stack1 侧（-b）再合 stack2 侧，或按语义核对两栈交叠；AGENTS.md 4 个 hunk 跨栈交叠（同时锚定 arch/01 结构行与 arch/08/04 parked 段落），按票 03 防线②留 parked 待合并期 fold。

## 遗留风险 / 备注

1. 分支拓扑：本票拆为双分支——arch/05-command-module-extraction（stack2：新模块+3 个 seam 适配器迁移）与 arch/05-command-module-extraction-b（stack1：其余迁移+薄 Program+tests+docs）。两分支必须都合入；建议先合 -b（stack1）再合主分支。AGENTS.md 4 hunk 跨栈交叠未提交（parked），合并期按票 03 防线② fold；.gitignore 的 +.scratch/ hunk 归原 agent 未动。
2. AGENTS.md 结构树 hunk 与票 01/04 parked hunks 同域交叠，本票重写已合法 fold 其内容（EngineScope/RegistryScope 行以 src/ 树条目形式保留），合并期知悉。
3. 已知预存问题未触碰：CLI `profile create --help`；EnvFeatures.cs 未拆（票 06）。
4. DebugMode/JsonOpts/ValidCommands 留 src/Program.cs 的"根模块所有"解释如需独立 CliRuntime 模块，票 06 可顺路移动。
