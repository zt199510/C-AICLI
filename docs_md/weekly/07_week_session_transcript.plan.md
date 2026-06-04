# 第 7 周 Session Store 与 Transcript Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 添加命名会话恢复、用户数据目录中的会话存储，以及版本化 JSON transcript v1。

**Architecture:** `CSharpAiCli.Core` 新增 provider-neutral conversation/transcript 领域类型和文件存储实现；`CSharpAiCli.Cli` 只负责 `chat --session <name> "<prompt>"` 参数接线、store 注入、会话加载、模型调用和 transcript 写入。Week 7 只持久化用户消息、assistant 消息和安全错误；`toolCalls` 保留稳定空数组，供 Week 9 接入真实工具调用记录。

**Tech Stack:** C#、`net9.0`、当前机器 .NET SDK `9.0.308`、`System.CommandLine` `2.0.8`、`System.Text.Json`、xUnit、Windows PowerShell。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 02 计划：`docs_md/plans/02_model_streaming_sessions.plan.md`
- 第 6 周计划：`docs_md/weekly/06_week_chat_streaming_renderer.plan.md`
- 第 6 周回顾：`docs_md/weekly/06_week_review.md`
- Week 5/6 spec：`docs_md/spec/model_client_responses_api.md`

第 7 周排期目标：

```text
添加会话存储、版本化转录格式和工具调用初始 schema。
周末验收：命名会话可以恢复，并写入可检查转录。
```

第 6 周输入：

```text
添加会话存储。
添加版本化 transcript 格式。
添加命名会话恢复。
为后续工具调用保留 transcript schema 占位字段。
```

用户确认的 Week 7 CLI 入口：

```text
caicli chat --session <name> "<prompt>"
```

## 本周范围

第 7 周必须完成：

- `chat --session <name> "<prompt>"` 可以加载或创建命名会话。
- 命名会话恢复时，新 prompt 会附加到同一个 transcript 文件。
- transcript 使用版本化 JSON v1，字段稳定且可人工检查。
- transcript 最低字段包含 `schemaVersion`、`sessionName`、`createdAtUtc`、`updatedAtUtc`、`messages`、`toolCalls`、`errors`。
- 成功模型调用写入一条 user message 和一条 assistant message。
- 模型调用失败写入一条 user message 和一条安全 error，不写入误导性的 assistant success。
- validation 失败，例如缺 API key、缺 model、空 prompt，也走同样的安全 transcript 失败路径，只要用户显式传入 `--session`。空 prompt 记录原始字符串，不把它伪装成非空消息。
- 无 `--session` 时保持 Week 6 行为，不创建 transcript。
- session name 必须校验并规范化，避免路径穿越和不可预测文件名。
- transcript 中不得写入原始 API key；错误只保存 `ModelError` 的安全字段。
- 单元测试覆盖 schema、store、恢复追加、错误记录、CLI 接线和无 session 不落盘。

第 7 周不做：

- 多轮 prompt 拼接到真实模型请求上下文；本周先恢复 transcript，不把历史消息发送给模型。
- 交互式 REPL。
- `chat --continue`、session list/delete/rename 命令。
- instruction loader 或 `AICLI.md`。
- 工具调用执行、工具注册表、文件读取、patch、shell runner。
- Microsoft Agent Framework、MCP 或 backend provider registry。
- token 统计、费用统计或 usage 精确值；只预留 metadata 字段。
- JSON/NDJSON 终端输出模式。

## 会话文件位置

Week 7 将 session transcript 写入用户 profile 下的 CLI 数据目录：

```text
<user profile>/.caicli/sessions/<safe-session-name>.transcript.json
```

当前 `CliEnvironmentSnapshot` 已有 `UserConfigPath`。实现时从 `Path.GetDirectoryName(snapshot.UserConfigPath)` 得到 `<user profile>/.caicli`，再拼接 `sessions`。这样测试可以继续用隔离的 fake user profile，不需要新增 OS-specific app data 解析。

## Transcript v1 JSON

最小 JSON 形状：

```json
{
  "schemaVersion": 1,
  "sessionName": "smoke",
  "createdAtUtc": "2024-01-01T00:00:00Z",
  "updatedAtUtc": "2024-01-01T00:00:05Z",
  "messages": [
    {
      "role": "user",
      "createdAtUtc": "2024-01-01T00:00:01Z",
      "content": "Reply with OK.",
      "provider": null,
      "model": null,
      "responseId": null
    },
    {
      "role": "assistant",
      "createdAtUtc": "2024-01-01T00:00:05Z",
      "content": "OK.",
      "provider": "openai",
      "model": "gpt-test",
      "responseId": "resp_123"
    }
  ],
  "toolCalls": [],
  "errors": []
}
```

错误记录形状：

```json
{
  "createdAtUtc": "2024-01-01T00:00:05Z",
  "provider": "openai",
  "operation": "responses.create",
  "statusCode": null,
  "localErrorCode": "missing-openai-api-key",
  "safeMessage": "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
  "retryable": false
}
```

Tool call 占位形状：

```json
"toolCalls": []
```

Week 9 可以把该数组扩展为对象数组；Week 7 不写入 tool call 对象。

