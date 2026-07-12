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

The `framework` aliases are currently parsed for the experimental adapter boundary, but the real Microsoft Agent Framework runtime backend is not enabled in the `0.3.0` release. Use `direct` for supported release behavior.

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
```

`config get`, `config list`, `doctor`, `status`, and command logs report whether an API key is present and where it came from, but do not print the key value. `doctor` also reports model/source, base URL/source, backend/source, approval mode/source, and backend status.

`models` reads local configuration only. It reports the current model, base URL, sources, API key presence, and static configuration examples; it does not call a model list API and does not require an API key.
