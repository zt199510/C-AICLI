# Week 87 Renderer Lane 执行计划：Message-first Conversation Projection 与 Timeline

状态：`Blocked by Week86 Renderer Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-renderer`

并行 CLI 计划：`87_week_cli_exec_skills_queue_modules.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week86-renderer-chat-first-shell/week86-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Shell Ready for Feature Migration`。
- Week86 三视口、keyboard/focus、accessibility、subscription 和 resource Gate 全为 `Passed`；用户 Week86 视觉确认已记录为 `Passed`。
- `desktop-v1` canonical inventory 仍为 exact `39 invoke + 2 event`，hash 等于 Week86 handoff。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema并绑定 Week86 exact clean Renderer revision；本 lane Entry 结果写入 `gates/W87-R0.json`。

任一条件不满足，Week87 Renderer lane 保持 `Blocked`。

## Goal

在不改变 durable timeline truth 的前提下，引入纯 presentation projection，把原始 protocol events 组织成用户可理解的对话、工具过程、审批和结果，同时保留完整审计入口与有界 DOM。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week87-renderer-conversation-projection/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week87-renderer-conversation-projection/
  gates/W87-R0.json ... W87-R5.json
  baseline-identity.json
  desktop-protocol-inventory.before.json
  desktop-protocol-inventory.after.json
  timeline-contract-inventory.json
  projection-golden-results.json
  subscription-ownership.before.json
  subscription-ownership.after.json
  subscription-ownership.diff.json
  viewport-matrix.json
  accessibility.json
  dom-resource-bounds.json
  resource-controls.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week87-renderer-handoff.json
  first-failures/
```

`timeline-contract-inventory.json` 必须证明每个 frozen timeline type 恰有一个 projection case，unknown/new type 进入 `AuditFallback`；`projection-golden-results.json` 对 canonical fixture 零未审计差异。`dom-resource-bounds.json` 分别记录 2,000-item unit 与 240-item E2E 的 frozen DOM 上限及 observed 值。schema 与 first-failure 规则按统一执行契约。

## 核心模型

定义穷举联合类型：

```text
ConversationBlock =
  UserMessage
  | AssistantMessage
  | ToolGroup
  | ApprovalBlock
  | WarningBlock
  | ResultSummary
  | AuditFallback
```

`ConversationBlockProjector` 必须是纯函数：输入 `TurnSummaryData + TimelineItemData[]`，输出 presentation blocks；不得访问 bridge、DOM、时间、随机数或全局 state。

## Phase 0：Contract Inventory

- 枚举 `desktop-v1` 全部 timeline type 和 payload kind。
- 为每个 type 指定主 presentation、分组规则、状态、icon、summary 和 audit fallback。
- 特别覆盖 `assistant.final`、verification、approval、changes/report/artifact 和 unknown-safe fallback。

Phase Gate：每种 contract type 都有 test case，新增 type 会使 exhaustiveness test 失败。

## Phase 1：Projection Rules

- user/assistant message 保持 turn 顺序。
- 连续 tool/command/verification events 按稳定 identity 组成 ToolGroup。
- approval 永不被折叠到不可见区域；resolved/expired/stale 显示明确状态。
- warning/failure/result summary 不因分组消失。
- redacted payload 不重新展开 raw value。
- source pointer、sequence、timestamp、status、item id 和 raw bounded payload 保留在 audit details。

## Phase 2：Conversation UI

- message typography、assistant markdown-safe presentation、code/pre overflow。
- ToolGroup 默认 collapsed，运行中/失败/待审批自动展示必要摘要。
- final summary 与 changed/report/artifact pointers 提供右侧 panel 跳转，不直接读取资源。
- 用户位于底部时跟随新 block；阅读历史时不强制滚动。

## Phase 3：Bounded History

- 延续有界 Turn window，不直接恢复旧 virtualizer。
- 2,000 item unit fixture 和 240 item E2E 下 DOM elements 保持冻结上限。
- load-more、turn switch、notification append、authoritative replace 不重复 blocks。
- projection memo/cache 必须随 thread/workspace/turn owner 释放。

## Phase 4：Audit View

- 每个原始 item 可从对应 block 追溯。
- unknown/new item 使用 AuditFallback，不静默丢弃。
- corrupt/truncated/redacted 状态有稳定 UI 和 screen-reader text。

## 非目标

- 不改 AppHost timeline schema 或 durable records。
- 不改变 approval mutation、composer 或 Terminal。
- 不引入完整 markdown extension、Monaco 或 IDE preview。
- 不修改 CLI lane 文件。

## 验证

- projector exhaustive/golden/property-like ordering tests。
- duplicate, out-of-order, truncated, corrupt, redacted 和 unknown type tests。
- 2,000 item unit 与 240 item E2E DOM bound。
- scroll anchoring、load more、new event follow behavior。
- Week81/83 projection、Week84 listener、Week80 memory controls。
- accessibility/typecheck/lint/test/build/unpacked E2E。

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

- frozen timeline contract type count 等于 `timeline-contract-inventory.json.caseCount`，missing/duplicate projection case 为 `0`；注入一个 unknown type 时恰好生成一个 `AuditFallback`。
- golden fixtures 的 block kind/order/identity/source/sequence/status/redaction canonical diff 为 `0`；duplicate/out-of-order/truncated/corrupt fixtures 全部得到冻结 outcome。
- 2,000-item unit 与 240-item E2E 的 rendered element、document、listener 和 cache survivor observed 均 `<=` entry 中绑定的 frozen limit；load-more/replace 后 duplicate block count 为 `0`。
- `viewport-matrix.json` 覆盖 `1440x900`、`1024x768`、`800x900`，overflow/critical-overlap/unreachable-control 为 `0`；scroll anchoring assertion 全绿。
- `accessibility.json` 中检查 exit code 为 `0`、自动 violation count 为 `0`，AuditFallback、redacted、failure 与 approval 状态都有 accessible name/status。
- subscription diff duplicate/unmatched 均为 `0`；resource workload 不变，listener delta `<=40`、Renderer private-bytes delta `<=15%`，DOM/resync 不超过 frozen limit。
- protocol after 为 exact `39 invoke + 2 event` 且 canonical hash 与 before 相同；所有命令 exit code 与 cleanup delta 为 `0`。

## Critical Gates

- [ ] W87-R0 Contract inventory 穷举且新 type fail closed。
- [ ] W87-R1 message/tool/approval/result grouping golden tests 通过。
- [ ] W87-R2 原始 sequence/source/status/redaction 可追溯。
- [ ] W87-R3 2,000/240 item DOM 与 memory bounds 通过。
- [ ] W87-R4 notification duplicate block、超限 resync 与 stale response commit count 均为 `0`。
- [ ] W87-R5 verify/E2E/a11y/cleanup 全绿。

## Exit Gate / Handoff

`W87-R5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未生成
的 `week87-renderer-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

- `gates/W87-R0.json` 至 `gates/W87-R5.json` 逐个通过 Gate result schema且全为 `Passed`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 contract/golden/DOM/a11y/subscription/resource evidence SHA-256。
- `week87-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Conversation Projection Ready`，并绑定 exact clean HEAD、frozen DOM limits、protocol inventory 和 Week88 entry prerequisites。
- exact clean HEAD 不包含 CLI lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `Conversation Projection Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
