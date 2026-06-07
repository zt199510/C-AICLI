# Week 16 review

状态：已稳固

已完成：
- 扩展配置模型，新增 `agentBackend` / `AgentBackend`，支持 `direct` 和 `framework`。
- 添加 backend resolver，优先级为 `CAICLI_AGENT_BACKEND` > user config > workspace config > default `direct`。
- 支持 `maf`、`framework`、`agent-framework` 归一化为 `framework`，`openai` 归一化为 `direct`。
- `config get` 输出 `agentBackend` 和 `agentBackendSource`。
- `doctor` 输出 backend 状态；`framework` 当前解释为 adapter experimental stub / framework package 未启用，`direct` 为 available。
- 添加测试覆盖 backend 优先级、workspace backend、非法 backend warning、config report 和 doctor framework unavailable 诊断。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，201 tests passed。

运行时说明：
- `framework` 可以被配置选择，但当前不会替代 direct 后端；doctor 会明确显示 unavailable 原因。
- direct 后端仍是默认值，不需要配置即可继续可用。
- workspace config 可设置 `agentBackend`，但 workspace `apiKey` 仍继续被禁用。

风险：
- 尚未实现生产 `run` 命令的 backend switch 接线。
- framework adapter 仍是 experimental stub，未接入真实 Microsoft Agent Framework SDK。

第 17 周输入：
- 回归 direct/offline 和 framework stub 后端边界。
- 阶段 04 验收时应清晰标注 framework 后端是否 accepted 或 deferred。
