# 报告 20 — profile create --help 解析修复（help 不当 profile 名落库）

日期：2026-09-05 · 分支：arch/20-profile-create-help（独立栈，提交 wyn/893ae8ba；代码+文档 hunks 实际随 445a0d9 落于 arch/19-preflight-two-tier，见 §4）

## 0. 开工复述与必读确认

- Blocked by：无 —— 直接开工。
- 必读清单已读完：handoffs/20、issues/20、spec.md Phase 4 段、WORKFLOW.md（§4.2 版本控制）、docs/agents/hard-boundaries.md（全文 115,520 字节，分两段读完）。
- 红线对照：本票改动只触 `profile create` 解析前置分支，不动 profiles.json 写路径本身、不动 mutex/audit/secret/provider 任何红线面；帮助请求路径在任何写动作之前 return。

## 1. 检查点核验

### ✅ A 定位解析分支与对照命令的 help 契约

- bug 锚点：`src/ProfileCommand.cs` `ProfileCreate`：`args[2]` 未经任何 help 识别直接作为 profile 名进入验证与落库，`profile create --help` 会把名为 "--help" 的 profile 写入 profiles.json。
- 对照契约盘点（实测 grep 全 src/）：全 CLI 无任何命令处理 `--help` flag；域级 help 契约 = 词形子命令（`profile help` → `ShowProfileHelp()`，顶层 `help` → `ShowHelp()`），均为 stdout 输出 + 退出码 0。`profile create` 缺名时走 `ArgError`（stderr + 退出码 1）。
- LenientArgs 复核：`WasArgsCorruptedByTrailingBackslashQuote` 只在 trailing-backslash+quote 签名时触发，`--help` 场景不触发 tokenizer 恢复，无交互。
- 并行窗口注意：勘察期间 Program.cs 由 300+ 行变为 138 行（票 21 CliRuntime 拆出已在工作区/分支 `arch/21-cliruntime-extraction` 落地，提交 qtp）。本票修复面不含 Program.cs，无冲突。

### ✅ B 修复（--help / -h 变体）

`src/ProfileCommand.cs`（单文件）：

1. 新增 `const string ProfileCreateUsage`——错误路径与帮助路径共用同一 usage 文本，防两份文案漂移。
2. 新增 `static bool IsProfileCreateHelp(string)`：识别 `--help` `-h`（均 OrdinalIgnoreCase）与 `-?` `/?`（Ordinal）。
3. `ProfileCreate` 在 `args.Length < 3` 守卫之后、取名之前前置 `IsProfileCreateHelp(args[2])` 分支：`Console.WriteLine(ProfileCreateUsage)`（stdout）+ `return 0`。早于 `LoadProfiles`/`SaveProfiles`/`RecordProfileAudit`——零写入、零审计。

刻意范围决策（已在代码注释写明）：

- 裸词 `help` 不拦截——它仍是合法 profile 名；词形帮助由既有 `profile help` 契约覆盖。"--help 及其变体"解释为旗标形态。
- 名后位置（如 `profile create foo --help`）不改：仍走 `Unknown flag` 错误路径（验收 2 的"其它非法调用路径错误行为不变"按最保守解释执行）。

### ✅ C 回归测试（xUnit 层）

新文件 `tests/EnvManager.Engine.Tests/ProfileCreateHelpTests.cs`（复用既有 `TempProfileDir` hermetic 重定向 seam 与 `CliSnapshotSerial` 串行 collection，零注册表接触）：

