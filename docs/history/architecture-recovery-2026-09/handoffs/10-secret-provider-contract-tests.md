# Handoff 10 — SecretProvider 契约测试套件

## 目标

xUnit 工程内落地抽象契约基类 + harness 工厂缝；DPAPI 挂 L0 真实后端全绿；其余 provider 子类 Skip 挂载；合规闸门防覆盖腐烂。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/10-secret-provider-contract-tests.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 2 段）
- 业界范式：`.scratch/architecture-recovery/research/secret-provider-patterns.md`（五条证据线 + 目录样板 + 契约/harness/闸门代码形）
- 现有 fail-closed 钉住测试：`tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs`（评估迁入契约或保留原位，勿重复）

## 现状勘察（开场 rg 盘点）

- ISecretProvider 接口形状以代码为准（`rg -n "interface ISecretProvider"`）；SecretProviderManager.Decrypt fail-closed 路由当前由 ProfileSeamValidationTests 钉住。
- 本票只在测试工程内加代码，不动 src/ 生产实现（票 09 已拆完）。

## 检查点

A 抽象契约基类 + ISecretProviderHarness 接口 → B DPAPI 子类绿 → C 其余 7 子类 Skip 挂载 + 实现特有纯函数单测 → D 合规闸门反射测试 → E 全门绿 + 分层记录落 docs。

## 完成定义

issues/10 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/10-secret-provider-contract-tests.md`，每条验收附当场命令输出。
