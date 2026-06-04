# 第 6 周 Chat Streaming Renderer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `caicli chat "<prompt>"` 从一次性非流式输出升级为可测试的终端流式输出。

**Architecture:** `CSharpAiCli.Core` 新增 provider-neutral streaming renderer contract，并把 OpenAI Responses SDK streaming event 适配为仓库内部的最小 streaming update。`CSharpAiCli.Cli` 仍只负责命令接线、工作区快照、命令日志、prompt 参数读取、model client 注入、renderer 注入和 exit code。第 6 周不做 session、transcript、instruction loader、工具调用或配置 schema 大调整。

**Tech Stack:** C#、`net9.0`、当前机器 .NET SDK `9.0.308`、`System.CommandLine` `2.0.8`、`OpenAI` NuGet package `2.10.0`、xUnit、Windows PowerShell。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 02 计划：`docs_md/plans/02_model_streaming_sessions.plan.md`
- 第 5 周计划：`docs_md/weekly/05_week_model_client_responses_api.plan.md`
- 第 5 周回顾：`docs_md/weekly/05_week_review.md`
- Week 5 spec：`docs_md/spec/model_client_responses_api.md`
- OpenAI streaming guide：`https://platform.openai.com/docs/guides/streaming-responses`
- OpenAI .NET SDK README：`https://github.com/openai/openai-dotnet`

第 6 周排期目标：

```text
添加用于 chat 输出的终端流式渲染器。
周末验收：caicli chat 可以流式输出响应。
```

第 5 周输入：

```text
添加用于 chat 输出的 terminal streaming renderer。
将 OpenAI Responses call 转为 streaming path。
保持当前缺 key/model 和 redaction 行为。
```

## 本周范围

第 6 周必须完成：

- 添加 `IChatStreamingRenderer` 和 `TerminalChatStreamingRenderer`。
- 终端输出在模型生成时逐段写入 `TextWriter`，而不是等待完整 `ChatResponse`。
- 成功 streaming 输出保留 Week 5 的关键安全上下文：workspace、workspace status、API key 状态和 API key source。
- 成功 streaming 输出包含 `status: streaming`、模型文本、`status: completed`、provider、model 和 responseId。
- 失败输出继续使用安全 `ModelError` 字段，不打印原始 API key。
- 保留当前缺 API key、缺 model、空 prompt、工作区 apiKey 禁用、SDK exception、HTTP status 和 cancellation 行为。
- `OpenAiResponsesModelClient.Send(...)` 继续可用，便于测试和未来非流式调用；`caicli chat` 默认改用 `SendStreaming(...)`。
- 单元测试通过 fake gateway 和 fake renderer 验证 streaming path，不做真实网络调用。
- smoke 验证可在具备真实 `OPENAI_API_KEY` 和配置 model 的环境中运行。

第 6 周不做：

- `chat --session <name>`。
- session 存储、transcript 文件和版本化 transcript schema。
- instruction loader 或 `AICLI.md`。
- 工具调用、工具注册表、文件读取、patch、shell runner。
- Microsoft Agent Framework、MCP 或 backend provider registry。
- 完整配置优先级和 schema 收紧；这些仍归属第 8 周。
- JSON/NDJSON 输出模式。

## 文件结构

第 6 周创建或修改：

```text
src/
  CSharpAiCli.Core/
    Chat/
      IChatModelClient.cs                    # 修改：增加 streaming 方法
      IChatStreamingRenderer.cs              # 新增：provider-neutral streaming renderer contract
      TerminalChatStreamingRenderer.cs       # 新增：终端流式输出实现
    ModelClients/
      OpenAI/
        IOpenAiResponsesGateway.cs           # 修改：增加 streaming gateway 方法
        OpenAiStreamingResponseUpdate.cs     # 新增：SDK streaming event 的内部最小形状
        OpenAiResponsesModelClient.cs        # 修改：增加 SendStreaming path
        SdkOpenAiResponsesGateway.cs         # 修改：调用 ResponsesClient.CreateResponseStreaming
  CSharpAiCli.Cli/
    Commands/
      CliCommandFactory.cs                   # 修改：chat 默认使用 TerminalChatStreamingRenderer
  CSharpAiCli.Tests/
    TerminalChatStreamingRendererTests.cs    # 新增：renderer 输出、完成和失败脱敏测试
    OpenAiResponsesModelClientTests.cs       # 修改：覆盖 streaming gateway、renderer 和错误路径
    CliCommandFactoryTests.cs                # 修改：chat 命令走 streaming path
docs_md/
  spec/
    model_client_responses_api.md            # 修改：记录 Week 6 streaming 行为
  weekly/
    26_week_goal_schedule.md                 # 周末收尾时更新 Week 6 状态
    06_week_review.md                        # 周末收尾时创建
```

