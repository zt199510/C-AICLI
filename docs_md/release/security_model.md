# C# AI CLI Security Model

## Trust Boundary

C# AI CLI is a local developer tool. It operates on the selected workspace and writes local logs and transcripts. It is not a sandbox, a multi-user service, or a replacement for code review.

Model providers are outside the local trust boundary. `chat`, `review`, and real agentic `exec` send prompts and selected context to the configured provider. Agent tool calls returned by the provider are treated as requests, not authority: the CLI validates the tool name, disabled-tool settings, workspace path boundary, approval mode, shell policy, dangerous-command rules, loop limits, and timeouts before execution.

## Workspace Guard

File and command tools resolve paths before use and require paths to stay inside the active workspace. Tests cover:

- `..` traversal.
- Prefix sibling bypass attempts.
- Case normalization.
- Junction or symlink targets outside the workspace.
- Binary and oversized text reads.
- Shell cwd outside the workspace.

## Project Instruction Files

Project instruction files are loaded only from inside the selected workspace. `chat` and agentic `exec` can use `--cwd <path>` to choose the instruction target path; `--workspace` remains the workspace root and tool boundary. Unsafe or outside targets produce warnings and do not load outside paths or contents.

For each directory from the workspace root to the target directory, the loader selects `AGENTS.md` when present; otherwise it selects legacy `AICLI.md`. It does not fall back to same-directory `AICLI.md` when `AGENTS.md` exists but is empty, invalid, or oversized. Loaded instruction text is merged root-to-leaf and sent with model requests for `chat` and agentic `exec`.

`doctor`, `config get`, and `config list` report instruction source paths, source order, and warnings, but they do not print instruction contents. Instruction contents may still be visible to the configured model provider because they are part of the model request.

## Workflow References

Agentic `exec` supports inline `@file:<path>` and `@folder:<path>` workflow references. These references are bounded local context inputs, not permissions.

- References are resolved before model execution and must stay inside the active workspace.
- Explicit `@file` references fail safely for outside paths, missing files, binary files, and unreadable files.
- Large explicit text files are truncated and marked with warning metadata before they enter model context.
- `@folder` references are bounded by recursion depth, file count, single-file bytes, and total bytes.
- Folder references skip binary or unreadable child files with warnings, and skip common generated/private directories such as `.git`, `.caicli`, `bin`, `obj`, and `node_modules`.
- Reference metadata is recorded in text output, NDJSON, trace, session task reports, and `changes` views, but raw referenced content is not persisted in reports.

References do not bypass disabled-tool settings, approval mode, workspace guard, dirty-workspace checks, shell policy, dangerous-command detection, MCP startup policy, loop limits, or timeouts. A model may still ask for write or shell tools after reading referenced context, but those requests go through the same approval and safety path as any other `exec` run.

## Approval Modes And Tool Risk

The CLI uses an approval mode plus per-tool risk metadata to decide whether a tool call may run. Config JSON supports `approvalMode` values:

- `never`
- `on-request`
- `on-failure`
- `always`

The effective approval mode is selected from user config, then workspace config, then the default `on-request`. There is no environment variable for approval mode. `doctor`, `config get`, and `config list` report the effective approval mode and source.

Tool risk levels are:

- `read`: read-only workspace and git inspection tools. Read tools do not need approval.
- `write`: file-editing tools such as patch apply.
- `shell`: ordinary shell execution inside the workspace.
- `dangerous-shell`: shell commands matching dangerous command patterns.

Write and shell tools use the effective approval mode:

- `always`: approve ordinary write and shell actions.
- `never`: deny approval-required write and shell actions.
- `on-request`: deny in the non-interactive CLI and report `approvalStatus` `approval-required`, because interactive approval is not available.
- `on-failure`: deny in the non-interactive CLI and report `approvalStatus` `approval-required`, because sandbox retry escalation is not implemented.

Dangerous shell commands are denied before execution with `approvalStatus` set to `dangerous-shell-denied`, even under `always` or `--approve`. Approval-policy denials still use the failure `errorCode` `approval-denied`; `approvalStatus` carries the specific approval decision.

