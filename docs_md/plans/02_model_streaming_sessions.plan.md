# 阶段 02 - 模型流式输出与会话计划

## 状态

`Accepted`

## 目标

添加真实模型调用、终端流式输出、会话持久化、转录存储，以及不依赖 Microsoft Agent Framework 也能工作的直接 OpenAI SDK agent runner。

## 目标周数

第 5-8 周

## 范围

创建：

- chat model client 抽象
- OpenAI .NET SDK 实现，主路径采用 Responses API
- 流式渲染器
- 会话存储
- 转录格式
- token 和费用元数据占位
- 基础 prompt/instruction 加载器
- 模型错误格式
- tool call/result 转录占位 schema

## 架构

CLI 应调用 `IChatModelClient` 或 `IAgentRunner` 接口。OpenAI SDK 实现可以是提供商专用的，但应用其余部分应保持提供商中立。

必需抽象：

```text
IChatModelClient
IAgentRunner
IConversationStore
IInstructionLoader
IStreamingRenderer
```

## Schema 决策

配置加载优先级从高到低为：

1. 当前进程环境变量。
2. 用户配置文件。
3. 工作区配置文件。
4. 内置默认值。

密钥只能来自环境变量或用户配置文件；工作区配置不得保存密钥。`config get`、日志和转录必须遮蔽任何疑似密钥值。

转录文件使用版本化 JSON 格式，最低字段为：

```json
{
  "schemaVersion": 1,
  "sessionName": "smoke",
  "createdAtUtc": "2024-01-01T00:00:00Z",
  "updatedAtUtc": "2024-01-01T00:00:00Z",
  "messages": [],
  "toolCalls": [],
  "errors": []
}
```

阶段 02 只写入普通消息和错误。`toolCalls` 保留为空数组，供阶段 03 记录工具调用请求、审批结果、工具输出摘要和失败原因。

模型错误格式必须包含：

```text
provider
operation
statusCode 或 localErrorCode
safeMessage
retryable
```

## 必需行为

- `caicli chat` 将用户消息发送给模型。
- 模型输出在终端中流式显示。
- `caicli chat --session <name>` 恢复命名会话。
- 会话转录存储在用户数据目录下。
- 从约定的本地文件加载工作区指令，例如 `AICLI.md`。
- 密钥只从环境变量或用户配置读取，绝不提交到仓库。
- 模型调用失败时显示有用错误。
- 缺少 API key 时不得创建空转录或写入误导性的成功记录。

## 验收标准

1. Chat 命令可以流式输出模型响应。
2. 会话恢复可用。
3. 转录文件足够确定，便于检查。
4. 缺少 API key 时，`doctor` 给出清晰警告，chat 给出清晰错误。
5. 单元测试覆盖会话存储和指令加载。
6. 直接 OpenAI SDK runner 在没有 Microsoft Agent Framework 时可用。
7. 配置优先级、密钥遮蔽和模型错误格式有测试覆盖。
8. 转录 schema 包含后续工具调用记录需要的稳定占位字段。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- doctor
dotnet run --project src/CSharpAiCli.Cli -- chat --session smoke
```

## 风险与保护边界

- 暂不实现工具调用。
- 不自动发送完整工作区内容。
- 从日志和转录中遮蔽密钥。
- 从一开始就给转录 schema 加版本。

## 阶段交付物

- 直接模型客户端：已完成
- 流式输出：已完成
- 持久化会话：已完成
- 指令加载：已完成
- 配置优先级和 workspace `apiKey` 禁用：已完成
- 阶段 02 验收：第 8 周已通过

## 下一阶段输入

阶段 03 将使用模型流式输出和会话能力，添加安全工具执行、文件读取、patch 编辑和命令执行。
