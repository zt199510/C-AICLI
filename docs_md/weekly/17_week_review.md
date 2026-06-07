# Week 17 review

状态：已验收

已完成：
- 添加 `Phase04AcceptanceTests`，回归 direct/offline runner 仍可通过 Core 工具链执行。
- 验证 framework adapter runner 在未启用真实 package 时返回结构化安全错误 `agent-framework-unavailable`。
- 验证 `MicrosoftToolBridge` 可复用同一 Core tool registry，不复制工具注册表。
- 验证 `agentBackend=framework` 可被配置选择，且 doctor 明确说明 experimental stub / package 未启用。
- 阶段 04 文档标记为 `Deferred`：adapter 边界、配置和诊断已验收；真实 Microsoft Agent Framework 后端作为增强目标未启用。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，205 tests passed。

运行时说明：
- Direct/offline backend 保持可用并继续作为 MVP 路径。
- Framework backend 当前是 experimental stub，不进入首版发布硬门槛。
- Core/CLI 不依赖 Microsoft Agent Framework 类型或预览包。

风险：
- 真实 framework SDK 未接入，后续恢复条件是选择稳定 package 并只在 adapter 项目内映射。
- `config set` 和生产 `run` 命令尚未实现，backend switch 目前通过 config loader/report/doctor 验证。

第 18 周输入：
- 进入阶段 05 MCP 配置与列表命令。
- 保持 MCP 作为增强目标，不阻塞 direct/offline MVP。
