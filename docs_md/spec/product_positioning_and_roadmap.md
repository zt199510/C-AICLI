# C-AICLI 产品定位与路线护栏

更新时间：2026-07-14

## 一句话定位

C-AICLI 是一个 Windows-first、C#/.NET-first、私有模型友好、可审计的本地工程 Agent CLI。

它不是 Codex CLI、Claude Code、Cursor、Lovable、Replit Agent 或 WorkBuddy 的替代品。它的目标是成为一个开发者和团队可以完全掌控的工程 Agent 底座：模型调用可配置，工具执行可审计，安全策略可验证，执行过程可追踪，并能逐步承载垂直工程工作流。

## 为什么要做自己的 CLI

Codex CLI、Claude Code、Gemini CLI、Aider、Goose、OpenCode 和 Copilot CLI 已经覆盖了大量通用 AI 编程场景。C-AICLI 不应该正面竞争“最强通用 AI 程序员”。

C-AICLI 的价值在于可控性和工程化：

- 完全掌控 agent loop、工具系统、审批策略、日志格式、错误码和发布节奏。
- 优先服务 Windows、C#/.NET、本地仓库和企业私有模型环境。
- 支持 OpenAI 官方服务和 OpenAI-compatible base URL，为私有网关、本地模型和企业内网部署保留空间。
- 将安全边界、trace、session、report、smoke tests 和 release 验收作为产品核心，而不是附加功能。
- 为后续 Gerber/TIFF、C++ 验证、EDA 或其他垂直工程工作流提供统一执行底座。

## 核心用户

- Windows 上的 C#/.NET 开发者。
- 需要本地可控 AI 工程助手的高级开发者。
- 使用私有 OpenAI-compatible 模型网关的个人或团队。
- 重视安全审批、日志审计、错误码、smoke tests 和可复现发布的工程团队。
- 未来需要把 AI agent 接入 Gerber/TIFF、C++ 验证、EDA 或企业内部工程流程的用户。

## 核心差异化

### Windows-first

优先保证 Windows shell、路径、发布包、PowerShell 脚本、.NET SDK 和本地工程体验可靠。跨平台可以作为后续增强目标，但不应牺牲 Windows 主路径质量。

### C#/.NET-first

优先把 C#/.NET 工程的 build、test、solution、project、NuGet、日志和错误路径做扎实。不要一开始追求覆盖所有语言生态。

### 私有模型友好

持续支持官方 OpenAI endpoint 和 OpenAI-compatible base URL。后续可以增加 provider router，但不应让 provider 复杂度破坏核心 CLI 体验。

### 安全可审计

所有写文件、shell、MCP server 启动和高风险工具调用都必须经过清晰的权限策略、风险摘要和可追踪日志。默认行为应保护用户仓库，而不是追求最大自动化。

### 工程工作流导向

每次 agent 任务应逐步形成可交付结果：读了什么、改了什么、跑了什么、失败在哪里、用户如何复核。C-AICLI 应重视 report、changes view、session export 和 trace。

### 垂直工作流承载

通用 coding agent 是基础，不是终点。项目长期价值来自将 agent runtime 接到具体工程流程，例如 C#/.NET 修复、测试修复、代码审查、Gerber/TIFF、C++ 验证和 EDA 辅助流程。

## 明确不做什么

- 不做通用聊天工具。
- 不做 Codex CLI 或 Claude Code 的完整克隆。
- 不优先做 IDE 或编辑器。
- 不优先做桌面 WorkBuddy 式工作台。
- 不进入 Lovable、Replit、Bolt、v0 的一句话生成 App 赛道。
- 不在 0.2.x 或 0.3.x 早期做企业 IM 远程控制、团队知识库或复杂权限平台。
- 不为了演示效果绕过安全审批、workspace 边界、secret redaction 或 release smoke。

## 版本路线

### 0.2.0：可信 CLI Agent 底座

目标：完成 Week 34-38，把 C-AICLI 打磨成可发布、可诊断、可扩展的 CLI 底座。

核心范围：

- 工具 schema、structured result 和错误码统一。
- MCP stdio v1 真连接。
- shell、patch、MCP 启动安全边界。
- `--verbose`、`--trace` 和 `logs` 命令。
- 0.2.0 release zip、smoke tests、capability status 和 known limitations。

不应在 0.2.0 临近收口时塞入大型新功能。配置向导、完整任务报告、回滚辅助等可以放入 0.2.x 小版本。

### 0.2.1-0.2.2：发布后体验补强

目标：不改变主架构，补齐用户第一次使用和任务复核体验。

候选范围：

- First-run quickstart 或 `doctor` 改进。
- 更清晰的错误信息和 exit code 文档。
- `exec` 简短任务 summary。
- markdown report。
- changed files summary。
- 手动撤销提示。
- smoke tests 和 release docs 加固。

### 0.3.0：真实 Agent 开发闭环

状态：已完成，已在 2026-07-12 验收为当前 release。

目标：从“有 agent 基础设施”升级到“真实模型可以驱动工具完成小型开发任务”。

