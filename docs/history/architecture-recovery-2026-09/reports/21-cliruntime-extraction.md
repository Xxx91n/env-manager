# 报告 21 — CliRuntime 拆出（纯搬迁，行为零变化）

**状态**: 已闭环（实施 + CI 验证全绿；PR #41，verify job pass）
**分支**: arch/21-cliruntime-extraction（GitButler 提交 qtp → push 8ac9b09，PR #41 → main）

## 关键命名澄清（票面勘误）

仓库内自始不存在名为 `CliRuntime` 的类型（git log -S CliRuntime --all -- src/ 零命中；全仓 rg 仅命中 .scratch 文档与 installer.wxs 的 MSI 组件 ID `CliRuntime`——那是 runtimeconfig.json 的安装组件，与本票无关）。"CliRuntime 441 行" 指票 05 报告遗留备注 4 / 票 06 报告备注 2 登记的 "共享运行时基础设施"（DebugMode/JsonOpts/ValidCommands/AppDataDirectory/IsWriteInvocation/AcquireMutationLock/CaptureEnvironmentSnapshot/CaptureScope/AtomicWriteJson/WriteAtomicUtf8 等）。本票按票面约束 "类型名保留" 将这批成员搬入新文件 `src/CliRuntime.cs`，仍为 `partial class Program`，无新类型/新 seam/新抽象。搬迁时点 Program.cs 实为 446 行（票 14 快照测试提交后），26 个非 Main 成员 + Main。

## 检查点证据

### A — 备份 + sanity
- 备份: `C:\Users\ADMINI~1\AppData\Local\Temp\envman-ticket21-preref\Program.cs.pre-move`（19497 字节，与原件逐字节一致，4 个关键片段探针全中）

### B — 字符串切片搬迁（禁 split/join，遵循 WORKFLOW §6 教训）
- 切片方案: head(L1-74) / M1=常量+共享成员(L75 前) / K=Main(L75-199) / M2=其余成员(L201-441) / tail("}\n")，五段为原文字节切片，守恒校验 head+M1+K+M2+tail === src 通过
- 10 项锚点守卫全过（head 以 "partial class Program\n{\n" 结尾、M1 以 SystemEnvPath 常量开头、K 内无 doc-comment、M2 以 ScrubExceptionMessage 的 <summary> 开头、tail 为 "}\n"、全文无 CR 等）
- 写后探针: CliRuntime.cs 26/26 个被搬成员定义全在、Main 不在其中；Program.cs 26/26 全不在、Main 与 mutationLock.Dispose() 调用仍在；两文件均无 CR、以 "}\n" 结尾、partial class Program 声明各恰 1 处
- 行数: Program.cs 446→138 行（thin Main）；CliRuntime.cs 326 行
- 唯一新增内容: CliRuntime.cs 文件头 5 条 using（搬迁成员所需，逐条对照成员体引用面）+ 6 行 XML doc 注释（非代码）；成员代码字节零改动
- EOL: Program.cs 原文 LF，两文件写后保持 LF（.scratch 之外的仓库无 .gitattributes，磁盘字节即真值）

### C — CI 验证（已闭环；用户收口指令授权推送）
- 推送: arch/21-cliruntime-extraction → origin（8ac9b09，含叠栈 arch/17 两提交）；PR #41（https://github.com/Xxx91n/env-manager/pull/41，gh api 直建——but pr new 无法识别 SSH 别名 forge，gh pr create 交互挂起）
- CI run 33908845963（PR #41 checks，2026-09-05 收口取证）: **verify pass 10m9s**（job 101140207264）+ verify-l1 pass 1m16s + verify-arch x86/arm64 pass + actionlint/dependency-review/validate-pr-title pass（package 为非门槛后续 job）
- **dotnet 测试全绿**: `dotnet test tests/EnvManager.Engine.Tests -c Release` 全量无 filter — "Passed! - Failed: 0, Passed: 127, Skipped: 24, Total: 151"（24 Skip 全为文档化 L0/L1 门控：7 个容器后端 contract 15 + DifferentialOracle 11 等，EM_L1_MATRIX 未设/EM_DIFFERENTIAL_ORACLE 未设时的预期 Skip；verify-l1 job 另证 L1 矩阵 pass）
- **CLI 快照 17 个不变**: 全量套件 0 Failed 且无任何 .received 差异文件（日志 "received: 0"）；tests/.../snapshots/ 下 17 个 CliOutputSnapshotTests.*.verified.txt 与 CI 运行实测输出逐字节匹配（CliOutputSnapshotTests 为 VerifyBase 无 Trait 过滤，全量跑必然覆盖；minimal verbosity 不打印 Passed 单行——Skip 行可见性与日志形态一致）
- **vitest 430/430**: "Test Files 40 passed (40) / Tests 430 passed (430)"
- 集成四套件: test-with-restore "ALL TESTS PASS + exact registry and internal-config snapshots match"（差分 oracle 在 harness 内 11/11: "Passed! - Failed: 0, Passed: 11"）
- 行为零变化静态证据链: 成员逐字节切片搬迁 + 守恒校验 + 26 成员定义/引用探针 + 引用点全仓扫描（src 内零调用点修改——类型名未变，调用面编译语义不变）

