# Round 4 收尾补强调研（2026-09-05）

> 来源：atomcode 深度调研（source: "atomcode"）· 一次串行 run，19 搜索（Exa 6 / Tavily 5 / AnySearch 8）/ 14 全文核验 / 3 引擎。
> 本文件是 spec.md Phase 4 与票 17–25 的权威依据；完整调研原文在 atomcode 输出（ctx 已索引），本摘要只保留决策锚点。

## 一句话结论

收尾阶段最值得补的不是新测试范式，而是把已有资产变成反馈回路：**幸存变异分诊**（零新增工具链，让 Stryker 绿灯重新可信）→ **夜间 CLI 解析模糊**（SharpFuzz，真实 DoS 前车之鉴）→ **CI 隔离/flake 纪律固化** → **预检验证两级降级**（低成本产品决策）。其中「CI 内嵌原生二进制资源的构建依赖」是当下每次 push 红 CI 的直接根因，属流水线可靠性第一优先项。

## 四类候选结论（决策锚点）

### A. 桌面 CI 流水线可靠性

- 隔离基线 = 每 job 全新 VM + Windows 管理员/UAC 关闭（GitHub Docs）：机器态写入无害、用户态写入污染自己 job 后续步骤 → LOCALAPPDATA 重定向是用户态隔离的正确形态。
- env-block 是快照不是实时视图：新进程继承创建者进程的 env-block，注册表广播只通知 shell/Explorer；测试与 CI 步骤不得假设跨进程实时刷新（自托管 runner 第一 flake 源，actions/runner #2540）。
- flake 治理成熟形态 = 隔离仓注册表 + SLA + 重试预算；盲目全量重试在真实回归时放大 170% 延迟成本（Mill）；Luo et al. FSE'14 异步等待类 flake 占 45%。
- 本项根因修复 = verify job 在 cargo 测试前置步骤把 CLI 发布产物 staging 到 Tauri 声明的资源目录（tauri.conf bundle.resources 五文件）。

### B. CLI 解析器模糊测试

- 真实缺陷证据：clap #6255 用 10 字节输入触发 >2GB 分配 OOM（Critical DoS）——环境/参数解析器确为不受信输入高危面。
- SharpFuzz 在 .NET/Windows 成熟：libFuzzer 驱动 + 微软 MORSE 开源 Windows 支持 + 异常二分纪律（Format/Argument/Overflow 吞掉；NRE/IndexOutOfRange/OOM/StackOverflow/AV 当 crash）+ .NET 8+ 发布产物剥 ReadyToRun。
- 单仓库落地形态 = corpus 入库 + 每 PR 短跑 5–10min + 夜间长跑；收益 = 覆盖率缺口 × 解析面大小 × corpus 养成时长，与既有差分/状态机/快照（语义层）正交。
- 时机：分诊清理完「无覆盖/弱断言」幸存者后启动，避免重复投资（票 25 因此 Blocked by 18）。

### C. 变异测试幸存变异分诊

- Stryker 输出六类：Killed/Survived/No Coverage/Timeout/Runtime/Compile Error；分诊 = Survived 与 No Coverage 分开处置（无覆盖 = 测试没执行到该行；有覆盖 = 断言弱或等价）。
- 不追 100%：FSE'14 约 23% 等价变异；arXiv 2404.09241：人工创建变异 <10% 等价且近 2/3 开发者无法准确识别等价变异 → 结构化登记（判定 + 理由入库），avoid（算子调优）最便宜、detect（LLM）次之、人工 suggest 最贵且不可靠。
- 治理成熟形态 = 阈值分级 + 模块分算 + 趋势 + incremental；OneUptime 排序：先修边界条件幸存者。本项目缺口 = 幸存者登记 + 低分模块曝光 + 趋势记录三件套（stryker-config.json 已有 85/70/60 + ignore string/logical）。

- 本轮分诊基线（大脑 2026-09-04 当场重跑，较票 13 报告 76/94 增长源于测试套件 131 增长）：96 受测 / 78 kill / 14 survived / 4 timeout / 40.00%；其中缺失断言类幸存者 16 条（横切登记来源，spec Phase 4 Problem Statement 同源引用）。

### D. 配置预检验证硬阻断 → 告警

- 工业范式 = MongoDB validationAction（error/warn/errorAndLog）+ warn-first 灰度收遥测 + 版本/日历驱动升级 + 显式开关保 CI 纪律。
- 本项目边界：数据破坏/半写状态（32767 截断、变量名含 =、受保护变量、elevation 缺失）保持 error；「可疑但可安全执行」（展开含未定义 %VAR%、路径条目陈旧/悬空 launch target）降 warn + 结构化报告 + 显式 --strict 才红。
- 唯一涉及用户可感知行为：退出码 0/1 纪律需增加 2=warn 全链文档化。

## 推荐落地顺序（本波票映射）

| 票 | 范式 | 一句话 |
|---|---|---|
| 17 | A（根因修复） | verify job 前置 staging CLI 产物，CI 每次 push 变绿 |
| 18 | C | 幸存者分诊 + 登记 + 模块化报告，不追 100% |
| 19 | D | 预检验证两级（error/warn）+ --strict + 退出码 2 |
| 24 | A（隔离纪律） | 集成测试 LOCALAPPDATA 重定向（Blocked by 17） |
| 25 | B | SharpFuzz 夜间模糊 + corpus 入库（Blocked by 18） |

（20–23 为横切登记 backlog 立票：--help 解析、CliRuntime 拆出、残留卫生、docs canary/golden 段。）

## 推迟（不立票）

- flake 隔离仓注册表全套（「一组流程治理」，已落地一半；待真实 flake 出现再立）。
- Coyote 并发 spike、GUI E2E 升级、fnox 式 export（沿用 Phase 3 推迟）。
- 变异等价 LLM 自动检测（detect 方向待工具成熟）。

## 信息缺口

- ScienceDirect 一篇被拦截（已用 PIT/Stryker/arXiv 补位）；等价变异占比文献冲突（23% vs <10%）按「不追 100%」处理；自托管 runner 语义仅文档核验。

## 关键来源（14 全文核验）

GitHub Docs（runner 隔离/管理员语义）；actions/runner #2540；clap #6255 + OSS-Fuzz；SharpFuzz 五周年 + Objektkultur Windows 全流程 + 2026-03 实践文；PIT 官方文档 + Stryker 文档/issue；FSE'14（等价变异 landmark）；arXiv 2404.09241；ACM'24 LLM 等价检测；MongoDB validationAction 文档；Gradle/dbt 弃用警告生命周期；Luo et al. FSE'14 + Google flaky 数据 + tenki.cloud quarantine + Mill 重试数学。
