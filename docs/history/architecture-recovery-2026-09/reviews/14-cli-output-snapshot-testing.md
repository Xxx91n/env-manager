# 复核报告 — 票 14：CLI 输出快照化（Verify）

日期：2026-09-04 · 复核方式：独立子代理只读取证 + 大脑会话当场复跑测试门 · 结论：✅ 可验收

## 声明 → 证据 → 结论

| 声明（子窗口报告） | 证据（仓库实物） | 结论 |
|---|---|---|
| CliOutputSnapshotTests.cs + 17 个 .verified.txt | 文件存在（448 行）；`ls *.verified.txt` = 17，文件名与快照清单逐一对上（help 2 / stdout 2 / 错误 12 / canary 1） | 属实 |
| csproj 含 Verify.Xunit | csproj:23 `<PackageReference Include="Verify.Xunit" Version="31.12.5" />`（带 issue-14 注释，已在提交 4d536e8 内） | 属实 |
| 快照覆盖 help/错误/canary | 抽查：Help_MainHelpText 完整横幅；ProfileShow_MasksSecretValueAsEncrypted 断言 `"value":"<encrypted>"` 无明文；Rename_SourceMissing 固定文案 | 属实 |
| scrubber 清易变字段 | Scrub() 覆盖版本行/GUID/审计 id/RFC3339 时间戳；无 PID 正则（报告已如实声明"当前输出面无 PID 字段"） | 属实（诚实边界） |
| i18n 每 locale 全键渲染快照 | translations.test.ts 引入 intl-messageformat 逐 leaf 渲染 + toMatchSnapshot；snap 文件 exports 计 10（ar/de/en/es/fr/ja/ko/pt/ru/zh 齐全） | 属实（两处数字漂移见附注） |
| 未改 build.yml | `git show 4d536e8 --name-only` 无 build.yml；提交文件集与"并行票隔离声明"一致 | 属实 |

## 大脑当场复跑

- `dotnet test -c Release` → 131 通过 / 20 跳过 / 0 失败（快照测试全绿，17 快照基线已入库）。
- `npx vitest run`（frontend/）→ 40 文件 **430 通过**（含 i18n 快照测试）。

## 附注（数字漂移，无实质影响）

- 报告称"439 叶子 key / 236,899 bytes"；实测 en.json 叶子 key=**456**、snap 文件=**276,727 bytes**。核心声明成立，报告两处统计需更正。
- `<revealed>` 无独立快照、无 PID scrub，均为报告中已如实披露的边界。

## 结论

6 项声明全部成立。✅ 可验收。
