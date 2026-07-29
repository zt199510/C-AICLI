# Week 86 Renderer Lane 执行计划：Chat-first App Shell 与 Design System

状态：`Blocked by Week85 Renderer Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-renderer`

并行 CLI 计划：`86_week_cli_jobs_review_session_modules.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week85-renderer-feature-boundaries/week85-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=State Boundary Ready`。
- Week85 `gates/W85-R0.json` 至 `gates/W85-R7.json` 全部为 `Passed`；feature ownership matrix 中每种 external subscription 的 owner count 为 `1`，unmatched cleanup 为 `0`。
- Week84 listener controls 与 Week85 cleanup/resource tests 全绿，冻结 `desktop-v1` inventory 仍为 exact `39 invoke + 2 event`。
- compatibility facade 能通过 Week85 全部 UI/E2E；中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema并绑定 Week85 exact clean revision，本 lane Entry 结果写入 `gates/W86-R0.json`。

任一条件不满足，Week86 Renderer lane 保持 `Blocked`。

## Goal

建立可复用的视觉 token 和 UI primitives，把现有固定 dashboard 改造成 Chat-first AppShell；本周只改变 shell 与布局，不改变 timeline、composer、approval 或 review 的业务语义。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week86-renderer-chat-first-shell/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week86-renderer-chat-first-shell/
  gates/W86-R0.json ... W86-R7.json
  baseline-identity.json
  desktop-protocol-inventory.before.json
  desktop-protocol-inventory.after.json
  subscription-ownership.before.json
  subscription-ownership.after.json
  subscription-ownership.diff.json
  viewport-matrix.json
  screenshot-manifest.json
  accessibility.json
  resource-controls.json
  test-results.json
  user-visual-confirmation.json
  final-summary.json
  handoff-readiness.json
  week86-renderer-handoff.json
  first-failures/
