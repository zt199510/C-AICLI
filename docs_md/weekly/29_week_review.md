## 第 29 周回顾

状态：已稳固

已完成：
- 新增 agent run loop 核心契约：`AgentRunRequest`、`AgentRunResult`、`AgentRunEvent`、`IAgentRunner`、`AgentRunLimits`。
- `OfflineAgentRunner` 支持模型 turn、工具调用、工具结果回写、final response 和结构化 agent error event。
- 工具 registry schema 可映射为 OpenAI tool definition，并修正参数 schema `JsonDocument` 生命周期。
- 新增 OpenAI 响应解析与工具调用模型契约，覆盖 final text、tool call、arguments、tool results writeback。
- `exec` 已路由到 `IAgentRunner`，并保留 `run` 作为 deterministic direct-tool 兼容/smoke 入口。
- `exec` 支持 `--max-turns`、`--max-tool-calls`、`--timeout-seconds`、`--session`。
- `exec --session` 会加载/保存 transcript，并记录 agent loop 工具调用与结果摘要。
- `exec` text/JSON 输出使用 agent event 流，覆盖 `model.turn`、`tool.call`、`tool.result`、`final.response`、`agent.error`。
- 补齐 fake model loop 测试：单工具、多工具、工具失败、轮数上限、工具调用上限和超时路径。
- 文档已更新：`exec` 进入 agentic v1 surface/contract，`run` 仍是 deterministic compatibility/smoke path。
- 使用 Subagent-Driven execution，并对 Step 6、7、8、9 进行 spec review 和 quality review。

验证：
- 命令：`dotnet test C:\Users\10335\.config\superpowers\worktrees\C-AICLI\week-29-agent-run-loop-v1\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj`
- 结果：通过，398 passed，0 failed，0 skipped。
- 命令：`dotnet build C:\Users\10335\.config\superpowers\worktrees\C-AICLI\week-29-agent-run-loop-v1\src\CSharpAiCli.Cli\CSharpAiCli.Cli.csproj`
- 结果：通过，0 warnings，0 errors。
- 说明：从 worktree 内直接运行 `dotnet test` 会受 root `global.json` 的 SDK `9.0.308` pin 影响；本轮验证使用 `C:\Users\10335` 工作目录和绝对 csproj 路径完成。

运行时说明：
- `exec` 当前是 agentic v1 surface/contract：通过 `IAgentRunner` 输出模型/工具/final/error event，并接受 loop limit 和 transcript 参数。
- fake/offline agent loop 已可驱动 workspace 工具调用并生成 final response。
- 默认 direct OpenAI SDK gateway 的真实工具调用 continuation 仍未启用；当配置 model/API key/source 后，默认 `exec` 会返回清晰的 `agent-backend-unavailable`。
- 无 model、无 API key、unsupported API key source 都返回结构化 agent error，且不输出 secret。
- `run` 仍用于成功的 deterministic local smoke task，例如 `run --workspace . --approve "create smoke note"`。

风险：
- `SdkOpenAiResponsesGateway.CreateAgentResponse` 仍需实现真实 SDK tool calls / tool results 转译，否则 direct OpenAI agent loop 只能停留在 surface/contract 和 offline/fake coverage。
- `exec --session` 目前记录工具调用与结果摘要，不把 exec prompt/final response 写入 chat-style messages。
- 默认 `exec` 异常 passthrough 仅对 `exec` 收窄启用；非 exec 命令保留 System.CommandLine 默认异常处理。
- 文档已标明真实 SDK continuation Deferred，但后续验收若要求 real OpenAI loop，需要继续推进 gateway 支持。

第 30 周输入：
- 实现真实 direct OpenAI SDK agent tool-call continuation，消除 `agent-backend-unavailable` 默认路径。
- 为 agentic `exec` 增加审批/权限 profile，并将审批结果稳定进入 text/JSON event。
- 继续扩展 session resume 管理，让 `exec` 能复用/恢复 agent transcript 上下文。
- 为真实 SDK tool loop 增加 smoke/contract 测试，确保 secret、tool arguments 和 tool results 不泄露敏感值。
