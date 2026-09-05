# 票 18 返修报告 — 编译错误 + workflow lint

日期：2026-09-05 · 分支：arch/18-mutation-survivor-triage（返修 hunk 已 amend 进提交 uro）· 依据：reviews/18-mutation-survivor-triage.md

## 开工再质检（声明 → 证据 → 结论）

| # | 复核声明（reviews/18） | 回仓库实物复验 | 复验结论 |
|---|---|---|---|
| 返修 1 | MutationSurvivorTriageTests.cs:46/:63 用 JsonSerializer 缺 using System.Text.Json，CS0103 ×2（run 33944493443） | 文件 using 区仅 `using EnvManager;` + `using Xunit;`；:46/:63 确有 `JsonSerializer.Serialize(...)` 两处；System.Text.Json 不在 ImplicitUsings 集合内 | **属实** |
| 返修 2 | build.yml stryker job「ls -t … | head -1」触发 actionlint SC2012（run 33944493448） | stryker job Per-module 步骤确为 `report="$(ls -t StrykerOutput/*/reports/mutation-report.html | head -1)"` | **属实** |

复核结论无误差，未扩大返修范围。

## 修复内容

### 返修项 1 — CS0103（tests/EnvManager.Engine.Tests/MutationSurvivorTriageTests.cs）

- 修复：using 区补 `using System.Text.Json;`（置于 `using EnvManager;` 之后、空行 + `using Xunit;` 之前，保持文件原有分区风格）。
- 根因记录：写票时误判 System.Text.Json 在 net10 ImplicitUsings 集合内（集合仅含 System/System.IO/System.Collections.Generic/System.Linq/System.Net.Http/System.Threading/System.Threading.Tasks）。

### 返修项 2 — SC2012（.github/workflows/build.yml stryker job）

- 修复：
  ```yaml
  report="$(find StrykerOutput -type f -name mutation-report.html | sort | tail -1)"
  echo "report: $report"
  test -n "$report"
  node scripts/stryker-module-scores.mjs "$report" | tee stryker-module-scores.txt
  cp "$report" mutation-report.html
  ```
- 等效性论证：Stryker 输出目录为 `StrykerOutput/<yyyy-MM-dd.HH-mm-ss>/reports/`，ISO 式时间戳目录名**字典序即时间序**，故 `sort | tail -1` ≡ `ls -t | head -1`（最新报告）。`test -n "$report"` 为新增 fail-fast 守卫：报告缺失时步骤立即红（原 ls 管道对空结果静默通过）。
- SC2012 消除依据：find 替代 ls 解析；无 `ls -t … | head` 模式残留（pattern 扫描见下）。

## 修复后自质检记录

| 检查 | 命令/方法 | 结果 |
|---|---|---|
| find 等效性（最新目录优先） | 本机对既有两个 StrykerOutput 目录执行 `find … | sort | tail -1` | 输出 `StrykerOutput/2026-09-04.19-08-22/reports/mutation-report.html`（较新目录）✓ |
| SC2012 模式清除 | stryker job 段 pattern 扫描 `ls -t…|head` 与裸 `ls` | 0 命中 ✓ |
| using 生效范围 | 文件内 `JsonSerializer.` 出现 2 处，全部可由新增 using 解析 | ✓ |
| 文件完整性 | 两文件字节数（6208 / 20573）、无 BOM、无 CRLF、花括号平衡 22/22 | ✓ |
| CI-only 纪律 | 本机未跑 dotnet build/test/actionlint；编译与 lint 证据交由大脑推 CI（verify + Workflow Lint） | 遵守 ✓ |

## 版本控制记录（WORKFLOW §4.2）

- 两处返修 hunk（build.yml 的 find 替换 + 测试文件的 using 行）经 `but amend -t arch/18-mutation-survivor-triage kl oz` 并入该分支 tip 提交 uro（未推送分支，amend 合规）。
- 并行会话文件未吸收：AGENTS.md 脏区含他票 Testing 段改写、ticket 25 fuzz 文件增删——均未进入本票提交；amend 后两文件在脏区归零、他票文件保持原状。
- stryker-config.json 零改动（git diff 为空），阈值/mutate/ignore 边界未动。

## 原始验收项影响面（issues/18，不扩大边界）

| 原验收项 | 返修影响 |
|---|---|
| 登记文件落盘 | 无影响（复核已证实） |
| 补测试后重跑 kill 上升、survived 仅余等价 | CS0103 修复后测试套件方可编译，Stryker 重跑路径打通 |
| 阈值 85/70/60 与 ignore 不变 | 无影响，配置未动 |
| Stryker 经 CI 可跑 + 模块分算 | SC2012 修复后 Workflow Lint 可绿，stryker job 可被 workflow_dispatch 触发 |
| 趋势记录 | 仍待大脑推 CI：verify + Workflow Lint 双绿 → workflow_dispatch 触发 stryker → 回填趋势表 |

## 大脑下一步（与 reviews/18 一致）

1. 合流/推 CI 验证分支：期望 **verify 绿**（CS0103 消除）+ **Workflow Lint 绿**（SC2012 消除）。
2. 双绿后手动触发 `stryker` job（workflow_dispatch），取 `stryker-mutation-report` artifact 回填 reports/18-mutation-survivor-triage.md 趋势表与 registry `runs[]`。
