# 第 5 周 Model Client Responses API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **历史状态：** 本计划作为历史实施记录保留；checkbox 状态已在 2026-06-08 验收固化时闭合，最终证据见 `05_week_review.md`。

**Goal:** 添加 provider-neutral model client 抽象，并把 `caicli chat "<prompt>"` 升级为一次性非流式 OpenAI Responses API 调用。

**Architecture:** `CSharpAiCli.Core` 承载 chat request/result/error/report、OpenAI Responses SDK 适配器和可测试的网关接口。`CSharpAiCli.Cli` 只负责命令接线、工作区快照、命令日志、prompt 参数读取、模型客户端注入和 exit code。第 5 周不做终端流式渲染、会话恢复、转录文件、工具调用或 Microsoft Agent Framework。

**Tech Stack:** C#、`net9.0`、当前机器 .NET SDK `9.0.308`、`System.CommandLine` `2.0.8`、`OpenAI` NuGet package `2.10.0`、xUnit、Windows PowerShell。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 02 计划：`docs_md/plans/02_model_streaming_sessions.plan.md`
- 第 4 周回顾：`docs_md/weekly/04_week_review.md`
- OpenAI Responses API reference：`https://platform.openai.com/docs/api-reference/responses`
- OpenAI .NET SDK API surface：`https://github.com/openai/openai-dotnet`

第 5 周排期目标：

```text
添加 model client 抽象和 OpenAI SDK Responses API 实现。
周末验收：smoke 命令可以用已配置凭据调用模型；缺 key 错误清晰。
```

用户确认的 Week 5 设计：

```text
把 caicli chat "<prompt>" 从 Phase 01 边界提示升级为一次性非流式模型调用。
流式输出留到 Week 6。
```

## 本周范围

第 5 周必须完成：

- 添加 provider-neutral `IChatModelClient`。
- 添加可测试的 chat request、response、result 和 model error 数据结构。
- 添加 chat 输出报告，成功时打印模型响应文本，失败时打印安全错误。
- 添加 OpenAI Responses API SDK 适配器，默认使用 `ResponsesClient.CreateResponse(string model, string userInputText, ...)`。
- `caicli chat "<prompt>"` 使用当前配置里的 model 和 API key 调用模型。
- 缺少 API key 时给出清晰错误，返回非 0 exit code，不创建误导性的成功输出。
- 缺少 model 时给出清晰错误，返回非 0 exit code。
- 工作区配置中的 `apiKey` 不用于真实模型调用；真实调用只接受 `OPENAI_API_KEY` 或用户配置来源，工作区密钥禁用的完整 schema 调整留到 Week 8。
- 单元测试不做真实网络调用，OpenAI SDK 调用通过 `IOpenAiResponsesGateway` fake。
- smoke 验证可以用真实 `OPENAI_API_KEY` 和配置 model 运行。

第 5 周不做：

- 流式输出和流式事件渲染。
- `chat --session <name>`。
- 会话存储、转录文件和版本化 transcript schema。
- instruction loader 或 `AICLI.md`。
- 工具调用、工具注册表、文件读取、patch、shell runner。
- Microsoft Agent Framework、MCP 或 backend provider registry。
- 重新排序全部配置优先级；本周只限制真实模型调用的密钥来源。

## 文件结构

第 5 周创建或修改：

```text
src/
  CSharpAiCli.Core/
    ChatRequest.cs                    # 新增：一次性 chat 请求
    ChatResponse.cs                   # 新增：成功响应摘要
    ChatModelResult.cs                # 新增：成功/失败 union
    ModelError.cs                     # 新增：provider-neutral 模型错误
    ChatModelReport.cs                # 新增：chat 输出格式
    IChatModelClient.cs               # 新增：模型客户端抽象
    OpenAiResponseEnvelope.cs         # 新增：SDK 网关返回的最小响应形状
    IOpenAiResponsesGateway.cs        # 新增：OpenAI SDK 网关接口
    SdkOpenAiResponsesGateway.cs      # 新增：ResponsesClient SDK 包装
    OpenAiResponsesModelClient.cs     # 新增：OpenAI Responses API model client
    CSharpAiCli.Core.csproj           # 修改：引用 OpenAI 2.10.0
  CSharpAiCli.Cli/
    CliCommandFactory.cs              # 修改：chat prompt 参数和 model client 注入
  CSharpAiCli.Tests/
    ChatModelResultTests.cs           # 新增：result 工厂和错误格式测试
    ChatModelReportTests.cs           # 新增：输出报告和脱敏测试
    OpenAiResponsesModelClientTests.cs # 新增：验证、fake gateway、错误路径测试
    CliCommandFactoryTests.cs         # 修改：chat 从边界提示变为一次性模型调用
docs_md/
  spec/
    model_client_responses_api.md     # 新增：Week 5 运行说明和 smoke 边界
  weekly/
    05_week_review.md                 # 周末收尾时创建
```

