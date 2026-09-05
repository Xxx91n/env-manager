# 复核 24 — CI 用户态隔离（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 24 验收） | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | Pester 步骤重定向 LOCALAPPDATA | build.yml:161-170 env: ENVMANAGER_LOCALAPPDATA: ${{ runner.temp }}\test-user-state | 证实 |
| 2 | 用户态路径 seam 生效 | src/CliRuntime.cs:241-250 LocalAppDataRoot（非空即用 env 值，回退 GetFolderPath）；ProviderHashPath/AppDataDirectory 及 AuditCrypto/AuditCommand/SecretMount/ProfileStorage/SecretProviderManager 五处旁路收敛 | 证实 |
| 3 | xUnit 钉住 set/unset/empty | tests/EnvManager.Engine.Tests/LocalAppDataRedirectTests.cs（3 Fact + 串行 collection + finally 恢复） | 证实 |
| 4 | 隔离纪律文档化 | docs/build-and-release.md:309 小节（两级隔离 + env-block 三守则）；AGENTS.md:161 同步 | 证实 |
| 5 | 运行后断言真实目录未被创建 | build.yml:184-206 Assert user-state isolation（if: always()，run 前不存在而 run 后存在则 throw） | 证实 |
| 附 | Blocked by 17 + CI | merge-base(arch/17, arch/24)=arch/17 tip，叠置正确；唯一暴露它的 CI 是 arch/25 PR——被票 18 编译红连坐，本票自身功能无独立绿证据 | 待全栈 CI |

## 总结论：🕐 待 CI。代码证实、叠置正确；登记 done 的前置 = 18 返修后全栈 CI 绿（含 Assert user-state isolation 步骤）。
> 终验（2026-09-05）：PR #45 全栈绿（run 33963823146），本票 ✅ done。
