# Final Acceptance 0.5.0

## Release

- Version: `0.5.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Source revision: pending clean acceptance commit
- Release directory: `artifacts/release/caicli-0.5.0-win-x64`
- Release executable: `artifacts/release/caicli-0.5.0-win-x64/caicli.exe`
- Release manifest: `artifacts/release/caicli-0.5.0-win-x64/release-manifest.json`
- Release checksums: `artifacts/release/caicli-0.5.0-win-x64.checksums.json`
- Release zip: `artifacts/release/caicli-0.5.0-win-x64.zip`
- Release zip size/SHA256: pending reproducibility verification

## Purpose

0.5.0 accepts the Week 58-64 Vertical Workflow Runtime and bounded Gerber/TIFF v1 path on top of the 0.4.0 local automation baseline. Project Pack is a deterministic domain-tool workflow, not a skill. Real executable names and arguments are fixed by reviewed adapters and cannot be generated or extended by a model, prompt, workspace file, pipeline, or automation.

## Gate Summary

| Requirement | Status | Evidence |
|---|---|---|
| Week 58-64 implementation closed | Passed | All seven reviews are complete; Week 64 reports no remaining implementation item. |
| Authorized real tool path | Pending current revision | Frozen Gerbv 2.13.0, ImageMagick 7.1.2-27, CC0 fixture, and strict baseline must pass current packaged opt-in smoke. |
| Build and tests | Pending | Release build and full suite evidence will be recorded after the clean acceptance commit. |
| Default packaged smoke | Pending | Must remain model-free, network-free, and real-tool-free. |
| Reproducible package | Pending | Two clean builds from one revision must match inventory, ZIP size/SHA256, manifest revision, and checksums. |
| Packaged diagnostics | Pending | Version, packs list/doctor, artifacts list, and missing-tool behavior must be verified. |
| Cleanup | Pending | No residual caicli/tool process or smoke/run temp directory. |

## Accepted

- Project Pack v1 registry, manifest, dependency diagnosis, deterministic plan, and bounded Gerber/TIFF inventory.
- Approval-gated typed Gerbv/ImageMagick conversion with bounded cwd/environment/output/time/cancel and process-tree cleanup.
- Isolated staging/checkpoint, TIFF metadata/content/baseline verification, managed preview, and explicit human accept/reject.
- Safe resume and new-attempt restart with current tool/input/output/policy revalidation and fresh approval.
- Managed artifact list/show/verify/export/prune with ownership, quarantine, reparse/race, and source/output preservation guards.
- Default fake/local smoke plus independent explicit real-tool smoke.

## Preview

- Local API/daemon remains default-off, IPv4 loopback-only, and read-only.
- Model-assisted visual review, if used separately, is non-deterministic guidance and never a hard acceptance gate.

## Deferred

- ZIP/network inputs, complete Gerber/Excellon/TIFF parsers, general CAM/EDA manufacturing correctness, and generic C++/EDA packs.
- Scheduler, parallel/concurrent writers or workers, remote runner, provider routing, API control/SSE/auth/TLS, team platform, marketplace, and UI.
- Acceptance undo, artifact restore/quarantine recovery, background retention/quota worker, and automatic split-state repair.

## Verification Commands

The final review records exact command results, counts, tool identities, package size/hash, and cleanup evidence. A fake driver, file existence, metadata validity, preview generation, or an earlier revision is never substituted for current real-tool acceptance.

## Decision

Pending all release gates. Any unmet gate keeps 0.5.0 candidate/blocked.
