# Final Acceptance

Current release acceptance is recorded in:

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
