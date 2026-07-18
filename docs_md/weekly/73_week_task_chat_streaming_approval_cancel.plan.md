# 第 73 周执行计划：Task Chat、Streaming、Approval 与 Cancel

**Goal:** 在 Week 72 已通过验收的 Composer、Catalog、受控上下文与 one-per-thread pending intent 基础上，交付 Desktop 首条 write-capable Task Chat 闭环：从 AppHost/Core 权威队列原子认领输入、创建 Turn 与 `user.message`、通过共享 Application/runtime path 执行 fake/real-capable agent、持续追加 bounded timeline、等待并解析 approval、支持 cooperative cancel，以及在明确安全边界内 resume/restart，最终生成可审计的 assistant/final summary。Renderer 不得重发 canonical prompt/path/tool arguments，不得直接决定 approval、执行 tool/shell 或把 notification 当作事实源。

**Architecture:** 扩展 `desktop-v1` reviewed contract 与现有 21+2 bridge，而不是引入 generic run/stream IPC。Core 为 pending intent 到 Turn 的跨 store 切换增加 durable claim、deterministic turn binding、idempotent start receipt 与收敛恢复；Application 定义可注入的 turn runtime、approval coordinator、cancel/recovery use case，并复用现有 `IAgentRunner`、tool registry、workspace guard、approval policy、redaction、conversation/report/changes 路径。AppHost 持有唯一 write execution supervisor 和 cancellation/approval waiter；持久化 timeline 是真相，`thread.changed` 只做 coalesced dirty hint，Renderer 始终用 `thread.get` 按 sequence authoritative resync。Approval 只接受绑定当前 turn/request/revision/policy/action hash 的 decision；cancel 先持久化 `canceling` 再触发 token；resume 只允许已有安全 checkpoint，restart 必须显式确认并从不可变 canonical input 创建新 Turn。

