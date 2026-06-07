# Week 14 review

状态：已稳固

已完成：
- 确认产品内核 `IAgentRunner` 位于 `CSharpAiCli.Core`，direct/offline runner 不依赖 framework 类型。
- 新增 `CSharpAiCli.AgentFramework` adapter 项目，并加入 solution。
- 添加 `MicrosoftAgentFrameworkRunner` 实验性 stub，实现 `IAgentRunner`，在 framework 包未启用时返回安全错误。
- 添加 `MicrosoftToolBridge`，复用 Core 的 `IToolRegistry` 和 `ToolDefinition`，避免重复工具注册表。
- 添加 `AgentFrameworkAdapterInfo` 和 capability report，明确当前状态为 `experimental-stub`。
- 添加边界测试确认 adapter 构建成功、tool bridge 复用 core registry、CLI 项目不引用 adapter。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，194 tests passed。

运行时说明：
- 当前未引入 Microsoft Agent Framework 预览包；adapter 只作为可构建骨架存在。
- `CSharpAiCli.Cli` 未引用 `CSharpAiCli.AgentFramework`，CLI 命令代码不暴露 framework 类型。
- direct/offline runner 和阶段 03 工具链保持可用。

风险：
- Framework 后端尚不可用，当前 capability report 标记为 `experimental-stub`。
- 后续第 15-17 周若引入真实 framework 包，仍需保持失败可回退到 direct 后端。

第 15 周输入：
- 将 Core 工具注册表桥接到 framework adapter 层。
- 若 framework 包仍不可用，继续以 adapter 内 stub/smoke 形式验证边界，不阻塞 direct 后端。
