# Week 08 review

状态：已验收

已完成：
- 创建第 8-26 周任务计划文件，并把后续阶段拆成可执行周计划骨架。
- 添加 `WorkspaceInstructionLoader`，从工作区根目录读取 `AICLI.md`，并对空文件、缺失文件、超大文件和缺失 workspace 做安全处理。
- `chat` 会把 workspace instructions 放入 `ChatRequest.Instructions`，OpenAI Responses gateway 会把它作为 developer message item 传给模型。
- 配置优先级收口为 `OPENAI_MODEL` > 用户配置 `model` > 工作区配置 `model` > default。
- API key 来源收口为 `OPENAI_API_KEY` > 用户配置 `apiKey` > missing；workspace config `apiKey` 只记录 warning，不进入 effective config。
- `doctor`、`config get`、日志和 chat error 继续只输出安全状态/安全 warning，不输出原始 key。
- 阶段 02 文档和 spec 已同步为 Accepted。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，121 tests passed。
- 命令：`dotnet src\CSharpAiCli.Cli\bin\Debug\net9.0\CSharpAiCli.Cli.dll config get --workspace <temporary workspace with model, workspace apiKey, AICLI.md>`
- 结果：输出 `modelSource: workspace config`、`apiKey: missing`、`apiKeySource: missing` 和 `configWarning: ignored workspace config apiKey`，不输出 `sk-week8-workspace-secret`。
- 命令：`dotnet src\CSharpAiCli.Cli\bin\Debug\net9.0\CSharpAiCli.Cli.dll chat --workspace <temporary workspace with model, workspace apiKey, AICLI.md> "Reply OK"`
- 结果：输出 `localErrorCode: missing-openai-api-key`，不输出 workspace key 或 instruction 内容。
- 命令：`dotnet src\CSharpAiCli.Cli\bin\Debug\net9.0\CSharpAiCli.Cli.dll doctor --workspace <temporary workspace with oversized AICLI.md>`
- 结果：输出 `instruction warning: ignored instruction file over 65536 bytes`，不输出 instruction 文件内容。

运行时说明：
- 当前项目仍为 `net9.0`。
- `AICLI.md` 指令不会写入 transcript user message；Week 8 仍不把历史 transcript 发送给模型。
- workspace config `apiKey` 从第 8 周起不再显示 `apiKey: present`；旧 Week 3 行为已被阶段 02 安全策略替换。

风险：
- `SdkOpenAiResponsesGateway` 使用 OpenAI .NET SDK 的 Responses 预览类型，仍受 `OPENAI001` 保护。
- 目前只有本地 `AICLI.md` 单文件 instruction；多文件指令、层级合并和指令冲突处理未实现。
- 阶段 03 尚未开始，真实工具调用、文件读取、patch 和 shell runner 仍不可用。

第 9 周输入：
- 添加工具注册表、工具执行器和工具调用 transcript schema。
- 用 fake model + fake test tool 完成离线 agent loop。
- 保持阶段 02 的 workspace key 禁用、instruction loading 和 transcript 安全边界。
