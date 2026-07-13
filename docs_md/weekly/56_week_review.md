# 第 56 周回顾

状态：已验收

## 已完成

- 汇总 Week 50-55 边界：本地 job/queue/pipeline/automation/CI artifact 为 0.4.0 Accepted candidate；default-off、IPv4 loopback、只读 daemon/API 为 Preview；scheduler、provider API、control/SSE、auth/TLS、remote/concurrent worker 等继续 Deferred。
- 回归新增 CLI 的只读/写入边界。只读命令继续复用现有 store/DTO/renderer；显式 CI markdown 写入继续复用 workspace guard/no-overwrite；queue/pipeline/automation 执行继续回到现有 command factory、approval/security、trace/session/report/job artifact path。
- 修复 queue JSON list/cleanup corrupt diagnostics 直接序列化的问题。Diagnostic path/id/summary 现在经 renderer 二次 secret redaction；cleanup 仍保留 corrupt/pending/running/unknown record，且不级联删除 job/artifact。
- 加固 Local API Preview：拒绝非 `localhost`/`127.0.0.1` Host，显式限制 request header count/total size，保留 GET-only、body rejection、bounded filters、no-server-header、no-CORS、no-store/nosniff 和只读 route invariant。
- 增加 security regression，覆盖 corrupt job/queue filename secret、CLI/API diagnostic redaction、cleanup 保留、invalid Host、response headers、port-in-use、cancellation 和 listener cleanup。
- 加固默认 packaged smoke：在既有 job/queue/pipeline/automation/CI artifact credential-free 路径上增加 corrupt store diagnostics、redaction、有效记录连续性和 cleanup preservation。
- 更新 security model、known limitations、troubleshooting、quickstart、Local API Preview threat model、CHANGELOG、runtime logging diagnostics、roadmap 与 capability status。
- Capability status 改为 `0.4.0 Candidate Capabilities`，明确 Accepted/Solidified/Preview/Deferred 的含义；不再把 Week 50-56 实现误归入 0.3.3 artifact。

## 验证

- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1147 passed，0 failed，0 skipped。
- 边界定向回归：Job/Queue/Pipeline/Automation/CI/API CLI tests 44 passed；TaskQueue/Local API security tests 21 passed；最终 smoke script contract 1 passed。
- 支撑命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过，刷新 packaged executable 供 Week 56 smoke。该构建不替代 Week 57 deterministic 双构建验收。
- 默认 smoke：清除 `CAICLI_REAL_MODEL_SMOKE` 与 `CAICLI_DAEMON_SMOKE` 后运行 `tools\Invoke-SmokeTests.ps1`。
- 结果：通过，输出 `smoke tests passed`；daemon/API 和 real model smoke 均按设计跳过，默认路径不启动 listener、不需要模型凭据。
- daemon opt-in smoke：设置 `CAICLI_DAEMON_SMOKE=1`、保持 real model opt-in 关闭后运行同一脚本。
- 结果：通过，packaged daemon 仅绑定随机 `127.0.0.1` port；health/jobs/queue 与 `api smoke` 通过；结束后 `caicliProcessCount=0`、`smokeTempDirectoryCount=0`。
- Week 56 smoke 支撑包：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `44422673` bytes，SHA256 `005CE5D6FC18C02672B90236E3646A138D56A944C49A7F040F18BCEBF5ECFE4E`。这不改变 0.3.3 release decision。
- `git diff --check`：通过，仅有仓库既有 line-ending conversion warning。

## 运行时说明

- 默认 smoke 中首次新增 cleanup fixture 使用了无效的 `--older-than-days 0`，命令按参数安全校验返回非零。Fixture 改为合法值 `1` 后从头重跑默认 smoke并通过；产品实现和安全边界未因该失误放宽。
- Queue/job corrupt record diagnostics 是 best-effort inspection，不执行 repair/delete。有效记录继续返回；corrupt 文件留给用户确认 active profile 和关联证据后人工处理。
- API Host/header hardening 是附加请求约束，不是 authentication boundary。Preview 仍不能抵御同一 OS 用户上下文中的恶意本地进程，也不得通过 reverse proxy/port forward 暴露。
- 真实模型 smoke 本周未运行。默认、daemon 和 API acceptance 不依赖模型凭据；provider quality/network/quota 仍是外部 opt-in 风险。

## 风险与限制

- Job/artifact retention、rotation、delete/cleanup 仍 Deferred。Queue cleanup 只删除匹配的旧 terminal queue records，不删除 job、trace、session、report 或 artifact。
- Queue/pipeline/automation 仍是同步、单进程路径，没有 lease/heartbeat、并发 worker、terminated-run recovery、pipeline retry/resume 或 automatic schedule execution。
- Redaction 是 bounded metadata 与 best-effort pattern contract，不是形式化无泄漏证明；本地 path、prompt、trace、session、report 和 artifact pointer 仍应按敏感数据处理。
- Local API/daemon 仍是无认证、无 TLS、same-user 可读的 Preview；control route、SSE、remote bind、service、browser/CORS integration 均 Deferred。
- ASP.NET Core Preview 使 self-contained package 保持约 44.4 MB。Week 57 应记录最终 0.4.0 可重复 size/hash；本周没有在缺少完整回归证据时启用 trimming 或改变 host 模式。

## Release acceptance 前剩余问题

- 将版本元数据和 release artifact 名称从 `0.3.3` 更新为 `0.4.0`，同步 CHANGELOG、installation/quickstart 路径和 final acceptance 文档。
- 连续运行两次 `tools\Build-Release.ps1`，确认 0.4.0 zip size/SHA256 deterministic，并记录 release manifest、runtime、target framework 和 package size decision。
- 对最终 0.4.0 package 重跑 build、full test、默认 credential-free smoke；Local API Preview 变更应同时重跑显式 daemon opt-in smoke。真实模型 smoke继续只在调用方提供凭据并显式 opt-in 时运行。
- 创建 `docs_md/release/final_acceptance_0.4.0.md`，最终核对 Accepted/Preview/Deferred、known limitations、security model、capability status 与 roadmap 一致。
- 当前没有需要 Week 57 补做的实现范围；若 final acceptance 发现新功能诉求，应 Deferred 到后续版本而不是扩大 0.4.0。

## 第 57 周输入

- 只做 0.4.0 release acceptance、版本元数据、deterministic package、最终 smoke/docs consistency 和 release artifact 记录。
- 不新增 automation target、provider integration、daemon/API route、SSE/control、认证平台、远程执行或 0.5.0 垂直能力。
