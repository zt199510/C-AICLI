# 运行时、日志与诊断说明

## Runtime

当前项目目标框架为 `net9.0`。当前验收和本机诊断环境使用 SDK `9.0.308`。当前仓库通过根目录 `global.json` 锁定 SDK `9.0.308`，并使用 `latestPatch` roll-forward 策略。

`doctor` 负责显示：

- target framework
- dotnet SDK
- dotnet runtime
- SDK lock 状态
- workspace 路径和状态
- 用户配置路径
- 工作区配置路径
- 日志目录
- API key 是否存在

## 配置来源

第 8 周仍沿用最小配置 schema：

以下 key 仅为示例值，不可作为真实凭据。

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-example"
}
```

配置优先级：

- API key：`OPENAI_API_KEY` > 用户配置 `apiKey` > missing
- model：`OPENAI_MODEL` > 用户配置 `model` > 工作区配置 `model` > `not configured`

工作区配置中的 `apiKey` 不进入 effective configuration；诊断和日志只记录 `ignored workspace config apiKey: <path>`，不记录原始 key。

报告和日志只打印 API key 的 `present` 或 `missing`，以及来源；不打印原始 key。

## 日志目录

当工作区状态是 `ready`：

```text
<workspace>/.caicli/logs
```

当工作区状态是 `missing` 或 `not directory`：

```text
<user profile>/.caicli/logs
```

命令日志按 UTC 日期写入：

```text
yyyy-MM-dd.log
```

每行日志记录：

- `timestampUtc`
- `command`
- `workspace`
- `workspaceStatus`
- `model`
- `modelSource`
- `baseUrl`
- `baseUrlSource`
- `agentBackend`
- `agentBackendSource`
- `agentBackendStatus`
- `apiKey`
- `apiKeySource`
- `warnings`
- `instructionWarnings`

日志不记录 `SecretValue.Value`。

## Week 37 Diagnostics And Trace Logs

Global `--verbose` is recursive and may be used on subcommands. It prints safe, human-readable diagnostics for text output, including command name, command/session ids, timestamp, workspace, log directory, config sources, warning counts, model/base URL/backend status, and API key presence/source. Commands that emit JSON (`--json` or `--output json`) must keep stdout JSON-clean and must not append verbose diagnostics to the JSON stream.

Global `--trace` is recursive. `CAICLI_TRACE=1` enables the same trace behavior without passing the flag. Trace logs are local JSONL files under `LogPathResolver.ResolveLogDirectory(snapshot)` and use this UTC date file name:

```text
yyyy-MM-dd.trace.log
```

Command logs continue to use:

```text
yyyy-MM-dd.log
```

Trace records share these core fields:

- `timestampUtc`
- `command`
- `commandId`
- `sessionId`
- `workspace`
- `type`
- `sequence`

Exec trace event/result records include `status`, `stepIndex`, `stopReason`, `durationMs`, `approvalDurationMs`, `errorCode`, `approvalStatus`, and `payload` when available. Payload keys are stable enough for diagnostics but must be treated as diagnostic data, not a public API contract.

For agentic `exec`, `status` is the coarse terminal outcome (`success`, `failure`, or `timeout`) and `stopReason` is the machine-readable terminal reason, such as `completed`, `max-steps-exceeded`, `max-tool-calls-exceeded`, `overall-timeout`, `model-timeout`, `tool-timeout`, `tool-disabled`, `approval-denied`, `tool-failure`, `model-error`, or `backend-unavailable`. `errorCode` remains the specific model/tool/runtime error code and is not interchangeable with `stopReason`.

Agent event order is shared by text output, NDJSON output, and trace output. `model.turn`, `tool.call`, and `tool.result` events can include `stepIndex`; terminal `final.response` and `agent.error` events include `stopReason`. Session transcript v1 is not a full event log, but agentic exec sessions add `agentRuns[]` summaries with final `status`, `stopReason`, optional `errorCode`, summary, event count, and tool call count.

Week 44 adds final review/report diagnostics for every agentic `exec` run:

- `review.gate` summarizes the final git diff through the read-only `git.diff` planning-phase path. Its payload includes `readOnly=true`, `toolName=git.diff`, `hasDiff`, and `truncated`. It must not call shell, patch, or workspace write tools.
- `taskReport` records the final report as an event. Its payload includes status, stop reason, prompt, plan, tools, workflow reference count/list, changed file count/list, command count/list, verification count/statuses, risk count/list, trace path when trace is enabled, optional review gate status, optional expert metadata, optional report artifact metadata, and secret presence metadata.
- The terminal `exec.result.payload.taskReport` contains the structured report payload used by JSON consumers and trace readers. `exec --report markdown` adds a `report.generated` event and `taskReport.report` metadata without placing raw markdown in NDJSON output.
- Session transcript `agentRuns[]` stores the same task report summary object when `exec --session` is used. Markdown session export renders compact task report counts, expert/report metadata, report path when present, and trace path.
- Full standalone markdown task report files are written only when `--report markdown --report-path <workspace-path>` is explicitly requested. Existing paths are rejected by default.

Week 47 adds workflow reference and changes-view diagnostics:

- `context.references` records bounded `@file` / `@folder` resolution for `exec`. Its payload includes reference count, included file count, skipped file count, byte count, truncation status, kinds, paths, and warning summaries. It is emitted before model/tool work for normal agent runs and before terminal failure for reference validation failures.
- `exec.result.payload.taskReport.references[]` records reference metadata only: kind, requested path, resolved path, status, included/skipped file counts, byte count, truncation flag, warnings, and optional error code. Raw referenced file content is not persisted in task reports, traces, or session exports.
- `caicli changes --output json` emits a single JSON object with `type="changes.view"`, `status`, `git`, `changedFiles`, optional `taskReport`, optional `session`, and `warnings`.
- `caicli changes --trace` or `CAICLI_TRACE=1` writes a redacted `changes.view` trace command event. `changes` does not write command logs by default.

Week 49 adds local skill metadata:

- `skills list` emits local-only text or a single JSON object with `type="skills.list"`, built-in/workspace-local pack metadata, and non-fatal manifest diagnostics. It does not call a model or run tools.
- `skills run --dry-run` emits text or a single JSON object with `type="skills.runPlan"`, selected skill, source, entry mode, expert, report, safety, validation command hint, suggested references, instructions, and expanded task. Dry-run does not write reports, run shell/patch/MCP tools, call a model, or save a session.
- Non-dry-run `skills run` uses the existing agentic `exec` trace/session/report path and sets the trace command name to `skills run`.
- `taskReport.skill` records name, version, description, source kind/path, entry mode, expert, report, safety summary, validation command hint, and suggested references. The same structured field is present in text-derived summaries, NDJSON result payloads, trace result payloads, session task reports, and markdown task reports.

Week 50 adds opt-in local job history and artifact indexing:

- `exec --record-job` and `skills run --record-job` create one schema-v1 JSON record per job under the user-level state directory `%USERPROFILE%\.caicli\jobs` (or the profile selected through `CAICLI_USER_PROFILE`). Default execution persistence is unchanged when the flag is absent.
- Job ids use the sortable shape `job_yyyyMMddTHHmmssfffZ_<8hex>`; the corresponding file is `<job-id>.job.json` with `schemaVersion: 1`.
- A record begins as `running` and is atomically updated to `succeeded` or `failed`; skill dry-run records finish as `dry-run`. Reference validation and other runtime failures after record creation also receive a terminal record. A create/update failure returns `job-record-write-failed` instead of silently losing an explicitly requested audit record.
- Job records contain bounded redacted command/task metadata, terminal status/error metadata, compact task report counts/summaries, compact skill plan metadata, and artifact pointers for task report, trace, session, markdown report, or dry-run plan. Artifact pointers include kind, path, existence, compact summary, and optional SHA256. Records do not contain raw referenced content, raw tool arguments, raw secrets, agent event payloads, or full diffs.
- `jobs list` supports text/JSON plus a positive `--limit`; corrupt/unreadable records are diagnostics and do not hide valid records. `jobs show` supports text/JSON, and `jobs export` writes JSON or markdown to stdout. A missing job returns `job-not-found`.
- `jobs list/show/export` do not call a model, run shell/patch/MCP tools, start MCP servers, write command logs, or write workspace files. A missing job directory is an empty successful list and is not created by read commands.
- `skills run --record-job --dry-run` is the explicit persistence exception to ordinary dry-run behavior. It finishes with status `dry-run` and an `inline:skills.runPlan` pointer, but still does not call a model, run tools, create a report, start MCP, save a session, or write the workspace.

Week 51 adds local task queue v1 and run control:

- Queue ids use `queue_yyyyMMddTHHmmssfffZ_<8hex>`. Schema-v1 records live under `%USERPROFILE%\.caicli\queue` (or the `CAICLI_USER_PROFILE` state root) with pending/running/succeeded/failed/canceled status, bounded redacted request metadata, attempts, warnings, and queue-to-job pointers.
- `queue add exec|skill` only persists a pending request. `queue list/show` are local read surfaces with text/JSON output. These commands do not call a model, run tools, start MCP, write command logs, or write workspace files.
- `queue run` accepts pending or failed items, creates a new running attempt, and invokes the existing `exec` or `skills run` command path with mandatory job recording. It does not persist or inject approval overrides. The delegated path retains approval, workspace/dirty checks, shell policy, dangerous-command detection, disabled tools, MCP startup, expert/skill, trace/session/report, and credential boundaries.
- A completed attempt stores exit/error metadata and its job id. The queue item stores the latest job id, and the corresponding job uses the queue id as its label. Failure paths, including missing model credentials, still reach a queue terminal state and produce job history when job recording succeeds.
- `queue cancel` is pending-only. Failed items can be manually retried by `queue run`. `queue cleanup` only accepts succeeded, failed, or canceled status and preserves pending, running, corrupt, and unknown records. It never deletes referenced jobs or artifacts.
- Queue v1 is manual and local. There is no daemon, scheduler, concurrent worker pool, lease/heartbeat recovery, remote runner, CI provider, or API/SSE control surface.

Week 52 adds local multi-role pipeline v1:

- `pipeline list` emits text or a single `pipeline.list` JSON object from the fixed built-in catalog. `pipeline plan` emits an auditable text or `pipeline.plan` JSON object with role order, expert, entry path, instructions, and tool boundary. These commands do not call a model, run tools, start MCP, create queue/job state, or write the workspace.
- `pipeline run` executes built-in roles sequentially by creating a queue item for each role and re-entering `queue run`, which delegates to the existing `exec` or `skills run` path with mandatory job recording. Each role report carries its queue id/attempt, job id, expert, boundary, status/error, task-report summary, artifacts, warnings, and remaining risks.
- The terminal text/JSON/markdown aggregate uses schema-v1 `PipelineFinalReport`. It contains bounded redacted metadata and artifact pointers only, not raw references, raw tool arguments, raw secrets, or full diffs. A failed role short-circuits later roles while preserving earlier evidence.
- Reviewer and security roles remain read-only through the existing expert/skill boundary: patch/shell tools are not registered, MCP discovery is skipped, and executor enforcement rejects patch, shell, and `mcp.*` calls. Pipeline orchestration does not inject approval overrides or bypass workspace/dirty checks, shell policy, disabled tools, trace/session/report, or credential checks.
- Pipeline v1 uses fixed role definitions and the caller's configured provider/model. Automatic role routing, provider assignment, parallel workers, retry/resume, scheduling, daemon/API/SSE, and remote collaboration are not enabled.

Week 53-54 add automation and CI correlation without a second diagnostic truth:

- Automation dry-run is non-persistent. Manual runs attach bounded redacted automation name/run/source/target metadata to the existing queue/job path and add an `inline:automation/<run-id>` job artifact pointer.
- Pipeline role reports and automation results reference queue ids, job ids, task-report summaries, warnings, remaining risks, and existing artifact pointers; they do not persist raw event payloads or full diffs.
- `ci summarize/check` read one job and render provider-neutral JSON/markdown. They do not write command logs or create execution state; explicit `--markdown-path` is the only workspace write and uses workspace guard/no-overwrite behavior.

Week 55-56 add Preview and storage diagnostics hardening:

- `daemon doctor` and `api routes` are static diagnostics and do not create a workspace snapshot or listener. `daemon start --preview` is a foreground, IPv4-loopback-only opt-in; default smoke never starts it.
- The read-only API reuses job/queue stores and renderers. Corrupt record diagnostics are returned alongside valid records with secret-like path/id/summary values redacted; no artifact content is read.
- The Preview rejects unsupported method/body/Host/filter/route inputs with bounded JSON errors and applies no-store/nosniff, no-server-header, no-CORS, connection/request/header limits.
- Default smoke clears model credentials and exercises credential-free job/queue/pipeline/automation/CI artifact paths. Real model and daemon/API smoke remain independent opt-ins through `CAICLI_REAL_MODEL_SMOKE=1` and `CAICLI_DAEMON_SMOKE=1`.

Week 58-59 add source-only Project Pack diagnostics:

- `packs list` emits local text or one `type="packs.list"` JSON object. Static `packs doctor` emits
  `type="packs.doctor"`; only explicit doctor `--probe` can start a process, after current approval and bounded
  typed executable/argument checks.
- `packs plan gerber-tiff` emits text or one `type="packs.plan"`, `schemaVersion=1`,
  `planSchema="gerber-tiff.plan.v1"` JSON object. Status is `blocked`, `ready-for-staging`, or `runnable`.
  Exit code is zero only for `runnable`; a safe frozen inventory with missing tools is `ready-for-staging` and
  returns one.
- Plan JSON records workspace-relative canonical input/output, sorted inventory kind/role/size/SHA256, static
  redacted tool identity, fixed stages/timeouts/restart policy, expected artifact slots, stable diagnostics,
  readiness flags, and a deterministic SHA256 fingerprint. It declares raw input content, absolute tool paths,
  arbitrary tool arguments, unknown-file hashes, and approval bypass absent.
- Unknown files are listed but excluded from hash/tool input/fingerprint. Unsafe, unreadable, changing,
  ambiguous, duplicate, missing, or over-limit supported input prevents a fingerprint/runnable plan.
- `packs list/doctor/plan` do not create queue/job/run/session/report state or write the workspace. `packs plan`
  validates `--output-dir` with no-overwrite semantics and never creates it. When recursive `--trace` is
  explicitly enabled, start/complete summaries use the existing redacted trace path; raw inventory content and
  absolute tool paths are not trace payloads.
- Default packaged smoke exercises credential-free list/static-doctor/plan and expects no conversion.
  Real Gerber/TIFF tool smoke remains Deferred and may only be introduced behind
  `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` with explicit installed-tool paths and current approval.

Week 60 adds source-only managed Project Pack run diagnostics:

- `packs run gerber-tiff --plan <workspace-file> --dry-run` creates one schema-v1 run under
  `%USERPROFILE%\.caicli\runs\<run-id>` (or the state root selected by `CAICLI_USER_PROFILE`). The stable layout
  contains `run.json`, `checkpoint.json`, a sanitized `plan.json`, `input-manifest.json`, and `staging`, `working`,
  `logs`, `artifacts`, and `reports` directories. This command does not call a model, fake driver, real tool,
  network service, TIFF verifier, or human decision path.
- Run ids use `run_yyyyMMddTHHmmssfffZ_<8hex>`. `run.json` and `checkpoint.json` carry matching schema, run id,
  revision, state, plan fingerprint, and policy fingerprint. Atomic replacement writes checkpoint first and run
  record second; a crash between them is detected as an inconsistent/corrupt checkpoint and fails closed.
  Unknown JSON fields and unsupported schemas also fail closed.
- Run states are `created`, `discovered`, `staged`, `ready`, `running`, `verifying`,
  `awaiting-acceptance`, `accepted`, `rejected`, `failed`, `canceled`, and `interrupted`. Only the compiled
  transition table is allowed. A fake driver used by tests emits explicit success/failure/timeout/cancel/
  partial-output/interrupted stage events through this same table; fake success ends at `awaiting-acceptance`
  and is not real conversion or TIFF verification evidence.
- Staging copies only supported files declared in the plan inventory. Source relative path, kind, layer role,
  tool-input flag, size, and SHA256 are revalidated before copy; destination names are generated as bounded flat
  mappings; source and destination SHA256 are checked after copy. Unknown files and raw contents are absent from
  `input-manifest.json` and `plan.json`.
- Pre-run and resume checks rebuild the Week 59 deterministic plan and compare plan id/fingerprint, current input
  inventory, static tool identity, no-overwrite output boundary, and current policy fingerprint. Approval grants,
  tokens, and overrides are not stored. `staged`/`ready` can continue only after current reapproval;
  `verifying` additionally requires complete managed artifact hashes; `awaiting-acceptance` can continue only at
  the later human gate. `running`/`interrupted` returns `pack-run-restart-required` and never auto-replays execute.
- Each CLI dry-run creates a normal job record whose `project-pack-run` and `project-pack-input-manifest`
  artifacts point to the managed evidence. The run record carries the job id and an optional validated queue id.
  The job `taskReport` remains null: pack run state is an operational checkpoint and does not create a second
  task-report truth.
- `packs runs show` reads the same run/checkpoint pair. `packs resume --dry-run` renders eligibility only.
  `packs cancel` transitions one non-terminal run to `canceled`. These paths do not start workers or external
  processes. A short exclusive mutation lock plus revision check prevents same-run double writes; it is not a
  cross-process worker lease or heartbeat.
- Week 60 packaged smoke is credential-free, model-free, network-free, and real-tool-free. It verifies source and
  staged hashes, managed layout, run/job correlation, resume/cancel, no conversion artifacts, atomic temp cleanup,
  and unchanged `caicli`/`gerbv`/`magick` process sets. `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` still does not execute a
  tool until the Week 61 adapter is available.

Week 61-62 add controlled conversion and TIFF verification diagnostics:

- A successful controlled conversion records bounded/redacted `logs/conversion-execution.json`, run/job pointers,
  tool filename/version/SHA256, fixed template id, current approval status, exit/duration, bounded stdout/stderr,
  cleanup flags, and input/output identities. It stops in `verifying`; this event alone is not TIFF verification.
- `packs verify <run-id>` emits text or one JSON object with `type="packs.verify"`, schema version, independent
  verification levels, frozen resource limits, sorted TIFF/frame metadata, optional exact/pixel comparison,
  bounded diagnostics, and `correctnessProof=false`. JSON stdout remains clean under `--output json`.
- Verification writes stable `reports/verification-rNNNN.json` and `.md` files with create-new/no-overwrite
  semantics. Their pointers are appended to the same run/job artifact indexes; job `taskReport` remains null.
  A hard pass moves only to `awaiting-acceptance`; failure records stable error evidence and enters `failed`.
- `packs preview <run-id>` emits `type="packs.preview"`, `correctnessProof=false`,
  `automaticallyOpened=false`, and `automaticallyUploaded=false`. Managed PNG/contact-sheet and preview-report
  pointers are added only through the same run/job evidence path. Preview does not alter hard verification.
- Explicit `--trace` records bounded `command.start`/`command.complete` summaries for verify/preview. Trace,
  JSON, markdown, run, and job diagnostics pass through secret redaction and do not persist raw TIFF/source/
  baseline content, raw external argv, tool absolute paths, or approval material.
- Default smoke proves a dry-run/fake-ready checkpoint cannot be verified. Real conversion + verification +
  preview smoke remains independent and runs only with `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` and explicit tool paths.

Trace and verbose diagnostics must redact secrets before writing output. Redaction covers API keys, access/refresh tokens, passwords, `Authorization` headers and common variants, `secretKey`, `privateKey`, nested or escaped `argumentsJson`, OpenAI `sk-...` keys, and GitHub token formats such as `ghp_...` and `github_pat_...`. Diagnostics may record key presence and source, but never raw key values.

`logs path` prints the resolved CLI log directory. It must not create the directory just to print the path.

`logs show --tail <n>` reads existing direct `*.log` files in the resolved log directory, including command logs and `*.trace.log` trace files. The default tail is `20`; `n` must be positive. A missing log directory is an empty success. Locked, deleted, unreadable, or raced files are skipped best-effort. `logs show` must not mutate command logs for itself.

`logs clear` deletes only direct `*.log` files in the resolved log directory. It does not recurse and must preserve non-log files, subdirectories, workspace files, and other `.caicli` content. Locked, deleted, or unreadable files are skipped best-effort. It refuses to clear if the resolved log directory or any ancestor is a symlink/reparse point. `logs clear` must not mutate command logs for itself.

Known residual risk: parse-time `System.CommandLine` validation errors can return before trace context creation. This is accepted because no model/tool flow has begun.

## Chat 边界

第 4 周只添加 `chat` 的 Phase 02 边界提示。该命令用于告诉用户模型客户端和流式渲染器属于第 5-6 周，不执行模型调用。

`chat` 命令可以使用 `--workspace <path>`，用于显示与记录当前工作区上下文。