### D — 引用点同步（10 文件，逐处锚点替换 + 写后校验）
1. AGENTS.md L52 四层架构句（"thin Main dispatcher plus shared runtime infrastructure" → 指向 src/CliRuntime.cs）
2. AGENTS.md L66-67 结构树（Program.cs 行改写 + 新增 CliRuntime.cs 行）
3. docs/architecture.md L5 三层架构句
4. docs/adr/0005-sensitive-data-redaction.md（ScrubExceptionMessage、SecretString 两处 src/Program.cs → src/CliRuntime.cs）
5. docs/agents/hard-boundaries.md L177（RecordProviderHash "defined in src/Program.cs" → "defined in src/CliRuntime.cs, moved ... by issue 21"）
6. docs/agents/reference-index.md L46（ScrubExceptionMessage in Program.cs → CliRuntime.cs）
7. CONTEXT.md L158（ScrubExceptionMessage 术语条目）
8. docs/agents/domain.md L23（结构树句）
9. .github/CONTRIBUTING.md L32
- 残留扫描: root *.md/*.cs/*.json + docs/（history 除外）+ scripts/ + .github/ + tests/ + .config/ 无陈旧指针；tests/ 中 "Main() emits (Program.cs)" 注释仍准确（Main 留守）；docs/history/ 与 .scratch/ 历史存档按纪律不改写
- 注意: AGENTS.md 带预存 UTF-8 BOM（ef bb bf），本票写入原样保留，未增删

### E — codegraph sync
- `codegraph sync .`: "Added: 1, Modified: 2 — 49 nodes"，`codegraph status .`: "Index is up to date"

## issues/21 验收单核验

- [x] CliRuntime 独立成文件；Program.cs 回到 thin Main — src/CliRuntime.cs 326 行承载 26 个共享成员；Program.cs 138 行仅 Main + 文件头（探针输出见 B）
- [x] 行为零变化 — CI 全绿收口: verify pass（dotnet 127 过/0 败/24 门控 Skip + 17 快照不变 + vitest 430/430），证据见 C
- [x] 引用点同步 — AGENTS.md 结构树/四层架构、docs/architecture.md、ADR 0005、hard-boundaries.md、reference-index.md、CONTEXT.md、domain.md、CONTRIBUTING.md（见 D）
- [x] 纯搬迁，无行为/接口变化 — 原文字节切片五段守恒；无签名/可见性/输出文案改动；无新 seam/抽象
- [x] codegraph sync — 见 E

## 提交

- GitButler 分支 arch/21-cliruntime-extraction，conventional commit: refactor(engine)
- 提交物: src/CliRuntime.cs（新增）、src/Program.cs、AGENTS.md、CONTEXT.md、.github/CONTRIBUTING.md、docs/architecture.md、docs/adr/0005、docs/agents/hard-boundaries.md、docs/agents/reference-index.md、docs/agents/domain.md
- .scratch/ 报告按现状不入库（gitignore 状态维持原 agent 的 parked hunk，未动）

## 提交实录（补登）

- 提交 `qtp` 落于分支 `arch/21-cliruntime-extraction`（叠于并行分支 `arch/17-verify-cli-staging` 之上——AGENTS.md 首次提交因该并行栈跨文件依赖被 GitButler 拒绝，按票 03 防线①以 --anchor 建叠栈后一次成功；未 push、未建 PR）
- 提交吸收 12 项改动（src/Program.cs、src/CliRuntime.cs 新文件、AGENTS.md 2 hunk、CONTEXT.md、docs/architecture.md、docs/adr/0005 2 hunk、docs/agents/domain.md、docs/agents/hard-boundaries.md、docs/agents/reference-index.md、.github/CONTRIBUTING.md）；.zcode/plans/ 计划文件（他会话产物）与 .scratch/ 报告均未吸收
- 收口实录: 用户收口指令授权推送；arch/21 → PR #41 → CI 全绿（见 C 节）。合入 main 由大脑会话执行（本窗口按票面止步于 CI 证据取得）
- CI 验证红则（未触发）按报告 B 节恢复源定位返修

## 遗留风险 / 备注

1. CI 红则返修: 若快照或编译失败，恢复源为 OS temp 备份 Program.cs.pre-move，再按红点定位。
2. install.wxs 的 `CliRuntime` 组件 ID 与本票无关（MSI 组件名，指 env-manager-cli.runtimeconfig.json），未触碰。
3. 本窗口自伤一次（无爆炸）: CliRuntime.cs 首版 doc 注释含 "(partial class Program)" 字样使自查探针双匹配误报，已改写注释措辞消除歧义并复验全绿；WORKFLOW §6 未新增条目（未构成爆炸）。
