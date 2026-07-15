# Troubleshooting

## Model Configuration

Run `doctor` first:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe doctor --verbose
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
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --trace --workspace . --approval never --max-turns 2 --max-tool-calls 2 --timeout-seconds 60 "Use workspace.read_text to read README.md, then summarize it in one sentence. Do not modify files or run shell commands."
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
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "Inspect @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . "Review @folder:src/CSharpAiCli.Core/Agents"
```

Common reference failures:

- `workflow-reference-boundary-denied`: keep the referenced path inside `--workspace`.
- `workflow-reference-not-found`: check the path relative to the workspace.
- `workflow-reference-binary-not-supported`: explicit `@file` references must be text.
- `workflow-reference-too-large`: the reference was bounded or truncated.

Reference failures happen before model execution. They do not call the model, run shell or patch tools, start MCP, or save a partial session agent run.

## Local Skills

List available packs before running one:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills list --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills list --output json --workspace .
```

Use dry-run to inspect the expanded plan without model or tool execution:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills run review-only --dry-run --workspace . -- "@file:README.md"
```

Common skill failures:

- `skill-not-found`: check the pack name from `skills list`.
- `skill-manifest-invalid`: a workspace-local `.caicli/skills` JSON manifest is missing required fields or has invalid entry/reference data.
- `skill-version-unsupported`: this release accepts only `0.x` skill pack manifest versions.
- `skill-safety-policy-invalid`: the manifest tries to enable unsupported safety behavior, such as MCP access.
- `skill-run-plan-invalid`: the task or report override could not be expanded into a valid run plan.

`skills run --dry-run` should not call a model, write reports, run shell/patch/MCP tools, or save a session. Non-dry-run `skills run` uses the same approval, workspace guard, disabled-tool, shell policy, trace, session, and report behavior as `exec`.

Passing `--record-job` is an explicit persistence request. For a dry-run it writes compact plan metadata only to the user-level job store; it still does not write the workspace or run the model/tools.

## Job History

Job recording is opt-in. Record an execution or a skill dry-run, then list the local user-level store before looking up an individual job:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --record-job --job-name smoke --workspace . "Summarize @file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe skills run review-only --record-job --dry-run --workspace . -- "@file:README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs list --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs show <job-id> --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs export <job-id> --format markdown --workspace .
```

Common job failures and diagnostics:

- `job-not-found`: the id is malformed or no matching record exists in the active user-level store.
- `corrupt-job-record`: the file is invalid JSON, uses an unsupported schema, or its id does not match its file name. `jobs list` reports the diagnostic and continues with valid records.
- `job-record-unreadable`: the record could not be read because of a local file/access race.
- `job-record-write-failed`: explicit `--record-job` persistence could not be created or finalized; the execution command does not hide recording loss.

The default directory is `%USERPROFILE%\.caicli\jobs`. `CAICLI_USER_PROFILE` selects an isolated profile for smoke or portable verification. There is no `jobs delete`, job/artifact cleanup, or automatic retention command; remove obsolete record files manually only after confirming the resolved user profile and job directory.

## Task Queue

Inspect the user-level queue before running an item:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue list --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue show <queue-id> --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue run <queue-id> --workspace .
```

Common queue failures and diagnostics:

- `queue-item-not-found`: the id is malformed or no matching record exists in the active user-level store.
- `corrupt-queue-record` / `queue-record-unreadable`: a record is invalid or cannot be read. List and cleanup continue with valid records and preserve the problematic file.
- `queue-record-write-failed`: the queue state could not be created or atomically updated.
- `queue-invalid-state`: the requested transition is not allowed. Run accepts pending or failed; cancel accepts pending only.
- `queue-execution-failed`: the delegated command threw or stopped before it could finalize a normal result; inspect the linked job and local diagnostics.
- `queue-job-record-missing`: the delegated exec/skills command did not produce its required job audit record, so the queue attempt fails.
- `queue-cleanup-status-unsafe`: cleanup only accepts succeeded, failed, or canceled.

A failed item can be rerun and receives a new attempt/job pointer. A running item cannot be canceled by task queue v1. If a process is terminated while running, inspect the queue/job files before manual recovery; there is no lease or daemon recovery protocol. Queue cleanup does not delete job records or artifacts.

`queue list` and `queue cleanup` report corrupt records while continuing with valid items. Diagnostics are redacted in text/JSON and corrupt files are deliberately preserved; do not rename or delete one until the active user profile, file path, and any linked job evidence have been reviewed.

## Multi-role Pipelines

Inspect a plan before execution, then use JSON output to correlate roles with queue and job records:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline list --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline plan fix-review-test --workspace . -- "Fix failing tests"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe pipeline run fix-review-test --output json --workspace . -- "Fix failing tests"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe queue show <role-queue-id> --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe jobs show <role-job-id> --output json --workspace .
```

