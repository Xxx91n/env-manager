# Handoff 25 — SharpFuzz 夜间模糊（参数解析面 + corpus 入库）

## 目标

SharpFuzz + libFuzzer harness 覆盖参数解析面，异常二分纪律；corpus 入库；PR 短跑 + 夜间长跑 workflow。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/25-sharpfuzz-lenientargs-nightly.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）+ research/round4-closeout-patterns.md（B 节）
- 依赖：票 18（分诊完成后再启动，避免重复投资）

## 现状勘察（开场用 rg/grep 盘点）

- ArgTokenizer.cs 与 dispatcher 的「不受信输入」面；SharpFuzz/libFuzzer 在 .NET/Windows 的成熟形态（调研 B 节）；.NET 10 发布产物 ReadyToRun 前提；仓库无夜间 workflow 先例（需新建）。

## 检查点

A 确认票 18 已完成 → B ReadyToRun 前提验证并记录结论 → C harness + 异常二分纪律（Format/Argument/Overflow 吞；NRE/IndexOutOfRange/OOM/StackOverflow/AV 当 crash）→ D 种子 corpus 入库 → E workflow（cron 夜间长跑 + PR 短跑 5–10min，不阻塞 PR）→ F 交大脑触发首次夜间跑取证据。
验证纪律（CI-only）：同票 17 检查点的验证纪律句；模糊跑只经 CI。

## 完成定义

issues/25 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/25-sharpfuzz-lenientargs-nightly.md，每条验收附证据。
