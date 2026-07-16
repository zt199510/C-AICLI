# C-AICLI Desktop

Week 66 establishes the secure Electron/AppHost foundation. The current UI is a workspace-open spike, not the complete 0.6.0 task experience.

## Prerequisites

- Node.js `22.13.0` or newer in the supported Node 22 line.
- npm `11.7.0` or newer.
- Repository .NET SDK `9.0.308` available at `%USERPROFILE%\.dotnet` or on `PATH`.

## Verify

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build ..\..\src\CSharpAiCli.sln -c Release
npm ci
npm run verify
```

`npm run verify` checks generated contract drift, dependency notices, TypeScript, ESLint, unit/process tests and the production build.

## Run

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

## Package

```powershell
npm run package:dir
powershell -NoProfile -ExecutionPolicy Bypass -File ..\..\tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ..\..\tools\Invoke-DesktopSmoke.ps1 -WindowClose
```

`CAICLI_ELECTRON_ZIP_DIR` may point to a directory containing the exact official Electron archive for offline/reproducible packaging. The archive remains subject to Electron's checksum validation.
