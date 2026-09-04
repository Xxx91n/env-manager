# 下一波架构/测试心智模型调研（2026-09-03）

> 来源：atomcode 深度调研（source: "atomcode"）· 一次串行 run，~21 查询 / 15 全文核验 / 3 引擎（Exa/Tavily/AnySearch）。
> 本文件是 spec.md Phase 3 与票 11–16 的权威依据；完整调研原文在本次会话 atomcode 输出中，本摘要只保留决策锚点。

## 一句话结论

seam 化已经把引擎变成"可当状态机来测"的靶子。下一步收益最高的是**测试心智模型三件套**：**差分测试（对 Windows 真实语义）→ 状态机模型测试（对写路径核心）→ 变异测试（对红线测试质量）**。架构侧不要再发明新东西——service+launch 分层已被 fnox（2026）独立印证，apply/unapply 的声明式方向正与 DSC v3 合流。真正没人做好的空白（注册表多值原子性、Tauri IPC 级 E2E、Windows 环境变量声明式管理）恰好是本项目可定义范式的位置。

## ROI 排序（测试心智模型层）

| 优先级 | 范式 | 关键证据（已核验） | 落点 |
|---|---|---|---|
| A1 最高 | 差分测试：Windows 为 oracle | setx 1024 截断是行业持续痛点（MS docs + AWS issue #132）；.NET SetEnvironmentVariable 会发 WM_SETTINGCHANGE；REG_EXPAND_SZ 需保留 %VAR% 不预展开 | InMemoryScope 忠实度钉住（现只证明"忠实于自身"，未证明"忠实于 Windows"） |
| A2 | 状态机模型测试 | FsCheck Machine API（标注 Experimental、无 semver）；CsCheck 自称 stateful+parallel 独有 | 写路径核心（rename/change-scope/set/delete/PATH）理想靶场 |
| A3 | 变异测试 | MS Learn 官方指南（勿追 100%）；Stryker 4.14.2（2026-05）活跃，但 v5 需 dotnet10、#3351/#3367 管线摩擦 | 红线代码（VariableRename/VariableChangeScope/ProfileEffective/ProtectionCommand）；本地/PR 辅助，非 CI 硬门 |
| A4 | 并发 + 模糊 | Coyote 1.7.11 约两年前发布（维护放缓，net10 未证实）；SharpFuzz 2026-03 仍有实战（异常白名单纪律） | Coyote 先 spike；SharpFuzz 目标=LenientArgs（唯一解析不可信字节的面） |
| A5 | 快照 + GUI E2E | Verify 比 ApprovalTests 更现代（scrubber/async）；Tauri v2 WebDriver 已 embedded driver + macOS 支持 | CLI help/错误/canary 文案快照；E2E 已由 ADR 0010 覆盖 |

## 架构范式层（三个有工业证据的方向）

- **B1 secret 访问与 env 热重载必须分层**：mise→fnox 拆分（2026）三理由=性能/缓存安全/架构，与本项目"service 管 secret mount + profile launch 一次性注入 + secrets 不进注册表"同构。→ 本项目红线可写成"业界 2026 才重新走到的结论"。
- **B2 声明式 desired-state + what-if + 版本钉扎**：DSC v3.2.0（2026-04-29 GA）新增 --what-if 与 requireVersion；apply/unapply + backup diff/merge + audit ledger 已是"registry 版 DSC"前身。诚实边界：DSC 无一等公民"环境变量"资源——这是本项目的生态位。
- **B3 TxR/TxF 已弃用**：MS 官方《Alternatives to using Transactional NTFS》确认 TxF/TxR 可能在未来 Windows 移除，官方替代清单无注册表多值事务原语。→ "禁止 TxR + 补偿式写入 + 三层锁 + 审计恢复"是官方验证过的唯一可持续路线，应写成 ADR 非目标。

## 票映射（本波 6 票，全部无阻塞，可并行）

| 票 | 范式 | 一句话 |
|---|---|---|
| 11 | A1 差分测试 | InMemoryScope ↔ RegistryScope 同操作序列，终态+广播次数一致 |
| 12 | A2 状态机模型 | CsCheck Machine 对写路径核心随机 1000 步 + 最小反例收缩 |
| 13 | A3 变异测试 | Stryker 收敛到红线代码，本地/PR 辅助闸门 |
| 14 | A5 快照 | Verify 锁定 CLI help/错误/canary 人读契约 |
| 15 | T7 Testcontainers L1 | 7 个 Skip 挂载转 Azurite/LocalStack/Lowkey/Vault dev 真后端 |
| 16 | B3 ADR | 制度化"禁止 TxR/TxF + 补偿式写入" |

## 推迟（不立票，写入 spec Further Notes）

- SharpFuzz LenientArgs 模糊测试（夜间任务，中 ROI）。
- Coyote 并发模型检查（1-2 天 spike 先验 net10 兼容，不直接进路线图）。
- GUI E2E 升级（ADR 0010 已覆盖；生态已迁 embedded driver，仅记录不重立）。
- fnox 式"profiles.json 可安全入库 export"（产品特性，非架构恢复范围）。

## 已知缺口（诚实的不知道）

1. Coyote 在 net10.0 实际可用性未实测（T6 式 spike 即为此设计）。
2. Windows CI runner 上 Testcontainers 的 Docker 可用性未核验（票 15 首步验证）。
3. Stryker.NET v5/dotnet10 runtime 正式时间线有出入（上 CI 前复核 release notes）。
4. PowerToys 源码内 profile 备份/还原测试策略未核验（可作差分测试参照）。
5. 未覆盖闭源 Windows 编辑器（RapidEE/EnvRocket）与 Tauri 2026 下半年 E2E 新进展。

## 关键来源（15/16 全文核验，SO 一例 403 已替代）

MS Learn setx / Win32 Environment Variables / Environment.SetEnvironmentVariable / TxF deprecation / Mutation testing / PowerToys Environment Variables；Tauri v2 WebDriver；FsCheck StatefulTesting；Testcontainers .NET modules；DSC v3.2.0；mise direnv 弃用页；fnox README；Coyote README；SharpFuzz 2026 实战 + 五周年。
