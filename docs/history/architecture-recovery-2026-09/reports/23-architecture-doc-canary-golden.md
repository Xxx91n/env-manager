# Report — 票 23：architecture.md 补 canary/golden 段（文档）

- 分支：`arch/23-architecture-doc-canary-golden`（GitButler 独立分支，commit `5bdf0aa`，未 push，按 WORKFLOW §4.2）
- 日期：2026-09-05
- 状态：实施完成，待大脑会话按 §4.4 验收
- 阻塞：无（开工时复述 Blocked by = None，必读清单 5 项读全）

## 实施范围（无代码行为变化）

1. **docs/architecture.md**（+31 行）：在 "Secret Provider Contract Test Suite (L0/L1/L2 layering)" 段之后、"Profile Drag Reorder (Pointer Events)" 标题之前新增 `## Canary Zero-Leak Assertion Net and Golden/Snapshot Layers`（architecture.md:381）。内容全部对照测试实物撰写，零臆造：
   - canary 零泄漏断言网（issue 07）：launch-env-injection 三层（golden env diff / probe echo / Launch-never-writes-registry）+ canary-redaction 七 sink 表（S1 show / S2 preview / S3 list / S4 history list 审计 trail / S5 launch stdout / S6 子进程 env dump=正控必须含 canary / S7 强制错误 stderr）、`profile reveal-secret` 唯一明文 stdout 路径不作为 sink、`<encrypted>`（JSON 转义形 `\u003Cencrypted\u003E`）与 `<revealed>` 占位正断言、`CanaryRedactionTests` scrub 纯函数回归 + 无模式 canary 直通（ADR 0005 best-effort）。
   - golden/快照层（issue 08 + 14）：IPC schema golden 两文件（机器契约，指针回指 "IPC Schema Contract" 段）；`CliOutputSnapshotTests`（Verify.Xunit 31.12.5）17 份源控 `.verified.txt` 的构成（help 2 + stdout 2 + 错误文案 12 + canary `<encrypted>` 1）、`Scrub()` 四类易变字段规范化 + `\u003C/\u003E/\u0026` 还原、scrubber 自检、串行 Collection；`<revealed>` 审计正路径归 Pester 实物而非快照；10 locale `intl-messageformat` 全键渲染 i18n 快照。
2. **docs/agents/reference-index.md**：architecture.md 行的主题描述追加 "canary zero-leak assertion net + golden/snapshot layers"。
3. **AGENTS.md**（测试清单 run-ci-tests 句尾，AGENTS.md:155）：追加一句指向 docs/architecture.md 新段标题（快照层此前在 AGENTS.md 无任何句子，属验收项「如适用」情形——issue 14 未在 AGENTS.md 登记，本句补上发现路径）。

## 明确不做

- 未动 architecture.md 既有段落错位（issue 15 内容现悬挂在 "Profile Drag Reorder" 标题下）——属他票/既有问题，避免本票 diff 语义扩散。
- 未改任何代码、测试、CI 配置；无新增用户可见字符串，无 i18n 键变更。

## 验收项逐条核验（当场命令输出）

### [x] architecture.md 新增段覆盖 canary 网与 golden/快照层，内容与测试实物一致

实为先、文档后（先勘察后写段，非事后对齐）：

```
$ ls tests/EnvManager.Engine.Tests/*.verified.txt | wc -l  → 17
$ 构成核对：CliOutputSnapshotTests*.verified.txt 文件名清单 → help×2、Rename_Success/Toggle_DisableRestore stdout×2、
  错误文案×12、ProfileShow_MasksSecretValueAsEncrypted×1（与段中 "2+2+12+1" 一致）
$ grep -n "Verify.Xunit" tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj
  → <PackageReference Include="Verify.Xunit" Version="31.12.5" />（与段中版本一致）
$ tests/canary-redaction.Tests.ps1 S1–S7 sink 清单与段中表格逐项一致（S6 正控、reveal-secret 不扫描、
  \u003Cencrypted\u003E / u003Crevealed 断言形一致）
$ ls frontend/src/lib/__snapshots__/translations.test.ts.snap → 存在，10 locale 全键导出键名核对（ar/de/en/es/fr/ja/ko/pt/ru/zh）
$ ls docs/schemas/ → env-manager-service-ipc.schema.json + ipc-samples.json
$ 插入完整性探针：
$ git show arch/23-architecture-doc-canary-golden:docs/architecture.md | grep -c "Canary Zero-Leak...\|S6\|CliOutputSnapshotTests\|verified.txt" → 3（段内锚点齐全）
$ wc -l docs/architecture.md → 439（原 408 + 31，Profile Drag Reorder 段 408→412 完整后移，无吞段）
```

### [x] docs/agents/reference-index.md 指针同步（如适用）

```
$ git show arch/23-architecture-doc-canary-golden --stat
  AGENTS.md 2 +- / docs/agents/reference-index.md 2 +- / docs/architecture.md 31 +
  提交内容探针：AGENTS.md 新句锚点 grep 计数 = 1
```

### [x] AGENTS.md 测试清单相关句同步（如适用）

同上提交探针（AGENTS.md:155 句尾追加指针，提及 7-sink / `<encrypted>`/`<revealed>` / 17 快照 / 10 locale / IPC golden 要素）。

### [x] 无代码行为变化

```
$ git show --stat arch/23-architecture-doc-canary-golden → 3 files changed, 33 insertions(+), 2 deletions(-)，
  全部为 .md；无 src/、tests/ 逻辑、frontend/ 代码、workflows 改动
```

### [x] doc-sync 检查脚本绿（CI 验证）

本窗口按 CI-only 政策不跑构建/测试；doc-sync 是纯文档路径检查（AGENTS.md 引用路径存在性），本地预验等价于 CI 步骤：

```
$ pwsh -NoProfile -File scripts/check-doc-sync.ps1
WARNING: README.md does not contain version '0.9.30' (may be using different version display)
=== Doc sync check PASSED ===
exit=0
（WARNING 为既有基线噪音，与本票无关；最终绿以 CI verify job 的 doc-sync step 为准，由大脑会话触发）
```

## 交付路径

- 分支：`arch/23-architecture-doc-canary-golden`，commit `5bdf0aa`（uno），未 push。
- 未提交残留（非本票）：`.zcode/plans/plan-sess_*.md`、`tests/EnvManager.Engine.Tests/ProfileCreateHelpTests.cs`（票 20 工作）、`scripts/test-with-restore.ps1` / `docs/build-and-release.md`（工作区另有他票改动，未纳入本提交）。
- 版本控制全程走 GitButler `but`，未用原生 git 写操作（上列 `git show` 为只读探针）。

---

## 收口修正（2026-09-05 大脑）

- 本文档写作时含「待 CI」表述；PR #45（head=完整 11 提交栈）已全绿（run 33963823146：verify/verify-l1/verify-arch×2/package 全 success + Fuzz/Workflow Lint/Dependency Review/Lint PR Title 全绿），本票终态 = ✅ done（README 已登记）。
