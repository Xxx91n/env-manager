# 架构恢复工作流（architecture-recovery）

> 版本控制唯一权威 = §4.2；窗口启动器只准引用本节编号。

## §1 总览

主流程（$ask-matt）：`improve-codebase-architecture（巡检）→ to-spec → to-tickets → implement（内驱 tdd，收口 code-review）`。
本地约束：大脑会话只编排与验收；实现由人工派发的新窗口（子窗口）按启动器逐票执行；调研统一走 atomcode；版本控制统一走 GitButler（§4.2）。

## §2 阶段流程（环节 → 入口技能 → 产出物路径）

| 环节 | 入口技能 | 产出物 |
|------|----------|--------|
| 巡检 | improve-codebase-architecture | architecture-review.html |
| 定规格 | to-spec | spec.md |
| 拆票 | to-tickets | issues/NN-slug.md + handoffs/NN-slug.md + prompts/NN-slug.md |
| 实现 | implement（内驱 tdd） | 代码 + 测试 + reports/NN-slug.md |
| 验收 | 大脑会话对照 issue 验收项 | README.md 波次表状态更新 |

## §3 产物与路径约定

一切多窗口引用的文件必须落盘：`.scratch/architecture-recovery/` 下 spec.md、issues/、handoffs/、prompts/、reports/NN-slug.md。对话文本只是副本；完成定义第 4 条「回报」按本节解释为落盘文件。

## §4 执行协议

### §4.1 大脑/子窗口双轨职责

大脑会话：编排、派发、按 issue 验收项逐条核验（要求命令输出/测试绿色等证据）、维护 README 波次表票状态、合并分支。
子窗口：按启动器读必读清单，只做本票改动，按 §4.2 提交，按 §3 落盘交付报告。

### §4.2 版本控制（唯一权威条款；启动器只准引用本节编号）

- 全部版本控制操作走 GitButler `but` CLI，不使用原生 git 写操作；技能参考 `$but`。
- 每票在独立 GitButler 分支上工作；只提交本票改动；不 push、不建 PR（除非用户明确要求）。
- 不移动/修改其他 agent 的并行工作；小修正 amend 进所属未推送提交。
- 提交信息遵守仓库 conventional-commit 规范（CI 有 PR title 校验）。
- 代码变更后按 AGENTS.md 运行构建验证；C# 引擎测试走 `dotnet test`（票 02 落地后），GUI 走 Vitest。

### §4.3 窗口启动器规范（硬规则）

1. 每份 ≤60 行，只含：一行身份、必读文件路径清单（handoff/issue/spec/WORKFLOW/相关 ADR）、本票专属 delta（检查点、专属验收项）、开工第一句（先复述阻塞状态 + 必读清单再动手）。
2. 禁止复述被引用文件的任何已有条款；版本控制只写"遵循 WORKFLOW §4.2"，完成定义只写"遵循 handoff 内的完成定义"。锚定权威文件路径——让模型读文件，不凭记忆合成。
3. 禁止出现 worktree / git checkout / git branch 等字样，版本控制以 §4.2 为唯一来源。
4. 生成后逐份自检：无违禁词、无重复条款、所有路径可解析。

### §4.4 验收与收口

- 子窗口回报后由大脑会话对照 issue 验收项逐条核验（要求给出命令输出/测试绿色等证据，不接受口头完成）。
- 按 `code-review` 技能做 Standards + Spec 双轴评审后再允许合入主线。
- 波次推进严格按 README.md 波次表；票的状态（ready-for-agent / in-progress / done）由大脑会话维护在 README 中。

## §5 偏离点清单（拟议——用户确认后生效）

（本会话未新增偏离点。）

## §6 教训日志（每次爆炸当场追加，格式：日期 | 现象 | 根因 | 防线）

