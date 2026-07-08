# C# AI CLI 26 周目标排期

更新时间：2026-06-03

## 当前进度

- 第 1 周已稳固：solution 骨架、仓库布局、文档目录和测试项目已建立。详见 `01_week_review.md`。
- 第 2 周已稳固：`System.CommandLine` 根命令、`doctor`、`config get`、运行环境快照和配置报告测试已完成。详见 `02_week_review.md`。
- 第 3 周已稳固：工作区检测、配置加载、配置优先级、密钥脱敏和 `--workspace <path>` 覆盖支持已完成。详见 `03_week_review.md`。
- 第 4 周已验收：日志、诊断、`chat` Phase 02 边界提示和阶段 01 文档已完成。详见 `04_week_review.md`。
- 第 5 周已稳固：model client 抽象、OpenAI SDK Responses API 实现和一次性 `chat "<prompt>"` 模型调用已完成。详见 `05_week_review.md`。
- 第 6 周已稳固：chat 终端流式渲染器和 OpenAI Responses streaming path 已完成。详见 `06_week_review.md`。
- 第 7 周已稳固：命名会话恢复、用户 profile transcript v1 和工具调用 schema 占位已完成。详见 `07_week_review.md`。
- 第 8 周已验收：`AICLI.md` 指令加载、配置优先级收口、workspace `apiKey` 禁用和阶段 02 验收已完成。详见 `08_week_review.md`。
- 第 9 周已稳固：工具注册表、工具执行器、工具调用 transcript schema 和离线 fake model + fake tool agent loop 已完成。详见 `09_week_review.md`。
- 第 10 周已稳固：WorkspaceGuard、workspace 文本读取工具、文本搜索工具和只读安全测试已完成。详见 `10_week_review.md`。
- 第 11 周已稳固：单文件 patch applier、dirty workspace 检测、文件编辑审批策略和 patch tool transcript 记录已完成。详见 `11_week_review.md`。
- 第 12 周已稳固：受限 shell runner、危险命令检测、命令审批、超时和输出截断已完成。详见 `12_week_review.md`。
- 第 13 周已验收：git status/diff 工具和阶段 03 离线 agentic MVP workflow 已完成。详见 `13_week_review.md`。
- 第 14 周已稳固：Microsoft Agent Framework adapter 项目骨架、`IAgentRunner` 边界和 CLI 隔离测试已完成。详见 `14_week_review.md`。
- 第 15 周已稳固：adapter tool bridge、framework-facing 工具 DTO 和 read/search 工具调用测试已完成。详见 `15_week_review.md`。
- 第 16 周已稳固：`agentBackend` 配置、backend resolver、config/doctor backend 诊断已完成。详见 `16_week_review.md`。
- 第 17 周已验收：阶段 04 adapter/fallback 边界已验收，真实 Microsoft Agent Framework 后端作为增强目标标记 Deferred。详见 `17_week_review.md`。
- 第 18 周已稳固：MCP 配置模型、loader 和 `mcp list` 命令已完成，disabled servers 保持 inactive。详见 `18_week_review.md`。
- 第 19 周已稳固：MCP connection manager、配置级诊断和 `mcp doctor` 命令已完成。详见 `19_week_review.md`。
- 第 20 周已稳固：MCP tool bridge、generic external tool adapter 和 disabled server 不可执行测试已完成。详见 `20_week_review.md`。
- 第 21 周已稳固：项目 workflow registry、validation profiles 和 `workflow list/validate` 命令骨架已完成。详见 `21_week_review.md`。
- 第 22 周已验收：Gerber/TIFF project pack 骨架和阶段 05 增强目标边界已完成，真实 MCP/Gerber 执行标记 Deferred。详见 `22_week_review.md`。
- 第 23 周已稳固：版本元数据、`caicli version`、Windows self-contained 发布脚本和 release manifest 已完成。详见 `23_week_review.md`。
- 第 24 周已稳固：安装、配置、安全模型、quickstart 和能力状态文档已完成，并验证干净用户路径下的 `doctor` 与 `chat` 错误路径。详见 `24_week_review.md`。
- 第 25 周已稳固：发布 smoke 脚本、工具禁用、`tools`、deterministic `run` 和 session export/clear 加固已完成。详见 `25_week_review.md`。
- 第 26 周已验收：changelog、known limitations、final acceptance 和首个本地发布 zip 已完成，MVP release `0.1.0` 通过验收。详见 `26_week_review.md`。
- 运行时仍为 `net9.0`，仓库通过根目录 `global.json` 锁定 .NET SDK `9.0.308`，并使用 `latestPatch` roll-forward 策略。

