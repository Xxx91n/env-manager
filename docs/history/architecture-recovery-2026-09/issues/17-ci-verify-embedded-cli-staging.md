# 17 — CI verify 内嵌 CLI 资源 staging（main 推送每次变绿）

**What to build:** verify job 在任何 cargo 编译（含 Tauri crate tests）发生前，把 CLI 发布产物五件套（exe / dll / runtimeconfig.json / deps.json / AGENTS.cli.md，与 tauri.conf bundle.resources 清单一致）staging 到 Tauri 声明的资源目录，使 main push 的 CI/CD Build and Release 全绿：verify 绿、package/release 恢复执行。本地 build.mjs 职责不变。

**Blocked by:** None — 可立即开工（失败根因已在 2026-09-04 gh 复核确认，run 33880367303）。

**Status:** done (PR #40, run 33907568349 green; awaiting brain merge per §4.4)

- [x] verify job 在 cargo 测试前置 staging step（五文件缺一即 fail-closed），CI run 日志可见 staging 成功（run 33907568349：5×`staged:` + `staging OK: 5/5 files present`）
- [x] main push 的 CI/CD Build and Release verify job 全绿（gh run 证据），package/release 不再因 needs:verify 被拖累（run 33907568349：verify/verify-l1/package/verify-arch×2 全 success；main push 绿随 PR #40 合入落地）
- [x] 本地 build.mjs 行为不变（staging 只存在于 CI 前置，不迁移本地职责；build.mjs/prebuild.mjs/tauri.conf.json 零改动）
- [x] 不动 cargo test/check 断言本身、不动 tauri.conf 资源清单（diff 仅 build.yml staging 步骤 + docs 同步）
- [x] docs/build-and-release.md CI 段同步；AGENTS.md 相关句随同 commit 更新（commits rwm/wsy）
