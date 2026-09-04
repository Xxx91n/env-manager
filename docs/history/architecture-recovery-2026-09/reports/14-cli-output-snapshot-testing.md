# Report — 票 14：CLI 输出快照化（Verify）

- 分支：`arch/14-cli-output-snapshot-testing`（GitButler，独立分支，基点 fb9c065）
- 日期：2026-09-03
- 状态：实施完成，待大脑会话按 §4.4 验收

## 实施范围

1. **Verify.Xunit 31.12.5** 引入 `tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`（文件末尾独立 `<ItemGroup>`，与票 12 的 CsCheck 引用行物理隔离，避免交错 hunk）。
2. **最小 seam**（行为零变化）：
   - `src/Program.cs`：`ShowHelp()` 提取为 `internal static string BuildHelpText()`；`ShowHelp()` 仍为 `Console.WriteLine(BuildHelpText()); return 0;`（原输出/返回码不变）。
   - `src/ProfileCommand.cs`：`RunProfileCommand` `static` → `internal static`（其他不变）。
   - `src/AuditCommand.cs`：`AuditFilePath` 增加 `_auditFilePathForTests` 重定向 + `SetAuditFilePathForTests`（默认路径不变）。
   - `src/AuditCrypto.cs`：`AuditKeyPath` 同型重定向 + `SetAuditKeyPathForTests`。
3. **`CliOutputSnapshotTests.cs`**（17 快照，串行 Collection，Console.Out/Error 捕获+恢复）：
   - help：主 help 全文、Unknown command 错误前缀+help 组合。
   - stdout：rename 成功、toggle disable/restore JSON 投影。
   - 错误文案：rename 源缺失/目标冲突/受保护源、set 受保护/含=、change-scope 已在目标 scope 警告、toggle/delete 受保护、profile show 未知 profile、reveal-secret 解密失败（scrub 后）、generic exception scrub 后文案、UnauthorizedAccess 固定文案。
   - canary 契约：profile show 以 `<encrypted>` 遮蔽 secret 值（正断言 + ciphertext 不出现负断言）。
4. **scrubber**：`Scrub()` 只规范化四类易变字段 —— 程序集版本行（`v0.9.30`→`v<version>`）、profile GUID（`<guid>`）、32 位 hex 审计 id（`<audit-id>`）、RFC3339 时间戳（`<timestamp>`）；并还原 JSON 的 `\u003C/\u003E/\u0026` 转义使 `<encrypted>` 等标记以字面量进入快照。`Scrubber_GuidTimestampAndVersion_AreNormalized` 自检锁定：占位符出现、原始易变值消失、`<encrypted>/<revealed>/<redacted>` 原样保留、普通错误文案不被吞。
5. **i18n（`frontend/src/lib/translations.test.ts` 重写强化）**：
   - 结构完整性（硬断言）：递归 flatten 全部叶子 key（439 个，替代原 426 顶层 key 比较），缺失/多余 key、空值/非 string、ICU placeholder 集合与 en 逐 key 一致（13 个占位符名清单全量固化）。
   - ICU 渲染：经 `intl-messageformat`（svelte-i18n 同源引擎）对每个 locale 全键渲染一次（渲染失败即红，锁定 ICU 单引号转义不回归——快照渲染从不把 `{placeholder}` 包进单引号）。
   - 每 locale 全键渲染快照：10 locale 各一份 Vitest snapshot（`__snapshots__/translations.test.ts.snap`，236 KB，稳定排序），任何语言文案漂移在 diff 中显式出现。
6. **CI**：未改 `build.yml` —— 现有 verify job 的 `dotnet test ... -c Release` 与 frontend `npx vitest run` 自动发现新测试；`.verified.txt` 与 `.snap` 均已入库，CI 只验证不更新。

## 验收项逐条核验（当场命令输出）

### [x] 引入 Verify.Xunit；对 help 文本、各命令 stdout、错误文案、canary 输出（<encrypted>/<revealed>）建立快照

```
$ dotnet build tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Debug --nologo -v q
→ BUILD OK (no error lines)   [error 行数 = 0]
$ ls tests/EnvManager.Engine.Tests/*.verified.txt | wc -l
17
（help 2 + stdout 2 + 错误文案 12 + canary <encrypted> 1；<revealed> 的审计正路径由既有 canary-redaction.Tests.ps1 Tier3 覆盖，
 单测 lane 钉住 reveal-secret 失败文案 + scrubber 自检中的 <revealed> 存活断言——快照目的为"人读契约"层，与 IPC golden 互补）
```

### [x] scrubber 清除 PID/时间戳等易变字段；任何 user-facing 文案改动在 diff 审阅里显式出现

