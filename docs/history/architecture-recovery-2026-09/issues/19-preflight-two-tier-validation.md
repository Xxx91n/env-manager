# 19 — 预检验证两级降级（error/warn + --strict + 退出码 2）

**What to build:** profile 预检验证（ValidateProfiles / ProfileEffective pre-flight）按危险度分两档：数据破坏/半写状态类保持 error（32767 截断、变量名含 =、受保护变量、elevation 缺失）；「可疑但可安全执行」类（展开含未定义 %VAR%、路径条目陈旧、悬空 launch target）降 warn + 结构化报告；--strict 显式把 warn 升红；退出码 2=warn 契约 CLI/GUI/文档全链。

**Blocked by:** None — 可立即开工（研究 D 节给出精确落地边界）。

**Status:** ready-for-agent

- [ ] 悬空 launch target 不再硬阻断 profile 写（降 warn）；error 档四类仍拒绝
- [ ] 默认 warn 时写操作照常执行且退出码 2；--strict 下 warn 档拒绝且退出码 1
- [ ] warn 输出结构化（可解析、含被降级项清单）
- [ ] 退出码契约全链文档化：CLI 文档、GUI 对齐表、AGENTS.md 命令表、hard-boundaries.md 相关句
- [ ] 测试扩展 ProfileSeamValidationTests 式两级断言 + 退出码断言（CI 验证）