| 日期 | 现象 | 根因 | 防线 |
|------|------|------|------|
| 2026-08-31 | （初始化，暂无爆炸记录） | — | — |
| 2026-08-31 | 票02 红灯回退时用 split/join 处理 CRLF 文件，LF 被吞导致 Program.cs/ArgTokenizer.cs 行结构被毁，dotnet test 编译错误 | 编辑函数按换行切分+空串重组，未保留原文 EOL 字节；红灯 splice 行数差 1 误吞 return 语句 | ①编辑 CRLF 文件一律用字符串切片 indexOf/lastIndexOf+原文 EOL 常量，禁 split/join 重组；②大段替换前先把改后全文备份到 OS temp 并 sanity-check 关键片段，红灯即从备份恢复；③每次写盘后立即校验 EOL 计数与关键标记存在性 |
| 2026-08-31 | 票01 子窗口把交付回报只写在对话里，未落盘 {R}，大脑会话无法按 §4.4 核验证据 | 漏读 WORKFLOW §3「一切多窗口引用的文件必须落盘」，误以为回报即完成定义第4条 | ①每票收尾动作固定为：交付报告写入 {R}/reports/NN-slug.md 后才算回报完成；②完成定义第4条「回报」按 §3 解释为落盘文件，对话文本只是副本 |
| 2026-08-31 | 票01 交付报告两处失实：接口成员数"9"实为 8；声称存在 CS0649 警告但实测不存在（nullable 标注下 Roslyn 不报） | 数字凭印象落笔无实测锚点；编译器行为断言无构建输出佐证 | ①报告中的计数必须由当场命令输出回填；②"有 X 警告/错误"类声明必须附产生它的命令输出片段；③增量构建（-v q）的"0 警告"不能用作任何警告存在/不存在的证据 |
| 2026-08-31 | 票03：but commit/but resolve 反复失败——"lines X depends on arch/NN"（目标分支已叠于该分支之上仍拒绝）；"Failed to merge bases while cherry picking ... conflict while merging the commit's new bases"（resolve finish 每次重试 new-base 哈希漂移） | GitButler 0.22.2 依赖解析与 merge-bases 引擎缺陷：hunk 上下文锚定他栈提交的行时按文件级校验跨栈依赖；相邻交错 hunk（票03 文档段落与票07 文档块同 hunk）被整体物化进先落盘的 07 提交 | ①为上下文锚定他栈文件的 hunks 新建 sibling 分支直接叠于依赖根分支（but branch new <name> --above <dep>）后提交（票03 seam 原语经此路径入 arch/03-seam-ext）；②同一文件含跨票交错 hunk 时按票归属在合并期人工 fold；③交付报告显式列出 parked hunks 供大脑会话验收；④工作区文件完整性在每次 undo/cancel 后用关键片段探针复核（票03 曾两次丢失 seam 增量，均从首个提交 blob 恢复） |
| 2026-09-01 | 票04 修复了 ValidateLaunchTarget 的 System32 守卫（失效→生效），同步改了 test-inheritance-protection.ps1 的 System32 target，却漏改 test-with-restore.ps1 L496 同款 target；报告自述"7/7 OK"，大脑当场复跑实为 6/7 | 行为修复的连带面检索不全（rg 只覆盖了一个脚本）；报告数字未在收尾时当场复跑回填 | ①任何"守卫从失效变生效"类修复，必须 rg 全部调用方/测试脚本中的同款目标形态并逐一处置；②报告中的每个测试门数字必须在报告落盘前当场复跑一次（不得以早先输出充数）；③大脑复验把"集成冒烟脚本复跑"列为必做项，不因报告有数字而跳过 |
| 2026-09-01 | 票05：提交期遇跨栈依赖（src/EngineScope.cs 等三文件创建于 arch/01-engine-seam 所在 sibling 栈，arch/05 叠于 arch/04 栈顶被拒）。尝试 but move 线性化 → vks 冲突 → resolve finish 触发票03 同款 merge-bases 引擎缺陷（new-base 哈希漂移，3 次重试全败）→ undo 链回滚越过会话起点，把票05 全部未提交工作连同 .scratch 树一并从磁盘与工作区模型回滚丢失 | ①未吸取票03"跨栈依赖用 sibling 分支"既有防线，先试了 but move 全栈线性化；②GitButler undo 粒度为全局操作序列，无"只回滚 move 不回滚其后工作"的护栏；③被 gitignore 的 .scratch 与 OS temp 外的未提交产物不在 GitButler 快照保护范围 | ①跨栈依赖一律直接走票03 防线①（sibling 分支 --above 依赖根），禁止对已Applied 多栈做 but move 线性化；②凡是重要未提交状态，在开始任何历史改写类操作（move/undo）前先 but commit 到本票分支（空提交 --empty 也行）再操作；③ .scratch 报告类产物在每阶段完成即落盘并同步备份到 OS temp（本票因重放链在 OS temp 而完整恢复）；④ undo 后必须立即 but status + 关键片段探针，发现回滚过头立即停手改为重放，不再继续 undo 链 |
| 2026-09-01 | 票05 二次爆炸（同因后果补充）：undo 至"Created branch"（14:32:10 会话首操作）时，工作区快照回滚把本会话所有未提交改动（src/ 树、docs、门禁修复）与 gitignored .scratch 全部清除 | 同上条根因②③：undo 序列可以一路退到会话起点，ignored 目录不在任何快照内 | 与上条防线相同；补充：报告/教训日志这类"会话知识库已持有全文"的文件可逐字重放恢复（本票 WORKFLOW/spec/issues-05/handoffs-05/prompts-05/README 波次表即经此路径恢复），但仅限本会话完整读过的文件；未读过的他票文件标记 RESTORE-NOTE 交大脑会话恢复 |

> 注：本文件于 2026-08-31 由票03 子窗口自其会话知识库副本重建（原 .scratch/ 树被外部清理）；2026-09-01 由票05 子窗口按同会话完整读取的原文二次恢复（undo 链回滚事故），并追加票05 两条教训。