Common pipeline failures and diagnostics:

- `pipeline-not-found`: the name is not one of `fix-review-test`, `review-test`, or `security-review`.
- `pipeline-invalid-request`: the task is empty.
- `pipeline-execution-failed`: local queue/job state could not be created or read; inspect the active user profile and filesystem access.
- A delegated role error such as `missing-model`, `missing-openai-api-key`, `approval-denied`, `tool-disabled`, or a shell/workspace error appears on that role report and linked job. Later roles are skipped after the first failure.

Reviewer and security roles should report `boundary.isReadOnly=true`, with writes, shell, MCP, and MCP discovery disabled. A `tool-disabled` result from these roles is an enforced boundary, not a signal to rerun with elevated permissions. There is no pipeline-level retry/resume; inspect the preserved role queue/job/artifact pointers, correct the cause, and start a new pipeline run when appropriate.

## Workspace-local Automation

Inspect and validate manifests before manual execution:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation list --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation validate --output json --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation plan nightly-review --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation run nightly-review --dry-run --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe automation run nightly-review --manual --output json --workspace .
```

Common automation diagnostics:

- `automation-manifest-invalid`: a required field, strict JSON field, cron preview, time zone, target shape, or report value is invalid. Manifest `script`, `command`, and approval fields are not accepted.
- `automation-unsafe-target`: `manualOnly` is false, `cwd` escapes the workspace, or the safety declaration understates a selected target capability.
- `automation-target-unavailable`: the named skill, expert, or built-in pipeline is not available.
- `automation-not-found`: the requested name did not enter the valid catalog; run `automation validate` to see its source diagnostic.
- `automation-run-mode-invalid`: specify exactly one of `--dry-run` or `--manual`.
- `automation-execution-failed`: queue/job artifacts could not be created or read. Inspect the active `CAICLI_USER_PROFILE` and filesystem access.

Schedule output always reports `enabled=false`. This is intentional: the schedule is preview data, not a background registration. Do not expect automation to create Windows Task Scheduler entries, daemon processes, API/webhook triggers, or automatic retries. The separate local API Preview is read-only and never executes an automation.

Dry-run is non-persistent and should not create queue/job/log state. Manual execution can return delegated errors such as `missing-model`, `tool-disabled`, `approval-denied`, or `shell-policy-denied`; these show that the existing target boundary was preserved. Inspect the returned queue/job ids with `queue show` and `jobs show` rather than rerunning with broader permissions.

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
artifacts\release\caicli-0.4.0-win-x64\caicli.exe tools call --workspace . --approval always workspace.run_shell --arguments-file shell-args.json
```

## Gerber/TIFF Controlled Conversion Preview

Start with static planning and explicit fixed probes. Both tools are mandatory for a real run:

```powershell
caicli packs doctor gerber-tiff `
  --tool-path "gerbv=C:\Tools\gerbv\gerbv.exe" "imagemagick=C:\Tools\ImageMagick\magick.exe" `
  --probe --approval always --output json --workspace .
```

Common controlled conversion failures:

- `pack-tool-not-found`: one explicit dependency path is missing, a directory, or unavailable. The CLI does not search, install, or download it.
- `pack-tool-version-unsupported`: the fixed version probe failed or reported a version below the manifest minimum.
- `pack-tool-identity-changed`: filename/path metadata/SHA256 changed before approval, after approval, or during execution. Generate a new plan only after reviewing the new tool identity.
- `pack-approval-required`: current non-interactive approval mode did not authorize a probe or external stage. This is not a resume token; make a new invocation-local decision.
- `pack-output-conflict`: the explicit output directory or a declared output already exists. Choose a new empty path; there is no force-overwrite option.
- `pack-output-boundary-violation`: a path escaped its frozen root, contained a reparse point, or the process created an undeclared output.
- `pack-output-limit-exceeded`: a declared output exceeded the frozen per-file size limit.
- `pack-partial-output`: exit `0` did not produce every declared output, or output was empty/changed; retained files are failure evidence.
- `pack-execution-failed`: process startup, non-zero exit, stderr, or evidence persistence failed.
- `pack-execution-timeout` / `pack-execution-canceled`: the process tree was terminated and the run recorded a failed/canceled terminal state.
- `pack-process-cleanup-failed` / `pack-residual-process-detected`: cleanup or descendant checks found an unsafe process condition. The run is interrupted and must not be auto-replayed.

Inspect `packs runs show <run-id> --output json`, the correlated job, and the managed
`logs/conversion-execution.json`. A `verifying` state means conversion ran and declared hashes exist, but TIFF
verification has not completed. Do not manually change it to `awaiting-acceptance`. Run `packs verify <run-id>`
and inspect its independent file/metadata/content/human-review levels. Preview remains a review aid, not proof.

### Human decision is refused

Run `packs resume <run-id> --output json`. A decision is allowed only from the current `awaiting-acceptance`
revision. Accept additionally reopens and verifies the indexed hard-verification JSON path, size/SHA256, schema,
run id, levels, and absence of error diagnostics. Ready/verifying/failed/canceled/interrupted, fake-driver
awaiting state, preview-only evidence, a changed report, or an existing decision is refused.

Use `artifacts list --run <run-id>` and `artifacts verify <artifact-id>` to inspect identity. Do not edit a report
to force acceptance and do not treat metadata validity or preview availability as a bypass. Reject requires
`--reason`; actor/note/reason are redacted and bounded before persistence.

### Resume or restart reports drift

Resume never reuses approval. Provide the current explicit `--tool-path` bindings for staged/ready/interrupted
runs and inspect the stable error code. Input/tool/policy drift requires a new plan/run rather than checkpoint
editing. In verifying/awaiting states, confirm the explicit workspace TIFF and managed report/log identities have
not moved or changed. Post-execution output may exist only at the frozen directory and must match the inventory.

`running` and `interrupted` are not replayed by resume. If a process crash left a stale running record, first
confirm at the OS level that no `caicli`, `gerbv`, or `magick` process for that run remains, then use
`packs recover <run-id> --mark-interrupted`. This records evidence only; it does not terminate a process.

`packs restart <run-id> --from execute` accepts only an interrupted run with `restartRequired=true`. Supply
current absolute Gerbv/ImageMagick bindings and invocation-local approval. Tool/input/policy drift, an existing
attempt output, corrupt parent state, or an already-created reserved child fails closed. Inspect the parent
`restartPlan` and `restartedByRunId`; never remove parent partial evidence or rename existing output to force it.

### Artifact manifest or prune diagnostics

For a corrupt manifest, confirm `CAICLI_USER_PROFILE`, then inspect `run.json`, `checkpoint.json`, and
`artifact-manifest.json` without editing them. Missing/unsupported/unknown fields, revision/state/owner/pointer
mismatch, reparse paths, or changed hashes are not repaired. Job/queue pointers are not a second run truth.

Start cleanup with `artifacts prune --older-than 30d --dry-run`. Only owned managed artifacts from accepted,
rejected, failed, or canceled runs are candidates. Source, baseline, explicit workspace output, external,
metadata, running/interrupted/corrupt, outside-root, and reparse paths are retained. For apply failures, close
viewers and rerun dry-run. A race fails quarantine identity recheck and changed content is retained. Successful
prune keeps run/job metadata and tombstones; there is no restore command. The default age comes from user-level
`artifactRetention.defaultMinimumAgeDays`; workspace overrides are ignored.

Default smoke intentionally skips real tools. To run the independent opt-in branch, set all three values:

