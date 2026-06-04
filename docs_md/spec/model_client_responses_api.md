# Model Client 与 OpenAI Responses API 说明

## 状态

第 5 周添加一次性非流式模型调用。`chat "<prompt>"` 会把单条用户输入发送到当前配置的模型，并输出一次完整响应。

流式输出、会话恢复和 transcript 文件仍归属第 6-7 周范围。

## CLI 行为

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."
```

成功时输出示例：

```text
C# AI CLI chat
workspace: <path>
workspace status: ready
api key: present
api key source: OPENAI_API_KEY
status: completed
provider: openai
model: <configured model>
responseId: <response id>

<model text>
```

缺少 API key 时输出示例：

```text
C# AI CLI chat
status: failed
provider: openai
operation: responses.create
statusCode: none
localErrorCode: missing-openai-api-key
safeMessage: OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.
retryable: false
```

## 配置

第 5 周继续使用最小配置 schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-user-config-only"
}
```

真实模型调用只使用以下 API key 来源：

- `OPENAI_API_KEY`
- 用户配置文件中的 `apiKey`

工作区配置中的 `apiKey` 不用于真实模型调用。完整配置优先级、schema 清理和工作区密钥策略收束留到第 8 周完成。

## SDK

OpenAI SDK package：

```text
OpenAI 2.10.0
```

第 5 周使用非流式 `OpenAI.Responses.ResponsesClient.CreateResponse(string model, string userInputText, ...)`。

第 6 周会切换到 streaming Responses API，用于终端流式输出。

## Smoke

缺少 key smoke：

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."
$exitCode = $LASTEXITCODE
if ($null -eq $oldOpenAiKey) {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
} else {
    $env:OPENAI_API_KEY = $oldOpenAiKey
}
$exitCode
```

预期：

```text
命令返回非 0，exit code 为 1。
输出包含 status: failed。
输出包含 localErrorCode: missing-openai-api-key。
输出不包含任何 API key 值。
```

真实模型 smoke：

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$env:OPENAI_API_KEY = "<real key from developer environment>"
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."
$exitCode = $LASTEXITCODE
if ($null -eq $oldOpenAiKey) {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
} else {
    $env:OPENAI_API_KEY = $oldOpenAiKey
}
$exitCode
```

预期：

```text
命令 exit code 为 0。
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
输出包含模型文本。
输出不包含任何 API key 值。
```
