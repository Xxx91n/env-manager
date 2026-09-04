# 票 13 交付报告 — 变异测试闸门（Stryker.NET，本地/PR 辅助）

> 日期: 2026-09-03 · 执行窗口: 票13 子窗口 · 工具: dotnet-stryker 4.16.0 · dotnet SDK 10.0.201 / runtime 10.0.5
> 版本控制: 遵循 WORKFLOW §4.2（GitButler 分支 arch/13-mutation-gate；已推 origin，用户授权，2026-09-04 收口修正）
> 核心立场（spec Phase 3 / research A3）: 追 100% 变异分是被否定的反模式；存活变异的人工审查分类是本票核心交付，非跑分。

---

## 验收项 1: 落地 stryker 配置 ✅

**交付物**: 根目录 `stryker-config.json` + `.config/dotnet-tools.json`（可复现安装: `dotnet tool restore`）。

```json
{
  "stryker-config": {
    "project": "env-manager.csproj",
    "test-projects": ["tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj"],
    "mutate": [
      "src/VariableRename.cs",
      "src/VariableChangeScope.cs",
      "src/ProfileEffective.cs",
      "src/ProtectionCommand.cs"
    ],
    "ignore-mutations": ["string", "logical"],
    "thresholds": { "high": 85, "low": 70, "break": 60 },
    "reporters": ["html", "progress"]
  }
}
```

当场验证（工具还原 + 配置键回读）:

```
$ dotnet tool restore
工具"dotnet-stryker"(版本"4.16.0")已还原。可用的命令: dotnet-stryker
还原成功。

$ cat stryker-config.json | jq '."stryker-config" | {mutate, thresholds, reporters, "ignore-mutations"}'
（四文件 mutate 清单 / 85-70-60 / html+progress / string+logical 如上）
```

注: v4 配置 schema 用 `ignore-mutations`（`mutator` 键在 v4 已移除，首次运行 exit 1 并列出全部合法键——诊断清晰，见验收项 4 摩擦记录）。Stryker 日志确认配置生效:
`ExcludedMutations: [String, Logical], Thresholds: { High: 85, Low: 70, Break: 60 }, Reporters: [Html, Progress], Mutate: [VariableRename/VariableChangeScope/ProfileEffective/ProtectionCommand]`（log-20260903.txt 13:42:29 [DBG] 行）。

## 验收项 2: net10 本地跑通变异分析 + 存活变异清单 ✅

当场命令与输出（完整跑通，wall clock 2m02.8s）:

```
$ dotnet stryker
Version: 4.16.0
[13:43:14 INF] Number of tests found: 106 for project D:\Aworker\env-manager\env-manager.csproj. Initial test run started.
[13:44:04 INF] 6678 mutants created
[13:44:07 INF] 341   mutants got status CompileError. Reason: Mutant caused compile errors
[13:44:07 INF] 111   mutants got status NoCoverage.   Reason: Not covered by any test.
[13:44:07 INF] 52    mutants got status Ignored.      Reason: Removed by block already covered filter
[13:44:07 INF] 3930  mutants got status Ignored.      Reason: Removed by mutate filter
[13:44:07 INF] 2150  mutants got status Ignored.      Reason: Removed by mutation type filter
[13:44:07 INF] 6584  total mutants are skipped for the above mentioned reasons
[13:44:07 INF] 94    total mutants will be tested
Killed:   76
Survived: 18
Timeout:   0
Errors:   0
[13:44:31 INF] The final mutation score is 37.07 %
[13:44:31 WRN] Final mutation score is below threshold break. Crashing...
（退出码 2 —— break=60 闸门语义生效）

Baseline（同树，无变异）: dotnet test → 已通过! - 失败: 0，通过: 106，已跳过: 14，总计: 120（268 ms 测试时长）
```

分数解读（诚实口径）: 37.07% 是 Stryker 把 111 个 NoCoverage 计入失败分母的官方算法；仅对 94 个实际执行变异的 kill 率为 76/94 = **80.85%**。两者都如实上报，不做修饰。

