# Week 89 Renderer Lane 执行计划：Context Panel、Terminal 与 Review Workspace

状态：`Blocked by Week88 Renderer Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-renderer`

并行 CLI 计划：`89_week_cli_automation_pipeline_compatibility.plan.md`

## 执行契约与 Entry Gate

本周必须遵守 `84_92_week_goal_execution_contract.md`。中央 Goal 状态必须通过
`84_92_week_goal_control.schema.json` 校验；每个 Gate 结果使用
`84_92_week_gate_result.schema.json`，Week90 交接使用
`84_92_week_handoff.schema.json`，三者 `schemaVersion` 均为 `1.0.0`。

进入实现前必须同时满足：

- `artifacts/week88-renderer-composer-approval/week88-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Core Chat Interaction Ready`，并且 source revision、dirty state 和 evidence hash 一致。
- Week84 listener/resource baseline 仍为 Passed，Week86 用户视觉确认与 Week87/88 Renderer Gates 均为 Passed。
- 当前 worktree clean，依赖锁、`desktop-v1` exact `39 invoke + 2 event` 和 shared frozen 区无未说明漂移。
- 中央 Goal control 的当前 checkpoint 为 Week89 Renderer Entry，开放 P0/P1 为 0。
- 用户已按统一执行契约确认本地分支/worktree/checkpoint commit、仓库内构建测试和 owned process/temp cleanup；本周不得借 UI 工作消耗真实 provider turn。

任一条件不满足时写入 `gates/W89-R0.json` 为 `Failed` 或 `NotRun`，保留首败并停止产品修改。

## Goal

完成 Chat-first Desktop 的右侧工作区：Changes、Terminal、Reports、Artifacts 和 Preview 使用统一 panel shell，按当前上下文加载、可关闭/resize/drawer，并保持全部安全、ownership 和 correctness 边界。

## Phase 0：Context Contract

定义统一 panel model：

- tab id、title、badge/status、availability、active context identity。
- workspace/thread/turn selection 与 query epoch。
- open/close/resize/drawer/restore focus UI state。
- leaf panel 只接受 data/commands，不直接访问 global bridge。

## Phase 1：Changes 与 Reports

- Changes 显示 summary、file list、bounded diff、truncated/corrupt state。
- Reports 显示 metadata/list/detail、source/status 和 safe content。
- 从 ResultSummary 跳转对应 tab/item。
- workspace/thread switch 后旧 query 不覆盖新上下文。

## Phase 2：Artifacts 与 Preview

- Artifacts list/detail、ownership、availability、verification、export/verify actions。
- Gerber/TIFF Preview 明确区分 preview、metadata、hard verification 和 human decision eligibility。
- missing/tamper/stale/double decision fail closed。
- 图片/文件内容仍由 reviewed AppHost methods 提供，不暴露任意 path API。

## Phase 3：Terminal

- Terminal 从中央固定底栏迁入 context tab。
- 保留显式 Open/Input/Cancel/Close、cwd/status/exit/truncation。
- 8 KiB Renderer tail 与 64 KiB protocol boundary 不变。
- close panel 不等于隐式 kill；用户动作与 process lifecycle 语义明确。
- panel unmount/remount 不重复 listener、poller 或 terminal session owner。

## Phase 4：Responsive 与 Accessibility

- 1440 宽右侧 panel resize。
- 1024 宽 drawer；800 宽 overlay，与左侧 drawer 互斥。
- Escape、tab order、focus return、aria-tabs、live status、reduced motion。
- composer 和当前 approval 不被 panel 覆盖。

## 非目标

- 不增加 Changes revert/discard。
- 不把 Terminal 变成 agent tool 或 approval bypass。
- 不扩大 artifact delete/overwrite/correctness 声明。
- 不修改 CLI lane 文件或 shared frozen contracts。

## 验证

