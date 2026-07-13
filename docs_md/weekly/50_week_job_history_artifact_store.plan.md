# 第 50 周 Job History 与 Artifact Store Foundation Implementation Plan

状态：计划中

**Goal:** 为 0.4.0 工程自动化平台化建立本地 job history 与 artifact index 底座，让后续 task queue、multi-role pipeline、automation、CI/API 都能引用同一套可审计任务记录，而不是各自生成孤立报告。

## 来源

- `docs_md/spec/product_positioning_and_roadmap.md`
- `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`
- `docs_md/release/final_acceptance_0.3.3.md`
- `docs_md/release/capability_status.md`
- `docs_md/spec/runtime_logging_diagnostics.md`

## 前置状态

- 0.3.3 已验收，`exec`、`skills run`、markdown report、expert profiles、`changes`、session、trace、`taskReport` 都是当前 release behavior。
- 当前还没有统一 job id、job store、artifact index 或 `jobs` 命令。
- `session` 是对话/agent run transcript，`trace` 是诊断日志，`taskReport` 是单次任务交付摘要；Week 50 的 job record 必须引用这些事实源，不创建第二套任务真相。

## 本周范围

- 定义本地 job record / artifact metadata / job store DTO。
- 增加只读 `caicli jobs list/show/export` 或等价命令，支持 text 和 JSON。
- 为 `exec` 和 `skills run` 增加显式 job recording 入口，建议先采用 opt-in，例如 `--record-job` 与可选 `--job-name`，避免 0.4.0 第一周改变所有用户的默认持久化行为。
- 记录 redacted metadata：command source、workspace/cwd、status、stop reason、selected skill/expert/report、task report summary、trace/session/report path、timestamps、exit code、warnings。
- artifact index 只保存 artifact pointer 和 compact metadata，不保存 raw referenced content、raw secrets、raw tool arguments 或完整 diff。
- 更新 tests、smoke、release docs 和 runtime logging diagnostics。

本周明确不做：

- 不实现 task queue、后台 runner、定时执行、retry/cancel queue state。
- 不实现 multi-role pipeline。
- 不实现 CI/PR provider integration。
- 不实现 daemon、HTTP API、SSE 或远程控制。
- 不做自动 job recording 默认开启；如后续要默认开启，必须有明确 privacy/security docs 和 disable path。

## 用户入口草案

### Job recording

```powershell
caicli exec --record-job --workspace . "Summarize @file:README.md"
caicli exec --record-job --job-name smoke-review --report markdown --workspace . "Review @folder:src"
caicli skills run review-only --record-job --dry-run --workspace . -- "@file:README.md"
caicli skills run test-fix --record-job --workspace . -- "Fix failing tests"
```

建议语义：

- `--record-job` 显式创建 job record；默认 `exec` / `skills run` 行为保持 0.3.3 兼容。
- `--job-name <name>` 是可选 human label，不作为唯一 id；唯一 id 由 runtime 生成。
- dry-run skill job 可以记录为 `planned` 或 `dry-run` status，但不得记录为 succeeded execution。
- job record 写入用户本地 C-AICLI state，不写 workspace 文件；workspace-local artifact 写入仍必须显式 path 或后续配置。

### Job read surface

```powershell
caicli jobs list
caicli jobs list --output json
caicli jobs show <job-id>
caicli jobs show <job-id> --output json
caicli jobs export <job-id> --format markdown
caicli jobs export <job-id> --format json
```

行为：

- `jobs list/show/export` 默认只读，不调用模型、不运行 shell、不执行 patch、不启动 MCP、不写 workspace。
- JSON schema 必须稳定，适合后续 CI/API 使用。
- text 输出必须适合人类复核，至少包含 status、workspace、command family、skill/expert、artifacts、warnings 和 timestamps。

## 实施设计

### 1. Job model

建议新增 `CSharpAiCli.Core/Jobs`：

- `JobId`
- `JobStatus`
- `JobRecord`
- `JobCommandSummary`
- `JobArtifact`
- `JobArtifactKind`
- `JobRecordStore`
- `JobListReport`
- `JobExportRenderer`

