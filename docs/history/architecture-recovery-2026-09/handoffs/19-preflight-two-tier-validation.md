# Handoff 19 — 预检验证两级降级（error/warn + --strict + 退出码 2）

## 目标

profile 预检验证分两档：数据破坏类保持 error，可疑可安全类降 warn + 结构化报告；--strict 升红；退出码 2=warn 全链文档化。

## 上游上下文

- 验收单：.scratch/architecture-recovery/issues/19-preflight-two-tier-validation.md
- 决策：.scratch/architecture-recovery/spec.md（Phase 4 段）+ research/round4-closeout-patterns.md（D 节）
- 红线：docs/agents/hard-boundaries.md（受保护变量、写路径、退出码纪律段）

## 现状勘察（开场用 rg/grep 盘点）

- ProfileEffective.cs 的 ValidateProfiles / pre-flight 清单与「悬空 launch target 硬阻断」现状；退出码 0/1 契约现文（CLI 文档 + GUI 对齐表 + AGENTS.md 命令表）。
- 两档边界：error 档四类（32767 截断、变量名含 =、受保护变量、elevation 缺失）；warn 档（展开含未定义 %VAR%、路径条目陈旧、悬空 launch target）。

## 检查点

A 定位校验清单与两档划分点 → B 实现 warn 档 + 结构化报告 → C --strict 与退出码 2 → D 文档全链同步 → E 测试扩展（ProfileSeamValidationTests 式两级断言 + 退出码断言）→ F 交大脑触发 CI。
验证纪律（CI-only）：同票 17 检查点的验证纪律句。

## 完成定义

issues/19 验收项全勾；报告落盘 .scratch/architecture-recovery/reports/19-preflight-two-tier-validation.md，每条验收附证据。
