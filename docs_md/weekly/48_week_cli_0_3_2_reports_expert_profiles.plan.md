# 第 48 周 0.3.2 Reports 与 Expert Profiles Implementation Plan

状态：已执行

**Goal:** 在 0.3.1 workflow references 与 changes view 稳定后，把 `exec` 的 `taskReport` 升级为按需可交付 markdown report，并增加受控的本地 expert profiles，让常见开发角色可以通过 CLI 明确选择。

## 来源

- `docs_md/spec/product_positioning_and_roadmap.md`
- `docs_md/weekly/47_week_cli_0_3_developer_workflow_packs_schedule.md`
- `docs_md/weekly/47_week_cli_0_3_1_workflow_inputs_changes_view.plan.md`
- `docs_md/weekly/47_week_review.md`
- `docs_md/release/capability_status.md`

## 前置状态

- 0.3.0 已完成 release acceptance。
- 0.3.1 Week47 已稳固：`@file:` / `@folder:` bounded references 和只读 `caicli changes` 已实现。
- 0.3.1 final release acceptance 仍需单独完成：版本元数据、deterministic package、final acceptance doc 和 zip size/SHA256 不属于本计划核心实现范围。

## 版本边界

本计划只覆盖 `0.3.2`：

- 支持按需 markdown task report。
- 支持显式 report 输出策略：stdout 或用户指定 path。
- 增加本地 `--expert bugfix/reviewer/tester/security/refactor` profiles。
- 复用 0.3.0/0.3.1 的 `taskReport`、trace、session、reference metadata、changes view 和 redaction 契约。
- 补齐 text、JSON、trace、session、report、smoke 和 release docs。

本计划明确不做：

- 不实现 0.3.3 的 `skills list/run`、pack manifest、内置 .NET workflow packs。
- 不做自动 model role routing；`--expert` 只调整本地提示、工具边界、默认报告重点和输出约束。
- 不做桌面 UI、TUI、daemon、远程 marketplace、团队知识库或云端技能分发。
- 不绕过 approval、workspace guard、secret redaction、shell policy、dangerous-command detection、trace/log 或 release smoke。
- 不把 markdown report 当作 correctness proof；它只是复核产物。

## 用户入口草案

### Markdown report

```powershell
caicli exec --report markdown --workspace . "Fix the failing test using @file:README.md"
caicli exec --report markdown --report-path .caicli/reports/latest.md --workspace . "Review @folder:src/CSharpAiCli.Core"
caicli exec --output json --report markdown --workspace . "Summarize @file:docs_md/release/capability_status.md"
```

建议语义：

- `--report none|markdown`，默认 `none`，避免静默新增文件。
- `--report markdown` 默认在任务完成后把 markdown report 输出到 stdout 的 report section；若同时使用 `--output json`，JSON 仍保持 NDJSON，markdown report 通过 final `taskReport` payload 中的 `report` metadata 或显式 `--report-path` 处理，避免污染 JSON stream。
- `--report-path <path>` 仅在用户显式指定时写文件；路径必须在 workspace 内或明确允许的诊断目录内，并经过 workspace guard。
- 如果 report path 指向已有文件，默认拒绝覆盖；后续可考虑显式 `--report-overwrite`，但 0.3.2 可先不做。

### Expert profiles

```powershell
caicli exec --expert bugfix --workspace . "Fix this using @file:src/App.cs"
caicli exec --expert reviewer --workspace . "Review @folder:src/CSharpAiCli.Core"
caicli exec --expert security --report markdown --workspace . "Audit @folder:src/CSharpAiCli.Core/Tools"
caicli exec --expert tester --workspace . "Improve test coverage for @file:src/CSharpAiCli.Core/Agents/AgentTaskReport.cs"
caicli exec --expert refactor --workspace . "Simplify this code path using @file:src/App.cs"
```

建议 expert 边界：

- `bugfix`：允许 read/search/patch/shell verification，但仍遵守 approval、workspace guard、dirty workspace、shell policy 和 retry budget。
- `tester`：偏向测试定位和验证命令；可写测试文件，但仍 approval-gated。
- `refactor`：可写，但报告必须强调 behavior-preservation、changed files、verification 和 remaining risks。
- `reviewer`：只读；不允许 patch/shell/MCP write-capable actions。
- `security`：只读；不允许 patch/shell/MCP write-capable actions；报告重点包含 risks、secrets、dangerous operations 和 assumptions。

## 实施设计

### 1. Report option model

新增或扩展：

- `ExecReportMode`
- `ExecReportOptions`
- `MarkdownTaskReportRenderer`
- `ReportPathResolver`
- `ReportWriteResult`

规则：

