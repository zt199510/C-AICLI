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

## File Editing

The release MVP uses a single-file exact-text patch tool.

- Patch preview runs before apply.
- Patch apply rechecks the file content before writing.
- Dirty workspace state is included in the preview.
- Default production approval denies file edits unless an explicit approval policy allows them.
- Patch operations are recorded as transcript tool calls when run through `exec --session` and the agent loop.

## Shell Execution

Shell execution is restricted:

- The working directory must remain inside the workspace.
- Commands require approval.
- Dangerous command patterns are denied before execution.
- Commands have timeouts and stdout/stderr byte limits.
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
- MCP config/list/doctor and a generic bridge exist, but real MCP protocol handshake and tool discovery are Deferred.
- Gerber/TIFF project pack status/profile support exists, but real Gerber execution is Deferred.

These deferred capabilities do not block the direct backend release.

## Current Limitations

- The tool cannot guarantee semantic correctness of generated code.
- It does not run destructive commands without the shell runner safety checks, but users should still review commands and diffs.
- The first release is Windows-focused.
- Dotnet tool packaging is not enabled for the first release package.
