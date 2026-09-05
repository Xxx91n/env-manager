# 20 — profile create --help 解析修复（help 不当 profile 名落库）

**What to build:** profile create 把 --help 及其变体识别为帮助请求而非 profile 名，与其它命令的 help 契约一致；回归测试钉住；不产生任何 profiles.json 写入。

**Blocked by:** None — 可立即开工（横切登记 backlog 立票）。

**Status:** ready-for-agent

- [x] profile create --help 输出帮助、退出码 0，profiles.json 无写入
- [x] profile create 不带名与其它非法调用路径错误行为不变
- [x] 回归测试钉住该行为（xUnit 或 CLI 快照层）
- [x] 新增用户可见字符串走 10 语言 i18n 同步
- [x] docs/cli-commands.md 与 AGENTS.md 快速参考同步（若行为描述变化）
