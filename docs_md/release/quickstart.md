# C# AI CLI Quickstart

## 1. Verify The Release

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe version
```

Expected shape:

```text
caicli 0.4.0
target framework: net9.0
release runtime: win-x64
```

## 2. Run Doctor

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe doctor
```

Without a configured key, `doctor` should still succeed and report:

```text
api key: missing (missing)
agent backend: direct (default)
approval mode: on-request (default)
agent backend status: available
```

## 3. Inspect Diagnostics And Logs

Global `--verbose` works on subcommands and prints safe human-readable diagnostics for text output. Commands that emit JSON keep stdout JSON-clean.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe doctor --verbose
artifacts\release\caicli-0.4.0-win-x64\caicli.exe logs path
artifacts\release\caicli-0.4.0-win-x64\caicli.exe logs show --tail 20
```

`logs path` prints the resolved CLI log directory without creating it. `logs show` reads existing command logs (`yyyy-MM-dd.log`) and trace logs (`yyyy-MM-dd.trace.log`); a missing log directory succeeds with no output. To remove local CLI logs, `logs clear` deletes only direct `*.log` files in that resolved log directory.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe logs clear
```

Trace-level `exec` diagnostics are shown later, after model access is configured. Trace logs are redacted and record key presence/source, not raw key values.

## 4. Configure Chat

For a one-session environment setup:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
```

Or create `%USERPROFILE%\.caicli\config.json`:

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "<your key>",
  "agentBackend": "direct",
  "approvalMode": "on-request"
}
```

`approvalMode` can be `never`, `on-request`, `on-failure`, or `always`. User config takes priority over workspace config, and the default is `on-request`. There is no environment variable for approval mode. `config set approvalMode` is not implemented; edit the JSON file directly.

## 5. Send A Chat Prompt

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe chat "Say hello in one sentence."
```

With no model configured, the command returns non-zero and prints `localErrorCode: missing-model`.
After setting `OPENAI_MODEL` but leaving the key unset, it returns non-zero and prints `localErrorCode: missing-openai-api-key`.

## 6. Use A Session

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe chat --session smoke "Remember this short note."
artifacts\release\caicli-0.4.0-win-x64\caicli.exe chat --resume smoke "What note did I ask you to remember?"
```

`--session` creates or appends a transcript under `%USERPROFILE%\.caicli\sessions`.
`--resume` requires an existing transcript and sends normalized prior transcript context with the new prompt. Missing sessions fail safely with `errorCode: session-not-found`.

## 7. Use Project Instructions

Project instruction files are optional. In each directory, `AGENTS.md` is preferred when present; legacy `AICLI.md` is used only when `AGENTS.md` is absent. `chat` and agentic `exec` load instruction files from the workspace root to the selected target path, merge them root-to-leaf, and send the merged instructions with the model request.

Create a root instruction file:

```powershell
Set-Content -Path AGENTS.md -Value "Prefer concise answers for this workspace."
artifacts\release\caicli-0.4.0-win-x64\caicli.exe chat --workspace . "Say hello in this project's style."
```

Use `--cwd <path>` to choose the instruction target path while `--workspace` remains the workspace root and tool boundary:

```powershell
New-Item -ItemType Directory -Force src\app | Out-Null
Set-Content -Path src\app\AGENTS.md -Value "For src/app, mention app-specific constraints."
artifacts\release\caicli-0.4.0-win-x64\caicli.exe chat --workspace . --cwd src\app "Draft a short implementation note."
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . --cwd src\app "Draft a short implementation note without changing files."
```

If both `AGENTS.md` and `AICLI.md` exist in the same directory, only `AGENTS.md` is considered for that directory. There is no same-directory fallback to `AICLI.md` when `AGENTS.md` exists but is empty, invalid, or oversized. `doctor`, `config get`, and `config list` report instruction source paths and order without printing instruction contents.

## 8. Inspect Development Commands

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe status --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe models --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --output json --workspace .
```

`status` reports workspace, git, and effective configuration state. `models` is local-only: it prints the current model, base URL, sources, and static examples without calling a model list API and without requiring an API key. `diff` prints the current git diff, or a stat summary with `--stat`. `changes` is a read-only changes view that combines git status/diff stat, changed files, and optional session task report data without calling a model or running shell/patch tools.

To include the latest task report from an `exec --session` transcript:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --workspace . --session smoke-exec
```

