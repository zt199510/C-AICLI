# Model Client 与 OpenAI Responses API 说明

## 状态

第 5 周添加一次性非流式模型调用；第 6 周已将 `caicli chat` 的默认执行路径切换为 OpenAI Responses streaming path。`OpenAiResponsesModelClient.Send(...)` 继续保留，供测试和后续非流式调用复用。

第 7 周已添加命名 session transcript 持久化和恢复追加；当前仍不会把历史消息发送给模型。

第 8 周已添加工作区 `AICLI.md` instruction loading，并完成配置优先级、workspace `apiKey` 禁用和阶段 02 验收收口。

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
    Instructions/
      IInstructionLoader.cs
      InstructionLoadResult.cs
      WorkspaceInstructionLoader.cs
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
- `Configuration`、`Diagnostics`、`Instructions`、`Workspace` 分别承载配置、运行时诊断、工作区指令加载和工作区上下文。

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

第 8 周继续使用最小配置 schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-user-config-only"
}
```

模型配置优先级：

1. `OPENAI_MODEL`
2. 用户配置文件中的 `model`
3. 工作区配置文件中的 `model`
4. `not configured`

真实模型调用只使用以下 API key 来源：

- `OPENAI_API_KEY`
- 用户配置文件中的 `apiKey`

工作区配置中的 `apiKey` 不进入 effective configuration；`doctor`、`config get` 和日志只记录安全 warning：

```text
ignored workspace config apiKey: <workspace config path>
```

warning 不包含原始密钥值。

## SDK

OpenAI SDK package：

```text
OpenAI 2.10.0
```

第 8 周起，非流式和流式路径都通过 `CreateResponseOptions` 构造 payload，以便在 user input 之外传入 workspace instructions。

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

前置条件：该 smoke 需要没有 `OPENAI_API_KEY`、没有用户配置 `apiKey`、没有 workspace config `apiKey`，并且需要从用户配置或临时 workspace config 获得已配置的 `model`。建议使用一个已配置 `model` 且不含 `.caicli/config.json` `apiKey` 的临时 workspace；不要为此删除或改动真实用户配置。若缺少 `model`，预期安全错误会是 `missing-model`；若 workspace config 含 `apiKey`，预期安全错误会是 `unsupported-api-key-source`；若用户配置含 `apiKey`，则不会触发 `missing-openai-api-key`，可能进入真实模型调用路径。

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
$smokeWorkspace = "<temporary workspace with configured model and no apiKey>"
try {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
    dotnet run --project src/CSharpAiCli.Cli -- chat --workspace $smokeWorkspace "Reply with OK."
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

## Week 7 session and transcript behavior

- `caicli chat --session <name> "<prompt>"` creates or appends to a named transcript.
- Session transcripts are stored under `<user profile>/.caicli/sessions/<safe-session-name>.transcript.json`.
- Transcript JSON uses `schemaVersion: 1`.
- Successful turns append one `user` message and one `assistant` message.
- Failed turns append one `user` message and one safe error entry.
- `toolCalls` is present as an empty array in Week 7 and reserved for Week 9 tool-call recording.
- Week 7 did not send historical transcript messages back to the model; Week 31 adds explicit `--resume` behavior described below.
- Without `--session`, `chat` keeps the Week 6 streaming behavior and does not create a transcript.
- Transcript files must not contain raw API keys.

## Week 8 instruction and Phase 02 acceptance behavior

- `CliEnvironmentSnapshot` loads `<workspace>/AICLI.md` through `WorkspaceInstructionLoader` when the workspace is ready.
- Missing `AICLI.md` keeps Week 7 behavior.
- Empty `AICLI.md` is ignored.
- `AICLI.md` over 65536 bytes is ignored with an `instruction warning`.
- `chat` passes instruction text through `ChatRequest.Instructions`.
- `OpenAiResponsesModelClient` passes instructions to `IOpenAiResponsesGateway`.
- `SdkOpenAiResponsesGateway` adds instructions as a developer message item before the user message item.
- Session transcript user messages continue to record the raw user prompt only; instructions are not copied into transcript user messages.
- Reports and logs never print instruction content when instruction loading fails.

阶段 02 acceptance status：

```text
Accepted after Week 8.
```

## Week 31 session resume behavior

- `caicli chat --resume <session> "<prompt>"` requires an existing named transcript.
- `caicli exec --resume <session> "<task>"` also requires an existing named transcript before the agent request is created.
- Missing transcripts fail with safe `session-not-found` output and do not create a new session.
- Resume requests send normalized prior transcript context with the current prompt or task, so named sessions can now provide historical context when `--resume` is used.
- The normalized context is structured to prevent transcript-controlled content from spoofing internal context section boundaries.
- `--session` remains the create-or-append recording mode. Use `--resume` when historical context must be included in the model or agent request.
