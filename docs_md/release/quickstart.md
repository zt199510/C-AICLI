# C# AI CLI Quickstart

## 1. Verify The Release

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe version
```

Expected shape:

```text
caicli 0.1.0
target framework: net9.0
release runtime: win-x64
```

## 2. Run Doctor

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe doctor
```

Without a configured key, `doctor` should still succeed and report:

```text
api key: missing (missing)
agent backend: direct (default)
approval mode: on-request (default)
agent backend status: available
```

## 3. Configure Chat

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

## 4. Send A Chat Prompt

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat "Say hello in one sentence."
```

With no model configured, the command returns non-zero and prints `localErrorCode: missing-model`.
After setting `OPENAI_MODEL` but leaving the key unset, it returns non-zero and prints `localErrorCode: missing-openai-api-key`.

## 5. Use A Session

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --session smoke "Remember this short note."
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --resume smoke "What note did I ask you to remember?"
```

`--session` creates or appends a transcript under `%USERPROFILE%\.caicli\sessions`.
`--resume` requires an existing transcript and sends normalized prior transcript context with the new prompt. Missing sessions fail safely with `errorCode: session-not-found`.

## 6. Use Project Instructions

Project instruction files are optional. In each directory, `AGENTS.md` is preferred when present; legacy `AICLI.md` is used only when `AGENTS.md` is absent. `chat` and agentic `exec` load instruction files from the workspace root to the selected target path, merge them root-to-leaf, and send the merged instructions with the model request.

Create a root instruction file:

```powershell
Set-Content -Path AGENTS.md -Value "Prefer concise answers for this workspace."
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --workspace . "Say hello in this project's style."
```

Use `--cwd <path>` to choose the instruction target path while `--workspace` remains the workspace root and tool boundary:

```powershell
New-Item -ItemType Directory -Force src\app | Out-Null
Set-Content -Path src\app\AGENTS.md -Value "For src/app, mention app-specific constraints."
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --workspace . --cwd src\app "Draft a short implementation note."
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --workspace . --cwd src\app "Draft a short implementation note without changing files."
```

If both `AGENTS.md` and `AICLI.md` exist in the same directory, only `AGENTS.md` is considered for that directory. There is no same-directory fallback to `AICLI.md` when `AGENTS.md` exists but is empty, invalid, or oversized. `doctor`, `config get`, and `config list` report instruction source paths and order without printing instruction contents.

## 7. Inspect Development Commands

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe status --workspace .
artifacts\release\caicli-0.1.0-win-x64\caicli.exe models --workspace .
artifacts\release\caicli-0.1.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.1.0-win-x64\caicli.exe diff --stat --workspace .
```

`status` reports workspace, git, and effective configuration state. `models` is local-only: it prints the current model, base URL, sources, and static examples without calling a model list API and without requiring an API key. `diff` prints the current git diff, or a stat summary with `--stat`.

Use `review` when you want model-assisted feedback on the current diff:

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe review --workspace .
artifacts\release\caicli-0.1.0-win-x64\caicli.exe review --json --workspace .
artifacts\release\caicli-0.1.0-win-x64\caicli.exe review --output json --workspace .
```

`review` is read-only for the workspace. It does not execute patch or shell tools and does not write workspace files, logs, transcripts, or patches. Diff collection may create transient temp files/directories outside the workspace and clean them up. It does send the current git diff to the configured model, so real use needs configured model credentials.

## 8. Inspect Optional Features

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.1.0-win-x64\caicli.exe mcp doctor
artifacts\release\caicli-0.1.0-win-x64\caicli.exe workflow list
```

MCP config/list/doctor are available. User-configured stdio MCP servers can be discovered and called through ordinary registry/tool paths, while workspace-configured MCP servers are not auto-started by `tools list`, `tools call`, `exec`, or `run`. `mcp doctor` can explicitly diagnose configured stdio servers. Remote/http MCP and Gerber/TIFF workflow execution remain enhanced Deferred capabilities.

## 9. Run An Agentic Exec Task

`exec` is the agentic v1 surface and contract. It is routed through `IAgentRunner`, emits model/tool/final/error events in the newline-delimited `--json` stream, supports loop limits and session transcripts, and returns exit code `0` on success, `1` on task failure, and `2` on argument error.

Today, the offline/fake agent loop is implemented and tested, but the default direct OpenAI SDK gateway does not yet continue real tool-call loops. With the default direct SDK backend, agentic `exec` tasks that need tool-call continuation return `agent-backend-unavailable` until SDK tool calls and tool results are translated.

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --workspace . "read README.md"
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --json --workspace . "read README.md"
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --output json --workspace . "read README.md"
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --output text --workspace . "read README.md"
```

