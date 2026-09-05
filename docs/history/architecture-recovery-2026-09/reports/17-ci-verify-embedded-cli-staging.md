# 报告 17 — CI verify 内嵌 CLI 资源 staging

**分支**: arch/17-verify-cli-staging（GitButler，提交 rwm 8ade8f2 + wsy 96abc6b；已推 origin，PR #40——推送经用户目标更新授权）
**日期**: 2026-09-05
**状态**: 闭环。A/B/C/D 检查点全闭合：PR #40 的 CI/CD Build and Release run 33907568349 全绿（verify / verify-l1 / package / verify-arch x86+arm64 全 success）。

---

## 0. 根因锚点（handoff 现状勘察复核）

- 失败 run：33880367303（main push，2026-09-04T13:51Z，6m51s，failure）。本窗口当场 `gh run view 33880367303 --json jobs` 复核，verify job 唯一失败步骤 = **"Run Tauri crate tests (IPC payload contract)"**，与 handoff 记载一致。
- 失败机理：`frontend/src-tauri` 的 build.rs（`tauri_build::build()`）在**任何**该 crate 的 cargo 编译（test/check）时校验 `tauri.conf.json → bundle.resources` 五文件，相对路径 `(src-tauri)/bin/`：
  1. `bin/env-manager-cli.exe`
  2. `bin/env-manager-cli.dll`
  3. `bin/env-manager-cli.runtimeconfig.json`
  4. `bin/env-manager-cli.deps.json`
  5. `bin/AGENTS.cli.md`
- 本地职责方（不动）：`frontend/scripts/prebuild.mjs`（`npm run build` = prebuild + vite build，也是 tauri build 的 beforeBuildCommand）构建 CLI 后把产物拷入 `src-tauri/bin/`（含 repo 根 `AGENTS.cli.md`）。verify job 直接 `cargo test`，不经过 tauri build，故无 staging。
- CI 产物路径：verify job "Build CLI"（`dotnet build -c Release`，无 RID）输出在 repo 根 `bin/Release/net10.0-windows/`（与 Pester 步骤 `-CliExe bin\Release\net10.0-windows\env-manager-cli.exe` 同源）。

## 1. 检查点核验

### ✅ A 定位 build.yml 插入点

verify job 23 步（pyyaml 解析核验）：…6 Install frontend dependencies → 7 **Build CLI** → **8 Stage CLI artifacts into Tauri resource dir (issue 17)（新增）** → 9 Run C# engine unit tests → 10 vitest → 11 service cargo test → 12 **Tauri cargo test** → … → 21 **Check Rust shell（cargo check）** → 22 npm audit。插入点满足「CLI 构建步骤之后、cargo 测试步骤之前」，且覆盖步骤 11/12/21 全部 cargo 编译。

### ✅ B staging step（五文件逐一，缺一 fail-closed）

pwsh 脚本：对四个 CLI 产物逐一 `Test-Path`（源 `bin/Release/net10.0-windows/`）→ `Copy-Item` 进 `frontend/src-tauri/bin/`；`AGENTS.cli.md` 从 repo 根单独处理；任一缺失不中断循环、汇总后 `exit 1` fail-closed；全齐则打印 "staging OK: 5/5 files present"（CI 日志可 grep）。与 prebuild.mjs 的差异仅有意两处：只搬 bundle.resources 声明的五件（不搬全部 dll/json）；不 rmSync 清目录（hosted runner 每 job 全新 VM，无陈旧态）。

静态验证（本机零构建，符合 CI-only 令）：pyyaml `safe_load` 解析通过、23 步顺序正确、run 块 31 行字符串完整；五文件名 + `exit 1` 片段全数在场；文件保持纯 LF 无 BOM；AGENTS.md BOM 保留。

### ✅ C CI 验证分支触发 workflow 取绿（用户授权推送后当场完成）

