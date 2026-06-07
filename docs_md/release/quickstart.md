# C# AI CLI Quickstart

## 1. Verify The Release

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe version
```

Expected shape:

```text
caicli 0.1.0
target framework: net9.0
release runtime: win-x64
```

## 2. Run Doctor

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe doctor
```

Without a configured key, `doctor` should still succeed and report:

```text
api key: missing
agent backend: direct
```

## 3. Configure Chat

For a one-session environment setup:

```powershell
$env:OPENAI_API_KEY = "<your key>"
$env:OPENAI_MODEL = "gpt-4.1-mini"
```

Or create `%USERPROFILE%\.caicli\config.json`:

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "<your key>",
  "agentBackend": "direct"
}
```

## 4. Send A Chat Prompt

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat "Say hello in one sentence."
```

With no model configured, the command returns non-zero and prints `localErrorCode: missing-model`.
After setting `OPENAI_MODEL` but leaving the key unset, it returns non-zero and prints `localErrorCode: missing-openai-api-key`.

## 5. Use A Session

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe chat --session smoke "Remember this short note."
```

The transcript is saved under `%USERPROFILE%\.caicli\sessions`.

## 6. Inspect Optional Features

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe mcp list
artifacts\release\caicli-0.1.0-win-x64\caicli.exe mcp doctor
artifacts\release\caicli-0.1.0-win-x64\caicli.exe workflow list
```

MCP and Gerber/TIFF workflow execution are enhanced capabilities and are not part of the direct backend MVP.

## 7. Run A Local Smoke Task

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe run --workspace . --approve "create smoke note"
```

This deterministic release-smoke task creates or updates `caicli-smoke.txt` in the workspace through the patch tool. Without `--approve`, the patch is denied.

## 8. Inspect And Call Tools

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools list --workspace .
```

For JSON-heavy tool calls on Windows PowerShell, prefer an argument file:

```powershell
Set-Content -Path args.json -Value '{"path":"README.md"}'
artifacts\release\caicli-0.1.0-win-x64\caicli.exe tools call --workspace . workspace.read_text --arguments-file args.json
```

## 9. Export Or Clear A Session

```powershell
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session export smoke
artifacts\release\caicli-0.1.0-win-x64\caicli.exe session clear smoke
```
