# Handoff 21 — CliRuntime 441 行拆出（纯搬迁，行为零变化）

## 目标

Program.cs 中 441 行 CliRuntime 类机械搬迁为独立文件，行为零变化，引用点同步。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/21-cliruntime-extraction.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）
- 红线：docs/agents/hard-boundaries.md；csproj 默认 glob 编译（移动免改项目文件）

## 现状勘察（开场用 rg/grep 盘点）

- Program.cs 中 CliRuntime 类起止行；类型引用面（rg 实测后再搬）；AGENTS.md 结构树与 docs 活指针中 Program.cs 相关句。

## 检查点

A 备份原文件到 OS temp 并 sanity-check 关键片段 → B 字符串切片搬运（保留原文 EOL，禁 split/join 重组——WORKFLOW §6 教训）→ C 交大脑触发 CI（dotnet 全绿 + CLI 快照 17 个不变）→ D 引用点同步 → E codegraph sync。
验证纪律（CI-only）：同票 17 检查点的验证纪律句。

## 完成定义

issues/21 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/21-cliruntime-extraction.md，每条验收附证据。
