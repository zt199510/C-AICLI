# C# AI CLI Week 39-46 0.3.0 真实 Agent 开发排期

更新时间：2026-07-10

## 目标

在 0.2.0 可信 CLI Agent 底座之上，将 `caicli exec` 从 fake/offline agent loop 和工具基础设施推进到真实模型驱动的开发闭环。

0.3.0 的核心目标不是增加更多命令，而是让一个真实模型可以在受控边界内完成小型开发任务：

```text
理解任务 -> 收集上下文 -> 制定有限计划 -> 调用工具 -> 修改文件 -> 运行验证 -> 根据失败反馈迭代 -> 输出可复核总结
```

推荐首个能力场景：修 bug / 小改动。

## 当前基础

- 0.2.0 已完成 Week 34-38 验收，具备可信 CLI 底座；0.2.1 readiness patch 将默认 `exec` 接入 direct OpenAI agent abstraction，便于 Week 39 只聚焦 SDK tool-call/result translation。
- 已具备 `exec`、NDJSON event stream、session resume、approval profile、workspace guard、patch、shell、git status/diff、MCP stdio v1、structured tool result、stable tool error code、trace/logs 和 release smoke。
- 已具备 fake/offline agent loop，可以用于确定性测试。
- direct OpenAI backend 仍是产品核心路径。
- project instructions 支持 `AGENTS.md` 和 `AICLI.md` fallback。
- 所有写文件、shell、MCP server 启动仍必须经过权限策略和风险摘要。
- 真实网络或真实模型调用不作为单元测试前提；真实模型 smoke 必须 opt-in。

## 0.3.0 成功标准

0.3.0 验收时，至少应满足以下结果：

- `caicli exec "修复一个小 bug"` 可以由真实模型驱动 read/search/patch/shell/git 工具完成闭环。
- 模型工具调用 continuation 可真实运行，并能把 tool result 回写给模型继续推理。
- agent loop 有明确 step budget、tool budget、token/output budget 和超时边界。
- 默认不会静默写文件或运行 shell；审批、拒绝、风险摘要在 text/JSON/trace 中可见。
- patch 后可运行配置或推断出的验证命令，并能把失败结果反馈给模型进行有限重试。
- 最终输出包含 changed files、commands run、tests/verification result、remaining risks 和 trace/log path。
- 离线 fake model end-to-end 测试覆盖主要闭环；真实模型 smoke 使用 opt-in 环境变量或脚本开关。
- release docs 准确标注 Accepted、Solidified 和 Deferred 能力。

## 非目标

以下能力不进入 0.3.0 主线：

- 桌面 App、Electron/Web UI、TUI。
- 完整多 Agent 团队协作。
- 自动 model role routing。
- Skills marketplace 或复杂 workflow packs。
- remote MCP transport。
- Microsoft Agent Framework real backend。
- Gerber/TIFF 真实工具链执行。
- 企业 IM、远程控制、团队知识库。

这些能力可作为 0.3.x、0.4.0 或 0.5.0 后续目标。

## 阶段划分

| 阶段 | 周次 | 主题 | 目标 |
|---|---:|---|---|
| Phase 12 | 39-40 | 真实模型工具调用闭环 | 实现 direct backend 的真实 tool-call continuation，并建立可控 agent state machine。 |
| Phase 13 | 41-42 | 上下文、编辑与验证 | 加强上下文收集、patch 应用、验证命令执行和 changed files 记录。 |
| Phase 14 | 43-44 | 失败反馈与复核 | 支持测试失败反馈、有限重试、只读复核和最终任务报告。 |
| Phase 15 | 45-46 | 真实 smoke 与 0.3.0 发布 | 增加 opt-in real model smoke，完成文档、验收和 release package。 |

## 状态说明

- `计划中` 表示已有目标和验收范围，尚未创建 week review。
- `已稳固` 表示功能契约、离线测试和主要错误路径稳定，但仍需在 release acceptance 中回归。
- `已验收` 表示已有对应 `NN_week_review.md`，并记录 build/test/docs/smoke 证据。

## 逐周计划

