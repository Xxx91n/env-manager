# 复核报告 — 票 15：Testcontainers L1 矩阵（7 个 Skip 挂载转真实后端）

日期：2026-09-04 · 复核方式：独立子代理只读取证 + 大脑会话 gh 核对 CI + dotnet test 复跑 · 结论：✅ 可验收（附合入期 fold 条件）

## 声明 → 证据 → 结论

| 声明（子窗口报告） | 证据（仓库实物） | 结论 |
|---|---|---|
| 7 个 ContractTests 静态 Skip → [SkippableFact] L1 | 7 文件均含 `[SkippableFact] [Trait("Category","L1")]`（L1 共 13 条；OnePassword 保留 2 条静态 Skip，Encrypt 侧 op 拒绝 Connect，已披露） | 属实 |
| 基类 internal AssertRoundTrip/AssertPlaintextNotEmbedded 重载 | SecretProviderContractTests.cs:68/:93，原 protected 无参版保留委托，零破坏 | 属实 |
| 每 provider 一个 L1Harness | L1Harnesses.cs 内 7 个类（VaultKv2/Aws/Azure/CredMan/PSSecretManagement/OnePassword/Sops） | 属实 |
| 包引用自包含 | 分支经 `tests/EnvManager.Engine.Tests/Directory.Build.props`（提交 565b2e6=nnk）钉 Testcontainers 三包 + skippablefact 1.5.85；工作区 csproj 另有 parked uq hunk 重复声明同一组包 | 属实（且暴露 fold 期去重点，见下） |
| build.yml 含 verify-l1 job | 工作区与分支树均含：ubuntu-latest、docker 检查、pwsh SecretManagement、`--filter "Category=L1"` + `EM_L1_MATRIX: "1"`；漂移：实际 `timeout-minutes: 45`，报告摘录写 30 | 属实（一处摘录漂移） |
| ws(AGENTS.md)+uq(csproj) 两 hunk 驻留 | but status `zz [uncommitted]`：`ws M AGENTS.md`、`uq M csproj`；git diff 内容与报告引用段落逐字吻合 | 属实 |
| origin/main=fb9c065；arch/12、arch/14 仍 Applied | `git rev-parse origin/main`=fb9c065；but status 含 rc(arch/12)、ch(arch/14) Applied | 属实 |

## 大脑核对 CI（gh，外部闭环）

- `gh pr checks 39` → **verify-l1 PASS（1m36s）**；verify FAIL 的失败步为 Tauri `resource path bin\env-manager-cli.exe doesn't exist` —— 大脑已用 `gh run view 33670504746 --log-failed` 确认 **main 分支 2026-09-02 同一步同样失败**，预存问题属实，与本票无关。
- validate-pr-title FAIL（draft PR 标题无 conventional 前缀），不影响证据价值，合入前需改名。

## 大脑当场复跑

- `dotnet test -c Release` → 131 通过 / 20 跳过 / 0 失败（L1 挂载按亲和跳过，本机无 Docker 符合检查点 A 结论）。

## 合入期 fold 条件（大脑职责，非返工）

1. ws hunk（AGENTS.md L1 段）与 uq hunk（csproj 包块）在 arch/12、arch/14 合入 origin/main 后 fold 进 arch/15；**uq 与已提交的 Directory.Build.props 重复声明同一组包（本复核 dotnet restore 现 NU1504 重复警告）——fold 时保留 Directory.Build.props 为唯一事实源，丢弃 uq 的 csproj 包块**。
2. 驻留 hunk 备份在 %TEMP%/env-manager-ticket15-backup/（报告自述，逐字节探针已过）。

## 结论

7 项声明全部属实，CI 证据经 GitHub 侧闭环。✅ 可验收（附上述 fold 条件）。
