# Week 91 执行计划：Refactor Security、Accessibility、Resource 与 Package Hardening

状态：`Blocked by Week90 Integration Gate`

创建日期：2026-07-28

固定产品集成分支：`codex/week84-92-integration`（control spine：`codex/week84-92-refactor`）

执行依据：`84_92_week_goal_execution_contract.md`。中央 Goal、Gate 和 handoff
分别使用 `84_92_week_goal_control.schema.json`、
`84_92_week_gate_result.schema.json`、`84_92_week_handoff.schema.json`
（`schemaVersion: 1.0.0`）。

## Goal

对 Week90 exact integration candidate 执行完整 hardening，关闭 UI/CLI 重构引入的安全、无障碍、资源、package、兼容性和清理风险；本周只修复 Gate 暴露的问题，不增加产品范围。

## Entry Gate

- `artifacts/week90-cli-desktop-integration/week90-integration-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Integration Candidate Ready for Hardening`。
- handoff 与 integration source revision、依赖锁、package/AppHost/Renderer identity 一致。
- integration source revision clean、package 可重建、两条 lane 与 W90 Gate evidence hash 可追踪。
- 无开放 P0/P1；若有则先回到归属 lane 修复。
- W86/W89 用户视觉确认均为 Passed；中央 Goal control 位于 Week91 Entry。
- 授权 ledger 记录 W91 可执行仓库内 Windows Narrator/人工 UX hardening；真实 provider 与 `.env.local` 授权仅属于 Week84/Week92 的冻结 Gates，W91 不得读取 `.env.local`、调用真实 provider、声明 provider scope 或追加 provider turn。用户只在已确认的 W86/W89/W92 checkpoint 被请求验收，W91 证据并入 W92 最终复核。

任一条件不满足时写入 `gates/W91-G0.json` 为 `Failed` 或 `NotRun`，停止
hardening candidate 修改。

## Phase 0：Candidate 与预算冻结

- 记录 source、dependency lock、protocol、command inventory。
- 记录 baseline/current package、tree、app.asar、AppHost、Renderer hashes 和 sizes。
- 冻结 Week77 private-bytes、Week80 controls、Week84 listener bounds、DOM/resync/worker/retry limits。
- 冻结 responsive viewport 和 accessibility scenarios。

## Phase 1：Security Review

### Desktop

- contextIsolation/nodeIntegration/sandbox/CSP/navigation/new-window。
- Preload exact allowlist、runtime validation、no generic IPC。
- Renderer no Node/fs/process/API key/arbitrary path。
- approval/terminal/artifact/preview/recovery 无 bypass。
- error/crash diagnostics、redaction、clipboard/drag-drop/external link。

### CLI

- composition injection 不能绕过 workspace guard、approval、shell/MCP policy。
- recursive root delegation 不能丢失 global options、trace、logger 或 security context。
- JSON/NDJSON 不泄漏 verbose/secret。
- output/export/no-overwrite/path/reparse 安全行为兼容。

## Phase 2：Accessibility 与 UX Hardening

- keyboard-only 完成 workspace -> thread -> compose -> approval -> review。
- drawer/panel Escape、focus trap/return、tab order。
- screen reader labels/live regions/status。
- high contrast、focus-visible、reduced motion、zoom 和长文本。
- Windows Narrator 人工 Gate 使用冻结 checklist，由 Goal-owned test operator 在
  exact candidate 上完成并记录真实观察；ARIA 自动化或历史 Week77 证据不得代替。
  W91 不新增用户暂停点，operator evidence 与 candidate manifest 在 W92 由用户一并
  复核。未执行即 W91-G2 `NotRun`，不能生成 Week92 Ready handoff。
- W91-G2 `Passed` 时使用 `operator-narrator-manual-ux` authorization scope；不得写成
  user-owned Narrator scope，也不得把 Goal-owned operator evidence 冒充用户验收。

冻结 checklist revision 为 `w91-narrator-operator-ux-v1`。`narrator-manual.json` 必须由
`performedBy=GoalTestOperator` 在 exact candidate 上逐项记录以下六项真实观察并全部为
`Passed`：`screen-reader-start-navigation`、`workspace-thread-conversation-landmarks`、
`streaming-status-and-tool-disclosure`、`approval-prompt-and-decision`、
`error-recovery-and-focus-return`、`context-drawer-review-terminal`。
`operator-ux-attestation.json` 同样绑定 revision/candidate/operator，并逐项记录
`keyboard-only-primary-flow`、`escape-focus-return`、`zoom-high-contrast-reduced-motion`、
`long-text-narrow-viewport`。两份文件都必须包含非空、逐项对应的 `observations`、
`observedAt` 与 `issuePointers`；`Passed` 时 issue pointers 必须为空，不能只写一个
`status=Passed` 空壳。