| 周 | 计划文档 | 状态 | 主要目标 | 周末验收 |
|---:|---|---|---|---|
| 39 | `39_week_real_model_tool_continuation.plan.md` | 计划中 | 为 direct backend 实现真实模型 tool-call continuation，支持模型请求本地工具、工具结果回写、模型继续生成。 | fake model 与真实模型接口共用契约；真实模型 smoke opt-in；无 key 时错误清晰且脱敏。 |
| 40 | `40_week_agent_state_machine_limits.plan.md` | 计划中 | 抽出 agent state machine，统一 step/tool/time/output budget、stop reason、loop error 和 JSON event。 | loop limit、tool failure、approval denied、model stop reason 均有稳定事件和测试。 |
| 41 | `41_week_context_gathering_planning.plan.md` | 计划中 | 增强任务上下文收集：repo status、project instructions、relevant files、read/search 策略和有限 plan。 | `exec` 能先收集上下文并形成可追踪 plan；不会读取 workspace 外文件。 |
| 42 | `42_week_patch_verify_workflow.plan.md` | 计划中 | 串联 patch、changed files、git diff、验证命令执行和验证结果回写。 | 小改动任务可修改文件、展示 changed files、运行验证命令并记录 result。 |
| 43 | `43_week_failure_feedback_retry.plan.md` | 计划中 | 将测试失败、shell failure、tool error 反馈给模型，支持有限重试和明确失败收敛。 | fake end-to-end 覆盖一次失败后修复；超过 retry budget 时输出可复核失败报告。 |
| 44 | `44_week_review_gate_task_report.plan.md` | 计划中 | 增加只读复核 gate 和任务报告：diff summary、commands、tests、risks、trace path。 | `exec` 最终回答包含可复核 summary；report 不泄露 secret；review gate 不写文件。 |
| 45 | `45_week_real_agent_smoke_docs.plan.md` | 计划中 | 扩展 smoke：离线 end-to-end、opt-in real model、approval/security regression；更新 docs。 | smoke 覆盖真实 agent 关键路径；真实模型 smoke 默认跳过但文档清晰。 |
| 46 | `46_week_cli_0_3_release_acceptance.plan.md` | 计划中 | 0.3.0 发布验收、版本元数据、CHANGELOG、capability status、release zip。 | build/test/smoke 通过；0.3.0 zip size/SHA256 记录；Deferred 边界准确。 |

## 关键设计原则

- Runtime-first：先把 Core agent loop 做稳定，再考虑 UI 或桌面入口。
- 真实模型路径和 fake model 路径共享同一套 tool contract、event contract 和 transcript contract。
- 单元测试不依赖真实网络；真实模型测试必须显式 opt-in。
- 所有工具调用必须经过统一 tool registry、structured result 和 error code。
- 写文件、shell、MCP 启动必须尊重 approval profile、workspace guard 和 risk summary。
- 每一次 agent 任务必须可追踪：session、event stream、trace/log、changed files 和最终 summary 对齐。
- 首个场景只做小型 bugfix / 小改动，不扩展到大型功能开发。
- 不以“更像 Codex/Claude”为验收标准；以可控、可审计、可验证的工程闭环为验收标准。

## 0.3.0 预期能力

- 真实模型可驱动本地工具调用，并能多轮 continuation。
- `exec` 可执行受限开发闭环，而不是只输出回答。
- agent loop 有明确预算、失败路径和 stop reason。
- patch、shell、git、MCP 工具结果可被模型继续消费。
- 验证命令结果可反馈给模型进行有限修复。
- 最终任务输出可复核，包含文件变更、命令、测试、风险和 trace/log。
- 离线测试覆盖真实 agent 语义，真实模型 smoke 可在有凭据时启用。

## Deferred 边界

- `@file` / `@folder` 引用进入 0.3.1-0.3.2 候选。
- `caicli changes`、markdown report 和 richer task history 进入 0.3.1-0.3.2 候选。
- model role routing 进入 0.3.2-0.3.3 候选。
- `skills list/run` 和 workflow packs 进入 0.3.3 候选。
- daemon/API、多角色 agent、job queue 进入 0.4.0 候选。
- Electron/Web UI 进入 0.5.0 候选。

## 风险与控制

| 风险 | 控制策略 |
|---|---|
| 真实模型工具调用不稳定 | fake model contract 先行；真实模型 smoke opt-in；失败路径结构化记录。 |
| agent loop 无限循环 | step/tool/time/output budget 必须统一进入 state machine。 |
| 模型误改文件 | patch preview、approval、dirty workspace guard、changed files summary 必须保留。 |
| shell 风险扩大 | shell policy、denylist、timeout、risk summary 和 approvalStatus 必须回归测试。 |
| 上下文过大或读取无关文件 | 初期只做 bounded context；优先 status、diff、instructions、search/read 结果。 |
| 测试失败后反复试错 | retry budget 默认小；超过预算输出失败报告，不继续盲目修改。 |
| 真实模型测试污染 CI | 单元测试使用 fake；real smoke 默认跳过并要求显式环境变量。 |

## 每周验证基线

每周至少运行：

```powershell
dotnet build src/CSharpAiCli.sln -c Release
dotnet test src/CSharpAiCli.sln -c Release
```

涉及 release 或 smoke 的周额外运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1
```

Week 46 额外运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SmokeTests.ps1
```

## 周回顾模板

每周结束时创建对应 `NN_week_review.md`：

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
