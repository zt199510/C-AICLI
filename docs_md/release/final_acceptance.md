# Final Acceptance

Week 77 release acceptance for 0.6.0 is complete with a `Blocked` decision. Clean-source, dual-candidate, packaged smoke, performance, package/security, and cleanup evidence passed, but the required Windows Narrator manual Gate was skipped and remains unproven. The authoritative 0.6.0 decision is recorded in `final_acceptance_0.6.0.md`; it is a Preview candidate and must not be presented as an Accepted release. The current accepted decision remains 0.5.0 below.

Current release acceptance is recorded in:

- `docs_md/release/final_acceptance_0.6.0.md` (`Blocked`, not Accepted)
- `docs_md/release/final_acceptance_0.5.0.md`
- `docs_md/release/final_acceptance_0.4.0.md`
- `docs_md/release/final_acceptance_0.3.3.md`
- `docs_md/release/final_acceptance_0.3.0.md`

## Current Release

- Version: `0.5.0`
- Runtime: `win-x64`
- Target framework: `net9.0`
- Release zip: `artifacts/release/caicli-0.5.0-win-x64.zip`
- Release zip size: `55,966,314` bytes
- Release zip SHA256: `CAFF04CB1CA7B028F6A4D0CB1A357BA4FCBD190B7C2D61679EB883E054C5E3AD`

## Decision

Release `0.5.0` is accepted on 2026-07-15 as the Vertical Workflow Runtime and Gerber/TIFF v1 release. The controlled full test run, deterministic package verification, default packaged smoke, version/manifest/checksum diagnostics, and cleanup checks passed. Current-source Gerbv/ImageMagick opt-in smoke was not run and is an optional environment validation rather than a release Gate. See `final_acceptance_0.5.0.md` for the complete decision and bounded Accepted/Preview/Deferred scope.
