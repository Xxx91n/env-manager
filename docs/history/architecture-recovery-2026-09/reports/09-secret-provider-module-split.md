# Report 09 — SecretProvider.cs 按 provider 拆模块（一 provider 一文件，行为零变化）

日期：2026-09-02 · 分支：GitButler `arch/09-secret-provider-split`（未 push，按 WORKFLOW §4.2）· 状态：子窗口自验全绿，待大脑会话按 issue 验收项核验。

## 实施方式

纯文件搬迁重构（票 05/06 同型）：node 字符串切片搬运（indexOf/lastIndexOf 锚点 + LF 常量，禁 split/join 重组——WORKFLOW 教训日志 2026-08-31 防线①）、每步写前备份 OS temp、写后片段探针 + BOM/CRLF/括号平衡校验。六个检查点 A→F，每点 `dotnet build` + `dotnet test` 全绿后 GitButler 提交。

## 检查点与提交（全部当场门禁绿）

| 检查点 | 内容 | 提交 | 门禁 |
|---|---|---|---|
| 锚 | 空锚提交（教训日志票 05 防线②：历史改写前先提交） | swq | — |
| A | SecretEnvelope + SecretEnvelopeJsonContext + ProviderConfigJsonContext + ISecretProvider → 4 文件 | zsu | build 0 错误 + 86/86 |
| B | DpapiCurrentUserProvider + CredentialManagerProvider → 2 文件 | onr | 同上 |
| C | PowerShellSecretManagementProvider → 1 文件 | wox | 同上 |
| D | VaultKV2Provider + SopsProvider → 2 文件 | lut | 同上 |
| E | AzureKeyVaultProvider + OnePasswordProvider + AwsSecretsManagerProvider → 3 文件 | kwz | 同上 |
| F | SecretProviderManager（含 ProviderConfig）→ SecretProviderManager.cs；删除 src/SecretProvider.cs | oyn | 同上 |
| 收尾 | 前端 3 门禁文件重指向 + secret-provider-source.ts helper + AGENTS.md/docs 引用同步 | wmw | dotnet 86/86 + vitest 50/50（3 文件）+ 398/398（全套） |

## 验收项逐条核验（当场命令输出）

### 1. SecretProvider.cs 文件删除；13 符号一符号一文件 — PASS

- `fs.existsSync('src/SecretProvider.cs')` → `false`（删除于检查点 F，删除前最终态备份 OS temp `env-manager-ticket09-SecretProvider.cs.final.bak`）。
- 新模块清单（`ls src/*.cs` 过滤，13 个新文件 + 既有 DpapiHelper.cs/SecretMount.cs）：
  - 核心：SecretEnvelope.cs (72 行)、SecretEnvelopeJsonContext.cs (16)、ProviderConfigJsonContext.cs (15)、ISecretProvider.cs (31)、SecretProviderManager.cs (280)
  - provider：DpapiCurrentUserProvider.cs (49)、CredentialManagerProvider.cs (178)、PowerShellSecretManagementProvider.cs (259)、VaultKV2Provider.cs (182)、SopsProvider.cs (269)、AzureKeyVaultProvider.cs (280)、OnePasswordProvider.cs (220)、AwsSecretsManagerProvider.cs (176)
- 行数账：原单文件 1899 行 → 13 模块合计 2027 行，delta +128 = 13 份 per-file 头注释 + usings + namespace 声明。所有类型体逐字节搬移（切片搬运，未触碰声明内部）。

### 2. "SecretProvider.cs" 文件名活引用清零 — PASS

验收范围（src/、docs/、AGENTS.md、frontend/src/，node 递归扫描等价于 `rg -n "SecretProvider\.cs"`，排除 gitignored/构建目录）最终命中 4 处，全部为 `ISecretProvider.cs`（新文件名含旧名子串）或其引用，非旧文件活引用：

```
AGENTS.md:86                                          （结构树新行：.../ISecretProvider.cs）
docs/architecture.md:304                              （Phase 1-2 指针：src/ISecretProvider.cs）
src/ISecretProvider.cs:1                              （文件自身头注释）
frontend/src/lib/secret-provider-source.ts:11 'ISecretProvider.cs' （门禁 helper 模块清单）
```

