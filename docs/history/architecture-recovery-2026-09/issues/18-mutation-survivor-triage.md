# 18 — 变异测试幸存者分诊 + 登记 + 模块化报告（CI 可跑）

**What to build:** 对当前 Stryker 基线（96 受测 / 78 kill / 14 survived / 4 timeout，40.00%）做结构化分诊：逐条把幸存者归类为 no coverage / weak assertion / equivalent，每条出判定 + 理由写入登记文件（含未来 LLM 检测预留字段）；非等价者补缺失断言测试（优先边界条件）；Stryker 补模块分算报告并可经 CI 执行；不追 100%。

**Blocked by:** None — 可立即开工（基线数字为大脑 2026-09-04 当场重跑值）。

**Status:** ready-for-agent

- [ ] 登记文件落盘，逐条含：位置、类别（no coverage / weak assertion / equivalent）、判定、理由、LLM 检测预留字段
- [ ] 非等价幸存者补测试后 Stryker 重跑：kill 数上升、survived 仅余登记为等价的条目
- [ ] 阈值 85/70/60 与 ignore string/logical 不变；无「为杀变异写防御性测试」式 100% 追求
- [ ] Stryker 可经 CI 执行（workflow_dispatch 短跑 job 或等效），输出含模块分算报告
- [ ] 趋势记录落盘：基线 vs 重跑数字可对比
