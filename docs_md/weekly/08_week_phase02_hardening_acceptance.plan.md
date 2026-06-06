# 第 8 周 Phase 02 Hardening 与 Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成阶段 02 收口：添加工作区 instruction loader，收紧配置优先级和工作区密钥策略，并验收模型错误格式、脱敏和 session/transcript 边界。

**Architecture:** `CSharpAiCli.Core` 保持 provider-neutral 的 `IInstructionLoader` 与 `InstructionSet`；CLI 负责读取工作区指令并写入 `ChatRequest`；OpenAI Responses client 负责把指令放进请求 payload。配置层统一执行 `OPENAI_MODEL`、用户配置、工作区配置、默认值的 model 优先级，以及 `OPENAI_API_KEY`、用户配置、missing 的 API key 优先级。

**Tech Stack:** C#、`net9.0`、System.CommandLine、OpenAI .NET SDK Responses API、System.Text.Json、xUnit、Windows PowerShell。

---

## 来源

- 总周计划：`docs_md/weekly/26_week_goal_schedule.md`
- 阶段 02 计划：`docs_md/plans/02_model_streaming_sessions.plan.md`
- 第 7 周计划：`docs_md/weekly/07_week_session_transcript.plan.md`
- 第 7 周回顾：`docs_md/weekly/07_week_review.md`
- Week 5-7 spec：`docs_md/spec/model_client_responses_api.md`

第 8 周排期目标：

```text
添加指令加载、配置优先级、密钥遮蔽和模型错误处理。
周末验收：阶段 02 验收清单通过。
```

## 本周范围

- 从工作区根目录加载 `AICLI.md`。
- `chat "<prompt>"` 和 `chat --session <name> "<prompt>"` 都把指令传给模型客户端。
- 指令文件不存在时保持 Week 7 行为。
- 指令文件为空时视为未配置。
- 指令文件过大时拒绝加载，并在配置/诊断 warning 中给出安全原因。
- `OPENAI_MODEL` 优先于用户配置和工作区配置。
- model 配置优先级为：`OPENAI_MODEL` > 用户配置 `model` > 工作区配置 `model` > default。
- API key 优先级为：`OPENAI_API_KEY` > 用户配置 `apiKey` > missing。
- 工作区配置 `apiKey` 不再进入 effective config，只记录 warning。
- `doctor`、`config get`、日志、chat error 和 transcript error 都不得打印原始密钥。
- 模型错误字段保持：`provider`、`operation`、`statusCode` 或 `localErrorCode`、`safeMessage`、`retryable`。

## 本周不做

- 不实现工具调用、工具注册表或 agent loop。
- 不把历史 transcript 传给模型。
- 不实现 `config set`。
- 不实现 token/费用统计。
- 不引入 Microsoft Agent Framework 或 MCP。
- 不读取除 `AICLI.md` 以外的指令文件。

## 文件结构

```text
src/
  CSharpAiCli.Core/
    Chat/
      ChatRequest.cs                         # 修改：增加 Instructions
    Configuration/
      ConfigLoader.cs                        # 修改：配置优先级和 workspace apiKey 禁用
    Instructions/
      IInstructionLoader.cs                  # 新增
      InstructionLoadResult.cs               # 新增
      WorkspaceInstructionLoader.cs          # 新增
    ModelClients/OpenAI/
      IOpenAiResponsesGateway.cs             # 修改：传递 instructions
      OpenAiResponsesModelClient.cs          # 修改：验证并传递 instructions
      SdkOpenAiResponsesGateway.cs           # 修改：Responses payload 包含 instructions
  CSharpAiCli.Cli/
    Commands/
      CliCommandFactory.cs                   # 修改：加载 instruction 并传入 request
  CSharpAiCli.Tests/
    ConfigLoaderTests.cs                     # 修改/新增
    CliEnvironmentSnapshotTests.cs           # 修改/新增
    WorkspaceInstructionLoaderTests.cs       # 新增
    CliCommandFactoryTests.cs                # 修改/新增
    OpenAiResponsesModelClientTests.cs       # 修改/新增
docs_md/
  spec/
    model_client_responses_api.md            # 修改：记录 Week 8 行为
  weekly/
    26_week_goal_schedule.md                 # 周末收尾更新
    08_week_review.md                        # 周末收尾创建
```

