# Report 10 — SecretProvider 契约测试套件（抽象基类 + harness 缝 + 合规闸门）

日期：2026-09-03 · 分支：GitButler `arch/10-secret-provider-contract-tests`（叠于 `arch/09-secret-provider-split`，未 push，按 WORKFLOW §4.2）· 状态：子窗口自验全绿，待大脑会话按 issue 10 验收项核验。

## 阻塞状态复述

Blocked by 09：09 的实现已收口落地——`src/SecretProvider.cs` 单文件已删除，13 符号一符号一文件（`ISecretProvider.cs` / `SecretEnvelope.cs` / `SecretProviderManager.cs` / 8 个 provider 各一文件），report 09 记录 dotnet test 86/86 绿；GitButler 活动应用分支为 `arch/09-secret-provider-split`。但正式验收文档 `reviews/09-secret-provider-module-split.md` 尚未落盘（issue 09 状态为 "done (pending brain-session acceptance)"）。本票为纯测试工程改动（不触碰 src/），且依赖的 8 个 provider 类型均已就位并可编译，故无技术阻塞，按用户目标开工并完成；reviews/09 缺口已在此显式标出，交大脑会话一并核验。

## 必读清单（已读完）

handoffs/10、issues/10、spec.md（Phase 2 段）、WORKFLOW.md（§4.2）、research/secret-provider-patterns.md、tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs。

## 实施方式

纯测试工程加码（不动 src/ 生产实现，票 09 已拆完）：抽象契约基类 + 中立 harness 缝（CreateProvider / SeedSecret 中性写 / ReadRawSecret 中性读，防读写对称 bug 互相抵消，对齐 research 的 drivertest/WopiHost 范式）+ 每 provider 一个 sealed 挂载 + 反射合规闸门（EF ComplianceTest 式）。文件一律 node `fs.writeFileSync` 写入（LF、无 BOM、与 .gitattributes 一致）。

## 新增文件（tests/EnvManager.Engine.Tests/，11 个）

- `ISecretProviderHarness.cs` — harness 接口 + `SkippedProviderHarness`
- `SecretProviderContractTests.cs` — 抽象契约基类（四项核心断言，只经 harness 表达）
- `DpapiCurrentUserContractTests.cs` — DPAPI 挂载（L0 真实后端，全绿）
- `CredentialManagerContractTests.cs` / `PowerShellSecretManagementContractTests.cs` / `VaultKV2ContractTests.cs` / `SopsContractTests.cs` / `AzureKeyVaultContractTests.cs` / `OnePasswordContractTests.cs` / `AwsSecretsManagerContractTests.cs` — 7 个 Skip 挂载（带理由）
- `SecretProviderContractComplianceTests.cs` — 反射合规闸门

## 验收项逐条核验（当场命令输出）

### 1. 抽象契约基类含核心行为断言集，只经 harness 表达 — PASS

`SecretProviderContractTests` 四项断言（`FailClosed_DecryptRejectsForeignProviderEnvelope` / `MalformedFormat_DecryptRejectsNonEnvelopeGarbage` / `AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext` / `AssertPlaintextNotEmbedded_EncryptOmitsPlaintext`），全部只通过 `ISecretProviderHarness`（CreateProvider / SeedSecret 中性写 / ReadRawSecret 中性读）表达；往返断言额外做「中性布数据→SUT 解密」与「SUT 加密→中性读」两个跨向校验，防读写对称 bug。fail-closed 解密对应 provider 级 foreign-envelope 拒绝；SecretProviderManager 级 fail-closed（未知 provider / 非信封垃圾）保留在 ProfileSeamValidationTests 原位（handoff「评估迁入或保留原位，勿重复」→ 选保留，未重复）。

### 2. DPAPI 契约子类全绿（L0 真实后端）— PASS

`DpapiCurrentUserContractTests` 四项全部通过，往返与明文不落信封在真实 crypt32 CurrentUser 后端上执行：

```
已通过 EnvManager.Engine.Tests.DpapiCurrentUserContractTests.FailClosed_DecryptRejectsForeignProviderEnvelope [3 ms]
已通过 EnvManager.Engine.Tests.DpapiCurrentUserContractTests.MalformedFormat_DecryptRejectsNonEnvelopeGarbage [< 1 ms]
已通过 EnvManager.Engine.Tests.DpapiCurrentUserContractTests.PlaintextNotEmbedded_EncryptOmitsPlaintext [28 ms]
已通过 EnvManager.Engine.Tests.DpapiCurrentUserContractTests.RoundTrip_EncryptThenDecrypt_ReturnsPlaintext [7 ms]
```

### 3. 其余 7 provider 各有契约子类（Skip 带理由）— PASS

7 个挂载各带 `[Fact(Skip = "<reason>")]`，reason 标注 L1/L2 层级；backend-independent 断言（fail-closed、格式错误）仍对每个 provider 真实执行。抽样当场输出：

```
已跳过 EnvManager.Engine.Tests.VaultKV2ContractTests.RoundTrip_EncryptThenDecrypt_ReturnsPlaintext [1 ms]
已通过 EnvManager.Engine.Tests.VaultKV2ContractTests.FailClosed_DecryptRejectsForeignProviderEnvelope [12 ms]
已通过 EnvManager.Engine.Tests.SopsContractTests.MalformedFormat_DecryptRejectsNonEnvelopeGarbage [< 1 ms]
已跳过 EnvManager.Engine.Tests.AwsSecretsManagerContractTests.RoundTrip_EncryptThenDecrypt_ReturnsPlaintext [1 ms]
已通过 EnvManager.Engine.Tests.AwsSecretsManagerContractTests.FailClosed_DecryptRejectsForeignProviderEnvelope [< 1 ms]
```

### 4. 合规闸门测试 — PASS

反射断言「每个 `ISecretProvider` 实现恰好映射一个契约挂载」+「每个挂载映射到真实实现」：

```
已通过 EnvManager.Engine.Tests.SecretProviderContractComplianceTests.EveryProviderImplementation_HasExactlyOneContractMount [3 ms]
已通过 EnvManager.Engine.Tests.SecretProviderContractComplianceTests.EveryContractMount_MapsToARealImplementation [32 ms]
```

### 5. 全部测试门绿 + L0/L1/L2 分层记录入 docs — PASS

`dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`：

```
已通过! - 失败:     0，通过:   106，已跳过:    14，总计:   120，持续时间: 361 ms - EnvManager.Engine.Tests.dll (net10.0)
```

（基线 86 → 现 120：+34 契约/闸门用例，其中 14 为 Skip 挂载的 backend-dependent 断言。）

L0/L1/L2 分层已落盘 `docs/architecture.md` 新节「Secret Provider Contract Test Suite (L0/L1/L2 layering)」+ AGENTS.md 测试清单补充（issue 10 段）。

## 红线遵循

- 本票零 src/ 生产改动（handoff 约束）；secrets 永进注册表 / DPAPI 语义等 hard-boundaries 未触碰。
- 版本控制走 GitButler（WORKFLOW §4.2），见下。
- 未 push、未建 PR。

## 工作区状态

GitButler 分支 `arch/10-secret-provider-contract-tests`（`--above arch/09-secret-provider-split`）：tests 提交 + docs 提交。工作区 clean（.scratch 报告为 gitignored 产物，不纳入提交）。
