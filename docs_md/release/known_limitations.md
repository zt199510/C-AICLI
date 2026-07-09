# Known Limitations

## Release Scope

- Version `0.1.0` is a local Windows MVP release candidate.
- The primary supported package is `win-x64` self-contained single-file publish.
- Dotnet tool packaging is not part of the first release package.

## Model And Agent Behavior

- `chat` uses the direct OpenAI Responses path.
- `exec` is routed through `IAgentRunner` for agentic v1 behavior. It emits agent model/tool/final/error events and enforces loop limits such as `--max-turns`, `--max-tool-calls`, and `--timeout-seconds`.
- `run` remains the deterministic direct-tool compatibility and smoke entry.
- The offline/fake model agent loop and OpenAI response parsing/writeback contracts are implemented and tested.
- The default direct OpenAI SDK gateway path for agent tool-call continuation is not yet enabled; real direct SDK tool loops return `agent-backend-unavailable` until SDK tool calls and tool results are translated.
- `chat --resume` and `exec --resume` provide prior transcript context only for existing local sessions. The context is normalized before use to reduce transcript section-spoofing risk.
- `review` is workspace-read-only and does not execute patch or shell tools or write workspace files, logs, transcripts, or patches. Diff collection may use cleaned-up temp files outside the workspace. It sends the current git diff to the configured model and requires configured model credentials for real use.
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
- Shell execution is restricted and approved, but users must still inspect commands.
- Dangerous command detection is conservative and pattern-based; it is not a complete proof of safety.

## Configuration And Secrets

- Workspace `apiKey` is ignored. Use `OPENAI_API_KEY` or user config for model calls.
- `baseUrl` can come from `OPENAI_BASE_URL`, user config, or workspace config, but it must be an absolute `http` or `https` URL without user info, query, or fragment components.
- `config get`, `config list`, `doctor`, and logs record key presence and source, not key value.
- Logs include configured model, base URL, and backend source/status for diagnostics.
- Release artifacts do not include user config, workspace config, keys, logs, or transcripts.

## Platform

- Windows is the primary target.
- Tests run on .NET 9 in the current environment.
- The repository SDK is locked by root `global.json` to .NET SDK `9.0.308` with `latestPatch` roll-forward.