```
$ dotnet test ... --filter FullyQualifiedName~CliOutputSnapshotTests（首跑）
失败: 17（Verify 无 verified 文件，正常首跑路径），通过 1 = Scrubber_GuidTimestampAndVersion_AreNormalized
$ 17 份 received 逐份审阅：无 RAW VERSION / RAW 32-HEX / RAW TIMESTAMP / JSON-ESCAPED MARKER（all clean）
$ dotnet test ... -c Release --nologo（accept 后二跑）
已通过! - 失败:     0，通过:   125，已跳过:    25，总计:   150   EnvManager.Engine.Tests.dll (net10.0)
$ 第二次完整复跑：leftover received (drift) = 0
注：当前 CLI 输出面无 PID 字段（temp 文件名含 PID 但不进 stdout）；scrubber 覆盖版本/GUID/审计id/时间戳并在自检中锁定。
```

### [x] i18n：每 locale 全键渲染快照（强化现有 translations.test）

```
$ npx vitest run（frontend/）
 Snapshots  10 written
 Test Files  40 passed (40)
      Tests  430 passed (430)
$ npx vitest run（CI=1 复跑，验证快照匹配不更新）
 Test Files  40 passed (40)
      Tests  430 passed (430)   [exit 0]
$ __snapshots__/translations.test.ts.snap：236,899 bytes，10 个 exports，无 "undefined" 泄漏
```

### [x] dotnet test / vitest 全绿；快照进 CI

```
$ dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj -c Release --nologo
已通过! - 失败: 0，通过: 125，已跳过: 25，总计: 150
$ npx vitest run（CI=1）
Test Files 40 passed (40); Tests 430 passed (430)
CI 无需改 build.yml：快照文件随票提交即被现有步骤验证（快照更新只允许本地显式生成并审阅）。
```

### [x] 报告落盘（本文件）；仓库门禁

```
$ dotnet build -c Release --nologo -v q  → 0 警告 / 0 错误（4.05s）
$ git diff --check → exit 0
$ codegraph sync . → Synced 6 changed files (Added 1, Modified 5, 204 nodes)
$ node scripts/build.mjs --arch x64 --skip-gui --skip-msi → exit 0；
  release/cli-only/env-manager-cli.exe、Env-Manager_cli-only_0.9.30_x64.zip、
  release/portable/env-manager.exe、Env-Manager_portable_0.9.30_x64.zip 均在（GUI/MSI 阶段跳过——本票未触及 Rust/WiX 面）
```

## 快照清单（17 .verified.txt，tests/EnvManager.Engine.Tests/）

| 类别 | 快照 |
|---|---|
| help | Help_MainHelpText_IsStable、Help_UnknownCommand_ErrorCopyPlusHelp_IsStable |
| stdout | Rename_Success_StdoutIsStable、Toggle_DisableRestore_InfoCopyIsStable |
| 错误文案 | Rename_SourceMissing / Rename_TargetExistsWithoutOverwrite / Rename_ProtectedSource / Set_ProtectedVariable / Set_NameWithEquals / ChangeScope_AlreadyInTargetScope / Toggle_ProtectedVariable / Delete_ProtectedVariable / ProfileShow_UnknownProfile / ProfileRevealSecret_FailureCopy / Error_GenericExceptionCopy / Error_UnauthorizedAccessCopy |
| canary | ProfileShow_MasksSecretValueAsEncrypted（+scrubber 自检断言 <encrypted>/<revealed>/<redacted> 存活） |

## 已知边界（诚实声明）

- `Main` 是 private 且初始化 crash-dialog/mutex/registry 快照机制，未知命令测试以稳定 seam 组合（stderr 前缀 + BuildHelpText）钉住同一人读契约，未直接跑 `Main`。
- list/get 读真实注册表（尚无 seam），其 stdout 快照不在 hermetic 单测 lane 内——与既有 Pester Tier3 集成测试互补，本票不扩 seam。
- reveal-secret 的 `<revealed>` 审计正路径需要真实 DPAPI 后端，Tier3 Pester（canary-redaction.Tests.ps1）覆盖；单测 lane 钉其失败文案与 scrubber 存活断言。
- intl-messageformat 经 `intl-messageformat/intl-messageformat.esm.js` 单文件 ESM bundle 引入（lib/ 入口是无扩展名 ESM import，Vite SSR resolver 拒绝），运行时行为同源。
- build.mjs 的 GUI/MSI 阶段本次跳过（本票未触及 Rust/WiX/前端构建产物面）；CI package job 会完整跑。

## 并行票隔离声明

- 本票分支只含：csproj 的 Verify.Xunit ItemGroup、4 个 src seam 文件、CliOutputSnapshotTests.cs + 17 verified 快照、translations.test.ts + translations.test.ts.snap。
- 票 11（DifferentialOracleTests.cs、test-with-restore.ps1）与票 12（WritePathStateMachineTests.cs、csproj 的 CsCheck 行）的未提交改动保持原样，未纳入本票提交。
