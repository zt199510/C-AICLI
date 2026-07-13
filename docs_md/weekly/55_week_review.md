# 第 55 周回顾

状态：已验收

## 已完成

- 完成 Local API / daemon Preview threat model 与默认关闭策略。普通 CLI 不启动 listener、worker、scheduler 或后台进程；`daemon start` 必须显式传 `--preview`。
- 定义 route contract v1 与 `api routes --output text|json`：仅包含 `GET /v1/health`、jobs list/show、queue list/show 五条只读路由。控制路由和 SSE 明确 Deferred。
- 实现 `daemon doctor --output text|json`、`daemon start --preview --bind localhost|127.0.0.1 --port <port>` 与 `api smoke --port <port>`。bind 输入统一规范化为 `127.0.0.1`；`0.0.0.0`、wildcard、IPv6、hostname、LAN/public 地址在 snapshot/server startup 前稳定拒绝。
- daemon host 复用 ASP.NET Core/Kestrel，仅监听 IPv4 loopback，关闭 server header，限制连接、request header timeout 和 request body size。API 只接受 GET；request body、非法 limit/status、未知 route 和 unsupported method 返回 bounded JSON error。
- jobs/queue 路由直接复用 `JobRecordStore`、`TaskQueueStore`、`JobsJsonRenderer` 和 `TaskQueueJsonRenderer`。没有第二套 job/report truth，也不创建 model client、tool registry、MCP connection、queue/job/session/trace state，不运行 shell/patch，不写 workspace，不接受 approval/config override，不读取 artifact/trace/session/report 文件内容。
- HTTP 集成测试覆盖真实 loopback listener、health、jobs/queue list/show、secret-like metadata redaction、limit/status/body/method/route 拒绝、无 Server/CORS 放行头，以及取消后的 host shutdown。
- 默认 packaged smoke 增加 credential-free 的 daemon doctor/routes、default-off 和 remote-bind rejection，但不启动 listener。真实 daemon/API smoke 仅在 `CAICLI_DAEMON_SMOKE=1` 时启动 packaged daemon，验证 health/jobs/queue 和 `api smoke`，并在结束时终止进程。真实模型 smoke 继续由独立 `CAICLI_REAL_MODEL_SMOKE=1` opt-in 控制。
- 新增 `docs_md/release/local_api_daemon_preview.md`，并同步 capability status、quickstart、security model、known limitations、troubleshooting 与 CHANGELOG。只读 localhost daemon/API 标为 Preview；API control、SSE、authentication/TLS、remote bind、reverse proxy、service installation、daemon worker 和 remote control 标为 Deferred。

## 验证

- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1145 passed，0 failed，0 skipped。
- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-build --filter "(FullyQualifiedName~LocalApiPreviewTests)|(FullyQualifiedName~SmokeTestScriptTests)"`
- 结果：通过，15 passed，0 failed，0 skipped；覆盖 route/bind/default-off/doctor、真实 HTTP store/renderer/redaction/error boundary 与 smoke opt-in 脚本契约。
- smoke 支撑命令：`$env:PATH="$env:USERPROFILE\.dotnet;$env:PATH"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过，刷新 packaged executable 供 Week 55 smoke 使用。
- 默认 smoke：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`；daemon/API 与真实模型 smoke 均按设计跳过。默认路径不需要模型凭据且不启动 daemon。
- daemon opt-in smoke：`$env:CAICLI_DAEMON_SMOKE="1"; powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过，输出 `smoke tests passed`；packaged daemon 仅绑定随机 `127.0.0.1` port，health/jobs/queue 与 `api smoke` 通过；真实模型 smoke仍按设计跳过。结束后 `caicliProcessCount=0`。
- smoke 支撑 artifact：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `44421614` bytes，SHA256 `CEA9DE57DD9788ABC33C899A3BA47DE078EBB7B59919E37CB26848E8AA7190FF`。该包只用于 Week 55 packaged smoke，不改变现有 `0.3.3` release decision；`0.4.0` deterministic package acceptance 仍在 Week 57。
- 检查：`git diff --check`
- 结果：通过，无 whitespace error；仅有仓库既有 Git line-ending conversion warning。

## 运行时说明

- `api routes` 和 `daemon doctor` 是静态诊断，不加载 workspace snapshot、不创建 store 或 listener。`api smoke` 只连接调用方已显式启动的 `127.0.0.1` daemon。
- `daemon start` 是前台进程，没有 detached/service/auto-start 模式。Ctrl+C 或进程终止即停止 listener；API 不消费 queue，也不恢复 running item。
- daemon 启动时只创建一次 CLI environment snapshot，固定 workspace 和 user-level state root。HTTP request 不能选择 workspace、切换 profile 或覆盖 approval/config。
- list limit 默认为 50、最大 100。queue status 仅接受既有 pending/running/succeeded/failed/canceled contract。
- loopback 不是 authentication boundary。任何同机、可连接该端口的进程都可能读取 bounded redacted job/queue metadata、workspace path 和 artifact pointer；不得通过 port forward 或 reverse proxy 暴露。

## 风险

- Preview 没有 authentication、authorization、TLS 或 same-user process isolation。当前以 default-off、IPv4 loopback-only 和 read-only route surface 控制风险，但不能抵御已在本机用户上下文运行的恶意进程。
- `JobsJsonRenderer`/`TaskQueueJsonRenderer` 输出包含 bounded redacted task/status/path/pointer metadata。redaction 是 best-effort contract，不是形式化无泄漏证明；本地路径仍属敏感数据。
- 本周未实现 SSE。现有 job/queue store 没有 durable event cursor、lease 或 subscription truth；直接轮询拼装 SSE 会引入第二套不可靠事件语义，因此保持 Deferred。
- 引入 `Microsoft.AspNetCore.App` framework reference 后，本周 packaged smoke zip 从 Week 54 的约 32.5 MB 增至约 44.4 MB。Week 56/57 应复核发布体积、single-file 内容与 trimming/host 取舍，不能在没有回归证据时贸然裁剪。
- 本周没有运行真实模型 smoke。API v1 不调用模型，因此 daemon acceptance 不依赖模型凭据；provider/network 行为不在该 Preview 验收范围。

## Deferred 边界

- 不实现 queue run/cancel/cleanup、automation/pipeline execution、CI write、shell/patch/MCP/model、approval override 或任何 HTTP control route。
- 不实现 SSE/event stream、browser UI、CORS integration、authentication platform、TLS/certificate、multi-user authorization、remote bind、public access、reverse proxy、webhook/callback、remote runner 或 remote agent。
- 不实现 Windows service/Task Scheduler registration、detached daemon、auto-start、auto-restart、concurrent queue worker、lease/heartbeat 或 terminated-run recovery。

## 第 56 周输入

- 对 read-only API 做 security hardening 回归：Host/header/request bounds、method/body rejection、malformed/corrupt store diagnostics、path metadata/redaction、same-user threat documentation 和 no-CORS/no-control invariant。
- 复核 daemon cancellation、port-in-use、rapid start/stop、process crash 与 smoke cleanup，确保无 listener/process/temp artifact 残留。
- 评估 ASP.NET Core 对 self-contained single-file package size 的影响，并在 Week 57 前记录可重复 package size/SHA256；任何 trimming 变更必须先通过完整 build/test/default smoke/daemon opt-in smoke。
- 保持 SSE 与控制路由 Deferred，除非先定义 durable event cursor、request authorization 和复用现有 CLI command/security boundary 的可验证设计。
- Week 56 只做 security/smoke/docs hardening，不扩展到远程访问、认证平台、浏览器 UI 或 daemon worker。
