# Known Limitations

## Release Scope

- Version `0.4.0` remains the current accepted local Windows release. Version `0.5.0` is a blocked candidate because current-revision exact-tool opt-in smoke evidence is unavailable.
- The primary supported artifact is the `win-x64` self-contained single-file package.
- Dotnet tool packaging is not part of the `0.5.0` release package.
- Project Pack is a deterministic domain-tool flow, not a skill. The model cannot generate or extend real Gerbv/ImageMagick arguments. Only the frozen single-layer authorized fixture/toolchain path has real conversion and strict verification evidence; this is not general CAM/EDA manufacturing correctness.
- Gerbv and ImageMagick are not redistributed. Missing or changed tools fail closed through `packs doctor/plan/run`; approval is invocation-local, and resume/restart revalidate tool/input/output/policy and require fresh approval.
- Release packages require clean `-ReleaseAcceptance` builds. PDBs are excluded by policy; revision/SDK/inventory/checksums prove build provenance and reproducibility, not semantic correctness.

## Model And Agent Behavior

- `chat` uses the direct OpenAI Responses path.
- `exec` is routed through `IAgentRunner` for agentic v1 behavior. It emits agent model/tool/final/error events and enforces loop limits such as `--max-turns`, `--max-tool-calls`, and `--timeout-seconds`.
- `run` remains the deterministic direct-tool compatibility and smoke entry.
- The offline/fake model agent loop and OpenAI response parsing/writeback contracts are implemented and tested.
- The default `exec` path reaches the direct OpenAI Responses SDK tool-call continuation when model and key are configured. Normal unit tests and default smoke tests still use fake/offline contracts or local-only checks; real model smoke is opt-in with `CAICLI_REAL_MODEL_SMOKE=1`.
- Real model smoke is intentionally skipped unless `CAICLI_REAL_MODEL_SMOKE=1`, `OPENAI_API_KEY`, and `OPENAI_MODEL` are all present. A skip in that path does not mean the offline smoke failed.
- `exec` failure feedback retry is finite, not an infinite auto-repair loop. The default retry budget is conservative, `--max-retries 0` disables retry, and budget exhaustion returns a failure summary with remaining risk, command history, and changed files.
- Retry behavior is covered by fake/offline end-to-end tests. Real model repair quality still depends on the configured provider/model and remains opt-in for real-network smoke.
- `chat --resume` and `exec --resume` provide prior transcript context only for existing local sessions. The context is normalized before use to reduce transcript section-spoofing risk.
- `review` is workspace-read-only and does not execute patch or shell tools or write workspace files, logs, transcripts, or patches. Diff collection may use cleaned-up temp files outside the workspace. It sends the current git diff to the configured model and requires configured model credentials for real use.
- `review.gate` and `taskReport` are implemented diagnostic outputs for `exec`; they summarize the final state but do not prove correctness and do not replace human diff review.
- Inline `@file:<path>` and `@folder:<path>` workflow references are available for `exec` and `skills run` through the same agentic execution path. They are bounded local context hints, not new tool permissions. `chat` references, URL references, glob expansion, and semantic retrieval remain Deferred.
- `skills list` and `skills run` support built-in packs and workspace-local JSON manifests under `.caicli/skills`. Pack manifests are data only: validation commands are hints, not directly executed scripts. Remote marketplaces, automatic updates, signed trust chains, YAML manifests, user-level skill directories, team knowledge distribution, and automatic model role routing remain Deferred.
- Local job history is opt-in for execution commands. `exec --record-job` and `skills run --record-job` write redacted job metadata to the user-level store under `%USERPROFILE%\.caicli\jobs`; default `exec` and `skills run` behavior is unchanged. `skills run --record-job --dry-run` is the explicit exception to the otherwise non-persistent dry-run path and writes only user-level compact plan metadata. `jobs list/show/export` is read-only and does not call a model, run shell/patch tools, start MCP, write command logs, or write workspace files.
- Job records are an audit index, not a second task report. They store bounded redacted task/summary metadata, status, timestamps, command family, task report summaries, and artifact pointers only. Raw referenced content, raw tool arguments, raw secrets, and full diffs are not stored in job records.
- Job retention, rotation, delete, cleanup, and workspace relocation commands remain Deferred. Queue cleanup deletes matching terminal queue records only; it does not delete referenced job records or artifacts. Local paths in records/exports should still be treated as sensitive metadata.
- Local task queue v1 is manual and single-process oriented. The read-only API Preview is not a queue worker. The queue has no execution daemon, scheduler, concurrent worker pool, lease/heartbeat recovery, remote runner, or automatic recovery for a process terminated while an item is running.
- Failed queue items can be manually rerun as a new attempt. Cancel is pending-only and does not terminate a running process. Cleanup accepts only succeeded, failed, or canceled records; pending, running, corrupt, and unknown records are preserved.
- Corrupt job/queue files are not repaired or deleted automatically. List/cleanup and read-only API diagnostics continue with valid records and redact secret-like diagnostic metadata, but local paths remain sensitive and manual recovery must verify the active `CAICLI_USER_PROFILE` first.
- Local multi-role pipeline v1 provides only the fixed `fix-review-test`, `review-test`, and `security-review` catalogs. Roles execute sequentially with the same configured provider/model; there is no automatic model role routing, provider assignment, parallel worker, background resume, or remote collaboration.
- Pipeline execution stops after the first failed role. Completed queue attempts, jobs, task-report summaries, and artifact pointers remain available, but pipeline-level retry/resume and a separate persistent pipeline history store are not implemented. The aggregate report is command output, not a correctness or security proof.
- `pipeline list/plan` are credential-free local read paths. `pipeline run` requires the same model configuration as its delegated exec/skill roles; without credentials it returns a stable failed report and linked queue/job evidence.
- Workspace-local automation supports strict JSON manifests under `.caicli/automations`, local list/validate/plan, non-persistent dry-run, and explicit manual trigger. Queue/skill/pipeline targets still require whatever model credentials their delegated path requires.
- Automation `schedule` fields are preview and validation data only. There is no background scheduler, Windows Task Scheduler registration, daemon worker, automatic retry/resume, webhook, remote trigger, or team automation. CI/PR providers, API control routes, SSE, and remote job control remain Deferred.
- Provider-neutral `ci summarize/check` can render only an existing local job record. It does not run a job, aggregate multiple jobs into a new persistent pipeline history, call GitHub/GitLab/Azure DevOps APIs, create PR comments, upload artifacts, or send webhooks/callbacks. Provider annotations and check APIs remain Deferred.
- The local API/daemon is Preview, default-off, unauthenticated, and HTTP-only. It always binds `127.0.0.1`, exposes only health and read-only existing job/queue metadata through bounded renderers, and has no same-user process isolation. Local paths and redacted task metadata remain sensitive. Control routes, SSE, CORS/browser integration, authentication/TLS, IPv6, remote bind, reverse-proxy deployment, service installation, detached mode, and auto-restart are Deferred.
- The Preview rejects Host values other than `localhost` and `127.0.0.1` and bounds request headers/body/connections, but these controls are not authentication and do not protect against a malicious same-user local process.
- CI artifact summaries and pointers are redacted and bounded, but local paths can still reveal repository layout. `--markdown-path` is explicit, workspace-only, and no-overwrite; retention, cleanup, publishing, and remote storage remain caller responsibilities.
- Automation safety declarations must cover the selected target's built-in capabilities, but they do not grant permissions and are not a sandbox. Manual runs still depend on configured approval, workspace guard, dirty-workspace checks, shell policy, disabled tools, MCP startup policy, and expert/skill boundaries.
- `caicli changes` is read-only and local. It summarizes current git/session/taskReport state but does not call a model, prove correctness, generate standalone markdown reports, or keep historical timelines.
- `exec --report markdown` is an audit artifact, not a correctness proof. It is generated from `AgentTaskReport`, records reference metadata only, and does not persist raw `@file`/`@folder` contents.
- `exec --report-path` never overwrites an existing file and has no automatic history store or report rotation.
- `exec --expert` uses built-in local profiles only. Custom expert files and model role routing remain Deferred.
- `reviewer` and `security` experts are read-only by local policy. They still depend on the model's textual output quality and do not prove security or review completeness.
- The Microsoft Agent Framework project is an adapter boundary and experimental stub; the real framework runtime backend is Deferred.

