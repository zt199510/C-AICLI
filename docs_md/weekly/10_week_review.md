# Week 10 review

状态：已稳固

已完成：
- 添加 `IWorkspaceGuard`、`WorkspaceGuard` 和 `WorkspaceGuardResult`，统一解析 workspace 内路径并拒绝工作区外访问。
- 路径保护覆盖 `..`、绝对路径越界、前缀 sibling 绕过、缺失 workspace，以及可用时的 directory symlink/junction 越界。
- 添加 `WorkspaceFileReadTool`，支持读取 workspace 内文本文件，拒绝缺失文件、越界路径、超大文件和二进制文件。
- 添加 `WorkspaceSearchTool`，支持 workspace 内文本搜索、大小上限、二进制跳过、最大结果数和子目录遍历。
- 将 read/search 工具接入第 9 周 `ToolRegistry`、`ToolExecutor` 和 `OfflineAgentRunner` 测试链路。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，156 tests passed。

运行时说明：
- 第 10 周仍只提供只读工具，不写文件、不运行 shell。
- `WorkspaceSearchTool` 搜索时跳过无法安全解析、越界、二进制或超过大小上限的文件。
- `WorkspaceFileReadTool` 默认文本文件读取上限为 256 KiB。

风险：
- 当前二进制判断为 NUL byte 探测，后续可根据真实项目类型扩展。
- 搜索工具使用内置遍历和字符串匹配，还没有 ripgrep 级别的性能或 ignore 文件语义。
- symlink/junction 测试在系统不允许创建符号链接时会安全跳过该断言路径。

第 11 周输入：
- 添加 patch applier、dirty workspace 检测和文件编辑审批流程。
- 文件写入必须复用第 10 周 `WorkspaceGuard`。
- patch 预览和应用必须将审批占位扩展为真实审批记录。
