# 06 — EnvFeatures.cs 五域分家并退役该名字

（2026-09-01 再重建版：原文件随 .scratch 树于票 05 提交期事故二次丢失，由大脑会话按 spec.md + 票 05 后仓库实态重新生成；语义以 spec.md「Implementation Decisions」与「User Story 12」为准。）

**What to build:** 把 src/EnvFeatures.cs 按域拆分为命名模块并让 "EnvFeatures" 这个名字退役。按 spec 决策拆为：audit/history、expand、bulk import-export、DPAPI helper、native-methods。票 05 已完成 native-methods（src/NativeMethods.cs）与主体搬迁，本票承接其余四域 + 退役收尾。行为零变化，全部测试与集成脚本仍是验收标准。

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] EnvFeatures.cs 按域拆分为独立命名模块（audit/history、expand、bulk、dpapi；native-methods 已由票 05 完成，核对无遗留）
- [ ] "EnvFeatures" 名字代码内退役（类型/文件名清零，rg 验证）
- [ ] 全部 dotnet test 与集成脚本绿灯（86/86 + run-ci-tests 四套件）
- [ ] 前端门禁测试引用路径同步（若有断言指向 EnvFeatures.cs）
- [ ] 文档同步：AGENTS.md 结构树、docs/agents/reference-index.md、相关 ADR 引用
- [ ] codegraph sync 并随提交

权威上下文：`D:\Aworker\env-manager\.scratch\architecture-recovery\spec.md`（Implementation Decisions + User Story 12）。
