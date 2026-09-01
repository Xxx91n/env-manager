# Handoff 06 — EnvFeatures.cs 五域分家

（2026-09-01 再重建版：原件随 .scratch 树二次丢失，由大脑会话按 spec + 票 05 后实态重新生成。）

## 目标

src/EnvFeatures.cs 按域拆分、名字退役。目标模块（spec 决策）：audit/history、expand、bulk import-export、DPAPI helper。native-methods 已由票 05 完成（src/NativeMethods.cs），本票仅核对其无回流。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/06-envfeatures-domain-split.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Implementation Decisions、User Story 12）
- 红线：`docs/agents/hard-boundaries.md`（scuba/出口/注册表路径均不得变更）

## 现状勘察（票 05 后实态）

- src/EnvFeatures.cs 现存内容需窗口 rg 盘点（AuditEntry/BulkVariable/ProtectionDefaults/DpapiHelper 等类型 + audit/expand/bulk 逻辑）。
- 门禁测试现状：vitest 398/398 全绿；若有断言指向 EnvFeatures.cs 路径/符号，须随拆分同步。
- AGENTS.md 目前工作区有 4 个 parked hunk（票 05 src/ 结构行）——本票在 parked 区同域编辑时按 hunk 小区提交，勿强提。

## 本票检查点

1. 逐域拆分：每搬一域立即 `dotnet build` + `dotnet test` 全绿才继续下一域（票 05 同纪律）。
2. 改名/退役扫尾：`rg -n "EnvFeatures" src/ tests/` 应只剩历史文件头注释（若有）；EnvFeatures.cs 文件本身删除（git mv 逐域移出后空壳删除）。
3. 集成复跑：`pwsh -NoProfile -File scripts/run-ci-tests.ps1 -CliExe release/cli-only/env-manager-cli.exe`（跑前先 `dotnet build -c Release` 刷新 release/cli-only 4 产物——票 04 B1 教训：测试二进制必须新鲜）。
4. 前端门禁：`cd frontend && npx vitest run` 398/398 全绿。
5. codegraph sync。

## 完成定义

issues/06 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/06-envfeatures-domain-split.md`，每条验收附当场命令输出。