## Phase 3：Resource 与 Long-session

- Week77 5 profiles。
- Week80 C0-C7 controls。
- Week84 listener ownership/control。
- 2,000 item unit、240 item E2E、turn switch、panel open/close、terminal lifecycle。
- private bytes、working set、JS heap、DOM、documents、listeners、resync、workers/retries。
- 任何超限先保存 evidence，再定位 owner；禁止调阈值或 forced GC/reload。

## Phase 4：Package 与 E2E

- clean build/package。
- dependency/license/notices/security audit。
- unpacked/packaged read-only、write、approval、cancel、recovery、terminal、artifact/preview smoke。
- process tree/temp/config cleanup。
- CLI release build、default smoke 和 package inventory。
- 所有 provider-facing smoke 必须使用 credential-free deterministic fake/mock；禁止读取
  `.env.local` 或调用真实 provider，`providerTurnsConsumed=0` 且 scopes 中不得出现
  `provider-read-only`、`provider-recovery`、`provider-resource` 或 `controlled-write`。
- W91 只读核对 Goal 级 `provider-turn-ledger.json` 的既有 prefix 与 120-turn 上限，
  不得追加 entry；该 ledger binding 不代表 W91 获得 provider 授权。

## Phase 5：Remediation Bound

只允许修复 hardening 暴露的 P0/P1：

- 每个修复必须有 baseline red/candidate green regression。
- 修改后重跑受影响定向 + 完整上游 Gate。
- 超出一周或需要协议/runtime redesign 时，Week91 `Blocked` 并创建独立后续计划。

## 机器证据

固定目录：`artifacts/week91-refactor-hardening/`。

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。provider ledger binding 使用
专用 `provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`；组根只保留组级 canonical 文件。

```text
entry-gate.json
candidate-identity-before.json
security-review.json
automated-accessibility.json
narrator-manual.json
operator-ux-attestation.json
resource-requalification.json
provider-ledger-bindings/W91-G0/provider-ledger-binding.json
provider-ledger-bindings/W91-G4/provider-ledger-binding.json
provider-ledger-bindings/W91-G7/provider-ledger-binding.json
package-e2e.json
cleanup.json
commands.json
first-failures/
gates/W91-G0.json ... W91-G7.json
candidate-identity-after.json
final-summary.json
handoff-readiness.json
week91-hardening-handoff.json
```

每个 Gate 文件必须通过统一 Gate schema，Week92 handoff 必须通过统一 handoff
schema。`narrator-manual.json` 与 `operator-ux-attestation.json` 必须包含 checklist revision、candidate identity、
Goal-owned test operator、观察和 issue pointers，并在 W92 用户最终验收 manifest 中被引用；
三个 Gate-local `provider-ledger-binding.json` 必须对中央 canonical ledger 的各 Gate 时点
prefix 精确重算，且 W91 新增 turn 为 `0`；不得复制 live ledger 到本周 artifact。

## Critical Gates

- [ ] W91-G0 exact Week90 candidate 与预算冻结。
- [ ] W91-G1 Desktop/CLI security review 无开放高风险项。
- [ ] W91-G2 automated accessibility、Windows Narrator 与人工 UX Gate 均 Passed。
- [ ] W91-G3 Week77/80/84 resources 和 long-session 全绿。
- [ ] W91-G4 unpacked/packaged/CLI smoke/package audits 全绿。
- [ ] W91-G5 process/temp/config cleanup 为 0，evidence 脱敏。
- [ ] W91-G6 所有 remediation 有 red/green 和完整 rerun。
- [ ] W91-G7 clean hardening candidate 与 Week92 handoff 完成。

## Exit 与 Handoff

`W91-G7` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未
生成的 `week91-hardening-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成
Gate 外 `preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

只有 W91-G0 至 W91-G7 全部 schema-valid 且为 `Passed`、Goal-owned operator 的 Narrator/人工 UX
证据有效、W91 provider turn 增量为 0、Goal 累计不超过 120、P0/P1 与 cleanup 为 0，才能生成 schema-valid
`week91-hardening-handoff.json`，其中 `decision=ReadyForNextCheckpoint`、
`laneOutput.summary=Candidate Ready for Final Acceptance`。handoff 必须绑定 exact hardening revision、依赖锁、
package/AppHost/Renderer identity、Narrator evidence hash、资源预算和所有 Gate
hash。

Narrator/operator UX `NotRun`、credential-free/mock provider-facing smoke 未执行、出现任何 W91 真实 provider turn、资源超限或无法解释的 remediation
都只能令 handoff `decision=Blocked`。
