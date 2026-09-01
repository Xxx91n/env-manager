# architecture-recovery 收口摘要（2026-09-02）

> 母体文件：本目录即 `.scratch/architecture-recovery/` 的归档副本（WORKFLOW / spec / issues / handoffs / prompts / reviews / reports 全量在侧）。本摘要是 git 跟踪的权威收口记录。

## 范围与结果

8 票全部收口（reviews/01..08）：引擎 seam（01）、xUnit 泳道（02）、写路径 seam 迁移（03）、profile/secret 迁移 + ADR 0010 amendment（04）、Program.cs 命令域拆分 + src/ 布局（05）、EnvFeatures.cs 五域分家退役（06）、launch 注入三层验证（07）、IPC schema 契约（08）。

## 终验留证（收口日当场复跑）

| 门 | 命令 | 结果 |
|---|---|---|
| 完整构建 | `node scripts/build.mjs --arch x64` | exit 0；portable/cli-only/msi + 3 zip 产物齐全 |
| xUnit | `dotnet test tests/EnvManager.Engine.Tests/` | 86/86（86 = 含 Theory 数据行展开；[Fact]+[Theory] 声明数 68） |
| 集成四套件 | `scripts/run-ci-tests.ps1 -CliExe release/cli-only/...` | launch 6/6、canary 9/9、inheritance 4/4、with-restore 7/7 + 快照精确匹配，CI tier PASSED |
| 前端 | `npx vitest run`（frontend/） | 40 文件 398/398 |
| Rust | `cargo test --locked`（src-tauri / service） | 11/11、15/15 |
| Ticket-05 行为零变化 | 迁移前参照树 vs 迁移后二进制 10 命令逐字节 diff | 10/10 IDENTICAL |

## 合并拓扑（but land 序列，待用户授权 push）

- Stack A（先合）：`but land arch/06-envfeatures-domain-split-b --whole-stack` → 02 → 07 → 03 → 08 → 04 → 05-b → 06-b
- Stack B（后合）：`but land arch/06-envfeatures-domain-split --whole-stack` → 01 → 03-seam-ext → 05 → 06
- target = origin/main（land 即 push）；合前已 `but pull`（无新上游）。

## 过程审计结论

- 交叉核对：README / reviews / reports 三方 8 票状态、测试数字链（71→86）、分支拓扑、parked hunks 演化链全部一致；2 处轻微数字矛盾（reports/01 "9 成员"、README/reviews "9 条分支"）已就地修正。
- 三层文档一致性（CONTEXT.md / docs/adr/ / 代码现状）：ADR 0010 amendment、hard-boundaries、architecture.md 全部与实物一致；修掉 CONTEXT.md ScrubExceptionMessage 失实句（含一个 ESC 残留控制字符）+ AGENTS.md 结构树补 ProfileAudit.cs；CONTEXT.md 补登本轮四决策（src 布局、EnvFeatures 退役、IPC golden、三层验证）。
- 过程事故均呈报不追认：票 08 裸 git stash 写、票 05 undo 越界毁 .scratch（已恢复，教训与防线入 WORKFLOW §6）。

## 验收方式沉淀（可复用）

每票"声明→证据→结论"对照表 + 独立子代理当场复跑测试门 + 分支/提交实物抽查；红灯可反证（伪造实现观察测试变红）为边界类验收的强制形态（ADR 0010 amendment 已制度化）。
