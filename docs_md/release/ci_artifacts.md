# CI Artifacts

## Scope

`caicli ci summarize` and `caicli ci check` project an existing local `JobRecord` into provider-neutral JSON or markdown. They read `%USERPROFILE%\.caicli\jobs` through the same job store as `jobs show/export`. They do not call a model, construct a tool registry, run shell or patch tools, start MCP, create queue/job state, or contact a CI provider.

The CLI does not call GitHub, GitLab, or Azure DevOps APIs; create PR comments; send webhooks or callbacks; or upload artifacts. Any pipeline YAML around these commands is owned by the caller.

## Commands

```powershell
caicli ci summarize --job <job-id> --output json --workspace .
caicli ci summarize --job <job-id> --output markdown --workspace .
caicli ci summarize --job <job-id> --markdown-path .caicli\reports\ci-summary.md --workspace .
caicli ci check --job <job-id> --workspace .
caicli ci check --job <job-id> --fail-on risks --workspace .
caicli ci check --job <job-id> --fail-on warnings --workspace .
```

`summarize` returns `0` when it successfully emits an artifact, even when the source job outcome is `failure`; consumers should read `check.outcome` and `check.recommendedExitCode`. A missing/corrupt job or an invalid/unsafe source storage boundary returns `2`.

`check` emits the same artifact and applies this deterministic process exit policy:

| Outcome or condition | Exit code |
|---|---:|
| `success` | `0` |
| `warning` with default `--fail-on none` | `0` |
| source job `failure` | `1` |
| `--fail-on risks` with remaining risks | `1` |
| `--fail-on warnings` with a warning outcome, job warnings, or remaining risks | `1` |
| missing/corrupt input, unsupported status, or unsafe redaction boundary | `2` |

## JSON Schema V1

The root object uses `schemaVersion: 1` and `type: "caicli.ci.summary"`. Its stable sections are:

- `job`: id, status, command family, optional name, and timestamps.
- `correlation`: optional queue id, pipeline run id, and automation name/run/target metadata already present in the job record.
- `summary`: bounded message plus changed-file, verification, warning, risk, annotation, and artifact counts.
- `check`: check name, `success|warning|failure|config-error` outcome, recommended exit code, policy, and optional error code.
- `annotations`: bounded provider-neutral `notice|warning|failure` entries. No provider annotation protocol is emitted.
- `artifacts`: existing local artifact pointers and hashes. Artifact contents are not embedded or uploaded.
- `redaction`: fixed declarations that secrets are redacted and raw references, raw tool arguments, and full diffs are not stored.

The schema id is `https://c-aicli.local/schemas/ci-artifact.v1.json`. The implementation contract is available through `CiArtifactJsonSchema.Render()` for tests and embedding code.

## Storage And Redaction

CI projection reuses the bounded, redacted job/task-report facts. It does not include the job task text, raw workflow reference contents, task-report command details, verification command details, raw tool arguments, raw secrets, or a full diff. Summary, warning, risk, correlation, and pointer strings pass through secret redaction again.

JSON and stdout markdown are not persisted by default. `--markdown-path` is an explicit write request: the path must remain inside the selected workspace, parent directories are created only inside that boundary, and existing files are never overwritten. If the source job does not declare the required redaction boundary, the renderer emits `config-error` and omits artifact pointers.

Local artifact paths can still disclose repository layout and should be handled as sensitive pipeline metadata.

## GitHub Actions Manual Wiring

This example assumes an earlier local step has placed a job id in `CAICLI_JOB_ID`. The CLI only writes a workspace-local markdown file; the workflow manually appends it to the runner summary.

```yaml
- name: Generate C-AICLI summary
  shell: pwsh
  env:
    CAICLI_JOB_ID: ${{ steps.caicli_job.outputs.job_id }}
  run: |
    .\caicli.exe ci summarize --job $env:CAICLI_JOB_ID --output json --markdown-path .caicli\reports\ci-summary.md --workspace .
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Get-Content .caicli\reports\ci-summary.md | Add-Content -Path $env:GITHUB_STEP_SUMMARY
    .\caicli.exe ci check --job $env:CAICLI_JOB_ID --fail-on risks --workspace .
    exit $LASTEXITCODE
```

There is no built-in GitHub check, comment, annotation, or artifact-upload integration.

## Azure DevOps Manual Wiring

This example assumes `CaicliJobId` was set by an earlier local step. The generated file remains in the checkout; publishing it is a separate pipeline decision.

```yaml
- pwsh: |
    .\caicli.exe ci summarize --job $env:CAICLI_JOB_ID --output json --markdown-path .caicli\reports\ci-summary.md --workspace .
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Get-Content .caicli\reports\ci-summary.md
    .\caicli.exe ci check --job $env:CAICLI_JOB_ID --fail-on warnings --workspace .
    exit $LASTEXITCODE
  displayName: Generate and check C-AICLI result
  env:
    CAICLI_JOB_ID: $(CaicliJobId)
```

There is no built-in Azure DevOps check, timeline, comment, or artifact-upload integration.

## Smoke Boundary

Default release smoke creates a controlled local failed job without model credentials, generates JSON and markdown CI artifacts, verifies the redaction flags, writes one guarded markdown file, and checks exit codes `1` and `2`. Real model smoke remains opt-in through `CAICLI_REAL_MODEL_SMOKE=1`; no external CI provider smoke is run by default.
