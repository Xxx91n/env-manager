# Handoff 17 — CI verify 内嵌 CLI 资源 staging（main 推送每次变绿）

## 目标

verify job 在 cargo 测试前把 CLI 五件套 staging 到 Tauri 资源目录，main push 的 CI/CD Build and Release 全绿。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/17-ci-verify-embedded-cli-staging.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）+ research/round4-closeout-patterns.md（A 节）
- 红线：docs/agents/hard-boundaries.md（构建规则段）

## 现状勘察（开场用 rg/grep 盘点）

- 失败日志锚点：gh run 33880367303 verify → Run Tauri crate tests → 「resource path bin/env-manager-cli.exe doesn't exist」。
- tauri.conf.json bundle.resources 五文件清单；build.yml verify job 中 cargo test 在 CLI 构建之后但无 staging 步骤；CLI 发布产物当前落在仓库根 bin/Release/net10.0-windows/。
- 本地 build.mjs 已负责搬运（职责不动，本票只补 CI 前置）。

## 检查点

A 定位 build.yml 插入点（CLI 构建步骤之后、cargo 测试步骤之前）→ B 写 staging step（五文件逐一，缺一 fail-closed）→ C 交大脑推 CI 验证分支触发 workflow 取绿 → D 报告附 gh run 证据。
验证纪律（CI-only，用户 2026-09-04 令）：窗口完成代码后回报大脑，由大脑推 CI 验证分支；窗口不本地跑构建/测试/lint、不自行推送（§4.2）。

## 完成定义

issues/17 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/17-ci-verify-embedded-cli-staging.md，每条验收附当场 CI run 证据。