**Tech Stack:** .NET SDK `9.0.308`、Core/Application/AppHost versioned stores、现有 `IAgentRunner` 与 tool/approval policy path、`desktop-v1` framed JSON-RPC over stdio、Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`。

---

状态：Pending

创建日期：2026-07-17

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`78b6bacc04a4755e65b041b76bebf8dd4f583b71`

执行约束：本次仅创建计划，不实现 Week 73 功能。后续执行继续在当前分支原地开发，不创建 worktree 或新分支；保留用户已有改动，不覆盖无关文件。只有全部 Critical Gate 通过后，plan、review 与 schedule 才能同步改为 Passed。

## 本周目标

1. 将 Week 72 pending composer intent 安全、幂等、可恢复地转换为一个 write-capable Turn；任何 crash/retry 都不得丢输入、重复建 Turn 或重复执行 write step。
2. 抽出 CLI/Desktop 共享的 Application execution vertical slice；AppHost 不复制 agent loop、tool registry、workspace guard、approval、redaction、report 或 changes 逻辑。
3. 将 plan/model/tool/command/verification/changes/assistant/final 状态投影为 bounded、versioned、append-only timeline，并以 sequence/revision 做权威增量同步。
4. 建立 durable UI-safe approval request 与一次性 resolve 语义，覆盖 approve、deny、expired、stale、duplicate、cancel 与 AppHost disconnect。
5. 建立 cooperative cancel、`canceling`/`canceled` 状态、重复请求幂等与有限等待；不得以 Renderer 隐藏状态或杀 AppHost 代替取消。
6. 冻结 resume/restart 安全边界：只从已证明的 checkpoint resume；无法证明时要求 explicit restart，不自动重放可能已产生副作用的动作。
7. 完成 Task Chat streaming UX、approval card、cancel/recovery controls、键盘与 screen reader 行为，并保持 Composer/context/catalog 的 Week 72 安全边界。
8. 用默认无凭据 fake runtime 的 unpacked/packaged E2E 完成 run、approve/deny、cancel、resume/restart 与 final summary；保持 CLI、.NET、Electron security、package/process baseline 不回归。

## 起点与稳定输入

### Week 72 已通过输入

- 最终起点提交为 `78b6bacc04a4755e65b041b76bebf8dd4f583b71`，clean-source manifest 为 `sourceDirty=false` / `releaseAcceptance=true`。
- `desktop-v1` 协商 hash 为 `1a66bec61e044dd7e684f32361053fc534ff78d9eb27b8bbfc66675ef8f7aa3a`；Main/Preload bridge 为 exact 21 invoke + 2 event。
- Core/AppHost 已有 one-per-thread pending intent、workspace/root identity、queue revision、mutation idempotency、restart recovery 与 explicit clear。
- pending intent 保存 enqueue-time canonical prompt、relative context reference、catalog reference、effective model/approval summary；Renderer 不持有 absolute path 或 file bytes。
- `composer.enqueue` 不创建 Turn/timeline、不执行 agent/model/tool/shell；active turn 时 delivery 为 `next-turn`。
- Renderer 已有 memory-only thread draft、`@` mention/native picker、error preservation、epoch guards 与 authoritative `composer.get` resync。
- Week 72 最终回归：.NET `1,372/1,372`、Vitest `84/84`、unpacked/packaged E2E、真实 packaged AppHost Composer smoke、四视口与 orphan delta 0 全部通过。

### 当前可复用能力

- `ThreadStore` 已支持 versioned Thread/Turn/Timeline、expected revision、append-only timeline、mutation receipts、状态机、atomic replace 与 corrupt/recovery diagnostics。
- Turn 状态已包含 `queued`、`running`、`waiting-for-approval`、`canceling`、`canceled`、`failed`、`completed`；Thread 状态已包含对应投影。
- Timeline 已支持 user/assistant/plan/tool/command/approval/changes/report/artifact/warning/completed 结构化 item，单 item、thread 总量与 sequence 均有边界。
- Core 已有 `IAgentRunner`、agent events、tool executor/registry、workspace guard、approval policy resolver、secret redaction、conversation、changes、report、artifact 与 trace 路径。
- AppHost 已有 framed RPC deadline、in-flight cancellation、bounded output queue、workspace session、thread.changed notification 与 graceful shutdown。
- Renderer 已有 timeline pagination/virtualization、sequence resync、status projection、reload/restart epoch 与 three-pane responsive shell。

### 当前缺口

- Composer queue 与 ThreadStore 是两个持久化边界；当前没有 durable claim 或 crash-convergent consume，直接 clear + CreateTurn 会产生丢失/重复窗口。
- `ThreadStore.CreateTurn` 尚未把 Turn 与初始 `user.message` 作为同一幂等 mutation 提交，也没有绑定 source intent/input hash。
- Application 尚无 Desktop write execution use case/runtime port；现有 agent composition 主要仍在 CLI command 路径。
- 现有 approval policy 面向非交互 CLI 决策，没有 durable Desktop request/waiter/resolve contract。
- 没有 turn start/cancel/resume/restart 或 approval resolve protocol、AppHost supervisor、reviewed IPC/Preload surface。
- `thread.changed` 尚未覆盖持续执行的 bounded/coalesced timeline resync；Renderer 没有 live run/approval/cancel/recovery controller。
- Week 72 fixture 只验证 queue persistence，不验证 agent events、approval、cancel 或 final summary。

## 方案选择

### 1. Pending intent 使用 durable claim 收敛到唯一 Turn

启动协议固定为：

```text
turn.start(thread, intent, expected revisions, mutation id)
-> 重新验证 workspace/context/catalog/effective policy
-> queue durable claim(intent id + input hash + deterministic turn id)
-> ThreadStore atomic StartTurn(user.message + queued/running turn)
-> 证明 turn/input hash 与 claim 一致
-> finalize queue consumption receipt
-> AppHost supervisor 执行该 turn
```

- queue record 增加 `pending | claimed` lifecycle；claim 保存 `claimId`、`intentId`、deterministic `turnId`、canonical input hash、start mutation id 与 source queue revision，不保存新的 absolute path 或 approval grant。
- `ThreadStore.StartTurnFromIntent` 在一次 thread lock 内创建 Turn、更新 active turn、追加首条 `user.message` 与 mutation receipt；同 mutation/payload retry 返回同一 Turn，不追加第二条 timeline。
- crash 在 claim 后、CreateTurn 前：恢复时复用同一 turn id；crash 在 CreateTurn 后、queue finalize 前：读取并校验 turn/input hash 后只完成 finalize。
- claim 与已存在 Turn 不匹配、receipt hash 不匹配或两侧 revision 不可收敛时 fail closed 为 recovery/corrupt；不得猜测成功或创建第二个 Turn。
- `turn.start` 是 start request，不接受 prompt/context/tool arguments。`next-turn` 在当前 active turn 终态后只启动该 thread 已明确请求的单个 pending intent，不建立跨 thread scheduler。

### 2. AppHost 持有唯一 write execution supervisor

- `Application` 定义 `ITurnExecutionRuntime`/event sink 等结构化 port；默认生产 adapter 复用现有 Core agent/tool/policy path，测试注入 deterministic fake runtime。
- AppHost supervisor 持有 active execution、linked cancellation token、approval waiter 与 terminal completion；Renderer/Main 不持有执行 task 或 policy state。
- 0.6.0 冻结为每个 AppHost workspace session 最多 1 个 active write execution；其它 thread 的 pending intent 可保存，但 `turn.start` 稳定返回 busy/queued，不并行修改同一 workspace。
- workspace change、shutdown 或 AppHost disconnect 取消 supervisor，并先持久化可判定状态；不得让后台 execution 越过 workspace generation。
- runtime event 只有在 ThreadStore append 成功后才通知 UI；内存 event、stdout token 或 Electron event 都不是执行真相。

### 3. Timeline streaming 使用持久化事实 + dirty hint

- 延续 `thread.changed` notification，不增加 raw token stream/event-as-truth channel；notification 只携带 workspace/thread/revision/committed sequence/原因。
- AppHost 对高频 event 做 bounded batch 与 coalesce，先按 sequence 原子 append，再最多按固定频率发 dirty hint；Renderer 用 `thread.get(afterSequence)` 补齐。
- model/token 输出在 Application 层聚合为 bounded assistant preview/chunk，经过 redaction 后持久化；不得把 provider raw object、secret、完整 command output 或 tool arguments投影到 timeline。
- tool/command output 默认 collapsed，只保存 safe summary、status、error code、duration 与受控 source pointer；完整输出继续留在既有受控 trace/report store。
- duplicate/out-of-order runtime event 通过 correlation/event sequence/mutation receipt 去重；timeline sequence 只由 ThreadStore 分配。

### 4. Approval 是绑定动作身份的一次性 durable decision

- Approval request 绑定 `workspaceId`、`threadId`、`turnId`、`requestId`、turn revision、policy identity/revision、risk、operation、canonical action hash、created/expiry 与 UI-safe summary。
- timeline/Renderer 只获得 safe summary、risk、target class、policy mode 与 expiry；不获得 secret、raw shell stdin、完整 patch/tool arguments 或可复用 approval token。
- `approval.resolve` 只接受 `{requestId, decision, expectedTurnRevision, expectedApprovalRevision, clientMutationId}`；AppHost/Application 重新加载当前 request 并 constant-time 比较 binding。
- approve/deny 只能消费一次；duplicate same mutation id 幂等，different decision、expired、stale、wrong turn/workspace、已 cancel 或 policy 改变均稳定拒绝。
- approval waiter 只在对应 active execution 中恢复。AppHost crash 后旧 waiter 不存在，request 标记 recovery-required/stale；不得在新进程中仅凭 UI decision 自动执行旧 action。
- CLI 继续使用非交互 policy adapter；Desktop interactive adapter 复用同一风险/policy判断，只替换“如何取得用户决定”，不绕过 policy。

### 5. Cancel 先持久化意图，再触发 cooperative cancellation

- `turn.cancel` 要求 active turn id、expected turn revision 与 client mutation id；成功先把状态改为 `canceling` 并追加 warning/status item，再触发 linked token。
- runtime 确认 `OperationCanceledException` 或 canceled result 后转为 `canceled`、追加 terminal summary、清理 approval waiter；重复 cancel 幂等。
- terminal turn、错误 turn id/revision、stale workspace 或不同 mutation payload 稳定拒绝；cancel 不等于 shutdown、kill AppHost 或删除 timeline。
- cancel grace 到期仍未返回时保持 `canceling/recoveryRequired` 并报告 diagnostics；不得假装 canceled，也不得在同一 workspace 启动另一个 write execution。
- cancel 与 approval.resolve race 由单一 supervisor/turn revision 串行化，只能一个决议胜出，另一个返回 stale/conflict。

### 6. Resume 与 restart 明确区分

- `turn.resume` 只允许 `recoveryRequired=true` 且 Application/runtime 提供匹配 input hash、event checkpoint、workspace identity 与“尚未越过未确认 write boundary”的 durable checkpoint。
- 无 checkpoint、action 结果未知、policy/context/catalog 已变化或 previous write 可能部分完成时，resume 返回 `restart-required`/`manual-review-required`，不自动重放。
- `turn.restart` 必须指定 terminal/recovery source turn、expected revision、explicit confirmation 与 client mutation id；Application 从 source turn 的 immutable canonical input snapshot 创建新的 Turn，并重新验证 context/catalog/model/policy。
- restart 不复制 approval grant，不跳过已完成检查，不隐藏旧 failed/canceled timeline；新旧 Turn 使用 source correlation 建立可审计关联。
- Week 73 不承诺任意 provider/tool 的 mid-call checkpoint；fake runtime 仅在已冻结的 safe boundaries 演示 resume。完整 crash matrix留到 Week 75 加固。

## 范围冻结

### 本周包含

- Core queue claim/finalize/recovery、idempotent StartTurn、execution/approval UI-safe records 与状态机增量。
- Application turn start/supervise/cancel/approval resolve/resume/restart use case、runtime port 与 CLI parity extraction。
- fake agent/runtime：deterministic plan/model/tool/approval/command/verification/changes/final events，可控 pause/cancel/failure/checkpoint。
- `desktop-v1` contract/schema/examples/generated C#/TS、AppHost handlers/supervisor、exact Main/Preload bridge。
- Renderer live timeline resync、running/approval/cancel/recovery controls、final summary 与可访问性。
- .NET/Vitest/RTL/Playwright、real packaged AppHost smoke、security scan、visual/package/process 与 clean-source evidence。

### 本周不包含

- 用户 terminal/xterm、arbitrary shell console、agent 向 terminal 注入 input 或 terminal 作为 approval bypass。
- artifact bytes/open/export/delete、Changes revert/discard、Gerber/TIFF accept/reject 或 external tool correctness。
- 多 workspace/multi-worker 并发、跨 thread queue scheduler、后台 automation schedule/run。
- 任意 provider raw token/raw response 直达 Renderer、unbounded command/tool output 或 protocol binary stream。
- approval “always allow”、跨 turn grant、记住决定、policy editing、arbitrary model override。
- 自动重放 unknown write、AppHost crash 后自动 approve/resume、silent restart 或 duplicate Turn 隐藏。
- Web/remote control、team/RBAC、plugins marketplace、IDE 能力。

## 冻结的状态、身份与上限

### 执行状态

```text
composer pending
  -> claimed
  -> turn queued
  -> running
  -> waiting-for-approval
  -> running
  -> completed | failed

