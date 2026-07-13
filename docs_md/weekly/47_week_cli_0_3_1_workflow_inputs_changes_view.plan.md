# 第 47 周 0.3.1 Workflow Inputs 与 Changes View Implementation Plan

状态：已实现，待 0.3.1 release acceptance。

**Goal:** 在不改变 0.3.0 已验收能力边界的前提下，为 `exec` 增加显式 bounded workflow references，并提供只读 changes view，让用户能限定上下文并复核任务变化。

## 来源

- `docs_md/spec/product_positioning_and_roadmap.md`
- `docs_md/release/final_acceptance_0.3.0.md`
- `docs_md/weekly/47_week_cli_0_3_developer_workflow_packs_schedule.md`

## 版本边界

本计划只覆盖 `0.3.1`：

- 支持 `@file` / `@folder` bounded reference for `exec`。
- 增加 `caicli changes` 或等价只读 changes view。
- 扩展 text、JSON、trace、session、taskReport 可追踪性。
- 补齐单元测试、集成测试、默认 smoke 和 release docs。

本计划明确不做：

- 不实现 0.3.2 的 `exec --report markdown`、独立 markdown report 文件、`--expert` profiles。
- 不实现 0.3.3 的 `skills list/run`、workflow pack manifest 或内置 .NET packs。
- 不改变 0.3.0 已验收的 direct OpenAI SDK agent loop、fake/offline contract、bounded `exec`、approval、patch、verification、failure retry、`review.gate`、`taskReport`、stdio MCP v1 能力边界。
- 不新增桌面 UI、TUI、daemon、远程 marketplace、自动 model routing、Gerber/TIFF 真实执行。

## 用户入口草案

### `exec` references

首版建议采用 inline token 语法：

```powershell
caicli exec "Fix null handling using @file:src/App.cs"
caicli exec "Review parser behavior in @folder:src/CSharpAiCli.Core/Agents"
caicli exec --output json --trace "Summarize @file:docs_md/release/capability_status.md"
caicli exec --session smoke "Fix with @file:src/App.cs and @folder:tests"
```

解析规则：

- `@file:<relative-path>`：解析单个 workspace 内文本文件。
- `@folder:<relative-path>`：递归解析 workspace 内目录中的文本文件集合。
- 路径解析基于现有 `WorkspaceGuard` 与 `--workspace` / `--cwd` 上下文；绝不允许 workspace 外路径或 reparse/symlink 逃逸。
- 路径带空格的 quoted form 可作为实现时的兼容目标，例如 `@file:"docs/My File.md"`；若实现成本过高，0.3.1 可先稳定拒绝并文档化。

### `changes` view

建议新增：

```powershell
caicli changes
caicli changes --output json
caicli changes --json
caicli changes --session smoke
caicli changes --session smoke --trace
```

行为：

- 默认只读汇总当前 workspace 的 git status、diff stat、changed files。
- 如果提供 `--session <name>`，再读取最近一次 `agentRuns[]` 的 task report，展示 commands、verification、remaining risks、trace path、session path。
- 不调用模型，不调用 `workspace.run_shell`，不写 workspace 文件，不产生 patch，不启动 MCP。
- 默认不写 session/report。`--trace` 仅沿用现有显式诊断路径写入 `.caicli/logs` 中的 redacted trace。

## 实施设计

### 1. Reference model 与 resolver

新增核心模型，建议放在 `CSharpAiCli.Core/Agents` 或 `CSharpAiCli.Core/WorkflowInputs`：

- `WorkflowReferenceToken`
- `WorkflowReferenceSet`
- `WorkflowReferenceResult`
- `WorkflowReferenceFile`
- `WorkflowReferenceWarning`
- `WorkflowReferenceResolverOptions`

默认 bounds 建议：