- src/ 下 13 个新文件头注释已从 "Split from the retired single-file src/SecretProvider.cs" 改写为 "One-symbol-per-file split of the retired single-file secret provider module (issue 09)"（消除机械 rg 误报）。
- 前端 3 个门禁测试文件（secret-regression / v0.7.2-secrets / secret-timeout-memory）`readFileSync` 读路径全部重指向 `readSecretProviderSources()`（helper 按原单文件顺序拼接 13 模块，全部 `indexOf` 切片断言语义不变）；v0.7-secrets.test.ts 经核不读该路径（handoff"4 文件"为"可能"措辞，实际命中 3 文件）。
- docs/architecture.md 5 处 Phase 指针改为具体新文件；AGENTS.md 结构树 L85-88 重写。

### 3. 行为零变化 — PASS

| 门 | 结果 | 当场输出 |
|---|---|---|
| dotnet test | 86/86 | `已通过! - 失败: 0，通过: 86，已跳过: 0，总计: 86`（每个检查点后复跑） |
| vitest 全套 | 398/398 | `Test Files 40 passed (40) / Tests 398 passed (398)`（23:51，拆分+重指向后） |
| release/cli-only 刷新 | 4 产物 | `node scripts/build.mjs --arch x64 --skip-gui --skip-msi` + Release 构建产物同步，env-manager-cli.exe/dll/deps.json/runtimeconfig.json + zip 均为 23:47-23:51 拆分后构建（票 04 B1 教训落实） |
| run-ci-tests 四套件 | 全绿 | launch-env-injection 6 passed/0 failed；canary-redaction 9 passed/0 failed；inheritance-protection PASS (4/4)；test-with-restore `ALL TESTS PASS + exact registry and internal-config snapshots match`；聚合横幅 `=== CI test tier PASSED ===` |
| clean build 警告账 | 8 条与基线持平 | `dotnet clean` + `dotnet build`：4 条既有 CS8602（AgentsCommand/Program/UpdateCommand，本票未触碰文件）+ 4 条 CS8600 随代码搬移转移路径至 OnePasswordProvider.cs(28)/SopsProvider.cs(29)/VaultKV2Provider.cs(133,134)——原 SecretProvider.cs 内同代码既有警告，非新增 |

### 4. 引用点同步 — PASS

- AGENTS.md：结构树 L85 拆为 L85-88（四个 secret-provider 行）。
- docs/architecture.md：5 处 `implemented in `src/SecretProvider.cs`` 改为具体新模块路径。
- 前端门禁：3 文件重指向（见上），新增共享 helper `frontend/src/lib/secret-provider-source.ts`。
- docs/agents/hard-boundaries.md：无文件名级引用需改（其条款全部为类型名/行为级，类型名全部保留，本票未触碰任何条款语义）。
- docs/agents/reference-index.md / secret-architecture-blueprint.md：仅类型名引用（`ISecretProvider`），无文件名活引用。

### 5. codegraph sync — PASS

`codegraph sync .` → `Added: 14, Modified: 3, Removed: 1 — 240 nodes in 2.2s`（14 增 = 13 新模块 + helper；1 删 = SecretProvider.cs）。

## 红线遵循

- secrets 永不进注册表 / DPAPI 语义 / 8 provider 行为细节（hard-boundaries v0.7.x–v0.9.x secret 条款）：纯搬移未触碰任何方法体；canary-redaction 9/0 与 test-with-restore `secrets never in registry ... OK` 当场复证。
- 版本控制走 GitButler（WORKFLOW §4.2）：8 个提交全部 `but commit`，未用原生 git 写操作，未 push。
- 三层锁/互斥、rename write-verify-delete：未触碰（未修改任何命令域代码）。

## 偏离与风险

- 无计划偏离。检查点粒度与 handoff 完全一致（A→F 六点各提交一次）。
- 残留 `ISecretProvider.cs` 子串命中属文件名巧合，非旧文件引用；如大脑会话要求字面零命中，可再议改名（不建议：接口名是 hard-boundaries 多处引用的公共符号名）。
- CS8600×4 警告路径转移为搬移的自然结果（同代码同警告，仅文件名变化）；警告总数与基线持平。

## 工作区状态

GitButler 分支 `arch/09-secret-provider-split`：swq(锚) → zsu(A) → onr(B) → wox(C) → lut(D) → kwz(E) → oyn(F) → wmw(门禁+docs)。工作区 clean，未 push（按 §4.2）。
