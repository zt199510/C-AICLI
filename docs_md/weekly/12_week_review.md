# Week 12 review

状态：已稳固

已完成：
- 添加 `IShellRunner`、`ShellCommandRequest` 和 `ShellCommandResult`。
- 添加 `DangerousCommandDetector`，拒绝删除、格式化、权限修改、下载执行、后台驻留和无限循环等危险模式。
- 添加 `RestrictedShellRunner`，要求 cwd 位于 workspace 内，并支持默认超时、stdout/stderr 截断和 exit code 记录。
- 添加 `WorkspaceShellTool`，将 shell 命令接入审批策略、工具注册表/执行器和 transcript approval status。
- 添加测试覆盖无害命令审批后运行、危险命令拒绝、cwd 越界、超时、stdout 截断、审批拒绝和离线 agent transcript 记录。

验证：
- 命令：`dotnet build src\CSharpAiCli.sln`
- 结果：通过，0 warning，0 error。
- 命令：`dotnet test src\CSharpAiCli.sln`
- 结果：通过，185 tests passed。

运行时说明：
- 默认审批策略仍安全拒绝 shell；测试使用 `AlwaysApproveApprovalPolicy` 显式批准。
- Shell runner 使用平台 shell：Windows 使用 `cmd.exe /d /c`，非 Windows 使用 `/bin/sh -c`。
- 输出摘要包含 `exitCode`、`timedOut`、`stdoutTruncated` 和 `stderrTruncated`。

风险：
- 危险命令检测是保守模式匹配，不等同完整 shell parser。
- 当前没有交互式 CLI 审批 UI，生产路径默认拒绝。
- Shell runner 不支持后台服务，也不允许绕过 workspace cwd。

第 13 周输入：
- 添加 git status/diff 工具。
- 完成 Phase 03 验收：读文件、搜索、patch、shell 和 git 工具在离线 agent loop 中可组合。
- Git 测试必须使用临时仓库。