`tools call` and `exec` support `--approval <mode>`. The legacy `--approve` option remains compatible and maps to an approving mode through the same risk-aware resolver when no explicit `--approval` value is supplied. `run` remains the deterministic direct-tool compatibility path; it does not have `--approval`, but its existing `--approve` option maps through the same resolver, and configured `approvalMode` applies when `--approve` is not supplied.

Approval status is included in text output and in `exec` JSON events/results through `approvalStatus`.

## File Editing

The release uses a single-file exact-text patch tool.

- `workspace.apply_patch` performs single-file exact-text replacement.
- Patch dry-run preview returns structured details, including preview type, paths, files, replacement counts, whether a diff exists, and dirty workspace status/summary.
- Patch apply rechecks the file content before writing.
- Dirty workspace state is included in the preview.
- File edits require approval unless the effective approval mode or CLI override approves them.
- Patch operations are recorded as transcript tool calls when run through `exec --session` and the agent loop.
- Agentic `exec` emits patch lifecycle events for preview, approval, and apply outcomes. After a successful patch it records changed-file summaries from git status/diff and carries them into text, NDJSON, trace, and session run summaries.

## Shell Execution

Shell execution is restricted:

- The working directory must remain inside the workspace.
- Config JSON supports `shellPolicy.allowedCommands`, `shellPolicy.deniedCommands`, and `shellPolicy.maxTimeoutMilliseconds`.
- `config get` reports the effective shell policy. `doctor` reports shell, patch, and MCP execution policy diagnostics.
- Shell policy applies to `workspace.run_shell` and MCP stdio startup commands.
- Shell policy is enforced before approval and before command execution or MCP startup.
- Denied commands take precedence over allowed commands.
- An empty configured allowlist denies all shell commands.
- Allowlist entries are command-text policy strings. Exact matches are allowed. Prefix matches allow ordinary arguments but reject shell control and metacharacter syntax after the prefix, including `&`, `&&`, `||`, `;`, `|`, newlines, carriage returns, redirection, backticks, and command substitution syntax.
- Denylist matching is boundary-aware, so it avoids matching inside larger words while still matching shell fragments.
- Ordinary commands require approval.
- Dangerous command patterns are denied before execution with `errorCode` `approval-denied` and `approvalStatus` `dangerous-shell-denied`.
- Structured payloads include `matchedRule`, and text output includes a readable matched-rule summary where applicable.
- Encoded PowerShell switches, supported abbreviations, and aliases are detected. Encoded payloads are not echoed in safe diagnostics.
- Commands have timeouts and stdout/stderr byte limits.
- Timeout requests above `shellPolicy.maxTimeoutMilliseconds` are rejected with a safe explanation instead of being silently clamped.
- Direct executable MCP policy input includes the executable and argv. Encoded PowerShell payloads are canonicalized/redacted in policy and detector messages.
- Timeout, denied approval, and non-zero exit are returned as safe tool failures.

## Post-Patch Verification

Agentic `exec` may run verification after a successful patch, but only when a command is explicitly configured. It does not infer build, test, package, or cleanup commands from project contents.

Verification command selection is conservative:

- Project instructions win when the merged instruction text contains `VerificationCommand: <command>` or `ValidationCommand: <command>`.
- If instructions do not define a command, a single unambiguous workflow profile `validationCommand` can be used.
- If multiple workflow commands are configured and no workspace match disambiguates them, verification is skipped.
- If no explicit command exists, verification is skipped.

Verification execution always goes through `workspace.run_shell`. Approval mode, shell policy allowlist/denylist, timeout limits, dangerous-command detection, cwd guard, stdout/stderr truncation, and safe error codes are reused. Verification results are written back as structured tool payload data for model continuation and as `verification.result` events for text, NDJSON, trace, and session consumers.

## Review Gate And Task Reports

Agentic `exec` ends each run with a read-only review gate and final task report.

- The `review.gate` event summarizes the final git diff through the read-only `git.diff` tool path.
- The review gate payload marks `readOnly=true` and `toolName=git.diff`.
- The review gate does not call patch tools, shell tools, or workspace write paths.
- The review gate can report `success` or `warning`; warning covers cases such as failed or truncated diff collection.
- The final `AgentTaskReport` records status, stop reason, prompt, plan, tools, workflow reference metadata, expert metadata, skill metadata, report artifact metadata, changed files, commands, verification, remaining risks, trace path, summary, error code, and optional review gate details.
- Text output, NDJSON output, trace result payloads, and session transcript agent run summaries all carry report-derived data.
- The report is diagnostic and review-oriented; it is not an automatic rollback or correctness guarantee.
- A standalone full markdown task report file is not written by default. `exec --report markdown` prints the full report in text mode, while JSON output records structured metadata only. `--report-path <path>` writes only on explicit request, must remain inside the workspace, creates parent directories, and refuses to overwrite existing files.