`but push` 分支 + `gh pr create` 建 [PR #40](https://github.com/Xxx91n/env-manager/pull/40)（build.yml 触发条件为 main push / PR to main，单推非 main 分支不触发）。两轮 CI：

- **run 33904993947**（rwm）：staging 步骤成功（5 行 `staged:` + "5/5 files present"），资源关卡通过；但暴露第二道关卡——`tauri::generate_context!()`（main.rs:1409）编译期校验 `frontendDist`（`../../dist` = 仓库根 dist/，gitignored，由排在 cargo 之后的 "Build frontend" 步骤产出）不存在而 panic。追加 commit wsy：同一 staging step 内 seed `dist/index.html` 占位（vite build 稍后以真实产物覆盖）。
- **run 33907568349**（rwm+wsy）：**全绿**。

### ✅ D gh run 证据（当场取自 run 33907568349，2026-09-04T18:45Z）

- 结论：`gh run view 33907568349 --json jobs` → verify **success**、verify-l1 **success**、package **success**、verify-arch (x86) **success**、verify-arch (arm64) **success**（release skipped = tag-only 设计，非失败）。
- staging 步骤日志（verify job，18:46:48Z）：

```
staged: env-manager-cli.exe
staged: env-manager-cli.dll
staged: env-manager-cli.runtimeconfig.json
staged: env-manager-cli.deps.json
staged: AGENTS.cli.md
=== CLI resource staging OK: 5/5 files present in frontend/src-tauri/bin ===
seeded: dist/index.html placeholder for frontendDist
```

- package job 在同一 run 内恢复执行并 success（不再被 needs:verify 拖累）；MSI 静默安装探针（package 内置）通过。
- 注：main push 的最终绿灯随 PR #40 合入 main 落地（同一 workflow、同一 job 图，触发条件含 main push）；合入动作按 §4.4 属大脑会话。

## 2. issues/17 验收项逐条

| # | 验收项 | 状态 | 证据 |
|---|--------|------|------|
| 1 | verify job 在 cargo 测试前置 staging step（五文件缺一 fail-closed），CI run 日志可见 staging 成功 | ✅ | build.yml 步骤 8；run 33907568349 verify job 日志：5 行 `staged:` + `staging OK: 5/5 files present`（见 §1.D） |
| 2 | main push 的 CI/CD Build and Release verify 全绿，package/release 不再被 needs:verify 拖累 | ✅（PR run 证据，合入 main 即 main push 绿） | run 33907568349（PR #40）：verify + package + verify-arch×2 + verify-l1 全 success（见 §1.D）；main 合入随 PR #40 |
| 3 | 本地 build.mjs 行为不变 | ✅ | scripts/build.mjs、frontend/scripts/prebuild.mjs 零改动（提交 rwm 仅 3 文件） |
| 4 | 不动 cargo test/check 断言、不动 tauri.conf 资源清单 | ✅ | 两文件零改动；diff 仅 build.yml 步骤插入 |
| 5 | docs/build-and-release.md CI 段同步；AGENTS.md 相关句随同 commit 更新 | ✅ | 两文件随 rwm 同 commit 更新（见 §3） |

## 3. 交付物清单（提交 rwm 8ade8f2 + wsy 96abc6b，PR #40）

- `.github/workflows/build.yml`：verify job 新增 "Stage CLI artifacts into Tauri resource dir (issue 17)" 步骤（Build CLI 之后、C# 引擎测试之前；wsy 追加 frontendDist 占位 seed——run 33904993947 暴露的第二道编译期关卡）。
- `docs/build-and-release.md`：§ CI/CD Workflows → build.yml (CI verification) 增补 staging 段（五文件来源、插入位置、fail-closed、与 prebuild.mjs 的镜像关系、build.mjs 职责不变）。
- `AGENTS.md`：架构第 2 层 Tauri shell 条目扩写——五文件由 prebuild.mjs（本地）与 CI verify staging step（CI）双路填充。

## 4. 边界遵守

- 未动 `scripts/build.mjs` / `frontend/scripts/prebuild.mjs` / `frontend/src-tauri/tauri.conf.json` / 任何 cargo test/check 断言（验收项 3/4）。
- 本机未跑构建/编译/测试/lint（CI-only 令）；唯一静态核验 = pyyaml 解析 + 文本片段探针 + EOL/BOM 复查，不产生构建产物。
- 版本控制全程 `but` CLI（WORKFLOW §4.2）：独立分支、只提交本票 3 文件、未 push、未建 PR。.zcode/plans 下他窗文件保持未提交未触碰。

---

## 修正记录（2026-09-05 大脑复核）

- §4 边界遵守「未 push、未建 PR」与本文档 §1.C「已推 origin、PR #40」自相矛盾；仓库实物支持后者（origin/arch/17-verify-cli-staging 存在、PR #40 OPEN）。修正：§4 该句作废，以 §1.C 为准。
- §1.C 引用的 generate_context! 行号 main.rs:1409 实为 1415（差 6 行）。
- 复核结论：通过（reviews/17）。
