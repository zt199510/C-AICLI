# 第 06 周回顾

状态：已稳固

已完成：

- 添加 provider-neutral `IChatStreamingRenderer`。
- 添加 `TerminalChatStreamingRenderer`，支持开始、delta、完成和失败输出。
- 添加 OpenAI Responses streaming gateway update 形状。
- 将 `OpenAiResponsesModelClient` 扩展为 `SendStreaming(...)`。
- 将 `caicli chat "<prompt>"` 默认切换到 streaming path。
- 保持缺 API key、缺 model、空 prompt、workspace config `apiKey` 禁用和 SDK failure 的安全错误行为。
- 保持 chat 输出和命令日志不打印原始 API key。

验证：

- 命令：`dotnet build src/CSharpAiCli.sln`
- 结果：Build succeeded，0 warnings，0 errors
- 命令：`dotnet test src/CSharpAiCli.sln`
- 结果：Failed: 0，Passed: 68，Skipped: 0
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace <temporary workspace with model and no apiKey> "Reply with OK."`，无 `OPENAI_API_KEY` 且隔离 user profile
- 结果：返回 exit code 1，输出 `status: failed` 和 `localErrorCode: missing-openai-api-key`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace <temporary workspace without model> "Reply with OK."`，无 `OPENAI_API_KEY` 且隔离 user profile
- 结果：返回 exit code 1，输出 `localErrorCode: missing-model`
- 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace <temporary workspace with workspace apiKey> "Reply with OK."`，无 `OPENAI_API_KEY` 且隔离 user profile
- 结果：返回 exit code 1，输出 `localErrorCode: unsupported-api-key-source`，不包含测试密钥值
- 命令：命令日志脱敏 smoke
- 结果：日志包含 `command=chat` 和 API key 状态，不包含 `sk-` 密钥值
- 命令：真实 streaming smoke
- 结果：未运行；当前环境没有 `OPENAI_API_KEY`，当前 workspace 也没有配置 model

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
- 真实 streaming smoke 需要具备真实 `OPENAI_API_KEY` 和配置 model 的开发者环境后补跑。

第 07 周输入：

- 添加会话存储。
- 添加版本化 transcript 格式。
- 添加命名会话恢复。
- 为后续工具调用保留 transcript schema 占位字段。