## MCP And Project Packs

- MCP config/list/doctor are available.
- Stdio MCP v1 is available in registry/tool paths for user-configured stdio servers, including the real initialize handshake, MVP tool discovery, and the MVP tool call path.
- Workspace-configured MCP servers are not auto-discovered or started during ordinary registry creation such as `tools list`, `tools call`, `exec`, or `run`.
- `mcp doctor` can explicitly diagnose configured stdio servers, including workspace config, with a real initialize handshake. Disabled servers are not started.
- Remote/http MCP transport remains Deferred.
- The Gerber/TIFF project pack now records deterministic plan/staging/checkpoint, controlled Gerbv/ImageMagick conversion, bounded TIFF verification, managed preview/report evidence, explicit human accept/reject, safe resume/new-attempt restart, managed artifact list/show/verify/export/prune, and run/job/queue/tombstone correlation. Week 64 security/schema/smoke/release-process hardening is implemented in source; the complete workflow remains Preview pending final verification evidence and Week 65 release acceptance.
- Week 58 adds a generic Project Pack v1 manifest/registry contract plus `packs list` and static/approval-gated `packs doctor` source paths. Static doctor does not launch tools. `--probe` runs only fixed pack-owned version arguments after current approval and does not persist approval or trust.
- Week 59 adds explicit-directory discovery and `packs plan gerber-tiff` text/JSON. The v1 input is workspace-local only: no single-file shortcut, ZIP/archive extraction, URL, UNC/network path, glob, device path, or reparse tree. Limits are depth 8, 256 files, 256 directories, 64 MiB per supported file, 512 MiB total, 512 workspace-relative path characters, and 10 seconds for scan plus hash.
- Extension and filename classification is deterministic metadata, not a complete RS-274X/Excellon parser. At least one Gerber layer is required; drill is optional. `.gbrjob` is hash evidence only and is never passed to an external tool. Unknown files are reported without reading/hashing their contents or adding them to tool input/fingerprint.
- A Week 59 plan with `readyForStaging=true` can freeze safe input/output metadata even when tools are missing. `runnable=true` additionally means required static tool identities were supplied; it does not mean conversion ran or was approved. Every plan has `conversionExecuted=false`, `executionAuthorized=false`, and `approvalPersisted=false`.
- `packs run gerber-tiff --plan <file> --dry-run` creates user-level managed run/job evidence and copies only the supported inventory into staging. It never calls the fake driver, Gerbv, ImageMagick, a model, or the network and does not create the explicit workspace output. Omitting `--dry-run` enters the Week 61 controlled conversion path and requires current approved tool probes/invocations.
- Managed run/checkpoint corruption, unknown fields, unsupported schemas, revision/state/fingerprint mismatch, unsafe containment, and reparse paths fail closed. The short mutation lock/revision check prevents a same-run double write, but it is not a concurrent worker lease, heartbeat, crash recovery service, or multi-process scheduler.
- `packs resume` is revalidation/plan output only. `staged`/`ready` requires new approval before execution; `running`/`interrupted` is never auto-replayed. `verifying` can continue only through the bounded verifier, and `awaiting-acceptance` can continue only through explicit human accept/reject after current hard-report revalidation.
- `packs restart --from execute` creates a new child run/attempt/output and reuses current approval-gated typed execution. It does not resume in place, overwrite the original output, delete parent partial evidence, persist approval, repair corrupt state, or automatically decide that a stale process is gone. A reserved child whose creation is interrupted may require manual inspection.
- `packs recover --mark-interrupted` is a local manual bookkeeping action for a confirmed stale running run/job/queue. It does not discover, kill, or prove cleanup of an external process; using it while a process is still active can leave competing output and must be avoided.
- `packs cancel` records a terminal canceled checkpoint. Week 61 controlled external execution has bounded timeout/cancel/tree cleanup, but cancel is not a background process-control API and there is no worker or scheduler.
- The run record is operational checkpoint truth; the correlated job stores artifact pointers and keeps `taskReport` null. Queue id correlation is schema support only: Week 60 does not add a Project Pack queue family, queue worker, scheduler, remote runner, API control route, or SSE path.
- Project Pack manifests and portable reports do not contain user-machine absolute tool paths. Tool executables are not downloaded from a workspace or network, and the Week 58 Gerbv/ImageMagick binaries used for the spike are not stored in the repository or release.
- Gerbv `v2.13.0` can return exit code `0` and create a small PNG even when it reports `Unknown file type` and `loaded 0` on stderr. Exit code, file existence, valid metadata, or a preview image alone must not be treated as successful conversion or business verification.
- Week 62 hard verification supports only classic TIFF with LZW-compressed RGB8/no-alpha frames. BigTIFF, other compression/pixel formats, alpha-bearing output, dimensions over 32,768, more than 32 frames, file/pixel/decoded-memory limits, and decode over 15 seconds fail closed. These are v1 safety/product boundaries, not statements that ImageMagick cannot decode other formats.
- Verification levels remain independent: `file-valid` does not imply `metadata-valid`, and neither implies `content-compared`. With no baseline, content comparison is `not-requested`; a metadata hard pass can only reach `human-review-required`, not Accepted. Exact file hashes are legal only for baseline outputs explicitly proven byte-deterministic.
- Preview/contact sheets are managed, bounded, no-overwrite local PNG evidence. They are not correctness proof, do not replace the TIFF, are not opened/uploaded automatically, and cannot auto-accept a run. Complete CAM/EDA manufacturing correctness and model vision pass/fail remain outside v1.
- Human acceptance records only that a named local reviewer accepted the current hard-verification evidence. It is not complete CAM/EDA manufacturing correctness, does not upgrade `content-compared=not-requested`, and is not a model/tool decision. Reject is also terminal; v1 has no undo, exception policy, or acceptance bypass.
- Artifact manifests and tombstones are a local audit index, not a shared content-addressed store. Prune is synchronous and manual, defaults to dry-run, and can delete only owned managed reports/previews/intermediates/logs for terminal runs. It never deletes source, baseline, explicit workspace TIFF, run/checkpoint/job metadata, outside-root/reparse paths, or running/interrupted/corrupt state. Locked/raced items remain with diagnostics. There is no background retention worker, quota enforcer, remote/shared store, cloud upload, restore command, or multi-user policy.
- `artifactRetention.defaultMinimumAgeDays` is a user-config default only (30 days, allowed 1-3650). It is not a cleanup schedule or authorization. Workspace retention config is ignored. Tombstones retain original local path/hash metadata, which remains sensitive even after content pruning.
- Default tests use protocol-v1 fake driver contracts plus the bundled Magick.NET/ImageMagick managed codec on synthetic fixtures; they do not require installed Gerbv/`magick.exe`, a model, credentials, network, or real manufacturing data. Fake success, file existence, metadata validity, and preview are never real-tool or business acceptance. Real conversion/verification smoke remains explicit opt-in through `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` plus caller-provided reviewed Gerbv/ImageMagick, authorized fixture, and strict baseline paths.
- The mature Magick.NET codec is bounded but remains native in-process attack surface. Decoder time/resource limits cannot terminate a native deadlock as an isolated process could. TIFF verification is serialized because ImageMagick resource settings are process-global; parallel TIFF writer/verifier work remains Deferred.
- Run/checkpoint/artifact-manifest files are individually atomic, not one filesystem transaction. A crash between replacements produces a mismatch that fails closed and requires diagnosis. Restart reservation and prune quarantine/tombstone can likewise leave partial evidence after a crash; automatic repair, restore, and quarantine recovery remain Deferred.
- `Magick.NET-Q8-x64 14.15.0` embeds ImageMagick 7.1.2-27 and transitive codecs for the managed verifier. This increases package size and native-code attack surface. Release builds copy the exact upstream notice as `THIRD-PARTY-NOTICES-MAGICK.NET.txt`; changing the package/version requires renewed notice, vulnerability, format, and resource-limit review.