- ReviewInspector/TerminalPanel 迁移 tests。
- query epoch/stale result/context switch tests。
- read-only-shell、desktop-recovery、artifact/Gerber E2E。
- resize/drawer/focus/accessibility visual matrix。
- process tree、terminal cleanup、listener/DOM/private-bytes controls。
- `npm run verify`、unpacked/packaged applicable smoke、.NET full regression。

## 机器证据

固定目录：`artifacts/week89-context-review-workspace/`。

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。组根仅保留 Gate results、
snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

必须生成：

```text
entry-gate.json
candidate-identity.json
screenshot-manifest.json
visual-acceptance.json
commands.json
first-failures/
gates/W89-R0.json ... W89-R7.json
final-summary.json
handoff-readiness.json
week89-renderer-handoff.json
```

`gates/*.json` 必须逐个通过 `84_92_week_gate_result.schema.json`；
`week89-renderer-handoff.json` 必须通过 `84_92_week_handoff.schema.json`。W89
用户视觉确认必须覆盖 1440x900、1024x768、800x900 以及 panel closed/open、
drawer/overlay、approval visible 等冻结场景。请求前先生成
`screenshot-manifest.json`，其中绑定 exact candidate、全部截图/DOM/a11y snapshot 的
相对路径与 raw SHA-256、viewport/fixture identity 和 redaction attestation；该 manifest
在发出请求后不可改写。必须先提交绑定 exact candidate 与该
`screenshot-manifest.json` raw SHA-256 的不可变 `UA-*` challenge request，再向用户展示
同一组材料。用户回复必须回显 request id、challenge code 与 `Passed`/`Failed`；回复后
才生成 `visual-acceptance.json` receipt，绑定 challenge request、screenshot manifest、
规范回复及 user-decision ledger entry。`visual-acceptance.json` 是决定回执，不是请求前
manifest，不得作为 challenge 的预绑定对象，也不得由自动截图或 Agent 自评代替用户决定。
随后只追加 user-decision ledger，不能回写 request 或 screenshot manifest。

## Critical Gates

- [ ] W89-R0 Entry Gate、lineage、identity 和授权状态有效。
- [ ] W89-R1 Context selection/query ownership 单一且 stale safe。
- [ ] W89-R2 Changes/Reports/Artifacts parity 通过。
- [ ] W89-R3 Preview correctness/ownership/decision 边界不扩大。
- [ ] W89-R4 Terminal 无 bypass、orphan 或 duplicate listener。
- [ ] W89-R5 三视口 drawer/resize/focus/a11y 自动化与用户视觉确认均 Passed。
- [ ] W89-R6 full verify/E2E/resources/cleanup 全绿。
- [ ] W89-R7 exact clean revision、P0/P1 为 0、final summary 与 Week90 Renderer handoff 完整。

## Exit 与 Handoff

`W89-R7` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence；它证明用户视觉
receipt、全部自动化 Gate、candidate、父 handoff 与问题清单已具备生成交接的条件，不能
引用尚未生成的 `week89-renderer-handoff.json`。Gate Passed 后才单向生成最终 handoff，
再生成 Gate 外 `preseal-receipt.json` 并封存 anchor；不得回写 Gate 或用户决定。

只有 W89-R0 至 W89-R7 的 schema-valid 结果全部为 `Passed`、用户视觉确认
为 Passed、开放 P0/P1 为 0、worktree clean，才能输出
`week89-renderer-handoff.json`，其中 `decision=ReadyForNextCheckpoint`、
`laneOutput.summary=Desktop Experience Lane Ready for Integration`。该 handoff 必须绑定 exact source
revision、依赖锁、package/AppHost/Renderer identity、Gate evidence hash、shared
freeze diff 和中央 Goal checkpoint。

任何 `Failed`、适用 Gate 的 `NotRun`、未确认视觉结果或 identity 漂移都只能输出
handoff `decision=Blocked`，不得生成 `ReadyForNextCheckpoint` handoff。
