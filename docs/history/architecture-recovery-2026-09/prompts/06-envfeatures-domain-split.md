# 窗口启动器 — 票 06：EnvFeatures.cs 五域分家

你是 Env Manager 仓库（D:\Aworker\env-manager）中一张实施票的独立执行窗口，只对票 06 负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/handoffs/06-envfeatures-domain-split.md
- .scratch/architecture-recovery/issues/06-envfeatures-domain-split.md
- .scratch/architecture-recovery/spec.md
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md
- .scratch/architecture-recovery/reports/05-command-module-extraction.md（前票交付与遗留）

## 本票 delta

- Blocked by：05（已收口，reviews/05）。
- 检查点与完成定义：遵循 handoff 内的完成定义。
- 版本控制：遵循 WORKFLOW §4.2。特别注意票 03/05 教训：跨栈依赖一律 sibling 分支，禁 but move 线性化；重要未提交状态先做快照提交再动历史改写类操作。
- AGENTS.md 有 4 个票05 parked hunk 在工作区——若你在同域编辑，按 hunk 小区提交，勿整体强提、勿破坏 parked 内容。
- 测试二进制必须新鲜（票 04 B1 教训）：跑任何集成脚本前先 `dotnet build -c Release` 并把 4 产物刷进 release/cli-only。
- 交付报告落盘：.scratch/architecture-recovery/reports/06-envfeatures-domain-split.md，验收项逐条附当场命令输出（不接受凭记忆数字）。.scratch 完成里程碑时同步备份关键文件到 OS temp（票 05 事故教训）。

开工第一句：先复述本票的 Blocked by（05）是否已收口 + 上面必读清单的标题，确认无阻塞后再动手。
