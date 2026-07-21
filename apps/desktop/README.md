# C-AICLI Desktop 0.6.0

The Windows-first Electron/React Desktop is the task-chat and result-review surface for the existing .NET Application/AppHost runtime. The source is feature-frozen at exact `39 invoke + 2 event` for Week 77 release acceptance. It remains a candidate until clean-source dual builds, both packaged smokes, performance, cleanup, package/security and the seven-step Windows Narrator manual Gate pass.

Desktop does not parse CLI output or duplicate CLI safety policy. Renderer has no Node, arbitrary file or arbitrary process authority; write, approval, workspace, terminal, artifact and recovery actions use typed `desktop-v1` RPC and authoritative .NET Application policies.

## Prerequisites

- Node.js `22.13.0` or newer in the supported Node 22 line.
- npm `11.7.0` or newer.
- Repository .NET SDK `9.0.308` available at `%USERPROFILE%\.dotnet` or on `PATH`.
- Windows x64 for packaged acceptance.

## Verify

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build ..\..\src\CSharpAiCli.sln -c Release
npm ci
npm run verify
npm run test:e2e:performance
npm run measure:performance
```

`npm run verify` checks contract/notices drift, accessibility, TypeScript, ESLint, unit/process tests, production security and builds. `measure:performance` writes one schema-v2 JSON with packaged baseline plus five consecutive isolated long-session profiles; every profile must remain at or below 15% renderer idle working-set/private-bytes retention and clean up all owned process/temp state.

## Development Run

Start the Renderer dev server:

```powershell
npm run dev:renderer
```

In another terminal, start Electron after the .NET Release build:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
$env:VITE_DEV_SERVER_URL = "http://127.0.0.1:5173"
npm run build:main
npm start
```

Development and unpacked fake-runtime results are not release evidence.

## Package And Acceptance

```powershell
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
npm run measure:accessibility
npm run measure:smoke
npm run measure:protocol
```

`CAICLI_ELECTRON_ZIP_DIR` may point to a directory containing the exact official Electron archive for offline/reproducible packaging. The archive remains subject to Electron checksum validation.

Release candidates must be built by `tools/Build-DesktopReleaseCandidate.ps1` from a user-confirmed clean revision into distinct roots under `artifacts`. Run `scripts/run-desktop-evidence.mjs --kind smoke --package-root <candidate-payload> --output <candidate-evidence>` for each candidate, then use `tools/Compare-DesktopReleaseCandidates.ps1` to bind both smokes and the manual Narrator result to the identical archive. Do not use `-SkipBuild`, combine candidate payloads, edit a smoked candidate, or infer real-model/real-MCP/real-Gerber/TIFF correctness from the credential-free fake runtime. Complete `docs_md/release/narrator_acceptance_0.6.0.md` manually on the final package before any Accepted decision.
