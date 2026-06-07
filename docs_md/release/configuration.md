# C# AI CLI Configuration

## Configuration Sources

The CLI reads configuration from environment variables, user config, and workspace config.

`CAICLI_USER_PROFILE` can override the user profile root for tests, smoke runs, and portable local verification. When unset, the OS user profile is used.

Priority for model:

1. `OPENAI_MODEL`
2. `%USERPROFILE%\.caicli\config.json`
3. `<workspace>\.caicli\config.json`
4. `not configured`

Priority for API key:

1. `OPENAI_API_KEY`
2. `%USERPROFILE%\.caicli\config.json`

Workspace `apiKey` values are ignored and reported as warnings. This keeps secrets out of source-controlled workspaces.

Priority for agent backend:

1. `CAICLI_AGENT_BACKEND`
2. `%USERPROFILE%\.caicli\config.json`
3. `<workspace>\.caicli\config.json`
4. `direct`

Accepted backend values:

- `direct` or `openai`
- `framework`, `maf`, or `agent-framework`

The `framework` backend is currently an experimental adapter boundary. The release MVP uses `direct`.

Tool execution can be disabled from user or workspace config:

```json
{
  "disabledTools": [
    "workspace.run_shell",
    "workspace.apply_patch"
  ]
}
```

Disabled tool names are merged from user and workspace config. A disabled tool is omitted from `caicli tools list` and returns `unknown-tool` if invoked.

## User Config Example

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-user-secret",
  "agentBackend": "direct"
}
```

## Workspace Config Example

```json
{
  "model": "gpt-4.1-mini",
  "agentBackend": "direct",
  "disabledTools": [],
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

## Diagnostic Commands

```powershell
caicli doctor
caicli config get
caicli mcp list
caicli mcp doctor
caicli workflow list
caicli workflow validate cpp
```

`config get` and `doctor` report whether a key is present, but do not print the key value.
