# 阶段 05 - MCP 与项目工作流计划

## 状态

`Deferred`

## 目标

添加 MCP 客户端支持和项目专用工程工作流，包括面向 Gerber/TIFF 转换仓库的工作流，以及 C++ CLI 验证命令。

本阶段是增强目标。MCP 和项目包必须保持可选，失败或未配置时不得影响 `chat`、`run`、本地工具和发布打包。

## 目标周数

第 18-22 周

## 范围

创建：

- MCP server 配置加载器：第 18 周已完成
- MCP client 连接管理器：第 19 周已完成配置级 doctor 诊断
- MCP tool bridge：第 20 周已完成 generic external tool bridge
- 项目工作流注册表：第 21 周已完成
- 验证命令 profile：第 21 周已完成 MVP
- Gerber/TIFF 项目工作流包：第 22 周已完成 project-pack MVP
- 计划文档读取工作流：第 22 周已完成 fixture/status MVP

## 架构

MCP 工具应作为外部工具出现在同一个 `IToolRegistry` 中。项目工作流应与通用 agent core 分离。

建议布局：

```text
CSharpAiCli.Core/
  Mcp/
  Workflows/
CSharpAiCli.ProjectPacks/
  GerberTiff/
```

项目包配置只允许通过 workflow profile、用户配置或命令行参数提供路径。通用 core 代码不得硬编码任何用户机器的绝对路径。

## 必需行为

- `caicli mcp list` 显示已配置的 MCP servers。
- `caicli mcp doctor` 检查连接状态。
- MCP 工具可以启用或禁用。
- 项目工作流可以读取 `docs_md/plans`。
- 项目工作流可以在审批后运行配置好的验证命令。
- Gerber/TIFF 工作流理解计划状态和下一阶段规则。
- 未配置 MCP server 时，`mcp list` 和 `mcp doctor` 显示空配置或诊断，不报崩溃式错误。
- workflow profile 可以声明 Gerber/TIFF 工作区路径；未声明时，命令必须要求显式 `--workspace`。

## 验收标准

1. MCP 配置可安全加载。
2. MCP 工具可发现。
3. 已禁用的 MCP 工具不能执行。
4. 项目工作流可以总结 master plan 状态。
5. 项目工作流可以提出验证命令。
6. C++ 验证命令执行仍使用审批策略。
7. 禁用全部 MCP servers 后，MCP 工具不可执行，但 direct 本地工具仍可用。
8. Gerber/TIFF 工作流测试使用临时 profile 或 fixture 路径，不依赖某个用户的桌面路径。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- mcp list
dotnet run --project src/CSharpAiCli.Cli -- mcp doctor
dotnet run --project src/CSharpAiCli.Cli -- workflow gerber-tiff status --workspace "<configured-gerber-tiff-workspace>"
```

## 风险与保护边界

- 在配置完成前，把 MCP 工具视为不受信任的外部能力。
- MCP 保持可选。
- 项目包保持可移除。
- 不把某个用户的绝对路径硬编码进通用 core 代码。

## 阶段交付物

- MCP 客户端基础
- 项目工作流系统
- Gerber/TIFF 工作流包
- 验证 profile 支持

## 下一阶段输入

阶段 06 打包 CLI，强化文档和测试，并准备第一个可用发布版本。

## 阶段 05 验收记录

- `dotnet build src\CSharpAiCli.sln`：通过，0 warning，0 error。
- `dotnet test src\CSharpAiCli.sln`：通过，233 tests passed。
- MCP config/list/doctor、generic MCP tool bridge、workflow profiles 和 Gerber/TIFF project-pack MVP 均已覆盖测试。
- 真实 MCP 协议 handshake、真实 MCP tool discovery 和完整 Gerber/TIFF 执行包作为增强目标 Deferred；不阻塞阶段 06 MVP 发布。
