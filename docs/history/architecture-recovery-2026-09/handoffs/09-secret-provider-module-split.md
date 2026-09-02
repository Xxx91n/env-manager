# Handoff 09 — SecretProvider.cs 按 provider 拆模块

## 目标

src/SecretProvider.cs 拆为一 provider 一文件 + 接口/信封/管理器归位；"SecretProvider.cs" 单文件退役（类型名保留）。行为零变化。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/09-secret-provider-module-split.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 2 段 Implementation/Testing Decisions）
- 红线：`docs/agents/hard-boundaries.md`（secrets 永不进注册表、DPAPI 加密语义等不得变更）

## 现状勘察（开场用 rg 盘点）

- src/SecretProvider.cs 约 1900 行；8 个 provider 类 + SecretEnvelope(L17) + 2 个 JsonSerializerContext(L81/L87) + SecretProviderManager(L1634，含 ProviderConfig)。
- 引用面（rg 实测）：src/ProfileCommand.cs、src/AuditCommand.cs 用类型名；AGENTS.md、docs/{architecture, agents/reference-index, agents/hard-boundaries, secret-architecture-blueprint}.md 有活指针；前端门禁 4 文件（secret-timeout-memory / secret-regression / v0.7-secrets / v0.7.2-secrets）可能 readFileSync 该路径。
- csproj 默认 glob 编译 src/ 下文件，移动免改项目文件。

## 检查点

A 信封 + JSON 上下文 + 接口 → B DPAPI + CredentialManager → C PowerShellSecretManagement → D Vault + sops → E AzureKeyVault + OnePassword + AwsSecretsManager → F 管理器归位 + 删源文件。每点 `dotnet build` + `dotnet test` 全绿才继续。
门禁测试引用：拆分前 `rg -n "SecretProvider" frontend/src` 列全量，随拆同步路径/断言。
集成复跑前先 `dotnet build -c Release` 刷新 release/cli-only 4 产物（票 04 B1 教训）。
codegraph sync。

## 完成定义

issues/09 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/09-secret-provider-module-split.md`，每条验收附当场命令输出。
