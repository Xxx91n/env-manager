# Env Manager — Agent Instructions

Windows 环境变量管理器：C# .NET 10 CLI 直读写注册表 + Tauri/Rust shell + Svelte 4 GUI + 可选 Rust service（密钥挂载生命周期）。Apache-2.0，仓库 `Xxx91n/env-manager`。

## 跑起来
- 全量构建：`node scripts/build.mjs --arch x64`（`--skip-gui/--skip-msi/--skip-cli` 分段），产物在 gitignored `release/`。
- 测试：`cd frontend && npx vitest run`；`dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`（不碰真实注册表）。
- 注册表写操作冒烟只能走 `scripts/test-with-restore.ps1`，裸跑写命令是红线。

## 别碰
- `src/Audit/`、`src/Secrets/` 是 bounded context，跨上下文只消费 `Secrets.Core` 与 `Secrets.Manager`；`release/`、`bin/`、`obj/` 是产物，不手改。
- 三条顶格红线（改码前读 `docs/agents/hard-boundaries.md`）：受保护变量/PATH 不可写删改；写操作三层锁（mutex + CLI_RWLOCK + writeChain）不可绕；rename/scope-change 必须 write+verify 后删源，禁 TxR/TxF。

## 看不出来的坑
- 版本号只有 `env-manager.csproj` 一个源，`build.mjs` 同步到 `frontend/package.json`。
- 改 `installer.wxs` 必须同时过字符串门禁测试和 `docs/build-and-release.md` 的四步静默 msiexec 验证。
- 词汇以 `GLOSSARY.md` 为准；README 改一处，9 语 i18n 同节镜像。
- 版本控制一律 `but`（GitButler）；未经明示不 push、不 merge、不动 tag/release/secret。

## Agent skills

### Issue tracker

Issues live as GitHub issues on `Xxx91n/env-manager`, driven by the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles; label strings equal the role names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/issue-tracker.md` §Triage labels.

### Domain docs

Single-context: root `GLOSSARY.md` for vocabulary; decisions live as comments at the code they constrain. See `docs/agents/domain.md`.
