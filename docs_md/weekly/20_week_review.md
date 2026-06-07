# Week 20 review

状态：已稳固

已完成：
- 添加 `IMcpToolInvoker`、`McpToolRequest` 和 `UnavailableMcpToolInvoker`。
- 添加 `McpToolBridge`，将 enabled/configured MCP servers 映射为通用 `ITool`。
- 添加 `McpExternalTool`，工具名形如 `mcp.<server>.call`。
- disabled 和 invalid MCP servers 不注册到 `IToolRegistry`，因此不可执行。
- 添加 fake MCP invoker 测试，覆盖工具注册、调用、disabled server 拒绝和 transcript tool call 记录。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，221 tests passed。

运行时说明：
- 当前 MCP tool invocation 是 bridge/stub 层，真实 MCP 协议调用仍未接入。
- `UnavailableMcpToolInvoker` 默认返回安全失败，避免误以为真实连接可用。
- MCP 工具结果通过现有 `OfflineAgentRunner` 写入 transcript。

风险：
- 真实 MCP tool discovery/schema 映射仍待后续扩展。
- 当前每个 server 只映射为一个 generic call tool。

第 21 周输入：
- 添加项目工作流注册表和验证 profiles。
- workflow profile 中的验证命令仍必须走第 12 周 shell approval。