## 文件结构

第 7 周创建或修改：

```text
src/
  CSharpAiCli.Core/
    Chat/
      ChatRequest.cs                         # 修改：增加可选 SessionName
    Conversations/
      ConversationTranscript.cs              # 新增：transcript aggregate
      ConversationMessage.cs                 # 新增：user/assistant message entry
      ConversationError.cs                   # 新增：safe model error entry
      IConversationStore.cs                  # 新增：store contract
      FileConversationStore.cs               # 新增：JSON file store
      ConversationSessionName.cs             # 新增：session name validation and file-safe name
      ConversationTranscriptRecorder.cs      # 新增：把 ChatModelResult 记录到 transcript
  CSharpAiCli.Cli/
    Commands/
      CliCommandFactory.cs                   # 修改：增加 --session 接线和 store 注入
  CSharpAiCli.Tests/
    ConversationSessionNameTests.cs          # 新增：session name 校验和路径安全
    FileConversationStoreTests.cs            # 新增：load/create/save 和 JSON schema 测试
    ConversationTranscriptRecorderTests.cs   # 新增：成功/失败 transcript 记录测试
    CliCommandFactoryTests.cs                # 修改：session 参数、恢复追加、无 session 不落盘
docs_md/
  spec/
    model_client_responses_api.md            # 修改：补充 Week 7 session/transcript 行为
  weekly/
    26_week_goal_schedule.md                 # 周末收尾时更新 Week 7 状态
    07_week_review.md                        # 周末收尾时创建
```

---

## Task 1: 添加 session name 值对象

**Files:**

- Create: `src/CSharpAiCli.Core/Conversations/ConversationSessionName.cs`
- Create: `src/CSharpAiCli.Tests/ConversationSessionNameTests.cs`

- [ ] **Step 1: 写失败的 session name 测试**

Create `src/CSharpAiCli.Tests/ConversationSessionNameTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationSessionNameTests
{
    [Theory]
    [InlineData("smoke", "smoke")]
    [InlineData("Week-7_Smoke", "week-7_smoke")]
    [InlineData("release notes", "release-notes")]
    [InlineData("  My Session  ", "my-session")]
    public void Parse_accepts_safe_names_and_creates_file_safe_name(string input, string expectedSafeName)
    {
        ConversationSessionName name = ConversationSessionName.Parse(input);

        Assert.Equal(input.Trim(), name.Value);
        Assert.Equal(expectedSafeName, name.FileSafeName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("name*")]
    public void Parse_rejects_empty_path_like_or_invalid_names(string input)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => ConversationSessionName.Parse(input));

        Assert.Contains("session", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationSessionNameTests
```

Expected: FAIL，因为 `ConversationSessionName` 尚不存在。

- [ ] **Step 3: 实现 session name 值对象**

Create `src/CSharpAiCli.Core/Conversations/ConversationSessionName.cs`:

```csharp
using System.Text;

namespace CSharpAiCli.Core;

public sealed record ConversationSessionName(string Value, string FileSafeName)
{
    private static readonly char[] InvalidChars =
    [
        ..Path.GetInvalidFileNameChars(),
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    ];

    public static ConversationSessionName Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A session name is required.", nameof(value));
        }

        string trimmed = value.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ArgumentException("Session name cannot contain path traversal or invalid file name characters.", nameof(value));
        }

        string safeName = ToFileSafeName(trimmed);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException("Session name must contain at least one letter or number.", nameof(value));
        }

        return new ConversationSessionName(trimmed, safeName);
    }

    private static string ToFileSafeName(string value)
    {
        StringBuilder builder = new();
        bool previousDash = false;

        foreach (char character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
                previousDash = false;
                continue;
            }

            if (char.IsWhiteSpace(character) && !previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
```