- markdown report 必须从现有 `AgentTaskReport` 单源生成。
- report 中可以引用 reference metadata，但不能写入 raw referenced content。
- report 必须复用现有 redaction，secret-like values 只记录 presence metadata 或 redacted text。
- report path 写入必须走 workspace path resolver；拒绝 workspace 外路径、目录 traversal、reparse/symlink 逃逸。
- report 写入失败不应吞掉，应进入 terminal result/report warning 或 failed status，具体取决于 report 是主输出还是附加产物。

### 2. Markdown report 内容

建议结构：

```markdown
# C# AI CLI Task Report

## Summary
## Prompt
## Workspace
## Expert
## References
## Plan
## Changed Files
## Commands
## Verification
## Review Gate
## Remaining Risks
## Trace And Session
## Redaction
```

字段来源：

- `AgentTaskReport`：status、stop reason、prompt、plan、tools、changed files、commands、verification、risks、trace path、review gate。
- 0.3.1 references：source token、resolved path、included/skipped counts、truncation、warnings。
- session/trace metadata：session id、trace path、redaction status。
- expert profile metadata：selected expert、read-only/write-capable boundary、default report focus。

### 3. Expert profile model

新增或扩展：

- `ExpertProfile`
- `ExpertProfileCatalog`
- `ExpertProfilePromptFormatter`
- `ExpertToolBoundary`
- `ExpertProfileReportMetadata`

规则：

- Expert profile 是本地配置和 prompt/tool-boundary policy，不是 provider/model router。
- Expert profile 必须进入 `AgentRunRequest`，使 fake/offline runner 和 direct OpenAI runner 共享同一行为契约。
- `reviewer` / `security` 必须只读：在 tool registry 或 agent request tool policy 层禁用 write/shell/MCP start 等高风险工具。
- `bugfix` / `tester` / `refactor` 不能扩大权限；它们只改变指令、报告重点和验证倾向。
- Text/JSON/trace/session/taskReport 都应记录 `expert` metadata。

### 4. CLI 与渲染

扩展 `exec`：

- `--report none|markdown`
- `--report-path <path>`
- `--expert bugfix|reviewer|tester|security|refactor`

输出策略：

- Text output：任务结尾显示 report summary；`--report markdown` 输出完整 markdown section 或 report path。
- JSON/NDJSON output：不混入 raw markdown stream；使用 structured `report.generated` event 或 terminal payload metadata。
- Trace：记录 report options、write result、expert profile metadata。
- Session：保存 taskReport 和 report metadata，不保存重复 markdown 全文，避免 transcript 膨胀。

### 5. 测试计划

新增或扩展测试：

- `MarkdownTaskReportRendererTests`
- `ReportPathResolverTests`
- `ExecReportOptionsTests`
- `ExpertProfileCatalogTests`
- `ExpertProfilePromptFormatterTests`
- `ExpertToolBoundaryTests`
- `CliCommandFactoryTests`
- `ExecRendererTests`
- `TraceLoggerTests`
- `ConversationTranscriptMarkdownFormatterTests`
- `SmokeTestScriptTests`

覆盖场景：

- markdown report 成功渲染完整 sections。
- report redaction：prompt、risks、commands、references、trace/session 中的 secret-like values 不泄露。
- `--report-path` 成功写入 workspace 内路径。
- `--report-path` 拒绝 workspace 外路径、已有文件覆盖、目录路径、reparse/symlink 逃逸。
- `--output json --report markdown` 不污染 NDJSON。
- expert unknown value 稳定 argument error。
- `reviewer` / `security` 禁用 write/shell/MCP start 工具。
- `bugfix` / `tester` / `refactor` 不扩大 approval 或 shell policy。
- expert metadata 出现在 text、JSON、trace、session、taskReport/report。
- smoke 覆盖 credential-free fake/offline path。

### 6. Smoke 与文档

默认 smoke 新增离线路径：

- `caicli exec --report markdown --workspace <workspace> "Summarize @file:note.txt"`。
- `caicli exec --expert reviewer --workspace <workspace> "Review @file:note.txt"`。
- `caicli exec --expert security --output json --workspace <workspace> "Audit @file:note.txt"`。
- `caicli exec --report markdown --report-path <workspace>\.caicli\reports\smoke.md ...`，如果实现 path write。

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

