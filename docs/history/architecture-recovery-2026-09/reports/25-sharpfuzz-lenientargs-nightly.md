# 报告 25 — SharpFuzz 夜间模糊（参数解析面 + corpus 入库）

> 子窗口执行窗口交付报告（2026-09-05）。启动器：prompts/25-sharpfuzz-lenientargs-nightly.md。
> 验证纪律（CI-only，用户 2026-09-04 令）：本窗口零本地构建/测试/插桩；全部验证走 CI（见 §触发指引）。
> 版本控制：遵循 WORKFLOW §4.2 —— GitButler 分支 `arch/25-sharpfuzz-lenientargs`，只提交本票改动，不 push。

## Blocked-by 确认（检查点 A）

票 18 已交付：分支 `arch/18-mutation-survivor-triage`（提交 e6f5fd9 已在工作区历史；README 波次表记「分支已交付，待大脑 CI 重跑」）。分诊清理了无覆盖/弱断言幸存者（登记 13 弱断言 + 1 等价，补杀测试 6 个）——票 25 的启动前提（避免重复投资）已满足。

## 必读清单执行记录

handoffs/25 ✓ · issues/25 ✓ · spec.md（Phase 4 段）✓ · research/round4-closeout-patterns.md（B 节）✓ · WORKFLOW.md ✓ · docs/agents/hard-boundaries.md（全 231 行）✓。工具链细节按启动器授权以官方一手来源核实（SharpFuzz README + docs/libFuzzer.md + fuzz-libfuzzer.ps1 源码 + NuGet/GitHub API），未走 atomcode（官方文档即权威，单发省一环）。

## 检查点 B — ReadyToRun 前提验证结论

**结论：仓库现状天然满足 SharpFuzz 插桩前提，零改动；fuzz 链路自身加双保险。**

证据链（当场命令输出锚定）：

