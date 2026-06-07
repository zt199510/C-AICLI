# Week 11 review

状态：已稳固

已完成：
- 添加 `IPatchApplier`、`PatchOperation`、`PatchPreview` 和 `PatchApplyResult`。
- 添加 `SingleFilePatchApplier`，支持单文件 exact-text replacement patch 预览、摘要 diff、上下文校验和应用。
- 应用前记录目标文件内容 hash；若预览后目标文件变化，则拒绝应用。
- 添加 `IDirtyWorkspaceDetector` 和 `GitDirtyWorkspaceDetector`，将 dirty 状态写入 patch preview 摘要。
- 添加 `IApprovalPolicy`、`DefaultDenyApprovalPolicy` 和 `AlwaysApproveApprovalPolicy`；默认策略拒绝文件编辑，测试策略显式批准。
- 添加 `WorkspacePatchTool`，将 patch 预览、审批、应用接入第 9 周工具注册表/执行器和 transcript tool call 记录。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，167 tests passed。

运行时说明：
- 当前 patch 形状是保守的单文件 exact-text replacement，不做复杂 unified diff 解析或三方合并。
- `WorkspacePatchTool` 使用第 10 周 `WorkspaceGuard`，禁止修改 workspace 外文件。
- transcript 的 `approvalStatus` 可以记录 `approved` 或 `denied`。

风险：
- 生产 CLI 还没有交互式审批 UI；默认策略安全拒绝。
- Patch preview diff 是最小摘要 diff，不是完整上下文 unified diff。
- Dirty workspace 检测基于 `git status --porcelain`；非 git workspace 会记录为 not a git workspace。

第 12 周输入：
- 添加 shell runner、命令审批、危险命令阻断、超时和输出截断。
- shell 工具必须复用审批策略和 transcript approval status。
- shell cwd 必须复用 workspace guard。