## Task 1: 添加终端 streaming renderer

**Files:**

- Create: `src/CSharpAiCli.Core/Chat/IChatStreamingRenderer.cs`
- Create: `src/CSharpAiCli.Core/Chat/TerminalChatStreamingRenderer.cs`
- Create: `src/CSharpAiCli.Tests/TerminalChatStreamingRendererTests.cs`

- [ ] **Step 1: 写失败的 renderer 测试**

Create `src/CSharpAiCli.Tests/TerminalChatStreamingRendererTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class TerminalChatStreamingRendererTests
{
    [Fact]
    public void Start_delta_and_complete_write_streaming_output_without_secret()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(output);

        renderer.Start(snapshot, provider: "openai", model: "gpt-test");
        renderer.WriteDelta("Hel");
        renderer.WriteDelta("lo");
        renderer.Complete(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_stream",
            Text: "Hello"));

        string text = output.ToString();
        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("workspace: workspace-root", text);
        Assert.Contains("workspace status: ready", text);
        Assert.Contains("api key: present", text);
        Assert.Contains("api key source: OPENAI_API_KEY", text);
        Assert.Contains("status: streaming", text);
        Assert.Contains("provider: openai", text);
        Assert.Contains("model: gpt-test", text);
        Assert.Contains("Hello", text);
        Assert.Contains("status: completed", text);
        Assert.Contains("responseId: resp_stream", text);
        Assert.DoesNotContain("sk-stream-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fail_before_start_writes_safe_error_report()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(output);

        renderer.Fail(snapshot, new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-model",
            SafeMessage: "Model is not configured.",
            Retryable: false));

        string text = output.ToString();
        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("status: failed", text);
        Assert.Contains("localErrorCode: missing-model", text);
        Assert.Contains("safeMessage: Model is not configured.", text);
        Assert.DoesNotContain("sk-stream-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fail_after_start_appends_safe_error_without_repeating_header()
    {
        using StringWriter output = new();
        CliEnvironmentSnapshot snapshot = CreateSnapshot(
            apiKey: "sk-stream-secret",
            apiKeySource: "OPENAI_API_KEY");
        TerminalChatStreamingRenderer renderer = new(output);

        renderer.Start(snapshot, provider: "openai", model: "gpt-test");
        renderer.WriteDelta("partial");
        renderer.Fail(snapshot, new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "model-call-canceled",
            SafeMessage: "Model call was canceled before it completed.",
            Retryable: true));

        string text = output.ToString();
        Assert.Equal(1, CountOccurrences(text, "C# AI CLI chat"));
        Assert.Contains("partial", text);
        Assert.Contains("status: failed", text);
        Assert.Contains("localErrorCode: model-call-canceled", text);
        Assert.Contains("retryable: true", text);
        Assert.DoesNotContain("sk-stream-secret", text, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey, string apiKeySource)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: "gpt-test",
            ModelSource: "workspace config",
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
            LoadedConfigPaths: [],
            Warnings: []);

        return new CliEnvironmentSnapshot(
            Workspace: workspace,
            Configuration: configuration,
            DotnetSdkVersion: "9.0.308",
            DotnetRuntime: ".NET 9.0.0",
            TargetFramework: "net9.0",
            HasGlobalJson: false);
    }
}
```

- [ ] **Step 2: 运行 renderer 测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter TerminalChatStreamingRendererTests
```

Expected:

```text
Failed because IChatStreamingRenderer and TerminalChatStreamingRenderer are not defined.
```

- [ ] **Step 3: 添加 renderer contract**

Create `src/CSharpAiCli.Core/Chat/IChatStreamingRenderer.cs`:

```csharp
namespace CSharpAiCli.Core;

public interface IChatStreamingRenderer
{
    void Start(CliEnvironmentSnapshot snapshot, string provider, string model);

    void WriteDelta(string textDelta);

    void Complete(ChatResponse response);