## 假设

- 开始日期：2024-06-03
- 周期：26 周
- 主要平台：Windows
- 主要实现语言：C#/.NET
- 产品方向：类似 Codex 的本地工程 CLI，而不是完整 Codex 克隆
- 核心框架策略：Microsoft Agent Framework 是适配器，不是产品内核
- 发布策略：direct OpenAI runner、本地安全工具和 Windows 发布包是 MVP；Microsoft Agent Framework、MCP 和 Gerber/TIFF 项目包是增强目标
- 运行时策略：当前保持 `net9.0`，通过 `global.json` 锁定 .NET SDK `9.0.308`；后续若迁移到 .NET 10 LTS 或 .NET 8 LTS，必须同步目标框架、安装说明和 release 文档。

## 逐周目标

| 周 | 日期 | 阶段 | 状态 | 主要目标 | 周末验收 |
|---:|---|---|---|---|---|
| 1 | 2024-06-03 至 2024-06-09 | 阶段 01 | 已稳固 | 创建 solution 骨架、仓库布局、第一批文档和测试项目。详见 `01_week_foundation_skeleton.plan.md` 和 `01_week_review.md`。 | 已验证：`dotnet build` 和 `dotnet test` 可在空骨架上运行。 |
| 2 | 2024-06-10 至 2024-06-16 | 阶段 01 | 已稳固 | 添加 System.CommandLine 根命令、`doctor`、`config get` 和帮助输出。详见 `02_week_cli_commands_doctor_config.plan.md` 和 `02_week_review.md`。 | 已验证：CLI 帮助包含 `doctor` 和 `config`；无 API key 时 `doctor` 可用；`config get` 不打印密钥值。 |
| 3 | 2024-06-17 至 2024-06-23 | 阶段 01 | 已稳固 | 添加工作区检测器和配置加载器，并支持密钥遮蔽。详见 `03_week_workspace_config.plan.md` 和 `03_week_review.md`。 | 已验证：工作区覆盖和配置遮蔽测试通过。 |
| 4 | 2024-06-24 至 2024-06-30 | 阶段 01 | 已验收 | 稳定基础能力、日志、诊断、`chat` Phase 02 边界提示和阶段 01 文档。详见 `04_week_phase01_hardening_acceptance.plan.md` 和 `04_week_review.md`。 | 已验证：阶段 01 验收清单通过。 |
| 5 | 2024-07-01 至 2024-07-07 | 阶段 02 | 已稳固 | 添加 model client 抽象和 OpenAI SDK Responses API 实现。详见 `05_week_model_client_responses_api.plan.md` 和 `05_week_review.md`。 | 已验证：`chat "<prompt>"` 在凭据和模型可用时可以调用已配置模型路径；缺 model/key 错误清晰；workspace config `apiKey` 会被拒绝用于真实调用。 |
| 6 | 2024-07-08 至 2024-07-14 | 阶段 02 | 已稳固 | 添加用于 chat 输出的终端流式渲染器。详见 `06_week_chat_streaming_renderer.plan.md` 和 `06_week_review.md`。 | 已验证：`caicli chat` 默认走 streaming renderer；缺 key/model 和 workspace `apiKey` 禁用路径保持安全脱敏。 |
| 7 | 2024-07-15 至 2024-07-21 | 阶段 02 | 已稳固 | 添加会话存储、版本化转录格式和工具调用初始 schema。详见 `07_week_session_transcript.plan.md` 和 `07_week_review.md`。 | 已验证：`chat --session <name> "<prompt>"` 可以恢复并追加命名 transcript v1。 |
| 8 | 2024-07-22 至 2024-07-28 | 阶段 02 | 已验收 | 添加指令加载、配置优先级、密钥遮蔽和模型错误处理。详见 `08_week_phase02_hardening_acceptance.plan.md` 和 `08_week_review.md`。 | 已验证：阶段 02 验收清单通过。 |
| 9 | 2024-07-29 至 2024-08-04 | 阶段 03 | 已稳固 | 添加工具注册表、工具执行器和工具调用转录记录。详见 `09_week_tool_registry_executor.plan.md` 和 `09_week_review.md`。 | 已验证：fake model + fake test tool 可以通过离线 agent 循环调用，工具失败和参数错误可记录到 transcript。 |
| 10 | 2024-08-05 至 2024-08-11 | 阶段 03 | 已稳固 | 添加工作区保护、文件读取工具和搜索工具。详见 `10_week_workspace_file_search_tools.plan.md` 和 `10_week_review.md`。 | 已验证：工作区外读取、`..`、前缀 sibling 绕过和 junction/symlink 越界在测试中被阻止；工作区内文本读取和搜索可用。 |
| 11 | 2024-08-12 至 2024-08-18 | 阶段 03 | 已稳固 | 添加 patch applier、dirty workspace 检测和文件编辑审批流程。详见 `11_week_patch_dirty_approval.plan.md` 和 `11_week_review.md`。 | 已验证：patch 可预览、审批、应用；目标文件预览后变化会拒绝应用。 |
| 12 | 2024-08-19 至 2024-08-25 | 阶段 03 | 已稳固 | 添加 shell runner、命令审批、危险命令阻断、超时和输出截断。详见 `12_week_shell_runner_approval.plan.md` 和 `12_week_review.md`。 | 已验证：无害命令审批后运行；危险模式、cwd 越界、超时和 stdout 截断路径有测试。 |
| 13 | 2024-08-26 至 2024-09-01 | 阶段 03 | 已验收 | 添加 git 工具并完成第一条读取、编辑、测试 MVP 工作流。详见 `13_week_git_tools_phase03_acceptance.plan.md` 和 `13_week_review.md`。 | 已验证：阶段 03 验收清单通过；git 测试使用临时仓库。 |
| 14 | 2024-09-02 至 2024-09-08 | 阶段 04 | 已稳固 | 添加 Microsoft Agent Framework 适配器项目或命名空间。详见 `14_week_agent_framework_adapter_skeleton.plan.md` 和 `14_week_review.md`。 | 已验证：适配器构建成功，且不需要修改 CLI 命令代码；direct 后端保持可用。 |
| 15 | 2024-09-09 至 2024-09-15 | 阶段 04 | 已稳固 | 将本地工具桥接到 Microsoft Agent Framework 后端。详见 `15_week_agent_framework_tool_bridge.plan.md` 和 `15_week_review.md`。 | 已验证：adapter bridge 可以调用读取和搜索工具，并保留失败/审批结构。 |
| 16 | 2024-09-16 至 2024-09-22 | 阶段 04 | 已稳固 | 添加后端配置开关和回退诊断。详见 `16_week_agent_backend_switch_diagnostics.plan.md` 和 `16_week_review.md`。 | 已验证：direct 和 framework 后端都可以通过配置选择；framework 缺失时 doctor 解释原因。 |
| 17 | 2024-09-23 至 2024-09-29 | 阶段 04 | 已验收 | 回归测试两个后端，并记录框架锁定边界。详见 `17_week_agent_framework_regression_acceptance.plan.md` 和 `17_week_review.md`。 | 已验证：direct 后端回归通过；framework 真实后端作为增强目标清晰标注未启用。 |
| 18 | 2024-09-30 至 2024-10-06 | 阶段 05 | 已稳固 | 添加 MCP 配置模型和 `mcp list` 命令。详见 `18_week_mcp_config_list.plan.md` 和 `18_week_review.md`。 | 已验证：MCP 配置可加载，已禁用 servers 保持 inactive。 |
| 19 | 2024-10-07 至 2024-10-13 | 阶段 05 | 已稳固 | 添加 MCP 连接管理器和 `mcp doctor`。详见 `19_week_mcp_connection_doctor.plan.md` 和 `19_week_review.md`。 | 已验证：已配置 servers 的连接诊断可用；disabled server 保持 inactive。 |
| 20 | 2024-10-14 至 2024-10-20 | 阶段 05 | 已稳固 | 将 MCP 工具桥接到通用工具注册表。详见 `20_week_mcp_tool_bridge.plan.md` 和 `20_week_review.md`。 | 已验证：MCP 工具在启用后可列出并调用；disabled server 不可执行。 |
| 21 | 2024-10-21 至 2024-10-27 | 阶段 05 | 已稳固 | 添加项目工作流注册表和验证 profiles。详见 `21_week_project_workflow_profiles.plan.md` 和 `21_week_review.md`。 | 已验证：工作流可以建议已配置的验证命令；路径来自 profile 或 `--workspace`。 |
| 22 | 2024-10-28 至 2024-11-03 | 阶段 05 | 已验收 | 添加面向计划文档和 C++ 验证的 Gerber/TIFF 工作流包。详见 `22_week_gerber_tiff_workflow_acceptance.plan.md` 和 `22_week_review.md`。 | 已验证：阶段 05 基础清单通过；真实 MCP/Gerber 执行作为增强目标清晰标注未启用。 |
| 23 | 2024-11-04 至 2024-11-10 | 阶段 06 | 已稳固 | 添加发布构建脚本和版本元数据。详见 `23_week_release_build_version.plan.md` 和 `23_week_review.md`。 | 已验证：`tools/Build-Release.ps1` 生成 `artifacts/release/caicli-0.1.0-win-x64/caicli.exe` 和 manifest。 |
| 24 | 2024-11-11 至 2024-11-17 | 阶段 06 | 已稳固 | 添加安装、配置、安全、MVP/增强能力状态和快速开始文档。详见 `24_week_docs_quickstart_security.plan.md` 和 `24_week_review.md`。 | 已验证：干净 user/workspace 下发布产物可运行 `version`、`doctor`，`chat` 缺 model/key 错误清晰。 |
| 25 | 2024-11-18 至 2024-11-24 | 阶段 06 | 已稳固 | 添加 smoke tests 和发布加固修复。详见 `25_week_smoke_tests_release_hardening.plan.md` 和 `25_week_review.md`。 | 已验证：smoke 脚本在干净测试工作区通过，覆盖缺 model/key、审批拒绝、路径越界、工具禁用、shell timeout、`run` 和 session export/clear。 |
| 26 | 2024-11-25 至 2024-12-01 | 阶段 06 | 已验收 | 最终验收、changelog、已知限制和第一个发布包。详见 `26_week_final_acceptance_release.plan.md` 和 `26_week_review.md`。 | 已验证：半年 MVP 验收清单通过，`caicli-0.1.0-win-x64.zip` 已生成并记录 SHA256。 |

## 周回顾模板

每周结束时使用：

```markdown
## 第 N 周回顾

状态：计划中 | 进行中 | 已稳固 | 已验收

已完成：
- 

验证：
- 命令：
- 结果：

运行时说明：
-

风险：
- 

第 N+1 周输入：
- 
```

## 月度里程碑

| 月份 | 预期结果 |
|---|---|
| 第 1 个月 | CLI 骨架、配置、工作区、doctor、测试 |
| 第 2 个月 | 真实模型 chat、流式输出、会话 |
| 第 3 个月 | 本地工具、安全门禁、文件编辑、shell 执行 |
| 第 4 个月 | direct 后端稳定；Microsoft Agent Framework 适配器作为增强目标验证 |
| 第 5 个月 | MCP 和项目工作流作为增强目标验证 |
| 第 6 个月 | 发布打包、文档、smoke tests、第一个本地发布版 |
