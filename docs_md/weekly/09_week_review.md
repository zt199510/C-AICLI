# Week 09 review

状态：已稳固

已完成：
- 添加 provider-neutral 工具抽象：`ITool`、`IToolRegistry`、`ToolDefinition`、`ToolExecutionContext`、`ToolExecutionResult`。
- 添加 `ToolRegistry`，支持工具注册、列出和按名称查找，并拒绝重复工具名。
- 添加 `ToolExecutor`，统一处理未知工具、无效 JSON 参数、安全异常、工具异常和安全错误摘要。
- 将 transcript `toolCalls` 从 Week 7 空数组占位升级为 `ConversationToolCall` schema，记录 call id、工具名、参数 JSON、审批占位、输出摘要、失败原因和错误码。
- 添加最小 `IAgentRunner`、`IToolCallingModel` 和 `OfflineAgentRunner`，支持 fake model + fake tool 的离线工具调用循环。
- 添加单元测试覆盖工具成功、未知工具、参数错误、安全异常、通用异常、工具失败继续循环、循环上限和 transcript JSON schema。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，137 tests passed。

运行时说明：
- 第 9 周只建立工具执行与离线 agent loop 骨架，不接线真实 OpenAI tool calling。
- 第 9 周不实现真实文件读取、搜索、patch 或 shell；这些仍留给第 10-12 周。
- 工具调用记录中的 `approvalStatus` 目前为 `not-required` 占位，真实审批记录将在 patch/shell 周期扩展。

风险：
- `ToolExecutor` 只校验参数是 JSON object，尚未按工具 schema 做结构化校验。
- 离线 runner 是测试用 direct runner 骨架，生产 CLI `run` 命令还未接入。
- 阶段 03 的高风险能力仍未开始：workspace guard、patch approval 和 shell runner。

第 10 周输入：
- 添加工作区路径保护器。
- 添加受控文件读取工具和搜索工具。
- 保持第 9 周工具失败返回与 transcript 记录格式。
