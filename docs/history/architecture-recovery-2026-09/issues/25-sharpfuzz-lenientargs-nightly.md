# 25 — SharpFuzz 夜间模糊（参数解析面 + corpus 入库）

**What to build:** SharpFuzz + libFuzzer harness 覆盖参数解析面（ArgTokenizer / dispatcher 不受信输入），异常二分纪律（预期异常吞、真 bug 类 crash）；种子 corpus 入库；每 PR 短跑 5–10min + 夜间长跑 workflow。.NET 10 发布产物 ReadyToRun 前提验证为首步。

**Blocked by:** 18（变异测试幸存者分诊）——调研结论：分诊清理完「无覆盖/弱断言」幸存者后启动，避免重复投资。

**Status:** done

- [x] harness 落地且异常二分纪律正确（Format/Argument/Overflow 吞；NRE/IndexOutOfRange/OOM/StackOverflow/AV 当 crash）— tests/EnvManager.Fuzz/Program.cs：Tokenize/WasArgsCorruptedByTrailingBackslashQuote/IsWriteInvocationForFuzz 三面，catch 仅 Format/Argument/Overflow，其余逃逸成 libFuzzer crash；StackOverflow 进程死即 crash；OOM 由 -rss_limit_mb=4096 兜底
- [x] 种子 corpus 入库（可复现初始语料）— tests/EnvManager.Fuzz/Corpus/ 27 个字节级验证种子（尾反斜杠+引号、反斜杠串、未闭合引号、嵌入 flag、控制字符、嵌入 NUL、空输入等）
- [x] 夜间 workflow 定义（cron）且不阻塞 PR；PR 短跑形态落定 — .github/workflows/fuzz.yml：cron '30 18 * * *' 夜间 1800s 红即信号；pull_request 短跑 300s + continue-on-error 不阻塞；workflow_dispatch 可覆写时长
- [x] .NET 10 发布产物 ReadyToRun 前提验证结论记录 — reports/25-sharpfuzz-lenientargs-nightly.md §R2R（仓库零处 PublishReadyToRun；build.mjs framework-dependent publish；harness csproj 双保险 false + CI -p 再断言）
- [x] 首次夜间跑（CI 触发）输出含运行时长与发现数（0 发现也要有证据）— CI run 33943975560（PR #42 draft，arch/25 分支 f3b664a）：**`Done 6496557 runs in 301 second(s)`、`libFuzzer exit=0 crash-artifacts=0`**、stat::new_units_added=1454、average_exec_per_sec=21583、peak_rss_mb=29；种子冒烟 + 插桩 + 发布全链路绿；artifact fuzz-results-33943975560（76KB）；driver SHA256 17AF5B3F…50BF 已回填 fuzz.yml 硬 pin（提交 srk/bfb9c6e）。夜间 1800s 形态的 dispatch 需 fuzz.yml 落入 main（GitHub 平台约束：workflow_dispatch/schedule 仅默认分支可寻址，404 实证于报告），栈合并后 cron 自动接管
