# 报告 07（补档版）— launch 注入生效验证：golden 断言 + 探针进程 + canary redaction 负断言

- 窗口性质：**补档窗口**（代码交付已于早前窗口完成并通过 reviews/07 实存核验；本窗口零代码改动、零版本控制写操作）
- 日期：2026-08-31
- Blocked by：02（已核验通过）；本票 Status：done（工作核验通过，报告补档）
- 代码归属：GitButler 分支 `arch/07-launch-injection-verification`（叠于 `arch/02-test-lane-bootstrap`），commit **`8670d80`**

## 0. 取证环境与局限（如实标注）

1. **红灯演示无法事后重演**：原"关掩码→红→恢复"专属验收是瞬时状态变更，已随原窗口结束还原（当时输出：`Tests Passed: 7, Failed: 2`，S1 断言红于 `CANARY LEAK in sink 'profile show'`）。事后以两类替代证据佐证：(a) 测试结构证据——S1 泄漏断言与掩码正断言在测试文件中实存且语义正确；(b) S6 正控制——canary 必须到达子进程 env 的正断言，防零泄漏断言虚绿。
2. **Pester 端到端复跑当前受环境脏数据阻塞**（与票 07 代码无关）：`profiles.json` 残留 launch profile 指向已删除的 `D:\honeygain\Honeygain.exe`，`SaveProfiles` 前全量校验 `ValidateProfiles` 使任何 profile 写操作 exit 1。只读命令不受影响。测试代码本身无回归——失败发生在 fixture 建立阶段，非断言阶段。

## 1. 交付物实存核验

`git show 8670d80 --stat`：

```
 AGENTS.md                                          |   9 +-
 scripts/run-ci-tests.ps1                           |  32 +++-
 .../CanaryRedactionTests.cs                        |  71 +++++++++
 tests/canary-redaction.Tests.ps1                   | 169 +++++++++++++++++++++
 tests/launch-env-injection.Tests.ps1               | 128 +++++++++++++----
 5 files changed, 373 insertions(+), 36 deletions(-)
```

行数：canary-redaction.Tests.ps1 169；launch-env-injection.Tests.ps1 205；CanaryRedactionTests.cs 71；run-ci-tests.ps1 108。分支 `arch/07-launch-injection-verification` 实存（与 arch/02、arch/08 同栈）。

## 2. issue 验收项逐条映射（仓库实物证据）

### 验收项 1：launch 注入有 golden env diff 断言

- `tests/launch-env-injection.Tests.ps1` L161 `It "golden env diff: injected set exactly matches the profile's resolved variables"`；L102 `Get-ProbeEnv`（launch → 子进程 dump → NAME=VALUE 解析）；L163-168 两向断言：`$unexpected`（注入集之外、allowlist 之外）必须为空 + `$expected.Keys` 逐一断言存在且值相等。
- allowlist：L65 `$script:cmdIntrinsicVars = @('COMSPEC', 'PATHEXT', 'PROMPT')`（Win11 实测）。
- 状态：**达成**。

### 验收项 2：探针进程从子进程内回读注入值并断言

- 同文件 L148 `It "profile launch injects both regular and secret variables into the spawned child process env"`（L153-156 经 `Get-ProbeEnv` 精确回读）。
- 状态：**达成**。

### 验收项 3：canary 假 secret 测试覆盖全部输出 sink，泄漏即红

- `tests/canary-redaction.Tests.ps1` sink 清单：S1 `profile show`、S2 `profile preview`、S3 `profile list`、S4 audit（`history list`）、S5 launch stdout、S6 子进程 dump（正控制）、S7 强制错误 stderr。
- 断言函数 L94 `Assert-NoCanary`（泄漏即 throw `CANARY LEAK in sink`）；It：L102(S1)/L106(S2)/L110(S3)/L114(S4)/L118(S5)/L122(S6)/L140(S7)。
- canary 形制：`password=canary-<12hex>` 全局唯一随机，无误报面。
- 状态：**达成**。

