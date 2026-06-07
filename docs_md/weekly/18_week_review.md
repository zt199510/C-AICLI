# Week 18 review

状态：已稳固

已完成：
- 添加 MCP 配置模型：`McpServerConfig`、`McpServerDefinition`、`McpConfiguration`。
- `CliConfigFile` 支持 `mcpServers`，`ConfigLoader` 保留 user/workspace config source 供 MCP loader 合并。
- 添加 `McpConfigurationLoader`，按 user config -> workspace config 顺序加载，workspace 同名 server 覆盖 user。
- 添加 `McpListReport` 和 CLI `mcp list` 命令。
- 禁用 server 输出 `inactive`，不会被视为可连接或可执行。
- 添加测试覆盖 JSON 配置加载、enabled/disabled server、workspace override、空配置、CLI `mcp list` 输出。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，212 tests passed。

运行时说明：
- 第 18 周只加载和列出 MCP 配置，不连接真实 MCP server。
- 未配置 MCP 时 `mcp list` 输出 `servers: none`。
- disabled server 保持 `inactive`。

风险：
- MCP transport 目前只做配置摘要，不做连接或协议握手。
- MCP server env/args 等高级字段尚未建模。

第 19 周输入：
- 添加 MCP 连接管理器和 `mcp doctor`。
- 已配置 servers 的连接诊断应可用，但 disabled servers 仍保持 inactive。
