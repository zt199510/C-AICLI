# C# AI CLI Quickstart

## 1. Verify The Release

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe version
```

Expected shape:

```text
caicli 0.3.0
target framework: net9.0
release runtime: win-x64
```

## 2. Run Doctor

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe doctor
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
artifacts\release\caicli-0.3.0-win-x64\caicli.exe doctor --verbose
artifacts\release\caicli-0.3.0-win-x64\caicli.exe logs path
artifacts\release\caicli-0.3.0-win-x64\caicli.exe logs show --tail 20
```

`logs path` prints the resolved CLI log directory without creating it. `logs show` reads existing command logs (`yyyy-MM-dd.log`) and trace logs (`yyyy-MM-dd.trace.log`); a missing log directory succeeds with no output. To remove local CLI logs, `logs clear` deletes only direct `*.log` files in that resolved log directory.

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe logs clear
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
artifacts\release\caicli-0.3.0-win-x64\caicli.exe chat "Say hello in one sentence."
```

With no model configured, the command returns non-zero and prints `localErrorCode: missing-model`.
After setting `OPENAI_MODEL` but leaving the key unset, it returns non-zero and prints `localErrorCode: missing-openai-api-key`.

## 6. Use A Session

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe chat --session smoke "Remember this short note."
artifacts\release\caicli-0.3.0-win-x64\caicli.exe chat --resume smoke "What note did I ask you to remember?"
```

`--session` creates or appends a transcript under `%USERPROFILE%\.caicli\sessions`.
`--resume` requires an existing transcript and sends normalized prior transcript context with the new prompt. Missing sessions fail safely with `errorCode: session-not-found`.

## 7. Use Project Instructions

Project instruction files are optional. In each directory, `AGENTS.md` is preferred when present; legacy `AICLI.md` is used only when `AGENTS.md` is absent. `chat` and agentic `exec` load instruction files from the workspace root to the selected target path, merge them root-to-leaf, and send the merged instructions with the model request.

Create a root instruction file:

```powershell
Set-Content -Path AGENTS.md -Value "Prefer concise answers for this workspace."
artifacts\release\caicli-0.3.0-win-x64\caicli.exe chat --workspace . "Say hello in this project's style."
```

Use `--cwd <path>` to choose the instruction target path while `--workspace` remains the workspace root and tool boundary:

```powershell
New-Item -ItemType Directory -Force src\app | Out-Null
Set-Content -Path src\app\AGENTS.md -Value "For src/app, mention app-specific constraints."
artifacts\release\caicli-0.3.0-win-x64\caicli.exe chat --workspace . --cwd src\app "Draft a short implementation note."
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . --cwd src\app "Draft a short implementation note without changing files."
```

If both `AGENTS.md` and `AICLI.md` exist in the same directory, only `AGENTS.md` is considered for that directory. There is no same-directory fallback to `AICLI.md` when `AGENTS.md` exists but is empty, invalid, or oversized. `doctor`, `config get`, and `config list` report instruction source paths and order without printing instruction contents.

## 8. Inspect Development Commands

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe status --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe models --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --output json --workspace .
```

`status` reports workspace, git, and effective configuration state. `models` is local-only: it prints the current model, base URL, sources, and static examples without calling a model list API and without requiring an API key. `diff` prints the current git diff, or a stat summary with `--stat`. `changes` is a read-only changes view that combines git status/diff stat, changed files, and optional session task report data without calling a model or running shell/patch tools.

To include the latest task report from an `exec --session` transcript:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --workspace . --session smoke-exec
```

Use `review` when you want model-assisted feedback on the current diff:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe review --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe review --json --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe review --output json --workspace .
```

`review` is read-only for the workspace. It does not execute patch or shell tools and does not write workspace files, logs, transcripts, or patches. Diff collection may create transient temp files/directories outside the workspace and clean them up. It does send the current git diff to the configured model, so real use needs configured model credentials.

## 9. Inspect Optional Features

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.3.0-win-x64\caicli.exe mcp doctor
artifacts\release\caicli-0.3.0-win-x64\caicli.exe workflow list
```

MCP config/list/doctor are available. User-configured stdio MCP servers can be discovered and called through ordinary registry/tool paths, while workspace-configured MCP servers are not auto-started by `tools list`, `tools call`, `exec`, or `run`. `mcp doctor` can explicitly diagnose configured stdio servers. Remote/http MCP and Gerber/TIFF workflow execution remain enhanced Deferred capabilities.

## 10. Run An Agentic Exec Task

`exec` is the agentic v1 surface and contract. It is routed through `IAgentRunner`, emits model/tool/final/error/review/report events in the newline-delimited `--json` stream, supports loop limits and session transcripts, and returns exit code `0` on success, `1` on task failure, and `2` on argument error.

At startup, `exec` builds bounded task context before write-capable work begins. The collected context includes the resolved workspace/cwd, project instruction source list, session or resume state, and a bounded git status/diff summary. The generated plan is emitted as a traceable `plan` event so text, NDJSON, session, and trace consumers can correlate the task goal, candidate files, expected tools, and risks before later model/tool events.