Use `review` when you want model-assisted feedback on the current diff:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe review --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe review --json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe review --output json --workspace .
```

`review` is read-only for the workspace. It does not execute patch or shell tools and does not write workspace files, logs, transcripts, or patches. Diff collection may create transient temp files/directories outside the workspace and clean them up. It does send the current git diff to the configured model, so real use needs configured model credentials.

## 9. Inspect Optional Features

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe mcp doctor
artifacts\release\caicli-0.4.0-win-x64\caicli.exe workflow list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills run review-only --dry-run -- "@file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline plan security-review --workspace . -- "@folder:src"
```

MCP config/list/doctor are available. User-configured stdio MCP servers can be discovered and called through ordinary registry/tool paths, while workspace-configured MCP servers are not auto-started by `tools list`, `tools call`, `exec`, or `run`. `mcp doctor` can explicitly diagnose configured stdio servers.

Local skills are lightweight workflow packs. `skills list` shows built-in packs plus workspace-local JSON manifests under `.caicli/skills`. `skills run <name> --dry-run -- <task>` expands the selected pack into expert/report/reference/safety/validation metadata without calling a model, writing files, running shell, or starting MCP. Non-dry-run `skills run` uses the same agentic safety path as `exec`; pack validation commands are hints and do not execute directly. Remote skill marketplaces, automatic updates, YAML manifests, and user-level skill directories remain enhanced Deferred capabilities. Project Packs are a separate deterministic domain-tool surface and are not model-guided skills.

The future 0.5.0 source tree also contains a Project Pack Preview. These commands are not part of the accepted
0.4.0 package:

```powershell
caicli packs list --output json --workspace .
caicli packs doctor gerber-tiff --output json --workspace .
caicli packs doctor gerber-tiff --tool-path "gerbv=C:\Tools\gerbv\gerbv.exe" --probe --approval always --workspace .
caicli packs plan gerber-tiff --input .\samples\board-a --output-dir .\out\board-a --output json --workspace .
```

`packs plan` accepts one explicit workspace directory, never creates `--output-dir`, and never runs conversion.
`--output` selects only `text` or `json`. With no configured tool identities it can still return a deterministic
`ready-for-staging` fingerprint, but returns exit code `1` and `runnable=false`. Supplying static `--tool-path`
bindings only inspects regular-file identity and SHA256; it does not start the tools. Only explicit
`packs doctor --probe` may start a fixed version probe, and it still requires current approval. Plan output uses
workspace-relative paths and does not store raw Gerber, drill, sidecar, or unknown file content.

Week 61 source Preview adds controlled Gerber -> TIFF execution. Save the JSON plan without changing it, then
choose dry-run staging or an explicit real conversion:

```powershell
$gerbv = "C:\Tools\gerbv\gerbv.exe"
$magick = "C:\Tools\ImageMagick\magick.exe"

caicli packs plan gerber-tiff `
  --input .\samples\board-a --output-dir .\out\board-a `
  --tool-path "gerbv=$gerbv" "imagemagick=$magick" `
  --output json --workspace . | Set-Content .\gerber-tiff-plan.json -Encoding utf8

caicli packs run gerber-tiff `
  --plan .\gerber-tiff-plan.json --dry-run `
  --tool-path "gerbv=$gerbv" "imagemagick=$magick" `
  --output json --workspace .

caicli packs run gerber-tiff `
  --plan .\gerber-tiff-plan.json `
  --tool-path "gerbv=$gerbv" "imagemagick=$magick" `
  --approval always --output json --workspace .
