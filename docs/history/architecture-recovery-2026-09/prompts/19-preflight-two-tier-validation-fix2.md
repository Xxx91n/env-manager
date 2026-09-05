# 窗口启动器 — 票 19 二次返修：%VAR% 判定去机器依赖（CI 首跑红）

你是 Env Manager 仓库（D:/Aworker/env-manager）中一张二次返修票的独立执行窗口，只对票 19 二次返修负责。

## 必读清单（动手前读完）

- .scratch/architecture-recovery/reviews/19-preflight-two-tier-validation.md（含返修复核段）
- .scratch/architecture-recovery/handoffs/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/issues/19-preflight-two-tier-validation.md
- .scratch/architecture-recovery/spec.md（Phase 4 段）
- .scratch/architecture-recovery/WORKFLOW.md
- docs/agents/hard-boundaries.md（测试纪律：不碰真实注册表、不依赖机器环境状态）

## 本票 delta（二次返修项）

- Blocked by：无 — 大脑 CI 复核发现首跑红，返修依据如下。
- 失败证据：PR #42 长链 CI（run 33953937157）→ Run C# engine unit tests → ProfileSeamValidationTests.Detailed_DefinedVarReference_NoWarning [FAIL]（Assert.False()）。
- 大脑根因诊断（开工先再质检复核此诊断，属实才动手）：该测试用 %SYSTEMROOT% 并期望无警告，而 CollectPreflightWarnings（src/ProfileEffective.cs:159）的 defined 判定只查 VariableQuery.cs:239 GetVariableValue 的 user/system 真实注册表 + profile 自有变量——单位测试语境下依赖机器注册表与机器环境（违反 hard-boundaries 测试纪律），且本测试从未在任何机器执行过，CI 首跑即红。
- 返修项 1：使 %VAR% defined 判定 hermetic——判定面补齐进程环境（Environment.GetEnvironmentVariable），并保证实现侧不再把「未定义」误报给真实存在的变量；对外语义 = 展开可解析即不警告。
- 返修项 2：测试去机器依赖——该测试改用具名变量（Process 作用域 set/清，遵循测试纪律），钉住 defined/undefined 两态各一条。
- 检查点与完成定义：遵循 handoff 内的完成定义；本返修不扩大原验收边界（error 档与 --strict 契约不动）。
- 版本控制：遵循 WORKFLOW §4.2。
- 验证纪律（CI-only）：遵循 handoff 内的检查点；修复后回报大脑，由大脑推送/触发 CI 取绿（PR #42 长链复跑 + 后续全栈复跑），窗口不本地自证、不自行推送。
- 修复报告落盘 .scratch/architecture-recovery/reports/19-preflight-two-tier-validation-fix2.md：含「声明 → 证据 → 结论」对照表（每条返修项 + 诊断再质检记录）+ 修复后自质检记录。

开工第一句：先复述本票 Blocked by 状态 + 上面必读清单的标题，确认无阻塞后再动手。
