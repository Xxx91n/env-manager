# 10 — SecretProvider 契约测试套件（抽象基类 + harness 缝 + 合规闸门）

**What to build:** 在 xUnit 工程落地共享契约测试套件：抽象 SecretProviderContractTests（断言只经 harness 表达）+ 每 provider 一个 sealed 挂载子类；DPAPI 作为真实本地后端 L0 全绿；其余 provider 在无可用后端时显式 Skip 并附理由，配置解析等纯函数路径有单测；反射合规闸门保证每个 ISecretProvider 实现恰好一个契约子类。

**Blocked by:** 09

**Status:** done

- [ ] 抽象契约基类含核心行为断言集（fail-closed 解密、往返、格式错误稳定报错、明文不落日志），只经 harness 表达
- [ ] DPAPI 契约子类全绿（L0 真实后端）
- [ ] 其余 7 provider 各有契约子类（Skip 带理由）或实现特有纯函数单测
- [ ] 合规闸门测试：每个 ISecretProvider 实现恰好映射一个契约子类，新增未挂即红
- [ ] 全部测试门绿；L0/L1/L2 分层记录入 docs
