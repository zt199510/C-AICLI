# Week 89 CLI Lane 执行计划：Automation、Pipeline 与 Full Compatibility

状态：`Blocked by Week88 CLI Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-cli`

并行 Renderer 计划：`89_week_context_panel_terminal_review_workspace.plan.md`

## 执行契约与 Entry Gate

本周必须遵守 `84_92_week_goal_execution_contract.md`。中央 Goal 状态使用
`84_92_week_goal_control.schema.json`，Gate 使用
`84_92_week_gate_result.schema.json`，Week90 handoff 使用
`84_92_week_handoff.schema.json`；`schemaVersion` 均为 `1.0.0`。

进入实现前必须同时满足：

- `artifacts/week88-cli-packs-artifacts/week88-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Packs/Artifacts Modules Ready`，并且 exact source revision、dirty state 和 evidence hash 一致。
- Week85–88 CLI command/help/output/exit/schema snapshots 可追踪，开放 P0/P1 为 0。
- 当前 CLI worktree clean，依赖锁与 shared frozen 区无未说明漂移，Renderer lane 文件没有被修改。
- 中央 Goal control 的当前 checkpoint 为 Week89 CLI Entry。
- 用户授权仅覆盖本地分支/worktree/checkpoint commit、仓库内构建测试和 owned cleanup；本周不得调用真实 provider 或扩大 external write。

任一条件不满足时将 `gates/W89-C0.json` 写为 `Failed` 或 `NotRun`，保留首败并停止迁移。

## Goal

最后迁移 automation/pipeline 的递归 root 创建和剩余 command domains，清理旧 factory helpers，完成 CLI lane 的全命令兼容性冻结并生成 Week90 integration handoff。

## 风险焦点

- automation/pipeline 会递归调用 Create 或委托 queue/exec/skills。
- root、global options、dependencies、clock、environment、TextWriter 和 store 必须保持同一 composition identity。
- 清理旧 helpers 时不能移除测试依赖的 public Create overload。

## Phase 0：Recursive Flow Baseline

冻结：

- automation validate/plan/dry-run/manual/run。
- pipeline list/validate/run 和 role/stage delegation。
- automation -> queue/pipeline、pipeline -> queue -> exec/skills 的 exit、job/queue/report correlation。
- cancel/failure/approval/disabled tool/MCP startup/cleanup。

## Phase 1：Automation Module

- 迁移 automation command tree、validators 和 renderers。
- root delegation 只能通过 composition context。
- 保留 workspace-local manifest、read-only planning、manual execution 和安全 policy。

## Phase 2：Pipeline Module

- 迁移 pipeline command tree、profile validation、stage execution wiring。
- 保留顺序、role identity、job/queue/report pointers、failure aggregation 和 exit policy。

## Phase 3：Factory Cleanup

- 迁移剩余 commands/helpers。
- `CliCommandFactory` 只保留 public compatibility façade 与 Invoke，或由 reviewed root composer 完全取代。
- 无 duplicate command registration、unused hidden root 或 second dependency container。
- 更新 architecture tests 和 command inventory generator。

## Phase 4：CLI Full Compatibility

比较 baseline 与新结构：

- 26 顶层命令、所有子命令、顺序、description、arguments/options/defaults/validators/help。
- public Create overload injection。
- global options recursive behavior。
- stdout/stderr、text/JSON/NDJSON、output files。
- exit code、logs/trace、session/job/queue/report/artifact schema。
- default smoke、release build、package behavior和 cleanup。

## 非目标

- 不改变 automation scheduler、pipeline execution model 或 agent runtime。
- 不顺手重构 Application/Core。
- 不修改 Renderer/shared frozen files。

## 机器证据

固定目录：`artifacts/week89-cli-automation-pipeline/`。

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。组根仅保留 Gate results、
snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

```text
entry-gate.json
command-inventory-before.json
command-inventory-after.json
compatibility-diff.json
candidate-identity.json
commands.json
first-failures/
gates/W89-C0.json ... W89-C7.json
final-summary.json
handoff-readiness.json
week89-cli-handoff.json
```

每个 Gate 文件必须通过 `84_92_week_gate_result.schema.json`，handoff 必须通过
`84_92_week_handoff.schema.json`。`compatibility-diff.json` 必须机器比较 26 个顶层
命令及所有子命令、help、arguments/options/defaults/validators、输出 channel/schema
和 exit code；未评审差异数必须为 0。

## Critical Gates

- [ ] W89-C0 Entry Gate、lineage、identity 和授权状态有效。
- [ ] W89-C1 recursive flow baseline 完整。
- [ ] W89-C2 automation/pipeline delegation composition identity 等价。
- [ ] W89-C3 factory cleanup 无 public/test compatibility 破坏。
- [ ] W89-C4 command/help/output/exit/schema inventory 零未评审差异。
- [ ] W89-C5 full .NET/CLI tests/smoke/release/package applicable matrix 全绿。
- [ ] W89-C6 shared freeze、Renderer isolation、cleanup 与 evidence redaction 通过。
- [ ] W89-C7 P0/P1 为 0，exact clean revision、final summary 与 Week90 CLI handoff 完整。

## Exit 与 Handoff

`W89-C7` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未
生成的 `week89-cli-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

只有 W89-C0 至 W89-C7 的 schema-valid 结果全部为 `Passed`、兼容性未评审差异为
0、开放 P0/P1 为 0 且 worktree clean，才能输出 `week89-cli-handoff.json`，其中
`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Lane Ready for Integration`。该 handoff 必须绑定 exact source revision、依赖锁、
command inventory hash、shared freeze diff、完整 Gate evidence hash 和中央 Goal
checkpoint。

任何 `Failed`、适用 Gate 的 `NotRun`、inventory 漂移或无法解释的 shared diff 都
只能令 handoff `decision=Blocked`，不得生成 `ReadyForNextCheckpoint` handoff。
