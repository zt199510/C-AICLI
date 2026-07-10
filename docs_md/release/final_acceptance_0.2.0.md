# Final Acceptance 0.2.0

## Release

- Version: `0.2.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.2.0-win-x64`
- Release executable: `artifacts/release/caicli-0.2.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.2.0-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.2.0-win-x64.zip`
- Release zip size: `32247811` bytes
- Release zip SHA256: `A65CB4AC5276C54E0C6DCB6104D438B6D8F18D705E760AAB84F1B7B00EB21565`

## Week 27-37 Status

| Week | Capability | Status | Release Decision |
|---|---|---|---|
| 27 | Config `baseUrl`, `config list/set/unset` | Accepted | Included. |
| 28 | `exec` text/NDJSON command surface | Accepted | Included. |
| 29 | Agent run loop v1 contracts | Solidified | Included as surface/contracts; direct OpenAI SDK tool-call continuation remains Deferred. |
| 30 | Approval modes and permission profiles | Accepted | Included; no interactive approval UI. |
| 31 | Session resume and management | Accepted | Included; `session clear` remains compatibility alias. |
| 32 | `AGENTS.md` project instructions | Accepted | Included. |
| 33 | `status`, `models`, `diff`, `review` | Accepted | Included; real `review` requires model credentials and sends diff to provider. |
| 34 | Tool schema, error codes, JSON/stdin tool calls | Accepted | Included. |
| 35 | Real stdio MCP v1 | Accepted | Included for user-configured stdio servers; remote/http remains Deferred. |
| 36 | Shell/patch/MCP security boundaries | Accepted | Included; local safety boundary, not a sandbox. |
| 37 | Verbose diagnostics, trace logs, log commands | Accepted | Included; trace payload shape remains diagnostic/internal. |

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Release build succeeds | Passed | `dotnet build src\CSharpAiCli.sln -c Release`: 0 warnings, 0 errors. |
| Release tests pass | Passed | `dotnet test src\CSharpAiCli.sln -c Release --no-build`: 970 passed, 0 failed, 0 skipped. |
| Release package deterministic | Passed | Two `tools\Build-Release.ps1` runs produced identical zip size and SHA256. |
| Packaged smoke passes | Passed | `tools\Invoke-SmokeTests.ps1`: `smoke tests passed`. |
| 0.2.0 executable reports correct version | Passed | Smoke asserts `caicli 0.2.0`. |
| Config, exec, approval, sessions, instructions, MCP stdio, logs covered by smoke | Passed | Expanded `tools\Invoke-SmokeTests.ps1` and `SmokeTestScriptTests`. |
| capability status distinguishes Accepted, Solidified, and Deferred accurately | Passed | `docs_md/release/capability_status.md`. |
| Implemented MCP stdio capability is not listed as broadly Deferred | Passed | Stdio MCP v1 is listed as Solidified; only remote/http MCP remains Deferred. |

## Verification Commands

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

## Deferred Items

- Real Microsoft Agent Framework runtime backend.
- Remote/http MCP transport.
- Real Gerber/TIFF toolchain execution.
- Dotnet tool packaging.
- Direct OpenAI SDK agent tool-call continuation for `exec`; default direct SDK tool-loop work still returns `agent-backend-unavailable`.
- Interactive approval UI and `on-failure` sandbox retry escalation.

## Decision

Release `0.2.0` is accepted on 2026-07-10. The release build, full test suite, deterministic package check, and packaged smoke tests passed, and the release zip exists with the size and SHA256 recorded above.