## Task 1: 添加 chat model 领域类型和输出报告

**Files:**

- Create: `src/CSharpAiCli.Core/ChatRequest.cs`
- Create: `src/CSharpAiCli.Core/ChatResponse.cs`
- Create: `src/CSharpAiCli.Core/ModelError.cs`
- Create: `src/CSharpAiCli.Core/ChatModelResult.cs`
- Create: `src/CSharpAiCli.Core/ChatModelReport.cs`
- Create: `src/CSharpAiCli.Tests/ChatModelResultTests.cs`
- Create: `src/CSharpAiCli.Tests/ChatModelReportTests.cs`

- [x] **Step 1: 写失败的 result 和 report 测试**

Create `src/CSharpAiCli.Tests/ChatModelResultTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatModelResultTests
{
    [Fact]
    public void Success_marks_result_successful_and_exposes_response()
    {
        ChatResponse response = new(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "hello");

        ChatModelResult result = ChatModelResult.Success(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Response);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_marks_result_failed_and_exposes_safe_error()
    {
        ModelError error = new(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing.",
            Retryable: false);

        ChatModelResult result = ChatModelResult.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Response);
        Assert.Equal(error, result.Error);
    }
}
```

Create `src/CSharpAiCli.Tests/ChatModelReportTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ChatModelReportTests
{
    [Fact]
    public void Create_formats_successful_chat_without_printing_api_key()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: "sk-secret", apiKeySource: "OPENAI_API_KEY");
        ChatModelResult result = ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "model says hello"));

        string text = ChatModelReport.Create(snapshot, result).ToDisplayText();

        Assert.Contains("C# AI CLI chat", text);
        Assert.Contains("status: completed", text);
        Assert.Contains("provider: openai", text);
        Assert.Contains("model: gpt-test", text);
        Assert.Contains("responseId: resp_123", text);
        Assert.Contains("model says hello", text);
        Assert.Contains("api key: present", text);
        Assert.DoesNotContain("sk-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_formats_failed_chat_with_safe_error()
    {
        CliEnvironmentSnapshot snapshot = CreateSnapshot(apiKey: null, apiKeySource: "missing");
        ChatModelResult result = ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false));

        string text = ChatModelReport.Create(snapshot, result).ToDisplayText();

        Assert.Contains("status: failed", text);
        Assert.Contains("provider: openai", text);
        Assert.Contains("operation: responses.create", text);
        Assert.Contains("localErrorCode: missing-openai-api-key", text);
        Assert.Contains("safeMessage: OpenAI API key is missing.", text);
        Assert.Contains("retryable: false", text);
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

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "ChatModelResultTests|ChatModelReportTests"
```

Expected:

```text
Failed because ChatModelResult and ChatModelReport are not defined.
```

- [x] **Step 3: 添加领域类型**

Create `src/CSharpAiCli.Core/ChatRequest.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatRequest(string Prompt)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt);
}
```

Create `src/CSharpAiCli.Core/ChatResponse.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatResponse(
    string Provider,
    string Model,
    string ResponseId,
    string Text);
```

Create `src/CSharpAiCli.Core/ModelError.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ModelError(
    string Provider,
    string Operation,
    int? StatusCode,
    string? LocalErrorCode,
    string SafeMessage,
    bool Retryable);
```

Create `src/CSharpAiCli.Core/ChatModelResult.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatModelResult(ChatResponse? Response, ModelError? Error)
{
    public bool IsSuccess => Response is not null;

    public static ChatModelResult Success(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new ChatModelResult(response, Error: null);
    }

    public static ChatModelResult Failure(ModelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ChatModelResult(Response: null, Error: error);
    }
}
```