    void Fail(CliEnvironmentSnapshot snapshot, ModelError error);
}
```

- [ ] **Step 4: 添加终端 renderer 实现**

Create `src/CSharpAiCli.Core/Chat/TerminalChatStreamingRenderer.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed class TerminalChatStreamingRenderer : IChatStreamingRenderer
{
    private readonly TextWriter output;
    private bool started;

    public TerminalChatStreamingRenderer(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    public void Start(CliEnvironmentSnapshot snapshot, string provider, string model)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        started = true;

        output.WriteLine($"{ProductInfo.DisplayName} chat");
        output.WriteLine($"workspace: {snapshot.CurrentDirectory}");
        output.WriteLine($"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}");
        output.WriteLine($"api key: {(snapshot.Configuration.HasApiKey ? "present" : "missing")}");
        output.WriteLine($"api key source: {snapshot.Configuration.ApiKeySource}");
        output.WriteLine("status: streaming");
        output.WriteLine($"provider: {provider}");
        output.WriteLine($"model: {model}");
        output.WriteLine();
    }

    public void WriteDelta(string textDelta)
    {
        if (string.IsNullOrEmpty(textDelta))
        {
            return;
        }

        output.Write(textDelta);
    }

    public void Complete(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        output.WriteLine();
        output.WriteLine();
        output.WriteLine("status: completed");
        output.WriteLine($"provider: {response.Provider}");
        output.WriteLine($"model: {response.Model}");
        output.WriteLine($"responseId: {response.ResponseId}");
    }

    public void Fail(CliEnvironmentSnapshot snapshot, ModelError error)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(error);

        if (!started)
        {
            output.WriteLine(ChatModelReport
                .Create(snapshot, ChatModelResult.Failure(error))
                .ToDisplayText());
            return;
        }

        output.WriteLine();
        output.WriteLine();
        WriteErrorLines(error);
    }

    private void WriteErrorLines(ModelError error)
    {
        output.WriteLine("status: failed");
        output.WriteLine($"provider: {error.Provider}");
        output.WriteLine($"operation: {error.Operation}");
        output.WriteLine($"statusCode: {(error.StatusCode.HasValue ? error.StatusCode.Value.ToString() : "none")}");
        output.WriteLine($"localErrorCode: {error.LocalErrorCode ?? "none"}");
        output.WriteLine($"safeMessage: {error.SafeMessage}");
        output.WriteLine($"retryable: {error.Retryable.ToString().ToLowerInvariant()}");
    }

    private static string FormatWorkspaceStatus(WorkspaceStatus status)
    {
        return status switch
        {
            WorkspaceStatus.Ready => "ready",
            WorkspaceStatus.Missing => "missing",
            WorkspaceStatus.NotDirectory => "not directory",
            _ => "unknown"
        };
    }
}
```

- [ ] **Step 5: 运行 renderer 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter TerminalChatStreamingRendererTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 3
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/Chat/IChatStreamingRenderer.cs src/CSharpAiCli.Core/Chat/TerminalChatStreamingRenderer.cs src/CSharpAiCli.Tests/TerminalChatStreamingRendererTests.cs
git commit -m "feat: add chat streaming renderer"
```

Expected:

```text
Commit created with terminal chat streaming renderer.
```

## Task 2: 添加 OpenAI streaming gateway update 形状

**Files:**

- Create: `src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiStreamingResponseUpdate.cs`
- Modify: `src/CSharpAiCli.Core/ModelClients/OpenAI/IOpenAiResponsesGateway.cs`
- Modify: `src/CSharpAiCli.Core/ModelClients/OpenAI/SdkOpenAiResponsesGateway.cs`

- [ ] **Step 1: 添加内部 streaming update 类型**

Create `src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiStreamingResponseUpdate.cs`:

```csharp
namespace CSharpAiCli.Core;

public enum OpenAiStreamingResponseUpdateKind
{
    OutputTextDelta,
    Completed
}

public sealed record OpenAiStreamingResponseUpdate(
    OpenAiStreamingResponseUpdateKind Kind,
    string? TextDelta,
    string? ResponseId,
    string? Model)
{
    public static OpenAiStreamingResponseUpdate OutputTextDelta(string textDelta)
    {
        return new OpenAiStreamingResponseUpdate(
            Kind: OpenAiStreamingResponseUpdateKind.OutputTextDelta,
            TextDelta: textDelta,
            ResponseId: null,
            Model: null);
    }

    public static OpenAiStreamingResponseUpdate Completed(string responseId, string model)
    {
        return new OpenAiStreamingResponseUpdate(
            Kind: OpenAiStreamingResponseUpdateKind.Completed,
            TextDelta: null,
            ResponseId: responseId,
            Model: model);
    }
}
```

- [ ] **Step 2: 扩展 gateway interface**

Replace `src/CSharpAiCli.Core/ModelClients/OpenAI/IOpenAiResponsesGateway.cs` with:

```csharp
namespace CSharpAiCli.Core;

public interface IOpenAiResponsesGateway
{
    OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);

    IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: 更新 SDK gateway 的 streaming path**

Replace `src/CSharpAiCli.Core/ModelClients/OpenAI/SdkOpenAiResponsesGateway.cs` with:

```csharp
using System.ClientModel;
using OpenAI.Responses;

namespace CSharpAiCli.Core;

#pragma warning disable OPENAI001
public sealed class SdkOpenAiResponsesGateway : IOpenAiResponsesGateway
{
    private readonly ResponsesClient client;

    public SdkOpenAiResponsesGateway(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        client = new ResponsesClient(apiKey);
    }

