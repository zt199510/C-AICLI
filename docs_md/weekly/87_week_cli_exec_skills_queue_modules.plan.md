# Week 87 CLI Lane 执行计划：Exec、Skills 与 Queue Modules

状态：`Blocked by Week86 CLI Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-cli`

并行 Renderer 计划：`87_week_conversation_projection_timeline.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week86-cli-jobs-review-session/week86-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Session/Query Modules Ready`。
- Week86 `compatibility-diff.json` 的 command/help/output/exit mismatch 均为 `0`，required Gate 全为 `Passed`。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema，绑定 Week86 exact clean CLI revision、baseline inventory hashes 和 worktree dirty state；本 lane Entry 结果写入 `gates/W87-C0.json`。
- Application、AppHost、protocol/generated 和 release scripts 冻结区无未授权差异。

任一条件不满足，Week87 CLI lane 保持 `Blocked`。

## Goal

迁移 CLI 最敏感的 agent execution、skills 和 queue 命令域，并保持 `queue run -> current root -> exec/skills` 的递归委托、approval、trace、job correlation 和 exit code 完全兼容。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week87-cli-exec-skills-queue/gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。
组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week87-cli-exec-skills-queue/
  gates/W87-C0.json ... W87-C5.json
  baseline-identity.json
  command-inventory.before.json
  command-inventory.after.json
  help-inventory.before.json
  help-inventory.after.json
  output-exit-inventory.before.json
  output-exit-inventory.after.json
  delegation-matrix.before.json
  delegation-matrix.after.json
  compatibility-diff.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week87-cli-handoff.json
  first-failures/
```

before/after 使用同一 queue/exec/skills fixtures、approval policy、clock、workspace identity、TextWriter 和 fake model/tool injections。`delegation-matrix.*.json` 必须记录 delegated argv、root identity、global options、NDJSON sequence、correlation ids 与 exit code；`compatibility-diff.json` 的全部 mismatch count 必须为 `0`。schema 与 first-failure 规则按统一执行契约。

## 风险焦点

- `queue run` 需要在当前 root 上重新 parse/invoke。
- exec 与 skills 共用 runner、approval、workspace/cwd、model、MCP、trace、job/report/session 能力。
- JSON/NDJSON event 顺序和 stdout/stderr 不能因 module boundary 变化。

## Phase 0：Delegation Baseline

冻结：

- queue add/list/show/cancel/cleanup/run command inventory。
- queue item、attempt、job/report pointers 和 correlation ids。
- exec/skills 成功、approval deny、tool failure、timeout、disabled tool、dirty workspace。
- text、JSON single object、NDJSON event order、verbose/trace。

## Phase 1：Exec Module

- 迁移 exec options、validators、runner assembly 和 output selection。
- 保留 max steps/tool calls/timeout、approval、cwd/workspace、expert/report/job/session 语义。
- 领域辅助方法归入 AgentExecution/Shared，不复制。

## Phase 2：Skills Module

- 迁移 skills list/run 和 local skill pack selection。
- 保留 tool boundary、project instruction、report/job/session 和 approval policy。
- 保留 skill not found/invalid/disabled 等稳定 error code。

## Phase 3：Queue Module 与 Root Delegation

- 通过 composition context 提供当前 root invoker/factory。
- queue run 必须调用同一命令树和 global options，不创建配置不同的隐式 root。
- 防止递归 composition 泄漏 resource、重复 logger 或错误 TextWriter。

## Phase 4：Failure Matrix

- queue -> exec 和 queue -> skills 的 success/failure/cancel/retry。
- approval、dirty guard、MCP startup、trace、session/job/report correlation。
- parse error 与 delegated exit code `0/1/2`。
- cleanup 和 temporary state。

## 非目标

- 不重写 queue executor 或 agent loop。
- 不改变 persistence schema、retry policy 或 approval。
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

inventory harness 对 baseline/after 执行 exec、skills 和 queue 的同一 failure/delegation manifest。机器判定同时满足：

- 全树 top-level count/顺序仍为 exact before（顶层 `26`）；本周域 command/help/option/default/validator canonical diff 为 `0`。
- success、approval deny、tool failure、timeout、disabled tool、dirty workspace、cancel/retry、parse error 的 stdout/stderr、text/JSON/NDJSON、stable error 与 exit mismatch count 均为 `0`。
- queue -> exec/skills 的 delegated argv、current-root identity、global option attachment、logger/TextWriter identity、session/job/report correlation 与 NDJSON sequence diff 为 `0`。
- 每次 delegated invocation 只有一个 root composition owner；duplicate root/logger/subscription/resource owner count 为 `0`，temporary state cleanup delta 为 `0`。
- full .NET、Release build 与 default credential-free smoke exit code 全为 `0`。

## Critical Gates

- [ ] W87-C0 delegation before manifest 的 required scenario missing count 为 `0`。
- [ ] W87-C1 exec command/help/output/error/exit mismatch count 为 `0`。
- [ ] W87-C2 skills command/help/output/error/exit mismatch count 为 `0`。
- [ ] W87-C3 queue current-root delegation mismatch 与 duplicate root state count 均为 `0`。
- [ ] W87-C4 NDJSON/order/correlation/exit-code snapshots 零差异。
- [ ] W87-C5 full .NET、smoke、cleanup 和 architecture tests 全绿。

## Exit Gate / Handoff

`W87-C5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未生成
的 `week87-cli-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

- `gates/W87-C0.json` 至 `gates/W87-C5.json` 逐个通过 Gate result schema且全为 `Passed`，`compatibility-diff.json` 全部 mismatch count 为 `0`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 before/after inventory、delegation matrix 和 test evidence SHA-256。
- `week87-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Agent/Queue Modules Ready`，并绑定 exact clean HEAD、共享冻结区 diff和 Week88 entry prerequisites。
- exact clean HEAD 不包含 Renderer lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `CLI Agent/Queue Modules Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
