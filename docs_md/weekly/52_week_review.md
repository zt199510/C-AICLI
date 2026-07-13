# 第 52 周回顾

状态：已验收

## 已完成

- 定义 pipeline schema v1：`PipelineManifest`、`PipelinePlan`、`PipelineRoleStep`、`PipelineRoleBoundary`、`PipelineRoleReport`、`PipelineFinalReport`、run id 与 final report JSON schema。
- 新增固定 built-in catalog：`fix-review-test`、`review-test`、`security-review`。本周没有增加自动 model role routing、provider 分配、并行 worker 或远程协作。
- 新增 `pipeline list/plan` text/json。两者只读取 built-in catalog 与环境快照，不调用模型、不运行工具、不启动 MCP、不创建 queue/job、不写 workspace。
- 新增 `pipeline run` 顺序执行。每个 role 创建真实 queue item/attempt，再通过现有 `queue run` 进入 `exec` 或 `skills run`，强制生成 role job pointer，继续复用 approval、workspace guard、dirty workspace checks、shell policy、dangerous-command detection、disabled tools、MCP startup boundary、trace/session/report、redaction 与 credential path。
- `fix-review-test` 按 implementer -> reviewer -> tester 执行；`review-test` 按 reviewer -> tester 执行；`security-review` 按 security -> reviewer 执行。首个失败 role 会 deterministic short-circuit，已完成 role 的 queue/job/report/artifact 不会被吞掉或删除。
- Reviewer 通过现有 `review-only` skill，security 通过现有 `security` expert；两者不注册 patch/shell/MCP tools，跳过 MCP discovery，executor 继续拒绝 patch、shell 和 `mcp.*` 越权请求。Tester/implementer 仍受当前 approval 和全部安全策略约束。
- Final report 合并 role boundary、queue attempt、job id、task-report summary、已脱敏 command/verification metadata、artifact pointers、warnings 与 remaining risks；不保存 raw referenced content、raw tool arguments、raw secrets 或 full diff。
- 新增 10 个 pipeline fake/offline 与 CLI 定向测试，覆盖 catalog/schema、三角色成功顺序、失败短路、artifact/risk 保留、secret redaction、list/plan 纯本地边界、reviewer/security 实际工具注册与 executor enforcement。
- 默认 packaged smoke 新增 pipeline list/plan text/json、无凭据 `security-review` JSON failure、queue/job pointer、read-only boundary、task-report artifact、remaining risks、short-circuit 与 aggregate markdown report 检查。
- 更新 capability status、quickstart、known limitations、security model、troubleshooting、CHANGELOG 与 runtime logging diagnostics，准确区分当前 local pipeline v1 和 Deferred automation/CI/daemon/API/remote boundaries。

## 验证

- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1108 passed，0 failed，0 skipped。
- 定向命令：`dotnet test src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --filter "FullyQualifiedName~Pipeline"`
- 结果：通过，10 passed，0 failed，0 skipped。
- 支撑命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过；刷新 packaged executable 供最终 smoke 验证。
- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：最终通过，输出 `smoke tests passed`；真实模型 smoke 按设计跳过，并提示设置 `CAICLI_REAL_MODEL_SMOKE=1`。
- Artifact：`artifacts/release/caicli-0.3.3-win-x64.zip`，size `32442226` bytes，SHA256 `FEDDEA460241BEE73FC6D19B7F7F7A2EF41FD6B757AC3CD780A4E8E6B0B4B00D`。该包用于本周 smoke 支撑，不改变当前 `0.3.3` release decision；0.4.0 最终 deterministic package acceptance 仍在 Week 57。

## 运行时说明

- `pipeline list/plan` 支持 `--json` 与 `--output json`，输出单个 JSON object；plan 明确显示 role order、expert、exec/skill entry、instructions 和 boundary。
- `pipeline run --output json` 输出单个 `pipeline.result` object。Text mode 输出 aggregate summary；`--report markdown` 在 summary 后输出 aggregate markdown，不自动写 workspace 文件。
- 每个 role queue record 的 warning 包含 redacted pipeline run/step/role correlation；queue attempt 保存 role job id，final report 同时列出 queue/job/artifact pointers。
- Role task 包含原始任务和 bounded/redacted prior-role job evidence，使后续 role 能复核前序状态，但不持久化 raw event payload 或完整 diff。
- Pipeline 使用调用者当前配置的同一 provider/model。无模型或 key 时，首个 role 返回稳定 failure，queue/job/task-report evidence 仍完整；默认 smoke 不需要真实凭据。

## 风险

- Pipeline v1 是顺序、单进程、首失败短路；没有 pipeline-level retry/resume、并行 worker、lease/heartbeat 或进程终止恢复。进程在 running role 中断时仍需人工复核 queue/job 文件。
- Final aggregate report 当前是命令输出，不是独立持久化的 pipeline history truth。持久事实仍是各 role 的 queue/job/taskReport/artifact；后续 automation/CI artifact 不应创建第二套相互漂移的 report store。
- Queue request 会持久化 bounded redacted task；secret-like value 会变为 `[redacted]`，保护本地状态但可能改变后续 role 看到的任务语义。
- Reviewer/security 的只读边界阻止写、shell 与 MCP，但模型输出质量仍不等于 review/security 完整性证明；final report 也不能替代人工 diff 与测试复核。
- 本周没有运行 opt-in 真实模型 pipeline smoke。真实 provider 的质量、延迟、配额和多 role 文本衔接仍是外部风险；本周只验收共享 contract、fake/offline path 与 credential-free failure path。

## Deferred 边界

- 不实现自动 model role routing、多模型/provider 自动分配、并行 multi-agent worker 或远程团队协作。
- 不实现 pipeline retry/resume、background worker、scheduler、daemon、HTTP API、SSE、CI/PR provider 或远程 runner/control。
- 不绕过 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、MCP startup boundary、trace/session/report/job history 或 smoke。

## 第 53 周输入

- Local automation list/validate/run --dry-run 应复用本周 `PipelinePlan`、built-in catalog、queue/job pointers 与 final report schema，不创建第二套执行器或 report truth。
- Automation manual trigger 如调用 pipeline，必须继续进入相同 queue/job/exec/skills command factory，不持久化或注入 approval override，也不能扩大 reviewer/security boundary。
- Dry-run/validate 必须 credential-free，不调用模型、不运行工具、不启动 MCP、不写 workspace；真实模型 automation smoke 继续显式 opt-in。
- Automation artifact 应引用 role job/taskReport/artifact pointers，并明确 first-failure、exit code、warnings、remaining risks 与未执行 roles；不要把 aggregate report 当成 correctness proof。
- Week 53 仍不做后台常驻、自动定时执行、并行 worker、daemon/API、远程 runner 或团队协作。
