# Local API / Daemon Preview

## Status And Scope

The local HTTP API is an explicit Preview. Ordinary CLI commands do not start a listener, worker, scheduler, or background process. Starting the daemon requires the user to run `caicli daemon start --preview`; there is no config value, environment variable, startup task, or implicit command that enables it.

The Preview is a read-only inspection surface for the existing user-level job and queue stores. It does not add a second job/report store, execute queued work, call a model, construct a tool registry, start MCP, run shell or patch tools, write the workspace, or accept approval overrides.

## Threat Model

The Preview assumes a single developer account on a trusted local machine. It does not provide authentication, authorization, TLS, multi-user isolation, or protection from another process already running as the same operating-system user. Local malware, a compromised browser process, or an untrusted local user may be able to connect to a loopback listener and read the bounded metadata it exposes.

The API can disclose local metadata such as workspace paths, job and queue identifiers, status, timestamps, bounded redacted task summaries, warnings, risks, and artifact pointers. These values can still be sensitive even though the underlying stores and renderers exclude raw reference contents, raw tool arguments, raw secret values, and full diffs. Callers must not treat loopback as an authentication boundary.

The daemon is not a sandbox and does not make CLI execution safe for remote control. Cross-user access controls, browser-origin protections, CSRF defenses, bearer tokens, certificates, remote bind, reverse-proxy deployment, service installation, and public-network exposure are outside this Preview.

## Default-Off And Binding Policy

- `daemon start` fails unless `--preview` is supplied explicitly.
- The listener always binds the IPv4 loopback address `127.0.0.1`.
- Accepted bind input is limited to `localhost` and `127.0.0.1`; it is normalized to `127.0.0.1`.
- Wildcard, LAN, public, hostname, IPv6, and `0.0.0.0` bind requests are rejected before server startup.
- The default port is `8787`; valid ports are `1024` through `65535`.
- Server headers are disabled, request bodies are not part of the v1 read-only contract, list sizes are bounded, and concurrent connections are limited.
- Process termination stops the listener. There is no detached mode, service registration, auto-restart, queue lease, or recovery worker.

## Permission Boundary

All Preview routes are read-only. They reuse `JobRecordStore`, `TaskQueueStore`, `JobsJsonRenderer`, and `TaskQueueJsonRenderer`, so API data comes from the same bounded, redacted DTO and rendering path as the CLI. The API does not accept a workspace path per request and does not expose queue run/cancel/cleanup, automation run, pipeline run, CI file writes, shell, patch, MCP, model, session contents, trace contents, report contents, or artifact file contents.

Because v1 has no execution routes, it cannot inject `--approve`, `--approval`, or configuration overrides. Any future control route must re-enter the existing CLI command/service path and preserve approval, workspace guard, dirty-workspace checks, shell policy, dangerous-command detection, disabled tools, expert/skill boundaries, MCP startup policy, trace/session/report flow, job recording, and redaction. Such routes are Deferred rather than implied by this Preview.

## Route Contract V1

`caicli api routes --output text|json` renders the contract without loading workspace configuration or starting a listener. JSON uses `schemaVersion: 1`, `type: "api.routes"`, and the following stable route catalog:

| Method | Route | Operation | Source |
|---|---|---|---|
| `GET` | `/v1/health` | `health.get` | bounded daemon runtime metadata |
| `GET` | `/v1/jobs?limit=50` | `jobs.list` | `JobRecordStore` and `JobsJsonRenderer` |
| `GET` | `/v1/jobs/{jobId}` | `jobs.show` | `JobRecordStore` and `JobsJsonRenderer` |
| `GET` | `/v1/queue?limit=50&status=<status>` | `queue.list` | `TaskQueueStore` and `TaskQueueJsonRenderer` |
| `GET` | `/v1/queue/{queueId}` | `queue.show` | `TaskQueueStore` and `TaskQueueJsonRenderer` |

List `limit` defaults to `50` and cannot exceed `100`. Queue status filtering uses the existing queue status contract. Unknown routes and unsupported methods return JSON errors; no route serves workspace files or artifact contents.

## Preview Risk Decision

The accepted Week 55 implementation is a localhost-only, read-only inspection daemon. SSE and all control routes are Deferred. This keeps the Preview useful for IDE integration experiments while avoiding an unauthenticated execution surface and avoiding a second event, queue, or report truth.
