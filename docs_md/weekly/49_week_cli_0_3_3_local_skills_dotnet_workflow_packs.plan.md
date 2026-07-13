# 第 49 周 0.3.3 Local Skills 与 .NET Workflow Packs Implementation Plan

状态：已执行

**Goal:** 在 0.3.1 workflow inputs/changes view 和 0.3.2 reports/expert profiles 稳定后，引入轻量本地 skills/workflow packs，让常见 .NET 工程任务可以通过 CLI 复用，同时保持本地、可审计、无远程 marketplace 的边界。

## 来源

- `docs_md/spec/product_positioning_and_roadmap.md`
- `docs_md/weekly/47_week_cli_0_3_developer_workflow_packs_schedule.md`
- `docs_md/weekly/47_week_review.md`
- `docs_md/weekly/48_week_cli_0_3_2_reports_expert_profiles.plan.md`
- `docs_md/release/capability_status.md`

## 前置状态

- 0.3.1 已提供 `@file:` / `@folder:` bounded references 和只读 `caicli changes`。
- 0.3.2 计划提供 markdown report 和 `--expert` profiles；0.3.3 实现前应确认 0.3.2 已稳固或据实际状态调整范围。
- 现有 `workflow list/validate`、workflow profiles、`CSharpAiCli.ProjectPacks` 和 Gerber/TIFF project pack skeleton 是可复用基础，但 Gerber/TIFF real execution 仍 Deferred。

## 版本边界

本计划只覆盖 `0.3.3`：

- 增加轻量本地 `skills list/run` 或等价 CLI。
- 定义最小本地 pack manifest 和 validation。
- 支持内置 packs 和本地目录 packs。
- 提供首批 .NET workflow packs：`test-fix`、`review-only`、`upgrade-package`、`doc-sync`。
- 将 pack 展开为受控 `exec`、`review` 或 report-oriented request，复用 0.3.1/0.3.2 的 references、expert、report、trace/session/taskReport。

本计划明确不做：

- 不做远程 marketplace、云端技能分发、自动更新或签名信任链。
- 不做团队知识库、权限平台、远程执行、daemon/API、CI/PR 集成。
- 不做自动 model role routing；pack 只能选择已有 expert/profile 和本地指令。
- 不做 Gerber/TIFF 真实工具链执行。
- 不让 pack 扩大工具权限或绕过 approval、workspace guard、secret redaction、shell policy、disabled tools、trace/log、release smoke。

## 用户入口草案

```powershell
caicli skills list
caicli skills list --output json
caicli skills run test-fix --workspace . -- "Fix the failing tests"
caicli skills run review-only --workspace . --report markdown -- "@folder:src/CSharpAiCli.Core"
caicli skills run upgrade-package --workspace . -- "Upgrade a NuGet package safely"
caicli skills run doc-sync --workspace . -- "Sync docs with @file:docs_md/release/capability_status.md"
```

建议语义：

- `skills list` 列出内置 packs 和本地 packs，默认 text，支持 JSON。
- `skills run <name> -- <task>` 将 pack manifest 展开为受控 request。
- pack 不能直接执行任意脚本；所有执行必须进入现有 `exec` / `review` / tool registry 安全路径。
- pack 默认只提供 task template、expert、references policy、validation command hint、report template 和 safety policy。

## Pack Manifest 草案

建议最小 JSON/YAML 模型先选 JSON，降低解析依赖：

```json
{
  "name": "test-fix",
  "version": "0.1.0",
  "description": "Find and fix failing .NET tests.",
  "appliesTo": {
    "projectTypes": ["dotnet"],
    "requiredFiles": ["*.sln", "*.csproj"]
  },
  "entry": {
    "mode": "exec",
    "expert": "tester",
    "report": "markdown"
  },
  "references": {
    "suggested": ["@folder:src", "@folder:tests"],
    "maxFiles": 50
  },
  "instructions": [
    "Prefer minimal changes.",
    "Run the configured verification command when available."
  ],
  "validationCommand": "dotnet test",
  "reportTemplate": "default",
  "safety": {
    "allowWrites": true,
    "allowShell": "verification-only",
    "allowMcp": false
  }
}
```

规则：

- Manifest schema 必须有版本字段，方便后续演进。
- Pack `safety` 只能收紧权限，不能放宽用户 approval、shell policy、disabled tools 或 workspace guard。
- Pack references 复用 0.3.1 `@file:` / `@folder:` 语义；不做 glob/URL/semantic retrieval。
- Pack report 复用 0.3.2 report renderer；不创建第二套报告格式。

## 内置 .NET Packs