---

## Task 1: 创建 instruction loader

- [ ] Step 1: 添加 `InstructionLoadResult`，包含 `Instructions`、`SourcePath`、`Warnings` 和 `HasInstructions`。
- [ ] Step 2: 添加 `IInstructionLoader`，定义 `Load(WorkspaceContext workspace)`。
- [ ] Step 3: 添加 `WorkspaceInstructionLoader`，读取 `<workspace>/AICLI.md`，默认最大 64 KiB。
- [ ] Step 4: 添加单元测试覆盖不存在、存在、空文件、超大文件、缺失 workspace。

## Task 2: 配置优先级和密钥策略收口

- [ ] Step 1: 给 `ConfigLoader.Load` 增加 `openAiModel` 参数，默认读 `OPENAI_MODEL`。
- [ ] Step 2: model 优先级改为 `OPENAI_MODEL` > user config > workspace config > default。
- [ ] Step 3: API key 优先级改为 `OPENAI_API_KEY` > user config > missing。
- [ ] Step 4: workspace config `apiKey` 只产生 warning，不进入 `EffectiveConfiguration.ApiKey`。
- [ ] Step 5: 更新 config、doctor、logger 相关测试，确认不泄漏密钥。

## Task 3: CLI 接线 instruction loader

- [ ] Step 1: `ChatRequest` 增加 `Instructions` 字段。
- [ ] Step 2: `CliCommandFactory.Create` 增加可注入的 `IInstructionLoader` factory，默认使用 `WorkspaceInstructionLoader`。
- [ ] Step 3: chat 命令在模型调用前加载 instruction，并传给 `ChatRequest`。
- [ ] Step 4: session transcript 只记录用户 prompt，不把 instruction 写入 user message。
- [ ] Step 5: 添加 CLI 单元测试确认 instruction 被传递，且无 `AICLI.md` 时传空。

## Task 4: OpenAI Responses payload 支持 instructions

- [ ] Step 1: `IOpenAiResponsesGateway` 在非流式和流式方法中增加 `instructions` 参数。
- [ ] Step 2: `OpenAiResponsesModelClient` 传递 `request.Instructions`，并保持 prompt trim 行为。
- [ ] Step 3: `SdkOpenAiResponsesGateway` 用 Responses options 构造请求，包含 instructions 和 user input。
- [ ] Step 4: 更新 fake gateway 测试，确认 instruction 透传。

## Task 5: Spec、验证与回顾

- [ ] Step 1: 更新 `docs_md/spec/model_client_responses_api.md` 的 Week 8 行为。
- [ ] Step 2: 运行 `dotnet build src/CSharpAiCli.sln`。
- [ ] Step 3: 运行 `dotnet test src/CSharpAiCli.sln`。
- [ ] Step 4: 运行缺 key、workspace key 禁用、instruction loader 的 CLI smoke。
- [ ] Step 5: 创建 `docs_md/weekly/08_week_review.md`。
- [ ] Step 6: 更新 `docs_md/weekly/26_week_goal_schedule.md`：第 8 周已验收，并将阶段 02 标记完成。

## 验收标准

Do not mark Week 8 accepted until:

- `dotnet build` 通过。
- `dotnet test` 通过。
- `AICLI.md` instruction 可以被 chat path 读取并传入模型客户端。
- 无 `AICLI.md` 时保持 Week 7 行为。
- workspace config `apiKey` 不会让 effective config 显示 `apiKey: present`。
- 工作区密钥值不会出现在 report、日志、chat output 或 transcript error。
- 阶段 02 计划的 8 条验收标准均有实现、测试或明确记录。