- [x] **Step 4: 添加 chat 输出报告**

Create `src/CSharpAiCli.Core/ChatModelReport.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatModelReport(IReadOnlyList<string> Lines)
{
    public static ChatModelReport Create(CliEnvironmentSnapshot snapshot, ChatModelResult result)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);

        List<string> lines =
        [
            $"{ProductInfo.DisplayName} chat",
            $"workspace: {snapshot.CurrentDirectory}",
            $"workspace status: {FormatWorkspaceStatus(snapshot.WorkspaceStatus)}",
            $"api key: {(snapshot.Configuration.HasApiKey ? "present" : "missing")}",
            $"api key source: {snapshot.Configuration.ApiKeySource}"
        ];

        if (result.Response is not null)
        {
            lines.Add("status: completed");
            lines.Add($"provider: {result.Response.Provider}");
            lines.Add($"model: {result.Response.Model}");
            lines.Add($"responseId: {result.Response.ResponseId}");
            lines.Add(string.Empty);
            lines.Add(result.Response.Text);
            return new ChatModelReport(lines);
        }

        ModelError error = result.Error ?? new ModelError(
            Provider: "unknown",
            Operation: "unknown",
            StatusCode: null,
            LocalErrorCode: "missing-error",
            SafeMessage: "Model call failed without a detailed error.",
            Retryable: false);

        lines.Add("status: failed");
        lines.Add($"provider: {error.Provider}");
        lines.Add($"operation: {error.Operation}");
        lines.Add($"statusCode: {(error.StatusCode.HasValue ? error.StatusCode.Value.ToString() : "none")}");
        lines.Add($"localErrorCode: {error.LocalErrorCode ?? "none"}");
        lines.Add($"safeMessage: {error.SafeMessage}");
        lines.Add($"retryable: {error.Retryable.ToString().ToLowerInvariant()}");

        return new ChatModelReport(lines);
    }

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine, Lines);
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

- [x] **Step 5: 运行领域类型和报告测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "ChatModelResultTests|ChatModelReportTests"
```

Expected:

```text
Passed!  - Failed: 0, Passed: 4
```

