# 第 56 周 Automation Security / Smoke / Docs Hardening Implementation Plan

状态：已验收

**Goal:** 对 Week 50-55 引入的 job、queue、pipeline、automation、CI artifacts 和 API preview 做安全、观测、smoke、文档收口，避免在 release acceptance 周继续扩大范围。

## 来源

- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- Week 50-55 review 文档
- `docs_md/release/security_model.md`
- `tools/Invoke-SmokeTests.ps1`

## 本周范围

- 回归 approval、workspace guard、secret redaction、dirty workspace、shell policy、disabled tools、MCP startup boundary。
- 加固 job/queue/artifact storage cleanup 和 corrupt record diagnostics。
- 加固 text/json schema docs 与 smoke 覆盖。
- 更新 release docs、known limitations、capability status 和 troubleshooting。
- 不新增大功能，只修正 scope drift、文档不一致和验收缺口。

本周明确不做：

- 不新增新的 automation target。
- 不新增 provider integration。
- 不扩展 daemon/API preview。
- 不做 0.5.0 垂直能力。

## 任务清单

- [x] Step 1: 汇总 Week 50-55 Deferred/Preview 边界。
- [x] Step 2: 回归所有新增命令是否只读/写入边界准确。
- [x] Step 3: 增加 redaction/security regression tests。
- [x] Step 4: 增加 smoke 覆盖 job/queue/pipeline/automation/CI artifact 的 credential-free 路径。
- [x] Step 5: 更新 release docs 和 runtime logging diagnostics。
- [x] Step 6: 修正 capability status 中 Accepted/Preview/Deferred 标记。
- [x] Step 7: 运行 build/test/smoke。
- [x] Step 8: 创建 `56_week_review.md`，列出 release acceptance 前剩余问题。

## Step 1 边界汇总

- Accepted：本地 job history/artifact index、手动 task queue、固定顺序 multi-role pipeline、workspace-local automation validate/dry-run/manual trigger、provider-neutral CI artifact。
- Preview：仅限显式 `daemon start --preview` 启动的 IPv4 loopback、只读 jobs/queue HTTP inspection surface；普通 CLI 与默认 smoke 不启动 listener。
- Deferred：job/artifact retention/delete、queue worker/lease/recovery、pipeline retry/resume/parallel/provider routing、automatic schedule execution、CI provider API/PR comment/upload、API control/SSE/authentication/TLS/remote bind/service，以及所有 remote/team execution。
- 安全边界：任何执行路径继续复用 approval、workspace guard、dirty workspace checks、secret redaction、shell policy、disabled tools、MCP startup boundary、trace/session/report/job artifact 与 smoke；Preview 和 Deferred 状态不授予额外权限。

## Step 2 命令边界回归

- 只读/零执行：`jobs list/show/export`、`pipeline list/plan`、`automation list/validate/plan`、`ci summarize/check`（stdout）、`daemon doctor`、`api routes`；不创建 model/tool/MCP/queue/job/command-log/workspace state。
- 显式 workspace 写入：仅 `ci summarize --markdown-path`，复用 `ReportPathResolver`，限制在 workspace 内的新文件且拒绝覆盖。
- 用户状态写入：`queue add/cancel/cleanup`、显式 job recording；cleanup 仅删除匹配的旧 terminal queue record，不级联删除 job/artifact，不处理 pending/running/corrupt record。
- 受控执行：`queue run` 回到 `exec`/`skills run` command factory；`pipeline run` 经 queue/job；`automation run --manual` 经 queue/pipeline。三者均不保存或注入 approval override。
- Preview：`daemon start --preview` 是显式前台 loopback listener；API route 只读，不执行 queue/pipeline/automation/CI write。
- 定向回归：上述 Job/Queue/Pipeline/Automation/CI/API CLI 测试 44 passed，0 failed，0 skipped。

## Step 3 Security hardening

