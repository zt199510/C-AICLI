# 第 51 周回顾

状态：已验收

## 已完成

- 建立本地 task queue v1：`TaskQueueItem`、五态 status、per-run attempt、稳定错误码、redaction contract 和 JSON schema v1。
- 新增用户级 queue store，默认位于 `%USERPROFILE%\.caicli\queue`，并沿用 Week 50 的单记录 JSON、create-new/atomic replace、best-effort diagnostics 和 `CAICLI_USER_PROFILE` 隔离策略。
- 新增 queue CLI：
  - `queue add exec|skill` 只创建 pending request，不立即执行。
  - `queue list/show` 支持 text、`--json` 和 `--output json`。
  - `queue run <id>` 支持 pending/failed item，failed item 再次运行会创建新 attempt。
  - `queue cancel <id>` 仅允许 pending -> canceled。
  - `queue cleanup --status succeeded|failed|canceled --older-than-days <n>` 只删除匹配的旧 terminal queue records。
- `queue run` 将 stored request 展开回同一个 `exec` 或 `skills run` command factory，并强制启用 job recording；不持久化或注入 `--approve` / `--approval`。
- queue run 继续复用现有 approval、workspace guard、dirty workspace checks、shell policy、dangerous-command detection、disabled tools、MCP startup boundary、expert/skill tool boundary、trace/session/report 和 credential path。
- 每次完成的 attempt 保存 job id，queue item 保存 `latestJobId`，对应 job 使用 queue id 作为 label，形成 queue -> job 审计关系。
- Queue request、attempt error/summary 和 diagnostics 使用 bounded secret redaction；不保存 raw referenced content、raw tool arguments、raw secrets、full diff 或 approval overrides。
- cleanup 保留 pending、running、corrupt 和 unknown records，也不删除引用的 jobs/artifacts。
- 新增 18 个 queue DTO/store/CLI/state/redaction/security boundary tests；覆盖 retry、pending-only cancel、corrupt record、cleanup、unexpected delegate failure、approval denial、read-only skill boundary、disabled tools 和 shell policy。
- 默认 smoke 新增 credential-free queue add/list/show/run/cancel/cleanup 与 queue -> job pointer 检查；真实模型 smoke 继续显式 opt-in。
- 更新 capability status、quickstart、known limitations、security model、troubleshooting、CHANGELOG 和 runtime logging diagnostics。

## 验证

- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。

- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1098 passed，0 failed，0 skipped。

- 支撑命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：显式设置 `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"` 后通过，用于刷新 smoke 默认执行的 packaged executable。
- Artifact：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `32419375` bytes，SHA256 `13B373DD19D2916B88B95CCCF953912E331E2684761ABDCDD18226D57C22D583`。

- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`；默认 real model smoke 按设计跳过并提示设置 `CAICLI_REAL_MODEL_SMOKE=1`。

## 运行时说明

- Queue add 是 user-level metadata write，不调用模型、工具或 MCP，也不写 workspace。
- Queue run 不接受或恢复 queued approval override；执行时使用当前配置的 approval/shell/disabled-tool policy。
- `failed` item 可以手动再次 `queue run`，attempt number 递增且每次创建独立 job record。
- `succeeded`、`canceled` 或 `running` item 不能再次 run；cancel 只接受 pending。
- JSON run 输出是 NDJSON：包含 `queue.run.started`、复用 exec/skills events/result 和 `queue.run.completed`。
- 初次 package refresh 未显式设置 `.dotnet` PATH，Windows PowerShell 选中系统 SDK 10.0.301，因 `global.json` 要求 9.0.308 而失败；按周基线补齐 PATH 后同一命令通过。源码 build/test 未受影响。

## 风险

- Queue v1 没有跨进程 lease/heartbeat 或 daemon recovery；进程在 running 状态被终止时，需要人工复核 queue/job 文件，不能自动回收。
- Store transition 通过原子替换与进程内 transition gate 防止本进程内覆写，但本周不承诺并发 worker 或跨进程抢占语义。
- Queue request 按安全策略持久化 redacted task；如果 task 本身包含 secret-like value，后续执行看到的是 `[redacted]`，这会保护本地 store，但可能改变该任务的执行语义。
- Queue cleanup 只删除 queue record，不级联删除 job history、trace、session、report 或其他 artifact；长期 retention/compaction 仍 Deferred。
- Queue run 当前通过 CLI command factory 重新进入既有 exec/skills handler，安全边界一致，但 command-layer coupling 较强；后续 pipeline 应复用 request/execution contract，避免复制第三套 runner。

## Deferred 边界

- 不实现 daemon、scheduler、并发 worker pool、lease/heartbeat、远程 runner 或远程控制。
- 不实现 multi-role pipeline、automation schedule、CI/PR provider、HTTP API 或 SSE。
- 不实现 running cancel/kill、自动重试策略或 job/artifact cleanup。
- 不绕过 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、MCP startup boundary、trace/session/report/job history 或 smoke。

## 第 52 周输入

- Multi-role pipeline 必须建立在 Week 51 queue attempt 和 Week 50 job/artifact truth 上，不创建第二套 task/report/history store。
- Implementer/reviewer/tester role 继续复用现有 expert profiles；reviewer/security 等只读 boundary 不能因 pipeline orchestration 被放宽。
- 每个 role stage 应形成独立可审计 job/attempt pointer，并定义 deterministic aggregate status、stop reason、remaining risks 和最终 artifact/report 合并规则。
- Pipeline fake/offline tests 应覆盖 implement -> review -> test 的 success/failure/short-circuit，并继续验证 approval、dirty workspace、shell policy、disabled tools、MCP startup 和 redaction boundary。
- 默认 smoke 继续 credential-free；真实模型、daemon/API、远程 runner 或 provider integration smoke 仍必须 opt-in，且不应进入 Week 52 范围。