`exec` also accepts bounded inline workflow references:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "Review parser flow in @folder:src/CSharpAiCli.Core/Agents"
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
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --report markdown --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --report markdown --report-path .caicli\reports\latest.md --workspace . "Review @folder:src/CSharpAiCli.Core"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --output json --report markdown --workspace . "Summarize @file:docs_md\release\capability_status.md"
```

Use `--expert` to select built-in local profiles. `bugfix`, `tester`, and `refactor` adjust guidance and report focus without expanding permissions. `reviewer` and `security` are read-only: patch, shell, and MCP tools are disabled and MCP discovery is skipped.

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --expert bugfix --workspace . "Fix this using @file:src\App.cs"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --expert reviewer --workspace . "Review @folder:src\CSharpAiCli.Core"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --expert security --report markdown --workspace . "Audit @folder:src\CSharpAiCli.Core\Tools"
```

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "read README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --json --workspace . "read README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --output json --workspace . "read README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --output text --workspace . "read README.md"
```

After model access is configured, pass `--trace` or set `CAICLI_TRACE=1` to write trace-level local JSONL diagnostics for `exec`:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --trace --workspace . "read README.md"
$env:CAICLI_TRACE = "1"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "read README.md"
```

The text result prints `tracePath` when a trace file is written. You can also inspect the latest trace log through the log reader:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe logs show --tail 80
```

For a read-only real agent trial, keep approval disabled and ask for a bounded inspection task:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --trace --workspace . --approval never --max-turns 2 --max-tool-calls 2 --timeout-seconds 60 "Use workspace.read_text to read README.md, then summarize it in one sentence. Do not modify files or run shell commands."
```

This uses the direct OpenAI Responses SDK agent tool loop. The model may request registered local tools, but the CLI still applies workspace guard, disabled-tool checks, approval policy, shell policy, dangerous-command detection, loop limits, and timeout limits before any tool runs.

Agent loop limits are available for non-interactive runs:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . --max-turns 4 --max-tool-calls 8 --timeout-seconds 60 "inspect README.md"
```

Use `--session` to record the transcript, including agent tool calls and summarized tool results:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . --session smoke-exec "inspect README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . --resume smoke-exec "continue from the prior inspection"
```

`exec --resume <session>` follows the same existing-transcript requirement as `chat --resume` and passes prior transcript context into the agent request before the current task.

`exec` supports `--approval <mode>` to select the approval policy used when an agent loop reaches write or shell tool calls. Use `--approval never` for read-only real model trials, and use the `tools call --approval always workspace.run_shell` example below for a deterministic local shell smoke trial.

Without an approving mode, approval-gated write and shell actions are denied. In the non-interactive CLI, the default `on-request` mode reports `approvalStatus` `approval-required` because interactive approval is not available. `on-failure` also reports `approvalStatus` `approval-required` because sandbox retry escalation is not implemented. Approval-policy denials use `errorCode` `approval-denied`. The legacy `--approve` option remains supported and maps through the same risk-aware resolver.

Text output and `exec --json` events/results include `approvalStatus` for approval-gated tool activity.

After an agentic task, review the local change set before committing or copying results:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe review --workspace .
```

`exec` records a read-only `review.gate`, a final `taskReport`, and optional markdown report metadata, but those are diagnostics. They do not replace manually reviewing the actual diff.

## 11. Run A Local Smoke Task

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe run --workspace . --approve "create smoke note"
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
artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools list --workspace .
```

Text `tools list` continues to print the enabled tools. For automation, use `--json`; the output is a stable object with `type: "tools.list"`, sorted `tools[]` entries, and sorted `disabledTools[]`. Each tool entry includes `name`, `description`, `riskLevel`, and normalized `parameters` schema metadata.

```powershell
$toolList = artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools list --workspace . --json | ConvertFrom-Json
$toolList.type
$toolList.tools | Select-Object name, riskLevel
$toolList.disabledTools
```

`tools call` accepts inline JSON, an argument file, or JSON from stdin. Use `--stdin` for pipeline-friendly calls:

```powershell
@{ path = "README.md" } | ConvertTo-Json -Compress | artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --stdin
```

`--stdin` cannot be combined with `--arguments-file` or positional inline JSON. For JSON-heavy tool calls on Windows PowerShell, an argument file remains a good option:

```powershell
Set-Content -Path args.json -Value '{"path":"README.md"}'
artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --arguments-file args.json
```

Read tools do not require approval. For approval-gated write or shell tool calls, use `--approval <mode>`:

```powershell
Set-Content -Path shell-args.json -Value '{"command":"dotnet --version","timeoutMilliseconds":10000}'
artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools call --workspace . --approval always workspace.run_shell --arguments-file shell-args.json
```

The legacy `--approve` option remains supported for compatibility. Dangerous shell commands are denied with `errorCode` `approval-denied` and `approvalStatus` `dangerous-shell-denied`, even under `--approval always` or legacy `--approve`.

Tool failures use stable centralized `errorCode` values, so scripts and agent/runtime consumers should key off `errorCode` and `approvalStatus` instead of parsing human-readable summaries. Tool results may also carry structured payloads internally for agent/runtime consumption; the CLI text output keeps the compatible `status`, `approvalStatus`, optional `errorCode`, and `summary` shape.

## 13. Manage Sessions

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session list
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session show smoke
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session rename smoke smoke-archive
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session export smoke-archive
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session export --format markdown smoke-archive
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session delete smoke-archive
```

`session list` prints local transcript summaries. `session show <name>` prints one summary or `session-not-found`. `session rename` updates the transcript file and metadata; missing source or destination conflict returns `session-rename-failed`. `session export` defaults to JSON raw transcript output, while `--format markdown` prints a readable redacted transcript. `session clear <name>` remains available for compatibility, but new docs and scripts should prefer `session delete <name>`.