running | waiting-for-approval
  -> canceling
  -> canceled | failed

recoveryRequired
  -> safe resume | explicit restart | manual review
```

- 每 workspace/AppHost session active write execution：1。
- 每 thread active Turn：1；pending intent：1；active approval request：1。
- `queued/running/waiting-for-approval/canceling` 均视为 active；terminal 为 `canceled/failed/completed`。
- approval、cancel、start、resume、restart mutation id 均最多 128 UTF-8 bytes并使用稳定字符集。

### Week 73 新增/沿用上限

| 项目 | 上限 |
|---|---:|
| pending prompt/context/catalog | 沿用 Week 72：64 KiB / 32 / 16 |
| execution input snapshot | 256 KiB UI-private metadata envelope；不得含 file bytes |
| timeline item | 沿用 16 KiB persisted record |
| timeline append batch | 32 items / 256 KiB target payload |
| assistant streamed preview chunk | 8 KiB UTF-8，先 redaction 后持久化 |
| safe approval summary | 4 KiB UTF-8 |
| approval target/operation | 各 1 KiB UTF-8 |
| approval decision note | 2 KiB UTF-8；默认不要求 |
| active approval per turn | 1 |
| approval lifetime | 30 minutes，过期后只能 deny/restart |
| notification coalesce | 最多 10 hints/second/thread |
| cancel acknowledgement target | 5 seconds；超时不伪造 canceled |
| final summary | 16 KiB aggregate，timeline preview仍受单 item限制 |
| active write execution | 1/workspace session |

所有上限必须进入 Core/Application constants、contract limits/generated validators 与 tests；Renderer 仅用于提前提示，不能成为唯一 enforcement。

## Protocol、runtime 与 bridge surface

### `desktop-v1` reviewed delta

新增 optional capability：`turn.write-path`。保留现有 methods/notification；新增：

| Method | 类型 | 责任 |
|---|---|---|
| `turn.start` | mutation | 对 pending intent 发出幂等 start request，完成 claim/Turn binding 并启动或保持 queued |
| `turn.cancel` | mutation | 持久化 canceling 并取消当前 active execution |
| `turn.resume` | mutation | 只从经验证的 safe checkpoint 恢复 source turn |
| `turn.restart` | mutation | 显式确认后从 immutable source input 创建新 Turn |
| `approval.resolve` | mutation | 解析当前有效且 identity-bound 的 approval request |

- 不新增 raw stream notification；`thread.changed` 扩展 reason/turn/revision/sequence 仍只是 dirty hint。
- 所有 mutation 包含 workspace/thread/turn/request identity、expected revision 和 client mutation id；strict unknown-member rejection。
- `turn.start` 不接受 prompt、path、context、catalog、model override、tool arguments 或 approval grant。
- `approval.resolve` decision enum 仅 `approve | deny`；无 arbitrary policy、remember、scope 或 token。
- initialize 缺 `turn.write-path` capability 时 UI 只读降级；旧 hash、缺 required fields、unknown status/member fail closed。
- protocol body仍为 1 MiB；timeline content用 `thread.get` pagination，不扩大 frame承载 raw output。

### Main/Preload exact delta

Week 73 bridge 固定为 Week 72 的 21 invoke + 以下 5 invoke，event仍为 2：

```text
turn:start
turn:cancel
turn:resume
turn:restart
approval:resolve
```

最终 exact bridge：26 invoke + 2 event。

- 每个 channel 有独立 command/result validator、timeout metadata 与 current-window sender guard。
- Preload 暴露 frozen nested API，不提供 `invoke(channel, payload)`、raw protocol、generic mutation、tool/shell/terminal或 approval token。
- Main runtime只保存 current generation 的 typed facade；workspace/restart generation变化立即使旧 completion失效。
- `app.cancel` 继续只取消 RPC request，不得与 persisted `turn.cancel` 混淆。

## Application 与执行边界

### Runtime port

建议结构：

```text
TurnExecutionApplicationService
  -> PendingIntentExecutionCoordinator
  -> ITurnExecutionRuntime
       -> production adapter: existing agent/tool/policy path
       -> deterministic fake adapter: tests/E2E
  -> IExecutionEventSink
       -> ThreadStore append + notification hint
  -> IInteractiveApprovalCoordinator