建议 job status：

- `planned`
- `running`
- `succeeded`
- `failed`
- `canceled`
- `approval-required`
- `dry-run`

### 2. Storage policy

建议先使用用户级本地 state directory，与 session/logs 同级或相邻：

```text
%USERPROFILE%\.caicli\jobs\
```

规则：

- 单个 job 一个 JSON record，文件名使用 generated job id。
- 写入使用 create-new/atomic replace 模式，避免并发写半文件。
- job record 不保存 raw referenced content、raw secrets、raw tool args、完整 diff 或完整 markdown report。
- artifact 只记录 path、kind、exists、createdAt、summary、sha256 可选值。
- workspace path 可以记录绝对路径用于本地复核，但 export 时必须经过 redaction policy 或至少明确 local-only。

### 3. Exec/skills integration

`exec --record-job`：

- 在命令开始时创建 `running` job record。
- 在 terminal result 后更新 status、exit code、task report summary 和 artifacts。
- 若 reference validation 在模型前失败，也应记录 failed job 与 stable error code。
- 若 report path 被写入，artifact index 应引用该 path。
- 若 session/trace 存在，artifact index 应引用其 path。

`skills run --record-job`：

- dry-run：记录 expanded plan、skill metadata、status `dry-run`。
- non dry-run：复用 `exec` integration，记录 skill metadata 和 final task report。

### 4. Jobs command

新增 `jobs` command：

- `jobs list [--output text|json] [--json] [--limit <n>]`
- `jobs show <job-id> [--output text|json] [--json]`
- `jobs export <job-id> --format json|markdown`

Exit code 建议：

- job store readable, list empty：0。
- job not found：1，error code `job-not-found`。
- invalid/corrupt job record：list 显示 warning 并继续；show/export 指向该 job 时返回 1。
- store write failure for `--record-job`：不吞掉，进入 command result warning 或 failure；需要在计划中明确最终策略。

### 5. Redaction and artifact boundaries

- `JobRecord` 中所有 prompt、summary、risk、warning 字段必须复用现有 redaction。
- 不保存 raw reference file content。
- 不保存 raw tool args；只保存 tool name、risk、status、error code、duration/summary。
- 不保存 complete diff；只保存 changed file summary 和 pointer to explicit report/trace/session when available。
- `jobs export markdown` 不能变成第二套 task report，应链接/汇总 existing task report metadata。

## 测试计划

新增或扩展测试：

- `JobRecordTests`
- `JobRecordStoreTests`
- `JobListReportTests`
- `JobExportRendererTests`
- `CliCommandFactoryTests`
- `ExecRendererTests`
- `SkillPackTests`
- `SmokeTestScriptTests`

覆盖场景：

- job id 格式稳定且可排序/可引用。
- store create/read/list handles missing directory、empty store、corrupt record、locked file best effort。
- `exec --record-job` 成功、missing model、reference validation failure、approval denied、report path written。
- `skills run --record-job --dry-run` 记录 dry-run plan，不调用模型、不运行工具。
- `jobs list/show/export` text/json 输出稳定。
- redaction 不泄露 API key、secret-like prompt、raw reference content、raw tool arguments。
- `jobs` read commands 不调用模型、不运行 shell/patch/MCP、不写 workspace。

## Smoke 与文档

默认 smoke 新增 credential-free 路径：

- `caicli jobs list --output json`
- `caicli exec --record-job --workspace <workspace> --max-turns 1 ... "summarize @file:note.txt"`，使用缺模型或 fake/offline path 验证 job failure record。
- `caicli skills run review-only --record-job --dry-run --workspace <workspace> -- "@file:note.txt"`。
- `caicli jobs list`
- `caicli jobs show <recorded-job-id> --output json`。
- `caicli jobs export <recorded-job-id> --format markdown`。

Release docs 需要在实现后更新：

