# 21 — CliRuntime 441 行拆出（纯搬迁，行为零变化）

**What to build:** Program.cs 中 441 行 CliRuntime 类机械搬迁为独立文件，类型名保留，行为零变化；引用点同步；现有套件回归。不引入新 seam / 新抽象。

**Blocked by:** None — 可立即开工（横切登记 backlog 立票）。

**Status:** done (CI-verified via PR #41, verify pass 10m9s)

- [x] CliRuntime 独立成文件；Program.cs 回到 thin Main 派发
- [x] 行为零变化：dotnet 测试全绿（CI 验证 127/0/24 + vitest 430/430）、CLI 快照 17 个不变（0 .received 差异）
- [x] 引用点同步：AGENTS.md 结构树、docs 活指针、hard-boundaries.md 如涉及
- [x] 纯搬迁，无行为/接口变化
- [x] codegraph sync