Task reports sanitize all captured strings before output or persistence. When a secret-like value is detected, the report records only presence metadata, expressed as source and kind, and never stores the raw value.

## Job History And Artifact Index

Job recording is explicit. `exec` and `skills run` write job records only when `--record-job` is passed; `--job-name` is an optional label and is not the unique id. The default store is user-level local state under `%USERPROFILE%\.caicli\jobs`, or the directory implied by `CAICLI_USER_PROFILE` during smoke and portable verification.

`jobs list`, `jobs show`, and `jobs export` are read-only inspection commands:

- They do not call a model.
- They do not run shell, patch, write, or MCP tools.
- They do not start MCP servers.
- They do not write workspace files or command logs.

Job records store redacted metadata and artifact pointers only. They can include bounded redacted task text, command family, job id/name, workspace/cwd, status, timestamps, exit code, stop reason, error code, compact task report counts, skill plan metadata, trace/session/report paths, and artifact hashes. They must not store raw referenced content, raw tool arguments, raw secret values, or full diffs. Paths remain local metadata and should still be treated as sensitive when exporting a record.

`skills run --dry-run` remains non-persistent by default. Passing `--record-job` is an explicit persistence request: it writes compact skill/expert/report/safety/validation/reference-suggestion metadata to the user-level job store, but does not persist expanded instructions, call a model, run tools, create a report, save a session, or write the workspace.

If `--record-job` cannot create the initial job record, the execution command fails before model/tool work. If the final job update fails after execution, the command returns `job-record-write-failed` so explicit recording loss is not hidden.

## Local Task Queue

Queue records are user-level local state under `%USERPROFILE%\.caicli\queue`, or the state root selected by `CAICLI_USER_PROFILE`. `queue add` stores a bounded redacted request in `pending` state and does not call a model, run tools, start MCP, or write the workspace. Approval overrides are not part of the queue schema.

`queue run` transitions one pending or failed item to running, then re-enters the existing `exec` or `skills run` command factory with mandatory `--record-job`. It does not add `--approve` or `--approval`; the effective approval mode is resolved from the current configuration. Workspace guard, dirty workspace checks, shell policy, dangerous-command detection, disabled tools, MCP startup policy, expert/skill tool boundaries, trace/session/report flow, and model credential checks remain owned by the reused execution path.

Each completed attempt records its terminal status, bounded error metadata, exit code, and job id. The queue item also stores the latest job id; job records use the queue id as their label. Queue files do not store raw referenced contents, raw tool arguments, raw secrets, full diffs, or approval overrides.

Cancel is pending-only and never attempts to terminate an active process. Cleanup accepts only succeeded, failed, or canceled queue status. It preserves pending, running, corrupt, and unknown records, and it does not delete job history or artifacts. Task queue v1 has no daemon, scheduler, concurrent worker pool, remote runner, or permission elevation mechanism.

## Local Multi-role Pipelines

`pipeline list` and `pipeline plan` read the fixed built-in catalog and render text/JSON only. They do not call a model, construct a tool registry, run tools, start MCP, create queue/job state, write command logs, or write the workspace.

`pipeline run` creates one queue item per role and invokes the existing `queue run` command path. Each queue attempt delegates to `exec` or `skills run` with mandatory job recording. Pipeline orchestration does not persist or inject approval overrides and cannot expand the effective approval mode, workspace guard, dirty-workspace checks, shell policy, dangerous-command detection, disabled tools, MCP startup policy, expert/skill tool boundary, trace/session/report path, or credential boundary.

Reviewer and security stages are read-only invariants in the built-in catalog. Their expert/skill execution paths do not register patch or shell tools, skip MCP discovery, and configure the executor to reject patch, shell, and `mcp.*` requests with `tool-disabled`. Tester and implementer stages can use the ordinary exec tool set, but every write/shell/MCP action remains subject to the existing policy and approval checks.