```powershell
$env:CAICLI_GERBER_TIFF_TOOL_SMOKE = "1"
$env:CAICLI_GERBV_PATH = "C:\Tools\gerbv\gerbv.exe"
$env:CAICLI_IMAGEMAGICK_PATH = "C:\Tools\ImageMagick\magick.exe"
tools\Invoke-SmokeTests.ps1
```

The opt-in smoke records tool versions/SHA256, input/output hashes, hard verification, explicit human accept and
reject, and controlled managed prune. It does not claim complete PCB manufacturing correctness. Source and
explicit workspace TIFF outputs must remain after prune.

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
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe diff --stat --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe changes --session smoke-exec --workspace .
artifacts\release\caicli-0.4.0-win-x64\caicli.exe review --workspace .
```

`review` sends the current git diff to the configured model. It is read-only for the workspace and does not run shell or patch tools.

`changes` is local and read-only. It does not call a model and can still return exit code `0` for clean workspaces, non-git workspaces, or missing session task reports while surfacing warnings in text/JSON output.

## Markdown Report Issues

Use `--report markdown` for a full task report:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --report markdown --workspace . "Summarize @file:README.md"
```

Common report failures:

- `Invalid value for --report`: use `none` or `markdown`.
- `report-path-requires-markdown`: use `--report markdown` together with `--report-path`.
- `workspace-boundary-denied`: the report path resolved outside the workspace.
- `report-path-exists`: the report path already exists. The CLI does not overwrite report files in this release.
- `report-path-is-directory`: choose a file path, not a directory path.

When using `--output json --report markdown`, the NDJSON stream should not contain the raw markdown report. Look for `report.generated` and `payload.taskReport.report` metadata instead.

## Expert Profile Issues

Use one of the built-in profiles:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --expert reviewer --workspace . "Review @folder:src"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --expert security --workspace . "Audit @folder:src"
```

Common expert failures:

- `Invalid value for --expert`: use `bugfix`, `reviewer`, `tester`, `security`, or `refactor`.
- `tool-disabled` under `reviewer` or `security`: the selected expert is read-only, so patch, shell, and MCP tools are blocked for that run.
- Expert profiles do not configure a model or API key. Missing model/key errors are diagnosed the same way as ordinary `exec`.

## Traces, Logs, And Sessions

Enable trace diagnostics for agent work:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --trace --workspace . "read README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe logs show --tail 80
```

Trace and command logs are redacted, but they can still contain prompts, paths, summaries, and diffs. Treat them as sensitive local diagnostics.

Use sessions when you need a transcript:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe exec --workspace . --session smoke-exec "inspect README.md"
artifacts\release\caicli-0.4.0-win-x64\caicli.exe session export --format markdown smoke-exec
```

## Local API / Daemon Preview

Use static diagnostics first; neither command starts a listener:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe daemon doctor --output json
artifacts\release\caicli-0.4.0-win-x64\caicli.exe api routes --output json
```

Common daemon failures:

- `--preview is required`: the API is default-off. Add `--preview` only after reviewing the local unauthenticated threat model.
- `remote and wildcard binds are disabled`: use `--bind localhost` or `--bind 127.0.0.1`. `0.0.0.0`, IPv6, LAN/public addresses, and hostnames are intentionally unsupported.
- `listener could not be started`: another process may already use the port. Choose an unused port from `1024` through `65535`.
- `invalid-host`: the request Host must be `localhost` or `127.0.0.1`. Do not place the Preview behind a reverse proxy, port forward, or alternate hostname.
- `api smoke failed`: start the daemon explicitly on the same port, then retry `api smoke --port <port>`.

Default smoke does not start a daemon. To run the packaged localhost API smoke explicitly:

```powershell
$env:CAICLI_DAEMON_SMOKE = "1"
tools\Invoke-SmokeTests.ps1
```

This opt-in does not require model credentials. It starts only the read-only `127.0.0.1` Preview for the smoke duration and terminates the process afterward.

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
artifacts\release\caicli-0.4.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.4.0-win-x64\caicli.exe mcp doctor
```

MCP stdio startup commands go through shell policy and dangerous-command detection before process start. Disabled servers are not started.
