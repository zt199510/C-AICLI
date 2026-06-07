# Week 21 review

状态：已稳固

已完成：
- 添加 workflow profile 配置模型：`WorkflowProfileConfig`、`WorkflowProfile`、`WorkflowConfiguration`。
- `CliConfigFile` 支持 `workflowProfiles`，`ConfigLoader` 保留 workflow profile config source。
- 添加 `WorkflowProfileLoader` 和 `WorkflowRegistry`。
- 添加 `WorkflowListReport` 和 `WorkflowValidateReport`。
- CLI 增加 `workflow list` 和 `workflow validate <profile>` 命令骨架。
- `workflow validate` 只建议验证命令并标记 `execution: not run`，实际执行仍需要后续审批 shell runner。
- 添加测试覆盖 profile override、路径来源来自 profile 或 `--workspace`、validate requiresApproval、CLI 输出。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，229 tests passed。

运行时说明：
- validation profile 中的命令不会直接执行。
- workspace path 优先来自 profile；未配置时使用 `--workspace` / 当前 workspace。
- 工作流仍是通用 registry，未引入 Gerber/TIFF 专用包。

风险：
- `workflow validate` 当前只是建议命令，尚未与审批 shell 执行整合为完整运行命令。
- profile schema 仍是 MVP，尚未支持多个验证命令或环境变量。

第 22 周输入：
- 添加 Gerber/TIFF workflow pack。
- 阶段 05 验收时应清晰标注 MCP/项目包作为增强目标的可用边界。
