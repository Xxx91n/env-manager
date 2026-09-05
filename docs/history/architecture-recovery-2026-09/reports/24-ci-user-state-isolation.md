# 报告 24 — CI 用户态隔离（LOCALAPPDATA 重定向 + 快照语义纪律）

> 状态：implementation complete（待大脑合并 arch/17 + 本分支后触发 CI 取全绿证据，见检查点 D）
> 票：.scratch/architecture-recovery/issues/24-ci-user-state-isolation.md
> 版本控制：WORKFLOW §4.2（GitButler 独立分支 arch/24-ci-user-state-isolation；只提交本票文件/hunks；.scratch 报告按惯例不入库）

## GitButler 交付状态（最终）

- 分支 `arch/24-ci-user-state-isolation`，两个提交：
  - `kpr` `feat(ci): isolate CI user-state via ENVMANAGER_LOCALAPPDATA redirect seam (issue 24)`：.github/workflows/build.yml、AGENTS.md、docs/build-and-release.md、src/{AuditCommand,AuditCrypto,ProfileStorage,SecretMount,SecretProviderManager}.cs、tests/EnvManager.Engine.Tests/LocalAppDataRedirectTests.cs（新）。
  - `ltl` `refactor(engine): route user-state paths through LocalAppDataRoot seam (issue 24)`：src/CliRuntime.cs。
