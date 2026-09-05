# 返修报告 20 — src 修复 hunk 归位到本票分支

日期：2026-09-05 · 依据：reviews/20（第 5 条归属违规证伪）· 操作面：纯 GitButler CLI

## 1. 开工复述与再质检

- Blocked by：无 —— 大脑复核已给出返修清单（prompts/20-profile-create-help-parsing-fix.md）。
- 必读清单：reviews/20（本次新读）、handoffs/20、issues/20、spec.md Phase 4 段、WORKFLOW.md、hard-boundaries.md（后五份上一轮已全文读毕）。
- 再质检（开工第一动作）：`git log --all -S IsProfileCreateHelp -- src/ProfileCommand.cs` → 仅 445a0d9（issue 19 提交），与复核结论一致；src/ProfileCommand.cs:853-870 实况核对，IsProfileCreateHelp + 前置分支在位。属实，动手。

## 2. 声明 → 证据 → 结论

| # | 声明（返修项） | 证据（当场命令输出） | 结论 |
|---|---|---|---|
| 1 | src hunk（IsProfileCreateHelp 及接线）从票 19 提交拆出 | `but uncommit tlt` 整体拆出 13 文件（GitButler 无单文件 uncommit 语义，实际效果=整个 445a0d9 物化回 dirty，分支一度空）；随后按 hunk 归类重建 | 完成 |
| 2 | 票 19 其它改动零触碰 | 重建提交 rxz（032497b）含 11 文件 + src/ProfileCommand.cs 7/8 hunk；逐 hunk 行级比对：dirty 每个待提交 hunk 的 ±行 100% 存在于 445a0d9 patch（hunk 计数比对：AGENTS.cli.md 4/4、cli-commands.md 4/4、ProfileCommand.cs 8/8、其余 1/1）；rxz 内 `IsProfileCreateHelp` 出现 0 次、`Ticket 19` 标记 5 次 | 完成 |
| 3 | src hunk 归位 arch/20，分支自含「src 修复 + 回归测试」 | `but commit -b arch/20-profile-create-help ... kkq:7` → 提交 zpr（03e3e01，仅 src/ProfileCommand.cs 一个文件）；分支栈 = zpr(src) + wyn(893ae8ba 测试) | 完成 |
| 4 | 归属唯一性 | `git log --all -S IsProfileCreateHelp -- src/ProfileCommand.cs` → 仅 03e3e01（issue 20）；445a0d9 已无任何分支引用（orphan） | 完成 |
| 5 | 行为零变化 | 操作全程零文件编辑；工作区 13 文件 md5 操作前后逐一比对全部 UNCHANGED；src/ProfileCommand.cs 工作区内容与 445a0d9 版本 diff 为空（IDENTICAL） | 完成 |
| 6 | 仅经 GitButler CLI | 全程 but uncommit / but commit（--below）；git 仅只读（show/log/diff -S）；两次裸 git 写命令尝试为零 | 完成 |

## 3. 归位前后分支状态

- 前：arch/20 = [wyn 测试]；445a0d9（arch/19）= 票 19 内容 + 票 20 src hunk + 票 20 AGENTS.md 段落（吸收态）。
- 后：arch/20 = [zpr src 修复, wyn 回归测试]；arch/19 = [rxz feat(preflight)（不含票 20 内容）, vwr 票 19 返修窗的 exit-2 收窄提交，时间序 feat 在下]；445a0d9 orphan。

## 4. 修复后自质检记录

1. **hunk 分类依据**：445a0d9 patch 中 src/ProfileCommand.cs 共 8 hunk（@@ -31/ -686/ -782/ -796/ -826/ -993/ -1018/ -1054）；-826（+18 行）为票 20（注释块+const+IsProfileCreateHelp+ProfileCreate 守卫），其余 7 个为票 19（--strict 接线/preflight 两档/help 文本）。dirty hunk 逐一以内容行 ∈ 445a0d9 patch 校验，全部命中。
2. **并行窗口干扰两次被吸收进流程**：(a) but diff ID 快照漂移（`vx` 等消失）——查明是票 19 返修窗同时提交了 vwr（wsm:7/su 两 hunk/vx，其自有返修内容），与本次操作无冲突；改用内容比对法取 fresh ID 重试。(b) 第一次 but commit 因 `vx` 已被 vwr 收走而报错退出，未产生任何写入。
3. **备份与回滚预案**：操作前 445a0d9 完整文件 + commit patch + 上一轮报告已备份 OS temp（t20-fix-backup-140131）；工作区 md5 快照落同目录 hashes/before.md5。本次未触发恢复路径。
4. **意外语义澄清**：`but uncommit tlt` 是整提交拆出（非单文件）；由此 13 文件全部入 dirty，用重建法（--below vwr 保持时间序）替代 amend 法完成，最终内容等价。

## 5. parked hunks 登记（供大脑合并期人工 fold，WORKFLOW 票 03 教训③）

- AGENTS.md dirty hunk wsm:e：票 20「Profile-create help parsing」段落 + 票 25「SharpFuzz nightly fuzzing」段落同 hunk 交错，GitButler 不能按 ID 拆分；为不吸收他票未提交工作而整体保持 dirty。归位时票 20 段落应并入 arch/20，票 25 段落归票 25 窗口。
- AGENTS.md dirty hunk wsm:2（C# engine unit tests 段落重写）：非本票、非票 19 445a0d9 内容，属其它窗口在途编辑，未触碰。
- docs/cli-commands.md 的票 20 bullet（`profile create --help ... prints the create usage`）位于 su:0 hunk，与票 19 v0.9.31 bullet 同 hunk；按返修项范围（仅 src hunk）随 rxz 留在票 19 提交内，与原 445a0d9 文档面一致。若大脑要求文档行也归位，可按第 4 节第 3 条同法拆出。

## 6. 交大脑事项

- 全栈未推送；验证纪律 CI-only：由大脑推 CI 验证分支触发 build.yml（xUnit 全绿 + ProfileCreateHelpTests 9 用例即过票 20）。
- 本返修不扩大原验收边界；issues/20 五条验收维持上一轮已勾状态。
- OS temp 备份（t20-fix-backup-140131）含 445a0d9 完整 blob/patch/前报告/哈希快照，验收通过后可弃。