    public OpenAiResponseEnvelope CreateResponse(string model, string prompt, CancellationToken cancellationToken = default)
    {
        ClientResult<ResponseResult> result = client.CreateResponse(
            model,
            prompt,
            cancellationToken: cancellationToken);

        ResponseResult response = result.Value;
        string text = response.GetOutputText();

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? model,
            Text: text);
    }

    public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        CreateResponseOptions options = new()
        {
            Model = model,
            StreamingEnabled = true,
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

        foreach (StreamingResponseUpdate update in client.CreateResponseStreaming(
            options,
            cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (update is StreamingResponseOutputTextDeltaUpdate delta)
            {
                yield return OpenAiStreamingResponseUpdate.OutputTextDelta(delta.Delta);
            }
            else if (update is StreamingResponseCompletedUpdate completed)
            {
                ResponseResult response = completed.Response;
                yield return OpenAiStreamingResponseUpdate.Completed(
                    response.Id ?? "unknown",
                    response.Model ?? model);
            }
        }
    }
}
#pragma warning restore OPENAI001
```

- [ ] **Step 4: 编译确认 SDK streaming API 名称匹配**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

If this step fails on `StreamingResponseCompletedUpdate.Response`, inspect the installed `OpenAI 2.10.0` type and keep `responseId` as `unknown` until a completed event property is verified:

```csharp
else if (update is StreamingResponseCompletedUpdate)
{
    yield return OpenAiStreamingResponseUpdate.Completed(
        responseId: "unknown",
        model: model);
}
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiStreamingResponseUpdate.cs src/CSharpAiCli.Core/ModelClients/OpenAI/IOpenAiResponsesGateway.cs src/CSharpAiCli.Core/ModelClients/OpenAI/SdkOpenAiResponsesGateway.cs
git commit -m "feat: add openai responses streaming gateway"
```

Expected:

```text
Commit created with OpenAI streaming gateway support.
```

## Task 3: 将 model client 扩展为 streaming-first path

**Files:**

- Modify: `src/CSharpAiCli.Core/Chat/IChatModelClient.cs`
- Modify: `src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiResponsesModelClient.cs`
- Modify: `src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs`

- [ ] **Step 1: 扩展 model client contract**

Replace `src/CSharpAiCli.Core/Chat/IChatModelClient.cs` with:

```csharp
namespace CSharpAiCli.Core;

