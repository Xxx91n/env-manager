# Changelog

All notable changes to Env Manager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.14.0](https://github.com/Xxx91n/env-manager/compare/v0.13.0...v0.14.0) (2026-10-31)


### Features

* **service:** join the SCM control protocol so Start=auto runs ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([bb6e8af](https://github.com/Xxx91n/env-manager/commit/bb6e8afdb9ab636b0b69def92b9cab55d2fdcea0))
* **service:** 服务实现 SCM dispatcher，让 Start=auto 真正可运行（[#149](https://github.com/Xxx91n/env-manager/issues/149)） ([e831227](https://github.com/Xxx91n/env-manager/commit/e831227b4f2ee67f179d2ffd34ef70b580c743da))


### Bug Fixes

* **audit,tests,docs:** 审查返修——钉住 ACE 权限与 .tmp 暂存名，路径表述精确化（[#182](https://github.com/Xxx91n/env-manager/issues/182)） ([697d5ec](https://github.com/Xxx91n/env-manager/commit/697d5ecd8982fa50ca2920e4b49182ce88e70b4f))
* **audit:** 生存包临时文件创建即 owner-only DACL（[#182](https://github.com/Xxx91n/env-manager/issues/182)） ([7a4c61c](https://github.com/Xxx91n/env-manager/commit/7a4c61c941623be54d1e29620fd2dbc6e5739550))
* **audit:** 生存包临时文件创建即 owner-only DACL（[#182](https://github.com/Xxx91n/env-manager/issues/182)） ([70f7d11](https://github.com/Xxx91n/env-manager/commit/70f7d1138f12b5c39eee84461a478495dafa1358))
* **ci:** CodeRabbit 第三轮——账本分支收窄为至少三位数字（[#91](https://github.com/Xxx91n/env-manager/issues/91)） ([3fe2317](https://github.com/Xxx91n/env-manager/commit/3fe231756a3bc1a49c7ea622864b73721f5e84b5))
* **ci:** CodeRabbit 返修——checkout 不留凭据、修正测试账本交叉引用（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([a16b397](https://github.com/Xxx91n/env-manager/commit/a16b397a0ed52d5f11e54d1c3874e9c052e6d9d7))
* **ci:** CodeRabbit 返修——下划线前缀粘接形式可见、kill9 备份不可被覆盖（[#91](https://github.com/Xxx91n/env-manager/issues/91)） ([aa5bcb3](https://github.com/Xxx91n/env-manager/commit/aa5bcb3453781dd1eb15b5021d311e9d941b4903))
* **ci:** 低权子进程改用可写环境目录，Background 启动输出入证据（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([23ef903](https://github.com/Xxx91n/env-manager/commit/23ef9034e27a7fe833bfdbf589f68d19cdec1d10))
* **ci:** 低权子进程改用可写环境目录，Background 启动输出入证据（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([ca4a567](https://github.com/Xxx91n/env-manager/commit/ca4a567580a58c82ebbe998e6339bafe7fb9e36c))
* **ci:** 审查返修——OW 归属语义、5.1 兼容与 Background owner 证据（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([feb9a7c](https://github.com/Xxx91n/env-manager/commit/feb9a7cd3e4ddaf03b048e746ad5c326f6feb974))
* **ci:** 把 Background 服务的 tracing 日志收进证据（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([037258e](https://github.com/Xxx91n/env-manager/commit/037258e11b7d29f125a384f48c5233cfe1aa8768))
* **ci:** 第三轮审查——掩码注释、索引行数与低权账户对称（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([01ff50a](https://github.com/Xxx91n/env-manager/commit/01ff50abd87722bc2a8154f56c9b0632b5a6db5e))
* **ci:** 第二轮审查——读回形态 GA→FA、身份矩阵与定向清理（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([7b8cf40](https://github.com/Xxx91n/env-manager/commit/7b8cf406d9a41e3ddeca5a9b240ad90338724d0a))
* **ci:** 观测任务先建证据目录，构建输出落进 build.log（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([cd3abdc](https://github.com/Xxx91n/env-manager/commit/cd3abdc50e188757e0bfdaf2a9550c19108a1fda))
* **cli,service:** rotate 解析 mount 引用并把新信封写回挂载（[#165](https://github.com/Xxx91n/env-manager/issues/165)） ([04b0bb8](https://github.com/Xxx91n/env-manager/commit/04b0bb80d9373ff8f69819f276324fdeeb4b9aba))
* **cli:** service 网关不再持会话写锁，Background rotate 解除自锁（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([62a408b](https://github.com/Xxx91n/env-manager/commit/62a408b2714b96b7829a51c6d754892e931d970c))
* **cli:** service 网关不再持会话写锁，Background rotate 解除自锁（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([5b8d8fc](https://github.com/Xxx91n/env-manager/commit/5b8d8fc12b3e21c4be52a5a41d4e1804bdbb020d))
* **cli:** 写锁分类按 service 动词收窄，只豁免 rotate 与探针（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([15d41f6](https://github.com/Xxx91n/env-manager/commit/15d41f6b15c3d036cdc7b6ba814bea61386e1054))
* **cli:** 按审查修正三锁论证、测试串行化与 E2E 观测（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([a0c22a5](https://github.com/Xxx91n/env-manager/commit/a0c22a59adf987fa7956780f49f20bdb8c03dd68))
* **cli:** 按审查把 service 整体移出写锁，消除 refresh–rotate 锁环（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([0080f5a](https://github.com/Xxx91n/env-manager/commit/0080f5a3d0f1629e2fac3ecc631822c84649ab32))
* **cli:** 控制类 service 动词同样不持锁，网关锁只留给 refresh（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([acc61e3](https://github.com/Xxx91n/env-manager/commit/acc61e3f31842fb1466885c80e2dbf884789c719))
* **cli:** 精确锁归属表述并给 Background rotate 加存在性断言（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([e5bd708](https://github.com/Xxx91n/env-manager/commit/e5bd70848fa44a81ccb64a24046ac6af126c7146))
* **cli:** 锁只留给 refresh，其余 service 动词与未知动词不取锁（[#164](https://github.com/Xxx91n/env-manager/issues/164)） ([f6023bc](https://github.com/Xxx91n/env-manager/commit/f6023bca1cc376be1096fa54a1e731767da16a0f))
* **installer:** drop the duplicate CreateFolder row for LogsDir ([1009bd8](https://github.com/Xxx91n/env-manager/commit/1009bd88372eed939b01c3762abc0cfb24bf2bfd))
* **installer:** gate the upgrade log cleanup in the sequence, not in a component ([d0f22b6](https://github.com/Xxx91n/env-manager/commit/d0f22b69b31816fed744744646c0b99da2c38246))
* **installer:** run the legacy log cleanup on upgrades only ([64a87c4](https://github.com/Xxx91n/env-manager/commit/64a87c4b234b716d7ba1b84311f12721ec21686d))
* **installer:** 安装期日志清理限到升级（[#138](https://github.com/Xxx91n/env-manager/issues/138)） ([6534e1b](https://github.com/Xxx91n/env-manager/commit/6534e1b93e6cbbd8a9f2467faa0ccd7fbf5d53a4))
* **msi-matrix:** keep the script ASCII-only for the BOM lint rule ([117ecdc](https://github.com/Xxx91n/env-manager/commit/117ecdc1247bf3dddd3ca1e4eb55e16188d62e80))
* **msi-matrix:** pass INSTALLDIR to the forced repair in a custom-path cell ([5ce26c3](https://github.com/Xxx91n/env-manager/commit/5ce26c3372182d9729212cf25ef2cc00b58b0a24))
* **scripts:** 交叉校验加锚定下限，枚举/解析失效不再空洞通过（[#183](https://github.com/Xxx91n/env-manager/issues/183) 审查返修） ([aee7295](https://github.com/Xxx91n/env-manager/commit/aee72952d12ca8b337b9017d5d62f9e7f0d4a2cd))
* **scripts:** 审查返修——锚定 partial 声明形态、去重缓存查找、交叉校验覆盖面（[#183](https://github.com/Xxx91n/env-manager/issues/183)） ([9ca4fb3](https://github.com/Xxx91n/env-manager/commit/9ca4fb32ce989bf28f41cd8df712fc0417b1cecf))
* **scripts:** 补回 Resolve-SymbolFiles 的缓存早退（[#183](https://github.com/Xxx91n/env-manager/issues/183) 审查返修） ([5112716](https://github.com/Xxx91n/env-manager/commit/51127169fed83f2ac86b11447b33d72529d7ccf9))
* **scripts:** 门禁扫全顶层文档并认得 partial class 成员（[#183](https://github.com/Xxx91n/env-manager/issues/183)） ([11e1717](https://github.com/Xxx91n/env-manager/commit/11e171769c37f589ddfb84fce3b3577ae17f565a))
* **scripts:** 门禁文档枚举改用 NUL 分隔，非 ASCII 文件名不再被静默漏扫（[#183](https://github.com/Xxx91n/env-manager/issues/183) 审查返修） ([03dd005](https://github.com/Xxx91n/env-manager/commit/03dd005b526a1254d7bd4492ab7da498b0845614))
* **secrets:** rotate 解析 mount 引用并把新信封写回挂载（[#165](https://github.com/Xxx91n/env-manager/issues/165)） ([7c11def](https://github.com/Xxx91n/env-manager/commit/7c11defdaa471a92f3c19820c1448c1761916c5c))
* **secrets:** sops 临时目录创建即收紧 DACL，明文不再随 %TEMP% 继承面外泄（[#178](https://github.com/Xxx91n/env-manager/issues/178)） ([a001b71](https://github.com/Xxx91n/env-manager/commit/a001b71d7149e18ab58c2a61512d3164fbc32521))
* **secrets:** sops 临时目录创建即收紧 DACL（[#178](https://github.com/Xxx91n/env-manager/issues/178)） ([d3f70e3](https://github.com/Xxx91n/env-manager/commit/d3f70e340b5ff0ae63935e7d44b79ecf5994ddbc))
* **secrets:** 按审查把 DACL 并入创建调用，并在记日志前过 scrubber ([bc8a12e](https://github.com/Xxx91n/env-manager/commit/bc8a12e054dbcdb2502373795367cd426c162e49))
* **secrets:** 让 ACL 失败回退真正可达，并钉住文件 ACE 的继承来源（[#178](https://github.com/Xxx91n/env-manager/issues/178) 复审） ([a19df9b](https://github.com/Xxx91n/env-manager/commit/a19df9b0d9548ada6217b3126aafe4565eceae21))
* **service:** bound the stop path inside the SCM wait hint ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([1a1ebf2](https://github.com/Xxx91n/env-manager/commit/1a1ebf27e308493f39b8aea053a72804d301a405))
* **service:** CLI 子进程套 kill-on-close job object，停服务收敛 helper 树（[#160](https://github.com/Xxx91n/env-manager/issues/160)） ([5ea5829](https://github.com/Xxx91n/env-manager/commit/5ea5829e819609d0a1e5bf90ed88a37db947d002))
* **service:** CLI 子进程的 Stdio 取 std 重导出（CI 红 E0603） ([2015afc](https://github.com/Xxx91n/env-manager/commit/2015afcb239102fc437339d5c0fb2230a03a5288))
* **service:** CodeRabbit 返修——reclaim 的 Drop 兜底不再被提前置位跳过（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([018aec3](https://github.com/Xxx91n/env-manager/commit/018aec3a0a1560892f0e05eab1667bb963103063))
* **service:** do not start the body when the SCM rejects RUNNING ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([86a53a7](https://github.com/Xxx91n/env-manager/commit/86a53a71676e1b4bbb1e52b20c8fc67aad8fda78))
* **service:** kill9 备份路径加时间戳，存在即拒绝复制（CodeRabbit 第二轮） ([0a0c5da](https://github.com/Xxx91n/env-manager/commit/0a0c5da811ecfe33a573228092e647635bb1110f))
* **service:** kill9 恢复/清理以 backupCreated 标志为条件（CodeRabbit 第三轮） ([85b3de4](https://github.com/Xxx91n/env-manager/commit/85b3de497db1e30b25a25344a92dc73ca5b9c1b2))
* **service:** make the stop-budget assert compare milliseconds ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([223e911](https://github.com/Xxx91n/env-manager/commit/223e9112981b3f19af5bf44c97b79f9239e380bb))
* **service:** rotate 挂死预算与锁范围收口（[#161](https://github.com/Xxx91n/env-manager/issues/161)） ([9b1ed46](https://github.com/Xxx91n/env-manager/commit/9b1ed46b8596a378dd882b736a2d1165f973050b))
* **service:** serialize SCM status reports and report START_PENDING first ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([8160889](https://github.com/Xxx91n/env-manager/commit/8160889585fea2872fc5bc3348e61462a57740a9))
* **service:** stop re-polling a finished task, restore background order ([#149](https://github.com/Xxx91n/env-manager/issues/149)) ([5d91b1d](https://github.com/Xxx91n/env-manager/commit/5d91b1d8cdd130e3a9871955a9a8567928f190ff))
* **service:** 与 [#178](https://github.com/Xxx91n/env-manager/issues/178) 的 SecretTempDirectory 融合，scratch 根目录内同样带 owner-only DACL（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([cbfbb29](https://github.com/Xxx91n/env-manager/commit/cbfbb29ccc81c670503126577a24b7db82c7d2e0))
* **service:** 停服务不再滞留 sops 明文，临时目录由服务拥有并回收（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([b305068](https://github.com/Xxx91n/env-manager/commit/b305068e3884b00db5be1542a251c7de64faaa75))
* **service:** 停服务不再滞留 sops 明文，临时目录由服务拥有并回收（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([dd7a147](https://github.com/Xxx91n/env-manager/commit/dd7a147ee3a4b77cce79c917672562473942c813))
* **service:** 停服务收敛 CLI 的 helper 树（kill-on-close job object，[#160](https://github.com/Xxx91n/env-manager/issues/160)） ([a1e3d12](https://github.com/Xxx91n/env-manager/commit/a1e3d128f73b31d09f8826b1d91da8c5b02937fe))
* **service:** 停止路径保留已捕获的 stderr，回退行不再随 abort 丢失（[#177](https://github.com/Xxx91n/env-manager/issues/177) CodeRabbit 返修） ([c3a31cc](https://github.com/Xxx91n/env-manager/commit/c3a31cc90344a746c4b39535feaa8ea1ea659101))
* **service:** 停止路径终止并回收 in-flight rotate 子进程（[#153](https://github.com/Xxx91n/env-manager/issues/153)） ([26f1e81](https://github.com/Xxx91n/env-manager/commit/26f1e812b730cf8bc278a6dd1a8eb0fa6d4df896))
* **service:** 停止路径终止并回收 in-flight rotate 子进程（[#153](https://github.com/Xxx91n/env-manager/issues/153)） ([6af678d](https://github.com/Xxx91n/env-manager/commit/6af678d6fc09650dac067416a892aef2dd9e27da))
* **service:** 命名管道在创建时应用声明的 DACL，删除按名无效的 pipe_dacl（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([e4651e1](https://github.com/Xxx91n/env-manager/commit/e4651e19d56782bab2c6aa5ad35cbe3d2352067c))
* **service:** 命名管道在创建时应用声明的 DACL，删除按名无效的 pipe_dacl（[#154](https://github.com/Xxx91n/env-manager/issues/154)） ([3e96378](https://github.com/Xxx91n/env-manager/commit/3e9637827d76d259145656d8c3cf50291c4d9e3b))
* **service:** 复审返修——stderr 入日志先过 scrub_message，补齐 CHANGELOG 与边界表述（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([9b4ce7c](https://github.com/Xxx91n/env-manager/commit/9b4ce7cbca2dd1f70f0328bd87a7cc2c76cabd50))
* **service:** 复审返修——清掉旧结局名、收敛分支与测试接缝（[#161](https://github.com/Xxx91n/env-manager/issues/161) 审查返修） ([b8fa7c0](https://github.com/Xxx91n/env-manager/commit/b8fa7c0a4fd0721c673624ed7da0da1b971860d3))
* **service:** 委托 rotate 增加 15 分钟挂死预算，到期终止并回收（[#161](https://github.com/Xxx91n/env-manager/issues/161)） ([8ab7ec8](https://github.com/Xxx91n/env-manager/commit/8ab7ec8c857992725abe72038054dfa2f7536628))
* **service:** 委托旋转在飞时，另两个 mount 文件写者让路（[#161](https://github.com/Xxx91n/env-manager/issues/161)） ([7855b9b](https://github.com/Xxx91n/env-manager/commit/7855b9b6e053ab5644b80fd9f66f9b4ea606fac9))
* **service:** 审查返修——回退双路留痕、成功路径 stderr 入服务日志、措辞去绝对化（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([89786be](https://github.com/Xxx91n/env-manager/commit/89786bea3b6293df0f44f384d7b07334ddb26ecd))
* **service:** 审查返修——锁测试断言、结局命名与收敛分支（[#161](https://github.com/Xxx91n/env-manager/issues/161) 审查返修） ([460fbe3](https://github.com/Xxx91n/env-manager/commit/460fbe34db17189a56a379b802746e1463e8fa86))
* **service:** 收口返修——陈旧注释、排空放弃留痕与可区分的 scrub 回归用例（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([e36bcc2](https://github.com/Xxx91n/env-manager/commit/e36bcc26d563ebd3cef00ca15d4b8e5946f1b4e3))
* **service:** 终审返修——日志守卫、被打断路径的 stderr 留痕与 scrub 的字符边界（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([fcbf034](https://github.com/Xxx91n/env-manager/commit/fcbf0341e1ca0f9d7d8ed7aa605854f78c00849f))
* **service:** 过明文金丝雀——reconcile.rs 注释不再出现 plaintext（[#177](https://github.com/Xxx91n/env-manager/issues/177)） ([20257a8](https://github.com/Xxx91n/env-manager/commit/20257a8bf8a1dc078d8c9ef62ac094cdcf5a47e8))

## [0.13.0](https://github.com/Xxx91n/env-manager/compare/v0.12.1...v0.13.0) (2026-10-27)


### Features

* **installer:** add a desktop shortcut check box to the MSI wizard ([#136](https://github.com/Xxx91n/env-manager/issues/136)) ([59fad51](https://github.com/Xxx91n/env-manager/commit/59fad519fb52f51bc6f539a36cbcabb2ff6a7644))


### Bug Fixes

* **build:** 补回删裸号时留下的孤儿冒号 ([c9c2ee8](https://github.com/Xxx91n/env-manager/commit/c9c2ee824d55fa488ed9de1f9e8182b95759cc14))
* **ci:** dependabot 安全更新组补 patterns 以过线上 schema 校验 ([#115](https://github.com/Xxx91n/env-manager/issues/115)) ([64df60a](https://github.com/Xxx91n/env-manager/commit/64df60a7605585d8a5f94cb3619236ee8feed49c))
* **ci:** npm audit 门禁按 OpenVEX 豁免口径放行 ([ef64d7f](https://github.com/Xxx91n/env-manager/commit/ef64d7f8081900d21a4e73bd16e8cab901f1fa1d))
* **ci:** npm audit 门禁按 VEX 口径放行（[#105](https://github.com/Xxx91n/env-manager/issues/105)） ([61c7465](https://github.com/Xxx91n/env-manager/commit/61c7465af9e37032205cdd23794cab99fc8b6d33))
* **ci:** release-please 的 tag 自动触发资产构建 ([deba936](https://github.com/Xxx91n/env-manager/commit/deba93653bfa3b6531149a72240387fdff7f65c0))
* **ci:** release-please 的 tag 自动触发资产构建 ([63c8f83](https://github.com/Xxx91n/env-manager/commit/63c8f83ac5ebc73efd2aacc226afb36925706e50))
* **ci:** 修好从来无法解析的 dependabot 自动合并工作流 ([1de9c58](https://github.com/Xxx91n/env-manager/commit/1de9c584763468b4eb755342a79438df7cfdd12c))
* **ci:** 修正 build.yml stryker job 过时的'必红'注释（[#102](https://github.com/Xxx91n/env-manager/issues/102)） ([f1bbb78](https://github.com/Xxx91n/env-manager/commit/f1bbb78b28940df129f09c39cbc360b813fa2d4e))
* **ci:** 修正 build.yml stryker job 过时的'必红'注释（[#102](https://github.com/Xxx91n/env-manager/issues/102)） ([a9eb697](https://github.com/Xxx91n/env-manager/commit/a9eb6972ad0c344f1c4b0990fff6ec1de88f9ac2))
* **ci:** 裸号扫描兼容两段版本号，并加可失败的自检门禁 ([9e39ef5](https://github.com/Xxx91n/env-manager/commit/9e39ef569dbf5dd9d3541a4ca6325d7bd6d8813a))
* **ci:** 裸号门禁认得复数形态，并清掉它一直看不见的四处残留 ([9530e59](https://github.com/Xxx91n/env-manager/commit/9530e596164c03ccc20ef6c2945dc9b5e392c7cc))
* **ci:** 让 dependabot 自动合并门真正轮询并合入 minor/patch ([e70f3b0](https://github.com/Xxx91n/env-manager/commit/e70f3b0c58eec3de309e63a7896e43498e70f60d))
* **release:** latest.json URL 指向 GitHub 归一后的资产名 ([4857c8f](https://github.com/Xxx91n/env-manager/commit/4857c8f55a04e99d3ea5e2fcbb7fb2f0820e300e))
* **release:** latest.json URL 指向 GitHub 归一后的资产名 ([aaf6507](https://github.com/Xxx91n/env-manager/commit/aaf6507d102ff1bdab200e0349f54d768390210d))
* **release:** 用 config 文件同步三个版本载体 ([2387748](https://github.com/Xxx91n/env-manager/commit/238774830405f079f4919239794afdba6dcabce7))
* **review:** 落实 CodeRabbit 的五条修正 ([9b88c01](https://github.com/Xxx91n/env-manager/commit/9b88c0100cd15f5c8b62b26b6c3cdb4e5e1c4dcb))
* **secrets:** 探测预算与真实调用对齐，超时不再冒充“二进制不可用” ([cc9eeba](https://github.com/Xxx91n/env-manager/commit/cc9eebae0e219493d376d3514d3d2aa419958591))

## [0.12.1](https://github.com/Xxx91n/env-manager/compare/v0.12.0...v0.12.1) (2026-09-14)


### Bug Fixes

* **ci:** collect stryker evidence even when break threshold trips (ticket 50) ([f736bc7](https://github.com/Xxx91n/env-manager/commit/f736bc7fda6dab211e0e421260bb4d2a62127e32))
* **ci:** early-exit non-Dependabot PRs and bound required-check wait in auto-merge gate (ticket 48) ([2893cf4](https://github.com/Xxx91n/env-manager/commit/2893cf4fce974676053357a9018ee9b5fe8387a1))
* **ci:** resolve service exe under cargo build.target triple dir (ticket 49) ([8c5940c](https://github.com/Xxx91n/env-manager/commit/8c5940ca72ebb91962549a1fe116f0a8f65d8cb2))
* **ci:** scope portable/cli-only artifacts to their zips - loose runtime files leaked into v0.12.0 release assets (review finding) ([a97b451](https://github.com/Xxx91n/env-manager/commit/a97b451c47a81039240b3323800f4df9babf66e1))

## [Unreleased]

### Fixed
- sops 明文临时目录由服务拥有并回收：停服务打断 rotate 后，服务在被终止的 helper 释放句柄的窗口内删除整棵临时目录树（有限重试），不再无声遗留 `env-manager-sops-*`；回退与删除失败均留痕（#177）

## [0.12.0] - 2026-09-09

Release-infrastructure milestone (architecture-recovery Round 7, tickets 44-45): first release cut from the tag-driven CI pipeline, with release-please taking over subsequent version bumps.

### Added
- Release infrastructure (ticket 44): tag-triggered `build.yml` release job — updater triple (`.msi` + `.msi.zip` + `.msi.zip.sig`), `latest.json` assembled by `scripts/gen-latest-json.mjs` (fail-closed on missing signature), SLSA L2 build provenance attestation
- Tauri updater wiring (ticket 37): `tauri-plugin-updater` with silent update check and in-app download-and-install
- Release automation takeover (ticket 45): `release-please.yml` manifest config — plain `vX.Y.Z` tags (`include-component-in-tag: false`), `.release-please-manifest.json` pinning the released version, `extra-files` syncing the version carriers checked by the release job's version gate; legacy manual `release.yml` retired
- v0.12.0 tag + GitHub Release with the four updater asset classes (.msi, .msi.zip, .msi.zip.sig, latest.json)
- CI hardening (P0): actionlint gate, dependency-review on PRs, Dependabot for GitHub Actions/cargo/npm, `timeout-minutes` on all jobs, `Swatinem/rust-cache` in verify + package jobs
- CI hardening (P1): RustSec audit (src-tauri + service), NuGet vulnerability audit, MSI quiet install/run/uninstall smoke test, release version-consistency gate (tag vs csproj vs tauri.conf vs package.json)

### Fixed
- CI: replace `rustsec/audit-check` (v2.0.0 JSON bug) with `taiki-e/install-action` + `cargo audit`
- CI: MSI smoke probes installed CLI with bare args per build.mjs version-probe protocol
- Tests: replace hardcoded `D:/Aworker` paths with repo-relative resolve; fix trailing-backslash PATH round-trip in snapshot tests
- Frontend: regenerate `package-lock.json` against registry.npmjs.org (npmmirror cache broke `npm ci` in CI)
- Deps: archiver v8 (ESM named exports — use `ZipArchive` class)

## [0.9.30] - 2026-08-27

### Added
- MSI: uninstall shortcut next to the EXE; GUI logs externalized to `%LOCALAPPDATA%\EnvManager\logs`; clean uninstall removes the entire install dir (ADR 0012)
- Brand icons applied to exe, MSI, and installer
- SignPath signing prep: code-signing policy page and artifact metadata alignment

### Fixed
- Release published to GitHub with MSI, portable zip, CLI-only zip, and `SHA256SUMS.txt`

## [0.9.29] - 2026-08-23

### Added
- MSI UI: custom install directory (`WixUI_InstallDir`), `ARPPRODUCTICON`, optional desktop shortcut property (ADR 0011)

## [0.9.28] - 2026-08-23

### Added
- MSI hygiene: per-component `RemoveFile` residue wipe, pinned GUIDs on the three binaries, WiX EOL supply-chain watchdog (ADR 0010)

## [0.9.27] - 2026-08-22

### Fixed
- MSI: replace `sc.exe` custom actions with WiX `util:ServiceConfig` for service stop/config (ADR 0009)

## [0.9.26] - 2026-08-19

### Fixed
- GUI: 5 bugs — path move-down guard, dead-entries source column, z-index layering, radio color-scheme, input focus outline

### Added
- ADR 0006: GUI z-index layering contract
- ADR 0007: Form control color-scheme override

## [0.9.25] - 2026-08-17

### Fixed
- GUI: notes reactive store + WAI-ARIA tooltip, path move new-array reference + focus/highlight, dead-entries reactive disabled state, dark destructive red-400 alignment

## [0.9.24] - 2026-08-16

### Fixed
- GUI: 5 bugs — CSS-only tab indicator, dark mode flash fix, color-scheme, destructive contrast, titlebar sticky

### Added
- 5 new hard boundaries; supersede stale v0.9.20 indicator boundary

## [0.9.23] - 2026-08-16

### Fixed
- GUI: 5 bugs — titlebar bottom truncation, notes async reactivity, path move index, dark mode white flash, dark destructive red

## [0.9.22] - 2026-08-15

### Fixed
- GUI: titlebar fixed + smooth scroll + dark mode toggle + font slider + notes fix + path move bidirectional
- i18n: add missing settings.lightMode to all 10 locales

## [0.9.21] - 2026-08-15

### Added
- UI-AUDIT.md v0.9.21 audit round — 6-color theme + custom titlebar + CSS containment

### Fixed
- themeStyle allow-list was stale — blocked non-slate themes on restart
- titlebar button tooltips + dead i18n key cleanup

## [0.9.20] - 2026-08-15

### Added
- UI design system — dual-axis theme tokens + lucide icons + 275 dark: class migration

### Fixed
- 5 UI bugs (theme style toggle/i18n/layout/jank/indicator) + regression tests
- dedup CSS classes, tokenize 180 residual gray/hardcoded colors

## [0.9.19] - 2026-08-14

### Added
- SWR caching + listProfiles cache + Tab preload + ProtectionPage concurrent

### Fixed
- tab indicator uses transform:translateX instead of left, rAF instead of setTimeout

## [0.9.18] - 2026-08-14

### Fixed
- service rotate CREATE_NO_WINDOW + refresh timestamp format + WebView2 context menu disabled

## [0.9.17] - 2026-08-14

### Fixed
- ProfilePage pathScopes badge + PathEditor reverse index + IPC mount_id snake_case

## [0.9.16] - 2026-08-14

### Added
- ProfileShow source-aware output + path scope badge + ServicePage i18n

## [0.9.14] - 2026-08-13

### Added
- InputDialog replaces window.prompt + sticky-note annotations + Material 3 ARIA tablist

### Fixed
- ProfilePage addVar inputs, note hover preview, tab indicator initial width
- add-var inputs use bind:value (Svelte 4 two-way binding) + tab indicator transition suppression
- i18n locale loader uses static import map instead of runtime path concat
- tauri.conf.json version sync to match csproj/package.json

### Added
- AGENTS.md hard boundary: PATH scope-aware apply/unapply

## [0.9.13] - 2026-08-12

### Added
- Process security hardening: process_guard.rs (crash dialogs, binary hash verify, pagefile protection, process mitigations, module enumeration, authenticode stub)
- 12 unit tests in process_guard::tests

## [0.9.12] - 2026-08-08

### Added
- GitHub release readiness Phase 1 — community health files + README rewrite + CSP hardening + Named Pipe DACL hardening
- Sensitive data redaction architecture — 22-pattern unified scrubbing across CLI/GUI/service tiers

### Fixed
- i18n settings.drTitle path mismatch + .NET 10 runtime fencing
- embed .NET 10 runtime detection in Tauri Rust shell

## [0.9.11] - 2026-08-07

### Fixed
- audit security hardening — atomic writes, path fix, transactional import, lock classification

### Added
- Final alignment — docs sync, security audit, CLI command ref update

## [0.9.10] - 2026-08-07

### Added
- Phase E audit ledger unification — migrate, verify, export, recover

## [0.9.9] - 2026-08-07

### Added
- Versioned schema migration + full-state export/import + GUI DR entry

## [0.9.8] - 2026-08-07

### Added
- Industrial logging backend — tracing+tracing-appender, request_id, service status enum

## [0.9.7] - 2026-08-07

### Fixed
- Service probe fast-fail — 18s to 2s when service down

## [0.9.6] - 2026-08-06

### Added
- ADR 0002: Service Watchdog & Heartbeat (two-layer watchdog: SCM recovery + GUI 30s ping)
- ADR 0003: Version Single Source & CHANGELOG (build.mjs version sync)
- Grill plan: Industrial lifecycle control + pipeline control + package management + doc alignment
- CONTEXT.md: Service Lifecycle domain terms (Watchdog, Heartbeat, SCM Recovery, IPC Connection Health)
- CLI pipe connect retry (3x with 1s backoff) — prevents transient pipe-busy false negatives
- Service ping heartbeat enrichment (uptime_seconds, reconcile_last_run_at, mount health summary)

### Changed
- build.mjs: reads <Version> from env-manager.csproj, syncs to frontend/package.json before every build

### Fixed
- watchdog activation + doc-sync regex + build.yml EOF
- service read/write lock classification — fix GUI page block

## [0.9.5] - 2026-08-02

### Added
- Phase D: cert_bootstrap.rs — Vault AppRole/client cert, Azure SP cert bootstrap
- Service: cert_bootstrap eliminates VAULT_TOKEN and AZURE_CLIENT_SECRET via short-lived token exchange

## [0.9.3] - 2026-08-05

### Fixed
- GUI exit (tray quit / ExitRequested) no longer kills background service
- stop_service Tauri command is the only path that kills the service process

## [0.9.2] - 2026-08-04

### Fixed
- Service CancellationToken + CLI pipe fix
- Log alignment: service logs to env-manager-service.log, frontend to env-manager.log
- Atomic write for gui-settings.json (write_atomic: temp + fsync + rename)
- Process leak: SERVICE_PID tracking + kill+wait anti-zombie
- Tray i18n: pre-load locale messages before localeStore.set to fix English fallback on tray menu
- HistoryPage column resize: rAF-based smooth drag

### Added
- AuditPage.svelte: audit ledger panel moved from Settings to main navigation
- Service manager page in GUI (Start/Stop/Ping/Reload)
- 10-locale i18n for service and audit keys

## [0.9.0] - 2026-08-02

### Added
- Phase B+C: env-manager-service.exe Rust binary (service crate)
- Named pipe IPC (\\.\pipe\EnvManager.Service / .Background)
- CLI service subcommand (thin IPC gateway)
- Reconcile loop: tokio interval 300s periodic full-scan
- WiX installer: ServiceInstall + ServiceConfig DelayedAutoStart
- SecretMount schema v2 (separate secretMount.json file)
- One-shot migration from inline envelopes to mount references

## [0.8.0] - 2026-07-26

### Added
- Phase A: SecretMount schema v2 + one-shot migration
- C# AtomicWriteProfiles with fsync (Flush(true) before File.Move)
- New SecretMount fields: refreshPolicy, refreshIntervalSeconds, bootstrapCertThumbprint

## [0.7.15] - 2026-07-25

### Added
- Build-time CLI version verification (build.mjs probes spawned CLI matches csproj version)
- findCliOutput() prefers net10.0-windows directory

### Fixed
- Stale v0.3.0 CLI binary deployment (lacked path/profile/history/protection commands)

## [0.7.14] - 2026-07-24

### Added
- Cross-platform build orchestrator (scripts/build.mjs) — Windows/Linux/macOS, no hardcoded paths
- Multi-architecture support: x64, x86, arm64
- CI release workflow: workflow_dispatch only (manual trigger, no auto-trigger)
- ZIP artifacts: Env-Manager_{portable|cli-only}_version_arch.zip

## [0.7.13] - 2026-07-23

### Fixed
- history delete classified as WRITE (was READ) — prevents concurrent audit.json mutation race

## [0.7.12] - 2026-07-22

### Added
- pwsh async pipe drain (BeginOutputReadLine/BeginErrorReadLine before WaitForExit) — fixes deadlock
- Locale pre-await + tray i18n race fix (loadLocaleMessages before localeStore.set)
- Staged PATH move + Apply (accumulate local, commit via Apply button)
- Secret reveal / provider-set audit records
- Toggle switch right-bias visual fix
- addCliToPath fail-fast 30s timeout

### Fixed
- Named pipe deadlock: pwsh blocks writing to full pipe while we wait for exit
- Tray menu showing English on Chinese startup

## [0.7.11] - 2026-07-21

### Fixed
- PowerShell SecretManagement canonical module name (Microsoft.PowerShell.SecretStore)
- SOPS env var SOPS_AGE_RECIPIENTS (plural) + no-BOM file write
- AWS SigV4 request headers on HttpRequestMessage (not HttpContent)
- Rust non-zero exit stderr_hint log (scrub secrets, 512 char bound)

## [0.7.10] - 2026-07-20

### Added
- Secret provider errors shown inline (amber banner under provider select)
- i18n locale race eliminated (await applyPersistedLocale before App mount)

## [0.7.9] - 2026-07-19

### Fixed
- setupI18n MUST NOT read localStorage on startup (portable Tauri localStorage unreliable)
- applyPersistedLocale empty-durable stays at default, never seeds from localStorage
- write_gui_setting atomic + fsync + rename (prevents torn writes)
- frontend_log IPC for diagnostic tracing

## [0.7.8] - 2026-07-18

### Added
- Secret provider activation error i18n keys (sopsConfig, opAccounts)
- Secret provider activation error inline display
- Edge-style true-overlay scrollbar (no layout-width track)

### Fixed
- Locale persistence startup read-order (no sync seed before durable IPC read)

## [0.7.7] - 2026-07-17

### Added
- Single-instance GUI (tauri-plugin-single-instance)
- Settings persistence via Rust IPC (read_gui_setting / write_gui_setting)
- History action column reactive $t (pass $t as argument, not close over $store)
- Launch badge i18n (profiles.typeLaunch key)
- Inheritance chain secret propagation (CollectInheritedSecrets)
- Global-inherits-Launch topology rejection (at set time, not just apply time)
- Live CLI inheritance-protection integration test

### Fixed
- Dark mode and font scale not read back on startup (onMount now reads from localStorage)
- Settings Dialog writing to localStorage but App never reading back

## [0.7.6] - 2026-07-16

### Added
- Secret provider activation error i18n (localizeError with provider-specific keys)
- History operation label i18n call-site fix (entry.command, not split)

### Fixed
- HistoryPage falling back to English on locale switch ($t inside function body not reactive)

## [0.7.5] - 2026-07-15

### Added
- PowerShell SecretManagement preflight (EnsureSecretManagementAvailable + EnsureVaultRegistered)
- CLIXML stderr stripper
- Secret provider activation preflight (probe before commit)
- Non-launch profile secret rejection (CLI side)
- Inheritance cycle / self-inheritance rejection
- Profile type filter (GUI segmented control)
- Add-var / add-path GUI input validation
- History page operation label full-command-first lookup
- GUI DPI-aware glyph rendering

## [0.7.4] - 2026-07-14

### Added
- Provider-change confirmation modal (prevent silent re-encryption-provider swap)
- PowerShell -EncodedCommand (eliminate shell quoting)
- Regular/secret variable display split in ProfilePage
- Global profiles cannot hold secrets (decision)
- Launch-target / Vault CLI error i18n (localizeError)
- Microsoft YaHei UI font (CJK Windows)
- HistoryPage column-resize highlight fix (col-resizing class pin)
- Clone-from-existing combobox (self-rendered, not native select)

## [0.7.3] - 2026-07-13

### Added
- 1Password provider (op binary, CREATE_NO_WINDOW, 30s timeout)
- AWS Secrets Manager provider (SigV4 signed requests)
- GUI Secret Provider Selector (dynamic from CLI output, no hardcoded list)

## [0.7.2] - 2026-07-12

### Added
- SOPS provider (age/PGP/AWS KMS/Azure/GCP/Vault decryptors)
- Azure Key Vault provider (REST API, managed identity / SP auth)

## [0.7.1] - 2026-07-11

### Added
- Launch profiles + DPAPI secrets (profile launch spawns target with env_clear + inject)
- Profile variable/PATH scope selector (--scope user|system)
- Profile creation with --type launch --target <exe>

### Fixed
- CommonProgramW6432 protection gap (was missing from protection.defaults.json)

## [0.7.0] - 2026-07-10

### Added
- DPAPI-CurrentUser encryption for secret values on disk
- Secrets never applied to registry (IsProfileApplicable rejects secret-bearing profiles)
- Profile audit history
