# C# AI CLI Security Model

## Trust Boundary

C# AI CLI is a local developer tool. It operates on the selected workspace and writes local logs and transcripts. It is not a sandbox, a multi-user service, or a replacement for code review.

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

The release MVP uses a single-file exact-text patch tool.

- `workspace.apply_patch` performs single-file exact-text replacement.
- Patch dry-run preview returns structured details, including preview type, paths, files, replacement counts, whether a diff exists, and dirty workspace status/summary.
- Patch apply rechecks the file content before writing.
- Dirty workspace state is included in the preview.
- File edits require approval unless the effective approval mode or CLI override approves them.
- Patch operations are recorded as transcript tool calls when run through `exec --session` and the agent loop.

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
- Dangerous command output and structured payloads include a readable `matchedRule` where applicable.
- Encoded PowerShell payloads and aliases are detected. Encoded payloads are not echoed in safe diagnostics.
- Commands have timeouts and stdout/stderr byte limits.
- Timeout requests above `shellPolicy.maxTimeoutMilliseconds` are rejected with a safe explanation instead of being silently clamped.
- Direct executable MCP policy input includes the executable and argv. Encoded PowerShell payloads are canonicalized/redacted in policy and detector messages.
- Timeout, denied approval, and non-zero exit are returned as safe tool failures.

## Tool Disable Controls

Users can disable tools through `disabledTools` in user or workspace config. Disabled tools are not registered in the CLI tool registry, so direct calls, agentic `exec` tool calls, and deterministic `run` tasks fail safely with `unknown-tool`.

## Secrets

- Release artifacts do not include API keys.
- `OPENAI_API_KEY` or user config may provide the key.
- Workspace `apiKey` is ignored and reported as a warning.
- Logs record key presence and source, not key value.

## Sessions And Logs

- Command logs are written under `<workspace>\.caicli\logs` when the workspace is usable.
- Chat transcripts are written under `%USERPROFILE%\.caicli\sessions`.
- `exec --session` records agent transcripts, including tool call requests and summarized tool results.
- `CAICLI_USER_PROFILE` can redirect user config and sessions for smoke tests or portable verification.
- Transcript file names are derived from validated session names and cannot traverse directories.

## Optional And Deferred Capabilities

- Microsoft Agent Framework integration is an experimental adapter boundary in this release. The real framework runtime is not enabled.
- MCP config/list/doctor and a generic bridge exist. Registry/tool paths can discover and call user-configured stdio MCP v1 servers through the real initialize, `tools/list`, and `tools/call` paths.
- Workspace-configured MCP servers are not auto-discovered or started during ordinary tool registry creation, including `tools list`, `tools call`, `exec`, and `run`.
- `mcp doctor` may explicitly perform real stdio handshake diagnostics for configured stdio servers, including workspace config. Disabled servers are not started.
- MCP stdio startup commands run dangerous command detection and shell policy checks before process start.
- MCP startup policy failures surface safe diagnostics in `tools call mcp.*` when configured server/tool discovery is blocked, instead of only returning `unknown-tool`.
- Remote/http MCP transport remains Deferred.
- Gerber/TIFF project pack status/profile support exists, but real Gerber execution is Deferred.

These deferred capabilities do not block the direct backend release.

## Current Limitations

- The tool cannot guarantee semantic correctness of generated code.
- It does not run destructive commands without the shell runner safety checks, but users should still review commands and diffs.
- Dangerous command detection and shell policy use conservative text and pattern boundaries, not full shell parsing or semantic proof.
- Allowlist entries are command text, not exact argv arrays. Unusual quoted arguments containing metacharacters may be conservatively blocked.
- The first release is Windows-focused.
- Dotnet tool packaging is not enabled for the first release package.
