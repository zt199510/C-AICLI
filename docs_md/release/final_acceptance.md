# Final Acceptance

## Release

- Version: `0.1.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.1.0-win-x64`
- Release executable: `artifacts/release/caicli-0.1.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.1.0-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.1.0-win-x64.zip`
- Release zip size: `32107967` bytes
- Release zip SHA256: `279AE0CE7802E6DC79344B76FC3C36954875FC8D34E1CF385085652E244D9AEA`

## Phase Status

| Phase | Status | Release Decision |
|---|---|---|
| Phase 01 - CLI foundation/workspace | Accepted | Included in MVP. |
| Phase 02 - model streaming/sessions | Accepted | Included in MVP. |
| Phase 03 - tools/safety/file editing | Accepted | Included in MVP. |
| Phase 04 - Microsoft Agent Framework adapter | Deferred | Experimental adapter boundary only; not a direct backend blocker. |
| Phase 05 - MCP/project workflows | Deferred | Config/diagnostic/profile MVP only; real protocol/toolchain execution Deferred. |
| Phase 06 - packaging/release hardening | Accepted | Included in MVP. |

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Release build succeeds | Passed | `tools/Build-Release.ps1`. |
| Release artifact exists | Passed | `caicli.exe`, manifest, zip under `artifacts/release`. |
| `doctor` diagnoses missing config/key | Passed | Week 25 smoke. |
| `chat` reports missing model/key safely | Passed | Week 25 smoke. |
| `run` completes a small workspace task | Passed | Week 25 smoke creates `caicli-smoke.txt`. |
| Logs are inspectable | Passed | `doctor` and `config get` report log directory; command logger tests pass. |
| Sessions can be exported or cleared | Passed | Week 25 smoke covers `session export` and `session clear`. |
| Users can disable tools | Passed | `disabledTools` config and Week 25 smoke. |
| Approval refusal is safe | Passed | Week 25 smoke covers patch denial. |
| Workspace path boundary refusal is safe | Passed | Week 25 smoke and workspace guard tests. |
| Shell timeout is safe | Passed | Week 25 smoke and shell runner tests. |
| Documentation covers install/config/chat/run/tools/MCP/security | Passed | `docs_md/release/*.md`. |
| Framework/MCP/Gerber enhanced limitations are clear | Passed | `capability_status.md` and `known_limitations.md`. |

## Verification Commands

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1
```

## Decision

MVP release `0.1.0` is accepted. The verification commands above passed on 2026-06-07, and the release zip exists with the SHA256 recorded above. The complete enhanced plan remains partially Deferred because real Microsoft Agent Framework backend execution, real MCP protocol execution, and real Gerber/TIFF execution are not enabled in this release.