public interface IChatModelClient
{
    ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default);

    ChatModelResult SendStreaming(
        ChatRequest request,
        IChatStreamingRenderer renderer,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: 添加 streaming model client 测试**

Append these tests inside `OpenAiResponsesModelClientTests` before the helper methods:

```csharp
    [Fact]
    public void SendStreaming_writes_deltas_and_returns_completed_response()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.OutputTextDelta("Hel"),
                OpenAiStreamingResponseUpdate.OutputTextDelta("lo"),
                OpenAiStreamingResponseUpdate.Completed("resp_stream", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.Response?.Text);
        Assert.Equal("resp_stream", result.Response?.ResponseId);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(["Hel", "lo"], renderer.Deltas);
        Assert.Equal(1, renderer.CompleteCount);
        Assert.Null(renderer.Error);
        Assert.Equal(1, gateway.StreamingCallCount);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void SendStreaming_validation_error_writes_failure_without_calling_gateway()
    {
        FakeGateway gateway = new();
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: null, apiKeySource: "missing", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-openai-api-key", result.Error?.LocalErrorCode);
        Assert.Equal("missing-openai-api-key", renderer.Error?.LocalErrorCode);
        Assert.Equal(0, renderer.StartCount);
        Assert.Equal(0, gateway.StreamingCallCount);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void SendStreaming_returns_retryable_error_when_stream_has_no_text()
    {
        FakeGateway gateway = new()
        {
            StreamingUpdates =
            [
                OpenAiStreamingResponseUpdate.Completed("resp_empty", "gpt-test")
            ]
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-model-response", result.Error?.LocalErrorCode);
        Assert.True(result.Error?.Retryable);
        Assert.Equal("empty-model-response", renderer.Error?.LocalErrorCode);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
    }

    [Fact]
    public void SendStreaming_maps_gateway_exception_to_safe_error_and_renderer_failure()
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = new InvalidOperationException("raw sdk detail")
        };
        FakeStreamingRenderer renderer = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.SendStreaming(new ChatRequest("hello"), renderer);

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-client-error", result.Error?.LocalErrorCode);
        Assert.Equal("openai-client-error", renderer.Error?.LocalErrorCode);
        Assert.DoesNotContain("raw sdk detail", result.Error?.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, renderer.StartCount);
        Assert.Equal(0, renderer.CompleteCount);
    }
```

In the existing `FakeGateway`, add these members:

```csharp
        public int StreamingCallCount { get; private set; }
        public IReadOnlyList<OpenAiStreamingResponseUpdate> StreamingUpdates { get; init; } =
        [
            OpenAiStreamingResponseUpdate.OutputTextDelta("fake response"),
            OpenAiStreamingResponseUpdate.Completed("resp_fake", "gpt-test")
        ];
```

Then add this method to `FakeGateway`:

```csharp
        public IEnumerable<OpenAiStreamingResponseUpdate> CreateResponseStreaming(
            string model,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            StreamingCallCount++;
            LastModel = model;
            LastPrompt = prompt;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return StreamingUpdates;
        }
```

Add this fake renderer before the final closing brace:

```csharp
    private sealed class FakeStreamingRenderer : IChatStreamingRenderer
    {
        public int StartCount { get; private set; }
        public int CompleteCount { get; private set; }
        public List<string> Deltas { get; } = [];
        public ModelError? Error { get; private set; }

        public void Start(CliEnvironmentSnapshot snapshot, string provider, string model)
        {
            StartCount++;
        }

        public void WriteDelta(string textDelta)
        {
            Deltas.Add(textDelta);
        }

        public void Complete(ChatResponse response)
        {
            CompleteCount++;
        }

        public void Fail(CliEnvironmentSnapshot snapshot, ModelError error)
        {
            Error = error;
        }
    }
```

- [ ] **Step 3: 运行 OpenAI model client 测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter OpenAiResponsesModelClientTests
```

Expected:

```text
Failed because OpenAiResponsesModelClient does not implement SendStreaming yet.
```

- [ ] **Step 4: Replace OpenAI model client implementation**

Replace `src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiResponsesModelClient.cs` with:

```csharp
using System.ClientModel;
using System.Text;

namespace CSharpAiCli.Core;

public sealed class OpenAiResponsesModelClient : IChatModelClient
{
    private const string Provider = "openai";
    private const string Operation = "responses.create";

    private readonly CliEnvironmentSnapshot snapshot;
    private readonly Func<string, IOpenAiResponsesGateway> gatewayFactory;

    public OpenAiResponsesModelClient(
        CliEnvironmentSnapshot snapshot,
        Func<string, IOpenAiResponsesGateway> gatewayFactory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gatewayFactory);

        this.snapshot = snapshot;
        this.gatewayFactory = gatewayFactory;
    }

    public static OpenAiResponsesModelClient Create(CliEnvironmentSnapshot snapshot)
    {
        return new OpenAiResponsesModelClient(snapshot, apiKey => new SdkOpenAiResponsesGateway(apiKey));
    }

    public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ModelError? validationError = ValidateRequest(request, out string prompt, out string model, out SecretValue? apiKey);
        if (validationError is not null)
        {
            return ChatModelResult.Failure(validationError);
        }

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(apiKey.Value);
            OpenAiResponseEnvelope response = gateway.CreateResponse(model, prompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return Failure(
                    statusCode: null,
                    localErrorCode: "empty-model-response",
                    safeMessage: "Model response did not include output text.",
                    retryable: true);
            }

            return ChatModelResult.Success(new ChatResponse(
                Provider: Provider,
                Model: response.Model,
                ResponseId: response.ResponseId,
                Text: response.Text));
        }
        catch (Exception exception)
        {
            return MapException(exception);
        }
    }

    public ChatModelResult SendStreaming(
        ChatRequest request,
        IChatStreamingRenderer renderer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(renderer);

        ModelError? validationError = ValidateRequest(request, out string prompt, out string model, out SecretValue? apiKey);
        if (validationError is not null)
        {
            renderer.Fail(snapshot, validationError);
            return ChatModelResult.Failure(validationError);
        }

        renderer.Start(snapshot, Provider, model);

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(apiKey.Value);
            StringBuilder text = new();
            string responseId = "unknown";
            string responseModel = model;

            foreach (OpenAiStreamingResponseUpdate update in gateway.CreateResponseStreaming(
                model,
                prompt,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (update.Kind == OpenAiStreamingResponseUpdateKind.OutputTextDelta)
                {
                    string delta = update.TextDelta ?? string.Empty;
                    if (delta.Length == 0)
                    {
                        continue;
                    }

                    text.Append(delta);
                    renderer.WriteDelta(delta);
                    continue;
                }

                if (update.Kind == OpenAiStreamingResponseUpdateKind.Completed)
                {
                    responseId = string.IsNullOrWhiteSpace(update.ResponseId) ? responseId : update.ResponseId;
                    responseModel = string.IsNullOrWhiteSpace(update.Model) ? responseModel : update.Model;
                }
            }

            if (text.Length == 0)
            {
                ModelError error = CreateError(
                    statusCode: null,
                    localErrorCode: "empty-model-response",
                    safeMessage: "Model response did not include output text.",
                    retryable: true);
                renderer.Fail(snapshot, error);
                return ChatModelResult.Failure(error);
            }

            ChatResponse response = new(
                Provider: Provider,
                Model: responseModel,
                ResponseId: responseId,
                Text: text.ToString());

            renderer.Complete(response);
            return ChatModelResult.Success(response);
        }
        catch (Exception exception)
        {
            ChatModelResult result = MapException(exception);
            renderer.Fail(snapshot, result.Error!);
            return result;
        }
    }

    private ModelError? ValidateRequest(
        ChatRequest request,
        out string prompt,
        out string model,
        out SecretValue? apiKey)
    {
        prompt = request.Prompt.Trim();
        model = snapshot.Configuration.Model;
        apiKey = snapshot.Configuration.ApiKey;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "empty-prompt",
                safeMessage: "Chat prompt is empty. Pass a prompt as caicli chat \"<prompt>\".",
                retryable: false);
        }

        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "missing-model",
                safeMessage: "Model is not configured. Set model in .caicli/config.json before running chat.",
                retryable: false);
        }

        if (apiKey is null)
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "missing-openai-api-key",
                safeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        if (!IsSupportedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return CreateError(
                statusCode: null,
                localErrorCode: "unsupported-api-key-source",
                safeMessage: "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        return null;
    }

    private static bool IsSupportedApiKeySource(string apiKeySource)
    {
        return apiKeySource is "OPENAI_API_KEY" or "user config";
    }

    private static ChatModelResult Failure(
        int? statusCode,
        string? localErrorCode,
        string safeMessage,
        bool retryable)
    {
        return ChatModelResult.Failure(CreateError(statusCode, localErrorCode, safeMessage, retryable));
    }

    private static ModelError CreateError(
        int? statusCode,
        string? localErrorCode,
        string safeMessage,
        bool retryable)
    {
        return new ModelError(
            Provider: Provider,
            Operation: Operation,
            StatusCode: statusCode,
            LocalErrorCode: localErrorCode,
            SafeMessage: safeMessage,
            Retryable: retryable);
    }

    private static ChatModelResult MapException(Exception exception)
    {
        return exception switch
        {
            ClientResultException clientResultException => Failure(
                statusCode: clientResultException.Status,
                localErrorCode: null,
                safeMessage: SafeHttpMessage(clientResultException.Status),
                retryable: clientResultException.Status is 429 or >= 500),
            OperationCanceledException => Failure(
                statusCode: null,
                localErrorCode: "model-call-canceled",
                safeMessage: "Model call was canceled before it completed.",
                retryable: true),
            _ => Failure(
                statusCode: null,
                localErrorCode: "openai-client-error",
                safeMessage: "OpenAI model call failed before a response was completed.",
                retryable: true)
        };
    }

    private static string SafeHttpMessage(int statusCode)
    {
        return statusCode switch
        {
            401 => "OpenAI rejected the API key. Check OPENAI_API_KEY or user config apiKey.",
            403 => "OpenAI rejected this request for the configured API key.",
            404 => "OpenAI model or endpoint was not found. Check the configured model.",
            429 => "OpenAI rate limit or quota was reached. Try again later.",
            >= 500 => "OpenAI service returned a temporary server error. Try again later.",
            _ => "OpenAI model call failed before a response was completed."
        };
    }
}
```

- [ ] **Step 5: 运行 OpenAI model client 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter OpenAiResponsesModelClientTests
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/Chat/IChatModelClient.cs src/CSharpAiCli.Core/ModelClients/OpenAI/OpenAiResponsesModelClient.cs src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs
git commit -m "feat: stream chat model responses"
```