- [x] **Step 6: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/ChatRequest.cs src/CSharpAiCli.Core/ChatResponse.cs src/CSharpAiCli.Core/ModelError.cs src/CSharpAiCli.Core/ChatModelResult.cs src/CSharpAiCli.Core/ChatModelReport.cs src/CSharpAiCli.Tests/ChatModelResultTests.cs src/CSharpAiCli.Tests/ChatModelReportTests.cs
git commit -m "feat: add chat model result reporting"
```

Expected:

```text
Commit created with chat model result reporting.
```

## Task 2: 添加 OpenAI Responses model client

**Files:**

- Modify: `src/CSharpAiCli.Core/CSharpAiCli.Core.csproj`
- Create: `src/CSharpAiCli.Core/IChatModelClient.cs`
- Create: `src/CSharpAiCli.Core/OpenAiResponseEnvelope.cs`
- Create: `src/CSharpAiCli.Core/IOpenAiResponsesGateway.cs`
- Create: `src/CSharpAiCli.Core/SdkOpenAiResponsesGateway.cs`
- Create: `src/CSharpAiCli.Core/OpenAiResponsesModelClient.cs`
- Create: `src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs`

- [x] **Step 1: 写失败的 OpenAI model client 测试**

Create `src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class OpenAiResponsesModelClientTests
{
    [Fact]
    public void Send_returns_missing_key_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: null, apiKeySource: "missing", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-openai-api-key", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_rejects_workspace_config_api_key_for_real_model_calls()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-workspace-secret", apiKeySource: "workspace config", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("unsupported-api-key-source", result.Error?.LocalErrorCode);
        Assert.Contains("OPENAI_API_KEY", result.Error?.SafeMessage);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_returns_missing_model_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "not configured"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("missing-model", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_returns_empty_prompt_error_without_calling_gateway()
    {
        FakeGateway gateway = new();
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("   "));

        Assert.False(result.IsSuccess);
        Assert.Equal("empty-prompt", result.Error?.LocalErrorCode);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Send_calls_gateway_and_returns_response_text()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_123",
                Model: "gpt-test",
                Text: "hello from model")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Response?.Provider);
        Assert.Equal("gpt-test", result.Response?.Model);
        Assert.Equal("resp_123", result.Response?.ResponseId);
        Assert.Equal("hello from model", result.Response?.Text);
        Assert.Equal(1, gateway.CallCount);
        Assert.Equal("gpt-test", gateway.LastModel);
        Assert.Equal("hello", gateway.LastPrompt);
    }

    [Fact]
    public void Send_maps_gateway_exception_to_safe_local_error()
    {
        FakeGateway gateway = new()
        {
            ExceptionToThrow = new InvalidOperationException("raw sdk detail")
        };

        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello"));

        Assert.False(result.IsSuccess);
        Assert.Equal("openai-client-error", result.Error?.LocalErrorCode);
        Assert.DoesNotContain("raw sdk detail", result.Error?.SafeMessage, StringComparison.Ordinal);
    }

    private static CliEnvironmentSnapshot CreateSnapshot(string? apiKey, string apiKeySource, string model)
    {
        WorkspaceContext workspace = new(
            RootPath: "workspace-root",
            ConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Status: WorkspaceStatus.Ready);

        EffectiveConfiguration configuration = new(
            WorkspaceRoot: "workspace-root",
            UserConfigPath: Path.Combine("user-home", ".caicli", "config.json"),
            WorkspaceConfigPath: Path.Combine("workspace-root", ".caicli", "config.json"),
            Model: model,
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

    private sealed class FakeGateway : IOpenAiResponsesGateway
    {
        public int CallCount { get; private set; }
        public string? LastModel { get; private set; }
        public string? LastPrompt { get; private set; }
        public Exception? ExceptionToThrow { get; init; }
        public OpenAiResponseEnvelope Response { get; init; } = new(
            ResponseId: "resp_fake",
            Model: "gpt-test",
            Text: "fake response");

        public OpenAiResponseEnvelope CreateResponse(string model, string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastModel = model;
            LastPrompt = prompt;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Response;
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter OpenAiResponsesModelClientTests
```

Expected:

```text
Failed because OpenAiResponsesModelClient and IOpenAiResponsesGateway are not defined.
```

- [x] **Step 3: 添加 OpenAI NuGet package**

Run:

```powershell
dotnet add src/CSharpAiCli.Core/CSharpAiCli.Core.csproj package OpenAI --version 2.10.0
```

Expected:

```text
PackageReference Include="OpenAI" Version="2.10.0" is added to CSharpAiCli.Core.csproj.
```

- [x] **Step 4: 添加 model client 和 gateway 接口**

Create `src/CSharpAiCli.Core/IChatModelClient.cs`:

```csharp
namespace CSharpAiCli.Core;

public interface IChatModelClient
{
    ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default);
}
```

Create `src/CSharpAiCli.Core/OpenAiResponseEnvelope.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record OpenAiResponseEnvelope(
    string ResponseId,
    string Model,
    string Text);
```

Create `src/CSharpAiCli.Core/IOpenAiResponsesGateway.cs`:

```csharp
namespace CSharpAiCli.Core;

public interface IOpenAiResponsesGateway
{
    OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);
}
```

- [x] **Step 5: 添加 SDK gateway 实现**

Create `src/CSharpAiCli.Core/SdkOpenAiResponsesGateway.cs`:

```csharp
using System.ClientModel;
using OpenAI.Responses;

namespace CSharpAiCli.Core;

public sealed class SdkOpenAiResponsesGateway : IOpenAiResponsesGateway
{
    private readonly ResponsesClient client;

    public SdkOpenAiResponsesGateway(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        client = new ResponsesClient(apiKey);
    }

    public OpenAiResponseEnvelope CreateResponse(
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ClientResult<ResponseResult> result = client.CreateResponse(
            model,
            prompt,
            previousResponseId: null,
            cancellationToken: cancellationToken);

        ResponseResult response = result.Value;
        string text = ExtractOutputText(response);

        return new OpenAiResponseEnvelope(
            ResponseId: response.Id ?? "unknown",
            Model: response.Model ?? model,
            Text: text);
    }

    private static string ExtractOutputText(ResponseResult response)
    {
        IEnumerable<string> textParts = response.OutputItems
            .OfType<MessageResponseItem>()
            .Where(item => item.Role == MessageRole.Assistant)
            .SelectMany(item => item.Content)
            .Where(part => part.Kind == ResponseContentPartKind.OutputText)
            .Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))!;

        return string.Join(Environment.NewLine, textParts);
    }
}
```

- [x] **Step 6: 添加 OpenAI Responses model client**

Create `src/CSharpAiCli.Core/OpenAiResponsesModelClient.cs`:

```csharp
using System.ClientModel;

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
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.gatewayFactory = gatewayFactory ?? throw new ArgumentNullException(nameof(gatewayFactory));
    }

    public static OpenAiResponsesModelClient Create(CliEnvironmentSnapshot snapshot)
    {
        return new OpenAiResponsesModelClient(
            snapshot,
            apiKey => new SdkOpenAiResponsesGateway(apiKey));
    }

    public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasPrompt)
        {
            return Failure(
                localErrorCode: "empty-prompt",
                safeMessage: "Chat prompt is empty. Pass a prompt as caicli chat \"<prompt>\".",
                retryable: false);
        }

        if (IsMissingModel(snapshot.Configuration.Model))
        {
            return Failure(
                localErrorCode: "missing-model",
                safeMessage: "Model is not configured. Set model in .caicli/config.json before running chat.",
                retryable: false);
        }

        if (snapshot.Configuration.ApiKey is null)
        {
            return Failure(
                localErrorCode: "missing-openai-api-key",
                safeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        if (!IsAllowedApiKeySource(snapshot.Configuration.ApiKeySource))
        {
            return Failure(
                localErrorCode: "unsupported-api-key-source",
                safeMessage: "Workspace config apiKey is not used for model calls. Set OPENAI_API_KEY or user config apiKey.",
                retryable: false);
        }

        try
        {
            IOpenAiResponsesGateway gateway = gatewayFactory(snapshot.Configuration.ApiKey.Value);
            OpenAiResponseEnvelope response = gateway.CreateResponse(
                snapshot.Configuration.Model,
                request.Prompt.Trim(),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return Failure(
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
        catch (ClientResultException exception)
        {
            return ChatModelResult.Failure(new ModelError(
                Provider: Provider,
                Operation: Operation,
                StatusCode: exception.Status,
                LocalErrorCode: null,
                SafeMessage: CreateHttpSafeMessage(exception.Status),
                Retryable: IsRetryableStatus(exception.Status)));
        }
        catch (OperationCanceledException)
        {
            return Failure(
                localErrorCode: "model-call-canceled",
                safeMessage: "Model call was canceled before it completed.",
                retryable: true);
        }
        catch
        {
            return Failure(
                localErrorCode: "openai-client-error",
                safeMessage: "OpenAI model call failed before a response was completed.",
                retryable: true);
        }
    }

    private static bool IsMissingModel(string model)
    {
        return string.IsNullOrWhiteSpace(model)
            || string.Equals(model, "not configured", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedApiKeySource(string apiKeySource)
    {
        return string.Equals(apiKeySource, "OPENAI_API_KEY", StringComparison.Ordinal)
            || string.Equals(apiKeySource, "user config", StringComparison.Ordinal);
    }

    private static ChatModelResult Failure(string localErrorCode, string safeMessage, bool retryable)
    {
        return ChatModelResult.Failure(new ModelError(
            Provider: Provider,
            Operation: Operation,
            StatusCode: null,
            LocalErrorCode: localErrorCode,
            SafeMessage: safeMessage,
            Retryable: retryable));
    }

    private static string CreateHttpSafeMessage(int statusCode)
    {
        return statusCode switch
        {
            401 => "OpenAI request was not authorized. Check the configured API key.",
            403 => "OpenAI request was forbidden for the configured project or model.",
            404 => "OpenAI model or endpoint was not found. Check the configured model.",
            429 => "OpenAI rate limit was reached. Retry later or use a different quota.",
            >= 500 => "OpenAI service returned a server error. Retry later.",
            _ => "OpenAI request failed. Check model configuration and account access."
        };
    }

    private static bool IsRetryableStatus(int statusCode)
    {
        return statusCode == 429 || statusCode >= 500;
    }
}
```

- [x] **Step 7: 运行 OpenAI model client 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter OpenAiResponsesModelClientTests
```

Expected:

```text
Passed!  - Failed: 0, Passed: 6
```

- [x] **Step 8: Commit**

Run:

```powershell
git add src/CSharpAiCli.Core/CSharpAiCli.Core.csproj src/CSharpAiCli.Core/IChatModelClient.cs src/CSharpAiCli.Core/OpenAiResponseEnvelope.cs src/CSharpAiCli.Core/IOpenAiResponsesGateway.cs src/CSharpAiCli.Core/SdkOpenAiResponsesGateway.cs src/CSharpAiCli.Core/OpenAiResponsesModelClient.cs src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs
git commit -m "feat: add openai responses model client"
```

Expected:

```text
Commit created with OpenAI Responses model client.
```

## Task 3: 将 `caicli chat "<prompt>"` 接到 model client

**Files:**

- Modify: `src/CSharpAiCli.Cli/CliCommandFactory.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [x] **Step 1: 更新 CLI tests**

In `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`, replace the existing `Chat_command_returns_phase_02_boundary_message_and_logs_command` test with these tests:

```csharp
    [Fact]
    public void Chat_command_sends_prompt_to_model_client_and_logs_command()
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
                _ => chatClient)
            .Parse(["chat", "--workspace", "custom-root", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("custom-root", receivedWorkspace);
        Assert.Equal(["chat"], loggedCommands);
        Assert.Equal("hello model", chatClient.LastPrompt);
        Assert.Contains("status: completed", output.ToString());
        Assert.Contains("fake model output", output.ToString());
    }

    [Fact]
    public void Chat_command_returns_nonzero_for_model_error()
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
                _ => chatClient)
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.Contains("status: failed", output.ToString());
        Assert.Contains("localErrorCode: missing-openai-api-key", output.ToString());
    }
```

Then keep a one-argument helper for existing method-group tests:

```csharp
    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
    {
        return CreateSnapshot(
            workspacePath,
            apiKey: null,
            apiKeySource: "missing",
            model: "not configured");
    }
```

Then add this overload:

```csharp
    private static CliEnvironmentSnapshot CreateSnapshot(
        string? workspacePath,
        string? apiKey,
        string apiKeySource,
        string model)
```

If the file currently has only this helper:

```csharp
    private static CliEnvironmentSnapshot CreateSnapshot(string? workspacePath)
```

replace it with both helpers shown above, preserving the existing body inside the four-argument overload.

Inside the four-argument helper, replace:

```csharp
            Model: "not configured",
            ModelSource: "default",
            ApiKey: null,
            ApiKeySource: "missing",
```

with:

```csharp
            Model: model,
            ModelSource: model == "not configured" ? "default" : "workspace config",
            ApiKey: SecretValue.From(apiKey),
            ApiKeySource: apiKeySource,
```

Add this fake client before the final closing brace:

```csharp
    private sealed class FakeChatModelClient(ChatModelResult result) : IChatModelClient
    {
        public string? LastPrompt { get; private set; }

        public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            return result;
        }
    }
```

- [x] **Step 2: 运行 CLI tests 确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Failed because CliCommandFactory does not accept a chat model client factory and chat still returns the Phase 01 boundary.
```

- [x] **Step 3: 更新 CLI command factory overloads**

In `src/CSharpAiCli.Cli/CliCommandFactory.cs`, replace the three `Create` overload declarations with:

```csharp
    public static RootCommand Create(TextWriter output)
    {
        return Create(
            output,
            workspacePath => CliEnvironmentSnapshot.Create(workspacePath: workspacePath),
            (commandName, snapshot) => CommandLogger.Append(commandName, snapshot),
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
    }

    public static RootCommand Create(TextWriter output, Func<string?, CliEnvironmentSnapshot> snapshotProvider)
    {
        return Create(
            output,
            snapshotProvider,
            (_, _) => { },
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
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
            snapshot => OpenAiResponsesModelClient.Create(snapshot));
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory)
```

At the start of the final overload body, add:

```csharp
        ArgumentNullException.ThrowIfNull(chatModelClientFactory);
```

- [x] **Step 4: Replace the chat command block**

In `src/CSharpAiCli.Cli/CliCommandFactory.cs`, replace the existing `chatCommand` block with:

```csharp
        Command chatCommand = new("chat", "Send one prompt to the configured model.");
        Argument<string> promptArgument = new("prompt")
        {
            Description = "The user message to send to the model.",
        };
        chatCommand.Arguments.Add(promptArgument);
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            ChatModelResult result = chatModelClient.Send(new ChatRequest(prompt));
            output.WriteLine(ChatModelReport.Create(snapshot, result).ToDisplayText());
            return result.IsSuccess ? 0 : 1;
        });
```

- [x] **Step 5: 运行 CLI tests**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected:

```text
Passed!  - Failed: 0
```

- [x] **Step 6: Commit**

Run:

```powershell
git add src/CSharpAiCli.Cli/CliCommandFactory.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "feat: connect chat command to model client"
```

Expected:

```text
Commit created with chat model command wiring.
```

## Task 4: 记录 Week 5 运行说明和 smoke 边界

**Files:**

- Create: `docs_md/spec/model_client_responses_api.md`

- [x] **Step 1: 创建 model client 和 Responses API 说明**

Create `docs_md/spec/model_client_responses_api.md`:

````markdown
# Model Client 与 OpenAI Responses API 说明

## 状态

第 5 周添加一次性非流式模型调用。流式输出、会话和转录归属第 6-7 周。

## CLI 行为

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."
```

成功时输出：

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

失败时输出：

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

第 5 周沿用最小配置 schema：

```json
{
  "model": "gpt-4.1-mini",
  "apiKey": "sk-user-config-only"
}
```

真实模型调用只使用：

- `OPENAI_API_KEY`
- 用户配置文件中的 `apiKey`

工作区配置中的 `apiKey` 不用于真实模型调用。完整配置优先级清理和 schema 收紧在第 8 周完成。

## SDK

OpenAI SDK 包：

```text
OpenAI 2.10.0
```

第 5 周使用 `OpenAI.Responses.ResponsesClient.CreateResponse(string model, string userInputText, ...)`。第 6 周改造为流式输出时，使用 SDK 的 streaming Responses API。

## Smoke

缺 key smoke：

```powershell
$oldOpenAiKey = $env:OPENAI_API_KEY
Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."
if ($null -eq $oldOpenAiKey) {
    Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
} else {
    $env:OPENAI_API_KEY = $oldOpenAiKey
}
````

Expected:

```text
命令返回非 0。
输出包含 status: failed。
输出包含 localErrorCode: missing-openai-api-key。
输出不包含任何 API key 值。
```

真实模型 smoke：

```powershell
$env:OPENAI_API_KEY = "<real key from developer environment>"
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."
```

Expected:

```text
命令返回 0。
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
输出包含模型文本。
输出不包含 API key 值。
```
```

- [x] **Step 2: 扫描说明文档占位内容**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/spec/model_client_responses_api.md
```

Expected:

```text
No matches.
```

- [x] **Step 3: Commit**

Run:

```powershell
git add docs_md/spec/model_client_responses_api.md
git commit -m "docs: add model client responses api notes"
```

Expected:

```text
Commit created with Week 5 model client notes.
```

## Task 5: Week 5 验证与回顾

**Files:**

- Verify: `src/CSharpAiCli.sln`
- Modify: `docs_md/weekly/26_week_goal_schedule.md`
- Create: `docs_md/weekly/05_week_review.md`

- [x] **Step 1: 构建 solution**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded.
```

- [x] **Step 2: 运行全部测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Passed!  - Failed: 0
```

- [x] **Step 3: 验证缺 key chat 错误**

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

- [x] **Step 4: 验证真实模型 smoke**

Only run this step when a real key is available in the developer environment.

Run:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."
```

Expected:

```text
输出包含 status: completed。
输出包含 provider: openai。
输出包含 responseId:。
输出包含模型响应文本。
命令 exit code 是 0。
```

- [x] **Step 5: 验证命令日志仍脱敏**

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

- [x] **Step 6: 更新总周计划 Week 5 状态**

In `docs_md/weekly/26_week_goal_schedule.md`, replace the current progress item:

```markdown
- 当前下一步是第 5 周：添加 model client 抽象和 OpenAI SDK Responses API 实现。
```

with:

```markdown
- 第 5 周已稳固：model client 抽象、OpenAI SDK Responses API 实现和一次性 `chat "<prompt>"` 模型调用已完成。详见 `05_week_review.md`。
```

Then update the Week 5 row from:

```markdown
| 5 | 2024-07-01 至 2024-07-07 | 阶段 02 | 计划中 | 添加 model client 抽象和 OpenAI SDK Responses API 实现。详见 `05_week_model_client_responses_api.plan.md`。 | smoke 命令可以用已配置凭据调用模型；缺 key 错误清晰。 |
```

to:

```markdown
| 5 | 2024-07-01 至 2024-07-07 | 阶段 02 | 已稳固 | 添加 model client 抽象和 OpenAI SDK Responses API 实现。详见 `05_week_model_client_responses_api.plan.md` 和 `05_week_review.md`。 | 已验证：`chat "<prompt>"` 可以用已配置凭据调用模型；缺 key 错误清晰。 |
```

- [x] **Step 7: 创建第 5 周回顾**

Create `docs_md/weekly/05_week_review.md`:

```markdown
# 第 05 周回顾

状态：已稳固

已完成：

- 添加 provider-neutral `IChatModelClient`。
- 添加 chat request、response、result、error 和 report 类型。
- 添加 OpenAI Responses API SDK 适配器。
- 将 `caicli chat "<prompt>"` 接到一次性非流式模型调用。
- 缺少 API key、缺少 model、空 prompt 和本地 SDK 失败会输出安全错误。
- 真实模型调用不使用工作区配置中的 `apiKey`。

验证：

- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."`，无 `OPENAI_API_KEY`
- 结果：返回 exit code 1，输出 `status: failed` 和 `localErrorCode: missing-openai-api-key`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with the single word OK."`，有真实 `OPENAI_API_KEY` 和配置 model
- 结果：返回 exit code 0，输出 `status: completed`、`provider: openai`、`responseId:` 和模型文本
- 命令：命令日志脱敏 smoke
- 结果：日志包含 `command=chat` 和 API key 状态，不包含密钥值

运行时说明：

- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- OpenAI SDK package 为 `OpenAI` `2.10.0`。
- 第 5 周是一次性非流式 Responses API 调用。

风险：

- 流式输出尚未实现，归属第 6 周。
- 会话和转录尚未实现，归属第 7 周。
- 完整配置优先级和 schema 收紧尚未完成，归属第 8 周。
- 当前错误输出是文本报告，尚未提供结构化 JSON 输出。

第 06 周输入：

- 添加用于 chat 输出的终端流式渲染器。
- 将 OpenAI Responses API 调用改造为 streaming path。
- 保持当前缺 key、缺 model 和脱敏行为。
```

- [x] **Step 8: 扫描文档占位内容**

Run:

```powershell
$patterns = @('TB' + 'D', 'TO' + 'DO', 'fill in ' + 'details', 'implement ' + 'later')
rg -n ($patterns -join '|') docs_md/spec/model_client_responses_api.md docs_md/weekly/05_week_review.md docs_md/weekly/26_week_goal_schedule.md
```

Expected:

```text
No matches.
```

- [x] **Step 9: Commit**

Run:

```powershell
git add docs_md/weekly/26_week_goal_schedule.md docs_md/weekly/05_week_review.md
git commit -m "docs: record week five model client progress"
```

Expected:

```text
Commit created with Week 5 review and schedule update.
```

## 验收标准

第 5 周完成时必须满足：

- `ChatModelResultTests` 通过。
- `ChatModelReportTests` 通过。
- `OpenAiResponsesModelClientTests` 通过。
- 更新后的 `CliCommandFactoryTests` 通过。
- `dotnet build src/CSharpAiCli.sln` 成功。
- `dotnet test src/CSharpAiCli.sln` 成功。
- `dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."` 在缺少 API key 时以 `1` 退出。
- 缺 key 输出包含 `status: failed` 和 `localErrorCode: missing-openai-api-key`。
- 缺 model 输出包含 `localErrorCode: missing-model`。
- 工作区配置 `apiKey` 不用于真实模型调用，输出 `localErrorCode: unsupported-api-key-source`。
- 配置真实 API key 和 model 后，`chat "<prompt>"` 可以调用 OpenAI Responses API 并打印模型文本。
- `chat` 成功输出包含 `provider: openai`、`model:`、`responseId:` 和响应文本。
- `chat` 输出、命令日志和对象字符串不打印原始 API key。
- `docs_md/spec/model_client_responses_api.md` 记录 SDK、配置和 smoke 边界。
- `docs_md/weekly/05_week_review.md` 在周末记录验证输出和第 6 周输入。
