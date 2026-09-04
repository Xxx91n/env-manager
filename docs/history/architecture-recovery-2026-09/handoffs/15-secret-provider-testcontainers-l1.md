# Handoff 15 — Testcontainers L1 矩阵

## 目标

用 Testcontainers 钉扎本地模拟器，把票 10 契约套件里 7 个外部 provider 的 backend-dependent 断言（round-trip/plaintext-never）从 Skip 转为真跑，无云凭据即闭环"外部 secret provider 注入生效"的端到端验证。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/15-secret-provider-testcontainers-l1.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段 Testcontainers 决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（T7）+ `.scratch/architecture-recovery/research/secret-provider-patterns.md`（L0/L1/L2 分层）
- 红线：`docs/agents/hard-boundaries.md`（secrets 不进注册表、fail-closed）

## 现状勘察（开场用 rg 盘点）

- `tests/EnvManager.Engine.Tests/SecretProviderContractTests.cs`：抽象契约基类。
- 7 个 Skip 挂载：`tests/EnvManager.Engine.Tests/*ContractTests.cs`（Vault/AWS/Azure/1Password/sops/PowerShellSecretManagement/CredentialManager）。
- `ISecretProviderHarness.cs`：harness 缝。
- 镜像（调研已核验）：Azurite 3.24.0 / LocalStack 2.0 / Lowkey Vault 官方模块；Vault dev server 通用容器（无官方模块）。

## 检查点

A Docker 可用性验证（Windows/Linux CI runner，本票唯一未就地验证假设）→ B 镜像钉扎 → C 挂载 Skip→真（每 provider 一条冒烟）→ D 无凭据全绿 + CI。

## 完成定义

issues/15 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/15-secret-provider-testcontainers-l1.md`，每条验收附当场命令输出。
