# 24 — CI 用户态隔离（LOCALAPPDATA 重定向 + 快照语义纪律）

**What to build:** 集成测试在 CI 中以隔离 LOCALAPPDATA 运行，profiles.json 与其它用户态写入落在 job 私有目录；两级隔离纪律（机器态写入靠 fresh-VM 无害、用户态写入不污染 job 后续步骤）与 env-block 快照语义纪律文档化。

**Blocked by:** 17（CI verify 内嵌 CLI 资源 staging）——同 workflow 文件，先绿再隔离。

**Status:** ready-for-agent

- [ ] Pester 集成步骤重定向 LOCALAPPDATA 到 job 私有目录，run 日志可见
- [ ] 测试后机器用户态无污染（run 内验证步骤或自检输出）
- [ ] env-block 快照语义纪律文档化（测试不假设跨进程实时刷新）
- [ ] verify job 在票 17 staging 之上全绿（gh run 证据）
- [ ] docs/build-and-release.md 测试隔离段同步
