## Week 36 review

Status: accepted

Completed:
- Step 1: Added shell policy configuration for `allowedCommands`, `deniedCommands`, and `maxTimeoutMilliseconds`, including effective config reporting and source tracking.
- Step 2: Extended dangerous command detection to return a readable reason and `matchedRule`.
- Step 3: Added shell command risk summaries to `workspace.run_shell` text output and structured payloads, without duplicating dangerous-denial text.
- Step 4: Added `workspace.apply_patch` dry-run preview structured payloads with paths, files, replacement counts, diff presence, and dirty workspace status.
- Step 5: Reused shell command risk checks before MCP stdio startup, including matched-rule reporting, encoded PowerShell alias coverage, payload-safe canonicalization, and direct/full-path PowerShell handling.
- Step 6: Added `doctor` diagnostics for shell policy, patch policy, and MCP execution policy, including trusted user-config stdio MCP counts.
- Step 7: Added and enforced shell policy coverage for allowlist, denylist, timeout max, dangerous matched rules, and MCP startup blocking. The policy now applies before shell execution, before MCP stdio process start, and during live `mcp doctor` stdio checks. Final review follow-up added regressions for nonpositive MCP timeouts, quoted encoded PowerShell executable/switch forms, and doctor startup policy enforcement.
- Step 8: Updated release security model and known limitations for shell policy, patch preview, MCP startup policy, matched-rule diagnostics, and conservative limitations.
- Step 9: Ran final build/test verification and created this weekly review.

Verification:
- Environment: set `$env:DOTNET_ROOT` to `%LOCALAPPDATA%\CodexDotnetSdk\9.0.308` and prepended that directory to `$env:PATH` for .NET commands.
- Command: `dotnet build src\CSharpAiCli.sln --no-restore`
- Result: passed; 0 warnings, 0 errors.
- Command: `dotnet test src\CSharpAiCli.sln --no-restore`
- Result: passed; 900 passed, 0 failed, 0 skipped.
- Command: `git diff --check`
- Result: passed after creating this weekly review; no whitespace errors.

Runtime notes:
- `workspace.run_shell` now evaluates configured shell policy before approval and before execution.
- Denylist entries take precedence over allowlist entries; empty configured allowlists deny all shell commands.
- Allowlist prefix matches allow ordinary arguments but conservatively reject shell control syntax such as `&`, `&&`, `||`, `;`, pipes, redirection, newlines, backticks, and command substitution.
- Timeout requests above `shellPolicy.maxTimeoutMilliseconds` are rejected with a safe explanation instead of silently clamped.
- MCP stdio startup evaluates dangerous command detection and configured shell policy before process start. Policy diagnostics use payload-safe startup command text, including redacted encoded PowerShell payloads. Nonpositive MCP timeouts are normalized before shell policy evaluation so policy and runtime use the same effective timeout.
- `mcp doctor` live stdio checks use the same configured shell policy and report policy-denied startup as unavailable without starting the server.
- Encoded PowerShell detection covers full-path, wrapped-shell, quoted executable, and quoted encoded switch forms.
- `tools call mcp.*` now surfaces safe MCP startup policy failures instead of reducing configured policy blocks to `unknown-tool`; disabled or genuinely missing MCP tools still fail as unknown tools.
- Patch dry-run preview is now represented in structured payloads but remains single-file exact-text replacement.

Risks:
- The CLI is still a local safety boundary, not a full sandbox.
- Shell policy is conservative command-text matching, not complete shell parsing or exact argv-array policy.
- Some quoted arguments containing shell metacharacters may be rejected by allowlist prefix policy.
- Dangerous command detection is pattern-based and should not be treated as semantic proof of safety.
- MCP remote/http transport remains Deferred.
- MCP policy and bridge diagnostics now duplicate a small amount of name/eligibility logic; consider extracting a shared helper if this grows.

Week 37 input:
- Consider documenting `shellPolicy` in `docs_md/release/configuration.md` with examples.
- Consider a shared MCP registry eligibility/name-normalization helper to reduce drift between `McpToolBridge` and CLI diagnostics.
- Consider CLI-level regression coverage for each shell metacharacter bypass shape if the policy surface expands.
- Continue remote/http MCP transport design only after the stdio safety model stays stable.
