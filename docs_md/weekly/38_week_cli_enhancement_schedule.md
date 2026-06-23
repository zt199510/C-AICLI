# C# AI CLI Week 27-38 CLI 增强排期

更新时间：2026-06-09

## 目标

在不引入 TUI 的前提下，将 0.1.0 MVP CLI 增强为更接近 Codex CLI 的纯命令行开发助手。重点补齐配置、非交互执行、agent loop、审批权限、会话恢复、项目指令、代码审查、工具系统、MCP stdio、安全边界、日志 trace 和 0.2.0 发布验收。

## 当前基础

- 0.1.0 MVP 已通过第 26 周验收。
- 第 27 周配置/base URL 增强已验收，`config get/list/set/unset`、base URL source reporting 和 secret redaction 已进入 release docs。
- 第 28 周 `caicli exec`/NDJSON 事件流已验收，`exec` 与旧 `run` 兼容路径已进入 capability status。
- 已具备 `version`、`doctor`、`config get/list/set/unset`、`chat`、`chat --session`、`session export/clear`、`exec`、`run`、`tools list/call`、workspace read/search、patch、shell、git status/diff 和 Windows release package。
- 已知 Deferred 能力包括真实 Microsoft Agent Framework backend、真实 MCP 协议执行、Gerber/TIFF 真实工作流和 dotnet tool package。
- 本排期优先完善 direct CLI，不做 TUI。

## 阶段划分

| 阶段 | 周次 | 主题 | 目标 |
|---|---:|---|---|
| Phase 07 | 27-28 | 配置与非交互入口 | 补齐 base URL/config 命令，建立 `exec` 和 JSON event 基础。 |
| Phase 08 | 29-30 | Agent loop 与审批 | 支持模型驱动工具调用，并建立审批/权限 profile。 |
| Phase 09 | 31-33 | 会话、指令、开发命令 | 增强 session resume、`AGENTS.md`、`status/diff/review/models`。 |
| Phase 10 | 34-36 | 工具、MCP、安全边界 | 统一工具系统，启用 MCP stdio v1，强化 shell/patch/MCP 安全策略。 |
| Phase 11 | 37-38 | 可观测性与 0.2.0 发布 | 增加 trace/logs，完成 0.2.0 验收和发布包。 |

## 逐周计划

| 周 | 计划文档 | 状态 | 主要目标 | 周末验收 |
|---:|---|---|---|---|
| 27 | `27_week_config_base_url.plan.md` | 已验收 | 支持 `OPENAI_BASE_URL`、user config `baseUrl`、`config set/list/unset` 和配置诊断增强。 | `chat` 可使用自定义 base URL；`doctor/config` 显示来源且不泄露 key。 |
| 28 | `28_week_exec_json_events.plan.md` | 已验收 | 新增 `caicli exec` 和 `--json` NDJSON 事件流。 | exec 可用于脚本/CI；旧 `run` smoke 路径保持兼容。 |
| 29 | `29_week_agent_run_loop_v1.plan.md` | 已稳固 | `exec` 进入 agentic v1：模型事件、工具调用、结果回写、最终回答。 | fake model 可驱动工具调用；loop limits 和 `exec --session` transcript 生效。 |
| 30 | `30_week_approval_permission_profiles.plan.md` | 计划中 | 新增审批模式和工具风险等级。 | 默认不静默写文件/跑 shell；审批结果进入 text/JSON event。 |
| 31 | `31_week_session_resume_management.plan.md` | 计划中 | 增强 session 管理和 `chat/exec --resume`。 | session 可 list/show/rename/delete/export markdown。 |
| 32 | `32_week_project_instructions_agents_md.plan.md` | 计划中 | 支持 `AGENTS.md` 和层级项目指令。 | chat/exec 使用 workspace 指令，doctor 显示来源。 |
| 33 | `33_week_diff_review_status_models.plan.md` | 计划中 | 新增 `status`、`diff`、`review`、`models`。 | review 默认只读；models 不需要 API key。 |
| 34 | `34_week_tool_system_hardening.plan.md` | 计划中 | 工具 schema、stdin、JSON list、错误码和 structured result 统一。 | `tools list --json` 稳定可解析；工具错误码稳定。 |
| 35 | `35_week_mcp_real_stdio_v1.plan.md` | 计划中 | 启用 MCP stdio 真连接 v1。 | fake MCP server 可 handshake/list/call；remote MCP 仍 Deferred。 |
| 36 | `36_week_security_sandbox_boundaries.plan.md` | 计划中 | 强化 shell policy、patch preview、MCP 启动安全策略。 | denylist/timeout/risk summary 生效。 |
| 37 | `37_week_observability_logs_trace.plan.md` | 计划中 | 增加 `--verbose`、`--trace`、`logs show/clear/path`。 | 可追踪 exec 的模型/工具/审批顺序，secret 不泄露。 |
| 38 | `38_week_cli_0_2_release_acceptance.plan.md` | 计划中 | 0.2.0 发布验收、文档、smoke tests 和 release zip。 | build/test/smoke 通过，0.2.0 zip size/SHA256 记录。 |

## 设计原则

- 继续以 direct OpenAI backend 为产品核心，Microsoft Agent Framework 维持适配边界。
- CLI 优先，不做 TUI，不引入图形依赖。
- 所有写文件、shell、MCP server 启动都必须经过清晰权限策略。
- 所有 secret 只记录 presence/source，不记录值。
- 真实网络或真实模型调用不作为单元测试前提；测试使用 fake model/fake MCP server。
- 每周结束必须创建对应 `NN_week_review.md`，记录验证命令、结果、风险和下一周输入。

## 0.2.0 预期能力

- 已验收：可配置 OpenAI 官方或兼容服务 base URL。
- 已验收：可用 `caicli exec` 进行非交互任务执行，并支持 text/NDJSON 输出。
- direct backend 具备 agent loop v1。
- 审批和权限 profile 可配置、可诊断。
- session 可恢复、可管理、可导出。
- `AGENTS.md` 项目指令兼容。
- `status/diff/review/models` 开发命令可用。
- 工具系统输出稳定 JSON 和结构化错误。
- stdio MCP server 可真实 handshake/list/call。
- 日志和 trace 可定位一次任务的完整执行链。

## Deferred 边界

- TUI 不在 Week 27-38 范围内。
- remote MCP transport 不在 Week 35 的 v1 范围内。
- Microsoft Agent Framework real backend 不在 0.2.0 核心路径内。
- Gerber/TIFF 真实工具链执行不在本轮 CLI 增强范围内。
- dotnet tool package 可作为 0.2.x 后续发布目标。

## 执行建议

- 每周开始时先将对应 `.plan.md` 拆成更细的 TDD 实施任务。
- 对于 Week 29、35、36、38，优先使用 Subagent-Driven 方式，因为这些周涉及多个独立子系统。
- 每周至少运行：

```powershell
dotnet build src/CSharpAiCli.sln -c Release
dotnet test src/CSharpAiCli.sln -c Release
```

- Week 38 额外运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1
```