Pipeline reports aggregate bounded redacted job/task-report metadata, queue/job pointers, warnings, remaining risks, and artifact pointers. They do not persist raw referenced contents, raw tool arguments, raw secrets, or full diffs. Execution is sequential and stops at the first failed role without deleting earlier role evidence. Pipeline v1 has no automatic model/provider routing, parallel workers, scheduler, daemon, remote runner, or remote collaboration path.

## Workspace-local Automation

Automation manifests are workspace-local JSON data under `.caicli/automations`. Loading is read-only and guarded by the workspace boundary, a manifest size limit, strict unknown-field rejection, target validation, and secret-safe diagnostics. Manifests cannot contain executable script/command fields or approval overrides. A `schedule` trigger is validation and preview metadata only: the CLI does not start a scheduler, register Windows Task Scheduler, or execute a manifest in the background.

`automation list`, `automation validate`, `automation plan`, and `automation run --dry-run` do not call a model, construct a tool registry, run tools, start MCP, create queue/job records, write command logs, or write the workspace. Dry-run is non-persistent and only renders the validated, redacted plan.

`automation run --manual` supports queue, skill, and built-in pipeline targets. Queue and skill targets enter `queue run`; pipeline targets enter `pipeline run`, whose roles then enter `queue run`. Automation does not pass `--approve` or `--approval`, cannot expand disabled tools or target role boundaries, and continues to use the existing workspace guard, dirty-workspace checks, shell policy, dangerous-command detection, MCP startup boundary, trace/session/report path, model credential checks, and smoke contract.

Manual runs store bounded redacted automation name/run/source/target correlation in queue and job metadata plus an inline automation artifact pointer. They do not store raw referenced contents, raw tool arguments, raw secrets, full diffs, or approval overrides. Automatic schedules, daemon workers, Windows Task Scheduler registration, API/webhook triggers, remote execution, and team automation remain Deferred.

## CI Artifacts

`ci summarize` and `ci check` read one existing job through the user-level job store and project it into provider-neutral JSON or markdown. They do not call a model, construct a tool registry, run shell/patch tools, start MCP, create queue/job/session/trace state, or write command logs. They cannot expand approval, workspace, dirty-workspace, shell, disabled-tool, expert/skill, or MCP boundaries because they do not execute the recorded task.

The projection includes only bounded redacted summary/check/annotation/correlation metadata and artifact pointers. It excludes job task text, raw workflow reference content, task-report command and verification details, raw tool arguments, raw secrets, and full diffs. Exported strings pass through secret redaction again. A source record that does not declare secrets redacted plus raw references/tool arguments/full diff absent becomes `config-error`, and its artifact pointers are not emitted.

JSON and stdout markdown are non-persistent. `--markdown-path` explicitly reuses the report path resolver: paths must stay inside the workspace and existing files are not overwritten. The CLI does not invoke GitHub/GitLab/Azure DevOps APIs, create PR comments, emit provider annotation protocols, send webhook/callback traffic, or upload artifacts. Local paths in artifact pointers remain sensitive metadata.

## Expert Profiles

`exec --expert` selects a built-in local profile. It is local policy and prompt/report guidance, not provider/model routing.

- `bugfix`, `tester`, and `refactor` do not expand permissions. They still use the configured disabled tools, approval mode, workspace guard, shell policy, dangerous-command detector, MCP startup policy, loop limits, and timeouts.
- `reviewer` and `security` are read-only profiles. The CLI does not register write or shell tools for those runs, skips MCP discovery, and the tool executor rejects write, shell, and `mcp.*` tool requests with `tool-disabled` even if a model asks for them.
- Expert metadata is recorded in text output, NDJSON, trace, session task reports, and markdown reports so the active role and boundary can be reviewed later.

## Local Skills

`skills list` and `skills run` are local workflow-pack entry points. Built-in packs and workspace `.caicli/skills` JSON manifests can select an existing expert profile, default report mode, reference suggestions, instructions, validation command hints, and stricter safety constraints.

