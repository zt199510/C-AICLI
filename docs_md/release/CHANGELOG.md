# Changelog

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

Known deferred items:
- Real Microsoft Agent Framework runtime backend.
- Real MCP protocol handshake/tool discovery.
- Real Gerber/TIFF toolchain execution.
- Dotnet tool packaging.
- OpenAI SDK tool-call continuation for agentic `exec`; the current SDK gateway returns `agent-backend-unavailable` until tool calls/results are translated. `run` remains the deterministic direct-tool smoke path.
