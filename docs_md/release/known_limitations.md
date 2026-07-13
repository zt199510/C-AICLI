# Known Limitations

## Release Scope

- Version `0.3.0` is a local Windows release candidate.
- The primary supported package is `win-x64` self-contained single-file publish.
- Dotnet tool packaging is not part of the `0.3.0` release package.

## Model And Agent Behavior

- `chat` uses the direct OpenAI Responses path.
- `exec` is routed through `IAgentRunner` for agentic v1 behavior. It emits agent model/tool/final/error events and enforces loop limits such as `--max-turns`, `--max-tool-calls`, and `--timeout-seconds`.
- `run` remains the deterministic direct-tool compatibility and smoke entry.
- The offline/fake model agent loop and OpenAI response parsing/writeback contracts are implemented and tested.
- The default `exec` path reaches the direct OpenAI Responses SDK tool-call continuation when model and key are configured. Normal unit tests and default smoke tests still use fake/offline contracts or local-only checks; real model smoke is opt-in with `CAICLI_REAL_MODEL_SMOKE=1`.
- Real model smoke is intentionally skipped unless `CAICLI_REAL_MODEL_SMOKE=1`, `OPENAI_API_KEY`, and `OPENAI_MODEL` are all present. A skip in that path does not mean the offline smoke failed.
- `exec` failure feedback retry is finite, not an infinite auto-repair loop. The default retry budget is conservative, `--max-retries 0` disables retry, and budget exhaustion returns a failure summary with remaining risk, command history, and changed files.
- Retry behavior is covered by fake/offline end-to-end tests. Real model repair quality still depends on the configured provider/model and remains opt-in for real-network smoke.
- `chat --resume` and `exec --resume` provide prior transcript context only for existing local sessions. The context is normalized before use to reduce transcript section-spoofing risk.
- `review` is workspace-read-only and does not execute patch or shell tools or write workspace files, logs, transcripts, or patches. Diff collection may use cleaned-up temp files outside the workspace. It sends the current git diff to the configured model and requires configured model credentials for real use.
- `review.gate` and `taskReport` are implemented diagnostic outputs for `exec`; they summarize the final state but do not prove correctness and do not replace human diff review.
- Inline `@file:<path>` and `@folder:<path>` workflow references are available for `exec` only. They are bounded local context hints, not new tool permissions. `chat` references, URL references, glob expansion, semantic retrieval, and workflow-pack references remain Deferred.
- `caicli changes` is read-only and local. It summarizes current git/session/taskReport state but does not call a model, prove correctness, generate standalone markdown reports, or keep historical timelines.
- `exec --report markdown` is an audit artifact, not a correctness proof. It is generated from `AgentTaskReport`, records reference metadata only, and does not persist raw `@file`/`@folder` contents.
- `exec --report-path` never overwrites an existing file and has no automatic history store or report rotation.
- `exec --expert` uses built-in local profiles only. Custom expert files, model role routing, skills, and workflow-pack manifests remain Deferred.
- `reviewer` and `security` experts are read-only by local policy. They still depend on the model's textual output quality and do not prove security or review completeness.
- The Microsoft Agent Framework project is an adapter boundary and experimental stub; the real framework runtime backend is Deferred.

## MCP And Project Packs

- MCP config/list/doctor are available.
- Stdio MCP v1 is available in registry/tool paths for user-configured stdio servers, including the real initialize handshake, MVP tool discovery, and the MVP tool call path.
- Workspace-configured MCP servers are not auto-discovered or started during ordinary registry creation such as `tools list`, `tools call`, `exec`, or `run`.
- `mcp doctor` can explicitly diagnose configured stdio servers, including workspace config, with a real initialize handshake. Disabled servers are not started.
- Remote/http MCP transport remains Deferred.
- The Gerber/TIFF project pack records status/profile behavior, but real Gerber/TIFF conversion execution is Deferred.

## Safety Boundaries

- The tool is local and not a sandbox.
- Workspace path checks reduce accidental boundary escapes but do not replace OS permissions or code review.
- Patch editing is single-file exact-text replacement, not a full merge engine.
- Patch writes remain approval-gated and recheck file content before apply, but previews do not make patching risk-free.
- There is no interactive approval UI. Non-interactive `on-request` and `on-failure` modes report approval-required failures for write and shell actions.
- Retry does not bypass approval, workspace guard, dirty-workspace checks, shell policy, or dangerous-command detection. A retry can ask for another patch or shell command only through the same tool safety path.
- `@file` / `@folder` resolution does not bypass workspace guard. Outside paths are rejected, explicit binary files fail, folder binary/unreadable children are skipped with warnings, and large inputs are bounded or truncated before model execution.
- Failure feedback is bounded/truncated before it is sent back through model continuation; long stdout/stderr or payload strings may require reading trace/session output or rerunning commands manually.
- Trace logs are local diagnostic artifacts. Redaction is best-effort and users should still treat traces, command logs, and session exports as sensitive when prompts or diffs contain private code.
- Shell policy and dangerous command detection run before approval/execution, including for MCP stdio startup commands, but users must still inspect commands.
- Dangerous command detection and shell policy are conservative text/pattern checks, not complete shell parsing or semantic proof.
- Shell allowlist entries are command text, not exact argv arrays; unusual quoted arguments with metacharacters may be conservatively blocked.
- Timeout requests above the configured shell maximum are rejected rather than silently clamped.
- Encoded PowerShell switches, supported abbreviations, and aliases are detected where supported, and encoded payloads are redacted in safe diagnostics, but this is not a general-purpose malware detector.

## Configuration And Secrets

- Workspace `apiKey` is ignored. Use `OPENAI_API_KEY` or user config for model calls.
- `baseUrl` can come from `OPENAI_BASE_URL`, user config, or workspace config, but it must be an absolute `http` or `https` URL without user info, query, or fragment components.
- `config get`, `config list`, `doctor`, and logs record key presence and source, not key value.
- Logs include configured model, base URL, and backend source/status for diagnostics.
- Trace logs redact common key, token, password, authorization, `secretKey`, `privateKey`, nested/escaped argument, OpenAI key, and GitHub token formats, but users should still treat local logs as sensitive diagnostic artifacts.
- Release artifacts do not include user config, workspace config, keys, logs, or transcripts.

## Logging And Diagnostics

- Parse-time `System.CommandLine` validation errors can return before trace context creation, so they may not appear in trace logs. No model or tool flow has begun in this path.
- `logs clear` refuses to clear when the resolved log directory or an ancestor is a symlink/reparse point. This is an intentional local-file safety boundary.

## Platform

- Windows is the primary target.
- Shell policy behavior is primarily designed and verified for Windows command execution.
- Tests run on .NET 9 in the current environment.
- The repository SDK is locked by root `global.json` to .NET SDK `9.0.308` with `latestPatch` roll-forward.
