# C# AI CLI 半年执行计划

## 状态

`MVP Accepted / Enhanced Deferred`

## 目标

构建一个基于 C#/.NET 的本地 AI CLI。它应当先服务于 Windows、C++、命令行工具以及当前用户的 Gerber/TIFF 转换工作流，并逐步扩展为类似 Codex 的工程辅助工具。

## 实施口径

本计划按单人全职开发者评估为可行，但必须按 MVP 优先交付，而不是把所有增强能力都作为首版硬门槛。

- MVP 可行性：约 `7/10`。
- 接近 Codex 稳定工程体验的可行性：约 `5/10`。
- 当前仓库基线允许只有计划文档和空源码目录；git 工具阶段必须另外准备受控测试仓库。
- 如果从当前时间重新启动，优先使用 .NET 10 LTS；如果坚持 .NET 8 LTS，必须用 `global.json`、安装说明和 CI 配置显式锁定 SDK。当前本机只检测到 .NET SDK `9.0.308`。

## 产品目标

半年目标不是复制 Codex 的全部能力，而是交付一个可运行、可扩展、具备安全边界的 C# AI CLI，包含：

- 终端中的交互式聊天
- 模型流式输出
- 感知工作区的文件读取
- patch 风格的文件编辑
- 带审批门禁的命令执行
- git diff/status 感知
- 工具注册表和工具调用循环
- 对话持久化
- 项目指令加载
- 直接 OpenAI SDK agent runner 作为回退路径
- 面向 C++/CLI 项目的验证工作流
- 面向 Windows 开发者的发布打包

增强目标包含：

- 通过适配器集成 Microsoft Agent Framework
- 可选的 MCP 客户端支持
- Gerber/TIFF 项目工作流包

增强目标失败时不得阻塞首版发布；直接 OpenAI SDK runner 必须始终保留。

## 核心架构决策

CLI 拥有产品内核。可以使用 Microsoft Agent Framework，但只能通过可替换的适配器使用。

要求的边界：

```text
C#AICLI
|-- CLI/TUI layer
|-- workspace and safety layer
|-- tool registry and tool executor
|-- conversation/session store
|-- agent abstraction interfaces
|-- Microsoft Agent Framework adapter
|-- direct OpenAI SDK fallback runner
`-- project-specific tools
```

这样可以保护项目免受框架变化影响。如果 Microsoft Agent Framework 停止更新或方向变化，只需要替换适配器。

## 技术栈

- 语言：C#
- 运行时：重新启动时优先 .NET 10 LTS；若沿用 .NET 8 LTS，必须显式锁定 SDK 和支持窗口
- CLI 命令：System.CommandLine
- 终端 UI：Spectre.Console
- 模型 API：OpenAI .NET SDK，主接口优先采用 Responses API，以便后续工具调用和流式事件统一
- AI 抽象：在合适场景使用 Microsoft.Extensions.AI
- Agent 框架：Microsoft Agent Framework，仅作为可选适配器或实验后端
- MCP：后续阶段使用官方 MCP C# SDK，保持可选
- 测试：xUnit
- 打包：dotnet tool、Windows 自包含可执行文件、zip 发布包

## 阶段顺序

1. `01_foundation_cli_workspace.plan.md`
2. `02_model_streaming_sessions.plan.md`
3. `03_tools_safety_file_editing.plan.md`
4. `04_agent_framework_adapter.plan.md`
5. `05_mcp_project_workflows.plan.md`
6. `06_packaging_release_hardening.plan.md`

规则：上一个核心阶段达到 `Accepted` 之前，不启动下一个核心阶段，除非某个阶段计划明确标注任务可以并行执行。阶段 04 和阶段 05 是增强阶段；如果它们因预览依赖、外部服务或项目包限制无法验收，可以记录为 `Deferred`，并允许阶段 06 继续推进 MVP 发布。

## 阶段状态定义

| 状态 | 含义 |
|---|---|
| `Planned` | 目标、范围和验收标准已定义。 |
| `In Progress` | 正在实现。 |
| `Solidified` | 交付物已存在，并已记录验证输出。 |
| `Accepted` | 验收标准通过，且该阶段可以作为后续工作的基础。 |
| `Deferred` | 增强目标未进入本次发布，已记录原因和恢复条件，不阻塞 MVP。 |

## 六个月后的成功定义

项目成功的标准是：Windows 开发者可以运行：

```powershell
caicli
```

然后让它检查仓库、读取计划文档、提出修改建议、通过 patch 编辑文件、在审批后运行验证命令、总结失败原因，并保留有用的会话历史。

最低发布目标：

```powershell
caicli chat
caicli run "inspect this repo and suggest next steps"
caicli config get
caicli config set <key> <value>
caicli doctor
```

最低发布目标必须覆盖：

```text
workspace 文件 read/search
审批式 patch
审批式 shell
会话转录
Windows 发布包
```

增强发布目标：

```powershell
caicli mcp list
```

## 前六个月非目标

- 完整的云端任务执行
- 完整的 IDE 扩展
- 浏览器自动化
- 自主执行破坏性命令
- 不受限制的文件系统访问
- 多用户服务器模式
- 计费、团队管理或 SaaS 后端
- 与 Codex 完全对等

## 保护边界

- 所有 shell 命令都必须经过审批策略。
- 文件写入默认限制在选定工作区内，除非明确批准。
- 优先使用 patch 编辑，不做盲目的整文件重写。
- 不把预览版框架特性放进核心产品。
- 将提供商相关代码放在模型或 agent 适配器之后。
- 项目专用工具保持可选、可插拔。
- Microsoft Agent Framework 和 MCP 都是增强能力，不能成为首版发布阻塞项。

## 验证节奏

每周结束时必须具备：

- 一个可运行的命令
- 改动组件的单元测试通过
- 一份简短状态说明
- 如果验收标准发生变化，同步更新计划状态
- 从阶段 03 开始持续沉淀 smoke tests、安全限制和失败案例，不能等到阶段 06 才补齐。

每个阶段结束时必须具备：

- 阶段验收清单
- 命令输出摘要
- 已知限制
- 下一阶段输入

## 交付物

- `src/CSharpAiCli.sln`
- CLI 应用项目
- core library 项目
- test 项目
- 文档和计划文件
- 本地配置示例
- Windows 发布产物
- 可选的 dotnet tool 包

## 当前计划索引

| 阶段 | 文件 | 目标周数 | 状态 |
|---|---|---:|---|
| 01 | `01_foundation_cli_workspace.plan.md` | 1-4 | `Accepted` |
| 02 | `02_model_streaming_sessions.plan.md` | 5-8 | `Accepted` |
| 03 | `03_tools_safety_file_editing.plan.md` | 9-13 | `Accepted` |
| 04 | `04_agent_framework_adapter.plan.md` | 14-17 | `Deferred` |
| 05 | `05_mcp_project_workflows.plan.md` | 18-22 | `Deferred` |
| 06 | `06_packaging_release_hardening.plan.md` | 23-26 | `Accepted` |

## 周计划

逐周路线图存放在：

- `docs_md/weekly/26_week_goal_schedule.md`
