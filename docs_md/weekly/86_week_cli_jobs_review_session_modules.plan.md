# Week 86 CLI Lane 执行计划：Jobs、Review、Run 与 Session Modules

状态：`Blocked by Week85 CLI Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-cli`

并行 Renderer 计划：`86_week_chat_first_shell_design_system.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week85-cli-composition/week85-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Composition Ready`。
- Week85 `compatibility-diff.json` 的 command/help/output/exit mismatch 均为 `0`，Week85 required Gate 全为 `Passed`。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema，绑定 Week85 exact clean CLI revision、baseline inventory hashes 和 worktree dirty state；本 lane Entry 结果写入 `gates/W86-C0.json`。
- CLI 与 Renderer 位于独立 worktree；Application、AppHost、protocol/generated 和 release scripts 冻结区无未授权差异。

任一条件不满足，Week86 CLI lane 保持 `Blocked`。

## Goal

在 Week85 composition context 上迁移中等耦合命令域，按 feature 拆分对应 tests 和 renderers，保持 store identity、日志、session 和输出兼容。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week86-cli-jobs-review-session/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week86-cli-jobs-review-session/
  gates/W86-C0.json ... W86-C5.json
  baseline-identity.json
  command-inventory.before.json
  command-inventory.after.json
  help-inventory.before.json
  help-inventory.after.json
  output-exit-inventory.before.json
  output-exit-inventory.after.json
  compatibility-diff.json
  injection-matrix.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week86-cli-handoff.json
  first-failures/
```

before/after 使用同一 job/session fixture、fake clock、environment、input、TextWriter、model/store injection；仅可归一化 evidence 中声明并经 fake 固定前仍不可消除的临时根路径，不得归一化输出文字、事件顺序或 exit code。`compatibility-diff.json` 的全部 mismatch count 必须为 `0`。schema 与 first-failure 规则按统一执行契约。

## 迁移范围

- jobs list/show/export。
- ci summarize/check。
- review、tools。
- run。
- session 子命令。
- chat。

每个 module 返回顶层 command；共享 output/diagnostic helpers 进入明确 Shared/Rendering 层。

## Phase 0：域基线

- 为每个命令采集 text/JSON/verbose/trace/help/parse failure snapshots。
- 记录 job/session store fixtures、clock/environment/input injection。
- 记录 output path validator 和 no-overwrite/error behavior。

## Phase 1：Jobs/CI/Review/Tools

- 先迁移纯 query/render 路径，再迁移 review/model 调用边界。
- 保持 job artifact pointers、CI schema、fail-on policy 和 error code。
- 不将 review prompt/business logic复制到 command module。

## Phase 2：Run/Session/Chat

- 保留 transcript、resume/export/clear、model/backend selection 和 streaming renderer。
- 保留 input/output/TextWriter、fake clock、model factory 和 conversation store injection。
- JSON 模式不得混入普通 text/verbose 输出。

## Phase 3：Tests 拆分

- 将巨型 `CliCommandFactoryTests` 中迁移域测试按 module 分类，但保留公共 compatibility suite。
- 旧 Create façade和新 composer 两条入口必须产生相同 command tree/behavior。

## 非目标

- 不迁移 exec、skills、queue、packs、artifacts、automation、pipeline。
- 不改 agent loop、provider、session schema 或 report schema。
- 不修改 Renderer/shared frozen files。

## 机器验收命令与判定

从仓库根目录按顺序运行，首次结果写入 `test-results.json`：

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
git diff --check
```

inventory harness 对 baseline/after 执行 jobs/ci/review/tools/run/session/chat 的同一 scenario manifest。机器判定同时满足：

- 全树 top-level count/顺序仍为 exact before（顶层 `26`）；本周域的 command/help canonical diff 为 `0`。
- text/JSON/verbose/trace/streaming/parse-failure/no-overwrite scenarios 的 stdout/stderr、JSON/NDJSON sequence、stable error 与 exit mismatch count 均为 `0`。
- `injection-matrix.json` 中 store identity、fake clock、environment/input/TextWriter、model factory、conversation store 的 before/after factory identity 和 observable call sequence 相同。
- 旧 Create façade与新 composer 产生相同 command tree hash 和 scenario result hash。
- full .NET、Release build、default credential-free CLI smoke exit code 全为 `0`；owned process/temp/config cleanup delta 为 `0`。

## Critical Gates

- [ ] W86-C0 Week85 composition handoff 有效。
- [ ] W86-C1 jobs/ci/review/tools 的 command/help/output/exit mismatch count 为 `0`。
- [ ] W86-C2 run/session/chat 的 text/JSON/NDJSON/streaming/exit mismatch count 为 `0`。
- [ ] W86-C3 store、clock、environment、input injection 的 observable call-sequence mismatch count 为 `0`。
- [ ] W86-C4 tests 按域拆分且 compatibility suite 保留。
- [ ] W86-C5 full .NET、CLI smoke、release applicable tests 全绿。

## Exit Gate / Handoff

`W86-C5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence；它不得引用尚未
生成的 `week86-cli-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

- `gates/W86-C0.json` 至 `gates/W86-C5.json` 逐个通过 Gate result schema且全为 `Passed`，`compatibility-diff.json` 全部 mismatch count 为 `0`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 before/after inventories、injection matrix 和 test evidence SHA-256。
- `week86-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Session/Query Modules Ready`，并绑定 exact clean HEAD、共享冻结区 diff和 Week87 entry prerequisites。
- exact clean HEAD 不包含 Renderer lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `CLI Session/Query Modules Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
