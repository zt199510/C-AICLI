# C# AI CLI Capability Status

## MVP Release Capabilities

| Capability | Status | Notes |
|---|---|---|
| `caicli version` | Accepted | Reports version, target framework, and release runtime. |
| `caicli doctor` | Accepted | Diagnoses runtime, workspace, model/source, base URL/source, key presence/source, logs, and backend source/status. |
| `caicli config get/list/set/unset` | Accepted | Reports effective config and writes user scalar config without printing secret values. |
| `caicli chat` | Accepted | Uses direct OpenAI Responses path with streaming renderer and configured OpenAI-compatible base URL. |
| `chat --session` | Accepted | Saves transcript v1 in the user profile. |
| `session export/clear` | Accepted | Exports or deletes one transcript file by validated session name. |
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

## Release Decision

The direct backend MVP is releasable when Phase 06 smoke tests and final acceptance pass. Deferred enhanced capabilities are documented and must not be presented as first-release core behavior.
