# Changelog

## Unreleased

Added:
- Week 49 local skills/workflow packs: `caicli skills list` lists built-in and workspace-local packs, and `caicli skills run <name> --dry-run -- <task>` expands a pack into a reviewable plan without calling a model, writing files, running shell, or starting MCP. Non-dry-run skill runs route through the existing agentic `exec` safety path and record skill metadata in text, JSON, trace, session task reports, and markdown reports.
- Built-in .NET workflow packs: `test-fix`, `review-only`, `upgrade-package`, and `doc-sync`. Pack manifests are JSON-only in this increment, with workspace-local loading from `.caicli/skills`.
- Week 48 markdown task reports for `exec`: `--report markdown` emits a full redacted task report in text mode, `--report-path <path>` writes an explicit workspace-bounded markdown file without overwriting existing files, and JSON output keeps markdown out of the NDJSON stream while carrying structured report metadata.
- Week 48 local expert profiles for `exec`: `--expert bugfix|reviewer|tester|security|refactor` records expert metadata in prompt context, text, JSON, trace, session task reports, and markdown reports. `reviewer` and `security` are read-only profiles that disable write, shell, and MCP tools and skip MCP discovery.
- Week 47 workflow inputs for `exec`: inline `@file:<path>` and `@folder:<path>` references are resolved inside the workspace before model execution, bounded by file count, byte count, depth, binary-file, and workspace-guard checks, and recorded in text, JSON, trace, session, and `taskReport` metadata without persisting raw referenced content in reports.
- Week 47 read-only `caicli changes`: summarizes git status/diff stat, changed files, optional latest session task report, commands, verification, remaining risks, trace path, and warnings in text or JSON without calling a model, running shell/patch tools, starting MCP, or writing command logs by default.

Changed:
- Default smoke coverage now includes credential-free `skills list`, `skills list --output json`, and `skills run review-only --dry-run` paths.
- `exec` task reports now include report artifact metadata and selected expert metadata. Markdown reports are generated from the final `AgentTaskReport` object and do not persist raw referenced file contents.
- Default smoke coverage now includes credential-free `exec --report markdown`, `--report-path`, `--expert reviewer`, and `--expert security --output json` paths.
- `exec` task reports now include workflow reference metadata and final text output includes a compact reference summary when references are present.
- Default smoke coverage now includes `changes`, `changes --output json`, `changes --session` warning behavior, and `exec @file` reference diagnostics without requiring model credentials.

Known deferred items:
- `@file` / `@folder` references are scoped to `exec`; `chat` references, URL references, glob expansion, semantic retrieval, and workflow-pack references remain Deferred.
- Remote skill marketplaces, automatic skill updates, signed trust chains, custom expert files, automatic model role routing, user-level skill directories, YAML manifests, and Gerber/TIFF real execution remain Deferred.

## 0.3.0 - 2026-07-12

Added:
- Week 39 real agent loop completion for `exec`: configured direct OpenAI Responses SDK runs now use the same `IAgentRunner` surface as the fake/offline contract instead of stopping at the readiness gateway.
- Week 39 SDK tool-call continuation: unified tool schemas are mapped to Responses function tools, SDK function calls are parsed into the existing agent loop, structured tool results are written back as function-call outputs, and the model can continue until final output or a bounded stop condition.
- Week 40 state machine limits for agentic `exec`: runs enforce step/turn, tool-call, retry, and timeout budgets, and terminal results preserve `status` and `stopReason` so limit exits are explicit.
- Week 41 bounded startup context and plan events: `exec` records workspace/cwd, instruction source order, session/resume context, and git status/diff summaries before write-capable work, while bounding oversized context and warning instead of expanding unboundedly.
- Week 42 patch and verification workflow: patch and shell actions remain approval-gated and workspace-guarded, changed files are recorded, and verification results are attached to tool feedback.
- Week 43 failure feedback retry: verification/tool failures can be summarized back to the model as bounded feedback, and retry is finite through `--max-retries`.
- Week 44 read-only `review.gate` and final `taskReport` outputs for `exec`: JSON output emits structured events and terminal result payloads, while text output surfaces changed files, commands, verification status, remaining risks, and trace path when available.
- Week 45 smoke, documentation, and release-boundary coverage for the real-agent release line: release docs now describe the direct SDK loop, offline/fake contract, opt-in real model smoke, retry behavior, review gate, task report, safety boundaries, and Deferred enhanced capabilities.

Changed:
- The default configured `exec` path no longer treats direct OpenAI SDK agent tool-call continuation as Deferred; it is current `0.3.0` behavior when model credentials are configured.
- Release smoke keeps real model execution opt-in with `CAICLI_REAL_MODEL_SMOKE=1`; default smoke remains credential-free and local-only.
- Failure retry does not bypass approval, workspace path guards, dirty-workspace checks, shell policy, dangerous-command detection, or timeout limits.
- `review.gate` and `taskReport` are documented as diagnostics and release evidence, not correctness proofs or replacements for human diff review.

Known deferred items:
- Real Microsoft Agent Framework runtime backend.
- Remote/http MCP transport.
- Real Gerber/TIFF toolchain execution.
- Dotnet tool packaging.
- Standalone full markdown task report file emission by default; compact task report events and session metadata are included.
- Interactive approval UI; non-interactive approval modes continue to report approval-required failures for write and shell actions.