```

三视口每个 fixture 的 screenshot 与 DOM/a11y snapshot SHA-256 写入 `screenshot-manifest.json`；`viewport-matrix.json` 必须记录 width/height、horizontal overflow、overlap、unreachable controls 和 drawer/focus assertions。请求用户视觉确认前，必须先把绑定 exact candidate 与该 manifest SHA-256 的 `UA-*` challenge request 提交到 `docs_md/weekly/84_92_user_acceptance_requests/`；用户回复需回显 request id、challenge code 与 `Passed`/`Failed`。用户视觉确认只记录选择与截图 hash，不替代自动 Gate。schema、first-failure、user-decision ledger 与 immutable evidence-anchor 规则按统一执行契约。

## 视觉方向

视觉参考固定为 `AIDotNet/OpenCowork@b4afc37d0f77a4bce8da7c16deb2bddc94bd5dfb`，只取 persistent workspace/thread navigation、conversation-owned main canvas、secondary inline tool/status disclosure 与 bottom-docked composer 的信息架构；不复制 OpenCowork 品牌或新增其功能。

- 参考 OpenCowork 的轻量桌面层级、紧凑导航、对话优先和可切换上下文面板。
- 保留 C-AICLI 的 Windows/.NET、可审计和工程工具定位。
- 不复制 OpenCowork 的品牌资产、完整组件代码或不属于本产品的导航入口。

## Phase 0：Visual Baseline

- 固定 1440x900、1024x768、800x900 三个 viewport。
- 记录 open workspace、selected thread、waiting approval、failed runtime、review/terminal open 等 fixture。
- 建立可比较的 screenshot/DOM/accessibility baseline。

## Phase 1：Design Tokens

建立 CSS variables：

- surface/background/border/text/accent/success/warning/danger。
- spacing、radius、shadow、font size/line height、panel width。
- focus ring、reduced motion、high contrast fallback。

同步更新 `check-accessibility.mjs`，避免继续硬编码旧颜色值。

## Phase 2：UI Primitives

最小集合：

- Button、IconButton、Badge、Tabs。
- Panel、Drawer、Separator、ScrollArea shell。
- Tooltip、EmptyState、InlineError、StatusIndicator。

每个 primitive 必须有 keyboard、disabled、focus-visible 和 accessible name tests；不引入未经资源/notice 审核的大型组件库。

## Phase 3：AppShell

- compact titlebar/header。
- 左侧 workspace/thread navigation。
- 中央 conversation surface 与底部 composer slot。
- 右侧 context panel slot，可 close/resize。
- 1024px 下右侧 drawer；800px 下左右 drawer 互斥。
- Escape、trigger focus restore、screen reader label 和 aria-expanded 完整。

现有 Timeline、Terminal、TaskControls、Composer、ReviewInspector 暂作为 slot 内容接入，不在本周重写。

## Phase 4：Responsive 与视觉 QA

- 三视口无 overlap、horizontal body scroll 或不可达 controls。
- 中央 conversation 与 composer 永远保持最小可用宽高。
- 长 workspace/thread title 有稳定 truncation 和 tooltip/accessibility text。
- light/dark 若未进入范围，只交付 light + system high contrast，不伪造完整主题支持。

## 非目标

- 不重写 ConversationBlock、TimelineItem 或 virtual window。
- 不移动 approval/cancel/recovery 语义。
- 不迁移 Terminal/Review 内容，只提供目标 panel shell。
- 不修改 `desktop-v1`、Application、AppHost 或 CLI lane 文件。

## 验证

- primitive RTL tests。
- AppShell responsive/focus/drawer tests。
- `npm run check:accessibility`、typecheck、lint、test、build。
- unpacked E2E 的 workspace/thread/open/collapse 基本流程。
- Week84 listener regression 和 Week77/80 适用资源对照。

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

AppShell E2E 必须对 5 个冻结 fixture（open workspace、selected thread、waiting approval、failed runtime、review/terminal open）各运行 `1440x900`、`1024x768`、`800x900`，形成 exact `15` 组截图/DOM/a11y evidence。机器判定同时满足：

- `viewport-matrix.json` 的 `caseCount=15`、missing/overlap/horizontal-overflow/unreachable-control count 均为 `0`；1024 右 drawer 与 800 左右 drawer 互斥断言全绿。
- Escape、trigger focus restore、Tab 顺序、accessible name、`aria-expanded`、reduced motion 与 forced-colors assertions failure count 为 `0`。
- `accessibility.json` 中 `npm run check:accessibility` exit code 为 `0`，自动 violation count 为 `0`；contrast/focus/high-contrast token checks 全绿。
- protocol after 为 exact `39 invoke + 2 event` 且 canonical hash 与 before 相同；现有 timeline/composer/approval/review fixture outcome 与 Week85 baseline 相同。
- subscription diff 的 duplicate/unmatched 均为 `0`；resource workload 与 Week84/85 相同，listener delta `<=40`、Renderer private-bytes delta `<=15%`，DOM/resync 不超过 frozen limit。
- 用户对 `screenshot-manifest.json` 所绑定 15 组证据给出确认，`user-visual-confirmation.json.status=Passed`；自动 Gate 不可替代该确认。
- 上述命令 exit code 全为 `0`，owned process/temp/config cleanup delta 为 `0`。

## Critical Gates

- [ ] W86-R0 Week85 Renderer handoff 有效。
- [ ] W86-R1 tokens/primitives 不降低 contrast、focus 或 high-contrast 行为。
- [ ] W86-R2 三视口中央主区与 composer 可用。
- [ ] W86-R3 drawer Escape/focus restore/互斥通过。
- [ ] W86-R4 现有 feature parity 与 `39 invoke + 2 event` 不变。
- [ ] W86-R5 listener `<=40`、private-bytes `<=15%`，DOM/resync observed 不超过 Week85 handoff 冻结 limit。
- [ ] W86-R6 verify/E2E/build/cleanup 全绿。
- [ ] W86-R7 用户对绑定 exact screenshot hashes 的视觉 Gate 明确确认，状态为 `Passed`。

## Exit Gate / Handoff

`W86-R7` 的用户决定与 closure 只绑定 Gate-local `handoff-readiness.json`；它证明 15 个
截图 hash、visual receipt、candidate、父 handoff 和自动化 Gate 已具备生成交接的条件，
不得引用尚未生成的 `week86-renderer-handoff.json`。Gate Passed 后才生成最终 handoff，
随后生成 Gate 外 `preseal-receipt.json` 并封存 anchor；不得回写用户决定或 Gate。

- `gates/W86-R0.json` 至 `gates/W86-R7.json` 逐个通过 Gate result schema且全为 `Passed`；`user-visual-confirmation.json.status` 为 `Passed`。
- `final-summary.json` 的 open P0/P1 为 `0/0`，列出 15 组截图 hash、a11y、subscription 与 resource evidence。
- `week86-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Shell Ready for Feature Migration`，并绑定 exact clean HEAD、feature parity、protocol inventory、viewport matrix 和 Week87 entry prerequisites。
- exact clean HEAD 不包含 CLI lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `Shell Ready for Feature Migration` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