Expected:

```text
Commit created with streaming model client path.
```

## Task 4: 将 `caicli chat` 接到 streaming renderer

**Files:**

- Modify: `src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [ ] **Step 1: 更新 CLI tests**

In `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`, replace `Chat_command_sends_prompt_to_model_client_and_logs_command` with:

```csharp
    [Fact]
    public void Chat_command_streams_prompt_to_model_client_and_logs_command()
    {
        using StringWriter output = new();
        string? receivedWorkspace = null;
        List<string> loggedCommands = [];
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath =>
                {
                    receivedWorkspace = workspacePath;
                    return CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test");
                },
                (commandName, _) => loggedCommands.Add(commandName),
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "--workspace", "custom-root", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(["chat"], loggedCommands);
        Assert.Equal("hello model", chatClient.LastStreamingPrompt);
        Assert.Null(chatClient.LastNonStreamingPrompt);
        Assert.Contains("status: streaming", output.ToString());
        Assert.Contains("fake model output", output.ToString());
        Assert.Contains("status: completed", output.ToString());
    }
```

Replace `Chat_command_returns_nonzero_for_model_error` with:

```csharp
    [Fact]
    public void Chat_command_returns_nonzero_for_streaming_model_error()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("localErrorCode: missing-openai-api-key", output.ToString());
    }