```

The real run performs fixed Gerbv PNG render and ImageMagick TIFF encode templates with no free-form arguments.
It re-probes both tools, requests current approval for every external invocation, uses the managed run cwd/temp
area, refuses existing outputs, captures bounded/redacted process evidence, and records job/run/artifact pointers.
A successful Week 61 run stops in `verifying`; this means conversion executed and declared output hashes were
recorded. It does not mean TIFF metadata/content/baseline verification passed, and it cannot enter
`awaiting-acceptance` until the Week 62 verifier exists.

Local job history is opt-in for execution commands. `jobs list/show/export` reads job records from the user-level store under `%USERPROFILE%\.caicli\jobs`; these read commands do not call a model, run shell/patch tools, start MCP, write command logs, or write workspace files.

## 10. Run An Agentic Exec Task

`exec` is the agentic v1 surface and contract. It is routed through `IAgentRunner`, emits model/tool/final/error/review/report events in the newline-delimited `--json` stream, supports loop limits and session transcripts, and returns exit code `0` on success, `1` on task failure, and `2` on argument error.

At startup, `exec` builds bounded task context before write-capable work begins. The collected context includes the resolved workspace/cwd, project instruction source list, session or resume state, and a bounded git status/diff summary. The generated plan is emitted as a traceable `plan` event so text, NDJSON, session, and trace consumers can correlate the task goal, candidate files, expected tools, and risks before later model/tool events.

`exec` also accepts bounded inline workflow references:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "Review parser flow in @folder:src/CSharpAiCli.Core/Agents"
```

`@file:<path>` and `@folder:<path>` are resolved inside the workspace before the model runs. Explicit files outside the workspace, missing files, and binary files fail safely. Folder references are recursive but bounded by file count, total bytes, single-file bytes, and depth, and common generated or private directories such as `.git`, `.caicli`, `bin`, `obj`, and `node_modules` are skipped. Reference metadata appears in text, JSON, trace, session, and `taskReport`; raw referenced content is not written into reports.

The offline/fake agent loop and the direct OpenAI Responses SDK path share the same tool-call contract. With model access configured, `exec` can expose local tool schemas to the model, execute requested tools, write structured tool results back, and continue to a final response. Normal tests do not require network access; the release smoke script only runs the real model path when `CAICLI_REAL_MODEL_SMOKE=1` is set with caller-provided `OPENAI_API_KEY` and `OPENAI_MODEL`.

After a successful `workspace.apply_patch` tool call, `exec` records patch lifecycle events (`patch.preview`, `patch.approval`, and `patch.apply`), collects a `changed.files` summary from git status/diff, and adds changed-file details to text, NDJSON, trace, and session run summaries. If an explicit verification command is configured, `exec` runs it through `workspace.run_shell` and records `verification.result`; the shell approval mode, shell policy allowlist/denylist, timeout cap, dangerous-command detector, cwd guard, and output truncation rules still apply.

Automatic verification uses only explicit commands. Project instructions take priority when they contain a line such as `VerificationCommand: dotnet test` or `ValidationCommand: dotnet test`. If instructions do not define a command, a single unambiguous workflow profile `validationCommand` can be used. When no explicit command exists, `exec` records verification as skipped and does not guess a build or test command.

At completion, `exec` runs a read-only `review.gate` over the final git diff summary. The gate uses the read-only `git.diff` tool path, does not call patch or shell tools, and does not write workspace files. The event payload includes `readOnly=true`, `toolName=git.diff`, `hasDiff`, and `truncated`.

Every agentic `exec` run also records a final `AgentTaskReport`. Text output includes the report-derived `expert`, `reportMode`, `reportPath`, `references`, `changedFiles`, `commands`, `verificationStatus`, `remainingRisks`, and `tracePath` fields when available. JSON output emits a `taskReport` event and includes the full `payload.taskReport` object on the terminal `exec.result`. The report payload contains status/stop reason, prompt, plan, tools, workflow reference metadata, expert metadata, report artifact metadata, changed files, commands, verification, risks, trace path, and optional review gate data.

When `--trace` is enabled, the task report is written into the trace result payload. When `--session` is used, the report is attached to the session transcript's agent run summary. Report redaction records secret presence/source/kind only and never raw secret values.

Use `--report markdown` to request a full markdown report. In text mode the report is printed after the ordinary exec event/result output. In JSON mode the NDJSON stream remains machine-readable and carries `report.generated` plus `payload.taskReport.report` metadata instead of raw markdown. Use `--report-path` only when you want an explicit workspace file; existing files and workspace escapes are rejected.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --report markdown --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --report markdown --report-path .caicli\reports\latest.md --workspace . "Review @folder:src/CSharpAiCli.Core"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --output json --report markdown --workspace . "Summarize @file:docs_md\release\capability_status.md"
```

Use `--expert` to select built-in local profiles. `bugfix`, `tester`, and `refactor` adjust guidance and report focus without expanding permissions. `reviewer` and `security` are read-only: patch, shell, and MCP tools are disabled and MCP discovery is skipped.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --expert bugfix --workspace . "Fix this using @file:src\App.cs"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --expert reviewer --workspace . "Review @folder:src\CSharpAiCli.Core"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --expert security --report markdown --workspace . "Audit @folder:src\CSharpAiCli.Core\Tools"
```

