# Week 85 CLI Lane 执行计划：Composition Root 与 Diagnostics Modules

状态：`Blocked by Week84 clean baseline`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-cli`

并行 Renderer 计划：`85_week_renderer_feature_boundaries_state_split.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week84-renderer-listener-retention/week84-baseline-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Baseline Ready for Refactor`，并绑定 exact clean source revision。
- Week84 `gates/W84-G0.json` 至 `gates/W84-G9.json` 均为 `Passed`，开放 P0/P1 为 `0/0`。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema，记录当前 HEAD、Week84 handoff SHA-256、CLI worktree dirty state 和冻结目录 diff；本 lane 的 Entry 结果写入 `gates/W85-C0.json`。
- Renderer lane 与 CLI lane 使用独立 worktree。
- `src/CSharpAiCli.Application`、AppHost、protocol/generated、release scripts 为共享冻结区。

任一条件不满足，Week85 CLI lane 保持 `Blocked`。

## Goal

先建立可承载全部既有注入和递归委托的 CLI composition context，保留 `CliCommandFactory` public façade；冻结命令树和行为基线后，迁移最低风险的诊断、配置和查询命令。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week85-cli-composition/gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。
组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week85-cli-composition/
  gates/W85-C0.json ... W85-C5.json
  baseline-identity.json
  command-inventory.before.json
  command-inventory.after.json
  help-inventory.before.json
  help-inventory.after.json
  output-exit-inventory.before.json
  output-exit-inventory.after.json
  compatibility-diff.json
  architecture-tests.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week85-cli-handoff.json
  first-failures/
```

中央 Goal state、每个 `gates/W85-C*.json` 和 `week85-cli-handoff.json` 分别通过 Goal control、Gate result、handoff schema。before 来自 Entry Gate 绑定的 exact baseline binary/revision，after 来自本周 candidate；两侧使用相同 fixture、参数、环境清单和 deterministic injections。`compatibility-diff.json` 的 command/help/stdout/stderr/JSON/NDJSON/exit mismatch count 必须全部为 `0`。首个失败写入 `first-failures/`，重跑不得覆盖。

## Baseline Inventory

必须记录并冻结：

- 26 个顶层命令及注册顺序。
- Command、Option、Argument、SetAction 的 exact 数量、注册位置与 parent path；以 `command-inventory.before.json` 为唯一 Gate 基线，不使用估算数。
- 所有 `CliCommandFactory.Create` public overload 和 fake injection 语义。
- `--workspace / --verbose / --trace` recursive global options。
- help、parse errors、stdout/stderr、JSON/NDJSON 和 exit-code snapshots。

## Phase 0：Characterization

- 生成机器可比较的 command tree inventory。
- 为 root 和每个迁移域冻结 `--help`。
- 冻结成功、domain/runtime failure、parse/usage error 的 `0/1/2` exit policy。
- 冻结 text/JSON、verbose、trace、output-file 行为。
- 保留首次 full test/smoke 结果。

## Phase 1：Composition Infrastructure

创建：

```text
Commands/Composition/CliDependencies.cs
Commands/Composition/CliGlobalOptions.cs
Commands/Composition/CliCommandContext.cs
Commands/Composition/CliRootComposer.cs
Commands/Abstractions/ICliCommandModule.cs
```

要求：

- 所有现有 Create overload 映射到同一个 `CliDependencies`。
- global option instance 只创建一次并按当前递归方式附着。
- module 返回一个顶层 Command，不自行创建第二个 root。
- delegation 所需 root factory 明确进入 context，Week87/89 使用。

## Phase 2：低风险模块迁移

迁移：

- version、doctor、status、models。
- diff、changes。
- config、mcp list/doctor、workflow list/validate。
- daemon/api diagnostics、logs。

辅助 validator/renderer 跟随所属域迁移，不复制。

## Phase 3：Architecture Tests

- 更新原先只扫描 `CliCommandFactory.cs` 的 architecture test，使其扫描 `Commands/**/*.cs`。
- 禁止 command module 依赖 Electron/AppHost。
- 禁止 module 间直接引用内部 builder；共享依赖只能经 composition/shared。
- `CliCommandFactory` 保留兼容 façade，测试继续可使用旧入口。

## 非目标

- 不迁移 exec/skills/queue/packs/automation/pipeline。
- 不把 CLI 业务重新设计为新的 Application use case。
- 不改命令名称、顺序、描述、默认值、validator 文案或输出。
- 不修改 `apps/desktop` 或共享冻结区。

## 机器验收命令与判定

从仓库根目录按顺序运行，首次结果写入 `test-results.json`：

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
git diff --check
```

inventory harness 必须分别对 Entry Gate 绑定的 baseline executable 和 after executable 执行相同 scenario manifest，并把 exact argv、fixture hash、environment allowlist、stdout、stderr 与 exit code 写入 before/after 文件。机器判定同时满足：

- top-level command count 为 `26` 且注册顺序一致；其余 Command/Option/Argument/SetAction 数量和结构必须等于 exact before inventory，不以“约”数作为 Gate。
- command path、alias、description、option/argument 顺序、required/default、validator 文案与 recursive global option attachment canonical diff 为 `0`。
- root 及本周迁移域的 help bytes 在仅归一化 fixture temp root 后 SHA-256 相同。
- 成功、domain/runtime failure、parse/usage error scenario 的 stdout/stderr stream、text、JSON、NDJSON sequence 与 exit code diff 均为 `0`；`0/1/2` policy 不变。
- 所有 public `CliCommandFactory.Create` overload 的 dependency/fake injection matrix before/after 相同。
- 上述命令 exit code 全为 `0`，owned process/temp/config cleanup delta 为 `0`。

## Critical Gates

- [ ] W85-C0 command/help/output/exit-code baseline 完整。
- [ ] W85-C1 所有 Create overload 经 composition context 保持注入语义。
- [ ] W85-C2 global options recursive behavior 不变。
- [ ] W85-C3 低风险模块迁移且 snapshots 零差异。
- [ ] W85-C4 architecture tests 扫描全部 command files。
- [ ] W85-C5 .NET full、CLI tests、smoke、dirty/cleanup 全绿。

## Exit Gate / Handoff

`W85-C5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence；它证明本 lane
全部 Gate、compatibility diff、candidate、父 handoff、cleanup 与问题清单已具备生成
交接的条件，不得引用尚未生成的 `week85-cli-handoff.json`。Gate Passed 后才单向生成
最终 handoff，再生成 Gate 外 `preseal-receipt.json` 并封存 anchor；不得回写 Gate。

- `gates/W85-C0.json` 至 `gates/W85-C5.json` 逐个通过 Gate result schema且全为 `Passed`，`compatibility-diff.json` 全部 mismatch count 为 `0`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 before/after executable、inventory 与 test evidence SHA-256。
- `week85-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Composition Ready`，并绑定 exact clean HEAD、共享冻结区 diff、baseline inventory hashes 和 Week86 entry prerequisites。
- exact clean HEAD 不包含 Renderer lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `CLI Composition Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