```

- Application contracts只使用 Core/value records，不引用 Electron/React/CLI renderer。
- CLI factory将现有 agent composition下沉为共享 builder/factory；CLI output/exit code保持兼容。
- fake runtime必须与production走同一 Application/store/protocol path，只替换模型/tool结果，不能直接伪造Renderer状态。
- context在消费时重新验证 lexical/final containment、reparse、identity、size/type；catalog/policy/model重新resolve，Week72旧结论不是执行授权。
- catalog Automation reference只作为 runtime capability hint；本周不schedule后台automation。

### Timeline event映射

必须覆盖并有typed projection：

- `user.message`：来自claimed canonical intent的bounded/redacted preview。
- `plan.updated`：计划摘要/阶段，非raw chain-of-thought。
- model状态：通过safe assistant/status item表达，不暴露provider raw payload或hidden reasoning。
- `tool.started/completed`、`command.started/completed`：name、safe target class、success/error/duration；output默认collapsed pointer。
- `approval.requested/resolved`：request identity-safe projection与decision状态。
- `changes.updated`、verification/report/artifact pointer：复用现有Application query和ownership。
- `assistant.message` 与 `turn.completed`：bounded final summary、stop reason、error code。
- cancel/failure/recovery：使用typed warning/terminal payload，不靠自由文本猜状态。

若现有 TimelineItemType/payload不足，应版本化扩展 single source contract；不得把所有新事件塞入 generic string map。

## Renderer state 与交互

### Controller/reducer state

```text
executionByThread[workspaceId/threadId]
  selectedTurnId
  status
  committedSequence
  timelineRequestId/epoch
  unseenItemCount
  approvalSnapshot
  cancelPending
  recoveryAction

