# Final Acceptance 0.5.0

## Release

- Version: `0.5.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Source revision: `09378e66463e2830bd432793d863fcfe8f8156bd`
- Release directory: `artifacts/release/caicli-0.5.0-win-x64`
- Release executable: `artifacts/release/caicli-0.5.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.5.0-win-x64/release-manifest.json`
- Release checksums: `artifacts/release/caicli-0.5.0-win-x64.checksums.json`
- Release zip: `artifacts/release/caicli-0.5.0-win-x64.zip`
- Release zip size: `55,966,314` bytes
- Release zip SHA256: `CAFF04CB1CA7B028F6A4D0CB1A357BA4FCBD190B7C2D61679EB883E054C5E3AD`

## Purpose

0.5.0 accepts the Week 58-64 Vertical Workflow Runtime and bounded Gerber/TIFF v1 path on top of the 0.4.0 local automation baseline. Project Pack is a deterministic domain-tool workflow, not a skill. Real executable names and arguments are fixed by reviewed adapters and cannot be generated or extended by a model, prompt, workspace file, pipeline, or automation.

## Gate Summary

| Requirement | Status | Evidence |
|---|---|---|
| Week 58-64 implementation closed | Passed | All seven reviews are complete; Week 64 reports no remaining implementation item. |
| Optional real-tool environment smoke | Not run / Non-blocking | Repository CC0 fixture and strict baseline retain frozen identities, but exact Gerbv/ImageMagick executables were unavailable. This opt-in environment validation is not a 0.5.0 release Gate. |
| Build and tests | Passed with recorded standard-run flake | Release build: 0 warnings/errors. Standard full run: 1259/1261 with two MCP timing/cleanup failures. After build-server cleanup, controlled single-processor full run: 1261/1261. |
| Default packaged smoke | Passed | Model, network, daemon, and real-tool independent opt-ins were disabled/skipped; fake/local smoke passed. |
| Reproducible package | Passed | Two clean `-ReleaseAcceptance` builds from `09378e6...` matched inventory, size, and SHA256. Manifest/checksums report the same revision, `sourceDirty=false`, SDK 9.0.308, and excluded PDB policy. |
| Packaged diagnostics | Passed | `version` reports 0.5.0/net9.0/win-x64; packs list succeeds; missing-tool doctor returns exit 1 with two `pack-tool-path-missing`; empty artifact list succeeds. |
| Cleanup | Passed | `caicli`, `gerbv`, and `magick` process counts and all reviewed smoke/run temp-pattern counts were zero. |

## Accepted

- Project Pack v1 registry, manifest, dependency diagnosis, deterministic plan, and bounded Gerber/TIFF inventory.
- Approval-gated typed Gerbv/ImageMagick conversion with bounded cwd/environment/output/time/cancel and process-tree cleanup.
- Isolated staging/checkpoint, TIFF metadata/content/baseline verification, managed preview, and explicit human accept/reject.
- Safe resume and new-attempt restart with current tool/input/output/policy revalidation and fresh approval.
- Managed artifact list/show/verify/export/prune with ownership, quarantine, reparse/race, and source/output preservation guards.
- Default fake/local smoke plus an independent explicit, optional real-tool environment smoke entry point.

## Preview

- Local API/daemon remains default-off, IPv4 loopback-only, and read-only.
- Model-assisted visual review, if used separately, is non-deterministic guidance and never a hard acceptance gate.

## Deferred

- ZIP/network inputs, complete Gerber/Excellon/TIFF parsers, general CAM/EDA manufacturing correctness, and generic C++/EDA packs.
- Scheduler, parallel/concurrent writers or workers, remote runner, provider routing, API control/SSE/auth/TLS, team platform, marketplace, and UI.
- Acceptance undo, artifact restore/quarantine recovery, background retention/quota worker, and automatic split-state repair.

## Verification Commands

The final review records exact command results, counts, package size/hash, and cleanup evidence. Real-tool smoke remains available as an explicit environment validation when reviewed external executables are configured. Fake driver success, file existence, metadata validity, or preview generation must not be described as proof of real CAM/EDA business correctness.

## Decision

Release `0.5.0` is **Accepted** on 2026-07-15 as the Vertical Workflow Runtime and Gerber/TIFF v1 release. The clean-source package is reproducible, the controlled full test run and default packaged smoke pass, and packaged diagnostics and cleanup checks pass. Current-source Gerbv/ImageMagick opt-in smoke was not run and is explicitly non-blocking; real external-tool and manufacturing correctness remain bounded by the documented user-configured toolchain and verification limitations. No release tag was created as part of this recorded acceptance run.
