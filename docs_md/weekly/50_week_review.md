# 第 50 周回顾

状态：已验收

## 已完成

- 建立本地 job history 与 artifact index foundation。
- 新增 `JobRecord`、`JobStatus`、`JobArtifact`、`JobCommandSummary`、`JobSkillSummary`、task report summary、redaction summary、JSON schema 和 user-level `JobRecordStore`。
- 默认 job store 位置为用户级 `%USERPROFILE%\.caicli\jobs`；`CAICLI_USER_PROFILE` 可在 smoke/portable 场景中重定向用户根。
- 新增只读 `jobs list/show/export`：
  - `jobs list [--output text|json] [--json] [--limit <n>]`
  - `jobs show <job-id> [--output text|json] [--json]`
  - `jobs export <job-id> --format json|markdown`
- `jobs` read surface 不调用模型、不运行 shell/patch、不启动 MCP、不写 workspace、不写 command log。
- `exec --record-job [--job-name <name>]` opt-in 接入 job recording，覆盖 success、missing model failure、reference failure、markdown report artifact pointer。
- `skills run --record-job [--job-name <name>]` opt-in 接入 job recording：
  - `--dry-run` 记录 `dry-run` status 和 `inline:skills.runPlan` artifact pointer。
  - non-dry-run 复用现有 exec safety/report path，并记录 skill metadata。
- Job record 只保存 redacted metadata 和 artifact pointers，不保存 raw referenced content、raw secrets、raw tool arguments 或完整 diff。
- 新增 unit/CLI/smoke tests，并更新 release docs、capability status、known limitations、security model、quickstart、troubleshooting、runtime diagnostics 和 smoke script。

## 验证

- 命令：`dotnet build src\CSharpAiCli.sln -c Release`
- 结果：通过，0 warnings，0 errors。

- 命令：`dotnet test src\CSharpAiCli.sln -c Release --no-build`
- 结果：通过，1080 passed，0 failed，0 skipped。
- 复跑说明：第一次全量运行中既有 `McpStdioTransportTests.Send_rejects_oversized_unterminated_stdout_line_quickly` 在全量负载下命中 2 秒时序边界（1079 passed，1 failed）；该测试单独复跑通过，未修改或放宽 MCP oversized stdout 安全断言，随后同一全量命令复跑 1080/1080 通过。

- 支撑命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1`
- 结果：通过，用于刷新 smoke 默认执行的 `artifacts/release/caicli-0.3.3-win-x64/caicli.exe`。

- 命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`
- 结果：通过；默认 real model smoke 按设计跳过，提示设置 `CAICLI_REAL_MODEL_SMOKE=1` 后启用。

## 运行时说明

- `--record-job` 是显式 opt-in；未传入时不改变 `exec` 或 `skills run` 的默认持久化行为。
- `--job-name` 只是 human label，唯一 id 由 runtime 生成。
- 初始 job record 创建失败时，显式 recording 的命令会在模型/工具执行前失败。
- 执行后 job record 终态更新失败时，命令返回 `job-record-write-failed`，避免显式记录丢失被静默吞掉。
- `jobs list` 会继续跳过 corrupt records 并输出 diagnostics；`jobs show/export` 指向 missing/corrupt target 时返回非零错误。

## 风险

- Job store v1 是本地 JSON 文件 store，没有 cleanup、retention、compaction 或并发锁协议；后续 queue/automation 前需要补清理策略。
- Artifact hash 是 best-effort，仅对当前可读路径计算；缺失、移动或不可读 artifact 会保留 pointer 但 `exists=false`。
- `jobs export markdown` 是 metadata 摘要，不是第二套完整 task report；用户仍需查看显式 markdown report、trace、session 或 workspace diff。
- MCP oversized unterminated stdout 的全量套件 2 秒时序断言出现过一次负载相关波动；安全行为的单测与第二次全量回归均通过，Week 51 应继续观察而不是降低边界。

## Deferred 边界

- 不实现 task queue、retry/cancel queue state、runner、schedule、automation、multi-role pipeline。
- 不实现 CI/PR provider、daemon、HTTP API、SSE 或远程控制。
- 不改变默认 exec/skills run recording 行为。
- 不保存 raw referenced content、raw tool arguments、raw secrets 或 full diff。
- 不绕过 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、MCP startup boundary 或 smoke。

## 第 51 周输入

- 在 Week 50 `JobRecordStore` 和 job status 基础上设计 local task queue v1。
- 明确 pending/running/succeeded/failed/canceled 状态转换，以及 retry/cancel/cleanup 语义。
- 继续保持 fake/offline tests 和 credential-free smoke；真实模型、daemon/API smoke 仍必须 opt-in。
- Queue runner 必须复用现有 approval、workspace guard、dirty workspace checks、redaction、shell policy、disabled tools、trace/session/report/job artifact path。