- 分支栈位：arch/18 栈顶（17 → 21 → 18 → 24），显式包含对票 21（CliRuntime.cs 文件创建者）与票 17（同 workflow 文件先绿）的依赖。
- 引擎缺陷与解法（已记入 WORKFLOW §6 教训日志 2026-09-05 行）：对票 21 新建文件的 hunk，中间栈位分支 commit/amend 均被 "depends on arch/21 (qtp)" 拒绝（--anchor/--above/--branch 三种堆叠皆无效）；把分支 move 到栈 TIP 后放行。
- 未提交区剩余文件（fuzz.yml、EnvManager.Fuzz/*、env-manager.csproj、.zcode 计划）属票 25 与其他会话，本票未触碰。
- 提交顺序说明：kpr（调用方文件）在 ltl（seam 定义）之前——分支内 bisect 穿越时 kpr 点不可编译，ltl 即修复；如大脑在意可 move 调序，本窗口因引擎缺陷不再做多余 history 操作。

## 检查点 A — 票 17 状态确认（开工前置）

- 票 17 的两个提交已在其 GitButler 分支落地：`arch/17-verify-cli-staging`（rwm `ci(verify): stage CLI bundle resources before cargo steps` + wsy `fix(ci): seed frontendDist placeholder for generate_context! in verify`），已应用到本工作区（工作树 build.yml 含 staging step，.github/workflows/build.yml:91-131）。
- main（d71b30d）当前 verify 为红（run 33880367303）：`Run Tauri crate tests` 步骤失败，根因 `resource path 'bin\env-manager-cli.exe' doesn't exist`（job 日志 2026-09-04T13:57:50Z）——即 main 尚未包含票 17 的 staging step。合并归大脑会话（WORKFLOW §4.1）。
- 结论：票 17 实现完成、待大脑合并；本票按启动器"与其他分支并行修复"在独立分支上开工，分支拓扑由大脑在合并期定序（17 先于 24 合入即可满足"先绿再隔离"的语义——本票的隔离步骤叠加在 17 的同一 workflow 文件上，两票改动区域不相交：17 改 staging step，24 改 Pester 步骤及其后）。

## 勘察结论（实现依据，均附当场证据）

1. **CLI 用户态落点机制**：所有 CLI 用户态文件（profiles.json、audit.json/audit.key、secretMount.json、secret-providers.json、provider-hash.json、protection JSON）以 `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` + `"EnvManager"` 组合解析；主路径经 `Program.AppDataDirectory`，旁路直用点 7 处。`rg -n "GetFolderPath\(Environment\.SpecialFolder\.LocalApplicationData\)" src/` 在改动前返回 10 处（2 处为 sops/op 二进制发现，属读路径，不属用户态写入面）。
2. **纯环境变量重定向无效（实测）**：本机只读探针（.codex-tmp 临时脚本，已删）证实 `[Environment]::GetFolderPath('LocalApplicationData')` 在进程级 `LOCALAPPDATA` 覆盖下返回 `C:\Users\Administrator\AppData\Local` 不变（`HONORED: False`）——.NET shell-folder API 从注册表 `HKCU\...\Explorer\User Shell Folders` 展开，不读进程 env。与 research/round4-closeout-patterns.md A 节"LOCALAPPDATA 重定向是用户态隔离的正确形态"结合，得出：重定向形态正确，但必须经 CLI 侧 seam 承载，workflow 层无法单方面完成。
3. **既有 seam 先例**：`SetAppDataDirectoryForTests`（票 18）等是进程内静态字段，跨进程（Pester harness → CLI 子进程）不可用；本票 seam 选用环境变量 `ENVMANAGER_LOCALAPPDATA` 作为 cross-process 等价物。
4. **范围边界**：`%ProgramData%`（服务账本）属机器态（两级纪律第一级：fresh-VM 无害），不改；Rust GUI/service 不在 Pester 套件执行面内，不改。

## 实现记录

### CLI seam（commit: src/ 六文件 + 新测试）

- `src/CliRuntime.cs`：新增 `internal static string LocalAppDataRoot`（读 `ENVMANAGER_LOCALAPPDATA`，非空即用，空/未设回退 `GetFolderPath`）；`AppDataDirectory` 与静态字段 `ProviderHashPath` 改经该属性。
- 旁路直用点收敛：`AuditCrypto.cs`（AuditKeyPath）、`AuditCommand.cs`（audit list 路径）、`SecretMount.cs`（SecretMountFilePath）、`ProfileStorage.cs`（ProfilesFilePath）→ `LocalAppDataRoot`；`SecretProviderManager.cs`（GetConfigPath + SetActiveProvider 落盘目录，独立类）→ `Program.LocalAppDataRoot`。
- 收敛后全库检查：`rg` 仅剩 seam 自身 fallback 一处 + sops/op 二进制发现两处（读路径，符合范围边界）。
- 生产路径行为零变化：变量未设时回退表达式与原实现逐字等价（同样的 `GetFolderPath` 调用）。

### xUnit 测试（tests/EnvManager.Engine.Tests/LocalAppDataRedirectTests.cs，新文件）

- 三个 [Fact]：变量设置时 root 被重定向；未设/空串时回退 shell LocalApplicationData（空串必须等价未设，不得回退到 cwd）。
- 串行 collection（`DisableParallelization = true`）+ finally 恢复环境变量（env 是进程全局态，遵循 AGENTS.md "Process-scoped and cleared in-test" 测试规则）。
- 断言面刻意停在 `LocalAppDataRoot`：组合路径 getter 先消费各自的 test-override 字段（票 04/18 seam），并行套件合法改写它们会让断言抖动；seam 属性直读进程环境，确定性强。

### workflow（.github/workflows/build.yml）

- `Run Pester integration tests` 步骤：`env: ENVMANAGER_LOCALAPPDATA: ${{ runner.temp }}\test-user-state`（runner.temp = job 私有目录）；run 块首行建目录并 `Write-Host "=== issue 24 user-state isolation: ENVMANAGER_LOCALAPPDATA=... ==="`（run 日志可见）；步前记录真实 `%LOCALAPPDATA%\EnvManager` 存在性到 `ISSUE24_REAL_DIR_EXISTED`（GITHUB_ENV），供断言步骤区分镜像预置与 run 产生。
- 新增 `Assert user-state isolation (issue 24)` 步骤（`if: always()`）：若 run 前真实目录不存在而 run 后存在 → throw（重定向失效即红）；若预置存在则打印说明（镜像态，永不删除）；最后打印重定向目录内容（含 profiles.json 时列出全部状态文件名）作为 redirect proof。
- 静态验证：YAML 结构解析 OK（python yaml.safe_load，verify 步骤数 24，两步骤名在列）；两 pwsh run 块经 `[System.Management.Automation.Language.Parser]::ParseInput` 零错误。

### 纪律文档化（检查点 C）

- `docs/build-and-release.md` 新段 "CI user-state isolation and env-block snapshot semantics (architecture-recovery issue 24)"（CI/CD Workflows 节内）：重定向机制 + GetFolderPath 不随进程 env 的原因 + 两级隔离纪律（机器态=fresh-VM 无害，注册表侧另有 test-with-restore 事务与票 22 残留归零断言兜底；用户态=job 内不共享）+ env-block 快照语义三守则（子进程继承 spawn 时刻快照→变量必须先设后 spawn；写注册表者自身 env-block 不刷新→刷新断言读注册表或产新子进程；hosted runner 恒已提权→elevation 门控路径在 seam 层钉住）。
- `AGENTS.md` Testing 节同步段（issue-04 基线段与 issue-12 基线段之间）：seam + 测试 + workflow 断言一句话索引，链接 docs 段名。

## 并行协作边界（WORKFLOW 教训③④执行记录）

- 提交前 `but diff` 出现他窗口并行改动（.github/workflows/fuzz.yml、tests/EnvManager.Fuzz/Program.cs、.zcode 计划文件、票 14 快照段等）：本票提交只选本票文件的 hunks，绝不吸收。
- 两处 hunk 锚定规避：docs/build-and-release.md 文档段最初误插票 22 未提交段之后（且留有重复标题残留），已撤回重插到 release.yml 段后纯基线锚点并清除残留；AGENTS.md 段落最初上锚 issue-22 段（arch/22 分支行），已下移到 issue-04/issue-12 两基线段之间。最终 `git diff` hunk 头均为基线上下文。
- AGENTS.md 的段落位置选择使本票 hunk 与 arch/20/22/23 分支的 hunk 无上下文重叠，合并期无需人工 fold。

## 验收项证据对照（issues/24 五项）

| 验收项 | 状态 | 证据 |
|---|---|---|
| Pester 集成步骤重定向 LOCALAPPDATA 到 job 私有目录，run 日志可见 | 已实现（代码层） | build.yml Pester step `env` 块 + `Write-Host` 行；YAML/pwsh 解析零错误；运行时证据待 CI（检查点 D） |
| 测试后机器用户态无污染（run 内验证步骤或自检输出） | 已实现（代码层） | `Assert user-state isolation (issue 24)` 步骤（if: always()，throw 即红）+ redirect proof 打印；运行时证据待 CI |
| env-block 快照语义纪律文档化（测试不假设跨进程实时刷新） | 完成 | docs/build-and-release.md 新段三守则；AGENTS.md 同步段 |
| verify job 在票 17 staging 之上全绿（gh run 证据） | 待大脑 | arch/17 未合入 main（run 33880367303 Tauri 步骤红为缺 staging 的直接证据）；合并后触发 CI 取 gh run 全绿证据 |
| docs/build-and-release.md 测试隔离段同步 | 完成 | 新段 + AGENTS.md 索引段落盘 |

## 检查点 D — 移交大脑的 CI 触发清单

1. 合并 `arch/17-verify-cli-staging`（先）与 `arch/24-ci-user-state-isolation`（后）到 main（两票 build.yml 改动区域不相交）。
2. push main 触发 verify：预期 Pester 步骤日志出现 `issue 24 user-state isolation: ENVMANAGER_LOCALAPPDATA=...`，`Assert user-state isolation (issue 24)` 打印 `user-state isolation OK` 或 redirect proof，且 `Run C# engine unit tests` 含 LocalAppDataRedirectTests 3 例。
3. gh run 全绿 = 验收项 1/2/4 的运行时证据齐备；若隔离断言步骤红（真实目录被创建），按报错排查 redirect 是否被覆盖（唯一已知风险面：未来新增代码绕过 LocalAppDataRoot 直写 GetFolderPath——rg 检查命令见勘察结论 1）。

## 验证纪律声明（CI-only 政策，2026-09-04 用户令）

本窗口未运行任何构建/编译/测试命令（无 dotnet build/test、无 cargo、无本地 Pester）。本地完成的验证仅为静态解析（YAML、PowerShell Parser、rg 落点清单、git diff --check）与只读行为探针。全部运行时证据由大脑合并后触发 CI 提供（上节清单）。

---

## 收口修正（2026-09-05 大脑）

- 本文档写作时含「待 CI」表述；PR #45（head=完整 11 提交栈）已全绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success + Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿），本票终态 = ✅ done（README 已登记）。
