# 第 57 周回顾

状态：已验收

## Release Decision

CLI `0.4.0` 于 2026-07-14 验收通过，作为当前本地工程自动化平台 release line。Accepted 范围包括 Week 50-56 的本地 job history/artifact index、手动 task queue/run control、固定顺序 multi-role pipeline、workspace-local automation validate/plan/dry-run/manual 和 provider-neutral CI artifacts。

Local API/daemon 继续保持 default-off、IPv4 loopback-only、read-only Preview。Background scheduler、concurrent/remote worker、provider API、API control/SSE、authentication/TLS、remote bind、job/artifact retention/delete 和其他列出的 Deferred 能力未进入 Accepted 范围。

## 已完成

- 汇总 Week 50-56 review：七周均为“已验收”，没有遗留实现范围；Week 57 只完成 release acceptance、文档一致性和一个阻塞级版本测试修正。
- 将 `Directory.Build.props` 的 Version/AssemblyVersion/FileVersion 更新到 `0.4.0`。
- 更新 CHANGELOG、installation、configuration、quickstart、security model、known limitations、capability status、troubleshooting 和 roadmap，统一 `0.4.0` release 路径并保持 Accepted/Preview/Deferred 边界。
- 创建 `docs_md/release/final_acceptance_0.4.0.md`。
- 修正 `ReleaseBuildScriptTests.Repository_version_metadata_is_defined_for_release_artifacts` 中遗留的 `<Version>0.3.3</Version>` 发布断言；没有修改运行时或安全边界。
- 连续两次生成 deterministic `artifacts/release/caicli-0.4.0-win-x64.zip`。

## 验证

- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- 结果：最终通过，0 warnings，0 errors。版本测试修正后再次构建同样通过。

- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：最终通过，1147 passed，0 failed，0 skipped。
- 初次运行：1146 passed、1 failed；唯一失败是发布测试仍硬编码 `0.3.3`。更新为 `0.4.0` 后，定向 `ReleaseBuildScriptTests` 6/6 通过，随后全量 1147/1147 通过。

- 命令：连续两次 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：两次均生成 size `44422661` bytes、SHA256 `FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`，deterministic 验证通过。

- 默认 smoke：显式移除 `CAICLI_REAL_MODEL_SMOKE` 与 `CAICLI_DAEMON_SMOKE` 后运行 `tools\Invoke-SmokeTests.ps1`。
- 结果：通过，输出 `smoke tests passed`；daemon/API 和 real model smoke 均按设计跳过，默认路径不需要模型凭据且不启动 listener。

- daemon/API opt-in smoke：设置 `CAICLI_DAEMON_SMOKE=1`、保持 `CAICLI_REAL_MODEL_SMOKE` 关闭后运行同一脚本。
- 结果：通过，输出 `smoke tests passed`；真实模型 smoke 按设计跳过。最终清理复核为 `caicli` process count `0`、`caicli-smoke-*` temp directory count `0`。

- 命令：`artifacts\release\caicli-0.4.0-win-x64\caicli.exe version`
- 结果：输出 `caicli 0.4.0`、`target framework: net9.0`、`release runtime: win-x64`。

- 命令：`Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.4.0-win-x64.zip`
- 结果：`FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`。

## 运行时说明

- 默认 smoke 仍是 credential-free；脚本会隔离用户 profile 并清除模型 key/model，不因调用方环境已有凭据而运行真实模型。
- Real model smoke 本周未运行。它继续要求 `CAICLI_REAL_MODEL_SMOKE=1`、`OPENAI_API_KEY` 和 `OPENAI_MODEL` 三者同时存在。
- Daemon/API smoke 只通过独立的 `CAICLI_DAEMON_SMOKE=1` 显式 opt-in 运行；这不改变 Preview 的 default-off、loopback-only、read-only 状态。
- Release manifest 报告 version/builtFromVersion `0.4.0`、runtime `win-x64`、target framework `net9.0` 和 self-contained single-file artifact。

## 风险与限制

- ASP.NET Core Local API Preview 使 self-contained zip 保持约 44.4 MB；本周未引入 trimming 或改变 host 模式。
- Job/artifact retention、rotation、delete、cleanup 和 remote/shared store 仍 Deferred；queue cleanup 不级联删除 job/artifact。
- Queue/pipeline/automation 仍为同步、单进程路径，没有 lease/heartbeat、concurrent worker、terminated-run recovery、pipeline retry/resume 或 automatic schedule execution。
- Redaction 是 bounded metadata 与 best-effort pattern contract，不是形式化无泄漏证明；本地 path、trace、session、report 和 artifact pointer 仍应按敏感数据处理。
- Local API/daemon 仍无 authentication/TLS/same-user isolation，不得通过 reverse proxy 或 port forward 暴露。
- 真实 provider 的质量、网络、配额和多角色文本衔接没有纳入 credential-free release acceptance。

## 后续输入

- 后续版本应从已验收的 `0.4.0` release line 开始；新增 scheduler、provider integration、control/SSE、认证平台、远程执行或垂直工具链必须作为新范围单独计划和验收。