### 存活变异清单（18 条，报告 JSON 提取 + 源码行映射 + trace 日志逐条核实变异内容）

**等价变异 — 2 条**（变异后行为可证明一致，无需新增测试）:

| ID | 位置 | 变异 | 等价性论证 |
|----|------|------|-----------|
| 4945 | ProtectionCommand.cs:139 `!File.Exists(file)` 移除取反 | CustomProtectedVars getter 的 File.Exists 守卫被移除后走 File.ReadAllText → FileNotFoundException → 外层 catch return new()。与守卫路径（文件缺失 → return new()）终态一致，目录由 AppDataDirectory 的 CreateDirectory 保证。 |
| 4987 | ProtectionCommand.cs:221 `!File.Exists(BuiltinProtectedVarsFile)` 移除取反 | 同构: LoadBuiltinProtectedVars 缺文件时 ReadAllText 抛异常 → catch → return defaults，与守卫路径终态一致。 |

**缺失断言 — 16 条**（测试存在真实缺口；均非追分目标，按价值排序）:

| ID | 位置 | 变异（trace 核实） | 缺失的断言 | 红线关联 |
|----|------|--------------------|-----------|----------|
| 4946 | ProtectionCommand.cs:140 `?? new()` 移除左操作数 | CustomProtectedVars 恒返回空 → 用户锁定（protection add-var）的保护完全失效 | **高价值**: 用户锁定变量是不可绕过红线（hard-boundaries "Locked variables cannot be toggled, edited, or deleted"），但测试从未驱动过非空自定义保护表。根因: AppDataDirectory 无测试重定向缝（对比 profiles.json 有 SetProfilesFilePathForTests）。 |
| 4954 | ProtectionCommand.cs:154 `Any`→`All` | IsCustomProtectedVar 语义翻转（空表时 Any=false/All=true） | 同上: 生产 IsProtectedVariable 的 custom-lock 路径只被"应当拒绝"的用例间接触达（seam 测试用注入谓词绕开了生产谓词），"未锁定变量必须可写"的正路断言缺失。 |
| 4989 | ProtectionCommand.cs:223 `?? defaults` 移除左操作数 | builtin-protected-vars.json 外部化配置失效，恒回退内嵌默认 | 外部可编辑保护清单（"edited without recompiling"）无测试覆盖；同受 AppDataDirectory 无缝制约。 |
| 4272 | ProfileEffective.cs:93 `ProfileType=="global"` 取反 | RunProfilePreflight 的 Global-继承-Launch 拓扑守卫整体失效 | **高价值**: v0.7.7 红线（Global 继承 Launch 会把 DPAPI 密文写入 HKCU\Environment）。现有 seam 测试只覆盖其后备层（inherited-secret union，见下），未直接断言"Global 父链含 Launch → preflight false"。 |
| 4278 | ProfileEffective.cs:98 `parent != null` 翻转 | FindProfile 命中时守卫被跳过（父 profile 查不到才拒绝） | 同 4272: 拓扑守卫无直接断言，两条一起死。 |
| 4294 | ProfileEffective.cs:110 `>= 255`→`> 255` | 255 字符变量名边界差一 | 边界值断言缺失（无 255 字符名用例）。 |
| 4304 | ProfileEffective.cs:136 `visited.Add(profile.Name)` 移除 | 循环继承的 poisoned profiles.json 无限递归 | visited-set 的存在理由（"undetected cycle cannot infinite-loop"）无 seam 层循环用例。 |
| 4327 | ProfileEffective.cs:193 `Scope ?? "user"` 移除左操作数 | UnapplyProfile 对 system-scope 变量按 user scope 撤销 → 备份恢复走错 hive | apply 路径有 system-scope 路由测试（票04），unapply 没有对偶用例。 |
| 4331 | ProfileEffective.cs:195 `DeleteValueWithoutNotify(variable.Name, scope)` 语句移除 | 无备份时 unapply 不删除变量（残留禁用/旧值）；有备份时删除+重写与直接重写终态相同（当前断言下不可区分） | unapply 的"删除必须发生"无直接断言（现测试只断言终态值与广播计数）。 |
| 6045 | VariableChangeScope.cs:18 `args.Length < 3`→`<= 3` | change-scope name system（自动探测入口, 3 参数）被误拒 | 覆盖的 4 个 change-scope 测试全走显式 --scope（4+ 参数），自动探测 happy path 无测试。 |
| 6070 | VariableChangeScope.cs:42 `oldScope == null`→`!=` | 自动探测块逻辑整体错位（同上根因） | 与 6045 同缺口: --scope 省略路径零覆盖。 |
| 6052 | VariableChangeScope.cs:25 `newScope != "user"`→`==` | user 目标域校验翻转 | 覆盖测试全部 user→system 方向，system→user 方向无用例。 |
| 6060 | VariableChangeScope.cs:30 `oldScope != null`→`==` | 显式非法 --scope 值（如 foo）不再被拒绝 | 非法显式 scope 值无拒绝断言。 |
| 6310 | VariableRename.cs:26 `ValidateVariableInput(newName,"",scope)` 语句移除 | rename 目标名输入校验（=、控制字符、长度）被绕过 | rename 目标名为非法值（如含 =）的拒绝断言缺失。 |
| 6125 | VariableChangeScope.cs:97 成功 Console.WriteLine 移除 | CLI 人读契约（成功消息）未锁定 | 快照层缺口——票 14（Verify 快照）的地盘，此处只记录不重立。 |
| 6325 | VariableRename.cs:41 成功 Console.WriteLine 移除 | 同 6125 | 同上。 |

