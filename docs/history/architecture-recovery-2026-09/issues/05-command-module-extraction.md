# 05 — Program.cs 按命令域拆模块（gh pkg/cmd 形态，保留 ArgTokenizer）

（2026-08-31 重建版：原文件随 .scratch 树外部清理丢失，由大脑会话按原始内容恢复）

**What to build:** profile、path、service、audit、agents、update 的实现从内联搬入各自命令模块，Program.cs 只剩薄 Main 分发；跨文件静态共享状态（DebugMode/JsonOpts/受保护集合）归并入对应模块；C# 源文件移入 src 风格子目录。行为零变化，全部测试与集成脚本仍是验收标准。

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Program.cs 缩减为薄入口（目标 <400 行）且 Main 只做分发
- [ ] 每个命令域一个文件，域内聚合其静态状态
- [ ] 迁移前后全部 dotnet test 与集成脚本绿灯
- [ ] 对外 CLI 命令帮助文本与退出码无变化
- [ ] 补 codegraph sync 并随提交更新

权威上下文：`D:\Aworker\env-manager\.scratch\architecture-recovery\spec.md`（实施/测试决策）、同目录 `architecture-review.html`（证据与优先级）。
