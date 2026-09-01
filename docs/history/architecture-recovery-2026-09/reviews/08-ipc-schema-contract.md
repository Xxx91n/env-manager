# 大脑复验 — 票 08（IPC schema 契约）

> **重建版（2026-09-01）**：原件随 .scratch 树于票 05 提交期事故中二次丢失，由大脑会话按已验收结论重建摘要；非逐字原文。

## 结论：通过，票 08 收口（2026-08-31，经修复窗口复验）

- 首轮复核**不通过**：报告/AGENTS.md/architecture.md 声称 build.yml verify job 有 cargo test 步骤，仓库实测无——文档失实（比"未做"更糟：声称做了没做）。
- 修复窗口：build.yml 实补 2 个 `cargo test --locked` 步骤（service + src-tauri，YAML 解析 22 步验证）；Cargo.lock MM 并发残留字节级查清（索引同步修复）；lightweight.rs 并行 static flake 修复（STATE_LOCK，--test-threads=8 × 10 全绿）。修复提交 afa2ae3 + a19ebab 入栈。
- 复验四侧全绿：service 15、cargo test src-tauri 11、dotnet test 71、TS 契约 14。
- 过程违规呈报（未追认）：窗口曾 `git stash pop` 裸 git 写（违 §4.2），已恢复无数据丢失——呈报记录，教训：版本控制只走 GitButler。
