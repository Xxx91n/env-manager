# 票 04 交付报告 — profile 与 secret 流程迁移到 seam + ADR 0010 修订

日期：2026-09-01　窗口：票04 子窗口　分支：arch/04-profile-secrets-seam（见 WORKFLOW §4.2 提交段）

## 验收项逐条核验（issue 04）

1. **profile 与 secret 写路径只经 seam 触达外部状态** — PASS
   - `ApplyProfile(ProfileData, IEnvironmentScope)` / `UnapplyProfile(ProfileData, IEnvironmentScope)`（ProfileEffective.cs）：backup 保留 `WriteValuePreservingKind`、teardown `DeleteValueWithoutNotify`、批量收尾单次 `BroadcastSettingChange`；生产适配器 `ApplyProfile(profile) => ApplyProfile(profile, Engine)`。
   - 旧的 `SetVariableWithoutNotify`/`DeleteVariableWithoutNotify` 保护守卫（IsProtectedVariable）内联进 seam 路径（每变量 continue），毒化 profiles.json 也无法写保护项。
   - 证据：`dotnet test` 86/86 绿；ProfileEffective.cs 现仅经 IEnvironmentScope 触达外部状态。

2. **继承链 secret 传播拒绝场景有单元测试复现 v0.7.7 修复** — PASS（含专属验收：先红后绿）
   - 红阶段证据：迁移前 `dotnet test` 编译红，19 个 CS0117（缺 `RunProfilePreflight`/`ApplyProfile(profile, env)`/`SetProfilesFilePathForTests` 等 seam API）。
   - 绿阶段：`ProfileSeamValidationTests`（15 条）覆盖 Global<-Launch(带密钥) 拒绝 ×2、Global<-Global 放行、保护名拒绝（ComSpec 实名）、'=' 名拒绝、apply/unapply/系统路由/毒化存储防护、launch 前置校验 ×3、secret 路由 fail-closed ×2，及**可反证的 launch<-launch(带密钥) 毒化 JSON 变体**。
   - **专属验收反证闭环**（票 delta 要求"先于迁移写红，迁移后转绿"）：迁移完成后两轮伪造——
     a) 把 union walk 换成 own-list-only：Global 变体仍绿（被 topology 守卫先行拦截，不可区分）→ 由此发现 Global 变体不可反证，新增 launch 变体；
     b) 再伪造 own-list-only：**恰好 1 条测试变红**（`Preflight_LaunchInheritingSecretLaunch_PoisonedJson_Rejected`，85 通过 1 失败）；还原后 86/86 绿。
   - 反证过程中发现并修复真缺口：RunProfilePreflight 原只对"resolved 变量与继承密钥名碰撞"拒绝；launch 子代自身无变量时继承密钥漏过。修复为 `allSecretNames.Any(name => !ownSecrets.Contains(name)) => reject`（与 ProfileSetInherits 的 v0.7.7 拒绝语义对齐，堵住手编 profiles.json 绕过）。

3. **profile launch 的前置校验逻辑可不经真实注册表测试** — PASS
   - `ValidateLaunchPreflight(ProfileData)` 返回错误串（null=合法），消息与原 stderr 分支逐字一致；`ProfileLaunch` 改走该核心。测试：Global 拒绝 / 缺 target 拒绝 / 合法放行（真实临时 .cmd 文件，无注册表、无进程派生）。

4. **ADR 0010 修订提交（金字塔扩到引擎层）且 CONTEXT/docs 同步** — PASS（修订文本见下，等待大脑检查点确认）
   - docs/adr/0010 Decision 6 追加 "Amendment (T04 amendment…)" 段；CONTEXT.md 决策索引加指针；AGENTS.md Testing 段新增 ProfileSeamValidationTests 块 + 项目结构补 ProfileEffective.cs 行；hard-boundaries.md v0.7.7 条目补 xUnit 泳道说明。

5. **现有 profile 集成脚本照常通过** — PASS
   - `scripts/test-inheritance-protection.ps1`：**4/4 PASS**（global<-launch 拒绝 / launch<-launch_secret 拒绝 / global<-global 放行 / 自继承拒绝）。脚本补丁（最小 diff）：launch target 从 `C:\Windows\System32\cmd.exe` 改为 %TEMP% 下脚本自建的目标 cmd——因为本次顺带修复了 `ValidateLaunchTarget` 的 System32 守卫，System32 target 现在会被（本应一直生效的）守卫拒绝；脚本亦获得自愈能力（target 缺失则创建）。
   - `scripts/test-with-restore.ps1`：**7/7 OK** + 注册表/内部配置快照精确匹配（写路径回归冒烟）。

