# 11 — 差分测试：以真实 Windows 环境语义为 oracle，钉住 InMemoryScope 忠实度

**What to build:** 一条差分测试夹具：同一操作序列分别跑 InMemoryScope（引擎内建测试替身）与 RegistryScope（真实注册表，经 test-with-restore 隔离），断言终态与广播次数逐条一致，把"InMemoryScope 忠实于 Windows"从假设变为被钉住的事实。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/11-differential-oracle-testing.md)

- [ ] 新增差分 oracle 夹具：同一操作脚本分别跑 InMemoryScope 与 RegistryScope，终态 + 广播次数逐条一致
- [ ] 语义矩阵覆盖：REG_EXPAND_SZ 保留 %VAR% 不预展开、PATH 值 1024~30000 字符边界、空条目=当前目录语义、变量名含 `=` 拒绝、system scope 写需 elevation
- [ ] dotnet test 全绿；差分套件在 CI windows-latest 上跑通（真实注册表隔离，不污染用户环境）
- [ ] 人为注入一处 InMemoryScope↔Windows 语义漂移（REG_SZ↔REG_EXPAND_SZ 保真回归），差分测试必须变红
- [ ] 报告落盘 `.scratch/architecture-recovery/reports/11-differential-oracle-testing.md`，每条验收附当场命令输出
