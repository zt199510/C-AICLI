# C# AI CLI Installation

## Accepted Package And Preview Candidate

The source tree targets CLI/Desktop `0.6.0`, but Week 77 release acceptance completed with a `Blocked` decision because the required Windows Narrator manual Gate was skipped. Version `0.5.0` remains the last Accepted CLI package; 0.6.0 Windows `win-x64` artifacts are Preview candidates only. Reproducible CLI candidate packaging from a recorded clean source revision uses:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -ReleaseAcceptance
```

The script writes the executable to:

```text
artifacts/release/caicli-0.6.0-win-x64/caicli.exe
```

The package also includes `release-manifest.json`, which records version, runtime, framework, clean source revision, locked SDK, PDB policy, acceptance mode, and payload inventory. The adjacent checksums JSON records publish and ZIP SHA256 values. Gerbv and ImageMagick are not bundled; configure reviewed local executable paths before using the Gerber/TIFF pack.

The Desktop candidate is produced separately from `apps/desktop` with `npm run package:dir` and the clean-source RC orchestrator. It is Windows x64 only, bundles the source-bound AppHost, and must not be treated as an Accepted release. Reopening release acceptance and running the Narrator checklist require separate user authorization and new source-bound evidence.

## Prerequisites

- Windows x64.
- PowerShell for running the build and smoke scripts.
- .NET SDK 9.0.x only if building from source or running tests.
- An OpenAI API key only for real model calls such as `chat`, `exec`, and `review`. `doctor`, `config get`, `version`, MCP diagnostics, workflow diagnostics, and default smoke tests can run without a key.

## Install From Local Release Artifacts

1. Build the release package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

2. Run the executable directly:

```powershell
artifacts\release\caicli-0.4.0-win-x64\caicli.exe version
artifacts\release\caicli-0.4.0-win-x64\caicli.exe doctor
```

3. Optional: add the release directory to `PATH` for the current PowerShell session:

```powershell
$env:PATH = "$(Resolve-Path artifacts\release\caicli-0.4.0-win-x64);$env:PATH"
caicli version
caicli doctor
```

## Build From Source

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release
```

For development runs:

```powershell
dotnet run --project src\CSharpAiCli.Cli -- version
dotnet run --project src\CSharpAiCli.Cli -- doctor
```

## Local Data Paths

- User config: `%USERPROFILE%\.caicli\config.json`
- Workspace config: `<workspace>\.caicli\config.json`
- Workspace logs: `<workspace>\.caicli\logs`
- User sessions: `%USERPROFILE%\.caicli\sessions`
- User job history: `%USERPROFILE%\.caicli\jobs`
- User task queue: `%USERPROFILE%\.caicli\queue`

The release package does not include API keys, user config, workspace config, logs, or transcripts.

For smoke tests or portable verification, set `CAICLI_USER_PROFILE` to redirect user config and sessions to a temporary directory.
