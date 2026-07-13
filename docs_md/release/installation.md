# C# AI CLI Installation

## Supported Package

The `0.3.3` release candidate is a Windows `win-x64` self-contained package produced by:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
```

The script writes the executable to:

```text
artifacts/release/caicli-0.3.3-win-x64/caicli.exe
```

The package also includes `release-manifest.json`, which records the version, runtime, target framework, and executable name.

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
artifacts\release\caicli-0.3.3-win-x64\caicli.exe version
artifacts\release\caicli-0.3.3-win-x64\caicli.exe doctor
```

3. Optional: add the release directory to `PATH` for the current PowerShell session:

```powershell
$env:PATH = "$(Resolve-Path artifacts\release\caicli-0.3.3-win-x64);$env:PATH"
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

The release package does not include API keys, user config, workspace config, logs, or transcripts.

For smoke tests or portable verification, set `CAICLI_USER_PROFILE` to redirect user config and sessions to a temporary directory.
