# C# AI CLI Configuration

## Configuration Sources

The CLI reads configuration from environment variables, user config, and workspace config.

`CAICLI_USER_PROFILE` can override the user profile root for tests, smoke runs, and portable local verification. When unset, the OS user profile is used.

Priority for model:

1. `OPENAI_MODEL`
2. `%USERPROFILE%\.caicli\config.json`
3. `<workspace>\.caicli\config.json`
4. `not configured`

Priority for OpenAI base URL:

1. `OPENAI_BASE_URL`
2. `%USERPROFILE%\.caicli\config.json`
3. `<workspace>\.caicli\config.json`
4. `https://api.openai.com/v1`

`baseUrl` is non-secret configuration. User config is preferred for personal endpoints; workspace config may set `baseUrl` when a repository intentionally shares an OpenAI-compatible endpoint for the team. `baseUrl` must be an absolute `http` or `https` URL and must not contain user info, query, or fragment components.

Priority for API key:

1. `OPENAI_API_KEY`
2. `%USERPROFILE%\.caicli\config.json`

Workspace `apiKey` values are ignored and reported as warnings. This keeps secrets out of source-controlled workspaces.

Priority for agent backend:

1. `CAICLI_AGENT_BACKEND`
2. `%USERPROFILE%\.caicli\config.json`
3. `<workspace>\.caicli\config.json`
4. `direct`

Recognized backend config values:

- `direct` or `openai`
- `framework`, `maf`, or `agent-framework`

The `framework` aliases are currently parsed for the experimental adapter boundary, but the real Microsoft Agent Framework runtime backend is not enabled in the `0.4.0` release. Use `direct` for supported release behavior.

Priority for approval mode:

1. `%USERPROFILE%\.caicli\config.json`
2. `<workspace>\.caicli\config.json`
3. `on-request`

There is no environment variable override for approval mode. Config JSON supports these `approvalMode` values:

- `never`
- `on-request`
- `on-failure`
- `always`

`doctor`, `config get`, and `config list` report the effective approval mode and source. `config set approvalMode` is not implemented; edit user or workspace JSON directly when you need to change approval behavior.

Tool execution can be disabled from user or workspace config:

```json
{
  "disabledTools": [
    "workspace.run_shell",
    "workspace.apply_patch"
  ]
}
```

Disabled tool names are merged from user and workspace config. A disabled tool is omitted from `caicli tools list` and returns `tool-disabled` if invoked.

Agentic `exec` loop budgets can be configured from user or workspace config:

```json
{
  "agentRunLimits": {
    "maxSteps": 8,
    "maxToolCalls": 32,
    "timeoutSeconds": 600
  }
}
```

`maxTurns` is accepted as a compatibility alias for `maxSteps`. If both are present they must match. User config takes priority over workspace config for each budget field. CLI options `--max-steps`, `--max-turns`, `--max-tool-calls`, and `--timeout-seconds` override configured values for one invocation.

Shell execution policy can be constrained from user or workspace config:

```json
{
  "shellPolicy": {
    "allowedCommands": [
      "dotnet test",
      "git status"
    ],
    "deniedCommands": [
      "rm -rf ."
    ],
    "maxTimeoutMilliseconds": 10000
  }
}
```

`deniedCommands` take precedence over `allowedCommands`. An empty configured allowlist denies all shell commands. Prefix allowlist entries permit ordinary arguments but reject shell control and metacharacter syntax after the prefix. The policy applies before approval and before execution for `workspace.run_shell` and MCP stdio startup commands.

## Gerber/TIFF External Tools (0.5.0)

Controlled Gerber/TIFF conversion does not discover, install, download, or update external tools. Both mandatory
executables must be bound explicitly for `packs plan` and `packs run`:

```powershell
$gerbv = "C:\Tools\gerbv\gerbv.exe"
$magick = "C:\Tools\ImageMagick\magick.exe"

caicli packs plan gerber-tiff `
  --input .\samples\board-a `
  --output-dir .\out\board-a `
  --tool-path "gerbv=$gerbv" "imagemagick=$magick" `
  --output json --workspace .
```

Tool bindings are invocation-local absolute paths. Workspace config, Project Pack plans, skills, prompts, models,
input filenames, and environment variables cannot add executable names or flags. Plans persist only tool filename,
size, SHA256, and dependency id; they do not persist the absolute executable path or an approval grant.

`packs run` rechecks the plan, input hashes, output no-overwrite boundary, policy fingerprint, and static tool
identity. A non-dry run also executes each fixed version probe and each external stage through the current approval
policy. Use `--approval always` or legacy `--approve` only as an explicit invocation-local decision. Approval is
never written as a resume/restart bypass.

The release smoke script uses separate environment variables. They are smoke inputs, not ordinary runtime tool
configuration:

```powershell
$env:CAICLI_GERBER_TIFF_TOOL_SMOKE = "1"
$env:CAICLI_GERBV_PATH = "C:\Tools\gerbv\gerbv.exe"
$env:CAICLI_IMAGEMAGICK_PATH = "C:\Tools\ImageMagick\magick.exe"
$env:CAICLI_GERBER_TIFF_FIXTURE = "$PWD\src\CSharpAiCli.Tests\Fixtures\GerberTiff\real\minimal-square.gbr"
$env:CAICLI_GERBER_TIFF_BASELINE = "$PWD\src\CSharpAiCli.Tests\Fixtures\GerberTiff\real\verification-baseline.json"
tools\Invoke-SmokeTests.ps1 -ExecutablePath <validation-caicli.exe>
```

