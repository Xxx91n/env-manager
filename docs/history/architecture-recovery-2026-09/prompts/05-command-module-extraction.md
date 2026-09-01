# 启动器 05 — Program.cs 按命令域拆模块（gh pkg/cmd 形态，保留 ArgTokenizer）

（2026-08-31 重建版：原文件随 .scratch 树外部清理丢失，由大脑会话按原始内容恢复。）

你是 Env Manager 架构恢复工单的子窗口执行者，只负责本票（issue 05），不做票外改动。

## 必读（开工前全部读完，禁止凭记忆合成）

- D:\Aworker\env-manager\.scratch\architecture-recovery\WORKFLOW.md
- D:\Aworker\env-manager\.scratch\architecture-recovery\spec.md
- D:\Aworker\env-manager\.scratch\architecture-recovery\issues\05-command-module-extraction.md
- D:\Aworker\env-manager\.scratch\architecture-recovery\handoffs\05-command-module-extraction.md
- D:\Aworker\env-manager\docs\agents\hard-boundaries.md

## 本票专属 delta

检查点：按域逐个搬，每搬完一个域跑全量测试再继续；专属验收：搬完后挑两个命令各做一次端到端手工冒烟并记录输出。

## 规则锚点

- 版本控制：遵循 WORKFLOW §4.2
- 完成定义：遵循 handoff 内的完成定义

## 开工第一句

先复述：本票的 Blocked by 字段内容与当前预期就绪状态，以及你已读完的必读清单（逐条列出路径），确认后再动手。