- Skill manifests are data only. They are read and validated, but manifest commands or scripts are not executed directly.
- `skills run --dry-run` only renders the expanded plan and is non-persistent unless `--record-job` is explicitly passed. It does not call a model, write workspace files, run shell/patch tools, start MCP, create reports, or save a session.
- Non-dry-run `skills run` uses the same `exec` agent runner, tool registry, approval policy, workspace guard, shell policy, disabled-tool checks, trace/session/report flow, and review gate.
- Skill safety can restrict tools, such as read-only mode or disabling shell/MCP. It cannot bypass approval, shell policy, workspace guard, disabled tools, or dangerous-command detection.
- Skill metadata is recorded in text output, NDJSON, trace, session task reports, and markdown reports so the selected pack and boundary can be audited later.

## Changes View

`caicli changes` is a read-only local review entry point. It combines git status/diff stat, changed files, and optional latest session `taskReport` data.

- It does not call a model.
- It does not run shell, patch, write, or MCP tools.
- It does not write workspace files or session transcripts.
- It does not write command logs by default.
- It writes trace diagnostics only when the user explicitly passes recursive `--trace` or sets `CAICLI_TRACE=1`.

The command is a diagnostic view, not a proof of correctness. Users should still inspect diffs and tests before trusting changes.

## Real And Fake Agent Contracts

The offline/fake agent loop and the direct OpenAI Responses SDK agent path share the same tool schema, tool result, event, retry feedback, `review.gate`, and `taskReport` contracts. Normal tests and default smoke runs use fake/offline or local-only paths so they do not need network access or credentials. The opt-in real model smoke uses the same local safety controls with `--approval never` for a bounded read-only task.

The direct OpenAI SDK agent tool loop is implemented for this release. It should not be treated as a Deferred capability. Provider quality, latency, quota, and availability are external dependencies, but local tool execution still stays behind the CLI security checks described in this document.

## Tool Disable Controls

Users can disable tools through `disabledTools` in user or workspace config. Disabled tools are not offered to agentic `exec`; direct `tools call` requests for disabled tools fail safely with `tool-disabled`.

## Secrets

- Release artifacts do not include API keys.
- `OPENAI_API_KEY` or user config may provide the key.
- Workspace `apiKey` is ignored and reported as a warning.
- Logs record key presence and source, not key value.
- Task reports record secret presence source/kind only, not secret values.

## Sessions And Logs

- Command logs are written under `<workspace>\.caicli\logs` when the workspace is usable.
- Chat transcripts are written under `%USERPROFILE%\.caicli\sessions`.
- `exec --session` records agent transcripts, including tool call requests and summarized tool results.
- `exec --session` also records the final task report on the transcript agent run summary.
- `exec --trace` writes the final task report into the trace result payload.
- `CAICLI_USER_PROFILE` can redirect user config and sessions for smoke tests or portable verification.
- Transcript file names are derived from validated session names and cannot traverse directories.

## Optional And Deferred Capabilities

- Microsoft Agent Framework integration is an experimental adapter boundary in this release. The real framework runtime is not enabled.
- MCP config/list/doctor and a generic bridge exist. Registry/tool paths can discover and call user-configured stdio MCP v1 servers through the real initialize, `tools/list`, and `tools/call` paths.
- Workspace-configured MCP servers are not auto-discovered or started during ordinary tool registry creation, including `tools list`, `tools call`, `exec`, and `run`. `exec --expert reviewer` and `exec --expert security` also skip user-configured MCP discovery for that run.
- `mcp doctor` may explicitly perform real stdio handshake diagnostics for configured stdio servers, including workspace config. Disabled servers are not started.
- MCP stdio startup commands run dangerous command detection and shell policy checks before process start.
- MCP startup policy failures surface safe diagnostics in `tools call mcp.*` when configured server/tool discovery is blocked, instead of only returning `unknown-tool`.
- Remote/http MCP transport remains Deferred.
- Gerber/TIFF project pack status/profile support exists, but real Gerber execution is Deferred.

These deferred capabilities do not block the `0.3.3` direct backend release.

## Current Limitations

- The tool cannot guarantee semantic correctness of generated code.
- It does not run destructive commands without the shell runner safety checks, but users should still review commands and diffs.
- Dangerous command detection and shell policy use conservative text and pattern boundaries, not full shell parsing or semantic proof.
- Allowlist entries are command text, not exact argv arrays. Unusual quoted arguments containing metacharacters may be conservatively blocked.
- The release is Windows-focused.
- Dotnet tool packaging is not enabled for the `0.3.3` package.
