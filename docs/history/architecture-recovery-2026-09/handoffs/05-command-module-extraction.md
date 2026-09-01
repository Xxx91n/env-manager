# Handoff 05 — Program.cs 按命令域拆模块（gh pkg/cmd 形态，保留 ArgTokenizer）

（2026-08-31 重建版：原文件随 .scratch 树外部清理丢失，由大脑会话按原始内容恢复）

面向：承接本票的新会话（子窗口）。

## 背景（不重复已落盘内容，只指路）

- Spec 与全部决策：`D:\Aworker\env-manager\.scratch\architecture-recovery\spec.md`
- 本票验收项与阻塞：`D:\Aworker\env-manager\.scratch\architecture-recovery\issues\05-command-module-extraction.md`
- 巡检证据与推荐顺序：`D:\Aworker\env-manager\.scratch\architecture-recovery\architecture-review.html`
- 工作流与版本控制：`D:\Aworker\env-manager\.scratch\architecture-recovery\WORKFLOW.md`（版本控制唯一来源 = §4.2）
- 相关 ADR/规范：docs/agents/hard-boundaries.md（仓库内相对路径）

## 专属 delta

检查点：按域逐个搬，每搬完一个域跑全量测试再继续；专属验收：搬完后挑两个命令各做一次端到端手工冒烟并记录输出。

## 完成定义

1. issue 内全部验收项逐条达成并能给出证据（命令输出/测试绿色/构建通过）；
2. 相关测试绿灯：建立后的 `dotnet test` 全绿 + 仓库既有集成脚本（按其文档要求）通过；
3. 仓库要求的文档同步已做（AGENTS.md/docs 维护表），若触及 CLI 命令面需同步对应文档；
4. 提交遵循 WORKFLOW §4.2；回报时附：改动清单、证据、遗留风险。

## Suggested skills

`implement`（内驱 `tdd`）→ 收口前 `code-review`；模块形状拿不准时 `codebase-design`。