推荐首个场景：修 bug / 小改动。

核心闭环：

- 理解用户任务。
- 读取和搜索相关代码。
- 规划有限步骤。
- 申请审批后修改文件。
- 运行测试或验证命令。
- 根据失败结果迭代一次或多次。
- 输出最终 summary、changed files、commands、tests 和风险说明。

0.3.0 的成功标准不是命令数量，而是一个真实任务能稳定走完“读代码 -> 改代码 -> 跑测试 -> 总结”的闭环。

完成确认：

- 版本元数据已更新到 `0.3.0`，当前 release artifact 为 `artifacts/release/caicli-0.3.0-win-x64.zip`。
- `docs_md/release/final_acceptance_0.3.0.md` 已记录 0.3.0 final acceptance、deterministic zip size/SHA256、packaged smoke 和 Deferred 边界。
- 当前复核通过：`dotnet build src\CSharpAiCli.sln -c Release`、`dotnet test src\CSharpAiCli.sln -c Release --no-build` 和 `tools\Invoke-SmokeTests.ps1` 均通过；真实模型 smoke 仍按设计需要 `CAICLI_REAL_MODEL_SMOKE=1` 显式启用。
- 当前能力边界：direct OpenAI SDK agent tool-call continuation、fake/offline agent contract、bounded `exec`、patch/verification/failure retry、`review.gate`、`taskReport`、user-configured stdio MCP v1 是 0.3.0 当前能力；Microsoft Agent Framework real backend、remote/http MCP、Gerber/TIFF real execution、dotnet tool package、独立 full markdown report 和交互式 approval UI 仍 Deferred。

### 0.3.1-0.3.3：Developer Workflow Packs

状态：已完成，已在 2026-07-13 验收为当前 release line。

目标：借鉴 WorkBuddy 的 Skills、Experts 和可交付结果思路，但保持 CLI 形态。

详细排期统一维护在 `docs_md/weekly/47_week_cli_0_3_developer_workflow_packs_schedule.md`，路线图只保留版本方向，避免同一范围在多个文档里重复漂移。

版本方向：

- `0.3.1`：Workflow Inputs 与 Changes View，重点是 `@file`/`@folder` bounded reference 和只读 changes view。
- `0.3.2`：Reports 与 Expert Profiles，重点是按需 markdown report 和本地 `--expert` profiles。
- `0.3.3`：Local Skills 与 .NET Workflow Packs，重点是轻量本地 `skills list/run` 和首批 .NET workflow packs。

0.3.3 可以借鉴 WorkBuddy 的“专家和技能包”，但不应复制 WorkBuddy 的桌面工作台、IM 控制或办公套件形态。

完成确认：

- 版本元数据已更新到 `0.3.3`，当前 release artifact 为 `artifacts/release/caicli-0.3.3-win-x64.zip`。
- `docs_md/release/final_acceptance_0.3.3.md` 已记录 0.3.3 final acceptance、deterministic zip size/SHA256、packaged smoke 和 Deferred 边界。
- 当前复核通过：`dotnet build src\CSharpAiCli.sln -c Release`、`dotnet test src\CSharpAiCli.sln -c Release --no-build` 和 `tools\Invoke-SmokeTests.ps1` 均通过；真实模型 smoke 仍按设计需要 `CAICLI_REAL_MODEL_SMOKE=1` 显式启用。
- 当前能力边界：bounded `@file:` / `@folder:` references、只读 `changes`、markdown task report、`--expert` profiles、本地 `skills list/run`、内置 .NET workflow packs、direct OpenAI SDK agent tool-call continuation、fake/offline agent contract、`review.gate`、`taskReport` 和 user-configured stdio MCP v1 是 0.3.3 当前能力；remote skill marketplace、custom expert files、automatic model routing、daemon/API、CI/PR integration、Gerber/TIFF real execution 和 team platform 仍 Deferred。

### 0.4.0：工程自动化平台化

状态：已完成，已在 2026-07-14 验收为当前 release；只读 localhost daemon/API 保持 Preview。

目标：在真实 agent 闭环稳定后，引入更强的任务编排。

详细排期统一维护在 `docs_md/weekly/50_week_cli_0_4_engineering_automation_platform_schedule.md`，路线图只保留版本方向，避免同一范围在多个文档里重复漂移。

候选范围：

- 多 agent 或多角色协作，例如 implementer、reviewer、tester。
- 自动化任务，例如定时检查、定时 review、定时报告。
- CI/PR 集成。
- 本地 daemon、HTTP API 或 SSE，为 IDE、Web UI 或远程控制预留入口。
- 更完整的 task queue、job history 和 artifact 管理。

0.4.0 的优先级应是本地可审计任务编排，而不是远程平台化。job history、artifact index、task queue、multi-role pipeline 和 CI/report 输出应先在 CLI 与本地文件边界内稳定；daemon/API/SSE 只能作为可选 preview 或后续阶段，不能绕过 approval、workspace guard、secret redaction、trace/log、session/report 或 smoke。

