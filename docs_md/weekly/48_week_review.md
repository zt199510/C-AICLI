# 第 48 周 Review - 0.3.2 Reports And Expert Profiles

## Scope Completed

- Added `exec --report none|markdown` with default `none`.
- Added `exec --report-path <path>` for explicit workspace-bounded markdown report files.
- Added built-in `exec --expert bugfix|reviewer|tester|security|refactor` profiles.
- Added markdown task report rendering from `AgentTaskReport` as the single source of report data.
- Added structured report metadata in text, JSON/NDJSON, trace, session task reports, and markdown session export.
- Added expert metadata in prompt context, startup plan events, text, JSON/NDJSON, trace, session task reports, and markdown reports.
- Added read-only tool boundary for `reviewer` and `security`: write, shell, and `mcp.*` tool calls are blocked, non-read tools are not registered, and MCP discovery is skipped.

## Implementation Notes

- Markdown reports are generated from the already-sanitized `AgentTaskReport` object and do not persist raw `@file` / `@folder` referenced content.
- `--output json --report markdown` keeps the output as parseable NDJSON. It emits `report.generated` and `payload.taskReport.report` metadata instead of raw markdown.
- `--report-path` is opt-in only. It resolves through the workspace guard, creates parent directories, rejects directories, rejects existing files, rejects workspace escapes, and uses atomic create-new file semantics so a race cannot overwrite a newly created target.
- Expert profiles are local policy and prompt/report guidance. They do not route to a different model/provider.
- `bugfix`, `tester`, and `refactor` do not expand permissions; approval, disabled tools, workspace guard, shell policy, dangerous-command detection, and retry/timeout limits still apply.

## Verification

Passed:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

Observed:

- Full test suite passed: 1048 tests.
- Release smoke passed with default credential-free path.
- Real model smoke was skipped because `CAICLI_REAL_MODEL_SMOKE=1` was not enabled.
- Build-Release produced the current versioned artifact from `Directory.Build.props`: `caicli-0.3.1-win-x64`.

## Deferred

- Version metadata bump, deterministic package acceptance, final acceptance document, zip size, and SHA256 recording remain release-acceptance work.
- Custom expert files are not implemented.
- Automatic model role routing is not implemented.
- Local skills, pack manifests, and built-in workflow packs remain 0.3.3+ scope.
- Report overwrite, automatic report history, report rotation, and report store are not implemented.

## Risks

- Markdown report quality depends on the completeness of `AgentTaskReport`; it is an audit artifact, not proof of correctness.
- Read-only experts prevent local write/shell/MCP tool execution, but model textual output can still be incomplete or wrong.
- Session markdown export intentionally stores compact report metadata only, not duplicate full markdown report text.
