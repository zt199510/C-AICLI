# Week 05 review

状态：已稳固

## 已完成

- provider-neutral `IChatModelClient`。
- chat request/response/result/error/report types。
- OpenAI Responses API SDK adapter 和可测试的 gateway fake path。
- `caicli chat "<prompt>"` 已接入一次性非流式模型调用。
- 缺 API key、缺 model、空 prompt、SDK failure、空模型响应和 cancellation 均有安全错误。
- workspace config `apiKey` 不用于真实模型调用。
- `CSharpAiCli.Cli` 和 `CSharpAiCli.Core` 的文件位置已按职责整理：CLI 命令接线放入 `Commands/`，Core 按 chat、configuration、diagnostics、OpenAI model client、product 和 workspace 分区。

## 验证

- Build
  - 命令：`dotnet build src/CSharpAiCli.sln`
  - 结果：succeeded, 0 warnings, 0 errors.
- Full tests
  - 命令：`dotnet test src/CSharpAiCli.sln`
  - 结果：passed, Failed: 0, Passed: 52, Skipped: 0, Total: 52.
- Source layout check
  - 命令：`dotnet build src/CSharpAiCli.sln`；`dotnet test src/CSharpAiCli.sln`
  - 结果：移动源码文件后 build succeeded；tests passed, Failed: 0, Passed: 52, Skipped: 0, Total: 52.
- 当前 repo workspace 缺 key / 缺 model smoke
  - 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace . "Reply with OK."`
  - 环境：无 `OPENAI_API_KEY`，无 configured model。
  - 结果：exit code 1；输出包含 `status: failed` 和 `localErrorCode: missing-model`。
- 临时 workspace 配置 model、缺 key smoke
  - 临时 `.caicli/config.json`：`{ "model": "gpt-test" }`；无 `OPENAI_API_KEY`。
  - 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace <tempRoot> "Reply with OK."`
  - 结果：exit code 1；输出包含 `status: failed` 和 `localErrorCode: missing-openai-api-key`；输出不包含 API key value。
- Workspace config key rejection smoke
  - 临时 `.caicli/config.json`：`{ "model": "gpt-test", "apiKey": "sk-workspace-secret-smoke" }`；无 `OPENAI_API_KEY`。
  - 命令：`dotnet run --project src/CSharpAiCli.Cli -- chat --workspace <tempRoot> "Reply with OK."`
  - 结果：exit code 1；输出包含 `localErrorCode: unsupported-api-key-source`；输出不包含 `sk-workspace-secret-smoke`。
- Real model smoke
  - 未运行：当前 developer environment 没有 `OPENAI_API_KEY`。
- Command log smoke
  - 读取 log path：`.caicli/logs/2026-06-04.log`。
  - 结果：log 包含 `command=chat` 和 `apiKey=missing` / `apiKey=present` status entries；未出现 raw API key values。

## 运行时说明

- Target framework：`net9.0`。
- 本地 .NET SDK：`9.0.308`。
- OpenAI package：`OpenAI` `2.10.0`。
- Week 5 是一次性非流式 Responses API 调用。
- Unit tests 使用 fake gateway，不进行真实网络调用。
- 本次仅规范物理文件位置，公共 namespace 仍保持 `CSharpAiCli.Cli` 和 `CSharpAiCli.Core`，避免影响调用方。

## 风险与剩余范围

- Streaming output 仍在 Week 6。
- Sessions/transcripts 仍在 Week 7。
- 完整 config precedence/schema cleanup 仍在 Week 8。
- 目前尚未提供 structured JSON output。
- Real model smoke 仍需在具备真实 `OPENAI_API_KEY` 和 configured model 的环境中运行。

## Week 6 输入

- chat 输出的 terminal streaming renderer。
- 将 OpenAI Responses call 转为 streaming path。
- 保持当前缺 key/model 和 redaction 行为。