## 0.2.1 - 2026-07-10

Changed:
- Default `exec` now routes configured model/key runs through the existing `OpenAiAgentRunner` / `OpenAiToolCallingModel` abstraction instead of stopping before the direct agent path.
- Until SDK tool-call continuation is implemented, the default direct agent gateway still reports the same structured `agent-backend-unavailable` error without leaking API keys.
- Release smoke tests now read the release version from `Directory.Build.props` instead of hardcoding `0.2.0`, so later 0.3.0 release verification can reuse the script.

Known deferred items:
- Direct OpenAI SDK tool-call continuation still needs the Week 39 SDK request/response translation work before real model-driven tool loops can run.

## 0.2.0 - 2026-07-10

Added:
- Configurable OpenAI-compatible `baseUrl`, `config list`, and scalar `config set` / `config unset` for user config.
- Agentic `exec` v1 surface with text/NDJSON output, loop limits, approval mode overrides, session transcript support, and `--cwd` instruction targeting.
- Approval modes, tool risk metadata, centralized tool error codes, structured tool payloads, `tools list --json`, and `tools call --stdin`.
- Hierarchical project instructions from `AGENTS.md` with legacy `AICLI.md` fallback.
- `status`, `models`, `diff` / `diff --stat`, and read-only `review` commands for local development inspection.
- `review --json` and `review --output json` for machine-readable review results.
- Real stdio MCP v1 support for user-configured stdio servers through registry/tool paths, plus live stdio `mcp doctor` diagnostics.
- Shell policy controls for allowed commands, denied commands, and maximum timeout, also applied to MCP stdio startup.
- Global recursive `--verbose` diagnostics for safe human-readable command context without polluting JSON output streams.
- Global recursive `--trace` and `CAICLI_TRACE=1` for redacted trace-level local JSONL logs.
- `logs path`, `logs show --tail <n>`, and `logs clear` for inspecting and cleaning CLI log files.
- `session list`, `session show <name>`, `session rename <old> <new>`, and `session delete <name>` for local transcript management.
- `chat --resume <session>` and `exec --resume <session>` require an existing transcript and pass normalized prior transcript context to the model or agent request.
- `session export --format json|markdown <name>` supports raw JSON transcript export and readable markdown export.

Changed:
- Version metadata and release artifact names now target `0.2.0`.
- Project instructions prefer `AGENTS.md` over legacy `AICLI.md` in each directory.
- Command logs continue to use `yyyy-MM-dd.log`; trace logs use `yyyy-MM-dd.trace.log`, and diagnostics record key presence/source instead of raw key values.
- `session clear <name>` remains supported for compatibility, but `session delete <name>` is the recommended delete command.
- Markdown session export omits raw tool arguments, fences transcript-controlled bodies, escapes metadata, and redacts common secret values.
- Release smoke coverage now exercises offline config, exec, approval, session, instruction, MCP stdio, workflow, log, status, models, and git diff paths without model credentials.
- Stdio MCP v1 registry/tool support is limited to user-configured stdio servers; workspace-configured MCP servers are not auto-started during ordinary registry creation, while `mcp doctor` remains an explicit diagnostic path for configured stdio servers.

Known deferred items:
- Real Microsoft Agent Framework runtime backend.
- Remote/http MCP transport.
- Real Gerber/TIFF toolchain execution.
- Dotnet tool packaging.
- Direct OpenAI SDK tool-call continuation for agentic `exec`; the current SDK gateway returns `agent-backend-unavailable` until tool calls/results are translated. `run` remains the deterministic direct-tool smoke path.

## 0.1.0 - 2026-06-07

First local MVP release candidate for C# AI CLI.

Added:
- Windows `win-x64` self-contained single-file release package.
- `caicli version`, `doctor`, `config get`, `chat`, `mcp list`, `mcp doctor`, `workflow list`, `workflow validate`, `tools list`, `tools call`, `run`, `session export`, and `session clear`.
- Direct OpenAI Responses chat path with streaming output and session transcript support.
- Workspace instruction loading from `AICLI.md`.
- Workspace read/search tools with path boundary protection.
- Single-file patch tool with preview, dirty-workspace awareness, and approval.
- Restricted shell tool with approval, workspace cwd guard, dangerous command detection, timeout, and output truncation.
- Git status and diff tools.
- Config support for `agentBackend`, MCP servers, workflow profiles, and `disabledTools`.
- Microsoft Agent Framework adapter boundary and tool DTO bridge as an experimental Deferred backend.
- MCP config/list/doctor and generic bridge as Deferred enhanced capability.
- Gerber/TIFF project pack status/profile MVP as Deferred enhanced capability.
- Release build script, smoke test script, installation/configuration/security/quickstart docs, known limitations, and final acceptance checklist.

Changed:
- Version metadata is centralized in `Directory.Build.props`.
- CLI executable assembly name is fixed as `caicli`.
- Release artifacts include `release-manifest.json`.

Known deferred items at the time of the 0.1.0 release:
- Real Microsoft Agent Framework runtime backend.
- Real MCP protocol handshake/tool discovery.
- Real Gerber/TIFF toolchain execution.
- Dotnet tool packaging.
- OpenAI SDK tool-call continuation for agentic `exec`; the current SDK gateway returns `agent-backend-unavailable` until tool calls/results are translated. `run` remains the deterministic direct-tool smoke path.