- [ ] **Step 4: 运行 session name 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationSessionNameTests
```

Expected: PASS。

- [ ] **Step 5: Commit**

```powershell
git add src/CSharpAiCli.Core/Conversations/ConversationSessionName.cs src/CSharpAiCli.Tests/ConversationSessionNameTests.cs
git commit -m "feat: add conversation session names"
```

---

## Task 2: 添加 transcript 领域类型

**Files:**

- Create: `src/CSharpAiCli.Core/Conversations/ConversationTranscript.cs`
- Create: `src/CSharpAiCli.Core/Conversations/ConversationMessage.cs`
- Create: `src/CSharpAiCli.Core/Conversations/ConversationError.cs`
- Create: `src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs`

- [ ] **Step 1: 写失败的 transcript aggregate 测试**

Create `src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs`:

```csharp
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ConversationTranscriptRecorderTests
{
    [Fact]
    public void New_transcript_starts_with_schema_version_and_empty_tool_calls()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

        ConversationTranscript transcript = ConversationTranscript.Create("smoke", now);

        Assert.Equal(1, transcript.SchemaVersion);
        Assert.Equal("smoke", transcript.SessionName);
        Assert.Equal(now, transcript.CreatedAtUtc);
        Assert.Equal(now, transcript.UpdatedAtUtc);
        Assert.Empty(transcript.Messages);
        Assert.Empty(transcript.ToolCalls);
        Assert.Empty(transcript.Errors);
    }

    [Fact]
    public void Add_user_message_appends_message_and_updates_timestamp()
    {
        ConversationTranscript transcript = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        DateTimeOffset messageTime = DateTimeOffset.Parse("2024-01-01T00:00:01Z");

        transcript.AddUserMessage("Reply with OK.", messageTime);

        ConversationMessage message = Assert.Single(transcript.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Reply with OK.", message.Content);
        Assert.Equal(messageTime, message.CreatedAtUtc);
        Assert.Null(message.Provider);
        Assert.Null(message.Model);
        Assert.Null(message.ResponseId);
        Assert.Equal(messageTime, transcript.UpdatedAtUtc);
    }
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationTranscriptRecorderTests
```

Expected: FAIL，因为 transcript 类型尚不存在。

- [ ] **Step 3: 实现 transcript message 类型**

Create `src/CSharpAiCli.Core/Conversations/ConversationMessage.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ConversationMessage(
    string Role,
    DateTimeOffset CreatedAtUtc,
    string Content,
    string? Provider,
    string? Model,
    string? ResponseId);
```

- [ ] **Step 4: 实现 transcript error 类型**

Create `src/CSharpAiCli.Core/Conversations/ConversationError.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed record ConversationError(
    DateTimeOffset CreatedAtUtc,
    string Provider,
    string Operation,
    int? StatusCode,
    string? LocalErrorCode,
    string SafeMessage,
    bool Retryable)
{
    public static ConversationError FromModelError(ModelError error, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ConversationError(
            CreatedAtUtc: createdAtUtc,
            Provider: error.Provider,
            Operation: error.Operation,
            StatusCode: error.StatusCode,
            LocalErrorCode: error.LocalErrorCode,
            SafeMessage: error.SafeMessage,
            Retryable: error.Retryable);
    }
}
```

- [ ] **Step 5: 实现 transcript aggregate**

Create `src/CSharpAiCli.Core/Conversations/ConversationTranscript.cs`:

```csharp
namespace CSharpAiCli.Core;

public sealed class ConversationTranscript
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SessionName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<ConversationMessage> Messages { get; init; } = [];
    public List<object> ToolCalls { get; init; } = [];
    public List<ConversationError> Errors { get; init; } = [];

    public static ConversationTranscript Create(string sessionName, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        return new ConversationTranscript
        {
            SchemaVersion = CurrentSchemaVersion,
            SessionName = sessionName,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void AddUserMessage(string content, DateTimeOffset nowUtc)
    {
        Messages.Add(new ConversationMessage(
            Role: "user",
            CreatedAtUtc: nowUtc,
            Content: content ?? string.Empty,
            Provider: null,
            Model: null,
            ResponseId: null));
        UpdatedAtUtc = nowUtc;
    }

    public void AddAssistantMessage(ChatResponse response, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        Messages.Add(new ConversationMessage(
            Role: "assistant",
            CreatedAtUtc: nowUtc,
            Content: response.Text,
            Provider: response.Provider,
            Model: response.Model,
            ResponseId: response.ResponseId));
        UpdatedAtUtc = nowUtc;
    }

    public void AddError(ModelError error, DateTimeOffset nowUtc)
    {
        Errors.Add(ConversationError.FromModelError(error, nowUtc));
        UpdatedAtUtc = nowUtc;
    }
}
```

- [ ] **Step 6: 运行 transcript aggregate 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationTranscriptRecorderTests
```

Expected: PASS。

- [ ] **Step 7: Commit**

```powershell
git add src/CSharpAiCli.Core/Conversations/ConversationTranscript.cs src/CSharpAiCli.Core/Conversations/ConversationMessage.cs src/CSharpAiCli.Core/Conversations/ConversationError.cs src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs
git commit -m "feat: add conversation transcript model"
```

---

## Task 3: 添加文件会话存储

**Files:**

- Create: `src/CSharpAiCli.Core/Conversations/IConversationStore.cs`
- Create: `src/CSharpAiCli.Core/Conversations/FileConversationStore.cs`
- Create: `src/CSharpAiCli.Tests/FileConversationStoreTests.cs`

- [ ] **Step 1: 写失败的 file store 测试**

Create `src/CSharpAiCli.Tests/FileConversationStoreTests.cs`:

```csharp
using System.Text.Json;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class FileConversationStoreTests
{
    [Fact]
    public void Load_or_create_returns_new_transcript_when_file_is_missing()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        DateTimeOffset now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

        ConversationTranscript transcript = store.LoadOrCreate(sessionName, now);

        Assert.Equal("smoke", transcript.SessionName);
        Assert.Equal(now, transcript.CreatedAtUtc);
        Assert.Empty(transcript.Messages);
    }

    [Fact]
    public void Save_writes_versioned_json_to_session_file()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("Reply with OK.", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));

        string path = store.Save(sessionName, transcript);

        Assert.Equal(Path.Combine(temp.Path, ".caicli", "sessions", "smoke.transcript.json"), path);
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("smoke", root.GetProperty("sessionName").GetString());
        Assert.Equal("Reply with OK.", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("toolCalls").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
    }

    [Fact]
    public void Load_or_create_restores_existing_transcript()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        store.Save(sessionName, transcript);

        ConversationTranscript restored = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z"));

        Assert.Equal("smoke", restored.SessionName);
        Assert.Equal(DateTimeOffset.Parse("2024-01-01T00:00:00Z"), restored.CreatedAtUtc);
        Assert.Equal("first", Assert.Single(restored.Messages).Content);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: 运行 file store 测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter FileConversationStoreTests
```

Expected: FAIL，因为 store 类型尚不存在。

- [ ] **Step 3: 实现 store contract**

Create `src/CSharpAiCli.Core/Conversations/IConversationStore.cs`:

```csharp
namespace CSharpAiCli.Core;

public interface IConversationStore
{
    ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc);

    string Save(ConversationSessionName sessionName, ConversationTranscript transcript);
}
```

- [ ] **Step 4: 实现 JSON file store**

Create `src/CSharpAiCli.Core/Conversations/FileConversationStore.cs`:

```csharp
using System.Text.Json;

namespace CSharpAiCli.Core;

public sealed class FileConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string sessionDirectory;

    public FileConversationStore(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        this.sessionDirectory = sessionDirectory;
    }

    public static FileConversationStore Create(CliEnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? userConfigDirectory = Path.GetDirectoryName(snapshot.UserConfigPath);
        string root = string.IsNullOrWhiteSpace(userConfigDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".caicli")
            : userConfigDirectory;

        return new FileConversationStore(Path.Combine(root, "sessions"));
    }

    public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessionName);

        string path = GetPath(sessionName);
        if (!File.Exists(path))
        {
            return ConversationTranscript.Create(sessionName.Value, nowUtc);
        }

        string json = File.ReadAllText(path);
        ConversationTranscript? transcript = JsonSerializer.Deserialize<ConversationTranscript>(json, JsonOptions);
        if (transcript is null || transcript.SchemaVersion != ConversationTranscript.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Conversation transcript is missing or uses an unsupported schema version.");
        }

        return transcript;
    }

    public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        ArgumentNullException.ThrowIfNull(transcript);

        Directory.CreateDirectory(sessionDirectory);
        string path = GetPath(sessionName);
        string json = JsonSerializer.Serialize(transcript, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    private string GetPath(ConversationSessionName sessionName)
    {
        return Path.Combine(sessionDirectory, $"{sessionName.FileSafeName}.transcript.json");
    }
}
```

- [ ] **Step 5: 运行 file store 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter FileConversationStoreTests
```

Expected: PASS。

- [ ] **Step 6: Commit**

```powershell
git add src/CSharpAiCli.Core/Conversations/IConversationStore.cs src/CSharpAiCli.Core/Conversations/FileConversationStore.cs src/CSharpAiCli.Tests/FileConversationStoreTests.cs
git commit -m "feat: add file conversation store"
```

---

## Task 4: 添加 transcript recorder

**Files:**

- Create: `src/CSharpAiCli.Core/Conversations/ConversationTranscriptRecorder.cs`
- Modify: `src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs`

- [ ] **Step 1: 扩展失败的 recorder 测试**

Append to `src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs`:

```csharp
    [Fact]
    public void Record_success_appends_user_and_assistant_messages()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset writeTime = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create("smoke", start);
        ChatModelResult result = ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_123",
            Text: "OK."));

        ConversationTranscriptRecorder.RecordTurn(transcript, "Reply with OK.", result, writeTime);

        Assert.Equal(2, transcript.Messages.Count);
        Assert.Equal("user", transcript.Messages[0].Role);
        Assert.Equal("Reply with OK.", transcript.Messages[0].Content);
        Assert.Equal("assistant", transcript.Messages[1].Role);
        Assert.Equal("OK.", transcript.Messages[1].Content);
        Assert.Equal("openai", transcript.Messages[1].Provider);
        Assert.Equal("gpt-test", transcript.Messages[1].Model);
        Assert.Equal("resp_123", transcript.Messages[1].ResponseId);
        Assert.Empty(transcript.Errors);
        Assert.Equal(writeTime, transcript.UpdatedAtUtc);
    }

    [Fact]
    public void Record_failure_appends_user_message_and_safe_error()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        DateTimeOffset writeTime = DateTimeOffset.Parse("2024-01-01T00:00:05Z");
        ConversationTranscript transcript = ConversationTranscript.Create("smoke", start);
        ChatModelResult result = ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false));

        ConversationTranscriptRecorder.RecordTurn(transcript, "Reply with OK.", result, writeTime);

        ConversationMessage message = Assert.Single(transcript.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Reply with OK.", message.Content);
        ConversationError error = Assert.Single(transcript.Errors);
        Assert.Equal("openai", error.Provider);
        Assert.Equal("responses.create", error.Operation);
        Assert.Equal("missing-openai-api-key", error.LocalErrorCode);
        Assert.DoesNotContain("sk-", error.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(writeTime, transcript.UpdatedAtUtc);
    }
```

- [ ] **Step 2: 运行 recorder 测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationTranscriptRecorderTests
```

Expected: FAIL，因为 `ConversationTranscriptRecorder` 尚不存在。

- [ ] **Step 3: 实现 recorder**

Create `src/CSharpAiCli.Core/Conversations/ConversationTranscriptRecorder.cs`:

```csharp
namespace CSharpAiCli.Core;

public static class ConversationTranscriptRecorder
{
    public static void RecordTurn(
        ConversationTranscript transcript,
        string prompt,
        ChatModelResult result,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(result);

        transcript.AddUserMessage(prompt, nowUtc);

        if (result.Response is not null)
        {
            transcript.AddAssistantMessage(result.Response, nowUtc);
            return;
        }

        if (result.Error is not null)
        {
            transcript.AddError(result.Error, nowUtc);
        }
    }
}
```

- [ ] **Step 4: 运行 recorder 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter ConversationTranscriptRecorderTests
```

Expected: PASS。

- [ ] **Step 5: Commit**

```powershell
git add src/CSharpAiCli.Core/Conversations/ConversationTranscriptRecorder.cs src/CSharpAiCli.Tests/ConversationTranscriptRecorderTests.cs
git commit -m "feat: record chat turns in transcripts"
```

---

## Task 5: 扩展 ChatRequest 支持 session name

**Files:**

- Modify: `src/CSharpAiCli.Core/Chat/ChatRequest.cs`
- Modify: `src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs`

- [ ] **Step 1: 写失败的 ChatRequest 测试**

Append to `src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs`:

```csharp
    [Fact]
    public void Send_ignores_session_name_for_week_seven_model_call_payload()
    {
        FakeGateway gateway = new()
        {
            Response = new OpenAiResponseEnvelope(
                ResponseId: "resp_session",
                Model: "gpt-test",
                Text: "hello from model")
        };
        OpenAiResponsesModelClient client = new(
            CreateSnapshot(apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
            _ => gateway);

        ChatModelResult result = client.Send(new ChatRequest("hello", SessionName: "smoke"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", gateway.LastPrompt);
    }
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter Send_ignores_session_name_for_week_seven_model_call_payload
```

Expected: FAIL，因为 `ChatRequest` 还没有 `SessionName` 参数。

- [ ] **Step 3: 修改 ChatRequest**

Replace `src/CSharpAiCli.Core/Chat/ChatRequest.cs` with:

```csharp
namespace CSharpAiCli.Core;

public sealed record ChatRequest(string Prompt, string? SessionName = null)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt);
    public bool HasSession => !string.IsNullOrWhiteSpace(SessionName);
}
```

- [ ] **Step 4: 运行 ChatRequest 相关测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "OpenAiResponsesModelClientTests|ChatModelResultTests|CliCommandFactoryTests"
```

Expected: PASS。既有 `new ChatRequest("hello")` 调用应继续编译。

- [ ] **Step 5: Commit**

```powershell
git add src/CSharpAiCli.Core/Chat/ChatRequest.cs src/CSharpAiCli.Tests/OpenAiResponsesModelClientTests.cs
git commit -m "feat: include session name on chat requests"
```

---

## Task 6: CLI 接入 `chat --session`

**Files:**

- Modify: `src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [ ] **Step 1: 写失败的 CLI session 成功测试**

Append to `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
    [Fact]
    public void Chat_session_loads_transcript_records_success_and_saves()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal("smoke", chatClient.LastRequest?.SessionName);
        Assert.Equal("smoke", store.LoadedSessionName?.Value);
        Assert.Equal("smoke", store.SavedSessionName?.Value);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(2, store.SavedTranscript.Messages.Count);
        Assert.Equal("hello model", store.SavedTranscript.Messages[0].Content);
        Assert.Equal("fake model output", store.SavedTranscript.Messages[1].Content);
    }
```

- [ ] **Step 2: 写失败的 CLI session 错误测试**

Append to `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
    [Fact]
    public void Chat_session_records_failure_and_returns_nonzero()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Failure(new ModelError(
            Provider: "openai",
            Operation: "responses.create",
            StatusCode: null,
            LocalErrorCode: "missing-openai-api-key",
            SafeMessage: "OpenAI API key is missing. Set OPENAI_API_KEY or user config apiKey.",
            Retryable: false)));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: null, apiKeySource: "missing", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "hello model"])
            .Invoke();

        Assert.Equal(1, exitCode);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal("hello model", Assert.Single(store.SavedTranscript.Messages).Content);
        Assert.Equal("missing-openai-api-key", Assert.Single(store.SavedTranscript.Errors).LocalErrorCode);
    }
