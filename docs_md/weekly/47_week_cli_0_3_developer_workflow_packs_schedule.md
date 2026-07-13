# C# AI CLI 0.3.1-0.3.3 Developer Workflow Packs 排期

更新时间：2026-07-12

## 起点确认

0.3.0 已完成并验收为当前 release。`docs_md/release/final_acceptance_0.3.0.md` 已记录 release artifact、deterministic zip、packaged smoke、capability status 和 Deferred 边界。

本次进入 0.3.1-0.3.3 规划前已复核：

- `dotnet build src\CSharpAiCli.sln -c Release` 通过，0 warnings，0 errors。
- `dotnet test src\CSharpAiCli.sln -c Release --no-build` 通过，1020 passed，0 failed，0 skipped。
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1` 通过；默认 real model smoke 按设计跳过。

当前可用基础：

- `exec` 已具备真实 OpenAI SDK tool-call continuation、fake/offline contract、bounded loop、patch/verification/failure retry、`review.gate` 和 `taskReport`。
- `diff`、`review`、`session export`、trace/logs、workflow profile、MCP stdio v1 和 project pack skeleton 已可作为 workflow pack 基础能力。
- 完整 `@file`/`@folder`、独立 markdown report、expert profiles、本地 `skills list/run` 和复杂 workflow packs 仍未启用。

## 产品目标

0.3.1-0.3.3 的目标是把 0.3.0 的真实 agent 开发闭环包装成可重复、可复核、可配置的 CLI developer workflows：

```text
限定上下文 -> 选择工作流/专家 -> 执行受控 agent 闭环 -> 生成报告 -> 复核 changes
```

这条线借鉴 WorkBuddy 的 Skills、Experts 和交付物思路，但保持 C-AICLI 的定位：Windows-first、C#/.NET-first、私有模型友好、CLI-only、可审计。

## 非目标

- 不做桌面 App、Electron/Web UI、TUI 或 IM 控制。
- 不做远程 marketplace、云端技能分发或团队知识库。
- 不在 0.3.x 做自动 model role routing；expert 先作为本地 profile 和提示/边界约束。
- 不在 0.3.x 启用 Gerber/TIFF 真实工具链执行。
- 不让 workflow pack 绕过 approval、workspace guard、secret redaction、shell policy、trace/log 或 release smoke。

## 版本拆分

| 版本 | 主题 | 目标 | 核心交付 |
|---|---|---|---|
| 0.3.1 | Workflow Inputs 与 Changes View | 让用户精确限定上下文，并复核任务前后变化。 | `@file`/`@folder` bounded reference、changes view、text/JSON/trace/session 覆盖。 |
| 0.3.2 | Reports 与 Expert Profiles | 把 `taskReport` 升级为可交付报告，并提供常见专家模式。 | `exec --report markdown`、report path/stdout 策略、`--expert` profiles、只读 reviewer/security 边界。 |
| 0.3.3 | Local Skills 与 .NET Workflow Packs | 引入轻量本地 skills/workflow packs，优先服务 .NET 工作流。 | `skills list/run` 或等价命令、最小 pack manifest、内置 .NET packs。 |

## 0.3.1：Workflow Inputs 与 Changes View

目标：把“模型自己猜上下文”降到最低，让用户能显式给出任务范围，并在执行后快速复核变更。

范围：

- 支持 `@file` 和 `@folder` 引用，优先进入 `exec`；`chat` 可作为后续兼容目标。
- 引用解析必须基于 workspace/cwd，遵守 workspace guard，拒绝 workspace 外路径。
- `@folder` 必须有数量、总字节、单文件大小和递归深度边界；默认尊重常见忽略目录，例如 `.git`、`bin`、`obj`、`node_modules`。
- 在 context/plan/taskReport 中记录引用来源、解析结果、跳过原因和截断警告，但不泄露 secret 值。
- 增加 `caicli changes` 或等价 changes view，用于整合 git status/diff、changed files、commands、verification result、remaining risks、trace/session path。
- changes view 必须只读，不写文件、不运行 shell、不调用模型，除非用户显式走 `review` 或 `exec`。

验收标准：

- `@file`/`@folder` 在 text、JSON event、trace 和 session/report 中可追踪。
- workspace 外路径、过大目录、二进制/不可读文件有稳定错误或 warning。
- changes view 能在有变更、无变更、非 git workspace、dirty workspace 和 session report 缺失时给出稳定输出。
- Release build/test/smoke 通过，默认 smoke 不依赖模型凭据。

## 0.3.2：Reports 与 Expert Profiles

目标：让每次 agent 任务形成更稳定的交付物，并让常见开发角色变成显式 CLI 选项。

范围：

- 支持按需 markdown report，例如 `exec --report markdown`；默认不静默写额外文件。
- 明确 report 输出策略：stdout、显式 `--report-path`，或 session/trace 引用；禁止覆盖 workspace 文件时绕过审批。
- report 内容至少包含 prompt、workspace/cwd、references、plan、changed files、commands、verification、review gate、remaining risks、trace path、session id 和 redaction 说明。
- 增加 `--expert bugfix/reviewer/tester/security/refactor` 本地 profiles。
- `reviewer` 和 `security` 默认只读；`bugfix` 和 `refactor` 可写但仍必须走 approval、patch preview、dirty workspace guard 和 verification。
- expert profile 不做自动 model routing，只调整系统/开发提示、工具边界、输出要求和默认报告重点。

验收标准：

- markdown report 与 text/JSON `taskReport` 字段一致，secret 只记录 presence metadata。
- expert profile 的行为边界可测试，尤其是只读 profile 不会写文件或运行 shell。
- 报告失败、无变更、verification failed、approval denied 和 retry exhausted 都有可读交付物。
- Release docs 标注 report/expert 的当前能力和 Deferred 边界。

## 0.3.3：Local Skills 与 .NET Workflow Packs

目标：在输入、changes、report 和 expert 稳定后，引入轻量本地 pack，让常见 .NET 工程任务可复用。

范围：

- 增加 `skills list/run` 或等价 CLI，先支持内置和本地目录，不做远程 marketplace。
- 定义最小 pack manifest：`name`、`description`、`version`、`appliesTo`、`expert`、`references`、`instructions`、`validationCommand`、`reportTemplate`、`safety`。
- 与现有 `workflow list/validate` 对齐：workflow profile 提供验证命令和项目匹配，skill/workflow pack 提供任务模板、专家 profile、引用策略和报告形态。
- 首批内置 .NET packs 候选：`test-fix`、`review-only`、`upgrade-package`、`doc-sync`。
- `CSharpAiCli.ProjectPacks` 保持 project/domain pack 边界；Gerber/TIFF 真实执行仍 Deferred 到 0.5.0+。

验收标准：

- `skills list` 可列出内置和本地 packs，输出 text/JSON。
- `skills run <pack>` 能展开为受控 `exec` 请求或只读 review/report 请求，并保留完整 trace/session/report。
- pack manifest 有 schema validation、错误码、docs 和 tests。
- pack 不能扩大工具权限；所有写文件、shell、MCP 启动仍通过现有 approval/security path。

## 共同设计约束

- 所有新增命令都应支持稳定 text 输出；面向工具集成的命令优先提供 JSON 输出。
- 所有路径输入都必须走统一 workspace path resolver，不做 ad hoc 字符串拼接。
- 所有报告都必须复用现有 `taskReport`/trace/session/redaction 契约，避免产生第二套事实来源。
- workflow pack 是 CLI 层的可复用任务边界，不是 UI、daemon、远程自动化或企业平台。
- 每个版本都需要同步更新 release docs、capability status、known limitations、quickstart/troubleshooting 和 smoke。

## 验证基线

每个 0.3.x 小版本至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

发布候选还需要运行 `tools\Build-Release.ps1` 并记录 release zip size/SHA256。

## 建议实现顺序

1. 0.3.1 先实现引用解析与 context/report 事件契约，再做 changes view。
2. 0.3.2 先让 markdown report 从现有 `taskReport` 单源生成，再加入 expert profile。
3. 0.3.3 先定义 pack manifest 和内置 pack registry，再实现 `skills list/run`。

