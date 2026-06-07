# 阶段 04 - Microsoft Agent Framework 适配器计划

## 状态

`Deferred`

## 目标

在项目自有的 agent 接口之后集成 Microsoft Agent Framework，证明 CLI 可以在直接 OpenAI SDK runner 和框架后端 runner 之间切换，并避免框架锁定。

本阶段是增强目标，不是首版发布硬依赖。Microsoft Agent Framework 仍按预览或快速变化依赖处理；阶段失败时必须保留直接 OpenAI SDK runner，并允许阶段 06 将 framework 后端标注为未启用或实验状态。

## 目标周数

第 14-17 周

## 范围

创建：

- `MicrosoftAgentFrameworkRunner`：第 14 周已完成 experimental stub
- 适配器测试：第 14 周已完成边界测试
- agent backend 的配置开关：第 16 周已完成 `agentBackend` resolver
- 共享工具注册桥接：第 15 周已完成 adapter DTO + Core executor bridge
- 框架会话映射：真实 framework 后端未启用，作为增强目标 Deferred
- 当框架包变化或失败时的回退行为：第 16 周 doctor 已诊断 experimental stub / package 未启用
- 预览依赖风险记录

## 架构

面向产品的契约仍然是 `IAgentRunner`。Microsoft Agent Framework 是适配器包或适配器命名空间中的实现细节。

推荐布局：

```text
CSharpAiCli.Core/
  Agents/
  Tools/
  Safety/
CSharpAiCli.AgentFramework/
  MicrosoftAgentFrameworkRunner.cs
  MicrosoftToolBridge.cs
```

## 必需行为

- `caicli config set agent.backend direct` 使用直接 runner。
- `caicli config set agent.backend maf` 使用 Microsoft Agent Framework；如果包或配置不可用，命令必须给出清晰错误，并保持 direct 后端可用。
- 两个后端都能调用相同的本地工具。
- 两个后端都能保留会话转录。
- 如果框架后端启动失败，`doctor` 会解释原因。
- 禁用或移除框架适配器项目时，核心 CLI、direct runner 和阶段 03 工作流仍可构建和运行。

## 验收标准

1. 直接后端仍通过阶段 03 的所有工作流。
2. 框架后端可以完成读取、搜索、总结工作流。
3. 工具注册表没有重复实现。
4. CLI 可以通过配置切换后端。
5. 适配器层有测试或 smoke 验证。
6. 核心 CLI 操作不依赖仅预览版特性。
7. 如果框架依赖不可用，阶段可以记录为增强目标未接受，但不得阻止 direct 后端进入发布候选。

## 验证命令

```powershell
dotnet build src/CSharpAiCli.sln
dotnet test src/CSharpAiCli.sln
dotnet run --project src/CSharpAiCli.Cli -- config set agent.backend direct
dotnet run --project src/CSharpAiCli.Cli -- run "summarize the workspace"
dotnet run --project src/CSharpAiCli.Cli -- config set agent.backend maf
dotnet run --project src/CSharpAiCli.Cli -- run "summarize the workspace"
```

## 风险与保护边界

- 第 14 周未引入 Microsoft Agent Framework 预览包；adapter 当前标记为 `experimental-stub`，避免预览依赖泄漏到 Core/CLI。
- 不在 CLI 命令代码中暴露 Microsoft Agent Framework 类型。
- 不移除直接 runner。
- 必需行为不依赖预览版特性。
- 保持提供商关注点和框架关注点分离。
- 不把 Microsoft Agent Framework 的类型、schema 或 session 模型泄漏到 `CSharpAiCli.Core` 的公共契约。

## 阶段交付物

- Microsoft Agent Framework 适配器
- 后端切换
- 保留回退 runner
- 降低框架锁定风险

## 下一阶段输入

本地 agent 和安全模型稳定后，阶段 05 添加 MCP 和项目专用工作流。

## 阶段 04 验收记录

- `dotnet build src\CSharpAiCli.sln`：通过，0 warning，0 error。
- `dotnet test src\CSharpAiCli.sln`：通过，205 tests passed。
- Direct/offline backend 通过 `Phase04AcceptanceTests` 回归。
- Microsoft Agent Framework adapter、tool bridge、backend config 和 doctor 诊断已完成。
- 真实 Microsoft Agent Framework package 未启用，framework 后端标记为增强目标 Deferred；不阻塞 direct MVP。
