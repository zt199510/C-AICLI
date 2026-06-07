# Week 15 review

状态：已稳固

已完成：
- 扩展 `MicrosoftToolBridge`，把 Core `ToolDefinition` 映射为 adapter 层 `MicrosoftFrameworkToolDefinition`。
- 添加 `MicrosoftFrameworkToolCall` 和 `MicrosoftFrameworkToolResult` DTO，隔离未来 framework 类型。
- `MicrosoftToolBridge.InvokeTool` 通过 Core `IToolExecutor` 调用工具，保持未知工具、安全拒绝、审批状态和错误码结构化。
- 添加测试验证 read/search 工具 metadata 映射、framework-facing 调用 read/search 工具、失败形状保留。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，197 tests passed。

运行时说明：
- 仍未引入真实 Microsoft Agent Framework 包；桥接层使用 adapter DTO 作为稳定边界。
- Core/CLI 不依赖 adapter DTO。
- 工具执行继续复用第 9-13 周 Core 工具注册表、执行器和安全策略。

风险：
- 真实 framework SDK 的工具 schema 形状可能变化；adapter DTO 后续需要在单独项目内映射。
- 当前 framework backend 仍是 experimental stub，尚不能由真实模型驱动。

第 16 周输入：
- 添加 agent backend 配置开关和诊断。
- doctor/config report 应解释 framework adapter 当前为 experimental stub，direct 后端保持可用。