## Safety Boundaries

- The tool is local and not a sandbox.
- Workspace path checks reduce accidental boundary escapes but do not replace OS permissions or code review.
- Patch editing is single-file exact-text replacement, not a full merge engine.
- Patch writes remain approval-gated and recheck file content before apply, but previews do not make patching risk-free.
- There is no interactive approval UI. Non-interactive `on-request` and `on-failure` modes report approval-required failures for write and shell actions.
- Retry does not bypass approval, workspace guard, dirty-workspace checks, shell policy, or dangerous-command detection. A retry can ask for another patch or shell command only through the same tool safety path.
- `@file` / `@folder` resolution does not bypass workspace guard. Outside paths are rejected, explicit binary files fail, folder binary/unreadable children are skipped with warnings, and large inputs are bounded or truncated before model execution.
- Failure feedback is bounded/truncated before it is sent back through model continuation; long stdout/stderr or payload strings may require reading trace/session output or rerunning commands manually.
- Trace logs are local diagnostic artifacts. Redaction is best-effort and users should still treat traces, command logs, and session exports as sensitive when prompts or diffs contain private code.
- Shell policy and dangerous command detection run before approval/execution, including for MCP stdio startup commands, but users must still inspect commands.
- Dangerous command detection and shell policy are conservative text/pattern checks, not complete shell parsing or semantic proof.
- Shell allowlist entries are command text, not exact argv arrays; unusual quoted arguments with metacharacters may be conservatively blocked.
- Timeout requests above the configured shell maximum are rejected rather than silently clamped.
- Encoded PowerShell switches, supported abbreviations, and aliases are detected where supported, and encoded payloads are redacted in safe diagnostics, but this is not a general-purpose malware detector.

