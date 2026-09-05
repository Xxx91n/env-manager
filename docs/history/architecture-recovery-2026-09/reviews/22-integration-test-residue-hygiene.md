# 复核 22 — 集成测试残留卫生（2026-09-05 大脑）

## 声明 → 证据 → 结论

| # | 声明（issue 22 验收） | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | 对账块残留归零断言 | scripts/test-with-restore.ps1:557-573（非 EM_TEST_ 前缀 → registry-foreign-drift 失败）、:586-599（RESIDUE-ZERO 断言逐名列出） | 证实 |
| 2 | 残留自检命令 | scripts/check-test-residue.ps1（86 行，枚举 HKCU/HKLM + profiles.json 中 EM_TEST_*，exit 0=clean / 1=found） | 证实 |
| 3 | 用户自清命令文档化 | docs/build-and-release.md:175 小节 + :195/:201-202（Remove-ItemProperty / reg delete 两条自清命令） | 证实 |
| 4 | docs 同步 | docs/build-and-release.md 测试段、AGENTS.md 同步（kko:w/x） | 证实 |
| 5 | 本票不动用户机器 | 报告自述一致；无反向证据 | 证实 |
| 附 | CI 验证 | 分支未推送、零 CI run（gh run list 为空）；脚本级改动需 Pester 套件实跑证据 | 待全栈 CI |

## 总结论：🕐 待 CI。代码全部证实；登记 done 的前置 = 全栈 CI verify 绿（Pester 四套件实跑）。

## 联合返修复核（2026-09-05 大脑，票 22+24）

| # | 声明（fix 报告） | 证据（仓库实物） | 结论 |
|---|---|---|---|
| 1 | 根因 = set 对已存在不同值且无 --overwrite 退出 1 | src/VariableWrite.cs:197-199：existing != null && existing != args[2] && !args.Contains("--overwrite") → ArgError("…use --overwrite")；与报告引文逐字一致 | 证实 |
| 2 | 三条大脑事实再质检（含对我事实③的修正） | 报告§一表：①/② 属实；③ 修正为「全库唯一真实注册表写入者是 harness round-trip 自身，预存属镜像谱系残留（未证部分如实标注）」——rg 全库核验口径正确 | 证实（且修正合理） |
| 3 | 返修 1：Invoke-Cli 失败保留并打印 stderr | scripts/test-with-restore.ps1:267-283：临时文件重定向 + exit≠0 红字打印（空 stderr 有 fallback）+ finally 清理 | 证实 |
| 4 | 返修 3：round-trip 改带戳名 | :463-477：$roundTripName = "EM_TEST_RT_$Stamp"，set/get/delete 全用；residue-zero 断言区逐字节零改动（vxk diff 唯一 residue 命中行是新增注释） | 证实 |
| 5 | 提交面 = 仅 harness 一文件 +36/−8 | git show f32ce6c：scripts/test-with-restore.ps1 1 file，36+/8−；分支 arch/ci-integration-first-run-fix 栈顶 | 证实 |
| 6 | scope 延伸（toggle 内 list 2>$null 改 2>&1 捕获） | hunk @@ -469,+495 覆盖 toggle 测试区，同诊断模式 | 属实 → **大脑追认**（同文件同模式，不另 revert） |
| 7 | CI 复跑 | PR #44（head=fix 分支，全栈内容）run 33961276094 复跑中 | 待绿后终验 |

**联合返修复核结论：✅ 代码层通过（含 scope 延伸追认）；登记 done 的最终前置 = PR #44 全绿。**
> 终验（2026-09-05）：PR #45 全栈绿（run 33963823146），本票 ✅ done。