1. `grep -n -i "readytorun\|PublishReadyToRun\|r2r"` 于 scripts/build.mjs、env-manager.csproj、.github/workflows/*.yml → **零命中**（无 Directory.Build.props）。
2. `scripts/build.mjs:210`：生产 CLI 发布为 `dotnet publish -c Release -r <rid> --no-self-contained -p:PublishSingleFile=true` —— framework-dependent、无 R2R。
3. CI verify（build.yml）`dotnet build -c Release` 同样不启用 R2R。
4. 原理（round4-closeout-patterns.md B 节「.NET 8+ 发布产物剥 ReadyToRun」）：SharpFuzz 经 CLR profiler 在 JIT 前重写 IL 注入覆盖反馈；R2R 预编译的原生映像绕过 IL 层，插桩失效、覆盖导向丢失。
5. 双保险落地：`tests/EnvManager.Fuzz/EnvManager.Fuzz.csproj` 显式 `PublishReadyToRun=false`/`PublishSingleFile=false`（single-file bundle 内 dll 不可独立插桩，一并禁掉），fuzz.yml publish 步骤再以 `-p:PublishReadyToRun=false -p:PublishSingleFile=false` 命令行断言（防未来全局 props 变更静默破坏）。

## 检查点 C — harness + 异常二分纪律

**落盘**：`tests/EnvManager.Fuzz/`（独立 exe 工程，net10.0-windows，ProjectReference 引擎；仅由 fuzz workflow 构建，不进 verify/build.mjs，零发布产物影响）。

- `Program.cs`（顶层语句）：`Fuzzer.LibFuzzer.Run(span => ...)`（SharpFuzz 2.3.0，`ReadOnlySpanAction = void(ReadOnlySpan<byte>)`，master 版本号 2.3.0 与 NuGet 最新版一致，API 已核实存在）。输入 span 以 UTF-8 解码为命令行（替换式回退，解码不抛），逐输入驱动三个不受信面：
  1. `LenientArgs.Tokenize(commandLine)`（含 SkipProgramPath/引号翻转/反斜杠串全路径）；
  2. `Program.IsWriteInvocationForFuzz(tokens)` —— 调度面纯函数 seam（新增于 src/CliRuntime.cs：null 元素按畸形输入返回 false，防 fuzzer 注入 null argv 造成假 crash；内部转调未改动的 `IsWriteInvocation`，锁门槛路由行为零变化）；
  3. `LenientArgs.WasArgsCorruptedByTrailingBackslashQuote(tokens)`（恢复判定）。
- **异常二分纪律**（对照验收项逐类）：catch 过滤器仅 `FormatException`、`ArgumentException`（含 ArgumentNull/OutOfRange 子类）、`OverflowException` 三类吞掉；`NullReferenceException`/`IndexOutOfRangeException`/`OutOfMemoryException`/`AccessViolationException` 及其余一切未预期异常逃逸回调 → libFuzzer 记 crash；`StackOverflowException` .NET 不可捕获、进程终止 → driver 记 crash。
- **插桩面**：只对 `env-manager-cli.dll` 执行 `sharpfuzz`（fuzz.yml 步骤），harness dll 与 SharpFuzz.Common/dnlib 按官方 fuzz-libfuzzer.ps1 排除表不插桩。
- **复现路径**：`Fuzzer.LibFuzzer.Run` 非 libFuzzer 环境内建文件模式——`dotnet EnvManager.Fuzz.dll <crash-file>` 单次重放，CI smoke 步骤即用此路径逐种子验证 harness 可执行性。
- 依赖授权：env-manager.csproj 新增 `<InternalsVisibleTo Include="EnvManager.Fuzz" />`（与既有 Engine.Tests 条款同模式；纯编译期 attribute，发布产物不变）。

## 检查点 D — 种子 corpus 入库

`tests/EnvManager.Fuzz/Corpus/`（入库，字节级 `od -c` 验证，27 文件，无行尾终止符）：代表性真实命令面 + 对抗形态——`01 list`、`03-08 set/rename/change-scope/delete/toggle --scope/--overwrite`、**`09/10 尾反斜杠+引号受难例**（`"C:\Program Files\PowerShell\7\" --scope user`，ArgTokenizer 注释中的 canonical victim）、`11 空引号 PATH 条目`、`12 嵌套 %VAR%`、`13 launch -- 分隔符 + 嵌套转义引号`、`15 嵌入 flag 合并形`、`16 多空格`、`17/18 带程序路径前缀`、`19 反斜杠串`、`20 未闭合引号`、`21 空输入`、`22 secret 形态`、`23-26 history/bulk/flag 串`、`25 真实控制字节 0x01/0x02`、`27 嵌入 NUL 字节`。

## 检查点 E — workflow（夜间长跑 + PR 短跑不阻塞）

**落盘**：`.github/workflows/fuzz.yml`（windows-latest；行为对齐 build.yml 的 pin-SHA 与行尾门禁）。

- 触发面三件：`schedule: cron '30 18 * * *'`（UTC 18:30 = 北京 02:30，仅 main，夜间长跑 `-max_total_time=1800`）；`pull_request`（paths: src/**、env-manager.csproj、tests/EnvManager.Fuzz/**、fuzz.yml；短跑 300s，job 级 `continue-on-error: ${{ github.event_name == 'pull_request' }}` **不阻塞 PR**，crash 仍上传 artifact 可见）；`workflow_dispatch`（`max_total_time` 输入可覆写，供大脑触发）。
- 步骤链：行尾门禁 → setup-dotnet 10.0.x → publish harness（R2R 断言）→ `dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0`（工具版本已核实 NuGet 存在）→ `sharpfuzz` 插桩引擎 dll → 下载固定 release tag 驱动 `libfuzzer-dotnet-windows.exe` v2025.05.02.0904 并把 SHA256 写入 step summary（URL 级不可变锚点；hash 硬 pin 留待首跑记录后加固）→ 种子独立 smoke（逐文件重放、exit 0）→ libFuzzer 主跑（`-timeout=10 -rss_limit_mb=4096 -max_len=4096 -max_total_time=<N> -print_final_stats=1 -artifact_prefix=…`，语料双目录：可写成长目录 + 只读入库种子目录）→ `fuzz-results-<run_id>` artifact（日志 + crash 文件 + 成长语料）。
- **运行时长与发现数证据**：每次跑把 `FUZZ_TIME`、driver exit code、crash artifact 计数写入 `$GITHUB_STEP_SUMMARY` 与 `fuzz/fuzz-run.log`（Tee 全量 libFuzzer 输出含 `stat::number_of_executed_units` 终值）——0 发现时 exit=0 + crash 数 0 即证据。

## 版本控制（WORKFLOW §4.2 执行记录）

- 分支 `arch/25-sharpfuzz-lenientargs`（`but branch new --above arch/24-ci-user-state-isolation` 创建，叠于 arch/24 之上）；主提交 `stk`：fuzz.yml + 27 corpus 文件 + EnvManager.Fuzz.csproj + Program.cs + env-manager.csproj IVT hunk（hunk 级 ID 挑选）。不 push、不建 PR。
- **CliRuntime.cs seam 归属实情**：`IsWriteInvocationForFuzz` seam 已被并行 arch/24 agent 的整文件提交 `ltl`（sha 7abad0f，"route user-state paths through LocalAppDataRoot seam"）吸收——提交 blob 行 287 已含该 seam（当场 `git show <sha>:src/CliRuntime.cs` 验证）。本票未移动/修改他 agent 提交；因 arch/25 叠于 arch/24 之上，seam 在本分支祖先链中编译成立。归属交叉已登记，交大脑验收期核（选项：维持现状，arch/24 合入即携带；或大脑在 fold AGENTS.md 时一并把 seam 归属改写进本票提交）。
- **AGENTS.md parked hunks（2 个，本票所有、未能提交）**：`wsm:2`（Testing 段 InternalsVisibleTo 句改写：only → to Engine.Tests and EnvManager.Fuzz）与 `wsm:7`（issue 25 SharpFuzz 段新增）。`but commit` 报 "conflicts with commits on arch/17/18/19/21/22/23/24"——Testing 段 7 票段落交错、上下文锚定 7 个并行 sibling 提交行，GitButler 文件级跨栈校验拒绝（票03 同款引擎缺陷形态）；7 个 sibling 无单一可叠放祖先，无法按防线①消解。按票03 防线②③处置：parked + 本清单登记，**大脑合并期人工 fold**。

## 验收项映射（issues/25）

| 验收项 | 状态 | 证据 |
|---|---|---|
| harness + 异常二分纪律 | ✅ | tests/EnvManager.Fuzz/Program.cs（catch 三类，其余逃逸）+ src/CliRuntime.cs `IsWriteInvocationForFuzz`；CI run 33943975560 编译+插桩+种子冒烟全绿 |
| 种子 corpus 入库 | ✅ | tests/EnvManager.Fuzz/Corpus/ 27 文件（od -c 字节验证）+ CI 冒烟 27/27 exit 0 |
| 夜间 workflow cron 且不阻塞 PR；PR 短跑形态 | ✅ | fuzz.yml（cron '30 18 * * *' / continue-on-error / 300s 短跑）；PR #42 实跑验证 |
| ReadyToRun 前提验证结论记录 | ✅ | 本报告 §检查点 B（grep 零命中 + build.mjs:210 + csproj/CI 双断言） |
| 首次夜间跑输出含时长与发现数 | ✅ | CI run 33943975560：`Done 6496557 runs in 301 second(s)`、`exit=0 crash-artifacts=0`、new_units_added=1454（见 §检查点 F） |

**验收状态**：issues/25 五项全勾（Status: done）。波次表更新已提交大脑会话复核（WORKFLOW §4.4：大脑对照验收项逐条核验后登记 done 并合并分支）。

## 检查点 F — 首次 CI 跑证据（2026-09-05 已取得）

- **触发路径**：GitHub 平台约束实测——`workflow_dispatch`/`schedule` 只能寻址默认分支上的 workflow（fuzz.yml 未落 main 时 dispatch 返回 404），故经 **PR #42（draft，base main ← arch/25，头 f3b664a）** 触发 pull_request 短跑形态。栈合并进 main 后 cron `30 18 * * *` 自动接管夜间 1800s 形态。
- **Run 33943975560（Fuzz (SharpFuzz nightly)，pull_request，windows-latest）**：
  - 全链路绿：publish harness → SharpFuzz 2.3.0 工具 → `sharpfuzz` 插桩 env-manager-cli.dll → driver 下载（sha256 `17AF5B3F6FF4D2C57B44B9A35C13051B570EB66F0557D00015DF3832709050BF`）→ **种子冒烟 OK**（27 文件独立重放全 exit 0，验证 harness 可执行）→ libFuzzer 主跑
  - **运行时长与发现数（验收项 5 证据）**：`Done 6496557 runs in 301 second(s)`；`stat::number_of_executed_units: 6496557`；`stat::average_exec_per_sec: 21583`；`stat::new_units_added: 1454`；`stat::peak_rss_mb: 29`；step summary + 日志锚定 `libFuzzer exit=0 crash-artifacts=0` —— **0 发现，有证据**
  - artifact：`fuzz-results-33943975560`（76,154 字节，含 fuzz-run.log / artifacts / corpus-grown）
- **加固闭环**：driver SHA256 已从首跑记录回填 fuzz.yml 硬 pin（`DRIVER_SHA256` env + fail-closed 校验，提交 `srk`，分支头 bfb9c6e）。**第二轮 CI（run 33944493440，synchronize 触发）验证 pin 生效**：sha256 校验通过、`Done 6229387 runs in 301 second(s)`、`libFuzzer exit=0 crash-artifacts=0`、new_units_added=1414→1412、peak_rss 29MB —— pin 引入无回归，连续两轮 0 crash。
- **运行中警告（非失败）**：`WARNING: Failed to find function "__sanitizer_acquire_crash_state"` 为 libfuzzer-dotnet 驱动在 Windows 上的已知无害告警（crash 经 exit code + artifact_prefix 仍正常记录）；`__AFL_SHM_ID` 不存在即走 LibFuzzer 文件模式，行为符合预期。
- **连带观察（非本票文件）**：同 PR 的 Workflow Lint（actionlint）红于 `build.yml:460` shellcheck SC2012（`ls -t`，票 18 stryker job 内既有行），本票 fuzz.yml 无 lint 发现；已登记交大脑，不修改他票代码。

## 遗留与风险

- **AGENTS.md parked hunks**：`wsm:2` + `wsm:7` 留在工作区未提交（跨栈依赖拒绝，见 §版本控制）；大脑合并期 fold，验收时以本报告清单 + `but diff` 现场核对。
- **CliRuntime.cs seam 归属**：`IsWriteInvocationForFuzz` 物理上随 arch/24 提交 `ltl` 入库；arch/25 叠于其上编译成立。若大脑重排/放弃 arch/24，需先把 seam hunk 迁回本票分支（内容见 src/CliRuntime.cs:286-291）。
- **编译验证未本地执行**（CI-only 政策）：C# 编译错误、sharpfuzz 插桩兼容性、driver/托管 IPC 形态由 CI 首跑验证；若红按 §触发指引 4 修复。
- libFuzzer-mode Windows 支持以官方 docs/libFuzzer.md + 预编译 Windows driver 为据（研究 B 节印证）；driver 的 SHA256 硬 pin 为首跑后加固项。
- PR 短跑为 `continue-on-error`，其绿/红不进入分支保护必检集合（repo 未配置 required checks 于本 workflow）。

---

## 收口修正（2026-09-05 大脑）

- 本文档写作时含「待 CI」表述；PR #45（head=完整 11 提交栈）已全绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success + Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿），本票终态 = ✅ done（README 已登记）。