| 测试 | 钉住内容 |
|------|----------|
| `Create_HelpVariant_ShowsUsageExit0_WritesNothing` (Theory ×6: --help/-h/-?//?/--HELP/-H) | 退出码 0、stdout 精确等于 usage 行、stderr 为空、profiles.json 与 audit.json 均未落盘 |
| `Create_NoName_KeepsUsageError` | 缺名：退出码 1、usage 在 stderr、stdout 空、无写入（行为不变） |
| `Create_UnknownFlagAfterName_KeepsUnknownFlagError` | `create foo --bogus`：退出码 1、"Unknown flag: --bogus"、无写入 |
| `Create_BareHelpWord_RemainsALegalProfileName` | `create help` 仍创建名为 help 的 profile（反过度识别钉） |

验证纪律（CI-only，用户 2026-09-04 令 + spec Phase 4 全波条款）：本窗口零本地构建/零本地测试。交付前的本地证据仅限不构建即可得的检查：两改动文件大括号/小括号平衡计数通过；`ProfileCreateUsage` 全文件仅 1 处声明、`ArgError` 与帮助输出共用同一 const（字节级同源）；"Usage: env-manager profile create" 字面量在 create 路径不再有第二份拷贝。绿色证据由 CI `dotnet test`（build.yml verify job，windows-latest，Release）出具——交大脑推送验证分支触发。

### ✅ D i18n / 文档同步

- i18n：**无变更，判定 N/A**。新增输出仅在 CLI 侧；hard-boundaries 明定 "CLI error messages remain English (CLI is locale-neutral)"，CLI 帮助/错误文案不进入 10 语言 JSON；GUI 未新增任何用户可见字符串，`localizeError` 无新分支需求（帮助是 stdout 正常输出，非错误）。
- `docs/cli-commands.md`：Profiles (detailed) 段新增 bullet（--help/-h/-?//? 名位识别、退出码 0、零写入、裸词 help 仍是合法名）。
- `AGENTS.md`：Testing 段新增票 20 测试盘点行（新测试文件 → 盘点强制要求）。快速参考表不改：该表为命令级清单，无 per-flag 行为描述，命令面未增减（验收第 5 条"若行为描述变化"按此判定）。
- README.md / docs/i18n/README.zh_CN.md：仅示例代码块引用 `profile create`，行为描述无变化，不改。

## 2. issues/20 验收项逐条

| 验收项 | 状态 | 证据 |
|--------|------|------|
| profile create --help 输出帮助、退出码 0，profiles.json 无写入 | ✅ 已实现+测试钉住 | `IsProfileCreateHelp` 前置分支；`Create_HelpVariant_ShowsUsageExit0_WritesNothing`（CI 出门） |
| 不带名与其它非法调用路径错误行为不变 | ✅ | 代码路径未动；`Create_NoName_KeepsUsageError` + `Create_UnknownFlagAfterName_KeepsUnknownFlagError` 钉住 |
| 回归测试钉住（xUnit 或快照层） | ✅ | ProfileCreateHelpTests.cs，4 方法 9 用例，xUnit 层 |
| 新增用户可见字符串走 10 语言 i18n | ✅ N/A（无新增 GUI 字符串；CLI locale-neutral 英文契约） | hard-boundaries v0.7.4 "CLI is locale-neutral" 条 |
| docs/cli-commands.md 与 AGENTS.md 快速参考同步 | ✅ | cli-commands.md bullet + AGENTS.md Testing 盘点行；quick-ref 无 per-flag 面故不动 |

## 3. 改动清单

- `src/ProfileCommand.cs` — help 识别前置分支 + `IsProfileCreateHelp` + `ProfileCreateUsage` const（LF、无 BOM 保持）
- `tests/EnvManager.Engine.Tests/ProfileCreateHelpTests.cs` — 新回归套件（LF、无 BOM）
- `docs/cli-commands.md` — Profiles detailed bullet（LF 保持）
- `AGENTS.md` — Testing 段盘点行（BOM 保留已验证）
- 本报告

## 4. 版本控制

实际落点（2026-09-05 当场核验）：

- 本票代码/文档 hunks（src/ProfileCommand.cs、docs/cli-commands.md、AGENTS.md）在并行工作区中被票 19 窗口的提交 **445a0d9**（arch/19-preflight-two-tier, "feat(preflight): two-tier profile validation..."，"Profile help updated" 同属其列）一并物化；逐文件核验（git show 445a0d9:<file>）确认三处内容字节级完整在位，无语义改写。stack 合并时该 fix 随 19 分支一同进主线，票 20 逻辑不丢失。
- 本票独立分支 **arch/20-profile-create-help**，提交 **wyn**："test(engine): pin profile create --help/-h family as help request with zero store writes (issue 20)"，仅含 tests/EnvManager.Engine.Tests/ProfileCreateHelpTests.cs 一文件（工作区 git status 中其余未提交条目——.zcode plan、票 24 的 LocalAppDataRedirectTests/build.yml/Audit*/CliRuntime/ProfileStorage/SecretMount/SecretProviderManager hunks——均非本票产物，未触碰）。
- 报告与 issues/prompts 同属 .scratch/（.gitignore:90 忽略），按 §3 落盘即完成；不进 git 是仓库既定约定。
- 不 push、不建 PR（WORKFLOW §4.2）。

## 5. 交大脑事项

- 推送 CI 验证分支触发 build.yml verify（xUnit 全绿即过票）；CI 红则按返修流程重开窗口。
- 大脑侧验收建议复看：CI 中 `ProfileCreateHelpTests` 9 用例全绿 + 全库 `dotnet test` 无回归。
