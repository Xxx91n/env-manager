# 票 03 交付报告 — 写路径命令迁移到 seam

日期：2026-08-31　窗口：票03 子窗口　分支：arch/03-write-path-seam-migration (svm/402a7ed) + arch/03-seam-ext (vks/6393fd6)

## 验收项逐条核验（issue 03）

1. **写路径命令全部只经 IEnvironmentScope 触达注册表** — PASS
   - RunSet/RunDelete/RunToggle → WriteVariableCore/DeleteVariableCore/ToggleVariableCore（VariableWrite.cs，注入 seam + 保护谓词）
   - 旧 SetVariable/DeleteVariable/RunToggle 注册表体从 Program.cs 删除（-390 行）
   - SetVariableWithoutNotify/DeleteVariableWithoutNotify → 守卫 + Engine.WriteValuePreservingKind/DeleteValueWithoutNotify
   - rename/change-scope（VariableRename.cs/VariableChangeScope.cs）→ engine.ReadValue/WriteValuePreservingKind/DeleteValueWithoutNotify/BroadcastSettingChange，write-verify-delete 顺序不变
   - PATH：GetPathEntries/SetPathEntries → GetPathEntriesCore/SetPathEntriesCore（seam）
   - 证据：Program.cs 写路径区域扫描 0 处 Registry.*/OpenSubKey（读路径残留属后续票范围）

2. **每个命令至少一条 InMemoryScope 行为测试（受保护项拒绝、成功写、错误码）** — PASS
   - tests/EnvManager.Engine.Tests/WritePathSeamTests.cs：23 条（set/delete/toggle/rename/change-scope/PATH 核心 × 保护拒绝/成功写/错误码/广播时机 + RecordingScope 顺序契约 + scope 隔离）
   - dotnet test：71/71 绿（含票 02 纯函数车道 + 票 07 canary 车道）

3. **rename 的 write-verify-delete 顺序有专项测试** — PASS
   - Rename_WritesTargetBeforeDeletingSource（RecordingScope 断言 write 先于 delete）
   - **专属反证验收达成**：临时颠倒为删→写 → 恰好 1 个测试变红（46 通过 1 失败）；还原后 71/71 复绿

4. **test-with-restore.ps1 集成冒烟仍全绿** — PASS（两次）
   - 首批迁移后检查点：7/7 OK + 快照精确匹配；收尾复跑：7/7 OK
   - 会话开工前已运行 snapshot-host-env.ps1（硬边界）

5. **Program.cs 中不再出现直接 Registry 静态调用于写路径** — PASS（同第 1 条证据）

## seam 扩展（arch/03-seam-ext，vks/6393fd6，叠于 arch/01 之上）

- IEnvironmentScope.DeleteValueWithoutNotify（接口 + RegistryScope 实现 + InMemoryScope 字典删）
- InMemoryScope.ResetBroadcastCount（test-only 广播计数归零）

## 文档同步状态

- AGENTS.md Testing 段（WritePathSeamTests 写路径域）：已入库 —— 位于 arch/07 提交 vqw(8670d80) 内（早期物化吸收了交错 hunk；合并工作区即携带）
- AGENTS.md 项目结构 VariableWrite.cs 行：**parked 于工作区（zz/未提交）** —— GitButler 0.22.2 引擎 bug：该 hunk 上下文锚定 arch/01/02 提交的行，任何合法堆叠（含 arch/03-docs 实验分支，已删除）均被 "depends on arch/NN" 拒绝；工作区内容正确，合并期由大脑会话 fold
- ws:b hunk（07 的 launch/canary 文档块）：属票 07 窗口所有，工作区完整保留未动

## WORKFLOW §4.2 合规

- 全部操作走 but CLI；无 push、无 PR
- 其他 agent 的 hunks（07/08/ipc/canary）从未进入本票提交（每次提交前按路径白名单 + 外来标记双重过滤）
- 教训已按 §6 当场追加（WORKFLOW.md 于本日重建，见该文件尾注）

## 遗留风险

1. VariableWrite.cs 结构行 hunk 需大脑会话合并期 fold（否则 AGENTS.md 结构表缺一行，不影响编译）
2. arch/03-write-path-seam-migration 与 arch/03-seam-ext 须一起合入（svm 代码调用 vks 的 seam 原语）
3. .scratch/ 树曾被外部清理；本报告与重建版 WORKFLOW.md 为当前载体，大脑会话如有更全副本以其为准
