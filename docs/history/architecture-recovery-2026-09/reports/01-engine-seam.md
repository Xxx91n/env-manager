# 交付报告 — 01 引入 IEnvironmentScope 引擎 seam（expand 阶段）

子窗口执行回报，落盘依据：WORKFLOW §3（一切多窗口引用的文件必须落盘到 {R} 下）。供大脑会话按 §4.4 对照 issue 验收项逐条核验。

## 提交元数据

- 分支：`arch/01-engine-seam`（GitButler 虚拟分支，§4.2 会话模式）
- 提交：`xtk` — `refactor(engine): introduce IEnvironmentScope seam with RegistryScope + InMemoryScope (add-only expand phase, no call-site changes)`
- 未 push（§4.2：不 push、不建 PR，除非用户明确要求）
- 提交 hunk：4 个，全部属于本票（`but status` 前后对照可证；剩余未提交 hunk 均属其他 agent 在途工作，未触碰）

## 改动清单

| 文件 | 性质 | 内容 |
|---|---|---|
| `D:\Aworker\env-manager\EngineScope.cs` | 新增（87 行） | `IEnvironmentScope` 接口（ListVariables/ReadValue/Exists/WriteValue/WriteValuePreservingKind/DeleteValue/Toggle/BroadcastSettingChange）+ `EnvValueSnapshot`/`WriteOutcome`/`ToggleResult` 契约类型 + 抽取票契约注记 |
| `D:\Aworker\env-manager\RegistryScope.cs` | 新增（344 行） | 生产实现：现有注册表/P-Invoke 机制纯搬移（GetScopeTarget、AppendEnvironmentItems、SetVariable 写验+回滚、SetVariableWithoutNotify、DeleteVariable 备份清理、RunToggle 核心无破坏性恢复、SendMessageTimeout/WM_SETTINGCHANGE 广播） |
| `D:\Aworker\env-manager\InMemoryScope.cs` | 新增（203 行） | 测试替身：双字典（OrdinalIgnoreCase）隔离 user/system，`BroadcastCount` 计数，机制与 RegistryScope 逐操作镜像 |
| `D:\Aworker\env-manager\AGENTS.md` | +3 行 | 结构树登记 3 个新文件（文档同步） |

## 检查点记录（handoff 专属 delta）

接口形状（8 成员，覆盖 issue 的「枚举/读/写/删/toggle + 变更广播」——勘误：本报告原版误写 9，实际接口成员 8 个，经 reviews/01 修正；落盘文本此处一并更正）已在实现前于本窗口贴出：scope 沿用 CLI 词汇 `"user"/"system"`（镜像 GetScopeTarget）；WriteValue 内含 kind 策略（`%`→ExpandString）+ 写后验证 + 回滚 + 验证路径广播（与 SetVariable 逐点一致），返回 WriteOutcome 供命令层复现两段 stderr；Toggle 返回 ToggleResult（Error 携带原文案）；命令层关切（校验/Console/JSON/保护变量检查）留在 Program.cs。设计全文见本报告附录 A。

## 验收项逐条证据

1. **三类型编译通过** — 全仓 `dotnet build env-manager.csproj` 0 错误（闭环复核实测，`已成功生成`，exit 0，含 3 个新文件）；另有隔离工程编译 0 错误 + 全引擎快照编译（18 个根目录 .cs）exit 0 双重佐证。
2. **RegistryScope 纯搬移** — 机制逐字搬移自 Program.cs 现有实现；命令层关切（IsProtectedVariable、MaxLength、Console 输出、JSON 发射）未动、留在 Program.cs。
3. **InMemoryScope 隔离语义** — 临时断言程序（handoff 专属验收要求的 REPL/断言形式）**17/17 PASS**：user 写后 system 读不可见、system 写后 user 读不可见、toggle 往返精确恢复、delete 清 toggle/_PowerToys_ 备份、kind 策略、广播计数。正式 xUnit 测试网按 handoff 约定留给票 02。
4. **原有调用点一律未动** — 提交仅含 3 新文件 + AGENTS.md 3 行；git diff 中 Program.cs 无本票改动。
5. **dotnet build 通过** — 同证据 1（闭环复核时并行票已修复 tests/EnvManager.Engine.Tests 的 xunit 引用，全仓构建转绿）。

完成定义对照（handoff）：① 验收项逐条达成（上表）② 测试网等票 02（handoff delta 明示豁免）③ 文档同步已做（AGENTS.md 结构树；未触及 CLI 命令面，无 cli-commands/README 同步义务）④ 提交遵循 §4.2 + 本报告即回报。

## 遗留风险

1. `RegistryScope.DebugSink` 有 1 条 CS0649 未赋值警告——为抽取票预留的静态注入点，票 02/03 接线 Program.DebugLog 时消除。
2. 集成脚本（test-with-restore.ps1）smoke 未跑：本票零 CLI 行为变化，且需待并行票（命令拆分）落定后有稳定验证面再跑。
3. 未 push（§4.2 规定）；CodeGraph 已 sync（Added 6 / Modified 3）。

## 附录 A — 检查点接口形状

```csharp
internal interface IEnvironmentScope
{
    IReadOnlyList<EnvVariable> ListVariables(string scope);            // 枚举：单 scope 投影（含 disabled 投影）
    EnvValueSnapshot? ReadValue(string name, string scope);            // 读：DoNotExpand 原始值 + kind
    bool Exists(string name, string scope);                            // toggle 冲突检测原语
    WriteOutcome WriteValue(string name, string? value, string scope); // 写：kind 策略 + 写后验证 + 回滚
    void WriteValuePreservingKind(string name, string value, string scope); // profile 批量路径盲写
    bool DeleteValue(string name, string scope);                       // 删：主值 + toggle/_PowerToys_ 备份清理
    ToggleResult Toggle(string name, string scope);                    // toggle 全机制，无破坏性恢复
    void BroadcastSettingChange();                                     // 变更广播信号
}
```