- 单次 `exec` 最多 20 个 reference token。
- `@file` 单文件最多 64 KiB 纳入 prompt context；超过后截断并记录 warning。
- `@folder` 最多 50 个文件、总计 256 KiB、递归深度 4。
- 默认忽略 `.git`、`.caicli/logs`、`bin`、`obj`、`node_modules`、`.vs`、`.idea`。
- 二进制、不可读、超出单文件限制的 folder child 以 warning/skip 记录；显式 `@file` 的 workspace boundary、missing path、binary/unreadable 错误应在进入模型前稳定失败。
- 所有 metadata、warning、error code 必须可序列化，且输出前经过现有 secret redaction。

### 2. `exec` context 集成

扩展 `AgentTaskContext`：

- 增加 `References` 字段，记录 source token、kind、requested path、resolved relative path、included file count、skipped count、byte count、truncated、warnings、error codes。
- `AgentTaskContextCollector` 在收集 git/instructions/session context 前后解析 references；解析失败时返回可渲染的 failure result，不能让 runner 在缺失强引用上下文时继续执行。
- `AgentTaskContextPromptFormatter` 将 bounded reference content 放入模型启动 prompt，但只作为显式用户输入上下文，不扩大工具权限。
- `AgentStartupPlanBuilder` 在 plan summary 中显示 reference scope 和 truncation risk。
- Offline/fake runner 与 direct OpenAI runner 继续共享同一 `AgentRunRequest` / `AgentTaskContext` 契约。

### 3. `exec` 输出与可追踪性

新增或扩展事件：

- `context.references`：在 model/tool 前输出 reference 解析摘要。
- `taskReport` payload 增加 `references` 摘要。
- `exec.result.payload.taskReport.references` 与 trace 中保持同源。
- session transcript `agentRuns[].taskReport.references` 保留结构化摘要。
- markdown session export 只渲染 compact counts/path/status，不输出原始 reference content。

Text 输出验收：

- text event 包含 `event: context.references`。
- final result 包含 reference count / truncated / warning 摘要。
- 失败时输出稳定 `errorCode`，例如 `workflow-reference-boundary-denied`、`workflow-reference-not-found`、`workflow-reference-binary-not-supported`、`workflow-reference-too-large`。

JSON/trace/session/report 验收：

- JSON mode 输出 NDJSON `context.references` event。
- final `exec.result.payload.taskReport.references` 可被机器读取。
- trace 与 JSON 字段命名一致，且 redaction 不泄露 secret-like values。
- `session export --format json` 可看到 taskReport references；markdown export 只显示 compact summary。

### 4. `changes` view 服务与命令

新增只读服务和 DTO，建议复用现有 `GitStatusTool`、`GitDiffTool`、`ConversationStore`、`AgentTaskReport`：

- `ChangesViewRequest`
- `ChangesViewReport`
- `ChangesTextRenderer`
- `ChangesJsonRenderer`

输出内容：

- workspace root / cwd。
- git status summary、dirty flag、diff stat、changed file list。
- task report source：`none`、`session:<name>`、`missing`、`invalid`。
- commands、verification statuses、remaining risks、trace path、session path。
- warnings：non-git workspace、truncated diff、missing session、missing taskReport、invalid transcript。

Exit code 建议：

- workspace 不可用或 option validation 失败：1。
- non-git workspace、missing session report、no changes：0，并通过 `status: warning|clean|dirty` 与 warnings 表达。
- git read failure 且无法形成 report：1。

只读边界：

- 只允许调用 read-only git/status/diff collector 与 session store read。
- 不调用 model。
- 不调用 `workspace.run_shell`、`workspace.apply_patch`、MCP tools。
- 默认不写 command/session/report；`--trace` 为显式诊断写入，必须 redacted。

### 5. 测试计划

新增或扩展测试：

- `WorkflowReferenceParserTests`
- `WorkflowReferenceResolverTests`
- `AgentTaskContextCollectorTests`
- `AgentTaskContextPromptFormatterTests`
- `AgentStartupPlanBuilderTests`
- `AgentTaskReportTests`
- `ExecRendererTests`
- `TraceLoggerTests`
- `ConversationTranscriptMarkdownFormatterTests`
- `CliCommandFactoryTests`
- `SmokeTestScriptTests`

覆盖场景：

