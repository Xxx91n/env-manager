# Handoff 14 — CLI 输出快照化（Verify）

## 目标

用 Verify.Xunit 把 CLI 的 help 文本、各命令 stdout、错误文案、canary 脱敏输出快照锁定，scrubber 清易变字段，i18n 每 locale 全键渲染快照——形成与 IPC golden 互补的"人读契约"层。

## 上游上下文

- 验收单：`.scratch/architecture-recovery/issues/14-cli-output-snapshot-testing.md`
- 决策：`.scratch/architecture-recovery/spec.md`（Phase 3 段快照测试决策）
- 调研依据：`.scratch/architecture-recovery/research/next-wave-patterns.md`（A5）
- i18n：`frontend/src/lib/translations/*.json`（10 语言，ICU 单引号转义）

## 现状勘察（开场用 rg 盘点）

- `src/Program.cs`：ShowHelp() help 文本与错误文案。
- `tests/EnvManager.Engine.Tests/CanaryRedactionTests.cs`：canary 输出格式（<encrypted>/<revealed>）既有断言。
- 前端 `frontend/src/lib/translations/` + 现有 translations.test：i18n 全键渲染的强化起点。
- Verify（前 VerifyTests）比 ApprovalTests 更现代：单测多文件、内置 scrubber、async（调研已核验）。

## 检查点

A Verify.Xunit 引入 → B help/stdout/错误/canary 快照建立 → C scrubber（PID/时间戳）→ D i18n 每 locale 全键渲染快照 → E CI（dotnet test + vitest）。

## 完成定义

issues/14 验收项全勾；报告落盘 `.scratch/architecture-recovery/reports/14-cli-output-snapshot-testing.md`，每条验收附当场命令输出。
