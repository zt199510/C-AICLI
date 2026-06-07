# 阶段 03 - 工具、安全与文件编辑计划

## 状态

`Accepted`

## 目标

添加安全工具系统，用于读取文件、搜索工作区、通过 patch 编辑文件、在审批后运行 shell 命令，并报告 git 状态。

## 目标周数

第 9-13 周

## 范围

创建：

- 工具注册表：第 9 周已完成
- 工具执行上下文：第 9 周已完成
- 工作区路径验证器：第 10 周已完成
- 审批策略：第 11 周已完成文件编辑审批基础策略
- 文件读取工具：第 10 周已完成
- 搜索工具：第 10 周已完成
- patch 编辑工具：第 11 周已完成单文件 exact-text replacement MVP
- shell 命令工具：第 12 周已完成受限 runner MVP
- git status 和 diff 工具：第 13 周已完成
- 工具调用转录记录：第 9-13 周已完成并用于工具、审批、patch、shell 和 git 验收

本阶段是最高风险阶段，按严格 MVP 实施。第一版只要求在一个受控工作区内完成小型读文件、搜索、单文件 patch、无害验证命令和 git 状态总结；多文件重构、复杂冲突合并、自动修复长测试链路和跨工作区操作不进入本阶段。

## 架构

工具应是位于小型接口之后的普通 C# 类。面向模型的 schema 应从工具元数据生成。工具执行必须始终经过工作区和审批检查。

必需抽象：

```text
ITool
IToolRegistry
IToolExecutor
IApprovalPolicy
IWorkspaceGuard
IPatchApplier
IShellRunner
```

## 安全规格

工作区路径验证：

- 所有输入路径先做绝对路径解析和规范化，再判断是否位于 workspace root 内。
- `..`、大小写差异、相对路径、尾随分隔符和 Windows 长路径前缀必须归一化后判断。
- junction、symlink 和 reparse point 必须按解析后的实际目标判断；目标在 workspace 外时拒绝。
- 默认拒绝读取二进制文件和超过配置上限的大文件；上限由配置提供，默认值写入测试。
- 工作区外读取、写入和命令 cwd 必须失败，并把安全原因返回给 agent 循环。

Patch 编辑：

- 第一版支持 unified diff 风格 patch 或内部等价结构，但必须只修改声明的 workspace 内文件。
- patch 预览必须展示待修改路径、增删摘要和 diff。
- 应用前必须重新读取目标文件并确认上下文仍匹配，避免覆盖用户并发改动。
- 若 git working tree dirty，仍可编辑，但必须在预览中标记当前 dirty 状态；如果目标文件自预览后变化，拒绝应用。
- Patch applier 不做整文件盲写，不修改未在 patch 中声明的内容。

审批交互：

- 文件写入和 shell 命令默认需要审批。
- 审批记录写入转录，包含请求摘要、审批结果、时间和执行结果。
- 非交互 smoke test 可使用显式测试模式或 fake approval policy，不能绕过生产默认策略。

Shell 执行：

- shell runner 的 cwd 必须是 workspace 内路径。
- 默认超时、最大 stdout/stderr 字节数和输出截断标记必须可配置。
- 默认拒绝删除、格式化、权限修改、无限循环、后台驻留、网络下载执行和跨工作区移动/复制等危险模式。
- 命令失败、超时和被拒绝都必须返回给 agent 循环。

Git 工具：

- 当前仓库不是 git repo 时，工具返回清晰诊断。
- 阶段 03 的 git 测试必须创建临时测试仓库，不能依赖本计划仓库本身已经有 `.git`。

## 必需行为

- AI 可以请求读取工作区内的文件。
- AI 可以请求在工作区内搜索文本。
- AI 可以提出 patch，并在审批后应用。
- AI 可以请求 shell 命令，并在审批后运行。
- 危险命令被阻止或需要明确确认。
- 可以总结 Git status 和 diff。
- 工具失败会返回给 agent 循环，而不是被隐藏。

## 验收标准

1. 默认阻止读取工作区外文件。
2. Patch 编辑保留无关文件内容。
3. Shell 命令需要审批。
4. 默认拒绝破坏性命令模式。
5. 工具调用循环可以完成简单的读文件、编辑、测试任务。
6. 单元测试覆盖工作区保护、审批策略和 patch applier。
7. Windows 路径安全测试覆盖 `..`、大小写路径、junction/symlink、只读文件、二进制文件和大文件。
8. Shell 测试覆盖无害命令允许、危险模式拒绝、超时、cwd 限制和 stdout/stderr 截断。
9. 离线集成测试使用 fake model client 和 fake tools，验证 agent loop 能处理工具失败并继续。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- run "read the README and summarize it"
dotnet run --project src/CSharpAiCli.Cli -- run "create a tiny note file in the workspace"
dotnet run --project src/CSharpAiCli.Cli -- run "run the configured smoke test command"
```

## 风险与保护边界

- 绝不允许静默写入工作区外。
- 绝不在没有明确审批的情况下运行破坏性命令。
- Shell runner 与 agent 逻辑保持分离。
- 保留用户改动，并在写入前展示 diff。

## 阶段交付物

- 安全本地工具
- 审批门禁
- patch 编辑
- 第一条 agentic coding 工作流

## 下一阶段输入

阶段 04 通过适配器引入 Microsoft Agent Framework，同时保留直接 OpenAI SDK 路径作为回退。

## 阶段 03 验收记录

- `dotnet build src\CSharpAiCli.sln`：通过，0 warning，0 error。
- `dotnet test src\CSharpAiCli.sln`：通过，190 tests passed。
- 离线集成测试 `Phase03AcceptanceTests` 串联读取、搜索、patch、shell、git status 和 git diff。