- `@file` 成功、missing、workspace 外、binary、too large/truncated、secret redaction。
- `@folder` 成功、ignore dirs、max files、max bytes、max depth、binary/unreadable skipped、all files skipped。
- `exec --output json` 中 reference event 与 final taskReport payload。
- `exec --session` 后 `session export json|markdown` 中 references 摘要。
- `caicli changes` 在 dirty、clean、non-git、missing session、missing taskReport、invalid transcript 场景的 text/json 输出。
- `changes` 不调用模型、不调用 shell/patch/MCP、不修改 workspace 用户文件。
- `--trace` 输出 redacted changes/exec reference diagnostics。

### 6. Smoke 与文档

默认 smoke 新增离线路径：

- `caicli changes --workspace <git workspace>`
- `caicli changes --output json --workspace <git workspace>`
- `caicli changes --session smoke --workspace <workspace>`，覆盖缺少 taskReport 时的稳定 warning。
- `caicli exec --workspace <workspace> --output json "Summarize @file:note.txt"` 在缺 model/key 场景下仍能验证 reference parsing failure/success event，不依赖真实模型。

Release docs 需要在实现后更新：

- `docs_md/release/CHANGELOG.md`
- `docs_md/release/capability_status.md`
- `docs_md/release/known_limitations.md`
- `docs_md/release/quickstart.md`
- `docs_md/release/security_model.md`
- `docs_md/release/troubleshooting.md`
- `docs_md/spec/runtime_logging_diagnostics.md`
- `tools/Invoke-SmokeTests.ps1`
- `src/CSharpAiCli.Tests/SmokeTestScriptTests.cs`

## 任务清单

- [x] Step 1: 确认 `@file:` / `@folder:` token 语法和 folder bounds 常量。
- [x] Step 2: 新增 reference parser、resolver、DTO 与 resolver options。
- [x] Step 3: 将 references 接入 `AgentTaskContext`、prompt formatter、startup plan。
- [x] Step 4: 将 reference diagnostics 接入 `exec` text/JSON events、trace、taskReport、session export。
- [x] Step 5: 新增 `caicli changes` text/json 命令和只读 changes view service。
- [x] Step 6: 增加 reference parser/resolver/context/report/render/session/trace 测试。
- [x] Step 7: 增加 changes view CLI/service/json/text/session/trace 测试。
- [x] Step 8: 更新 smoke script 与 smoke script tests。
- [x] Step 9: 更新 release docs、capability status、known limitations、runtime logging diagnostics。
- [x] Step 10: 运行 `dotnet build src\CSharpAiCli.sln -c Release`。
- [x] Step 11: 运行 `dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- [x] Step 12: 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`。
- [x] Step 13: 创建 `47_week_review.md`，记录实际验收、风险、Deferred 边界。

## 验收矩阵

