# Week 88 Renderer Lane 执行计划：Composer、Inline Approval 与 Task Controls

状态：`Blocked by Week87 Renderer Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-renderer`

并行 CLI 计划：`88_week_cli_packs_artifacts_modules.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week87-renderer-conversation-projection/week87-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Conversation Projection Ready`。
- Week87 projection exhaustiveness、DOM bound、subscription、accessibility 和 resource Gate 全为 `Passed`。
- `desktop-v1` canonical inventory 仍为 exact `39 invoke + 2 event`，hash 等于 Week87 handoff。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema并绑定 Week87 exact clean Renderer revision；本 lane Entry 结果写入 `gates/W88-R0.json`。

任一条件不满足，Week88 Renderer lane 保持 `Blocked`。

## Goal

把 Composer 和当前 turn 操作整合进 Chat-first flow：Composer 成为中央底部输入面；Approval、Cancel、Resume、Restart 附着到对应 turn/block。只改变 presentation 和 feature wiring，不改变 AppHost authority、revision 或 replay 规则。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week88-renderer-composer-approval/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week88-renderer-composer-approval/
  gates/W88-R0.json ... W88-R5.json
  baseline-identity.json
  desktop-protocol-inventory.before.json
  desktop-protocol-inventory.after.json
  interaction-contract.before.json
  interaction-contract.after.json
  mutation-revision-matrix.json
  subscription-ownership.before.json
  subscription-ownership.after.json
  subscription-ownership.diff.json
  viewport-matrix.json
  accessibility.json
  recovery-results.json
  resource-controls.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week88-renderer-handoff.json
  first-failures/
```

`interaction-contract.*.json` 使用同一 authoritative fixture；`mutation-revision-matrix.json` 必须逐项记录 request/turn/approval revision、clientMutationId、预期 authority result、实际 result 与 replay count。任何 stale/double/late case 被接受或 replay count 非 `0` 即失败。schema 与 first-failure 规则按统一执行契约。

## Phase 0：Interaction Contract 冻结

冻结：

- composer queue revision、pending intent、draft key 和 controlled context selection。
- approval `requestId / approvalRevision / turnRevision`。
- cancel/resume/restart expected revision 与 clientMutationId。
- archived/running/runtime failed/workspace missing disabled reasons。

## Phase 1：Composer Surface

- 紧凑 textarea、auto-grow 上限、send/cancel 状态。
- `@file/@folder`、Skill、Expert、Automation 使用一致 chips 和 mention list。
- pending intent 明确显示并可按既有 contract clear。
- 模型、approval mode 和 context summary 保持可见但不抢占主操作。
- Enter 发送、Shift+Enter 换行、IME composing 不误发送。

## Phase 2：Inline Approval

- ApprovalCard 渲染在对应 tool/turn block。
- 明确 risk、summary、safe reason、approve/deny、resolved/stale/expired。
- mutation 参数只能来自 authoritative feature state，不从 DOM/presentation block 重新推导。
- double click、late response、thread switch 和 stale revision fail closed。

## Phase 3：Inline Turn Controls

- running turn 显示 cancel/canceling。
- interrupted/recovery-required 显示 resume/restart eligibility 和理由。
- controls 不再作为永久独立底栏占高。
- focus 在 mutation 后返回稳定位置；状态通过 aria-live 宣告。

## Phase 4：Failure 与 Recovery

- AppHost failed/restarting、approval deny、cancel、interrupted、new attempt identity 分离。
- crash/restart 不自动 replay；旧 approval 不进入新 attempt。
- Composer draft/pending intent 在 workspace/thread switch 后符合既有规则。

## 非目标

- 不增加 approval mode 或 bypass。
- 不改变 context resolution、安全 guard 或 composer protocol。
- 不引入富文本编辑器、slash command framework 或附件直读。
- 不修改 CLI lane 文件。

## 验证

- Composer RTL：IME、keyboard、mention、chips、draft、pending、disabled reasons。
- Approval RTL：approve/deny/stale/double action/thread switch。
- recovery E2E：durable approval、crash、explicit restart、new attempt cancel、no replay。
- controlled-context/path ownership tests。
- Week84 listener、Week77/80 memory、a11y 和 packaged applicable smoke。

## 机器验收命令与判定

从仓库根目录按顺序运行，首次结果写入 `test-results.json`：

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
Push-Location apps\desktop
npm run verify
npm run test:e2e:unpacked
npm run test:e2e:performance
Pop-Location
git diff --check
```

机器判定同时满足：

- Composer queue revision、pending intent、draft key、controlled context selection 和 disabled reason 的 before/after canonical outcome diff 为 `0`。
- approve/deny、double action、late response、thread switch、stale revision、cancel/resume/restart 每个 fixture 的 authoritative expected/actual result 相同；stale accepted count、duplicate mutation count、old-approval reuse count、automatic replay count 均为 `0`。
- Enter、Shift+Enter、IME composing、focus restore 与 aria-live fixture failure count 为 `0`。
- `viewport-matrix.json` 覆盖 `1440x900`、`1024x768`、`800x900`，overflow/critical-overlap/unreachable-control 为 `0`；Composer 与 active turn controls 在每个 viewport 可达。
- `accessibility.json` 中检查 exit code 为 `0`、自动 violation count 为 `0`；Approval/turn controls 的 keyboard accessible action count 等于 visible action count。
- subscription diff duplicate/unmatched 均为 `0`；resource workload 不变，listener delta `<=40`、Renderer private-bytes delta `<=15%`，DOM/resync 不超过 frozen limit。
- protocol after 为 exact `39 invoke + 2 event` 且 canonical hash 与 before 相同；所有命令 exit code 与 cleanup delta 为 `0`。

## Critical Gates

- [ ] W88-R0 authoritative IDs/revisions contract 冻结。
- [ ] W88-R1 Composer queue/draft/context canonical outcome mismatch count 为 `0`。
- [ ] W88-R2 ApprovalCard 可见、可键盘操作且 stale fail closed。
- [ ] W88-R3 cancel/resume/restart 无自动 replay 或身份复用。
- [ ] W88-R4 IME/focus/screen-reader tests 通过。
- [ ] W88-R5 listener `<=40`、private-bytes `<=15%`、DOM/resync 不超过 frozen limit，E2E/full verify exit code 为 `0`。

## Exit Gate / Handoff

`W88-R5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未生成
的 `week88-renderer-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

- `gates/W88-R0.json` 至 `gates/W88-R5.json` 逐个通过 Gate result schema且全为 `Passed`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 interaction/revision/recovery/a11y/subscription/resource evidence SHA-256。
- `week88-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Core Chat Interaction Ready`，并绑定 exact clean HEAD、authority/revision/replay invariants 和 Week89 entry prerequisites。
- exact clean HEAD 不包含 CLI lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `Core Chat Interaction Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
