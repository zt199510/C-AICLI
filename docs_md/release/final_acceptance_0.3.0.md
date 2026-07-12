# Final Acceptance 0.3.0

## Release

- Version: `0.3.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.3.0-win-x64`
- Release executable: `artifacts/release/caicli-0.3.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.3.0-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.3.0-win-x64.zip`
- Release zip size: `32312392` bytes
- Release zip SHA256: `030BF5AFCAB3365A15D296A73DA8967ED87D8ECF816F3B61ED990F97CB3FDFDA`

## Purpose

`0.3.0` accepts the Week 39-45 direct backend work as the current real-agent development-loop release. It promotes the direct OpenAI Responses SDK agent tool-call loop, bounded `exec` state machine, bounded context/plan events, patch/verification workflow, finite failure retry, `review.gate`, `taskReport`, smoke coverage, and release documentation into the supported release line.

## Week 39-45 Status

| Week | Capability | Status | Release Decision |
|---|---|---|---|
| 39 | Real OpenAI Responses SDK agent loop and tool-call continuation | Solidified | Included as current `0.3.0` behavior for configured direct OpenAI `exec` runs; fake/offline contracts exercise the same schema/result/event path. |
| 40 | Agent state machine limits | Accepted | Included with bounded steps/turns, tool calls, retries, timeouts, terminal `status`, and terminal `stopReason`. |
| 41 | Bounded context and plan events | Accepted | Included for workspace/cwd, instruction source, session/resume, and git summary context before write-capable work. |
| 42 | Patch and verification workflow | Accepted | Included with approval-gated patch/shell execution, dirty-workspace and content rechecks, changed files, and verification result payloads. |
| 43 | Failure feedback retry | Accepted | Included with bounded failure summaries, finite retry budgets, and failure reports when retry is exhausted. |
| 44 | `review.gate` and `taskReport` | Accepted | Included as read-only final diagnostics for `exec`; they summarize state and risk but do not prove correctness. |
| 45 | Smoke, release docs, and Deferred boundary | Accepted | Default smoke remains offline/local-only; real model smoke remains opt-in with `CAICLI_REAL_MODEL_SMOKE=1`. |

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Release build succeeds | Passed | `dotnet build src/CSharpAiCli.sln -c Release` succeeded with 0 warnings and 0 errors using SDK `9.0.308`. |
| Release tests pass | Passed | `dotnet test src/CSharpAiCli.sln -c Release --no-build` passed: 1020 passed, 0 failed, 0 skipped. |
| Release package deterministic | Passed | `tools\Build-Release.ps1` ran twice. Both runs produced size `32312392` bytes and SHA256 `030BF5AFCAB3365A15D296A73DA8967ED87D8ECF816F3B61ED990F97CB3FDFDA`. |
| Packaged smoke passes | Passed | `tools\Invoke-SmokeTests.ps1` passed against `artifacts\release\caicli-0.3.0-win-x64\caicli.exe`; default real model smoke was skipped because `CAICLI_REAL_MODEL_SMOKE` was not set. |
| 0.3.0 executable reports correct version | Passed | `caicli 0.3.0`, `target framework: net9.0`, `release runtime: win-x64`. |
| Release manifest matches metadata | Passed | Manifest reports `version: 0.3.0`, `runtime: win-x64`, `targetFramework: net9.0`, `executable: caicli.exe`, and `builtFromVersion: 0.3.0`. |
| Real model smoke path is explicitly handled | Passed | Explicit opt-in command with `CAICLI_REAL_MODEL_SMOKE=1` and no `OPENAI_API_KEY`/`OPENAI_MODEL` skipped with `requires caller OPENAI_API_KEY and OPENAI_MODEL` and did not fail smoke. |
| Direct SDK tool-call continuation is covered | Passed | Week 39 tests cover tool schema mapping, function-call parsing, tool-result writeback, continuation, final output, and opt-in real-network smoke behavior. |
| Agent state machine limits are covered | Passed | Week 40 tests cover max steps/turns, tool calls, retries, timeout, terminal status, and stop reason. |
| Bounded context and plan events are covered | Passed | Week 41 tests cover bounded workspace/cwd, instruction source, session/resume, and git status/diff summary context. |
| Patch/verify/failure retry stays inside safety boundaries | Passed | Week 42-43 tests and smoke cover patch preview/apply, changed files, verification result payloads, finite retry, and no bypass of approval, workspace guard, dirty-workspace checks, shell policy, dangerous-command detection, or timeouts. |
| `review.gate` and `taskReport` are emitted | Passed | Week 44-45 tests cover text and JSON output, terminal `exec.result` payloads, changed files, commands, verification status, remaining risks, trace path, session summaries, and secret redaction. |
| Release docs describe smoke and Deferred boundaries | Passed | `CHANGELOG.md`, `configuration.md`, `quickstart.md`, `security_model.md`, `known_limitations.md`, `capability_status.md`, `installation.md`, `troubleshooting.md`, and this file document current and Deferred behavior. |

## Verification Commands

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
$env:CAICLI_REAL_MODEL_SMOKE = "1"
Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
artifacts\release\caicli-0.3.0-win-x64\caicli.exe version
Get-Content artifacts\release\caicli-0.3.0-win-x64\release-manifest.json
```

## Deferred Items

- Real Microsoft Agent Framework runtime backend.
- Remote/http MCP transport.
- Real Gerber/TIFF workflow execution.
- Dotnet tool package.
- Standalone full markdown task report file emission by default.
- Interactive approval UI and richer approval workflow.

## Decision

Release `0.3.0` is accepted on 2026-07-12 as the real-agent development-loop release. The accepted scope is the local Windows `win-x64` package with credential-free default smoke and opt-in real model smoke. Direct OpenAI SDK agent tool-call continuation, fake/offline agent contracts, `exec`, `review.gate`, `taskReport`, and user-configured stdio MCP v1 are current release behavior. Deferred items remain outside the release boundary.
