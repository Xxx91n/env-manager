# 22 — 集成测试残留卫生（补偿式清理 + 用户自清文档化）

**What to build:** 测试 harness 补偿式清理其写入的注册表值（EM_TEST_DST=v1 一例）：差分对账块保证运行后无新增残留，新增残留自检命令，文档化用户侧自清步骤。本票不执行用户机器上的实际删除。

**Blocked by:** None — 可立即开工（横切登记 backlog 立票）。

**Status:** ready-for-agent

- [x] test-with-restore 差分对账块补「残留归零」断言：运行后前后快照 diff 仅含登记值
- [x] 新增残留自检命令/脚本可列出 EM_TEST_* 类残留
- [x] 文档给出用户自清命令（注册表删除路径）与操作说明
- [x] docs/build-and-release.md 测试段同步
- [x] 本票不改动用户机器现状（用户侧操作，非泄漏）
