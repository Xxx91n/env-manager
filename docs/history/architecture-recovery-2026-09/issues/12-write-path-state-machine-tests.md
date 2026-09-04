# 12 — 写路径状态机模型测试（CsCheck/FsCheck Machine）

**What to build:** 对写路径核心（rename/change-scope/set/delete/PATH add/remove）落地状态机模型测试：模型与引擎同步推进、随机 1000 步、收缩到最小反例，钉住 write-verify-delete 顺序、保护变量拒写与广播时机。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/12-write-path-state-machine-tests.md)

- [x] 新增状态机模型测试：`Machine<EngineState, ModelState>`，操作=Rename/ChangeScope/Set/Delete/PathAdd/PathRemove，模型=字典 + 广播计数
- [x] 人为在 VariableRename 注入"先删后写"，测试在 ≤1e3 迭代内失败并给出最小反例序列
- [x] 广播时机断言：apply 仅在实际写入时广播 1 次；保护变量拒写、rename write-verify-delete 顺序被模型覆盖
- [x] dotnet test 全绿；新增测试进 CI verify job
- [x] 报告落盘 `.scratch/architecture-recovery/reports/12-write-path-state-machine-tests.md`，每条验收附当场命令输出
