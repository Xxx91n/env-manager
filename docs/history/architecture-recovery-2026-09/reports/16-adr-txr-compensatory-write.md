# 票 16 交付报告 — ADR：禁止 TxR/TxF，制度化补偿式写入

日期：2026-09-03 · 分支：arch/16-adr-txr-txf-ban（GitButler，§4.2）· 状态：待大脑会话验收

## 交付物

1. **docs/adr/0014-no-txr-txf-compensatory-writes.md**（新文件，5297 字节）
   - 弃用依据：MS Learn Transactional NTFS portal 两条官方引语（"strongly recommends ... alternative means"、"TxF may not be available in future versions of Microsoft Windows"），替代清单页 deprecation-of-txf 的 "extremely limited developer interest" / "considering deprecating TxF APIs"。两页均于 2026-09-03 当场核验（ctx_fetch_and_index 原文入知识库）。
   - 关键论据：官方替代清单（whole-file replacement / installer coordination / embedded DB / SQL Filestreams）中**无注册表多值事务原语**——registry hive 场景唯一官方答案是 Windows Installer，不适用交互式变量编辑器。
   - 制度化：补偿式写入五支柱（SetVariable 验证+回滚、write-verify-delete、apply 备份保留、三层锁 mutex/CLI_RWLOCK/writeChain、audit.json + audit_ledger.rs），TxR/TxF/KTM 定为非目标 + 采纳门（新写路径引入 KTM 即评审否决）。
   - 靶点清单（引用不重复实现）：rename write-verify-delete 与 apply 备份保留列为 Phase 3 变异/模型测试首批靶点，指向 WritePathSeamTests / ProfileSeamValidationTests。

## 验收项逐条核验（当场命令输出）

### 1. 新增 docs/adr/0014-*.md — DONE
```
$ head -6 docs/adr/0014-no-txr-txf-compensatory-writes.md
# ADR 0014: No TxR/TxF — Compensatory Writes Are the Only Sustainable Mutation Route

Date: 2026-09-03
Status: Accepted

## Context
```
（文件 5297 字节；Status: Accepted；含两条 MS Learn 引用 URL）

### 2. hard-boundaries.md 与 AGENTS.md 同步引用 — DONE
```
$ git diff -- docs/agents/hard-boundaries.md | grep "^[+-].*ADR 0014" | head -6
+- **No TxR/TxF (ADR 0014)**: registry mutations MUST NOT use the Windows Kernel Transaction Manager (Transacted Registry TxR / Transactional NTFS TxF) or any dependency on it. Microsoft documents TxF as deprecated and possibly removed in a future Windows release, and its official alternatives list contains no registry multi-value transaction primitive (learn.microsoft.com/en-us/windows/win32/fileio/deprecation-of-txf). The only sanctioned mutation route is compensatory writes + three-layer locking + audit recovery (see the Verified registry writes boundary above and ADR 0014 in docs/adr/0014-no-txr-txf-compensatory-writes.md).
+- **Mutation/model test first targets (ADR 0014)**: rename write-verify-delete ordering and apply/unapply backup preservation are the first targets for the Phase 3 differential/model/mutation test upgrade (architecture-recovery spec Phase 3); suites pin them by reference (WritePathSeamTests, ProfileSeamValidationTests) — never re-implement the contract inline in a new suite. Never delete-then-set for renames.

$ git diff -- AGENTS.md | grep "^+" | head -3
+++ b/AGENTS.md
+- **Rename/scope-change contract**: write+verify target before deleting source. Never delete-then-set. Registry mutations are compensatory-write only: TxR/TxF are non-goals (ADR 0014, docs/adr/0014-no-txr-txf-compensatory-writes.md).
```
hard-boundaries.md 新增两条红线（No TxR/TxF + Mutation/model test first targets），插在 "Verified registry writes" 条目之前；AGENTS.md 顶栏 Rename/scope-change contract 行追加 ADR 0014 指针。另同步：CONTEXT.md Decisions 段新增 ADR 0014 段落、docs/agents/reference-index.md 新增索引行。

### 3. rename write-verify-delete、apply 备份保留列为首批靶点（引用不重复实现）— DONE
ADR 0014 "Mutation/model test first targets" 节明确 "by reference, not re-implementation"，靶点指向既有 WritePathSeamTests / ProfileSeamValidationTests 与 spec Phase 3 的三件套，未新增任何测试或代码。本票零代码变更（git diff --stat 仅文档）。

### 4. 文档三层一致性（CONTEXT.md / docs/adr/ / 代码现状）— VERIFIED 18/18
当场核验脚本 18 项全 OK：ADR 内容 5 项（标题/状态/双 URL 弃用依据/无注册表原语论据/五支柱符号）、CONTEXT 段落 2 项、hard-boundaries 3 项、AGENTS 1 项、reference-index 1 项、代码现状 6 项（WriteOutcome.RollbackFailed 于 src/VariableWrite.cs、EnvManager.RegistryMutation 于 src/Program.cs、CLI_RWLOCK 于 frontend/src-tauri/src/main.rs、writeChain 于 frontend/src/lib、service/src/audit_ledger.rs、tests/WritePathSeamTests.cs）。
```
$ git grep -n "ADR 0014" -- CONTEXT.md
CONTEXT.md:74:[ADR 0014](docs/adr/0014-no-txr-txf-compensatory-writes.md) bans TxR/TxF as non-goals: Microsoft documents TxF as deprecated (possibly removed in a future Windows release) and its official alternatives list has no registry multi-value transaction primitive. Compensatory writes + three-layer locking (mutex + CLI_RWLOCK + writeChain) + audit recovery are the institutionalized route for every registry mutation; rename write-verify-delete and apply backup preservation are the first mutation/model test targets.

$ git grep -n "0014" -- docs/agents/reference-index.md
docs/agents/reference-index.md:20:| ADR 0014: TxR/TxF non-goals, compensatory writes + three-layer locking + audit recovery institutionalized | [docs/adr/0014-no-txr-txf-compensatory-writes.md](docs/adr/0014-no-txr-txf-compensatory-writes.md) |

$ git grep -n "RollbackFailed" -- src/VariableWrite.cs | head -2
src/VariableWrite.cs:74:            case WriteOutcome.RollbackFailed:

$ git grep -n "EnvManager.RegistryMutation" -- src/Program.cs | head -2
src/Program.cs:375:        var mutex = new Mutex(false, "Local\\EnvManager.RegistryMutation");
```

## 门禁与卫生

```
$ pwsh -NoProfile -File scripts/check-doc-sync.ps1
=== Doc sync check PASSED ===   (exit 0)
$ git diff --check
(no output — clean)
$ git diff --stat
 AGENTS.md                                                    | 2 +-
 CONTEXT.md                                                   | 2 ++
 docs/agents/hard-boundaries.md                               | 2 ++
 docs/agents/reference-index.md                               | 1 +
 tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj | 1 +
 5 files changed, 7 insertions(+), 1 deletion(-)
```
注：diff --stat 中 tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj（+CsCheck 4.8.0）是并行票 12 的工作，本票提交已排除。EOL：四份编辑文件均保持 LF（AGENTS.md 保留其原有 UTF-8 BOM）；reference-index.md L46 的一个预存 CR 早于本票存在、未被触碰。

## 备注

- 弃用依据两页 MS Learn 原文已核验入会话知识库（source: ms-learn-txf-portal / ms-learn-txf-deprecation）。
- 版本控制按 WORKFLOW §4.2：GitButler 分支 arch/16-adr-txr-txf-ban，conventional commit（docs(adr): ...）；已推 origin，用户授权，2026-09-04 收口修正。