- [x] Step 1: 确认 `--report` / `--report-path` / `--expert` CLI option 语义和错误码。
- [x] Step 2: 新增 report option model、path resolver 和 markdown renderer。
- [x] Step 3: 将 report generation 接入 `exec` text、JSON、trace、session 和 terminal result。
- [x] Step 4: 新增 expert profile catalog、prompt formatter 和 metadata。
- [x] Step 5: 将 expert profile 接入 `AgentRunRequest`、startup context、startup plan 和 task report。
- [x] Step 6: 实现 reviewer/security 只读 tool boundary，并测试不能写文件、运行 shell 或启动 MCP。
- [x] Step 7: 增加 markdown report、report path、redaction、JSON stream、session/trace 测试。
- [x] Step 8: 增加 expert profile CLI/prompt/tool-boundary/report 测试。
- [x] Step 9: 更新 smoke script 与 smoke script tests。
- [x] Step 10: 更新 release docs、capability status、known limitations、runtime logging diagnostics。
- [x] Step 11: 运行 `dotnet build src\CSharpAiCli.sln -c Release`。
- [x] Step 12: 运行 `dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- [x] Step 13: 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`。
- [x] Step 14: 创建 `48_week_review.md`，记录实际验收、风险、Deferred 边界。

## 验收矩阵

| 功能 | 用户入口 | 行为边界 | 测试覆盖 | 文档/Smoke | 验收标准 |
|---|---|---|---|---|---|
| Markdown task report stdout | `caicli exec --report markdown "..."` | 默认不写额外文件；从 `AgentTaskReport` 单源生成；不持久化 raw referenced content；secret redaction 必须生效 | markdown renderer、redaction、text output、session/taskReport tests | quickstart/security/capability 更新；smoke 覆盖 credential-free path | text 输出完整 report sections；status、changed files、commands、verification、risks、references、trace/session 均可复核；Deferred：report 不是 correctness proof |
| Markdown report path | `caicli exec --report markdown --report-path .caicli/reports/run.md "..."` | 仅显式 path 写文件；路径必须在 workspace 或允许诊断目录内；拒绝覆盖和 path escape；写入不绕过 approval/security | path resolver、write success/failure、workspace guard、existing file tests | troubleshooting 说明 path 错误；smoke 可覆盖成功写入 | report file 写入稳定；text/json/trace 记录 report path/write result；失败有稳定 error/warning；Deferred：不做自动历史 report store |
| JSON output compatibility | `caicli exec --output json --report markdown "..."` | 不污染 NDJSON；markdown 不直接混入 JSON stream；structured metadata 可机器读取 | JSON renderer、terminal payload、trace tests | runtime diagnostics 更新 schema；smoke 断言 JSON parseable | NDJSON 每行可解析；terminal result/report event 有 `report` metadata；exit code 与任务状态一致 |
| `--expert bugfix/tester/refactor` | `caicli exec --expert bugfix|tester|refactor "..."` | 只改变本地提示、默认报告重点和验证倾向；不扩大写入/shell/MCP 权限；仍 approval-gated | expert catalog、prompt formatter、taskReport metadata、approval boundary tests | quickstart 增加示例；capability status 标当前能力 | text/json/trace/session/taskReport 显示 expert；工具权限不高于默认 exec；report 说明 expert focus |
| `--expert reviewer/security` 只读 | `caicli exec --expert reviewer|security "..."` | 禁止 patch、shell、write-capable MCP；不写 workspace；不启动 MCP；只读复核，不证明正确性 | tool boundary tests、CLI tests 断言 write/shell blocked、fake runner tests | security model/troubleshooting 更新；smoke 覆盖只读 expert | reviewer/security 无法执行写工具；JSON/trace 记录 read-only expert boundary；remaining risks 明确 |
| Expert failure and validation | `caicli exec --expert unknown "..."`；expert 与 disabled tools 冲突 | unknown expert 稳定 argument error；disabled tools 不被 expert 重新启用；错误不调用模型 | option validator、disabled tools、model factory not called tests | troubleshooting 列出错误 | exit code 和 error code 稳定；text/json 可诊断；Deferred：不做 custom expert files |
| Release docs and smoke | release smoke/docs | 默认 smoke 不需要模型凭据；real model smoke 仍 opt-in；docs 准确区分 0.3.2 current 与 0.3.3 Deferred | `SmokeTestScriptTests`、build/test/smoke | CHANGELOG/capability/known limitations/quickstart/security/troubleshooting | docs 不宣称 skills/packs 已可用；smoke local-only；release acceptance 可复用 |

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

1. `--report markdown` 在 text mode 是否直接输出完整 markdown，还是只输出 path/summary；建议 text mode 输出完整 markdown，path mode 输出 path。
2. `--report-path` 是否允许写 `.caicli/reports` 以外的 workspace path；建议允许 workspace 内显式 path，但拒绝覆盖。
3. `reviewer` / `security` 是否允许运行 read-only git tools；建议允许 git status/diff/read/search，禁止 shell/patch/MCP start。