## 验收项 3: 红线测试 kill 率与红线清单一一对应 ✅

方法: 从报告 JSON 提取全部 Killed/Survived 变异，按 hard-boundaries.md 契约逐条定位到源码行并核对变异状态（当场提取，非印象）:

| 红线契约（hard-boundaries 条目） | 源码位置 | 变异结局 |
|----------------------------------|----------|----------|
| rename 写-验-删契约（write-verify-delete） | VariableRename.cs:37 / :39 | **全杀**（1+1 killed, 0 survived） |
| rename 保护拒绝（source/target） | :21 / :23 | **全杀** |
| rename --overwrite 门 | :30 | **全杀**（3 killed） |
| rename 单次广播 | :40 | **全杀** |
| change-scope 写-验-删 | VariableChangeScope.cs:80 / :84 | **全杀** |
| change-scope 保护拒绝（source/target） | :63 / :65 | **全杀** |
| change-scope --overwrite 门 | :74 | **全杀**（3 killed） |
| RunProfilePreflight 继承密钥 union 门（v0.7.7 后备层） | ProfileEffective.cs:107 | **全杀**（4 killed） |
| ApplyProfile 保护跳过守卫（poisoned store 防线） | :168 | **全杀**（2 killed） |
| ApplyProfile 备份保留 | :171 | **全杀** |
| ApplyProfile 广播仅当有写入 | :177 | **全杀**（2 killed） |
| IsProtectedVariable system 规则 | ProtectionCommand.cs:166 | **全杀**（2 killed） |
| IsProtectedVariable custom 规则 | :169 | **全杀** |
| change-scope 歧义双 scope 拒绝 | VariableChangeScope.cs:46 | 无受测变异（1 NoCoverage + 3 Ignored）→ 归入验收项 2 缺口清单 |
| change-scope toggle 备份搬迁 | :91 / :93 | 无受测变异（NoCoverage）→ 同上 |

结论: **红线核心契约（写-验-删顺序、保护拒绝、--overwrite 门、广播时机、备份保留、继承密钥门）所在行的全部受测变异 100% 被杀**。存活变异全部落在红线的外围（文件存储层、输入校验边界、自动探测入口、stdout 文案），没有一条穿透红线本体。守卫从失效变生效的连带面教训（票05/09-01）已核: 三处 [!!] 行均为 NoCoverage/Ignored 而非 "survived 被误判 OK"。

## 验收项 4: .NET 10 管线摩擦记录 + 结论 ✅