- `docs_md/release/CHANGELOG.md`
- `docs_md/release/capability_status.md`
- `docs_md/release/known_limitations.md`
- `docs_md/release/quickstart.md`
- `docs_md/release/security_model.md`
- `docs_md/release/troubleshooting.md`
- `docs_md/spec/runtime_logging_diagnostics.md`
- `tools/Invoke-SmokeTests.ps1`
- `src/CSharpAiCli.Tests/SmokeTestScriptTests.cs`

## 任务清单

- [ ] Step 1: 确认 job recording 是否 0.4.0 Week 50 先采用 `--record-job` opt-in，以及 job store 默认位置。
- [ ] Step 2: 定义 `JobRecord`、`JobStatus`、`JobArtifact`、`JobCommandSummary` DTO 和 JSON schema。
- [ ] Step 3: 实现用户级 `JobRecordStore`，支持 create/update/read/list 和 corrupt record diagnostics。
- [ ] Step 4: 增加 `jobs list/show/export` text/json/markdown renderer。
- [ ] Step 5: 将 `--record-job` 接入 `exec`，覆盖 success/failure/reference failure/report artifact。
- [ ] Step 6: 将 `--record-job` 接入 `skills run` dry-run 与 non dry-run plan metadata。
- [ ] Step 7: 复用 redaction，确保 job record 不保存 raw references、raw secrets、raw tool args 或完整 diff。
- [ ] Step 8: 增加 unit/CLI tests，覆盖 store、render、exec integration、skills dry-run、error codes。
- [ ] Step 9: 更新 smoke script 与 smoke script tests。
- [ ] Step 10: 更新 release docs、capability status、known limitations、runtime logging diagnostics。
- [ ] Step 11: 运行 `dotnet build src\CSharpAiCli.sln -c Release`。
- [ ] Step 12: 运行 `dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- [ ] Step 13: 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`。
- [ ] Step 14: 创建 `50_week_review.md`，记录实际验收、风险、Deferred 边界和 Week 51 输入。

## 验收矩阵

| 功能 | 用户入口 | 行为边界 | 测试覆盖 | 文档/Smoke | 验收标准 |
|---|---|---|---|---|---|
| Job record DTO/schema | internal JSON record | redacted local metadata；不保存 raw refs/secrets/tool args/full diff | DTO/schema/redaction tests | runtime diagnostics 更新 | schema 稳定；可被 jobs list/show/export 读取；corrupt record 有 warning |
| Job store | user local state | 不写 workspace；atomic create/update；locked/corrupt best effort | store tests | security/known limitations 更新 | empty/missing/corrupt/normal store 行为稳定 |
| `exec --record-job` | `caicli exec --record-job ...` | opt-in；不改变默认 exec；不扩大权限；失败也记录 terminal status | CLI/integration tests | smoke 覆盖 credential-free failure path | job status、task report summary、artifacts、error code 可复核 |
| `skills run --record-job --dry-run` | `caicli skills run ... --record-job --dry-run` | 不调用模型、不运行工具、不写 workspace；记录 plan metadata | skill/job tests | smoke 覆盖 | job status 为 dry-run 或 planned；skill/expert/report/safety metadata 可读 |
| `jobs list/show` | text/json | 只读；不调用模型/shell/patch/MCP；不写 workspace | renderer/CLI tests | quickstart/troubleshooting 更新 | text 可读；JSON 可解析；job not found/corrupt 有稳定 error |
| `jobs export` | json/markdown | 导出 job metadata，不生成第二套 task report，不泄露 raw content | export/redaction tests | docs 更新 | markdown/json 可复核；artifact pointers 清晰 |
| Smoke/docs | default smoke | credential-free；real model smoke 仍 opt-in | SmokeTestScriptTests | release docs 更新 | smoke local-only；docs 准确标记 queue/automation/API 为 Deferred |

## 验证基线

实现完成后至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

## 待确认问题

1. Job recording 是否 Week 50 先固定为 `--record-job` opt-in；建议是，避免突然新增默认持久化。
2. Job store 默认位置是否采用用户级 `%USERPROFILE%\.caicli\jobs`；建议是，避免默认写 workspace。
3. `jobs export markdown` 是否只汇总 job metadata 并链接 existing report/session/trace；建议是，避免创建第二套 full task report。