Agent loop limits are available for non-interactive runs:

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --workspace . --max-turns 4 --max-tool-calls 8 --timeout-seconds 60 "inspect README.md"
```

Use `--session` to record the transcript, including agent tool calls and summarized tool results:

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --workspace . --session smoke-exec "inspect README.md"
artifacts\release\caicli-0.1.0-win-x64\caicli.exe exec --workspace . --resume smoke-exec "continue from the prior inspection"
```

`exec --resume <session>` follows the same existing-transcript requirement as `chat --resume` and passes prior transcript context into the agent request before the current task.

`exec` supports `--approval <mode>` to select the approval policy used when an agent loop reaches write or shell tool calls. This option is part of the `exec` contract, but with the default direct SDK backend, real tool-call continuation is still unavailable for shell/write tasks and may return `agent-backend-unavailable`. Use the `tools call --approval always workspace.run_shell` example below for an actionable shell smoke trial.

Without an approving mode, approval-gated write and shell actions are denied. In the non-interactive CLI, the default `on-request` mode reports `approvalStatus` `approval-required` because interactive approval is not available. `on-failure` also reports `approvalStatus` `approval-required` because sandbox retry escalation is not implemented. Approval-policy denials use `errorCode` `approval-denied`. The legacy `--approve` option remains supported and maps through the same risk-aware resolver.

Text output and `exec --json` events/results include `approvalStatus` for approval-gated tool activity.

## 10. Run A Local Smoke Task

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe run --workspace . --approve "create smoke note"
```

This deterministic smoke task creates or updates `caicli-smoke.txt` in the workspace. Under the default `on-request` approval mode, the write is denied without `--approve`.
`run` remains the deterministic direct-tool compatibility path for release smoke checks. It does not support `--approval`; configured `approvalMode` applies when `--approve` is not supplied, and the existing `--approve` option remains for compatibility.

## 11. Inspect And Call Tools

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools list --workspace .
```

Text `tools list` continues to print the enabled tools. For automation, use `--json`; the output is a stable object with `type: "tools.list"`, sorted `tools[]` entries, and sorted `disabledTools[]`. Each tool entry includes `name`, `description`, `riskLevel`, and normalized `parameters` schema metadata.

```powershell
$toolList = artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools list --workspace . --json | ConvertFrom-Json
$toolList.type
$toolList.tools | Select-Object name, riskLevel
$toolList.disabledTools
```

`tools call` accepts inline JSON, an argument file, or JSON from stdin. Use `--stdin` for pipeline-friendly calls:

```powershell
@{ path = "README.md" } | ConvertTo-Json -Compress | artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --stdin
```

`--stdin` cannot be combined with `--arguments-file` or positional inline JSON. For JSON-heavy tool calls on Windows PowerShell, an argument file remains a good option:

```powershell
Set-Content -Path args.json -Value '{"path":"README.md"}'
artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --arguments-file args.json
```

Read tools do not require approval. For approval-gated write or shell tool calls, use `--approval <mode>`:

```powershell
Set-Content -Path shell-args.json -Value '{"command":"dotnet --version","timeoutMilliseconds":10000}'
artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools call --workspace . --approval always workspace.run_shell --arguments-file shell-args.json
```

The legacy `--approve` option remains supported for compatibility. Dangerous shell commands are denied with `errorCode` `approval-denied` and `approvalStatus` `dangerous-shell-denied`, even under `--approval always` or legacy `--approve`.

Tool failures use stable centralized `errorCode` values, so scripts and agent/runtime consumers should key off `errorCode` and `approvalStatus` instead of parsing human-readable summaries. Tool results may also carry structured payloads internally for agent/runtime consumption; the CLI text output keeps the compatible `status`, `approvalStatus`, optional `errorCode`, and `summary` shape.

## 12. Manage Sessions

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session list
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session show smoke
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session rename smoke smoke-archive
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session export smoke-archive
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session export --format markdown smoke-archive
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session delete smoke-archive
```

`session list` prints local transcript summaries. `session show <name>` prints one summary or `session-not-found`. `session rename` updates the transcript file and metadata; missing source or destination conflict returns `session-rename-failed`. `session export` defaults to JSON raw transcript output, while `--format markdown` prints a readable redacted transcript. `session clear <name>` remains available for compatibility, but new docs and scripts should prefer `session delete <name>`.
