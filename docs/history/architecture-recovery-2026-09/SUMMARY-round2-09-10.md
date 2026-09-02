# architecture-recovery 收口摘要 — Round 2（2026-09-03）

> 母体文件：本目录为 `.scratch/architecture-recovery/` 的归档副本。本摘要为 git 跟踪的权威收口记录（Round 2 = 票 09/10，Round 1 = 01-08 见 SUMMARY.md）。

## 范围与结果

2 票收口：SecretProvider.cs 按 provider 拆模块（09）、SecretProvider 契约测试套件（10）。两票均由大脑会话按「声明→证据→结论」对照表当场复跑验证（reviews/09、reviews/10）。

## 终验留证（收口日当场复跑）

| 门 | 命令 | 结果 |
|---|---|---|
| 完整构建 | `node scripts/build.mjs --arch x64` | EXIT 0；portable/cli-only/msi + 3 zip + SHA256SUMS 齐全 |
| xUnit | `dotnet test tests/EnvManager.Engine.Tests/` | 106 通过 + 14 跳过 = 120（基线 86 → +34 契约/闸门） |
| 前端 | `npx vitest run` | 40 文件 398/398 |
| Rust | `cargo test --locked`（src-tauri / service） | 11/11、15/15 |
| 受拆分影响的 3 门禁文件 | vitest 定向 | 50/50 |

## 合并拓扑（待用户授权 push）

- target = origin/main；`but pull` 已执行（No new upstream commits）。
- `but land` 对远程 target = 合入+推送一步到位，无「只合不推」分离命令 → 按用户指令在 push 前停下。
- 授权后的合入命令（栈序：arch/09 先、arch/10 后；10 叠于 09）：`but land arch/09-secret-provider-split --whole-stack --yes`（`--whole-stack` 一次落 09+10 两栈）。

## 过程审计结论

- 交叉核对 reports ↔ README：一致。唯一发现 = report 10 含「reviews/09 尚未落盘」过期陈述（现 reviews/09 已存在，系报告时间差，非矛盾）。
- 三层文档一致性：CONTEXT.md 补登 issues 01-10（新增 09/10 一句）；ADR 0010 已覆盖 C# 引擎测试金字塔，契约测试为其一层、无需新 ADR；AGENTS.md Testing 节（ticket 10 已更新）与代码一致。
- 过程违规 1 条（呈报不追认）：票 10 下游提前解锁（详见 reviews/10）。

## 遗留事项（backlog，待用户决定是否立票）

1. CLI `profile create --help` 解析缺失（把 --help 当 profile 名落库）
2. Program.cs 441 行 CliRuntime 拆出（如需 <400）
3. ValidateProfiles 悬空 launch target 硬阻断一切 profile 写 → 评估降级 warning+隔离
4. CI 用户态隔离（集成测试独立 LOCALAPPDATA 防污染用户 profiles.json）
5. architecture.md 补 canary/golden 段（现权威在 AGENTS.md Testing 节）
6. 用户侧注册表残留 `EM_TEST_DST=v1`（用户自清）

## 构建教训（入档）

`build.mjs` 与全量 `vitest` 并发会互相争用 vite/CPU，导致 tauri release 链接阶段 flake（「could not compile env-manager」假失败）。留证：干净单跑 `cargo build --release` 3m32s 成功、`build.mjs` 单跑 EXIT 0。禁止构建与全量 vitest 并发。