```

Replace the existing `FakeChatModelClient` class with:

```csharp
    private sealed class FakeChatModelClient(ChatModelResult result) : IChatModelClient
    {
        public string? LastNonStreamingPrompt { get; private set; }
        public string? LastStreamingPrompt { get; private set; }

        public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastNonStreamingPrompt = request.Prompt;
            return result;
        }

        public ChatModelResult SendStreaming(
            ChatRequest request,
            IChatStreamingRenderer renderer,
            CancellationToken cancellationToken = default)
        {
            LastStreamingPrompt = request.Prompt;

            if (result.Response is not null)
            {
                renderer.Start(CreateSnapshot(
                    workspacePath: null,
                    apiKey: "sk-test",
                    apiKeySource: "OPENAI_API_KEY",
                    model: result.Response.Model), result.Response.Provider, result.Response.Model);
                renderer.WriteDelta(result.Response.Text);
                renderer.Complete(result.Response);
                return result;
            }

            renderer.Fail(CreateSnapshot(
                workspacePath: null,
                apiKey: null,
                apiKeySource: "missing",
                model: "gpt-test"), result.Error!);
            return result;
        }
    }
```

- [ ] **Step 2: 运行 CLI tests 确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Failed because CliCommandFactory does not accept a streaming renderer factory and chat still calls Send.
```

- [ ] **Step 3: 更新 CLI command factory overloads**

In `src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs`, replace the overload chain with this shape:

```csharp
    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot),
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(
            output,
            snapshotProvider,
            (_, _) => { },
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            snapshot => OpenAiResponsesModelClient.Create(snapshot),
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            writer => new TerminalChatStreamingRenderer(writer));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory)
```

At the start of the final overload body, add:

```csharp
        ArgumentNullException.ThrowIfNull(streamingRendererFactory);
```

- [ ] **Step 4: Replace the chat command action**

In `src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs`, replace the existing `chatCommand.SetAction` block with:

```csharp
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            IChatStreamingRenderer renderer = streamingRendererFactory(output);
            ChatModelResult result = chatModelClient.SendStreaming(new ChatRequest(prompt), renderer);
            return result.IsSuccess ? 0 : 1;
        });
```

- [ ] **Step 5: 运行 CLI tests**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "feat: stream chat output from cli"
```

Expected:

```text
Commit created with streaming chat CLI wiring.
```

## Task 5: 记录 streaming 行为和 smoke 边界

**Files:**

- Modify: `docs_md/spec/model_client_responses_api.md`

- [ ] **Step 1: 更新 Week 5 spec，加入 Week 6 streaming section**

Append this section to `docs_md/spec/model_client_responses_api.md`:

````markdown

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

真实 streaming smoke：

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
输出先出现 status: streaming。
模型文本在 status: completed 之前出现。
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
输出不包含任何 API key 值。
```
````

- [ ] **Step 2: 扫描说明文档占位内容**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/spec/model_client_responses_api.md
```

Expected:

```text
No matches.
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add docs_md/spec/model_client_responses_api.md
git commit -m "docs: describe chat streaming behavior"
```

Expected:

```text
Commit created with Week 6 streaming notes.
```

## Task 6: Week 6 验证与回顾

**Files:**

- Verify: `src/CSharpAiCli.sln`
- Modify: `docs_md/weekly/26_week_goal_schedule.md`
- Create: `docs_md/weekly/06_week_review.md`

- [ ] **Step 1: 构建 solution**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 2: 运行全部测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 3: 验证缺 key chat 错误**

Run:

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

Expected:

```text
输出包含 status: failed。
输出包含 localErrorCode: missing-openai-api-key。
输出不包含 API key 值。
最后打印的 exit code 是 1。
```

- [ ] **Step 4: 验证真实 streaming smoke**

Only run this step when a real key and model are available in the developer environment.

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."
```

Expected:

```text
输出包含 status: streaming。
模型文本出现在 status: completed 之前。
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
命令 exit code 是 0。
```

- [ ] **Step 5: 验证命令日志仍脱敏**

Run:

```powershell
$today = [System.DateTime]::UtcNow.ToString("yyyy-MM-dd")
$logPath = Join-Path (Resolve-Path ".") ".caicli/logs/$today.log"
Get-Content -LiteralPath $logPath
```

Expected:

```text
日志包含 command=chat。
日志包含 apiKey=present 或 apiKey=missing。
日志不包含任何 sk- 开头的测试密钥值。
```

- [ ] **Step 6: 更新总周计划 Week 6 状态**

In `docs_md/weekly/26_week_goal_schedule.md`, update the current progress list so it includes:

```markdown
- 第 6 周已稳固：chat 终端流式渲染器和 OpenAI Responses streaming path 已完成。详见 `06_week_review.md`。
```

Then update the Week 6 row from:

```markdown
| 6 | 2024-07-08 至 2024-07-14 | 阶段 02 | 计划中 | 添加用于 chat 输出的终端流式渲染器。 | `caicli chat` 可以流式输出响应。 |
```

to:

```markdown
| 6 | 2024-07-08 至 2024-07-14 | 阶段 02 | 已稳固 | 添加用于 chat 输出的终端流式渲染器。详见 `06_week_chat_streaming_renderer.plan.md` 和 `06_week_review.md`。 | 已验证：`caicli chat` 可以流式输出响应，并保持缺 key/model 和脱敏行为。 |
```

- [ ] **Step 7: 创建第 6 周回顾**

Create `docs_md/weekly/06_week_review.md`:

```markdown
# 第 06 周回顾

状态：已稳固

已完成：

- 添加 provider-neutral `IChatStreamingRenderer`。
- 添加 `TerminalChatStreamingRenderer`，支持开始、delta、完成和失败输出。
- 添加 OpenAI Responses streaming gateway update 形状。
- 将 `OpenAiResponsesModelClient` 扩展为 `SendStreaming(...)`。
- 将 `caicli chat "<prompt>"` 默认切换到 streaming path。
- 保持缺 API key、缺 model、空 prompt、工作区 `apiKey` 禁用和 SDK failure 的安全错误行为。
- 保持 chat 输出和命令日志不打印原始 API key。

验证：

- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."`，无 `OPENAI_API_KEY`
- 结果：返回 exit code 1，输出 `status: failed` 和 `localErrorCode: missing-openai-api-key`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."`，有真实 `OPENAI_API_KEY` 和配置 model
- 结果：返回 exit code 0，输出 `status: streaming`、模型文本、`status: completed`、`provider: openai` 和 `responseId:`
- 命令：命令日志脱敏 smoke
- 结果：日志包含 `command=chat` 和 API key 状态，不包含密钥值

运行时说明：

- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- OpenAI SDK package 为 `OpenAI` `2.10.0`。
- 第 6 周只渲染 Responses output text delta。

风险：

- 会话和 transcript 尚未实现，归属第 7 周。
- instruction loader 和完整配置 schema 收紧尚未完成，归属第 8 周。
- reasoning、tool call、annotation、MCP 和 image generation streaming events 当前被忽略。
- 当前输出是文本报告，尚未提供结构化 JSON 输出。

第 07 周输入：

- 添加会话存储。
- 添加版本化 transcript 格式。
- 添加命名会话恢复。
- 为后续工具调用保留 transcript schema 占位字段。
```

- [ ] **Step 8: 扫描文档占位内容**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/spec/model_client_responses_api.md docs_md/weekly/06_week_review.md docs_md/weekly/26_week_goal_schedule.md
```

Expected:

```text
No matches.
```

- [ ] **Step 9: Commit**

Run:

```powershell
git add docs_md/weekly/26_week_goal_schedule.md docs_md/weekly/06_week_review.md
git commit -m "docs: record week six streaming progress"
```

Expected:

```text
Commit created with Week 6 review and schedule update.
```

## 验收标准

第 6 周完成时必须满足：

- `TerminalChatStreamingRendererTests` 通过。
- 更新后的 `OpenAiResponsesModelClientTests` 通过。
- 更新后的 `CliCommandFactoryTests` 通过。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- `dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."` 在缺少 API key 时以 `1` 退出。
- 缺 key 输出包含 `status: failed` 和 `localErrorCode: missing-openai-api-key`。
- 缺 model 输出包含 `localErrorCode: missing-model`。
- 工作区配置 `apiKey` 不用于真实模型调用，输出 `localErrorCode: unsupported-api-key-source`。
- 配置真实 API key 和 model 后，`chat "<prompt>"` 使用 OpenAI Responses streaming path。
- streaming 成功输出包含 `status: streaming`、模型文本、`status: completed`、`provider: openai`、`model:` 和 `responseId:`。
- 模型文本在 `status: completed` 之前输出。
- `chat` 输出、命令日志和对象字符串不打印原始 API key。
- `docs_md/spec/model_client_responses_api.md` 记录 Week 6 streaming 行为和 smoke 边界。
- `docs_md/weekly/06_week_review.md` 在周末记录验证输出和第 7 周输入。

## Self-Review

- Spec coverage：计划覆盖 Week 6 的 terminal streaming renderer、OpenAI Responses streaming path、缺 key/model、redaction、smoke 和 Week 7 输入；session/transcript 被明确排除。
- Placeholder scan：计划不使用占位内容；执行时仍需运行文档扫描命令。
- Type consistency：`IChatModelClient.SendStreaming(...)`、`IChatStreamingRenderer`、`OpenAiStreamingResponseUpdate`、`IOpenAiResponsesGateway.CreateResponseStreaming(...)` 在测试和实现步骤中保持同一签名。
