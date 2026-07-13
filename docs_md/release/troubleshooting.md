# Troubleshooting

## Model Configuration

Run `doctor` first:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe doctor --verbose
```

Common model setup failures:

- `missing-model`: set `OPENAI_MODEL` or user config `model`.
- `missing-openai-api-key`: set `OPENAI_API_KEY` or user config `apiKey`.
- Invalid `baseUrl`: use an absolute `http` or `https` URL without user info, query, or fragment.
- Workspace `apiKey` is ignored by design. Move the key to `OPENAI_API_KEY` or user config.

Use `models --workspace .` to inspect the effective model and base URL without making a network call.

## Agent Exec

For a safe real agent check, start with a bounded read-only task:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --trace --workspace . --approval never --max-turns 2 --max-tool-calls 2 --timeout-seconds 60 "Use workspace.read_text to read README.md, then summarize it in one sentence. Do not modify files or run shell commands."
```

If the task stops early, inspect:

- `approvalStatus`: whether a write or shell tool was denied.
- `errorCode`: stable machine-readable failure code.
- `stopReason`: terminal agent stop reason.
- `changedFiles`, `commands`, `verificationStatus`, and `remainingRisks` in the final task report.
- `tracePath` when `--trace` or `CAICLI_TRACE=1` was used.

Loop-limit failures usually mean the prompt needs a smaller task, or `--max-turns`, `--max-tool-calls`, or `--timeout-seconds` needs a deliberate increase.

## Workflow References

Use `@file:<path>` and `@folder:<path>` only with `exec`:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "Inspect @file:README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . "Review @folder:src/CSharpAiCli.Core/Agents"
```

Common reference failures:

- `workflow-reference-boundary-denied`: keep the referenced path inside `--workspace`.
- `workflow-reference-not-found`: check the path relative to the workspace.
- `workflow-reference-binary-not-supported`: explicit `@file` references must be text.
- `workflow-reference-too-large`: the reference was bounded or truncated.

Reference failures happen before model execution. They do not call the model, run shell or patch tools, start MCP, or save a partial session agent run.

## Tool And Approval Failures

Read tools do not require approval. Patch and shell tools do.

- `approval-required`: the effective mode is `on-request` or `on-failure` in the non-interactive CLI. Use `--approval always` only when you intend to permit ordinary write or shell actions.
- `approval-denied`: the current policy denied an approval-gated action.
- `dangerous-shell-denied`: the command matched dangerous shell detection and is denied even under `--approval always` or `--approve`.
- `unknown-tool`: the tool is not registered, or an MCP server/tool was not available.
- `tool-disabled`: the tool is disabled through user or workspace `disabledTools`.
- Workspace boundary errors: keep file paths and shell cwd inside the selected `--workspace`.

For deterministic local shell checks, call the shell tool directly with an argument file:

```powershell
Set-Content -Path shell-args.json -Value '{"command":"dotnet --version","timeoutMilliseconds":10000}'
artifacts\release\caicli-0.3.0-win-x64\caicli.exe tools call --workspace . --approval always workspace.run_shell --arguments-file shell-args.json
```

## Verification

Agentic `exec` runs verification only when an explicit command is configured. Add one of these to project instructions when appropriate:

```text
VerificationCommand: dotnet test
```

or:

```text
ValidationCommand: dotnet test
```

If no explicit command exists, verification is reported as skipped. The CLI does not guess build or test commands from repository contents.

## Review Gate And Diff Review

Each `exec` run records a read-only `review.gate` event and final `taskReport`. These outputs summarize the final diff and risks, but they do not prove the change is correct.

Always inspect the local diff after write-capable work:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe changes --session smoke-exec --workspace .
artifacts\release\caicli-0.3.0-win-x64\caicli.exe review --workspace .
```

`review` sends the current git diff to the configured model. It is read-only for the workspace and does not run shell or patch tools.

`changes` is local and read-only. It does not call a model and can still return exit code `0` for clean workspaces, non-git workspaces, or missing session task reports while surfacing warnings in text/JSON output.

## Traces, Logs, And Sessions

Enable trace diagnostics for agent work:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --trace --workspace . "read README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe logs show --tail 80
```

Trace and command logs are redacted, but they can still contain prompts, paths, summaries, and diffs. Treat them as sensitive local diagnostics.

Use sessions when you need a transcript:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe exec --workspace . --session smoke-exec "inspect README.md"
artifacts\release\caicli-0.3.0-win-x64\caicli.exe session export --format markdown smoke-exec
```

## Real Model Smoke

Default smoke runs do not require network access or credentials. The real model smoke is opt-in:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
$env:CAICLI_REAL_MODEL_SMOKE = "1"
tools\Invoke-SmokeTests.ps1
```

Expected skip behavior:

- If `CAICLI_REAL_MODEL_SMOKE` is unset, the real model check is skipped.
- If `CAICLI_REAL_MODEL_SMOKE=1` but `OPENAI_API_KEY` or `OPENAI_MODEL` is missing, the real model check is skipped.
- Offline fake and local smoke checks still run independently of the real model opt-in path.

## MCP Stdio

User-configured stdio MCP v1 servers can be discovered and called through registry/tool paths. Workspace-configured MCP servers are not auto-started by `tools list`, `tools call`, `exec`, or `run`.

Use explicit diagnostics when MCP tools are missing:

```powershell
artifacts\release\caicli-0.3.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.3.0-win-x64\caicli.exe mcp doctor
```

MCP stdio startup commands go through shell policy and dangerous-command detection before process start. Disabled servers are not started.