Use `--record-job` when you want a local job history entry for an exec run. `--job-name` is an optional label; the unique id is generated by the runtime.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --record-job --job-name readme-review --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs show <job-id> --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs export <job-id> --format markdown
```

Job records store redacted metadata and artifact pointers only. They do not store raw referenced file contents, raw tool arguments, raw secrets, or full diffs.

Skill runs use the same opt-in flags. A normal dry-run remains non-persistent; `skills run --record-job --dry-run` writes only compact plan metadata to the user-level job store and still does not call a model, run tools, create a report, save a session, or write the workspace.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills run review-only --record-job --job-name readme-plan --dry-run --workspace . -- "Review @file:README.md"
```

Queue requests are local user-level records. Adding an item does not execute it. Running an item later uses the stored workspace/task metadata but resolves the current approval mode, disabled tools, shell policy, workspace/dirty checks, MCP startup policy, and skill/expert boundary through the existing execution command.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue add exec --workspace . -- "Review @folder:src"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue add skill review-only --workspace . -- "Review @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue show <queue-id> --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue run <queue-id>
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue cancel <queue-id>
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue cleanup --status succeeded --older-than-days 30
```

`queue run` always creates a job record and stores its id on the attempt. Failed items can be run again as a new attempt. Cancel is pending-only. Cleanup accepts only succeeded, failed, or canceled queue status and does not delete job history or artifacts.

Built-in multi-role pipelines compose the existing expert, skill, queue, job, exec, and task-report paths. `pipeline list` and `pipeline plan` are local plan-only commands: they do not call a model, run tools, start MCP, create queue/job records, or write the workspace. The built-ins are `fix-review-test`, `review-test`, and `security-review`.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline plan fix-review-test --workspace . -- "Fix failing tests"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline run fix-review-test --workspace . -- "Fix failing tests"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline run security-review --report markdown --workspace . -- "@folder:src"
```

`pipeline run` executes roles sequentially. Every role creates a queue attempt and linked job, then re-enters `exec` or `skills run`; approval, workspace and dirty-workspace checks, shell policy, disabled tools, MCP startup policy, trace/session/report flow, and credential checks are unchanged. Reviewer and security roles remain read-only, with patch, shell, MCP tools, and MCP discovery disabled. A role failure stops later roles but preserves completed role reports and artifact pointers in the final text/JSON/markdown aggregate report.

Pipeline v1 uses fixed local role definitions and the caller's configured provider/model. It does not perform automatic model routing, provider assignment, parallel workers, background scheduling, or remote collaboration. Without model credentials, `pipeline run` returns a stable failed report with queue/job/task-report evidence; `list` and `plan` remain credential-free.

## Workspace-local Automation

Place strict JSON manifests under `.caicli/automations`. A manifest selects a queue, skill, or built-in pipeline target and declares its trigger and safety envelope. Manifests are data only: they cannot contain scripts or approval overrides. A `schedule` trigger is preview metadata and is never executed automatically.

```json
{
  "schemaVersion": 1,
  "name": "nightly-review",
  "description": "Review the workspace using a local schedule preview.",
  "trigger": {
    "type": "schedule",
    "schedule": "0 2 * * *",
    "timeZone": "UTC"
  },
  "target": {
    "type": "skill",
    "name": "review-only",
    "task": "Review @folder:src",
    "report": "none"
  },
  "safety": {
    "manualOnly": true,
    "allowWrites": false,
    "allowShell": false,
    "allowMcp": false
  }
}
```

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation list --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation validate --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation plan nightly-review --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation run nightly-review --dry-run --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation run nightly-review --manual --workspace .
```

List, validate, plan, and dry-run are credential-free and do not call a model, run tools, start MCP, create queue/job records, or write the workspace. Manual run delegates to the existing queue or pipeline path, so approval, workspace and dirty-workspace checks, shell policy, disabled tools, MCP startup policy, trace/session/report, and model credentials are unchanged. Manual runs record redacted automation correlation in queue/job metadata and an inline job artifact pointer.

C-AICLI does not include a background scheduler, Windows Task Scheduler registration, queue daemon worker, API/webhook execution trigger, remote execution, or team automation. The separate local API Preview described below is read-only and cannot run an automation. To run a schedule preview, a user must still invoke `automation run --manual` explicitly.

## Provider-neutral CI Artifacts

After an exec, queue, pipeline role, or manual automation has produced a job record, generate stable JSON or markdown without model credentials:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe ci summarize --job <job-id> --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe ci summarize --job <job-id> --markdown-path .caicli\reports\ci-summary.md --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe ci check --job <job-id> --fail-on risks --workspace .
```