composer draft/pending
  enqueue -> authoritative get -> turn.start
```

- enqueue成功且 delivery=`ready` 后，controller用authoritative queue/thread revision调用 `turn.start`；失败保留draft/selection和pending truth。
- delivery=`next-turn` 时显示queued，并只对该pending发出一次幂等start request；当前turn终态后由AppHost收敛启动。
- notification completion必须同时匹配 runtime generation、workspace epoch、thread selection epoch、turn id和sequence；late approval/cancel/start response不得覆盖新thread。
- timeline增量按sequence合并/去重；出现gap、revision倒退、truncation或restart时执行authoritative full/page resync。
- reload后不恢复unsent draft，但必须从AppHost恢复active turn、pending approval、canceling与recoveryRequired状态。

### Task Chat UX

- Composer仍固定可达；有pending/active execution时显示明确发送/排队状态，不允许创建第二个active write Turn。
- running header显示Turn、阶段、effective model/policy与Cancel；waiting状态展示内联approval card和风险摘要。
- timeline对plan/tool/command/verification默认结构化折叠；失败、approval与terminal summary突出显示。
- 自动滚动仅在用户接近底部时启用；用户向上阅读时显示“new updates”计数，不抢焦点。
- approve/deny需明确按钮与request摘要；dangerous target不使用仅颜色区分。approve不得设为默认Enter动作。
- cancel首次点击进入`Canceling…` disabled状态；重复响应由authoritative turn status决定。
- recovery panel区分Resume、Restart、Manual review；restart显示可能重复副作用警告并要求显式确认。
- final summary包含结果、stop reason、changes/report/artifact引用；不伪造未产生的verification或correctness结论。

### Accessibility

- timeline live updates使用polite region且批量播报，不逐token打断；approval request使用可聚焦heading/region，不自动触发approve。
- approve、deny、cancel、resume、restart、new-updates均有稳定accessible name/state/describedby。
- focus规则：approval出现时提供可发现入口但不强抢输入；resolve后回到timeline/Composer；错误进入`role=alert`。
- keyboard：Tab顺序稳定，Escape只关闭辅助UI不resolve approval，Enter不误触dangerous decision，IME期间Composer保持Week72语义。
- long tool name/path/summary、CJK/emoji/RTL、reduced motion和760x560窗口不得溢出或遮挡controls。

## 文件布局

预期新增或主要修改：

```text
src/CSharpAiCli.Core/
  Threads/ThreadContracts.cs
  Threads/ThreadStore.cs
  Threads/ComposerIntentContracts.cs
  Threads/ComposerIntentStore.cs
  Approvals/*

src/CSharpAiCli.Application/
  TurnExecutionApplicationContracts.cs
  TurnExecutionApplicationService.cs
  PendingIntentExecutionCoordinator.cs
  InteractiveApprovalCoordinator.cs
  TimelineExecutionEventSink.cs
  DesktopApplicationSession.cs

src/CSharpAiCli.AppHost/
  Execution/DesktopTurnExecutionSupervisor.cs
  Protocol/DesktopRpcServer.cs
  Protocol/DesktopProtocolMapper.cs
  Protocol/Generated/DesktopProtocolContracts.g.cs

protocol/desktop-v1/
  contract.json
  contract.schema.json
  examples/methods.json

apps/desktop/src/main/
  desktop-requests.ts
  apphost-runtime.ts
  ipc-bridge.ts

apps/desktop/src/preload/bridge.ts

apps/desktop/src/renderer/
  execution-state.ts
  ApprovalCard.tsx
  TurnControls.tsx
  TimelineItem.tsx
  use-desktop-controller.ts
  App.tsx
  styles.css

apps/desktop/e2e/
apps/desktop/scripts/invoke-write-path-smoke.mjs
tools/Invoke-DesktopWritePathSmoke.ps1
docs_md/weekly/73_week_review.md
```

实际命名可按现有模块调整，但 execution supervisor不得进入Renderer/Main，interactive approval不得在Electron本地成为policy truth，fake runtime不得绕过Application/ThreadStore。

## Execution Tasks

### Task 1：冻结起点、write-path contract 与失败语义

- [ ] **Step 1：记录 clean baseline**

记录HEAD/status、SDK/Node/npm/Electron、protocol hash、method/capability、21+2 bridge、.NET/Vitest/E2E counts、Week72 package/process/memory与pending intent证据。

- [ ] **Step 2：先写 lifecycle/protocol proposal tests**

冻结start/claim/create/finalize、approval、cancel、resume/restart的identity/revision/mutation字段、状态矩阵、error category与unknown-member行为。

- [ ] **Step 3：建立write authority guard**

禁止Renderer/Main调用agent/tool/shell/policy/store；禁止Application依赖CLI/Electron；禁止generic run/stream/approval IPC与event-as-truth。

### Task 2：实现 durable intent claim 与幂等 Turn start

- [ ] **Step 1：版本化queue claim record**

定义pending/claimed lifecycle、claim/input hash/deterministic turn binding、receipts与v1 read/upgrade策略。

- [ ] **Step 2：实现atomic StartTurnFromIntent**

一次thread mutation创建Turn、active binding、initial `user.message`、sequence与mutation receipt；覆盖same/different payload retry。

- [ ] **Step 3：实现crash convergence**

覆盖claim-before-turn、turn-before-finalize、missing/corrupt/mismatch两侧状态；证明重启只收敛到同一Turn。

### Task 3：抽出共享 Application execution runtime

- [ ] **Step 1：定义runtime/event/approval ports**

使用结构化Core contracts与CancellationToken，禁止provider/Electron类型泄漏到Application surface。

- [ ] **Step 2：复用现有CLI agent/tool composition**

将必要builder/factory从CliCommandFactory下沉或适配，保留CLI文本、exit code、approval默认与report行为。

- [ ] **Step 3：实现deterministic fake runtime**

支持scripted plan/model/tool/approval/command/verification/changes/final、pause、deny、cancel、failure和safe checkpoint。

### Task 4：实现 pending intent 消费与 start orchestration

- [ ] **Step 1：消费时全量重验**

重新验证workspace/root/context identity、reparse/size/type、catalog revision、effective model/policy与thread/queue revision。

- [ ] **Step 2：实现ready/next-turn语义**

ready立即进入single supervisor；next-turn只在current turn终态且start已请求时启动，不跨thread自动调度。

- [ ] **Step 3：冻结input snapshot与source correlation**

Turn保存bounded immutable input identity/pointer，Renderer无需重发；restart从该source建立新Turn关联。

### Task 5：实现 bounded streaming timeline sink

- [ ] **Step 1：typed event mapping**

映射user/plan/model/tool/command/approval/verification/changes/assistant/final，补齐必要versioned payload而非generic map。

- [ ] **Step 2：batch、redaction与output pointer**

先redact再按32 items/256KiB target append；大输出只持久化safe preview和受控pointer。

- [ ] **Step 3：sequence/dedup/backpressure**

覆盖duplicate/out-of-order event、append conflict、10Hz hint coalesce、output queue pressure和page resync。

### Task 6：实现 interactive approval coordinator

- [ ] **Step 1：定义durable UI-safe request**

绑定workspace/thread/turn/request/policy/action hash/revisions/expiry；不保存可复用grant或Renderer可执行参数。

- [ ] **Step 2：实现approve/deny/expire**

一次性resolve与mutation idempotency；覆盖wrong identity、stale policy、duplicate opposite decision、expired/canceled。

- [ ] **Step 3：接回existing policy/tool path**

Desktop只提供用户decision，risk/policy最终判断仍在Core/Application；CLI保持noninteractive parity。

### Task 7：实现 cancel、resume 与 restart

- [ ] **Step 1：persist-before-cancel**

先transition canceling/append item，再触发linked token，完成后terminalize；重复cancel幂等。

- [ ] **Step 2：串行化approval/cancel race**

同一supervisor中只允许一个revision winner，清理waiter，稳定返回stale/conflict。

- [ ] **Step 3：safe resume/restart contract**

resume要求checkpoint和no-unknown-write证明；restart要求显式确认、重验与新Turn，不能继承approval。

### Task 8：更新 `desktop-v1` 与生成物

- [ ] **Step 1：修改唯一contract source**

增加`turn.write-path`、5 methods、limits/types/status/error字段与thread.changed reason扩展。

- [ ] **Step 2：更新schema/examples/generated**

同步JSON schema、完整成功/失败examples、C#/TS生成物与stale-generation tests。

- [ ] **Step 3：更新hash/handshake negative tests**

新hash exact协商；旧hash、缺capability、unknown member/status、oversize与wrong revision fail closed。

### Task 9：接入 AppHost execution supervisor

- [ ] **Step 1：typed dispatch与lifecycle**

接入start/cancel/resume/restart/approval handlers、single active registry、workspace generation和graceful shutdown。

- [ ] **Step 2：notification coalesce与recovery**

persist后发dirty hint；restart读取active/claim/approval状态，不自动重放write。

- [ ] **Step 3：real-process integration**

真实AppHost完成claim->fake run->approval->final、cancel与restart；断开/重复request后无duplicate Turn或orphan。

### Task 10：扩展 Main、IPC 与 Preload exact surface

- [ ] **Step 1：typed runtime facade**

为5 methods建立generated command/result descriptor、timeouts与generation guard。

- [ ] **Step 2：冻结26+2 bridge**

current-window sender、strict validation、deep freeze和exact inventory tests；继续禁止raw/generic接口。

- [ ] **Step 3：production authority scan**

允许reviewed write symbols，同时继续禁止Node/fs/path/process/terminal/raw tool/approval token与content bytes。

### Task 11：实现 Renderer live execution controller

- [ ] **Step 1：execution reducer/state machine**

按workspace/thread/turn管理status、sequence、approval、cancel、unseen与recovery；reload从AppHost恢复。

- [ ] **Step 2：authoritative timeline resync**

notification只触发thread.get分页；处理gap/dedup/truncation/late completion和workspace/thread/runtime epoch。

- [ ] **Step 3：Composer到turn.start闭环**

enqueue/get确认后发幂等start；next-turn、busy、conflict/error保留明确UI状态且不重复start。

### Task 12：完成 Task Chat、Approval 与 Cancel UX

- [ ] **Step 1：live timeline/final summary**

结构化折叠plan/tool/command/verification，近底自动滚动，离底显示new-updates，terminal summary可复核。

- [ ] **Step 2：approval/cancel controls**

风险摘要、approve/deny、canceling与错误恢复；危险decision不设默认键盘动作。

- [ ] **Step 3：resume/restart/accessibility**

区分safe resume/restart/manual review，显式副作用警告；覆盖focus、live region、keyboard、reduced motion与long content。

### Task 13：扩展 fixture、Playwright 与真实 packaged smoke

- [ ] **Step 1：rich fake runtime fixture**

脚本化approve、deny、cancel、resume、restart、failure与final，状态位于fixture Main/AppHost侧并跨Renderer reload。

- [ ] **Step 2：unpacked/packaged E2E矩阵**

create->compose->run->approval approve/deny->cancel/restart->final，console/unhandled error 0、0 retry/0 skip。

- [ ] **Step 3：real packaged AppHost write smoke**

验证真实协议/store/supervisor，Turn/timeline/approval revisions、no duplicate execution与shutdown orphan 0。

### Task 14：运行 Week 73 全量 Gate 并封板

- [ ] **Step 1：Desktop clean install与verify**

`npm ci`、audit、contracts/notices/typecheck/lint/Vitest/build/security全部通过并记录counts。

- [ ] **Step 2：.NET Release/full suite与CLI parity**

SDK 9.0.308、0 warnings/errors、full suite 0 failed/skipped；CLI exec/approval/cancel/report核心行为回归。

- [ ] **Step 3：package/E2E/process/visual baseline**

startup/window-close/crash/read-only/write-path smoke、四视口、package/app.asar/process/memory增长与orphan delta。

- [ ] **Step 4：创建 `73_week_review.md`**

记录source/protocol/bridge/limits/test count、approval/cancel/recovery证据、首次失败、deferred与Week74输入。

- [ ] **Step 5：状态同步与clean-source acceptance**

全部Critical Gate通过后更新plan/schedule为Passed并提交；从最终clean HEAD重跑CLI release/smoke与Desktop package/write-path smoke，manifest必须`sourceDirty=false` / `releaseAcceptance=true`。

## Verification Matrix

| 层 | 必须证明 | 自动化入口 |
|---|---|---|
| Core claim | crash窗口收敛、deterministic turn、idempotent start/finalize、corrupt mismatch | .NET unit tests |
| Thread store | Turn + user.message atomic、sequence/revision/receipt、single active | Core tests |
| Application | consume-time revalidation、runtime port、CLI parity、no duplicate write | Application tests |
| Approval | identity/policy/action binding、approve/deny/expire/stale/race | Core/Application tests |
| Cancel/recovery | persist-before-cancel、idempotency、timeout truth、safe resume/restart | Application/AppHost tests |
| Timeline | typed mapping、redaction、bounds、dedup/order/backpressure | Application/protocol tests |
| Protocol | exact methods/capability/types/limits/hash/examples | generation + contract tests |
| AppHost | single supervisor、workspace generation、disconnect/shutdown/orphan | real-process integration |
| Main/Preload | exact26+2、sender guard、deep freeze、no generic/raw authority | bridge/security tests |
| Renderer | sequence resync、epochs、start once、approval/cancel/recovery state | reducer/controller/RTL |
| UX/A11y | live timeline、focus、keyboard、polite announcements、long/narrow | RTL + Playwright |
| Security | no raw path/bytes/token/tool args/terminal/policy bypass | production scan |
| Regression | Week71/72 read-only/composer、CLI/.NET、package/process | standard Gates |
| Release | unpacked/packaged real write smoke、visual、clean manifest | smoke scripts |

## 周末 Gate

Week 73 只有同时满足以下条件才可标记 Passed：

1. pending intent到Turn采用durable claim与deterministic binding；所有crash窗口收敛到0或1个匹配Turn，绝无重复执行。
2. Turn创建、active binding、initial `user.message`、sequence与mutation receipt在单一ThreadStore mutation内提交。
3. start/claim/finalize same mutation retry幂等；different payload、wrong revision、mismatch/corrupt均fail closed。
4. 执行前重新验证workspace/root、context identity/reparse/size/type、catalog revision、model与approval policy；Week72 token不是执行授权。
5. Desktop write path复用Core/Application agent/tool/workspace/approval/redaction/report路径；Application无CLI/Electron依赖，AppHost不复制policy。
6. 每workspace session最多1个active write execution、每thread最多1个active Turn/1个pending intent/1个approval；无并发write worker。
7. fake runtime只替换模型/tool结果，仍走真实Application/store/protocol/supervisor，不直接伪造Renderer状态。
8. timeline event全部typed、bounded、redacted、append-only；provider raw payload、hidden reasoning、secret、tool args和full command output不进入Renderer。
9. runtime event只有持久化成功后才可通知；duplicate/out-of-order按correlation/receipt去重，sequence只由ThreadStore分配。
10. `thread.changed`仅dirty hint；Renderer通过`thread.get(afterSequence)`收敛，gap/reload/restart/truncation有authoritative resync。
11. approval request绑定workspace/thread/turn/request/policy/action hash/revisions/expiry；UI只获safe projection。
12. approve/deny一次性消费；duplicate same mutation幂等，opposite/stale/expired/wrong identity/canceled/policy-changed稳定拒绝。
13. approval不跨Turn复用、不记住grant、不由Renderer修改policy；CLI noninteractive行为保持兼容。
14. cancel先持久化`canceling`再触发token；ack后才为`canceled`，超时保持truthful recovery state，不伪造终态。
15. cancel/approval/start竞态只有一个revision winner；重复请求不产生第二个terminal item或执行。
16. resume只从验证checkpoint且无unknown write边界执行；否则明确restart/manual review；restart显式确认、重新验证且创建新Turn。
17. `desktop-v1` contract/schema/examples/generated C#/TS一致，新hash/capability exact协商；旧hash/unknown member fail closed。
18. protocol只新增reviewed5 methods；`turn.start`不接收prompt/path/tool args，`approval.resolve`不接收grant/policy/generic payload。
19. exact bridge为26 invoke + 2 event，sender guard、deep freeze、strict command/result validation通过，无generic IPC/raw protocol。
20. running/approval/cancel/recovery/final UX可达；键盘、focus、screen reader、IME、reduced motion、long content与760x560通过。
21. unpacked与packaged E2E完成approve、deny、cancel、resume/restart和final summary，console/unhandled error 0、0 retry、0 skipped。
22. real packaged AppHost smoke证明真实claim/store/supervisor/revision，无duplicate Turn/timeline/write，shutdown后orphan delta 0。
23. Week71 read-only与Week72 composer/context/catalog E2E、安全scan、absolute path/bytes不泄漏全部不回归。
24. Desktop verify/audit、全部Vitest/Playwright/build/security与.NET SDK 9.0.308 Release/full suite/CLI regression通过，0 failed/skipped。
25. package/app.asar/process/memory相对Week72同口径增长不超过15%，或review给出根因、修复或Blocked决定。
26. `73_week_review.md`、plan、schedule一致，记录首次失败、approval/cancel/recovery限制与Week74稳定输入。
27. final clean-source acceptance绑定最终提交，manifest为`sourceDirty=false` / `releaseAcceptance=true`，最终package/write smoke来自同一clean HEAD。

若Gate 1-6、8-19、21-24或27失败，Week74不得用Renderer-local execution、raw stream、自动approve/replay、kill AppHost当cancel、放宽validator/CSP或跳过packaged E2E绕过。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| queue与thread跨store出现原子性窗口 | durable claim + deterministic turn + idempotent receipt + crash convergence，不做clear-first |
| approval adapter绕过existing policy | policy决定risk/eligibility，Desktop只提供identity-bound user decision；CLI parity tests |
| cancel后tool仍产生副作用 | persist canceling、cooperative token、未知结果保持recovery/manual review，不宣称canceled |
| restart重复已执行write | explicit confirmation、source timeline保留、重新验证、new Turn correlation，不自动重放 |
| raw token/command output压垮protocol | bounded aggregation、redaction、collapsed preview、pointer与thread.get分页 |
| notification丢失/乱序导致UI错误 | event仅dirty hint、sequence gap detection、authoritative resync |
| AppHost crash遗留approval waiter | waiter仅内存绑定active execution；restart将旧request标为stale/recovery-required |
| single supervisor阻塞其它thread | 明确busy/queued，0.6.0不引入并发worker或跨thread scheduler |
| fake runtime掩盖production差异 | fake只实现runtime port；真实store/protocol/AppHost smoke与CLI parity并行验证 |
| Electron bridge扩大为generic write API | exact26+2、per-method validators、production authority scan |
| timeline item类型膨胀/兼容破坏 | versioned typed payload、single contract generation、old projection regression |
| E2E时序flake | deterministic fake barriers而非sleep；首次失败必须记录，retry不计通过 |

按责任回退：claim/store、runtime port、timeline sink、approval、cancel/recovery、protocol、AppHost supervisor、bridge、Renderer state、UX、fixture/smoke分别独立提交。任何回退不得恢复clear-first消费、Renderer重发canonical input、raw provider stream、approval grant缓存、automatic write replay或generic IPC。

## Week 74 输入

Week 73 Gate通过后，Week 74只能依赖以下稳定输入：

- reviewed `desktop-v1` turn/approval methods、generated validators、`turn.write-path` capability、new hash与exact26+2 bridge。
- durable claim到唯一Turn的crash-convergent/idempotent协议，以及immutable execution input/source correlation。
- AppHost single write supervisor、persisted typed timeline、coalesced dirty hint与authoritative sequence resync。
- identity-bound approval request/resolve、persist-before-cancel与safe resume/restart边界。
- fake runtime packaged E2E和真实AppHost write smoke覆盖run/approve/deny/cancel/restart/final，无duplicate write与orphan。
- Renderer live Task Chat、approval/cancel/recovery controls及accessibility/四视口证据。
- Week74 terminal必须建立独立user-terminal identity和process lifecycle，不能复用agent command/approval channel；artifact/Gerber-TIFF accept/reject必须继续走ownership、verification与human decision边界。