### 验收项 4：掩码正断言存在

- canary-redaction.Tests.ps1 L142-146：show 输出匹配 `\u003Cencrypted\u003E`（CLI JSON 转义）；L148-153：audit reveal 条目 `profile reveal-secret` + `<revealed>`。
- CanaryRedactionTests.cs L21-24 `Positive_MaskPlaceholderAppearsInScrubbedOutput`：断言 `<redacted>` 出现。
- 状态：**达成**。

### 验收项 5：并行安全、不摸敏感注册表、集成冒烟照旧

- fixture 全部唯一随机命名（`emci_<8hex>` / canary `<12hex>`），AfterAll 清理；xUnit 纯函数无共享状态。
- launch 用例仅**读** HKCU\Environment（L184-194 `launch never writes the registry`）。
- `scripts/run-ci-tests.ps1` L7/L62-78 挂载 Suite 2 CanaryRedaction（JUnit `canary-redaction.junit.xml`）。
- dotnet test：`失败: 0，通过: 71`（71 = 票02 18 + 票03 23 + 票07 6 + 票08 12 + 既有 12）。
- 状态：**达成**。

### xUnit canary 6 例

```
Negative_CanaryPasswordPatternDoesNotSurviveScrub
Positive_MaskPlaceholderAppearsInScrubbedOutput
Negative_CanaryAfterBearerPatternDoesNotSurviveScrub
Negative_CanaryAfterVaultTokenPatternDoesNotSurviveScrub
Boundary_CanaryWithoutKnownPatternIsNotMasked
Negative_MultipleCanaryOccurrencesAllMasked
```

语义符合 ADR 0005：22-pattern 词表内形制不存活、`<redacted>` 出现、无模式值透传（best-effort 边界成测）。

## 3. 专属验收（关掩码→红→恢复）——如实标注

原窗口完成并留输出：RED `Tests Passed: 7, Failed: 2`（S1 泄漏断言 + 掩码正断言双红，`RuntimeException: CANARY LEAK in sink 'profile show'`）→ 还原（cmp 字节一致）→ GREEN `Tests Passed: 9, Failed: 0`。补档窗口不可零改动复演；替代佐证见 §0。

## 4. 文档同步核验

- 8670d80 含 5 文件（AGENTS.md / run-ci-tests.ps1 / CanaryRedactionTests.cs / canary-redaction.Tests.ps1 / launch-env-injection.Tests.ps1）。
- AGENTS.md Testing 节 L129-134：three-layer net 声明 + 四个交付物条目。
- 注：reviews/07 曾记 "docs/architecture.md canary 段已入库"，当场 grep=0——失实，经大脑勘误：AGENTS.md 为本票权威文档位，不构成验收阻塞。

## 5. 完成定义对照

| # | 条款 | 状态 |
|---|------|------|
| 1 | 报告落盘 + 验收项逐条证据或如实标注 | ✅ |
| 2 | 补档窗口零代码改动、零版本控制写操作 | ✅ |
| 3 | 大脑核验通过后收口 | ✅（见 reviews/07） |

## 6. 教训补录

| 日期 | 现象 | 根因 | 防线 |
|------|------|------|------|
| 2026-08-31 | 补档窗口 Pester 复跑 6/6→0/6（fixture 阶段 profile create exit 1） | 用户卸载 honeygain 后 profiles.json 残留 launch profile 指向不存在 exe；ValidateProfiles 全量校验阻断一切 profile 写 | ①端到端测试报告应记录运行时环境前提；②产品层可评估 ValidateProfiles 对 launch target 缺失降级为 warning+隔离（产品决策，非本票范围）；③CI 应用独立 LOCALAPPDATA 环境隔离用户数据 |

## 7. 大脑复验结论（2026-08-31）

- **通过，票 07 收口**。5 条验收项逐条复核成立（71/71、5 文件 373/36、行数 169/205/71/108 当场复核一致）。
- §4 差异裁定：reviews/07 "architecture.md canary 段"记录失实，已勘误；不构成验收阻塞。