`ci summarize` returns `0` after artifact generation and records the source result in `check.outcome` plus `check.recommendedExitCode`. `ci check` returns `0` for success/default warnings, `1` for a failed source job or selected `--fail-on` threshold, and `2` for missing/corrupt input or an unsafe source boundary. Explicit markdown paths stay inside the workspace and never overwrite an existing file.

CI artifacts include bounded redacted job/task-report summaries, annotations, correlation, and artifact pointers. They exclude raw reference contents, raw tool arguments, verification command details, raw secrets, and full diffs. See `docs_md/release/ci_artifacts.md` for the v1 schema and manual GitHub Actions/Azure DevOps YAML. C-AICLI does not call provider APIs, create PR comments, send callbacks, or upload artifacts.

## Local API / Daemon Preview

Inspect the default-off boundary and route contract without starting a listener:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe daemon doctor --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe api routes --output json
```

Explicitly start the read-only Preview on IPv4 loopback:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe daemon start --preview --bind 127.0.0.1 --port 8787 --workspace .
```

In another terminal, check the already-running process or read the v1 endpoints:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe api smoke --port 8787
Invoke-RestMethod http://127.0.0.1:8787/v1/health
Invoke-RestMethod http://127.0.0.1:8787/v1/jobs?limit=50
Invoke-RestMethod http://127.0.0.1:8787/v1/queue?limit=50
```

`--preview` is mandatory. Only `localhost` and `127.0.0.1` are accepted as bind and HTTP Host values, and the listener always uses `127.0.0.1`; `0.0.0.0`, IPv6, hostnames, LAN, and public addresses are rejected. Requests, headers, and connections are bounded, but this Preview still has no authentication or TLS and must not be exposed through a reverse proxy or port forward. It exposes bounded redacted job/queue metadata, corrupt-record diagnostics, and local artifact pointers, not artifact contents. Control routes, SSE, queue workers, remote access, service installation, and browser UI are Deferred. See `docs_md/release/local_api_daemon_preview.md` for the threat model and route contract.

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "read README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --json --workspace . "read README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --output json --workspace . "read README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --output text --workspace . "read README.md"
```

After model access is configured, pass `--trace` or set `CAICLI_TRACE=1` to write trace-level local JSONL diagnostics for `exec`:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --trace --workspace . "read README.md"
$env:CAICLI_TRACE = "1"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "read README.md"
```

The text result prints `tracePath` when a trace file is written. You can also inspect the latest trace log through the log reader:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe logs show --tail 80
```

For a read-only real agent trial, keep approval disabled and ask for a bounded inspection task:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --trace --workspace . --approval never --max-turns 2 --max-tool-calls 2 --timeout-seconds 60 "Use workspace.read_text to read README.md, then summarize it in one sentence. Do not modify files or run shell commands."
```

This uses the direct OpenAI Responses SDK agent tool loop. The model may request registered local tools, but the CLI still applies workspace guard, disabled-tool checks, approval policy, shell policy, dangerous-command detection, loop limits, and timeout limits before any tool runs.

Agent loop limits are available for non-interactive runs:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . --max-turns 4 --max-tool-calls 8 --timeout-seconds 60 "inspect README.md"
```