| 功能 | 用户入口 | 行为边界 | 测试覆盖 | 文档/Smoke | 验收标准 |
|---|---|---|---|---|---|
| `@file` bounded reference for `exec` | `caicli exec "Task using @file:src/App.cs"`；支持 text 与 `--output json` | 只读解析 workspace 内文本文件；走 `WorkspaceGuard`；workspace 外、missing、binary/unreadable 稳定失败；大文件按 64 KiB 截断并 warning；不扩大 patch/shell/MCP 权限 | Parser/resolver 单测；`CliCommandFactoryTests` 覆盖 text/json；`ExecRendererTests` 覆盖 event/result；`TraceLoggerTests` 覆盖 redaction | quickstart/security/capability/known limitations 需更新；smoke 覆盖缺模型场景的 reference parsing | text 有 `context.references` 与 final reference summary；JSON/trace/session/taskReport 有 `references[]` metadata；不输出 raw secret-like values；Deferred：不支持 chat references、URL references、glob expansion |
| `@folder` bounded reference for `exec` | `caicli exec "Review @folder:src/CSharpAiCli.Core/Agents"` | 只读递归；默认忽略 `.git`、`.caicli/logs`、`bin`、`obj`、`node_modules`；限制文件数/总字节/单文件/深度；binary/unreadable child skip warning；路径逃逸硬失败 | Resolver 集成测试覆盖 ignore、depth、count、byte、binary skip；prompt formatter/task report 测试覆盖 truncation | quickstart/security/known limitations 需说明 bounds；smoke 可用小目录验证 JSON reference count | text/json/trace/session/report 都显示 folder requested/resolved、included/skipped/truncated；不包含 raw folder content 于 report；Deferred：不做 embedding/vector retrieval、semantic folder selection、pack-level references |
| Reference diagnostics and failure handling | `caicli exec --output json "Use @file:../outside.txt"`；`caicli exec "Use @folder:too-large"` | 在进入模型前完成 validation；边界错误不调用模型、不运行工具；warnings 不绕过审批；所有错误码稳定 | 单测覆盖 error codes；CLI 测试断言 model factory/runner 未调用；JSON NDJSON 测试覆盖 terminal failure | troubleshooting 需列出常见 reference 错误；smoke 覆盖 workspace boundary | text 输出 `status: failed` / `errorCode` / summary；JSON 输出 terminal `exec.result`；trace 记录 failure 且 redacted；session 不写入 partial agent run；Deferred：不做交互式选择/确认 UI |
| `caicli changes` text view | `caicli changes`；`caicli changes --session smoke` | 只读；不调用模型、不写 workspace、不执行 shell/patch/MCP；只复用 read-only git collector 和 session read；non-git/missing report 输出 warning | `CliCommandFactoryTests` + service tests 覆盖 dirty、clean、non-git、missing session、missing taskReport、invalid transcript | quickstart/capability/security/troubleshooting 更新；smoke 覆盖 dirty git workspace 与 missing taskReport | text 稳定显示 status、git status、diff stat、changed files、commands、verification、risks、trace/session path；clean/no changes 输出明确；Deferred：不做模型 review、不做 markdown report |
| `caicli changes` JSON view | `caicli changes --output json` 或 `--json` | JSON schema 稳定；不包含 raw secrets；exit code 与 text view 一致；字段来自 git/session/taskReport 单一事实源 | JSON renderer tests；CLI tests 验证 schema、warnings、redaction、exit code | smoke 新增 JSON assertion；runtime logging diagnostics 记录 schema | 输出单个 JSON object，含 `type:"changes.view"`、`status`、`git`、`changedFiles`、`taskReport`、`warnings`；Deferred：不做 NDJSON stream、不做 report file emission |
| Changes trace/session/report linkage | `caicli changes --session smoke --trace`；`caicli exec --session smoke ...` 后再 changes | 默认不写 session/report；显式 `--trace` 只写 redacted diagnostics；读取 session 只看 latest agent run/taskReport；missing/invalid transcript warning | `TraceLoggerTests` 或 command trace tests；session store failure tests；markdown/json export tests | runtime logging diagnostics、security model 更新；smoke 可选 trace path 检查 | trace 记录 changes view command/result；session path 和 trace path 在 text/json 可复核；不把 changes view 当作 correctness proof；Deferred：不实现 standalone markdown report 或 historical diff timeline |
| Smoke/release docs hardening | release smoke 与 docs | 默认 smoke 不需要模型凭据；真实模型 smoke 仍 opt-in；不改变 0.3.0 release artifact 边界直到发布候选 | `SmokeTestScriptTests` 覆盖新增 smoke 命令；build/test/smoke 全量通过 | `CHANGELOG`、`capability_status`、`known_limitations`、`quickstart`、`security_model`、`troubleshooting` 更新 | 0.3.1 docs 准确区分 current vs Deferred；default smoke local-only；Deferred 明确包含 0.3.2 report/expert 与 0.3.3 skills/packs |

## 验证基线

实现完成后至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

发布候选再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
```

并在 release acceptance 中记录 zip size/SHA256。

## 待确认问题

1. 已确认：0.3.1 只采用 `@file:` / `@folder:` inline token，不增加 `--ref`。
2. 已确认：`@file` 超过单文件上限采用截断 warning；binary/unreadable 仍 hard failure。
3. 已确认：`changes` 默认不写 command log；只有显式 `--trace` 或 `CAICLI_TRACE=1` 走诊断 trace。