### `test-fix`

- Entry：`exec`
- Expert：`tester`
- 默认 report：markdown
- 行为：定位 failing tests、做最小修复、运行明确 verification command。
- 边界：不自动猜大型迁移；无明确验证命令时报告 skipped verification。

### `review-only`

- Entry：`exec` 或 `review`
- Expert：`reviewer`
- 默认 read-only
- 行为：读取 references/git diff，输出 review/report。
- 边界：不写文件、不运行 shell、不启动 MCP。

### `upgrade-package`

- Entry：`exec`
- Expert：`bugfix` 或后续 `dependency`
- 行为：更新单个包或受限包集，读取 project files，运行 build/test。
- 边界：不自动升级整个解决方案；必须列出 changed files、commands、risks。

### `doc-sync`

- Entry：`exec`
- Expert：`refactor` 或后续 `docs`
- 行为：同步 docs 与 release/capability/status 事实。
- 边界：不改代码行为；主要写 docs；需要 changes/report。

## 实施设计

### 1. Skill/Pack model

新增或扩展：

- `SkillPackManifest`
- `SkillPackCatalog`
- `SkillPackSource`
- `SkillPackValidator`
- `SkillRunRequest`
- `SkillRunPlan`
- `SkillRunResult`

Catalog 来源：

- 内置 packs：代码内静态定义或嵌入 JSON 资源。
- 本地 packs：用户配置目录或 workspace `.caicli/skills`；0.3.3 可先只读 workspace local directory。
- 禁止自动从网络下载。

### 2. CLI command

新增：

- `caicli skills list`
- `caicli skills list --output json` / `--json`
- `caicli skills run <name> -- <task>`

可选：

- `--dry-run`：只显示 expanded request，不执行 agent。
- `--report markdown`
- `--expert <name>` override 是否允许待确认；建议 0.3.3 先禁止 override 或仅允许更严格 read-only override。

### 3. Expansion boundary

`skills run` 应展开为：

- Prompt/task template。
- Expert profile。
- Report mode。
- Reference suggestions 或用户提供 references。
- Validation command hint。
- Safety constraints。

所有最终执行仍经过：

- existing `exec` request builder。
- tool registry/approval/shell policy/workspace guard。
- trace/session/taskReport/report。

### 4. Validation and errors

错误码建议：

- `skill-not-found`
- `skill-manifest-invalid`
- `skill-version-unsupported`
- `skill-safety-policy-invalid`
- `skill-run-plan-invalid`
- `skill-local-source-denied`

Validation 必须覆盖：

- required fields。
- name/version 格式。
- entry mode。
- expert exists。
- safety policy 不能放宽权限。
- references 语法合法。
- validation command 不直接执行，只作为 verification hint。

### 5. 测试计划

新增或扩展测试：

- `SkillPackManifestTests`
- `SkillPackCatalogTests`
- `SkillPackValidatorTests`
- `SkillRunPlannerTests`
- `SkillsCommandTests`
- `CliCommandFactoryTests`
- `TraceLoggerTests`
- `SmokeTestScriptTests`

覆盖场景：

- list 内置 packs text/json。
- manifest validation 成功/失败。
- local pack loading 成功、invalid manifest、duplicate names、unsupported version。
- `skills run test-fix` 展开为 tester expert + markdown report + validation hint。
- `skills run review-only` 保持 read-only，不能 patch/shell/MCP。
- pack safety 不能放宽 disabled tools/approval/shell policy。
- `--dry-run` 输出 plan 不调用模型、不写文件。
- trace/session/taskReport 中记录 skill metadata。
- smoke credential-free path。

### 6. Smoke 与文档

默认 smoke 新增离线路径：

- `caicli skills list --workspace <workspace>`。
- `caicli skills list --output json --workspace <workspace>`。
- `caicli skills run review-only --dry-run --workspace <workspace> -- "@file:note.txt"`。
- 如 fake/offline exec 支持稳定 credential-free path，再覆盖 `skills run test-fix` 的 no-model 或 fake path。

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