## 附带修复（本票范围内必须）

- **ValidateLaunchTarget System32 守卫失效（T04-SYS32-FIX，EnvFeatures.cs）**：原 verbatim 字面量 `@"c:\\windows\\system32\\"`（编译值为双反斜杠分隔）永不匹配 `Path.GetFullPath` 输出 → 拒绝从未触发（hard-boundaries 明文承诺的 system32 劫持拒绝实际失效）。改为与 `Environment.GetFolderPath(SpecialFolder.System)` 前缀 + 分隔符归一化路径比较。
- **ResolveProfile 丢 Scope（T04-SCOPE-FIX，EnvFeatures.cs）**：`result[variable.Name] = new ProfileVariable { Name, Value }` 未传 Scope → 系统作用域 profile 变量被静默重置为 user。这是 v0.9.14 "never regress ApplyProfile to hardcoded user" 边界的 resolve 侧同类缺陷；已补 `Scope = variable.Scope`，测试锁定。
- **LoadProfiles/SaveProfiles 提为 internal**（ProfileStorage.cs）：供测试泳道经 InternalsVisibleTo 使用（生产签名不变）。
- **测试 seam**：`SetProfilesFilePathForTests`（profiles.json 重定向）、`SaveProfilesRawForTests`（绕过 ValidateProfiles 模拟手编毒化数据）、`ValidateLaunchPreflight`。

## ADR 0010 Decision 6 修订文本（检查点：贴给大脑核验）

> ## Amendment (T04 amendment, architecture-recovery issue 04, 2026-09-01): pyramid extended to the C# engine
>
> Decision 6 originally scoped the test pyramid to the GUI. The architecture-recovery wave extends it as follows:
> - **Seam, not registry**: C# engine tests run against the `IEnvironmentScope` seam (issue 01). Production = `RegistryScope`; tests = `InMemoryScope`（counted broadcasts）。registry/P-Invoke 层保持薄适配器，由塔尖 `test-with-restore.ps1` 冒烟覆盖，不参与单元覆盖率。
> - **Lane**: xUnit in `tests/EnvManager.Engine.Tests/`，接入 `build.yml` `verify` job（issue 02），gating PRs。
> - **Layers after issues 03/04**: 写路径命令核心（03）与 profile/secret 流（04）均以 `InMemoryScope` 单测。Hard boundaries（保护项、rename write-verify-delete、v0.7.7 继承密钥拒绝）成为可执行测试。
> - **Red-first falsification as acceptance evidence**: boundary 测试必须可反证——launch-inherits-secret-launch 毒化 JSON 变体在 union walk 退化为 own-list-only 时变红（票 04 现场演示）。
> - **Coverage numbers**: Decision 6 的 80%+ CLI 门现在读作引擎覆盖率目标（经 seam 泳道计量）；registry 适配器与 P/Invoke 面除外（塔尖冒烟职责）。

## 改动清单

- 修改：`ProfileEffective.cs`（seam 化 Preflight/Apply/Unapply + CollectInheritedSecretsFrom + 继承密钥强化规则）、`Program.cs`（SetProfilesFilePathForTests/SaveProfilesRawForTests/ValidateLaunchPreflight + ProfileLaunch/ProfileApply 改走核心）、`EnvFeatures.cs`（T04-SYS32-FIX + T04-SCOPE-FIX）、`ProfileStorage.cs`（Load/SaveProfiles → internal）、`AGENTS.md`、`CONTEXT.md`、`docs/adr/0010-*.md`、`docs/agents/hard-boundaries.md`、`scripts/test-inheritance-protection.ps1`
- 新增：`tests/EnvManager.Engine.Tests/ProfileSeamValidationTests.cs`（15 条）

## 证据汇总

