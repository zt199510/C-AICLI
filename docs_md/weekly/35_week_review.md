## Week 35 review

Status: accepted

Completed:
- Step 1: Added MCP JSON-RPC request/response DTOs.
- Step 2: Implemented stdio process transport/session with timeout, stderr capture and redaction, workspace cwd guard, notification handling, id matching, bounded stdin/write deadlines, bounded stderr, and bounded stdout line reads.
- Step 3: Implemented the MCP initialize handshake using protocol `2025-03-26`, required `serverInfo`, and fail-fast protocol mismatch handling before the initialized notification.
- Step 4: Implemented `tools/list` with `nextCursor` pagination, a 100-page ceiling, and input schema validation/defaulting.
- Step 5: Implemented `tools/call` with JSON object arguments, required content arrays, content block validation, and `isError: true` protocol-success semantics.
- Step 6: Mapped discovered stdio MCP tools into the local registry as `ToolDefinition` entries named like `mcp.<server>.<tool>`, with deterministic normalization/collision suffixing, real stdio discovery/invocation, and `isError: true` mapped to failed local tool results.
- Step 7: Added real stdio initialize handshake diagnostics to `mcp doctor`; stdio servers become active on success, unavailable on safe failure, disabled without start, and remote/http remains deferred.
- Step 8: Added a reusable fake stdio MCP server fixture covering handshake, `tools/list`, `tools/call`, timeout, and invalid JSON; stabilized the FileConversationStore temp-file observation race.
- Step 9: Updated `docs_md/release/known_limitations.md` to state that stdio MCP v1 is available and remote/http MCP remains Deferred.
- Step 10: Ran final build/test verification and created this weekly review.

Verification:
- Environment: set `$env:DOTNET_ROOT='D:\AI\C-AICLI\.worktrees\.dotnet-sdk'` and prepended it to `$env:PATH` for all .NET commands.
- Command: `dotnet --version`
- Result: passed; SDK `9.0.308`.
- Command: `dotnet build src\CSharpAiCli.sln --no-restore`
- Result: passed; 0 warnings, 0 errors.
- Command: `dotnet test src\CSharpAiCli.sln --no-restore`
- Result: passed; 801 passed, 0 failed, 0 skipped.
- Command: `git diff --check`
- Result: passed with exit code 0; no whitespace errors. Git emitted an LF-to-CRLF normalization notice for this new markdown file.

Runtime notes:
- stdio MCP v1 is available for configured stdio servers: initialize, `tools/list`, discovered tool registration, and `tools/call` are now on real stdio protocol paths.
- Remote/http MCP remains Deferred and should not be presented as available.
- `mcp doctor` now performs real stdio handshake diagnostics for enabled stdio servers and keeps disabled or unsupported transports safe.
- MCP protocol `isError: true` remains a successful JSON-RPC response but is surfaced locally as a failed tool result.
- The fake stdio MCP fixture is now the reusable regression surface for handshake, discovery, invocation, timeout, and invalid JSON behavior.

Risks:
- Remote/http MCP transport is still Deferred and needs a separate design/acceptance pass.
- Reviewer notes worth carrying forward: discovery failures could expose richer detail, failed tool summaries can be improved, and the doctor cancellation API/UX should be tightened.
- Security and sandbox boundaries around external stdio processes should remain conservative, especially cwd guards, process lifetime, stderr/stdout bounds, and argument handling.
- Protocol compatibility is intentionally narrow around `2025-03-26`; future MCP protocol versions may require explicit negotiation or compatibility documentation.
- The FileConversationStore temp-file race was stabilized, but similar filesystem observation timing remains worth watching in Windows-heavy test paths.

Week 36 input:
- Continue with remote MCP design or defer it explicitly behind user-visible status.
- Improve MCP diagnostics: richer discovery errors, clearer failed invocation summaries, and more actionable doctor output.
- Tighten cancellation and process-lifetime UX for `mcp doctor` and stdio sessions.
- Revisit security/sandbox boundaries for external tool processes before broadening MCP transport support.