- [x] Step 1: 确认 `skills list/run` CLI shape、manifest format 和 local source policy。
- [x] Step 2: 新增 skill pack manifest model、schema validation 和错误码。
- [x] Step 3: 新增内置 pack catalog，包含 `test-fix`、`review-only`、`upgrade-package`、`doc-sync`。
- [x] Step 4: 新增本地 pack loading，先支持只读 workspace/user local directory。
- [x] Step 5: 新增 `skills list` text/json command。
- [x] Step 6: 新增 `skills run` planning 和 `--dry-run`。
- [x] Step 7: 将 `skills run` 接入受控 `exec`/`review` request builder，复用 expert/report/references。
- [x] Step 8: 增加 skill metadata 到 trace/session/taskReport/report。
- [x] Step 9: 增加 manifest/catalog/list/run/planner/security boundary tests。
- [x] Step 10: 更新 smoke script 与 smoke script tests。
- [x] Step 11: 更新 release docs、capability status、known limitations、runtime logging diagnostics。
- [x] Step 12: 运行 `dotnet build src\CSharpAiCli.sln -c Release`。
- [x] Step 13: 运行 `dotnet test src\CSharpAiCli.sln -c Release --no-build`。
- [x] Step 14: 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1`。
- [x] Step 15: 创建 `49_week_review.md`，记录实际验收、风险、Deferred 边界。

## 验收矩阵

| 功能 | 用户入口 | 行为边界 | 测试覆盖 | 文档/Smoke | 验收标准 |
|---|---|---|---|---|---|
| `skills list` text/json | `caicli skills list`；`caicli skills list --output json` | 只读；不调用模型、不运行 shell、不写 workspace；列出内置和允许的本地 packs | catalog/list renderer/CLI tests | quickstart/capability 更新；smoke 覆盖 text/json | text 稳定列出 name/version/description/source；JSON schema 可解析；duplicate/invalid local pack 有 warning |
| Manifest validation | local pack manifest | required fields、version、entry、expert、safety、references validation；禁止安全策略放权 | manifest validator tests、invalid fixture tests | troubleshooting 列出错误码 | invalid manifest 不影响内置 packs；错误码稳定；不执行 manifest 中任何命令 |
| `skills run --dry-run` | `caicli skills run test-fix --dry-run -- "..."` | 只生成 plan；不调用模型、不写文件、不运行 shell/patch/MCP | planner tests、model factory not called tests、CLI tests | smoke 覆盖 dry-run | 输出 expanded expert/report/references/validation/safety；JSON 可机器读取；exit code 稳定 |
| `skills run test-fix` | `caicli skills run test-fix -- "Fix tests"` | 展开为 tester expert + controlled exec；可写但 approval-gated；shell 仅按现有 policy/verification | run planner、exec integration、approval/shell boundary tests | quickstart 增加示例；smoke 如可本地化则覆盖 fake/offline path | taskReport/report/trace/session 记录 skill metadata；不能绕过 approval；verification 结果可复核 |
| `skills run review-only` | `caicli skills run review-only -- "@folder:src"` | read-only；禁止 patch/shell/MCP start；复用 reviewer expert/report | read-only boundary tests、fake runner tests | security model/smoke 更新 | 无写文件、无 shell、无 MCP；report 显示 read-only skill boundary 和 remaining risks |
| Built-in .NET packs | `test-fix`、`review-only`、`upgrade-package`、`doc-sync` | 服务 .NET workflow；不自动大规模迁移；validation command 是 hint，不直接绕过 shell policy | built-in catalog snapshot tests、pack-specific expansion tests | docs 列出适用范围和限制 | 四个 pack 都可 list/run dry-run；每个 pack 的 expert/report/safety 明确 |
| Local pack loading | workspace/user local packs | 只读加载本地 files；不执行 pack 脚本；unsupported version warning/error；duplicate name 规则稳定 | local source tests、path guard tests、duplicate tests | known limitations/troubleshooting 更新 | local pack 可被发现；invalid/duplicate 不破坏内置 packs；无网络访问 |
| Skill metadata in diagnostics | `skills run ... --trace --session smoke` | metadata redacted；不保存 raw referenced content；report 复用 taskReport 单源 | trace/session/report tests | runtime diagnostics 更新 | trace/session/taskReport/report 包含 skill name/version/source、expanded expert/report/safety；secret 不泄露 |
| Release docs and smoke | release smoke/docs | 默认 smoke credential-free；real model smoke 仍 opt-in；docs 准确标记 current/deferred | `SmokeTestScriptTests`、build/test/smoke | CHANGELOG/capability/known limitations/quickstart/security/troubleshooting | docs 不宣称 marketplace/remote packs/Gerber real execution；smoke local-only；release acceptance 可复用 |

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

1. Manifest 格式是否先固定为 JSON；建议 0.3.3 只支持 JSON，避免引入 YAML 依赖。
2. 本地 pack 目录是否先支持 workspace `.caicli/skills`；建议先只读 workspace local，加用户级目录需更明确 trust model。
3. `skills run` 是否必须提供 `--dry-run`；建议必须实现，便于安全复核和 smoke。
