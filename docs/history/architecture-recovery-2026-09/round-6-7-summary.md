# Architecture Recovery — Round 6/7 Summary（收口 2026-09-10）

> 沉淀自 .scratch/architecture-recovery/（工作区证据保留：spec.md、issues/、handoffs/、prompts/、reports/、WORKFLOW.md、README.md）。
> 心智模型决策（atomcode 24 信源综合）：PRIMARY = Modular Monolith + Bounded Contexts；SECONDARY = Hexagonal（边缘）/ DDD Tactical（两聚合）/ ACL（两边界）；NEW = Capability Descriptors；REJECT = CQRS/ES、Plugin Architecture。

## Round 6（票 36-43，架构清理，纯代码）

| 票 | 主题 | 落点 |
|----|------|------|
| 36 | SettingsDialog 硬编码版本修复 | app_version Tauri command + api.ts appVersion()；CI run 34143791360 |
| 37 | tauri-plugin-updater 接线 | check_update_plugin/download_and_install + updater:default + i18n×10；终绿 run 34307585060（经 37-fix 栈返修） |
| 38 | 抽取 src/Secrets/ | Core(5)/Providers(8)/Manager(1) 三层 + StructuralFitnessTests Rule 4（Secrets-isolation）；红先演练 run 34149414639 |
| 39 | 双层端口 ISecretStore | 五域动词门面 + SecretStorePortTests；run 34222406102 |
| 40 | 类型化 SecretProviderException | 基类+6 子类+SecretProviderErrors 分类器+22 测试；经 #41 栈 run 34267923885 |
| 41 | 能力元数据 | ISecretProvider 四属性 + 真 Available + GUI 通用解析器 + i18n AC4 门；15 轮 run 链 |
| 42 | Profile 聚合根封装 | 15 属性 private set + 域方法 + 62 快照零漂移；run 34268694767 |
| 43 | 抽取 src/Audit/ + audit verify | 五子目录 + AuditVerify 链校验 + CI gate；run 34148618632 |

## Round 7（票 44-45，发版基础设施，HARD GATE）

| 票 | 主题 | 落点 |
|----|------|------|
| 44 | release workflow + latest.json + SLSA L2 | tag 触发 release job + gen-latest-json.mjs（fail-closed）+ attest v4.2.2 + minisign 签名（keyID E52A9736F9A9B1B9 重生成配对）；终绿 run 34351589379 |
| 45 | v0.12.0 Release + tag + release-please 接管 | annotated tag → main HEAD；6 资产（msi/msi.zip/sig/latest.json/portable zip/cli-only zip）；release.yml 退役 |

## 收口时发现并修复

1. v0.12.0 散件泄漏（用户发现）：package job portable/cli-only artifact glob 目录通配 → 16 散件上 Release。修复：glob 收紧（a97b451c）+ live 资产清理 16/16 → 6 资产。
2. Workflow Lint 8 连红（自 39b36db）：dependabot-auto-merge.yml SC2129 ×2 → 分组重定向修复（dfa84699），Lint 首绿。
3. #37 CI 红未回填（窗口流程缺口）→ 37-fix 返修闭环（WORKFLOW §6 教训：CI 结果必须当场回填）。
4. onq 无 hunk ID 吞 22 文件（票 41 窗口）→ mqx revert 自纠；教训：but commit 必带 hunk/file ID。

## 终态指标

- origin/main = dfa84699；四 workflow（CI/CD、Workflow Lint、release-please、Mirror）全绿。
- CI/CD verify 16 步全绿（dotnet 306、vitest 437、Pester 11、gen-latest-json 22、cognitive gate、audit verify --strict、doc-sync、i18n drift）。
- GitHub Release v0.12.0：6 资产 + SLSA L2 attestation；updater 端到端链路（接线→manifest→签名→发布）全通。
