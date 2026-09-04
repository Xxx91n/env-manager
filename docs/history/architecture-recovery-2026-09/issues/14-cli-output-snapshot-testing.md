# 14 — CLI 输出快照化（Verify：help / 错误文案 / canary 输出）

**What to build:** 用 Verify.Xunit 把 CLI 的 help 文本、各命令 stdout、错误文案、canary 脱敏输出（<encrypted>/<revealed>）快照锁定，scrubber 清除易变字段，i18n 每 locale 全键渲染快照——形成与 IPC golden 互补的"人读契约"层。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/14-cli-output-snapshot-testing.md)

- [x] 引入 Verify.Xunit；对 help 文本、各命令 stdout、错误文案、canary 输出（<encrypted>/<revealed>）建立快照 — Verify.Xunit 31.12.5 已引入（csproj 独立 ItemGroup）；17 份 .verified.txt（help 2 / stdout 2 / 错误文案 12 / canary <encrypted> 1）首跑接受、二跑零漂移；证据：reports/14-cli-output-snapshot-testing.md「验收项逐条核验」§1 与快照清单
- [x] scrubber 清除 PID/时间戳等易变字段；任何 user-facing 文案改动在 diff 审阅里显式出现 — Scrub() 规范化版本行/GUID/32-hex 审计id/RFC3339 时间戳（当前 CLI 输出面无 PID 字段）；17 份 received 逐份审阅无易变字段泄漏；Scrubber_GuidTimestampAndVersion_AreNormalized 自检锁定 <encrypted>/<revealed>/<redacted> 存活、普通文案不被吞；证据：reports/14-cli-output-snapshot-testing.md §2
- [x] i18n：每 locale 全键渲染快照（强化现有 translations.test）— 递归 flatten 439 叶子 key + ICU placeholder 逐 key 一致性硬断言 + intl-messageformat 全键渲染 gate；10 locale 各一份渲染快照（__snapshots__/translations.test.ts.snap，236,899 bytes）；vitest 430 passed，CI=1 二跑匹配；证据：reports/14-cli-output-snapshot-testing.md §3
- [x] dotnet test / vitest 全绿；快照进 CI — dotnet test -c Release：125 passed / 0 failed / 25 skipped（150 total）；npx vitest run（CI=1）：40 files / 430 tests 全绿；build.yml 未改，快照随提交被现有 verify job 验证；dotnet build -c Release 0 警 0 错、git diff --check 干净、build.mjs CLI 产物 exit 0；证据：reports/14-cli-output-snapshot-testing.md §4
- [x] 报告落盘 `.scratch/architecture-recovery/reports/14-cli-output-snapshot-testing.md`，每条验收附当场命令输出 — 报告已落盘（5,970 bytes，11 headings，每条验收附当场命令输出与测试计数）；提交 4d536e8 于分支 arch/14-cli-output-snapshot-testing（WORKFLOW §4.2，未 push）
