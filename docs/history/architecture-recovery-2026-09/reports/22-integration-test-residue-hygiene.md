# 报告 22 — 集成测试残留卫生（补偿式清理 + 用户自清文档化）

**状态**: 实施完成；CI 验证（检查点 D）按纪律移交大脑触发
**分支**: arch/22-integration-test-residue-hygiene（GitButler，提交号见「提交」节）
**验证纪律（CI-only，用户 2026-09-04 令 + handoff 票 17 同款条款）**: 本窗口未跑任何构建/编译/测试实跑；涉及注册表夹具的实跑只经 CI 的 test-with-restore 路径。本窗口仅执行：文件编辑、PowerShell Parser 静态语法校验（ParseFile 只解析不执行）、codegraph sync。未对用户机器执行任何删除。

## 阻塞与必读确认

- Blocked by: 无（issue 原文 "None — 可立即开工"）。
- 必读清单已读：handoffs/22、issues/22、spec.md Phase 4、WORKFLOW.md（§4.2 版本控制 / §4.4 验收）、docs/agents/hard-boundaries.md（注册表变异测试只走 test-with-restore 夹具）。

## 变更清单

1. `scripts/test-with-restore.ps1`（575 → 620 行，CRLF/无 BOM 保持，pwsh Parser 静态校验通过）
   - `Compare-RegistrySnapshot` 返回值新增 `Added`/`Removed`/`Changed` 结构化字段（Match/Diff 保留，既有调用面零影响；两处 early-return 不含新键，消费侧以 ContainsKey 守卫）。
   - 「残留归零」断言·恢复前分类（对账块内，`$allPass` 计算之后）：前后快照 diff 中出现的一切名字必须落在登记前缀集 `$TestPrefix`（`EM_TEST_`）内；越界名字记为 `registry-foreign-drift` 失败项并置 `$allPass = $false`——即「运行后前后快照 diff 仅含登记值」的硬断言。
   - 「残留归零」断言·恢复后确认（补偿式对账之后）：重扫 HKCU/HKLM 与术前快照差异，非空则 `RESIDUE-ZERO assertion failed` 警告逐名列出并沿用 exit 1；为空输出绿色确认行。
   - `Backup-Registry` 快照时对先于本次运行存在的 `EM_TEST_*` 值（如既有 `EM_TEST_DST=v1`）仅输出信息性提示，绝不触碰（harness 只补偿自己写入的值）。
2. `scripts/check-test-residue.ps1`（新增，86 行，CRLF/无 BOM，pwsh Parser 静态校验通过）
   - 只读枚举 `HKCU\Environment`、`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` 下 `EM_TEST_*` 值，以及 `%LOCALAPPDATA%\EnvManager\profiles.json` 中的 `EM_TEST_*` profile（顶层 JSON 数组、`name` 字段，与 ProfileStorage.cs 形状核对一致）。
   - 退出码：0 = 无残留，1 = 发现残留；支持 `-Prefix` 复用。脚本自身零写入。
3. `docs/build-and-release.md` 测试段新增小节 "Test residue hygiene (self-check and user self-clean)"：断言契约、自检脚本用法、用户自清命令（首选 `env-manager-cli delete <name> --scope user`，因其自带 WM_SETTINGCHANGE 广播；原生 `Remove-ItemProperty` / `reg delete "HKCU\Environment" /v <name> /f` 备选并注明广播差异需重新登录/重启 Explorer；profile 残留走 `env-manager-cli profile delete`）。
4. `AGENTS.md` 测试段追加残留卫生段（保留文件既有 UTF-8 BOM 与 LF）。

## issues/22 验收单核验

- [x] test-with-restore 差分对账块补「残留归零」断言：运行前后快照 diff 仅含登记值 — 证据：变更清单 1（分类断言 + 恢复后归零断言两处挂载点）；`grep -n "registry-foreign-drift\|RESIDUE-ZERO" scripts/test-with-restore.ps1` 各命中唯一插入块；Parser 校验 PARSE-OK。
- [x] 新增残留自检命令/脚本可列出 EM_TEST_* 类残留 — 证据：`scripts/check-test-residue.ps1`（3200 bytes，86 行），双注册表 hive + profiles store 三面枚举，exit 0/1 语义；PARSE-OK。
- [x] 文档给出用户自清命令（注册表删除路径）与操作说明 — 证据：docs/build-and-release.md 小节内含 `env-manager-cli delete EM_TEST_DST --scope user` 与 `reg delete "HKCU\Environment" /v EM_TEST_DST /f` 等具体命令及传播注意事项。
- [x] docs/build-and-release.md 测试段同步 — 证据：同上小节即落在该文件 "Live CLI smoke test" 段之后（锚点 `\`.test-backups/\` is gitignored.`）。
- [x] 本票不改动用户机器现状（用户侧操作，非泄漏） — 证据：本窗口未执行任何注册表/文件系统删除；自检脚本与文档对既有残留只做列出与指引；harness 对先存 EM_TEST_* 值只提示不触碰（Backup-Registry 注释明示）。

## 检查点状态

- A 对账块残留归零断言 — 完成（本窗口）。
- B 残留自检脚本 — 完成（本窗口）。
- C 用户自清文档 — 完成（本窗口）。
- D 交大脑触发 CI — 移交中：按纪律本窗口不推送；需大脑推送 CI 验证分支，走 verify job 的 Pester 集成步骤（run-ci-tests.ps1 → test-with-restore.ps1）观察新断言在绿/红两态的行为；附着 gh run 证据后即闭环。
- E 报告 — 本文件。

## 提交

- GitButler 分支 `arch/22-integration-test-residue-hygiene`，提交 `kko`（65e7f970，change-id kkonymrqpqkulkutstxvnoposupslskp）：`test(scripts): harness residue-zero assertion and EM_TEST_* residue self-check (issue 22)`，`but show` 核实仅含 AGENTS.md / docs/build-and-release.md / scripts/check-test-residue.ps1(A) / scripts/test-with-restore.ps1 四文件；其他 agent 的并行改动（票 14 plan、票 20 测试等）未混入。
- 未推送（WORKFLOW §4.2：不 push、不建 PR，除非用户明确要求）。

## 风险与备注

- 静态校验不等于实跑：两处断言的首次真机行为将在 CI test-with-restore 路径得到红/绿验证；若 CI 红，按 §6 教训流程返修、不本地自证。
- 自检脚本未接入 run-ci-tests.ps1 编排：验收项原文只要求「新增…可列出」，接入 CI 会把共享 runner 上的外侧存量残留直接变红，超出本票最小面；如需硬门，另立票。
- codegraph sync 已执行（Done）；ps1 不在其索引面，属约定性维护。
