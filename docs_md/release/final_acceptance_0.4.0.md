# Final Acceptance 0.4.0

## Release

- Version: `0.4.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.4.0-win-x64`
- Release executable: `artifacts/release/caicli-0.4.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.4.0-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.4.0-win-x64.zip`
- Release zip size: `44422661` bytes
- Release zip SHA256: `FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`

## Purpose

`0.4.0` accepts the Week 50-56 local engineering automation work as the current release line. It adds local job history and artifact pointers, manual task queue/run control, fixed sequential multi-role pipelines, workspace-local automation validation/dry-run/manual triggers, and provider-neutral CI artifacts while preserving the existing approval, workspace, dirty-workspace, shell, disabled-tool, MCP startup, redaction, trace/session/report, and smoke boundaries.

The default-off, IPv4 loopback-only, read-only Local API/daemon remains Preview. Background scheduling, concurrent or remote workers, provider APIs, control routes, SSE, authentication/TLS, remote bind, and remote control remain Deferred.

## Week 50-56 Status

| Week | Capability | Status | Release Decision |
|---|---|---|---|
| 50 | Local job history and artifact index | Accepted | `jobs list/show/export` and opt-in execution recording use bounded redacted job metadata and existing task-report/artifact pointers. |
| 51 | Local task queue and run control | Accepted | Manual add/list/show/run, pending-only cancel, and terminal queue cleanup reuse the existing CLI command factory and safety path. |
| 52 | Local multi-role pipeline | Accepted | Fixed sequential pipelines reuse expert/skill/queue/job/report paths; reviewer/security stages remain read-only. |
| 53 | Workspace-local automation | Accepted | List/validate/plan/dry-run and explicit manual trigger are supported; schedule data remains preview-only and does not start a scheduler. |
| 54 | Provider-neutral CI artifacts | Accepted | Existing jobs can be projected into deterministic JSON/markdown/check output without provider calls or a second report store. |
| 55 | Local API / daemon | Preview | Explicit `--preview`, IPv4 loopback-only, read-only health/jobs/queue inspection; no control, model/tool/MCP, auth/TLS, or remote surface. |
| 56 | Security, smoke, and docs hardening | Accepted | Redaction, corrupt-store diagnostics, API request boundaries, listener cleanup, credential-free smoke, and docs status boundaries were hardened. |

No implementation item remained after Week 56. Week 57 release work was limited to metadata, documentation consistency, regression verification, deterministic packaging, and release evidence.

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Version metadata is current | Passed | `Directory.Build.props` reports `0.4.0`; packaged `caicli.exe version` prints `caicli 0.4.0`, `target framework: net9.0`, and `release runtime: win-x64`; the manifest reports version/builtFromVersion `0.4.0`. |
| Release build succeeds | Passed | `dotnet build src\CSharpAiCli.sln -c Release` succeeded with 0 warnings and 0 errors. |
| Release tests pass | Passed | `dotnet test src\CSharpAiCli.sln -c Release --no-build` passed: 1147 passed, 0 failed, 0 skipped. The initial run found one stale `0.3.3` release-version assertion; it was updated to `0.4.0`, then the targeted 6-test release suite and full suite passed. |
| Release package deterministic | Passed | Two consecutive `tools\Build-Release.ps1` runs produced size `44422661` bytes and SHA256 `FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`. |
| Default packaged smoke passes | Passed | Credential-free `tools\Invoke-SmokeTests.ps1` passed; daemon/API and real model branches were both skipped by default. |
| Local API Preview smoke passes | Passed | Explicit `CAICLI_DAEMON_SMOKE=1` packaged smoke passed; real model smoke remained disabled. Post-run checks found 0 `caicli` processes and 0 `caicli-smoke-*` temp directories. |
| Automation platform paths are covered | Passed | Full tests and default packaged smoke cover jobs, queue, pipeline, automation, CI artifacts, corrupt-store redaction, approval/workspace/shell/disabled-tool boundaries, MCP stdio, trace/session/report, and deterministic local failure paths. |
| Release docs describe current and Deferred behavior | Passed | CHANGELOG, installation, configuration, quickstart, security model, known limitations, capability status, troubleshooting, roadmap, weekly reviews, and this file distinguish Accepted, Preview, and Deferred scope. |

## Verification Commands

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
$env:CAICLI_DAEMON_SMOKE = "1"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
Remove-Item Env:CAICLI_DAEMON_SMOKE
artifacts\release\caicli-0.4.0-win-x64\caicli.exe version
Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.4.0-win-x64.zip
```

Default smoke is credential-free and does not start a listener. The daemon/API smoke is an explicit opt-in and does not require model credentials. Real model smoke is not part of the default or daemon/API acceptance and remains gated by `CAICLI_REAL_MODEL_SMOKE=1` plus caller-provided `OPENAI_API_KEY` and `OPENAI_MODEL`.

## Preview Boundary

- Local API/daemon is default-off and requires `daemon start --preview`.
- The listener accepts only `localhost` or `127.0.0.1` input, normalizes to IPv4 `127.0.0.1`, and exposes read-only health/jobs/queue routes.
- It has no authentication, authorization, TLS, same-user isolation, control route, model/tool/MCP execution, artifact-content endpoint, SSE, CORS/browser integration, remote bind, service installation, or auto-start.

## Deferred Items

- Job/artifact retention, rotation, delete, cleanup, compaction, remote/shared stores, and cross-process queue lease/heartbeat recovery.
- Background scheduler, Windows Task Scheduler registration, automatic schedule execution, concurrent workers, pipeline retry/resume, remote runners, and remote control.
- Automatic model role routing, provider assignment, multi-provider pipeline routing, and remote team collaboration.
- GitHub/GitLab/Azure DevOps APIs, checks/status APIs, PR comments, provider annotations, artifact upload, webhook, and callback integration.
- API control routes, SSE, authentication/authorization, TLS, browser/CORS integration, remote bind, reverse proxy, detached service, auto-start, and auto-restart.
- Microsoft Agent Framework real runtime backend, MCP remote/http transport, Gerber/TIFF real toolchain execution, dotnet tool packaging, remote skill marketplace, and interactive approval UI.

## Decision

Release `0.4.0` is accepted on 2026-07-14 as the local engineering automation platform release. The accepted artifact is the self-contained Windows `win-x64` package with deterministic size `44422661` bytes and SHA256 `FC83EF0D05347B59DE1E1454FE45625E3CD6AA789F0A8C2BB3ACA0A1473E61CA`.

The default smoke remains credential-free. Real model smoke was not run and remains an explicit opt-in requiring caller credentials. The read-only localhost Local API/daemon was verified only through its independent explicit opt-in and remains Preview. All Deferred items above remain outside the release boundary.