- Queue JSON list/cleanup diagnostics 通过显式 renderer 投影二次脱敏，不再直接序列化 store diagnostic path/id/summary。
- Local API Preview 拒绝非 `localhost`/`127.0.0.1` Host，Kestrel 显式限制 request header count/total size；继续保持 GET-only、body rejection、no-CORS、no-server-header。
- 回归覆盖 corrupt job/queue filename secret、CLI/API diagnostic redaction、cleanup 保留 corrupt record、invalid Host、response security headers、port-in-use 与 cancellation cleanup。
- 定向回归：TaskQueue store/renderer 与 Local API Preview 测试 21 passed，0 failed，0 skipped。

## Step 4 Smoke hardening

- 默认 packaged smoke 已覆盖 job list/show/export、queue add/list/show/run/cancel/cleanup、pipeline list/plan/run/report、automation list/validate/plan/dry-run/manual、CI JSON/markdown/file/check。
- 新增 corrupt job/queue record packaged checks：有效记录继续可见，JSON diagnostic 二次脱敏，queue cleanup 报告但保留 corrupt record 供人工诊断。
- 默认路径继续清除 `OPENAI_API_KEY`/`OPENAI_MODEL`，不启动 daemon；real model 与 daemon/API listener 分别由 `CAICLI_REAL_MODEL_SMOKE=1`、`CAICLI_DAEMON_SMOKE=1` 独立 opt-in。
- Smoke 脚本契约测试 1 passed，0 failed，0 skipped；完整 packaged smoke 留在 Step 7 最终验证。

## Step 5 Docs hardening

- 更新 security model、known limitations、troubleshooting、quickstart、Local API Preview threat model 与 CHANGELOG，记录 queue corrupt diagnostics redaction、cleanup 保留语义、Host/header/lifecycle hardening。
- runtime logging diagnostics 补齐 Week 53-56 automation/CI correlation、Preview 静态诊断、store diagnostics 与默认/opt-in smoke 边界。
- 修正 job cleanup 文档漂移：job/artifact cleanup 仍 Deferred；现有 `queue cleanup` 仅处理匹配的旧 terminal queue record。
- `git diff --check` 通过；仅有仓库既有 line-ending conversion warning。

## Step 6 Capability status

- Capability status 改为 `0.4.0 Candidate Capabilities`，定义 Accepted/Solidified/Preview/Deferred 语义，避免把 Week 50-56 能力误归入 0.3.3 artifact。
- Week 50-54 本地能力标为 Accepted；只读、default-off localhost daemon/API 保持 Preview；scheduler/provider API/control/SSE/auth/TLS/remote/concurrent worker 等保持 Deferred。
- Candidate decision 明确 0.3.3 仍是最后已验收 package，Week 57 才负责 0.4.0 version metadata、deterministic package 与 final acceptance。
- Roadmap 同步为 Week 50-56 implementation/hardening 完成、Week 57 只做 release acceptance。

## Step 7 Verification

- `dotnet build src\CSharpAiCli.sln -c Release`：通过，0 warnings，0 errors。
- `dotnet test src\CSharpAiCli.sln -c Release --no-build`：通过，1147 passed，0 failed，0 skipped。
- `tools\Build-Release.ps1`：通过，刷新 packaged executable 供 smoke；Week 57 仍负责 0.4.0 deterministic 双构建验收。
- 默认 `tools\Invoke-SmokeTests.ps1`：通过；daemon/API 与 real model smoke 均按设计跳过。
- 显式 `CAICLI_DAEMON_SMOKE=1` smoke：通过；real model smoke 按设计跳过；结束后 `caicliProcessCount=0`、`smokeTempDirectoryCount=0`。
- Week 56 支撑包：`caicli-0.3.3-win-x64.zip`，size `44422673` bytes，SHA256 `005CE5D6FC18C02672B90236E3646A138D56A944C49A7F040F18BCEBF5ECFE4E`。该包不改变 0.3.3 release decision。
- 最终 smoke 脚本契约测试 1 passed；`git diff --check` 通过，仅有既有 line-ending conversion warning。

## 验收标准

- 0.4.0 新增能力都有 text/json/trace/report 或 job artifact 复核路径。
- 默认 smoke 不依赖模型凭据、不启动 daemon、不访问网络。
- docs 准确区分 current、preview、deferred。
- Week 57 可以只做 release acceptance，不再补实现范围。