When `CAICLI_GERBER_TIFF_TOOL_SMOKE` is unset, default smoke does not require or start either tool and reports the
real-tool branch as skipped. When it is `1`, all four explicit paths are mandatory and a missing, reparse, renamed,
unreviewed-hash, corrupt-baseline, or wrong-fixture input fails the smoke before trusted evidence is claimed.
`CAICLI_USER_PROFILE` may still redirect managed runs and jobs for isolated validation; it does not select tools.

These variables configure only the repository smoke harness. Ordinary `packs plan/run` continues to accept tools
only through invocation-local `--tool-path`; there is no environment extra-argument, approval, or runtime tool-path
configuration channel.

## Release Build Source Policy

`tools\Build-Release.ps1` requires a clean Git source tree by default and records the 40-character
`sourceRevision`, `sourceDirty=false`, locked SDK version, configuration/runtime, `pdbPolicy=excluded`, and payload
SHA256 inventory in `release-manifest.json`. The adjacent `*.checksums.json` also hashes the full publish inventory
and ZIP without creating a self-referential manifest hash.

`-ReleaseAcceptance` is the Week 65 release mode and cannot be combined with `-AllowDirtySource`.
`-AllowDirtySource` is explicit and only for Week 64/local validation packages; those manifests record
`sourceDirty=true` and `releaseAcceptance=false` and cannot be promoted to Accepted artifacts.

## Managed Artifact Retention (0.5.0 Source Preview)

Artifact lifecycle state is stored under the same user-level managed root as Project Pack runs. The optional
user config value below changes only the default age used when `artifacts prune` omits `--older-than`:

```json
{
  "artifactRetention": {
    "defaultMinimumAgeDays": 30
  }
}
```

The accepted range is 1 through 3650 days and the default is 30. Workspace `artifactRetention` is ignored:
repository content cannot lower the user-level managed-store retention default. This setting does not schedule
cleanup and does not authorize deletion. `artifacts prune` remains dry-run by default; `--apply` is required for
each mutation. Explicit `--older-than`, `--status`, `--min-size`, and `--max-size` filters are invocation-local.

The setting applies only to artifact-manifest entries marked `managed`, `owned`, and `terminal-prunable`.
Source, baseline, explicit workspace output, external pointers, run/checkpoint/job metadata, and tombstones are
not made prunable by configuration. Running, interrupted, corrupt, outside-root, or reparse paths remain
ineligible regardless of age or size.

## User Config Example

```json
{
  "model": "gpt-4.1-mini",
  "baseUrl": "https://api.openai.com/v1",
  "apiKey": "<your-api-key>",
  "agentBackend": "direct",
  "approvalMode": "on-request",
  "agentRunLimits": {
    "maxSteps": 8,
    "maxToolCalls": 32,
    "timeoutSeconds": 600
  }
}
```

## Workspace Config Example

```json
{
  "model": "gpt-4.1-mini",
  "baseUrl": "https://gateway.example.test/v1",
  "agentBackend": "direct",
  "approvalMode": "on-request",
  "disabledTools": [],
  "agentRunLimits": {
    "maxSteps": 8,
    "maxToolCalls": 32,
    "timeoutSeconds": 600
  },
  "mcpServers": {
    "disabled-example": {
      "enabled": false,
      "transport": "stdio",
      "command": "example-mcp-server"
    }
  },
  "workflowProfiles": {
    "cpp": {
      "description": "C++ validation",
      "workspacePath": ".",
      "validationCommand": "dotnet test"
    }
  }
}
```

Do not put `apiKey` in workspace config. If present, it is ignored.
Workspace `mcpServers` are visible to `mcp list` and explicit `mcp doctor` diagnostics, but they are not auto-discovered or started during ordinary registry/tool commands. Put stdio MCP servers in user config when you want them available through `tools list`, `tools call`, `exec`, or `run`.

## Config Commands

```powershell
caicli config get
caicli config list
caicli config set model gpt-4.1-mini
caicli config set baseUrl https://gateway.example.test/v1
caicli config set agentBackend direct
caicli config set apiKey <your-api-key>
caicli config unset baseUrl
```

`config get` and `config list` print the effective non-secret configuration and sources. They include model, base URL, backend, approval mode, disabled tools, config paths, warnings, and API key presence/source, but never the API key value.

`config set` and `config unset` write scalar values in user config only. Supported scalar keys are `model`, `baseUrl`, `agentBackend`, and `apiKey`. Edit results print status, key, scope, and path without echoing the written value.

## Diagnostic Commands

```powershell
caicli doctor
caicli status
caicli models
caicli config get
caicli config list
caicli mcp list
caicli mcp doctor
caicli workflow list
caicli workflow validate cpp
caicli skills list
caicli skills list --output json
caicli skills run review-only --dry-run -- "@file:README.md"
```

`config get`, `config list`, `doctor`, `status`, and command logs report whether an API key is present and where it came from, but do not print the key value. `doctor` also reports model/source, base URL/source, backend/source, approval mode/source, and backend status.

`models` reads local configuration only. It reports the current model, base URL, sources, API key presence, and static configuration examples; it does not call a model list API and does not require an API key.

`skills list` and `skills run --dry-run` are also local-only diagnostic paths. Workspace-local packs are read from `.caicli/skills` JSON manifests; invalid local manifests are reported as diagnostics without hiding built-in packs.
