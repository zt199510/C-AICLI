# C# AI CLI Capability Status

## MVP Release Capabilities

| Capability | Status | Notes |
|---|---|---|
| `caicli version` | Accepted | Reports version, target framework, and release runtime. |
| `caicli doctor` | Accepted | Diagnoses runtime, workspace, model/source, base URL/source, key presence/source, logs, and backend source/status. |
| `caicli config get/list/set/unset` | Accepted | Reports effective config and writes user scalar config without printing secret values. |
| Project instruction loading | Accepted | Loads root-to-leaf `AGENTS.md`/legacy `AICLI.md` sources for `chat` and agentic `exec`; `AGENTS.md` wins per directory, and reports show source order without contents. |
| `caicli chat` | Accepted | Uses direct OpenAI Responses path with streaming renderer, configured OpenAI-compatible base URL, and `--cwd` instruction target selection. |
| `chat --session` | Accepted | Saves transcript v1 in the user profile. |
| `chat --resume` | Accepted | Requires an existing transcript and sends normalized prior transcript context with the current prompt. |
| `session list/show/rename/delete/export/clear` | Accepted | Lists local transcript summaries, shows one summary, renames or deletes one transcript by validated session name, exports JSON or markdown, and keeps `clear` as a compatibility alias for delete. |
| `caicli exec` | Solidified | Agentic v1 surface routed through `IAgentRunner`; emits model/tool/final/error events and supports `--cwd` instruction target selection, `--output text|json`, loop limits, approval-gated tool actions, session transcript, and `--resume` context. Real direct SDK tool-call continuation is deferred. |
| `caicli run` | Solidified | Deterministic direct-tool release smoke tasks: create smoke note, read file, run approved shell command. |
| `tools list/call` | Solidified | Lists enabled tools and invokes one tool with JSON or `--arguments-file`. |
| Workspace read/search tools | Accepted | Enforced by workspace guard. |
| Patch tool | Accepted | Single-file exact-text patch with approval and dirty-workspace awareness. |
| Shell tool | Accepted | Workspace cwd guard, approval, dangerous command detection, timeout, and truncation. |
| Git status/diff tools | Accepted | Tested against temporary repositories. |
| Windows release package | Solidified | Self-contained `win-x64` single-file executable and manifest. |

## Enhanced Or Deferred Capabilities

| Capability | Status | Notes |
|---|---|---|
| Microsoft Agent Framework real backend | Deferred | Adapter project and tool bridge exist; real framework package/runtime is not enabled. |
| MCP real protocol handshake | Deferred | Config/list/doctor and generic bridge exist; real discovery/execution is not enabled. |
| Gerber/TIFF real workflow execution | Deferred | Project pack status/profile MVP exists; real toolchain execution is not enabled. |
| Dotnet tool package | Deferred | Windows self-contained package is the first release artifact. |
| Direct OpenAI SDK agent tool loop | Deferred | Offline/fake agent loop contracts are implemented and tested, but the default SDK gateway still returns `agent-backend-unavailable` for tool-call continuation until SDK tool calls/results are translated. |

## Release Decision

The direct backend MVP is releasable when Phase 06 smoke tests and final acceptance pass. Deferred enhanced capabilities are documented and must not be presented as first-release core behavior.
