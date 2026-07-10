# Final Acceptance 0.2.1

## Release

- Version: `0.2.1`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.2.1-win-x64`
- Release executable: `artifacts/release/caicli-0.2.1-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.2.1-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.2.1-win-x64.zip`
- Release zip size: `32248419` bytes
- Release zip SHA256: `FA66CEA06002C9D684D5394D7AA686C03FF22F94E4070290C8F1EBF78A8DFA4F`

## Purpose

`0.2.1` is a readiness patch for the Week 39-46 `0.3.0` real-agent work. It preserves the accepted `0.2.0` command surface while connecting configured default `exec` runs to the direct OpenAI agent abstraction. Real SDK tool-call continuation remains a Week 39 implementation task.

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Release build succeeds | Passed | `dotnet build src\CSharpAiCli.sln -c Release`: 0 warnings, 0 errors. |
| Release tests pass | Passed | `dotnet test src\CSharpAiCli.sln -c Release --no-build`: 972 passed, 0 failed, 0 skipped. |
| Release package deterministic | Passed | Two `tools\Build-Release.ps1` runs produced identical zip size and SHA256. |
| Packaged smoke passes | Passed | `tools\Invoke-SmokeTests.ps1`: `smoke tests passed`. |
| 0.2.1 executable reports correct version | Passed | `caicli 0.2.1`. |
| Default `exec` reaches direct agent abstraction | Passed | `OpenAiAgentRunnerTests` and CLI default error-path regression. |
| SDK continuation remains clearly deferred | Passed | Default SDK gateway still returns structured `agent-backend-unavailable` until Week 39 translation work. |

## Verification Commands

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

## Decision

Release `0.2.1` is accepted on 2026-07-10 as the supported baseline for starting `0.3.0` real-agent development.
