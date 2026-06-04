# Model Client 与 OpenAI Responses API 说明

## 状态

第 5 周添加一次性非流式模型调用；第 6 周已将 `caicli chat` 的默认执行路径切换为 OpenAI Responses streaming path。`OpenAiResponsesModelClient.Send(...)` 继续保留，供测试和后续非流式调用复用。

会话恢复和 transcript 文件仍归属第 7 周范围。

## 源码布局

Week 5-6 收尾时已把 `CSharpAiCli.Cli` 和 `CSharpAiCli.Core` 的物理文件位置按职责整理，项目名和公共 namespace 暂不改变。

```text
src/
  CSharpAiCli.Cli/
    Program.cs
    Commands/
      CliCommandFactory.cs
  CSharpAiCli.Core/
    Chat/
      ChatRequest.cs
      ChatResponse.cs
      ChatModelResult.cs
      ChatModelReport.cs
      ChatUnavailableReport.cs
      IChatStreamingRenderer.cs
      IChatModelClient.cs
      ModelError.cs
      TerminalChatStreamingRenderer.cs
    Configuration/
      CliConfigFile.cs
      ConfigLoader.cs
      ConfigReport.cs
      EffectiveConfiguration.cs
      SecretValue.cs
    Diagnostics/
      CliEnvironmentSnapshot.cs
      CommandLogger.cs
      DoctorReport.cs
      LogPathResolver.cs
    ModelClients/
      OpenAI/
        IOpenAiResponsesGateway.cs
        OpenAiStreamingResponseUpdate.cs
        OpenAiResponseEnvelope.cs
        OpenAiResponsesModelClient.cs
        SdkOpenAiResponsesGateway.cs
    Product/
      ProductInfo.cs
    Workspace/
      WorkspaceContext.cs
      WorkspaceStatus.cs
```

整理原则：

- `CSharpAiCli.Cli` 只保留入口和命令接线。
- `CSharpAiCli.Core/Chat` 承载 provider-neutral chat contract、result 和 report。
- `CSharpAiCli.Core/ModelClients/OpenAI` 承载 OpenAI Responses SDK adapter 和 gateway。
- `Configuration`、`Diagnostics`、`Workspace` 分别承载配置、运行时诊断和工作区上下文。

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
status: streaming
provider: openai
model: <configured model>

<model text appears as streaming deltas>

status: completed
provider: openai
model: <configured model>
responseId: <response id or unknown>
```

缺少 API key 时输出示例：

```text
C# AI CLI chat
workspace: <path>
workspace status: ready
api key: missing
api key source: missing
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

第 5 周引入的非流式 `OpenAI.Responses.ResponsesClient.CreateResponse(string model, string userInputText, ...)` 仍由 `OpenAiResponsesModelClient.Send(...)` 使用。

第 6 周起，默认 `chat` 路径使用 `ResponsesClient.CreateResponseStreaming(CreateResponseOptions, CancellationToken)` 渲染终端流式输出。

## Smoke

当前默认 `chat` smoke 使用 Week 6 streaming path；缺少 key 和真实 streaming smoke 的脚本与预期见下方 `Week 6 streaming 行为`。

## Week 6 streaming 行为

第 6 周将 `chat "<prompt>"` 的默认执行路径切换为 OpenAI Responses streaming path。CLI 仍只发送单条用户输入，不恢复会话，不写 transcript，不加载 instruction 文件。

成功时输出示例：

```text
C# AI CLI chat
workspace: <path>
workspace status: ready
api key: present
api key source: OPENAI_API_KEY
status: streaming
provider: openai
model: <configured model>

<model text appears as streaming deltas>

status: completed
provider: openai
model: <configured model>
responseId: <response id or unknown>
```

失败时输出仍使用安全错误字段：

```text
C# AI CLI chat
workspace: <path>
workspace status: ready
api key: missing
api key source: missing
status: failed
provider: openai
operation: responses.create
statusCode: none
localErrorCode: missing-openai-api-key
safeMessage: OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.
retryable: false
```

SDK path：

```text
ResponsesClient.CreateResponseStreaming(CreateResponseOptions, CancellationToken)
StreamingResponseOutputTextDeltaUpdate.Delta
StreamingResponseCompletedUpdate
```

第 6 周只渲染 output text delta。reasoning、tool call、annotation、MCP 和 image generation streaming events 会被忽略，供后续阶段按工具和 transcript schema 统一处理。

缺少 key smoke：

前置条件：该 smoke 需要没有 `OPENAI_API_KEY`、没有用户配置 `apiKey`，且传入的 workspace 不含 workspace config `apiKey`。建议使用一个临时 workspace，并确认其中没有 `.caicli/config.json` apiKey；不要移动或破坏真实用户配置。若 workspace config 含 `apiKey`，预期安全错误会是 `unsupported-api-key-source`；若用户配置含 `apiKey`，则不会触发 `missing-openai-api-key`，可能进入真实模型调用路径。

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
try {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."
    $exitCode = $LASTEXITCODE
} finally {
    if ($null -eq $oldOpenAiKey) {
        Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    } else {
        $env:OPENAI_API_KEY = $oldOpenAiKey
    }
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

真实 streaming smoke：

前置条件：使用开发者环境中的真实 key；不要把 key 写入仓库文件、workspace config 或文档。

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
try {
    $env:OPENAI_API_KEY = "<real key from developer environment>"
    dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."
    $exitCode = $LASTEXITCODE
} finally {
    if ($null -eq $oldOpenAiKey) {
        Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    } else {
        $env:OPENAI_API_KEY = $oldOpenAiKey
    }
}
$exitCode
```

预期：

```text
命令 exit code 为 0。
输出先出现 status: streaming。
模型文本在 status: completed 之前出现。
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
输出不包含任何 API key 值。
```
