## Week 37 review

Status: accepted

Completed:
- Step 1: Added `DiagnosticContext` coverage for command id, session id, workspace, workspace status, command name, and UTC timestamp context shared by diagnostics and trace logging.
- Step 2: Added duration, status, approval, and MCP diagnostics around exec model/tool/approval flows, using monotonic timing for elapsed measurements.
- Step 3: Added global recursive `--verbose` diagnostics for safe human-readable command context without polluting JSON output streams.
- Step 4: Added global recursive `--trace` and `CAICLI_TRACE=1` trace JSONL logging for exec flows, with redaction hardening for API keys, authorization headers, nested or escaped arguments, private/secret keys, GitHub tokens, and related secret-like payloads.
- Step 5: Added `logs path` to print the resolved CLI log directory without creating it.
- Step 6: Added `logs show --tail <n>` for best-effort tailing of direct command and trace log files.
- Step 7: Added `logs clear` with direct-only `*.log` deletion, preserving subdirectories and non-log files, and refusing linked/reparse log directories or linked/reparse ancestors.
- Step 8: Audited and extended regression coverage for log path resolution, tail behavior, clear behavior, redaction, trace disabled by default, and trace enablement through both flag and environment variable.
- Step 9: Updated the runtime logging diagnostics spec and release docs for verbose diagnostics, trace logs, log commands, redaction guarantees, and accepted residual limitations.
- Step 10: Ran final build/test verification and created this weekly review.

Verification:
- Environment: set `$env:DOTNET_ROOT='D:\AI\C-AICLI\.worktrees\.dotnet-sdk'` and prepended it to `$env:PATH` for all .NET commands.
- Command: `dotnet --version`
- Result: passed; SDK `9.0.308`.
- Command: `dotnet build src\CSharpAiCli.sln --no-restore`
- Result: passed; 0 warnings, 0 errors.
- Command: `dotnet test src\CSharpAiCli.sln --no-restore`
- Result: passed; 966 passed, 0 failed, 0 skipped; duration 43 s for `CSharpAiCli.Tests.dll`.
- Command: `git diff --check`
- Result: passed with exit code 0; no whitespace errors. Git emitted an LF-to-CRLF normalization notice for this new markdown file.

Runtime notes:
- Verbose diagnostics are safe, human-readable command diagnostics for text output and are intentionally omitted from JSON output streams.
- Trace logging writes local JSONL files named `yyyy-MM-dd.trace.log` under the resolved CLI log directory when `--trace` or `CAICLI_TRACE=1` is enabled.
- Trace records include shared command/session/workspace context plus ordered exec event/result details such as status, duration, approval status/duration, error code, sequence, and redacted payload data when available.
- Command logs continue to use `yyyy-MM-dd.log`; trace logs use `yyyy-MM-dd.trace.log`.
- `logs path` reports the resolved log directory without creating it.
- `logs show --tail <n>` reads existing direct `*.log` files, including trace logs, in filename/read order rather than timestamp-merging records across files. Missing log directories succeed with no output, and unreadable/raced files are skipped best-effort.
- `logs clear` deletes only direct `*.log` files in the resolved log directory. It does not recurse, does not follow linked/reparse paths, and refuses linked/reparse log directories or ancestors rather than clearing through them.

Risks:
- Parse-time `System.CommandLine` validation errors may return before trace context exists; this is accepted because no model/tool flow has begun.
- `logs show` is a simple filename/read-order tail rather than a global timestamp merge across command and trace files.
- `logs clear` is intentionally conservative around symlinks/reparse points and refuses those paths instead of following them.
- Trace payloads remain diagnostic data, not a stable public API contract.
- Redaction coverage is stronger for common secret forms, but diagnostics should continue to treat any new payload surface as sensitive by default.

Week 38 input:
- Decide whether `logs show` should eventually timestamp-merge command and trace records across files.
- Consider whether parse-time failures need a lightweight pre-context trace marker, or whether the accepted residual remains the right boundary.
- Continue treating trace payload shape as internal diagnostic data unless a public schema is explicitly designed.
- Include the observability commands in 0.2.0 release smoke coverage so trace/log behavior stays visible during packaging.
