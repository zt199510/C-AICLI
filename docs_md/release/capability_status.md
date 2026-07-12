# C# AI CLI Capability Status

## 0.2.x Release Capabilities

| Capability | Status | Notes |
|---|---|---|
| `caicli version` | Accepted | Reports version, target framework, and release runtime. |
| `caicli doctor` | Accepted | Diagnoses runtime, workspace, model/source, base URL/source, key presence/source, logs, and backend source/status. |
| Global diagnostics | Accepted | Recursive `--verbose` prints safe human-readable diagnostics for text output; recursive `--trace` or `CAICLI_TRACE=1` writes redacted trace JSONL without polluting JSON command output. |
| `caicli logs path/show/clear` | Accepted | Prints the resolved log directory, tails existing command and trace logs, and clears only direct `*.log` files with best-effort raced/locked-file handling and symlink/reparse safety checks. |
| `caicli status` | Accepted | Reports workspace, git, and effective configuration state without requiring model credentials. |
| `caicli models` | Accepted | Prints local current model/base URL/source and static examples only; does not call a model list API or require an API key. |
| `caicli config get/list/set/unset` | Accepted | Reports effective config and writes user scalar config without printing secret values. |
| Project instruction loading | Accepted | Loads root-to-leaf `AGENTS.md`/legacy `AICLI.md` sources for `chat` and agentic `exec`; `AGENTS.md` wins per directory, and reports show source order without contents. |
| `caicli chat` | Accepted | Uses direct OpenAI Responses path with streaming renderer, configured OpenAI-compatible base URL, and `--cwd` instruction target selection. |
| `chat --session` | Accepted | Saves transcript v1 in the user profile. |
| `chat --resume` | Accepted | Requires an existing transcript and sends normalized prior transcript context with the current prompt. |
| `session list/show/rename/delete/export/clear` | Accepted | Lists local transcript summaries, shows one summary, renames or deletes one transcript by validated session name, exports JSON or markdown, and keeps `clear` as a compatibility alias for delete. |
| Exec bounded context and plan events | Accepted | At `exec` startup, collects bounded workspace/cwd, instruction source, session/resume, and git status/diff summary context, then emits a traceable `plan` event before write-capable work; oversized context/plan details are bounded and warned instead of expanding unboundedly. |
| `caicli exec` | Solidified | Agentic v1 surface routed through `IAgentRunner`; emits model/tool/final/error plus read-only `review.gate` and final `taskReport` events with terminal `status`/`stopReason`, supports `--cwd` instruction target selection, `--output text|json`, `--max-steps`/`--max-turns`, `--max-tool-calls`, `--max-retries`, timeout budgets, approval-gated tool actions, session transcript run summaries, and `--resume` context. Tool/shell/verification failures can be summarized as bounded model feedback for finite retry, and retry/failure summaries preserve changed files, commands, and stop reasons. Task reports preserve changed files, commands, verification status, remaining risks, trace path, and secret presence/source/kind without storing secret values. Configured direct OpenAI runs use real Responses SDK tool-call continuation through the same offline/fake agent contract. |
| `caicli diff` / `diff --stat` | Accepted | Prints the current git diff or stat summary for the selected workspace. |
| `caicli review` | Accepted | Sends the current git diff to the configured model for workspace-read-only review; supports text, `--json`, and `--output json`; does not write workspace files, logs, transcripts, patches, or run shell/patch tools. Diff collection may use cleaned-up temp files outside the workspace. |
| `caicli run` | Solidified | Deterministic direct-tool release smoke tasks: create smoke note, read file, run approved shell command. |
| `tools list/call` | Solidified | Text `tools list` is preserved; `tools list --json` emits a stable `tools.list` object with sorted `tools[]` metadata and sorted `disabledTools[]`; `tools call` accepts inline JSON, `--arguments-file`, or `--stdin` JSON. |
| Stdio MCP v1 tools | Solidified | User-configured stdio MCP servers can be discovered and called through registry/tool paths using real initialize, `tools/list`, and `tools/call`. Workspace-configured MCP servers are not auto-discovered or started during ordinary registry creation. |
| `caicli mcp list/doctor` | Accepted | Lists configured MCP servers. `mcp doctor` can explicitly diagnose configured stdio servers with a real initialize handshake, including workspace config; disabled servers are not started. |
| Tool schema/result contracts | Solidified | `ToolSchemaRenderer` normalizes parameter schemas for OpenAI mapping, the Agent Framework bridge, and `tools list --json`; tool failures use centralized `ToolErrorCode` values and agent-level `AgentFailureKind` classification; tool results can carry optional structured payloads and bounded retry feedback for agent/runtime consumers while keeping CLI text output compatible. |
| Workspace read/search tools | Accepted | Enforced by workspace guard. |
| Patch tool | Accepted | Single-file exact-text patch with approval and dirty-workspace awareness. |
| Shell tool | Accepted | Workspace cwd guard, approval, dangerous command detection, timeout, and truncation. |
| Git status/diff tools | Accepted | Tested against temporary repositories. |
| Windows release package | Solidified | Self-contained `win-x64` single-file executable and manifest. |

## Enhanced Or Deferred Capabilities

| Capability | Status | Notes |
|---|---|---|
| Microsoft Agent Framework real backend | Deferred | Adapter project and tool bridge exist; real framework package/runtime is not enabled. |
| MCP remote/http transport | Deferred | Stdio MCP v1 is available for user-configured stdio servers in registry/tool paths and for explicit `mcp doctor` diagnostics; remote/http transport is not enabled. |
| Gerber/TIFF real workflow execution | Deferred | Project pack status/profile MVP exists; real toolchain execution is not enabled. |
| Dotnet tool package | Deferred | Windows self-contained package is the current release artifact. |
| Direct OpenAI SDK agent tool loop | Solidified | The SDK gateway translates unified tool schemas to Responses function tools, parses model function calls, writes structured tool results back as function-call outputs, and keeps real-network smoke opt-in so normal tests do not require credentials. |

## Release Decision

The direct backend `0.2.x` release line is scoped to the accepted and solidified capabilities above. Deferred enhanced capabilities are documented and must not be presented as current release behavior.
