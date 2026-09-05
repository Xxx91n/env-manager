# 23 — architecture.md 补 canary/golden 段（文档）

**What to build:** architecture.md 增补 canary 零泄漏断言网与 golden/快照层的描述段：三 sink 扫描、<encrypted>/<revealed> 占位断言、17 个 CLI 快照与 IPC golden 的关系；文档指针同步。无代码行为变化。

**Blocked by:** None — 可立即开工（横切登记 backlog 立票）。

**Status:** ready-for-agent

- [ ] architecture.md 新增段覆盖 canary 网与 golden/快照层，内容与测试实物一致
- [ ] docs/agents/reference-index.md 指针同步（如适用）
- [ ] AGENTS.md 测试清单相关句同步（如适用）
- [ ] 无代码行为变化
- [ ] doc-sync 检查脚本绿（CI 验证）
