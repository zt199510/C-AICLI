# Week 13 review

状态：已验收

已完成：
- 添加 `GitStatusTool` 和 `GitDiffTool`，提供只读 git status/diff 摘要。
- Git 工具测试全部使用临时 git repo，不依赖当前计划仓库。
- 添加 Phase 03 offline acceptance：在离线 agent loop 中串联 read、search、patch、shell、git status 和 git diff。
- Phase 03 安全边界已有测试覆盖：workspace guard、symlink/junction 越界、文件大小/二进制拒绝、patch 审批、目标变化拒绝、shell 审批、危险命令拒绝、cwd 越界、超时和输出截断。
- 阶段 03 计划状态更新为 `Accepted`。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，190 tests passed。

运行时说明：
- Git tools 是只读工具，不会自动 commit。
- Phase 03 仍未接入生产 `run` CLI 命令；当前使用 provider-neutral offline runner 验收核心工具链。
- Patch 仍是单文件 exact-text replacement MVP；shell 仍默认需要审批，生产默认拒绝。

风险：
- Shell 危险命令检测是保守模式匹配，不是完整 shell AST。
- 搜索工具未实现 ignore 文件语义。
- Git diff/status 输出按 git 原始文本摘要返回，后续可增加结构化字段。

第 14 周输入：
- 确认产品内核 `IAgentRunner` 边界。
- 添加 Microsoft Agent Framework 适配器骨架，保持 direct/offline runner 和 CLI 核心解耦。
