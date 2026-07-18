# Week 74 Review: Terminal, Artifacts and Gerber/TIFF Human Loop

Status: Passed

Review date: 2026-07-18

Starting commit: `173eb701665a90c8d94545fe7758c3a49ad75c1a`

## Conclusion

Week 74 is complete. Desktop now exposes a user-identity terminal, managed artifact review/export operations, and a Gerber/TIFF human decision loop through the reviewed `desktop-v1` boundary. The final protocol and Electron bridge inventory is exactly 39 invoke methods plus 2 events.

The first packaged retry reproduced the Week 73 `ECONNRESET` while Electron packager attempted a download. A second retry used the already present, version-matched `electron-v41.1.0-win32-x64.zip` through the repository's supported `CAICLI_ELECTRON_ZIP_DIR` input. Packaging and packaged E2E then passed, so the carryover blocker is closed without claiming that the network path succeeded.

## Delivered

- Added optional `terminal.user-session`, `artifact.review`, and `gerber.review` capabilities. Unnegotiated methods are omitted from initialization and rejected as method-not-found.
- Added six reviewed terminal methods with one AppHost-owned process session, workspace-bound cwd, allowlisted shell profiles, bounded 64 KiB output, truncation marker, explicit input/resize/cancel/close/get actions, audit-before-kill, and whole-process-tree cleanup.
- Chose the plan's bounded plain terminal panel instead of xterm.js, avoiding a new dependency while preserving explicit lifecycle, copy selection, output/exit/truncation status, and accessible live status.
- Added artifact preview, verify, and export. Preview returns metadata only and always reports `correctnessProof=false`; verify rechecks retained size/SHA-256/path identity; export accepts a Main save-picker destination, rejects overwrite/workspace/reparse/unsafe paths, and never exposes the destination or managed absolute path to Renderer.
- Added Gerber/TIFF review projection and explicit accept/reject methods. Decisions reload the current run revision and reuse `ProjectPackAcceptanceService`, so missing/stale/mutated hard verification, tamper, invalid state, and double decision fail closed.
- Added Renderer terminal controls, artifact actions, Gerber verification summary, correctness disclaimer, disabled reasons, bounded reject reason, and decision status.
- Expanded generated C#/TypeScript contracts, examples, Main request descriptors, runtime wrapper, exact IPC registry, Preload validators, security allowlist, fixtures, and E2E coverage.

## Verification

| Gate | Result |
|---|---|
| `.NET full suite` | Passed: 1384/1384 |
| AppHost Debug/Release build | Passed: 0 warnings, 0 errors |
| `npm run check:contracts` | Passed |
| `npm run check:notices` | Passed |
| `npm run typecheck` | Passed |
| `npm run lint` | Passed |
| `npm test` | Passed: 19 files, 86 tests |
| `npm run build` | Passed, including production security scan |
| `npm run test:e2e:unpacked` | Passed: 1/1; terminal, artifact metadata and Gerber fail-closed review smoke |
| first `npm run package:dir` retry | Blocked: Electron download `ECONNRESET` reproduced after AppHost publish |
| cached `npm run package:dir` retry | Passed with `CAICLI_ELECTRON_ZIP_DIR=%LOCALAPPDATA%\electron\Cache` |
| `npm run test:e2e:packaged` | Passed: 1/1 |
| test-owned orphan check | Passed: no matching Desktop, AppHost, terminal shell, Electron or testhost process remained |

Two full-suite retries encountered unrelated process-fixture timing failures (one ProjectPack doctor cleanup assertion, then four MCP stdio 10-second timeouts). Each failed subset passed immediately in isolation, no owned process remained, and the final clean full run passed 1384/1384. One final packaged E2E run also reached its original 30-second startup/close budget while the ready UI was visible; the packaged timeout was raised to 60 seconds to account for self-contained AppHost antivirus/startup latency, and the clean rerun passed in 19.2 seconds.

## Security and correctness boundaries

- Terminal is a `terminal.user` surface and is not an agent tool, approval route, model context source, raw process handle, or persistent session across AppHost restart.
- Renderer receives no artifact bytes, arbitrary managed absolute path, generic file API, delete/prune capability, or arbitrary export destination input.
- Preview availability, metadata validity, file existence, fake evidence, and dry-run state cannot accept a run. Only the existing current hard-verification gate can make a run decision-eligible.
- Accept/reject is one-way. Existing ProjectPack tests plus the Desktop projection tests cover stale evidence, report mutation, artifact tamper, invalid/fake evidence, and double-decision rejection.

## Deferred / skipped

- Real Gerbv/ImageMagick/LibTIFF/Magick.NET correctness smoke was not enabled because `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` and reviewed real-tool fixture identities were not supplied. This does not block the Desktop control-plane gate, and manufacturing/image correctness remains explicitly unproven.
- Terminal PTY emulation, xterm.js, session persistence, agent terminal use, arbitrary file browsing, artifact delete/prune/replace, accept undo, and remote terminal remain out of scope.
