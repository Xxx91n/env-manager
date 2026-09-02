# 09 — SecretProvider.cs 按 provider 拆模块（一 provider 一文件，行为零变化）

**What to build:** src/SecretProvider.cs（约 1900 行，8 个 ISecretProvider 实现 + SecretEnvelope + JSON 序列化上下文 + SecretProviderManager）拆为：接口/信封/管理器各归其文件，8 个 provider 各一文件；"SecretProvider.cs" 单文件形态退役（类型名保留）。行为零变化，全部测试与集成门绿。

**Blocked by:** None — 可立即开工（架构恢复 01–08 已收口合入 origin/main）。

**Status:** done

- [x] SecretProvider.cs 文件删除；ISecretProvider / SecretEnvelope / JSON 上下文 / SecretProviderManager / 8 个 provider 一符号一文件
- [x] "SecretProvider.cs" 文件名活引用清零（rg 校验 src/docs/frontend/src/AGENTS.md，含前端门禁测试的读文件路径）
- [x] 行为零变化：dotnet test 86/86、vitest 398/398、run-ci-tests 四套件绿（跑前刷新 release/cli-only 产物）
- [x] 引用点同步：AGENTS.md 结构树、docs 活指针、hard-boundaries.md、前端 4 个门禁测试文件
- [x] codegraph sync
