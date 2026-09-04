# 15 — Testcontainers L1 矩阵：7 个 Skip 挂载转真实后端

**What to build:** 用 Testcontainers 钉扎本地模拟器（Azurite/LocalStack/Lowkey Vault/Vault dev server），把票 10 契约套件里 7 个外部 provider 的 backend-dependent 断言（round-trip/plaintext-never）从 Skip 转为真跑——无云凭据即闭环"外部 secret provider 注入生效"的端到端验证。

**Blocked by:** None — 可立即开工（01–10 已收口合入 origin/main）。

**Status:** done (brain-reviewed 2026-09-04, reviews/15-secret-provider-testcontainers-l1.md)

- [x] 首步验证：Windows/Linux CI runner 的 Docker 可用性，Linux 容器 runner 优先（本票唯一未就地验证的假设）——本机实测无 Docker（docker/podman/WSL 全缺）；CI 侧 verify-l1 job 首步显式 `docker version`，报告 §1.1
- [x] 镜像钉扎：LocalStack 4.4.0（2.0/3.24 过旧或需 token——4.4.0 为最后免 token 社区版）/ Lowkey Vault 4.0.0-ubi9-minimal（Testcontainers.LowkeyVault 4.14.0 官方模块）+ Vault dev server hashicorp/vault:1.20.4（通用容器，Testcontainers.Vault 404 无官方模块）；全部 NuGet/Docker Hub/Releases 原文核验（报告 §1.2）
- [x] 7 个 backend-dependent 契约断言（round-trip/plaintext-never）从 Skip 转真跑，每 provider 至少一条冒烟——CredMan/SecretStore/sops 本机真跑通过；1Password Decrypt 方向真跑（真实 op CLI 2.39.0 → OpConnectMock，Encrypt 侧 Skip：op item create 拒绝 Connect，live-verified）；容器 Vault+LocalStack 已在 CI ubuntu lane 真跑通过（run 33856417214），Azure/1Password 因模拟器/op 自身缺陷证据跳过（报告 §1.3/§4.1）
- [x] 无云凭据即全绿；dotnet test 全绿进 CI——Debug 与 Release 均 131 通过/20 跳过/0 失败（报告 §1.4）；verify-l1 job 已入 build.yml 且在 CI 绿（run 33856417214，Passed 4/Skipped 9，报告 §4.1）
- [x] 报告落盘 `.scratch/architecture-recovery/reports/15-secret-provider-testcontainers-l1.md`，每条验收附当场命令输出（§1.1–§1.5 + §7 命令总账）
