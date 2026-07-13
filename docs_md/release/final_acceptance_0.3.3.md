# Final Acceptance 0.3.3

## Release

- Version: `0.3.3`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release directory: `artifacts/release/caicli-0.3.3-win-x64`
- Release executable: `artifacts/release/caicli-0.3.3-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.3.3-win-x64/release-manifest.json`
- Release zip: `artifacts/release/caicli-0.3.3-win-x64.zip`
- Release zip size: `32371265` bytes
- Release zip SHA256: `3EA45A7366BD3E7EDC940E1737D786EA0D4DFB6C8ECB963D6FED668F6E903BF1`

## Purpose

`0.3.3` accepts the Week 47-49 Developer Workflow Packs work as the current release line. It promotes bounded workflow references and read-only changes view, markdown task reports and expert profiles, and local skills/workflow packs into supported CLI behavior while preserving the Windows-first, C#/.NET-first, local, auditable release boundary.

## Week 47-49 Status

| Week | Capability | Status | Release Decision |
|---|---|---|---|
| 47 | Workflow inputs and changes view | Accepted | `exec` supports bounded `@file:` / `@folder:` references with workspace guard, metadata, warnings, and task report coverage. `caicli changes` is a read-only text/JSON view. |
| 48 | Reports and expert profiles | Accepted | `exec --report markdown`, explicit `--report-path`, and `--expert bugfix|reviewer|tester|security|refactor` are current behavior. Reviewer and security experts are read-only. |
| 49 | Local skills and .NET workflow packs | Accepted | `skills list` and `skills run` are current behavior. Built-in packs are `test-fix`, `review-only`, `upgrade-package`, and `doc-sync`; workspace-local JSON manifests are supported. |

## Acceptance Checklist

| Requirement | Status | Evidence |
|---|---|---|
| Version metadata is current | Passed | `Directory.Build.props` reports `0.3.3`; packaged `caicli.exe version` prints `caicli 0.3.3`, `target framework: net9.0`, and `release runtime: win-x64`. |
| Release build succeeds | Passed | `dotnet build src\CSharpAiCli.sln -c Release` succeeded with 0 warnings and 0 errors. |
| Release tests pass | Passed | `dotnet test src\CSharpAiCli.sln -c Release --no-build` passed: 1060 passed, 0 failed, 0 skipped. |
| Release package deterministic | Passed | `tools\Build-Release.ps1` ran twice after this acceptance pass. Both runs produced size `32371265` bytes and SHA256 `3EA45A7366BD3E7EDC940E1737D786EA0D4DFB6C8ECB963D6FED668F6E903BF1`. |
| Packaged smoke passes | Passed | `tools\Invoke-SmokeTests.ps1` passed after the final release build; default real model smoke was skipped because `CAICLI_REAL_MODEL_SMOKE` was not set. |
| Workflow references are covered | Passed | Smoke covers `exec` with `@file:note.txt`; tests cover bounded reference parsing, workspace guard, binary/missing/outside paths, JSON/text/trace/session/task report metadata, truncation, and redaction behavior. |
| Changes view is covered | Passed | Smoke covers `changes`, `changes --output json`, and `changes --session` warning behavior without model credentials. |
| Markdown reports are covered | Passed | Smoke covers `exec --report markdown`, stdout report output, explicit `--report-path`, report file creation, and JSON stream compatibility. |
| Expert profiles are covered | Passed | Smoke covers `--expert reviewer` and `--expert security --output json`; tests cover local guidance, report metadata, and read-only tool boundaries. |
| Skills/workflow packs are covered | Passed | Smoke covers `skills list`, `skills list --output json`, `skills run review-only --dry-run`, and dry-run JSON. Manual packaged checks confirmed built-in pack list and read-only dry-run plan output. |
| Release docs describe current and Deferred behavior | Passed | `CHANGELOG.md`, `capability_status.md`, `quickstart.md`, `known_limitations.md`, `security_model.md`, `troubleshooting.md`, runtime diagnostics docs, weekly reviews, and this file document accepted scope and limits. |

## Verification Commands

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
artifacts\release\caicli-0.3.3-win-x64\caicli.exe version
artifacts\release\caicli-0.3.3-win-x64\caicli.exe skills list --output json
artifacts\release\caicli-0.3.3-win-x64\caicli.exe skills run review-only --dry-run -- "@file:docs_md/release/capability_status.md"
Get-FileHash -Algorithm SHA256 artifacts\release\caicli-0.3.3-win-x64.zip
```

## Deferred Items

- Remote skill marketplace, automatic skill updates, signing/trust chain, YAML manifests, user-level skill directories, team knowledge distribution, and remote execution.
- Custom expert files and automatic model role routing.
- Gerber/TIFF real toolchain execution.
- Microsoft Agent Framework real runtime backend.
- MCP remote/http transport.
- Dotnet tool package.
- Interactive approval UI and richer approval workflow.

## Decision

Release `0.3.3` is accepted on 2026-07-13 as the Developer Workflow Packs release. The accepted scope is the local Windows `win-x64` package with credential-free default smoke and opt-in real model smoke. Bounded `exec` references, `changes`, markdown task reports, expert profiles, local skills/workflow packs, fake/offline agent contracts, direct OpenAI SDK agent tool-call continuation, `review.gate`, `taskReport`, and user-configured stdio MCP v1 remain current release behavior. Deferred items remain outside the release boundary.