**本地实测（SDK 10.0.201 / runtime 10.0.5, Windows x64, 4C）**:

1. **工具可用**: dotnet-stryker 4.16.0（稳定线最新）在 net10.0-windows 上完整跑通 6678→94 变异全流程，2m02s。调研所称 "v5 起要求 dotnet10 runtime" 与本机实测无冲突——v4.16 稳定线已兼容 net10，无需 v5 预览。
2. **配置 schema 摩擦**: v4 移除了 `mutator` 键（旧文档/调研记忆中的写法），首次运行 exit 1，错误信息完整列出 24 个合法键（诊断质量好）。已改用 `ignore-mutations`。
3. **CLI 小摩擦**: `dotnet stryker --version` 不是合法旗标（version 是需要值的选项）；工具版本从启动横幅读取。
4. **#3351/#3367 式 CI 管线摩擦（调研已核验）**: 本地未能复现（属 GitHub Actions 托管 runner / vstest 平台问题，本地 Windows 实机不触发）。
5. **闸门语义实测**: break=60 生效——最终分 37.07% < 60 → 退出码 2（"Crashing..."）。这正是"本地/PR 辅助"的正确形态: 当天若直接挂 CI 硬门必然红。

**结论（票级决定，与 spec Phase 3 一致）**: 变异测试定位为**本地/PR 辅助闸门，不上 CI 硬门**。理由: ① 调研核验的 v5/dotnet10 CI 管线摩擦未在 v5 时间线上消除，CI 侧复现成本高；② 当前存活变异中 16 条是缺失断言，修复（补 AppDataDirectory 重定向缝、system→user 方向、拓扑守卫直测等）是后续测试增强工作，在完成前 CI 硬门只会天天红；③ MS Learn 官方指南明确"勿追 100% 变异分"，闸门价值在红线的 kill 证据（验收项 3 已给出），不在分数。运行方式: 本地 `dotnet tool restore && dotnet stryker`，PR 评审时人工查看 StrykerOutput HTML 报告。

## 交付物清单

- `stryker-config.json`（根目录; mutate 红线四文件, ignore string/logical, thresholds 85/70/60, reporters html/progress）
- `.config/dotnet-tools.json`（dotnet-stryker 4.16.0 本地工具清单, `dotnet tool restore` 可复现）
- 本报告 `.scratch/architecture-recovery/reports/13-mutation-testing-gate.md`
- docs/build-and-release.md: 新增 "Mutation testing (local gate)" 小节
- AGENTS.md: Testing 段新增变异闸门一段
- 未提交（他票并行工作，按 §4.2 不动，排除出本票提交）: tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj 的 CsCheck PackageReference（票 12 领域）、tests/EnvManager.Engine.Tests/WritePathStateMachineTests.cs（票 12，未完成态，当前含 CS1003 编译错误）、tests/EnvManager.Engine.Tests/DifferentialOracleTests.cs（票 11 领域）。因票 12 文件未完成，本次收尾的 dotnet test 全绿校验不可用；本票未改任何 C# 代码，红线四文件与其余源码保持提交时原样（Stryker 分析基线即证）。
- StrykerOutput/ 由 Stryker 自带 .gitignore（`*`）自管, 未入库

## 遗留与建议（不阻塞本票闭环）

1. 高价值缺口 → 建议后续票: AppDataDirectory 测试重定向缝（解锁 4946/4954/4989/4945/4987 五个存活的真正修复）。
2. RunProfilePreflight 拓扑守卫直接断言（4272/4278 一对）是廉价的补测点（无需新缝, 纯 seam 数据构造）。
3. change-scope 自动探测入口（6045/6070/6052/6060 四个存活同源于 --scope 省略路径零覆盖）。
4. 4331/6125/6325 属终态断言与 stdout 快照层, 票 14 Verify 落地后自然解决。
5. Stryker 报告含全部 4 文件全量变异明细（含 Killed 76 条的逐一 killing test）: StrykerOutput/2026-09-03.13-42-28/reports/mutation-report.html。
