# Known Limitations

## Release Scope

- Version `0.1.0` is a local Windows MVP release candidate.
- The primary supported package is `win-x64` self-contained single-file publish.
- Dotnet tool packaging is not part of the first release package.

## Model And Agent Behavior

- `chat` uses the direct OpenAI Responses path.
- `run` is a deterministic direct-tool smoke/task entry and does not yet perform general natural-language agent planning with real OpenAI tool calling.
- The Microsoft Agent Framework project is an adapter boundary and experimental stub; the real framework runtime backend is Deferred.

## MCP And Project Packs

- MCP config/list/doctor are available.
- Real MCP protocol handshake and dynamic tool discovery are Deferred.
- The Gerber/TIFF project pack records status/profile behavior, but real Gerber/TIFF conversion execution is Deferred.

## Safety Boundaries

- The tool is local and not a sandbox.
- Workspace path checks reduce accidental boundary escapes but do not replace OS permissions or code review.
- Patch editing is single-file exact-text replacement, not a full merge engine.
- Shell execution is restricted and approved, but users must still inspect commands.
- Dangerous command detection is conservative and pattern-based; it is not a complete proof of safety.

## Configuration And Secrets

- Workspace `apiKey` is ignored. Use `OPENAI_API_KEY` or user config for model calls.
- Logs record key presence and source, not key value.
- Release artifacts do not include user config, workspace config, keys, logs, or transcripts.

## Platform

- Windows is the primary target.
- Tests run on .NET 9 in the current environment.
- No `global.json` SDK lock is currently present.
