# Secret provider 拆分 + 契约测试 —— 业界范式摘要（atomcode 调研，2026-09-02）

来源：atomcode 深度调研（16 次全文抓取、9 域名、18+ 搜索、多引擎交叉验证，置信度高）。五条独立证据线收敛到同一答案。

## 共识结构

接口/契约放独立核心层；每 provider 一文件/目录；契约测试作为共享"抽象测试基类"，每 provider 只写一个 harness（工厂缝）+ 一个继承子类，自动继承同一套行为断言。

## 五条先例

1. gocloud.dev secrets/drivertest（Go）：契约库与 driver 接口同居，RunConformanceTests(t, HarnessMaker, AsTest)。
2. Dapr components-contrib：secretstores/ 每 provider 一目录 + 集中 conformance 框架 + tests.yml 能力位 + PR-vs-cron CI 二分 + env 凭据注入。
3. EF Core Specification Tests：契约套件打包 NuGet，第三方 provider 继承基类测试类跑同一套回归。
4. WopiHost PR #411（2026-05，.NET/xUnit 原生）：抽象 LockProviderConformanceTests + 工厂缝 + TimeProvider，每 provider 派生 sealed 子类获得同一套断言。
5. Arcus.Security（.NET）：Core + Providers.{AzureKeyVault, ...} 每实现一项目（多 provider 先例，但无共享契约套件）。

## 契约套件核心形态

- 抽象 xUnit 基类：全部 Fact 行为断言（Get/RoundTrip/Delete 后 Get 抛/格式错误稳定报错等），CreateHarness() 为唯一待填缝。
- harness 中立夹具：CreateProviderAsync() / SeedSecretAsync()（绕过 SUT 布数据）/ ReadRawSecretAsync()（绕过 SUT 验落盘）。
- 挂载：每 provider 一个 sealed 子类，连接参数来自 env，本地指向模拟器。
- 反模式：不要用 Theory+MemberData 反射枚举所有实现一次跑（装配/清理不同 + 失败定位靠参数名）。

## 合规闸门（EF ComplianceTest 式）

反射断言每个 ISecretProvider 实现恰好映射一个契约套件子类，新增未挂即构建红。

## 测试分层与 CI

- L0 内存 fake / 真实本地后端（DPAPI）每 PR
- L1 模拟器（Vault dev server / Azurite / Testcontainers）每 PR 有条件
- L2 真实云服务定时/发布管道，凭据 env 注入，不在 PR 跑

## 术语与反方信源

- 不要抄 Martin Fowler "ContractTest"（验证 test double 与真实服务一致）或 Pact consumer-driven contract（服务间契约 + broker，重型，与本场景无关）；要搜 conformance / specification tests。
- 反方：Pact 规模化成本批评（Nemanja Tanasković 2026-03）；in-memory fake 失真批评（ploeh）；MSTest 抽象基类套件方案被作者因可见性/可装配性否定，xUnit 继承派生是正解。

## 落到本项目的裁剪

单 exe CLI 不对外分包 → 契约基类放测试工程内，不做独立 NuGet 库；首轮 DPAPI + 一个 fake provider 挂契约，其余 7 个先 Skip + 配置解析纯函数单测，逐步点亮 L1。