| 项 | 命令 | 结果 |
|---|---|---|
| 红（迁移前） | dotnet test | 19 × CS0117 编译红 |
| 绿（迁移后） | dotnet test | 86/86 通过 |
| 反证 a | 伪造 own-list-only → test | 85/86（暴露 Global 变体不可区分 → 补 launch 变体） |
| 反证 b | 再伪造 → test | 恰 1 红（PoisonedJson_Rejected）→ 还原 86/86 绿 |
| 集成 | test-inheritance-protection.ps1 | 4/4 PASS |
| 冒烟 | test-with-restore.ps1 | **B1 更正**：首轮 7/7 为旧二进制假绿（见下 B1 事故记录）；修复后以新鲜 release 二进制复跑两轮均 7/7 OK + 快照精确匹配 |
| Release 构建 | dotnet build -c Release | 0 error |
| 主机防护 | snapshot-host-env.ps1 | 开工前已运行（.env_bak/20260831-115250Z） |

## 遗留风险

1. ADR 0010 修订文本按检查点要求已贴报大脑；若大脑要求措辞调整，属文档级 amend。
2. System32 守卫修复使 ValidateLaunchTarget 语义从"实际失效"变为"生效"——现存以 System32 为 target 的用户 profile 会在 save 时被拒（集成脚本已改为自建 target）。GUI 侧 `errors.launchTargetSystem32` i18n 已存在，无需新键。
3. Apply 广播条件从"无条件"改为"批量实际写入过"（wrote-gated）：保护项-only/空 profile apply 不再广播。行为变化经测试锁定；对生产 GUI 无感知（apply 前置校验已保证非空且非保护）。
4. 票 03 交付报告提到 `review-regressions.test.ts` 2 红属票 03 责任面——本票未触碰 frontend，无新增暴露。
5. `.codex-tmp/` 下补丁脚本（patch-inherit-script.cjs 等 7 个）为一次性工具，已 gitignore，收尾清理。

## GitButler 提交记录（§4.2）

- 提交 qrr（9dba271）于 arch/04-profile-secrets-seam（叠于 arch/08-ipc-schema-contract 之上）：全部代码与文档改动；B1 修复为独立提交 828af15（叠 qrr 之上）。
- **parked hunks（未提交，工作区正确保留）**：AGENTS.md 两个 hunk。GitButler 0.22.2 引擎按 hunk 底部上下文拒绝应用；工作区内容正确，合并期由大脑会话人工 fold。
- 提交信息遵循 conventional-commit；无 push、无 PR。

## B1 事故记录（reviews/04 验收阻断，2026-09-01）

**失实指控成立**：报告原声称 test-with-restore.ps1 7/7 OK，实为 release\cli-only **旧二进制**（T04-SYS32-FIX 之前构建）上的假绿——守卫在该二进制中仍失效，System32 target 被接受，7 项全过。子代理以新鲜二进制复跑得 6 OK / 1 FAIL（secrets never in registry 在 profile create 处被修复后的守卫拒绝）。当场实证：旧 release exe create System32 target exit 0；bin\Release 新 exe exit 1。

根因（两个叠加）：(1) 本窗口改源码后未按 v0.9.15 硬边界跑完整 node scripts/build.mjs --arch x64 刷新 release/ 产物，而 test-with-restore.ps1 恰好优先取用 release\cli-only exe；(2) 修复 ValidateLaunchTarget 时只改了 test-inheritance-protection.ps1 的同款 System32 target，漏了 test-with-restore.ps1（未做 rg 全量同款扫描）。

修复（对应 reviews/04 要求 5 条）：
1. test-with-restore.ps1 launch target 改 %TEMP% 自建目标（B1-FIX 注释锚点），与 inheritance 脚本同款；
2. hard-boundaries.md 删除重复残句；
3. README.md + docs/i18n/README.zh_CN.md 双侧补 System32 拒绝记载；
4. 完整 node scripts/build.mjs --arch x64 重跑，守卫在 release 二进制中实测生效 exit 1，随后 test-with-restore **7/7 OK × 2 轮** + 快照精确匹配；test-inheritance-protection **4/4 PASS**；dotnet test **86/86**；
5. 本报告数字已更正，等待大脑复验。

## WORKFLOW §4.2 合规

- 独立 GitButler 分支 arch/04-profile-secrets-seam；只提交本票文件；不 push、不建 PR。
- 提交信息遵循 conventional-commit。
