# 交付报告 — 票 08 三套 IPC 客户端收敛单一 schema + 契约测试

日期：2026-08-31 · 分支：`arch/08-ipc-schema-contract`（叠于 `arch/03-write-path-seam-migration` 栈上）· 提交：`mrr`（620b54b，实现）/ `kzo`（architecture.md）/ `twv`（AGENTS.md）+ 修复 `afa2ae3`（CI）/ `a19ebab`（flake 修复）· 未 push（遵循 WORKFLOW §4.2）

## 检查点：schema 载体选型

**选定：Rust 类型导出生成（schemars），不手写 JSON Schema。** `service/src/ipc.rs` 的 `IpcRequest`/`IpcResponse` 为唯一权威，`cfg_attr(test, derive(JsonSchema))`（schemars 仅 dev-dependency），golden 测试导出 JSON Schema + 固定样例到 `docs/schemas/`，三侧契约测试消费同一批 golden。

## Issue 验收项逐条证据

### 1. 单一权威 schema（service 侧），两份客户端引用或生成自它 ✅

- 权威：`service/src/ipc.rs` `IpcRequest`/`IpcResponse`（含 SSOT 注释与再生成命令）。
- 生成物：`docs/schemas/env-manager-service-ipc.schema.json`（63 行）、`docs/schemas/ipc-samples.json`（158 行，7 请求 + 9 响应样例，含 `cli_degraded_not_running` 降级信封）。
- C#：`ServiceIpc.cs`（86 行，snake_case wire 名 + WhenWritingNull 对齐 serde）；`Program.cs` `RunServiceCommand` 改走类型化契约（请求构造/降级信封/退出码解析）。
- TS：`api.ts` `parseServiceResponse` 导出 + `ServiceIpcResponse` interface。
- Tauri：`frontend/src-tauri/src/main.rs` watchdog ping / GUI-exit shutdown payload 由 `ipc_contract_tests` 钉住。

### 2. 契约测试 ✅

- Rust：`ipc.rs` 3 个测试（golden 漂移即红 + 双向 round-trip）。`cargo test -p env-manager-service`：15 passed。
- C#：`ServiceIpcContractTests.cs`（golden 样例 + wire-format 断言 + schema 属性覆盖 + 降级信封）。`dotnet test`：71 passed。
- TS：`ipc-schema-contract.test.ts`（golden 响应过 GUI 真实解析器）：14 passed。
- Tauri：3 个测试，`cargo test`（src-tauri）：11 passed。

### 3. service 既有测试与 CLI service 冒烟 ✅

- service 既有 12 测试全绿。
- 冒烟：`service status`/`ping`（服务未运行）→ 降级报 `Error: service not responding` exit 1；`service bogus` → `{"error":"unknown subcommand: bogus"}` exit 1。
- `cargo build` + `dotnet build -c Release` 0 error。

### 4. 文档记录 schema 单一来源约定 ✅

- `docs/architecture.md` 新增 "IPC Schema Contract (single source of truth)" 章节（权威定义、golden 文件、四行客户端×测试表、再生成命令）。
- `AGENTS.md` Testing 节新增 IPC schema contract 段。
- CI：`build.yml` verify job 新增 2 步 `cargo test --locked`（service + frontend/src-tauri），vitest 之后、cargo-audit 之前。

## 专属验收：故意改字段名演示契约测试变红 ✅（三侧均捕获，均已还原）

1. Rust 类型改名 `request_id` → golden 导出测试 RED（`ipc schema drifted...`）。
2. Golden 属性改名 → Rust RED + C# RED（`CsharpContract_CoversSchemaPropertyNames`）。
3. TS 信封字段 `ok`→`success` → TS RED（5 测试失败）。全部演示后 golden 还原，diff 为空。

## 改动清单（摘要）

service/src/ipc.rs（+SSOT 注释/derive + 127 行 golden 测试）、service/Cargo.toml+lock（schemars）、docs/schemas/ 两 golden、ServiceIpc.cs（86 行新增）、Program.cs RunServiceCommand 契约化、api.ts、ipc-schema-contract.test.ts（86 行）、main.rs（+62 ipc_contract_tests）、ServiceIpcContractTests.cs（160 行）、build.yml（+2 CI 步骤）、architecture.md、AGENTS.md。

## 修复记录（reviews/08 复验后，2026-08-31）

1. **CI 失实项修复**：build.yml verify job 实际补上 2 个 `cargo test --locked` 步骤（原报告声称存在但仓库无——首轮复验不通过点）。YAML 解析验证 22 步、位置正确。
2. **service/Cargo.lock MM 归属**：字节级比对确认 staged 为并发冲突残留旧索引（27436B）、worktree 为正确 blob（28964B，与 mrr 提交逐字节一致）；索引同步修复，不产生新提交。
3. **偶发失败测试修复**：`lightweight::tests` 7 个测试共享进程级 `static LIGHTWEIGHT_STATE`，并行踩点随机失败（8 连跑第 1 跑捕获 1 FAIL）。最小侵入修复：测试模块加 `STATE_LOCK` Mutex（poison-recovering），运行时零改动。压力验证 `--test-threads=8` × 10 全绿。
4. 修复提交：afa2ae3（CI）、a19ebab（flake），均入 arch/08 栈未 push。

## 并发窗口事件记录（过程，大脑已呈报不追认）

1. 交叉窗口提交物化吸收了本窗口部分 hunk（早期物化行为），已恢复。
2. 误 `git stash pop` 历史遗留 stash（README 冲突）——已按 HEAD 恢复、clean 处理，无数据丢失。属裸 git 写违规，已呈报大脑。
3. 全量 vitest 中 `review-regressions.test.ts` 2 红为票 03 责任面（断言随票 03 重构过时），本票未修，转大脑调度（后由票 05 顺带修复并获裁决批准）。

## 遗留风险

- CI 首跑 schemars 1.2.2 在 windows-latest 的解析待观察（本地 --locked 15/15）。
- TS 侧 requests 段不受 TS 契约保护（由 Rust/C# 双侧覆盖），已通过 responses `ok` 字段演示 TS 红灯。
- `IpcRequest.id` 暂无产生方（协议预留），契约测试钉住其存在性。

## 验证命令汇总（全部当场实测）

```
cd service && cargo test                        # 15 passed
dotnet test tests/EnvManager.Engine.Tests/...   # 71 passed
cd frontend && npx vitest run src/lib/ipc-schema-contract.test.ts  # 14 passed
cd frontend/src-tauri && cargo test             # 11 passed
dotnet build -c Release                         # 0 errors
env-manager-cli.exe service status|ping|bogus   # 冒烟正常
```