完成确认：

- 版本元数据已更新到 `0.4.0`，当前 release artifact 为 `artifacts/release/caicli-0.4.0-win-x64.zip`。
- `docs_md/release/final_acceptance_0.4.0.md` 记录 0.4.0 final acceptance、deterministic zip size/SHA256、packaged smoke 和 Accepted/Preview/Deferred 边界。
- 本地 job history/artifact index、手动 task queue、固定顺序 multi-role pipeline、workspace-local automation validate/plan/dry-run/manual 和 provider-neutral CI artifacts 为 Accepted；default-off、IPv4 loopback-only、read-only daemon/API 为 Preview。
- Background scheduler、concurrent/remote worker、provider API、API control/SSE、authentication/TLS、remote bind、job/artifact retention/delete 和真实 Gerber/TIFF 执行继续 Deferred。

### 0.5.0：垂直工程工作流与 Gerber/TIFF v1

状态：candidate/blocked；Week 58-64 垂直工作流与安全加固已收口，但 Week 65 无法取得当前 source revision 的冻结 Gerbv/ImageMagick opt-in smoke 证据，因此未作 Accepted 决定。历史真实工具证据、fake smoke、metadata 或 preview 均不替代该 Gate。

目标：第一次用真实 Gerber/TIFF 工程场景验证 0.4.0 的 queue、job、pipeline、artifact、安全和报告底座，形成“发现输入 -> 诊断工具 -> 计划 -> 审批执行 -> TIFF 验证 -> 人工验收 -> artifact 清理”的本地可审计闭环。

详细排期统一维护在 `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`。

核心范围：

- Project Pack v1 契约、注册、依赖声明和工具链诊断。
- Gerber/TIFF bounded input discovery、真实外部工具适配和结构化执行。
- 每次运行独立 staging/output、checkpoint、cancel 和安全 resume。
- TIFF metadata/preview/baseline verification 与稳定 JSON/markdown report。
- managed artifact list/show/verify/export/prune 和人工 accept/reject gate。
- 默认 fake-tool/fixture smoke 与显式 opt-in 真实工具 smoke。

0.5.0 不同时扩展为通用 C++、EDA 或团队平台。Background scheduler、并行写 worker、自动 provider routing、API control/SSE、远程执行、团队权限、插件市场、桌面/Web UI 和自研完整 Gerber parser 继续 Deferred。

### 0.6.0+：后续垂直能力和团队化

候选方向：

- 通用 C++ 验证工作流和 EDA 辅助流程。
- 企业内部 MCP server、受控插件分发和团队审计。
- 在本地垂直工作流稳定后评估 scheduler、并发只读 worker 和 provider integration。
- 远程入口、团队知识库、权限平台以及可选桌面/Web 工作台必须作为独立产品线评估。

## 目标偏离检查清单

每次新增功能或制定版本计划时，先回答以下问题：

1. 这个功能是否强化 Windows-first、C#/.NET-first 或私有模型友好的定位？
2. 这个功能是否提升可审计性、安全性、可诊断性或工程工作流交付质量？
3. 这个功能是否服务本地工程 agent，而不是变成通用聊天、办公助手或 App builder？
4. 这个功能是否可以通过 CLI 清晰表达，而不需要过早引入桌面 UI？
5. 这个功能是否能被 build/test/smoke/release docs 验证？
6. 这个功能是否会绕过审批、安全边界、secret redaction 或 workspace guard？
7. 这个功能是否比当前版本主线更重要？如果不是，应进入后续小版本或 backlog。
8. 这个功能是否让 C-AICLI 更像“可控工程 Agent Runtime”，而不是更像“另一个 Codex/Claude 克隆”？

如果多数答案是否定的，应暂缓实现或移出当前版本范围。

## 参考产品与借鉴边界

- Codex CLI / Claude Code：借鉴本地 coding agent、项目指令、工具执行、MCP、权限和开发闭环，但不追求完整克隆。
- Gemini CLI / Aider / OpenCode / Goose：借鉴开源 CLI、模型接入、MCP、本地执行和隐私控制，但保持 Windows/.NET 主路径。
- Cursor / Windsurf / Copilot：借鉴 developer workflow 和 review/fix/test 体验，不优先做 IDE。
- Lovable / Replit / Bolt / v0：观察交付物和 app builder 趋势，不进入一句话生成 App 赛道。
- WorkBuddy：借鉴 Skills、Experts、多任务、Automation 和可交付结果，不复制桌面办公工作台或 IM 控制。

## 成功标准

C-AICLI 的长期成功不是“比 Codex/Claude 更通用”，而是做到：

- 在 Windows + C#/.NET 工程中稳定可靠。
- 可以接官方或私有模型。
- 每次 agent 行为可解释、可追踪、可审计。
- 高风险操作有明确审批和安全边界。
- 任务结果可以通过报告、diff、测试和日志复核。
- 可以逐步承载垂直工程工作流。

最终目标是成为一个开发者完全掌控的本地工程 Agent Runtime。