Use `--session` to record the transcript, including agent tool calls and summarized tool results:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . --session smoke-exec "inspect README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . --resume smoke-exec "continue from the prior inspection"
```

`exec --resume <session>` follows the same existing-transcript requirement as `chat --resume` and passes prior transcript context into the agent request before the current task.

`exec` supports `--approval <mode>` to select the approval policy used when an agent loop reaches write or shell tool calls. Use `--approval never` for read-only real model trials, and use the `tools call --approval always workspace.run_shell` example below for a deterministic local shell smoke trial.

Without an approving mode, approval-gated write and shell actions are denied. In the non-interactive CLI, the default `on-request` mode reports `approvalStatus` `approval-required` because interactive approval is not available. `on-failure` also reports `approvalStatus` `approval-required` because sandbox retry escalation is not implemented. Approval-policy denials use `errorCode` `approval-denied`. The legacy `--approve` option remains supported and maps through the same risk-aware resolver.

Text output and `exec --json` events/results include `approvalStatus` for approval-gated tool activity.

After an agentic task, review the local change set before committing or copying results:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe review --workspace .
```

`exec` records a read-only `review.gate`, a final `taskReport`, and optional markdown report metadata, but those are diagnostics. They do not replace manually reviewing the actual diff.

## 11. Run A Local Smoke Task

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe run --workspace . --approve "create smoke note"
```

This deterministic smoke task creates or updates `caicli-smoke.txt` in the workspace. Under the default `on-request` approval mode, the write is denied without `--approve`.
`run` remains the deterministic direct-tool compatibility path for release smoke checks. It does not support `--approval`; configured `approvalMode` applies when `--approve` is not supplied, and the existing `--approve` option remains for compatibility.

Default smoke validation stays offline and deterministic. The real model smoke is opt-in:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
$env:CAICLI_REAL_MODEL_SMOKE = "1"
tools\Invoke-SmokeTests.ps1
```

When `CAICLI_REAL_MODEL_SMOKE` is unset, the smoke script prints that real model smoke is skipped. When it is set but either `OPENAI_API_KEY` or `OPENAI_MODEL` is missing, the real model portion is also skipped. These skip paths are intentional so normal smoke runs do not require network access or credentials.

## 12. Inspect And Call Tools

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools list --workspace .
```

Text `tools list` continues to print the enabled tools. For automation, use `--json`; the output is a stable object with `type: "tools.list"`, sorted `tools[]` entries, and sorted `disabledTools[]`. Each tool entry includes `name`, `description`, `riskLevel`, and normalized `parameters` schema metadata.

```powershell
$toolList = artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools list --workspace . --json | ConvertFrom-Json
$toolList.type
$toolList.tools | Select-Object name, riskLevel
$toolList.disabledTools
```

`tools call` accepts inline JSON, an argument file, or JSON from stdin. Use `--stdin` for pipeline-friendly calls:

```powershell
@{ path = "README.md" } | ConvertTo-Json -Compress | artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --stdin
```

`--stdin` cannot be combined with `--arguments-file` or positional inline JSON. For JSON-heavy tool calls on Windows PowerShell, an argument file remains a good option:

```powershell
Set-Content -Path args.json -Value '{"path":"README.md"}'
artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --arguments-file args.json
```

Read tools do not require approval. For approval-gated write or shell tool calls, use `--approval <mode>`:

```powershell
Set-Content -Path shell-args.json -Value '{"command":"dotnet --version","timeoutMilliseconds":10000}'
artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools call --workspace . --approval always workspace.run_shell --arguments-file shell-args.json
```

The legacy `--approve` option remains supported for compatibility. Dangerous shell commands are denied with `errorCode` `approval-denied` and `approvalStatus` `dangerous-shell-denied`, even under `--approval always` or legacy `--approve`.

Tool failures use stable centralized `errorCode` values, so scripts and agent/runtime consumers should key off `errorCode` and `approvalStatus` instead of parsing human-readable summaries. Tool results may also carry structured payloads internally for agent/runtime consumption; the CLI text output keeps the compatible `status`, `approvalStatus`, optional `errorCode`, and `summary` shape.

## 13. Manage Sessions

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session show smoke
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session rename smoke smoke-archive
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session export smoke-archive
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session export --format markdown smoke-archive
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session delete smoke-archive
```

`session list` prints local transcript summaries. `session show <name>` prints one summary or `session-not-found`. `session rename` updates the transcript file and metadata; missing source or destination conflict returns `session-rename-failed`. `session export` defaults to JSON raw transcript output, while `--format markdown` prints a readable redacted transcript. `session clear <name>` remains available for compatibility, but new docs and scripts should prefer `session delete <name>`.