```

- [ ] **Step 3: 写无 session 不落盘测试**

Append to `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
    [Fact]
    public void Chat_without_session_does_not_create_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_test",
            Text: "fake model output")));
        FakeConversationStore store = new();

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "hello model"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.Null(store.LoadedSessionName);
        Assert.Null(store.SavedSessionName);
        Assert.Null(store.SavedTranscript);
    }
```

- [ ] **Step 4: 更新 FakeChatModelClient 和添加 FakeConversationStore**

Modify the existing `FakeChatModelClient` in `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
    private sealed class FakeChatModelClient(ChatModelResult result) : IChatModelClient
    {
        public string? LastNonStreamingPrompt { get; private set; }
        public string? LastStreamingPrompt { get; private set; }
        public ChatRequest? LastRequest { get; private set; }

        public ChatModelResult Send(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastNonStreamingPrompt = request.Prompt;
            return result;
        }

        public ChatModelResult SendStreaming(
            ChatRequest request,
            IChatStreamingRenderer renderer,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
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

Add this fake below it:

```csharp
    private sealed class FakeConversationStore : IConversationStore
    {
        public ConversationSessionName? LoadedSessionName { get; private set; }
        public ConversationSessionName? SavedSessionName { get; private set; }
        public ConversationTranscript? SavedTranscript { get; private set; }
        public ConversationTranscript Transcript { get; init; } = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

        public ConversationTranscript LoadOrCreate(ConversationSessionName sessionName, DateTimeOffset nowUtc)
        {
            LoadedSessionName = sessionName;
            return Transcript;
        }

        public string Save(ConversationSessionName sessionName, ConversationTranscript transcript)
        {
            SavedSessionName = sessionName;
            SavedTranscript = transcript;
            return Path.Combine("user-home", ".caicli", "sessions", $"{sessionName.FileSafeName}.transcript.json");
        }
    }
```

- [ ] **Step 5: 运行 CLI 测试并确认失败**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected: FAIL，因为 `CliCommandFactory.Create(...)` 还没有 conversation store 注入，也没有 `--session` option。

- [ ] **Step 6: 修改 CliCommandFactory overloads**

Update `src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs` so the full overload accepts store and clock factories:

```csharp
    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory)
    {
        return Create(
            output,
            snapshotProvider,
            commandLogger,
            chatModelClientFactory,
            streamingRendererFactory,
            FileConversationStore.Create,
            () => DateTimeOffset.UtcNow);
    }

    public static RootCommand Create(
        TextWriter output,
        Func<string?, CliEnvironmentSnapshot> snapshotProvider,
        Action<string, CliEnvironmentSnapshot> commandLogger,
        Func<CliEnvironmentSnapshot, IChatModelClient> chatModelClientFactory,
        Func<TextWriter, IChatStreamingRenderer> streamingRendererFactory,
        Func<CliEnvironmentSnapshot, IConversationStore> conversationStoreFactory,
        Func<DateTimeOffset> utcNowProvider)
```

At the start of the full overload, add:

```csharp
        ArgumentNullException.ThrowIfNull(conversationStoreFactory);
        ArgumentNullException.ThrowIfNull(utcNowProvider);
```

- [ ] **Step 7: 添加 --session option 并记录 transcript**

In the chat command setup, add:

```csharp
        Option<string> sessionOption = new("--session")
        {
            Description = "Resume or create a named chat session.",
        };
        chatCommand.Options.Add(sessionOption);
```

Replace the chat action body with:

```csharp
        chatCommand.SetAction(parseResult =>
        {
            string? workspacePath = parseResult.GetValue(workspaceOption);
            string prompt = parseResult.GetValue(promptArgument) ?? string.Empty;
            string? session = parseResult.GetValue(sessionOption);
            CliEnvironmentSnapshot snapshot = snapshotProvider(workspacePath);
            TryWriteCommandLog(commandLogger, "chat", snapshot);

            IChatModelClient chatModelClient = chatModelClientFactory(snapshot);
            IChatStreamingRenderer renderer = streamingRendererFactory(output);
            ChatRequest request = new(prompt, session);
            ConversationSessionName? sessionName = string.IsNullOrWhiteSpace(session)
                ? null
                : ConversationSessionName.Parse(session);
            IConversationStore? conversationStore = sessionName is null
                ? null
                : conversationStoreFactory(snapshot);
            DateTimeOffset nowUtc = utcNowProvider();
            ConversationTranscript? transcript = sessionName is null
                ? null
                : conversationStore!.LoadOrCreate(sessionName, nowUtc);

            ChatModelResult result = chatModelClient.SendStreaming(request, renderer);

            if (sessionName is not null && transcript is not null && conversationStore is not null)
            {
                ConversationTranscriptRecorder.RecordTurn(transcript, prompt, result, nowUtc);
                conversationStore.Save(sessionName, transcript);
            }

            return result.IsSuccess ? 0 : 1;
        });
```

- [ ] **Step 8: 运行 CLI 测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter CliCommandFactoryTests
```

Expected: PASS。

- [ ] **Step 9: Commit**

```powershell
git add src/CSharpAiCli.Cli/Commands/CliCommandFactory.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "feat: wire chat sessions into cli"
```

---

## Task 7: 覆盖真实文件恢复追加路径

**Files:**

- Modify: `src/CSharpAiCli.Tests/FileConversationStoreTests.cs`
- Modify: `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`

- [ ] **Step 1: 添加 store 追加持久化测试**

Append to `src/CSharpAiCli.Tests/FileConversationStoreTests.cs`:

```csharp
    [Fact]
    public void Save_after_restore_preserves_existing_messages_and_appends_new_turn()
    {
        using TempDirectory temp = TempDirectory.Create();
        FileConversationStore store = new(Path.Combine(temp.Path, ".caicli", "sessions"));
        ConversationSessionName sessionName = ConversationSessionName.Parse("smoke");
        ConversationTranscript transcript = ConversationTranscript.Create(
            sessionName.Value,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        transcript.AddUserMessage("first", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        store.Save(sessionName, transcript);

        ConversationTranscript restored = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        restored.AddUserMessage("second", DateTimeOffset.Parse("2024-01-01T00:01:01Z"));
        store.Save(sessionName, restored);

        ConversationTranscript loadedAgain = store.LoadOrCreate(
            sessionName,
            DateTimeOffset.Parse("2024-01-01T00:02:00Z"));

        Assert.Equal(["first", "second"], loadedAgain.Messages.Select(message => message.Content).ToArray());
    }
```

- [ ] **Step 2: 添加 CLI 恢复已有 transcript 测试**

Append to `src/CSharpAiCli.Tests/CliCommandFactoryTests.cs`:

```csharp
    [Fact]
    public void Chat_session_appends_to_existing_transcript()
    {
        using StringWriter output = new();
        FakeChatModelClient chatClient = new(ChatModelResult.Success(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_second",
            Text: "second response")));
        ConversationTranscript existing = ConversationTranscript.Create(
            "smoke",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        existing.AddUserMessage("first prompt", DateTimeOffset.Parse("2024-01-01T00:00:01Z"));
        existing.AddAssistantMessage(new ChatResponse(
            Provider: "openai",
            Model: "gpt-test",
            ResponseId: "resp_first",
            Text: "first response"), DateTimeOffset.Parse("2024-01-01T00:00:02Z"));
        FakeConversationStore store = new()
        {
            Transcript = existing
        };

        int exitCode = CliCommandFactory
            .Create(
                output,
                workspacePath => CreateSnapshot(workspacePath, apiKey: "sk-test", apiKeySource: "OPENAI_API_KEY", model: "gpt-test"),
                (_, _) => { },
                _ => chatClient,
                writer => new TerminalChatStreamingRenderer(writer),
                _ => store,
                () => DateTimeOffset.Parse("2024-01-01T00:00:05Z"))
            .Parse(["chat", "--session", "smoke", "second prompt"])
            .Invoke();

        Assert.Equal(0, exitCode);
        Assert.NotNull(store.SavedTranscript);
        Assert.Equal(
            ["first prompt", "first response", "second prompt", "second response"],
            store.SavedTranscript.Messages.Select(message => message.Content).ToArray());
    }
```

- [ ] **Step 3: 运行恢复追加测试**

Run:

```powershell
dotnet test src/CSharpAiCli.sln --filter "FileConversationStoreTests|CliCommandFactoryTests"
```

Expected: PASS。

- [ ] **Step 4: Commit**

```powershell
git add src/CSharpAiCli.Tests/FileConversationStoreTests.cs src/CSharpAiCli.Tests/CliCommandFactoryTests.cs
git commit -m "test: cover session transcript append"
```

---

## Task 8: 补充文档和 smoke 验证

**Files:**

- Modify: `docs_md/spec/model_client_responses_api.md`
- Modify: `docs_md/weekly/26_week_goal_schedule.md`
- Create: `docs_md/weekly/07_week_review.md`

- [ ] **Step 1: 更新 Week 5/6 spec 的 session/transcript 说明**

Append this section to `docs_md/spec/model_client_responses_api.md`:

```markdown
## Week 7 session and transcript behavior

- `caicli chat --session <name> "<prompt>"` resumes or creates a named transcript.
- Session transcripts are stored under `<user profile>/.caicli/sessions/<safe-session-name>.transcript.json`.
- Transcript JSON uses `schemaVersion: 1`.
- Successful turns append one `user` message and one `assistant` message.
- Failed turns append one `user` message and one safe error entry.
- `toolCalls` is present as an empty array in Week 7 and reserved for Week 9 tool-call recording.
- Week 7 does not send historical transcript messages back to the model; model requests still use the current prompt only.
- Without `--session`, `chat` keeps the Week 6 streaming behavior and does not create a transcript.
- Transcript files must not contain raw API keys.
```

- [ ] **Step 2: 运行 build/test**

Run:

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded
Failed: 0
```

- [ ] **Step 3: 运行缺 key session smoke**

Use an isolated user profile and temporary workspace so this smoke does not touch real user config:

```powershell
$tempRoot = Join-Path $env:TEMP ("caicli-week7-" + [Guid]::NewGuid().ToString("N"))
$workspace = Join-Path $tempRoot "workspace"
$userProfile = Join-Path $tempRoot "user"
New-Item -ItemType Directory -Force -Path (Join-Path $workspace ".caicli") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $userProfile ".caicli") | Out-Null
@{ model = "gpt-test" } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $workspace ".caicli/config.json")
$env:OPENAI_API_KEY = $null
$oldUserProfile = $env:USERPROFILE
$env:USERPROFILE = $userProfile
try {
    dotnet run --project src/CSharpAiCli.Cli -- chat --workspace $workspace --session smoke "Reply with OK."
}
finally {
    $env:USERPROFILE = $oldUserProfile
}
$transcript = Join-Path $userProfile ".caicli/sessions/smoke.transcript.json"
Get-Content -Raw -LiteralPath $transcript
```

Expected:

```text
exit code: 1
output contains: status: failed
output contains: localErrorCode: missing-openai-api-key
transcript exists
transcript contains: "schemaVersion": 1
transcript contains: "sessionName": "smoke"
transcript contains: "role": "user"
transcript contains: "localErrorCode": "missing-openai-api-key"
transcript does not contain: sk-
```

If PowerShell keeps the real user profile despite `$env:USERPROFILE`, run this smoke through tests only and document that `CliEnvironmentSnapshot.Create(userProfile: ...)` is the deterministic isolation path.

- [ ] **Step 4: 可选真实 streaming session smoke**

Only run when a real `OPENAI_API_KEY` is available and a model is configured:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --session smoke "Reply with OK."
```

Expected:

```text
exit code: 0
output contains: status: streaming
output contains: status: completed
<user profile>/.caicli/sessions/smoke.transcript.json exists
transcript contains a user message and an assistant message
transcript does not contain raw API key values
```

- [ ] **Step 5: 创建 Week 7 review**

Create `docs_md/weekly/07_week_review.md`:

```markdown
# 第 07 周回顾

状态：已稳固

已完成：

- 添加 `chat --session <name> "<prompt>"` 命名会话入口。
- 添加 `ConversationSessionName`，阻止路径穿越和不安全文件名。
- 添加 transcript v1 领域模型，包含 `schemaVersion`、`sessionName`、`createdAtUtc`、`updatedAtUtc`、`messages`、`toolCalls` 和 `errors`。
- 添加用户 profile 下的 JSON file conversation store。
- 成功 chat turn 会写入 user message 和 assistant message。
- 失败 chat turn 会写入 user message 和安全 error。
- 无 `--session` 时保持 Week 6 streaming 行为，不创建 transcript。
- `toolCalls` 在 Week 7 保持空数组，作为 Week 9 工具调用记录占位。

验证：

- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：
- 命令：缺 key session smoke
- 结果：
- 命令：真实 streaming session smoke
- 结果：

运行时说明：

- 当前目标框架仍为 `net9.0`。
- 本地 .NET SDK 仍为 `9.0.308`。
- Week 7 会恢复 transcript 文件，但不会把历史消息发送给模型。

风险：

- instruction loader 和完整配置 schema 收紧尚未完成，归属第 8 周。
- 真实多轮上下文发送尚未实现；当前只持久化历史。
- tool call、reasoning、annotation、MCP 和 image generation events 仍未进入 transcript。

第 08 周输入：

- 添加 instruction loader。
- 收紧配置优先级和 schema。
- 补全密钥遮蔽规则。
- 加固模型错误处理和阶段 02 验收清单。
```

- [ ] **Step 6: 更新总排期 Week 7 状态**

In `docs_md/weekly/26_week_goal_schedule.md`, update the Week 7 row after verification:

```markdown
| 7 | 2024-07-15 至 2024-07-21 | 阶段 02 | 已稳固 | 添加会话存储、版本化转录格式和工具调用初始 schema。详见 `07_week_session_transcript.plan.md` 和 `07_week_review.md`。 | 已验证：`chat --session <name> "<prompt>"` 可以恢复命名会话，并写入可检查 transcript v1。 |
```

Also add a current progress bullet:

```markdown
- 第 7 周已稳固：命名会话恢复、用户 profile transcript v1 和工具调用 schema 占位已完成。详见 `07_week_review.md`。
```

- [ ] **Step 7: Commit**

```powershell
git add docs_md/spec/model_client_responses_api.md docs_md/weekly/26_week_goal_schedule.md docs_md/weekly/07_week_review.md
git commit -m "docs: record week seven session progress"
```

---

## 最终验证

Run:

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
```

Expected:

```text
Build succeeded
Failed: 0
```

Run help smoke:

```powershell
dotnet run --project src/CSharpAiCli.Cli -- chat --help
```

Expected:

```text
help includes --session
```

Run deterministic no-key transcript smoke through tests or an isolated profile. Expected transcript:

```json
{
  "schemaVersion": 1,
  "sessionName": "smoke",
  "messages": [
    {
      "role": "user"
    }
  ],
  "toolCalls": [],
  "errors": [
    {
      "localErrorCode": "missing-openai-api-key"
    }
  ]
}
```

Do not mark Week 7 stable until:

- `dotnet build` passes.
- `dotnet test` passes.
- `chat --help` shows `--session`.
- A named session transcript can be created and restored.
- Transcript JSON contains `toolCalls: []`.
- Transcript JSON does not contain raw API key values.

## 风险与保护边界

- If transcript load fails because JSON is invalid or schemaVersion is unsupported, fail fast with a safe local error in the CLI rather than overwriting the file. If this path is implemented in Week 7, add a focused test before changing behavior.
- Session names are file names. Keep validation strict in Week 7; do not silently map path separators.
- Do not pass transcript history into the OpenAI request in Week 7. That behavior changes model semantics and should be planned separately.
- Keep transcript writes after model result is known so failed validation paths can still record safe errors without implying assistant output.
- Keep all transcript error text sourced from `ModelError.SafeMessage`.