## Configuration And Secrets

- Workspace `apiKey` is ignored. Use `OPENAI_API_KEY` or user config for model calls.
- `baseUrl` can come from `OPENAI_BASE_URL`, user config, or workspace config, but it must be an absolute `http` or `https` URL without user info, query, or fragment components.
- `config get`, `config list`, `doctor`, and logs record key presence and source, not key value.
- Logs include configured model, base URL, and backend source/status for diagnostics.
- Trace logs redact common key, token, password, authorization, `secretKey`, `privateKey`, nested/escaped argument, OpenAI key, and GitHub token formats, but users should still treat local logs as sensitive diagnostic artifacts.
- Release artifacts do not include user config, workspace config, keys, logs, or transcripts.

## Logging And Diagnostics

- Parse-time `System.CommandLine` validation errors can return before trace context creation, so they may not appear in trace logs. No model or tool flow has begun in this path.
- `logs clear` refuses to clear when the resolved log directory or an ancestor is a symlink/reparse point. This is an intentional local-file safety boundary.

## Platform

- Windows is the primary target.
- Shell policy behavior is primarily designed and verified for Windows command execution.
- Tests run on .NET 9 in the current environment.
- The repository SDK is locked by root `global.json` to .NET SDK `9.0.308` with `latestPatch` roll-forward.
