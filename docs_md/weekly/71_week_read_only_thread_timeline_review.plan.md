# 第 71 周执行计划：只读 Thread、Timeline 与复核面板

**Goal:** 在 Week 70 安全 Electron Shell 上接入现有 `desktop-v1` 的 thread 生命周期与只读查询，使用户能够在受控 workspace 中创建、选择（恢复查看）、重命名、归档和筛选 thread，分页复核结构化 timeline，并按需查看 Changes、Reports、Artifacts 与 managed Gerber/TIFF artifact 状态；Renderer reload 后从 Main/AppHost durable truth 重建一致状态，且不引入 turn 执行、composer、通用 IPC、任意文件访问或新的业务权威。

**Architecture:** 不修改 `protocol/desktop-v1/contract.json`、generated DTO、AppHost/Application/Core 权威或 method 语义。Main 的 `AppHostClient` 增加 strict notification envelope 与 `thread.changed` validator，initialize 显式协商第四项 capability；runtime 保留当前 canonical workspace snapshot，并提供 generated-result-validated thread/review facade。Preload 只新增逐方法 reviewed channel，不暴露 generic request。Renderer 使用 React reducer、请求 epoch、sequence 去重和 query-after-notification resync；`thread.changed` 只触发 authoritative list/get，绝不直接改写 thread truth。Timeline 使用协议的 `afterSequence`/`nextSequence` 分页与虚拟渲染。Playwright 的 unpacked rich fixture 与 packaged real-AppHost smoke 分离，fixture/harness 不进入 production package。

**Tech Stack:** Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`、TanStack React Virtual `3.14.6`、.NET SDK `9.0.308`、exact `desktop-v1` framed JSON-RPC over stdio。

---

状态：Passed

更新时间：2026-07-17

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`0f10eb9df95b89ae2f4e15bc2401a75f6fed26e7`

执行约束：按用户要求继续在当前分支原地开发，不创建 worktree 或新分支；保留用户已有改动，不覆盖无关文件。

依赖版本事实：2026-07-17 从官方 npm registry 核对 `@playwright/test@1.61.1`；`@tanstack/react-virtual@3.14.6` 为 npm current tag。执行时必须由 lockfile、integrity、notices 与 audit 再次确认，不以网页信息替代锁文件证据。

## 本周目标

Week 70 已把 Desktop 固化为 strict transport、single-child runtime、reviewed IPC/Preload 与 responsive shell，但 Renderer 仍只知道 runtime/workspace：Main 未协商 `thread.changed`，client 会把 notification 当成无 id response 并 fail closed；runtime 不保留可供 reload 读取的 workspace snapshot；bridge 没有 thread/review query；中央区、左侧栏和 inspector 仍是 shell placeholder。

Week 71 结束时必须形成可发布验证的 Phase 2 只读复核闭环：打开 workspace 后可列出并选择既有 thread，明确创建/重命名/归档 thread metadata，分页显示全部 14 种 frozen timeline item，Changes/Reports/Artifacts 按需读取，managed Gerber/TIFF artifact 只显示 ownership/verification/availability 结论；notification gap、duplicate、reload、workspace change、runtime crash 和 stale response 都通过 authoritative resync 收敛。Week 71 不启动或恢复 write-capable turn；排期中的“resume”仅指重新选择并读取既有 thread，不等同于 agent/turn resume。

## 起点与稳定输入

### Week 70 已冻结输入

- exact 4-channel bridge：runtime snapshot/restart/status event 与 workspace picker；所有 status/result 经过 runtime validation。
- single-child AppHost runtime：generation isolation、explicit restart、bounded stop、unexpected exit safe projection。
- exact `desktop-v1` / schema `1` / contract SHA256 `0e89e542511ee9a1531db2bda55daa75ed4593257bdf5e9b80a99b065dcd33a4`。
- generated TypeScript 已提供 thread list/get/create/rename/archive/delete、changes、report、artifact params/result validators，以及 `ThreadChangedParams` validator。
- AppHost 已实现 ordered `thread.changed`：仅 negotiated 时在成功 create/rename/archive/delete response 之后发送，`eventSequence` 为当前 AppHost process 单调序列。
- BrowserWindow/session/CSP/navigation/permission/download/webview 安全 Gate、current-window sender guard 与 frozen Preload factory。
- wide/medium/narrow shell、packaged startup/window-close/crash-restart/capture smoke、orphan delta 0 与 Week 70 package/process baseline。
- Week 70 clean acceptance：Desktop 11 files / 57 tests；.NET 1365/1365；package 464,731,162 bytes；`app.asar` 1,380,905 bytes；6 processes；working set 396,378,112 bytes；private 230,088,704 bytes。

### Frozen protocol facts

- `thread.list`：`pageSize` 1..200；返回最多 200 条与 `truncated`，没有 cursor。Week 71 固定请求 200；被截断时显示明确 capped banner，不伪造分页。
- `thread.get`：`afterSequence >= 0`、`timelinePageSize` 1..100；返回 `timeline`、`nextSequence`、`timelineTruncated` 与 `recoveryRequired`。Week 71 固定页长 100。
- Timeline item frozen 为：`user.message`、`assistant.message`、`plan.updated`、`tool.started`、`tool.completed`、`command.started`、`command.completed`、`approval.requested`、`approval.resolved`、`changes.updated`、`report.available`、`artifact.available`、`warning.raised`、`turn.completed`。
- `thread.changed` params 包含 `eventSequence`、workspace/thread/revision、`created|renamed|archived|deleted` 与 UTC timestamp；notification 不包含完整 record，不是 durable truth。
- `changes.get` 只返回 git status/diff stat/changed files/session identity 与 warnings，不含 diff hunks；本周不得声称完成 Monaco/structured diff。
- report get 返回 bounded structured summary/commands/verification/risks/artifact pointers；本周不引入 Markdown renderer。
- artifact get 只返回 metadata/ownership/retention/availability/verification，不返回文件 bytes 或 preview URL。

### 当前差距

- initialize 只请求三项 required capability；strict handshake 要求 negotiated capability 数量精确为三。
- JSON-RPC decoder 只接受 response root；合法 notification 会因缺少 id 被视为 protocol failure。
- `AppHostRuntime` 只有 workspace open，没有 canonical workspace snapshot、thread/review facade 或 domain event subscription。
- Main/Preload 只有 3 invoke + 1 event；没有 read-only surface、argument validators 或 reload snapshot。
- Renderer 没有 workspace/thread normalized state、request epoch、notification sequence、pagination、virtualization或复核 panel query state。
- production scan 当前主动拒绝 Week 71 method name；必须改为“只允许 reviewed Week 71 channel/method”，继续拒绝 generic IPC 与 Week 72/73 surface。
- 现有 smoke 只验证 shell/runtime；没有真实 AppHost thread lifecycle、Renderer reload、long timeline DOM bound 或多 viewport E2E。

## 方案选择

### 采用：现有协议的显式 vertical slice

Main 为每个 generated method 建立 descriptor 与明确 runtime method；IPC/Preload 每个能力一个 reviewed channel。这样可以复用 generated params/result validator、timeout metadata 与 AppHost authority，同时在 source scan 中证明没有 arbitrary method、path 或 process surface。

### 采用：notification hint + authoritative resync

Renderer 不从 `thread.changed` 拼接或修改 ThreadSummary。每个合法 notification 仅标记 workspace/thread dirty，随后重新调用 list/get；duplicate 被去重，gap 触发 full resync，旧 runtime generation/workspace 的 notification 被丢弃。这样 notification 丢失、乱序、reload 或 reconnect 都不会形成第二套 truth。

### 采用：React reducer + request epoch

本周状态规模可由 typed reducer、controller hook 与 domain selectors管理。每次 workspace、selected thread 或 runtime generation 变化都会递增 epoch；late list/get/review result 只有 epoch 与当前 context 精确匹配时才可提交。暂不引入 Zustand/Redux/query framework。

### 采用：协议分页 + `@tanstack/react-virtual`

timeline 每次最多读取 100 条，按 sequence/id 合并、排序、去重；只有用户显式“Load newer items”才继续读取 `nextSequence`。累计数据保留为已验证 projection，但 DOM 由 virtualizer 限制。虚拟化是本周唯一新增 runtime dependency。

### 不采用：扩展或替换 `desktop-v1`

Week 69 contract 已提供本周所需 method/type/limit。为 UI 便利新增 reverse cursor、diff hunks、artifact bytes、preview URL 或 aggregate dashboard method 会破坏冻结输入，且把 Week 74 能力提前塞入本周。

### 不采用：Renderer 事件补丁、localStorage truth 或自动 reopen

不得把 notification 当成完整 mutation，不保存 raw workspace path、thread detail、timeline 或 review result 到 localStorage/IndexedDB。Renderer reload 可读取 Main 当前 runtime 的 in-memory canonical workspace snapshot；AppHost restart 后 snapshot 必须清空并要求用户重新选择 workspace。

## 范围冻结

### 本周必须交付

| 能力 | Week 71 输出 | 权威 |
|---|---|---|
| Notification transport | strict notification envelope、capability negotiation、generated params validation | Main AppHost client |
| Workspace reload | in-memory canonical snapshot、runtime restart clear | Main runtime |
| Thread navigation | list/select/reload/status filter/capped state | AppHost + Renderer projection |
| Thread metadata command | create/rename/archive + expected revision conflict UX | AppHost authority |
| Timeline | 14 types、100-item forward pagination、sequence/id dedupe、virtual DOM | Renderer |
| Resync | subscribe-before-query、event sequence/gap、request epoch、reload | Main event + Renderer controller |
| Changes | read-only status/stat/files/warnings | AppHost `changes.get` |
| Reports | list/detail structured fields | AppHost report methods |
| Artifacts | list/detail ownership/hash/retention/verification | AppHost artifact methods |
| Gerber/TIFF | managed preview artifact metadata、availability/verification disclaimer | Artifact projection |
| E2E | unpacked rich fixture + packaged real AppHost lifecycle/reload | Playwright Electron |
| Evidence | 4 viewport screenshots、long timeline DOM/memory、package/process/orphan | Desktop scripts |

### 本周明确不做

- 不修改 protocol contract/schema/examples/generator output、C# generated code、method/capability/limit 或 AppHost/Application/Core 业务语义。
- 不新增 `turn.start`、agent/model/tool execution、streaming、approval resolve、cancel/resume execution 或 write worker。
- 不实现 composer、draft、attachments、`@file/@folder`、catalog、Skill、Expert、Automation、model/policy selector；这些属于 Week 72。
- 不暴露 `thread.delete`；删除是 destructive operation，虽已存在协议 method，但不在 Week 71 排期交付，后续需独立确认 UX/Gate。
- 不实现 Terminal、artifact open/export/delete、Gerber/TIFF bitmap preview、accept/reject 或外部 Gerbv/ImageMagick execution；这些属于 Week 74。
- 不渲染 raw Markdown/HTML，不引入 Monaco、Shiki、xterm、Radix、Zustand 或 generic data-fetch framework。
- 不允许 Renderer 传入 workspace path、AppHost executable/cwd、method/channel、protocol/capability、arbitrary URL/file path 或 external command。
- 不把 report/artifact relative path 转为可点击 `file:` URL，不调用 `shell.openExternal`，不把 source pointer 当文件 authority。
- 不让 Changes 面板执行 revert/discard/stage/commit，不声称 diff stat 等同完整 diff。
- 不自动加载所有 timeline 页；不因 `timelineTruncated` 进行无界 background loop。
- 不把 E2E fixture/harness、Playwright、test workspace 或 screenshot secret 打入 production `app.asar`。

## 冻结 Week 71 bridge surface

`src/shared/bridge-contract.ts` 继续作为唯一 Renderer API 定义；保留 Week 70 surface并新增以下 exact channel。名称只在该文件出现一次，Main/Preload/Renderer 不复制字符串：

| Channel | Direction | Bridge method | Params authority |
|---|---|---|---|
| `runtime:get-status` | invoke | `getRuntimeStatus()` | none |
| `runtime:restart` | invoke | `restartRuntime()` | none |
| `runtime:status` | event | `onRuntimeStatus()` | Main snapshot |
| `workspace:open` | invoke | `openWorkspace()` | Main dialog |
| `workspace:get-snapshot` | invoke | `getWorkspaceSnapshot()` | Main runtime memory |
| `thread:list` | invoke | `listThreads()` | fixed pageSize 200 |
| `thread:get` | invoke | `getThread({threadId, afterSequence})` | strict command validator；pageSize fixed 100 |
| `thread:create` | invoke | `createThread({title})` | generated params validator after schema injection |
| `thread:rename` | invoke | `renameThread({threadId, expectedRevision, title})` | generated validator |
| `thread:archive` | invoke | `archiveThread({threadId, expectedRevision})` | generated validator |
| `thread:changed` | event | `onThreadChanged()` | generated notification validator |
| `changes:get` | invoke | `getChanges({sessionName?})` | bounded generated validator；no path |
| `report:list` | invoke | `listReports()` | fixed pageSize 50 |
| `report:get` | invoke | `getReport({reportId})` | generated validator |
| `artifact:list` | invoke | `listArtifacts()` | fixed pageSize 50；no arbitrary filter in Week 71 |
| `artifact:get` | invoke | `getArtifact({artifactId})` | generated validator |

约束：

- exact 14 invoke + 2 event channel；禁止 `desktop:request`、`query(method, params)`、raw `ipcRenderer` 或 optional catch-all。
- Preload 对每个 invoke result 使用对应 generated validator；invalid output reject fixed safe error，event invalid 时丢弃并不调用 listener。
- Main 再次校验 IPC sender、argument count、plain object/exact keys 与 generated params/result；schemaVersion、pageSize 和 method string只由 Main 注入。
- thread title、id、revision、cursor、sessionName、reportId、artifactId 都有 strict byte/numeric bound；unknown member 一律拒绝。
- workspace snapshot 仅在当前 runtime generation 内存中存在，成功 workspace.open 时替换，failed/cancel 保留旧 snapshot，runtime restart/stop/crash 时清空。
- mutation result 仍是 AppHost authoritative `ThreadSummaryResult`；Main 不缓存或合成 ThreadSummary。

## Notification、reload 与 resync contract

### Main transport

server message root 是 response 或 notification 的严格互斥 union：

```text
response     = { jsonrpc: "2.0", id, result|error }
notification = { jsonrpc: "2.0", method: "thread.changed", params }
```

- notification root 必须 exact keys、exact method、`isThreadChangedParams(params)`；包含 id/result/error、unknown method 或 unknown member 均 fatal protocol error。
- initialize 请求第四项 `thread.changed` capability；handshake 必须 exact negotiated 4 capabilities 与 exact notification list `["thread.changed"]`。
- response 仍必须匹配 pending id；notification 不查询 pending map、不消耗 request id，可与 response 连续到达。
- AppHost 已保证成功 mutation response 先于 notification；Desktop test 必须保留并证明此顺序。

### Renderer sequence rules

- subscription 必须先建立，再读取 workspace snapshot/list，避免 initial-query race。
- sequence scope 是 `{runtimeReadyEpoch, workspaceId}`。runtime 离开 ready 或 workspaceId 变化时清零 lastEventSequence、selected thread、timeline、review cache。
- `eventSequence <= last`：duplicate/late event，不直接改 state；记录测试可见 counter，忽略 UI patch。
- `eventSequence === last + 1`：接受 hint，更新 last，然后调度 list resync；若命中 selected thread，再调度 get resync。
- `eventSequence > last + 1`：标记 gap，更新 last，执行 full list + selected detail resync；UI 可短暂显示 `Refreshing`，不得显示 raw sequence diagnostic。
- workspaceId 不匹配当前 snapshot：忽略，不切换 workspace。
- deleted hint 命中 selected thread 时仍先 authoritative get/list；thread-not-found 后才清除 selection。
- 同一时刻最多一个 list resync 与一个 selected-thread resync；连续 hint coalesce，完成后若 dirty 再执行一次，不形成 request storm。

### Request epoch

每个 request 记录 `{runtimeEpoch, workspaceId, selectedThreadId, queryEpoch}`。response 到达时任一字段不匹配即丢弃；不得把旧 workspace/thread 的 late success/error 写入新页面。IPC 不提供 UI cancel；Week 71 只做 stale suppression，不调用或暴露 `app.cancel`。

## Renderer state 与 UX

### Thread navigation

- workspace ready 后自动 list 200；排序使用 AppHost 返回顺序，不在 Renderer 重解释持久化语义。
- filter 仅作用于已加载 projection：All、Active（非 archived）、Completed、Failed、Archived；未知状态显示 safe fallback，不隐藏。
- 点击既有 thread 即“恢复查看”：读取 detail/timeline，不启动 turn、不改变 persisted status。
- create/rename title 使用明确表单、byte count、pending state；archive 需要一次明确确认并携带当前 revision。
- revision conflict 显示 stable message并立即 resync list/detail；不自动覆盖或重试 mutation。
- truncated list 显示“Showing first 200 threads”；没有 cursor 时不得用本地切片冒充完整数据。

### Timeline

- initial `afterSequence=0`；按 response `nextSequence` 加载后续页，`timelineTruncated=false` 时结束。
- merge key 同时检查 sequence 与 itemId；同 sequence/different id 或同 id/different payload 视为 client invariant failure，清空并 authoritative reload，不静默选择一个。
- 14 种 item 均有稳定 icon/label/status/time/summary；payload 只呈现 generated bounded fields。
- user/assistant message 保留换行与长词 wrap；plan/tool/command/approval/review reference 使用 collapsed structured card；historical approval 绝不显示可操作 allow/deny。
- warning 与 failed state 使用非颜色提示；redacted item 显示 redacted badge，不尝试恢复原文。
- large output 默认折叠；`<details>` 或 reviewed button 必须可键盘操作并保持 accessible name。
- virtualizer overscan 固定并有测试；2,000-item fixture 的 mounted timeline row 设明确上限（目标 <= 80），不以隐藏 CSS 节点冒充虚拟化。

### Review inspector

- tab：Changes、Reports、Artifacts、Preview；Runtime 退为紧凑 secondary section，不再占唯一 inspector 内容。
- panel 首次打开或 selected thread/context 变化时 lazy query；关闭 drawer 不丢已验证 cache，workspace/runtime change 必须清空。
- Changes 显示 git status、diff stat、changed files、truncated/warnings；不显示 revert/stage/button，不解释为完整 diff。
- Reports 先 list，再按用户选择 get detail；structured summary/commands/verification/risks 使用 bounded list，无 Markdown/HTML injection。
- Artifacts 显示 kind/source/status/size/hash/verification/retention/availability；relativePath 作为 text，不是 link。
- Preview 只筛选 artifact metadata 中 managed、owned、available 且 kind/verification 表示 Gerber/TIFF preview 的条目，显示 ownership/verification/size/hash 和“Preview is not correctness proof”声明。协议没有 bytes/URL，本周不渲染位图、不 open/export、不 accept/reject。

### Responsive/accessibility

- 1440/1920：左侧 + timeline + inspector；1280：inspector 可收起；760：左右 drawer 默认关闭且互斥，中央 timeline 优先。
- 线程列表、timeline 与 inspector 各自滚动；body 继续不滚动，不产生双滚动锁死。
- toolbar、filter、menu、tabs、drawer、load-more、details 全部具备 accessible name、focus-visible 与 keyboard path。
- status/live region 只播报 meaningful transition，不在 timeline page append 时重复播报全部内容。
- reduced motion 禁止 spinner/scroll animation；长 workspace/thread/path/hash 不撑宽布局。

## Playwright 与 fixture 边界

### Unpacked rich fixture

- `apps/desktop/e2e/renderer-fixture.html` 只由 Playwright dev server加载，注入 frozen fake bridge projection；fixture 覆盖 14 item、2,000-item long timeline、truncated/gap/error/missing artifact。
- fixture 数据必须逐值通过 generated validators；invalid fixture 应使测试 setup 失败，不能给 Renderer 任意 shape。
- production Vite entry、build output、package inventory 和 security scan 必须证明不包含 `e2e`、fixture title 或 fake bridge marker。

### Packaged real AppHost

- Playwright Electron 启动正式 package；测试在 Main process 中临时 stub native `dialog.showOpenDialog` 返回 test-owned temp workspace，不增加 production IPC/test env bypass。
- 通过真实 UI + real AppHost 完成 open workspace -> create -> list -> select/get -> rename -> archive -> reload -> state一致。
- package test 不直接写 `.caicli/threads`，不复制 store schema，不使用 Renderer mock truth。
- 每个 test 记录 Desktop/AppHost before/after PID，测试只清理自己启动的 process/temp workspace，orphan delta 0。
- unpacked rich fixture证明 UI projection；packaged real AppHost 证明 transport/authority。两者不能互相替代。

## 文件布局

### 新建

| 文件 | 单一职责 |
|---|---|
| `apps/desktop/src/main/desktop-requests.ts` | generated request descriptors、fixed page sizes、notification descriptor |
| `apps/desktop/src/main/desktop-requests.test.ts` | method/timeout/validator exactness |
| `apps/desktop/src/renderer/desktop-state.ts` | reducer、epoch、sequence、normalized query state |
| `apps/desktop/src/renderer/desktop-state.test.ts` | stale/gap/duplicate/workspace reset deterministic tests |
| `apps/desktop/src/renderer/use-desktop-controller.ts` | bridge orchestration、coalesced resync、lazy review query |
| `apps/desktop/src/renderer/ThreadSidebar.tsx` | list/filter/select/create/rename/archive UI |
| `apps/desktop/src/renderer/ThreadSidebar.test.tsx` | metadata command/keyboard/conflict tests |
| `apps/desktop/src/renderer/TimelineView.tsx` | pagination、virtualizer、load-more、empty/recovery state |
| `apps/desktop/src/renderer/TimelineItem.tsx` | exact 14 item presentation |
| `apps/desktop/src/renderer/TimelineView.test.tsx` | types/dedupe/long DOM/collapse tests |
| `apps/desktop/src/renderer/ReviewInspector.tsx` | Changes/Reports/Artifacts/Preview tabs |
| `apps/desktop/src/renderer/ReviewInspector.test.tsx` | lazy query/metadata/disclaimer/error tests |
| `apps/desktop/playwright.config.ts` | Electron/unpacked projects、timeouts、artifacts |
| `apps/desktop/e2e/renderer-fixture.html` | unpacked fixture entry，仅 E2E |
| `apps/desktop/e2e/read-only-shell.spec.ts` | rich fixture + packaged real AppHost E2E |
| `apps/desktop/e2e/fixtures.ts` | generated-validator-checked bounded fixture factory |
| `tools/Invoke-DesktopReadOnlySmoke.ps1` | build/package/Playwright/orphan/capture composition |

### 修改

| 文件 | 修改职责 |
|---|---|
| `apps/desktop/src/main/apphost-client.ts` | response/notification union、capability negotiation、event |
| `apps/desktop/src/main/apphost-client.test.ts` | adversarial notification/ordering tests |
| `apps/desktop/src/main/apphost-process.test.ts` | real negotiated notification + read-only/mutation lifecycle |
| `apps/desktop/src/main/apphost-runtime.ts` | workspace snapshot、typed thread/review facade、notification projection |
| `apps/desktop/src/main/apphost-runtime.test.ts` | restart clear、workspace epoch、query guard/event generation |
| `apps/desktop/src/main/ipc-bridge.ts` | exact Week 71 handler/event registry |
| `apps/desktop/src/main/ipc-bridge.test.ts` | sender/args/params/result/lifecycle tests |
| `apps/desktop/src/main/index.ts` | event composition、live window publication、E2E remains external |
| `apps/desktop/src/preload/bridge.ts` | exact typed invoke/event factory |
| `apps/desktop/src/preload/bridge.test.ts` | validator/frozen surface/unsubscribe tests |
| `apps/desktop/src/shared/bridge-contract.ts` | channels、command DTO、strict validators、DesktopBridge |
| `apps/desktop/src/renderer/App.tsx` | composition root、workspace/thread/timeline/panel states |
| `apps/desktop/src/renderer/App.test.tsx` | reload/runtime/workspace integration states |
| `apps/desktop/src/renderer/styles.css` | multi-scroll responsive layout、virtual rows、panels |
| `apps/desktop/vite.config.ts` | E2E dev entry isolation、test config |
| `apps/desktop/package.json` / `package-lock.json` | exact virtualizer/Playwright、E2E scripts |
| `apps/desktop/THIRD_PARTY_NOTICES.md` | regenerated dependency inventory |
| `apps/desktop/scripts/generate-notices.mjs` | 仅在新许可证事实需 reviewed allowlist 时修改 |
| `apps/desktop/scripts/package-desktop.mjs` | exclude e2e/Playwright fixture、inventory assertions |
| `apps/desktop/scripts/check-production-security.mjs` | allow reviewed Week 71 surface，继续拒绝 generic/Week72/73 |
| `tools/Measure-DesktopBaseline.ps1` | 增加 long-timeline sample，不改变 Week70同口径 baseline |
| `docs_md/weekly/66_77_week_cli_0_6_desktop_app_schedule.md` | Gate 后标记 Week 71 Passed、Week 72 pending |

### 周末创建

- `docs_md/weekly/71_week_review.md`：记录 bridge surface、notification/resync、timeline/panel coverage、test counts、Playwright screenshots、package/process/long timeline、deviation 与 Week 72 输入。

## Execution Tasks

### Task 1：冻结起点与 dependency delta

**Files:** `package.json`、`package-lock.json`、`THIRD_PARTY_NOTICES.md`、必要时 notice generator

- [x] **Step 1：记录 source、环境与 clean baseline**

```powershell
git status --short --branch
git rev-parse HEAD
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
node --version
npm --version
```

Expected：起点 `0f10eb9df95b89ae2f4e15bc2401a75f6fed26e7`；SDK 9.0.308、Node v22.13.0、npm 11.7.0；保留任何用户改动。

- [x] **Step 2：安装 exact dependencies**

```powershell
cd apps\desktop
npm install --save-exact @tanstack/react-virtual@3.14.6
npm install --save-dev --save-exact @playwright/test@1.61.1
npm run generate:notices
npm audit
cd ..\..
```

Expected：audit 0；新增 runtime dependency 只有 virtualizer，Playwright 为 devDependency；不执行 browser download，Electron E2E 使用锁定 Electron 41.1.0。

- [x] **Step 3：提交 dependency delta**

```powershell
git add apps/desktop/package.json apps/desktop/package-lock.json apps/desktop/THIRD_PARTY_NOTICES.md apps/desktop/scripts/generate-notices.mjs
git commit -m "chore: 锁定第71周桌面复核依赖"
```

### Task 2：严格接收并协商 `thread.changed`

**Files:** apphost client/tests、desktop request descriptor、real process test

- [x] **Step 1：写 notification adversarial tests**

覆盖 exact root、unknown method/member、id+method、missing params、invalid generated params、response/notification 连帧、notification 不影响 pending、response-before-notification、duplicate event sequence 仍交给上层、invalid notification fatal cleanup。

- [x] **Step 2：实现 server message union 与 descriptor**

request descriptor 只从 generated method metadata/validator 建立；notification descriptor引用 `DESKTOP_NOTIFICATIONS.ThreadChangedNotification` 与 `isThreadChangedParams`。initialize 请求并 exact 验证第四 capability/notification。

- [x] **Step 3：真实 AppHost integration**

real process 执行 initialize/open/create/rename/archive，证明每次成功 mutation response 先于 ordered notification；failed/conflict mutation 不发 event；shutdown 清理。

- [x] **Step 4：验证并提交**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
cd apps\desktop
npx vitest run src/main/apphost-client.test.ts src/main/desktop-requests.test.ts src/main/apphost-process.test.ts
cd ..\..
git add apps/desktop/src/main/apphost-client.ts apps/desktop/src/main/apphost-client.test.ts apps/desktop/src/main/desktop-requests.ts apps/desktop/src/main/desktop-requests.test.ts apps/desktop/src/main/apphost-process.test.ts
git commit -m "feat: 协商并验证桌面线程变更通知"
```

### Task 3：扩展 runtime workspace/thread/review facade

- [x] **Step 1：用 controlled fake 写 lifecycle/query tests**

覆盖 workspace snapshot success/failure/cancel/restart clear、not-ready query、all method descriptor/result、notification generation isolation、old client event、concurrent query、stop/restart race。

- [x] **Step 2：实现 in-memory snapshot 与 typed facade**

runtime 只保存 canonical successful `WorkspaceOpenResult.data`；提供固定 page size 的 list/get/create/rename/archive/changes/report/artifact methods。不得引入 store、CLI、React 或 Electron。

- [x] **Step 3：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/main/apphost-runtime.test.ts src/main/desktop-requests.test.ts
npm run typecheck
cd ..\..
git add apps/desktop/src/main/apphost-runtime.ts apps/desktop/src/main/apphost-runtime.test.ts
git commit -m "feat: 提供桌面线程与只读复核 Runtime"
```

### Task 4：冻结 Week 71 bridge、IPC 与 Preload

- [x] **Step 1：写 exact surface/validator failing tests**

断言 14 invoke + 2 event exact channel、strict command keys/bounds、no schema/method/pageSize from Renderer、sender/arg guards、invalid result、unsubscribe、disposed handler、destroyed window 与 event live-window guard。

- [x] **Step 2：实现 shared contract 与 Main registry**

所有 handler 调用 runtime typed method；mutation/query error 只返回 generated Application result或 fixed IPC failure，不记录 title/session/id payload。

- [x] **Step 3：实现 frozen Preload projection**

每个 invoke/result/event 独立 validator；bridge Object.freeze；没有 raw on/invoke/send、generic query、path/file/process/shell。

- [x] **Step 4：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/main/security.test.ts src/main/ipc-bridge.test.ts src/preload/bridge.test.ts
npm run typecheck
cd ..\..
git add apps/desktop/src/shared/bridge-contract.ts apps/desktop/src/main/ipc-bridge.ts apps/desktop/src/main/ipc-bridge.test.ts apps/desktop/src/main/index.ts apps/desktop/src/preload/bridge.ts apps/desktop/src/preload/bridge.test.ts apps/desktop/src/preload/index.ts
git commit -m "feat: 冻结桌面线程复核桥接面"
```

### Task 5：建立 Renderer reducer、epoch 与 resync controller

- [x] **Step 1：写 pure reducer/controller deterministic tests**

覆盖 subscription-before-query、reload snapshot、duplicate/gap/workspace mismatch、coalesced resync、stale list/get/review response、runtime failure/restart clear、mutation conflict refresh；不使用 sleep。

- [x] **Step 2：实现 state/controller**

state 明确区分 runtime、workspace、thread list、selected detail、timeline pages、notification cursor、review queries；每个 async action携带 context epoch。

- [x] **Step 3：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/renderer/desktop-state.test.ts src/renderer/App.test.tsx
cd ..\..
git add apps/desktop/src/renderer/desktop-state.ts apps/desktop/src/renderer/desktop-state.test.ts apps/desktop/src/renderer/use-desktop-controller.ts apps/desktop/src/renderer/App.tsx apps/desktop/src/renderer/App.test.tsx
git commit -m "feat: 管理桌面线程重载与通知重同步"
```

### Task 6：实现 ThreadSidebar metadata lifecycle

- [x] **Step 1：写 list/filter/select/create/rename/archive tests**

覆盖 loading/empty/error/truncated、状态 filter、long title、create cancel/domain failure、rename revision conflict、archive confirmation、keyboard/focus restore、selected thread deleted on resync。

- [x] **Step 2：实现最小 UI**

“Resume”只选择/get既有 thread；create/rename/archive 显式表单和 pending state；不显示 delete、run、send 或 turn resume。

- [x] **Step 3：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/renderer/ThreadSidebar.test.tsx src/renderer/App.test.tsx
npm run lint
cd ..\..
git add apps/desktop/src/renderer/ThreadSidebar.tsx apps/desktop/src/renderer/ThreadSidebar.test.tsx apps/desktop/src/renderer/App.tsx apps/desktop/src/renderer/styles.css
git commit -m "feat: 完成桌面线程导航与元数据操作"
```

### Task 7：实现 Timeline pagination、dedupe 与 virtualization

- [x] **Step 1：创建 validator-checked 14-type fixture**

fixture 含 long text/path/word、redacted、failed、missing reference、2,000 items、page boundary duplicate/conflict；全部通过 generated guards。

- [x] **Step 2：写 timeline failing tests**

覆盖 exact type presentation、afterSequence/nextSequence、load-more、dedupe、invariant reload、recoveryRequired、collapsed payload、keyboard、reduced motion、mounted row <=80。

- [x] **Step 3：实现 virtual timeline**

使用 `@tanstack/react-virtual` dynamic measurement；scroll container稳定，append 不跳回顶部，selected/reload 可恢复到明确初始位置但不持久化 raw content。

- [x] **Step 4：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/renderer/TimelineView.test.tsx
npm run typecheck
cd ..\..
git add apps/desktop/src/renderer/TimelineView.tsx apps/desktop/src/renderer/TimelineItem.tsx apps/desktop/src/renderer/TimelineView.test.tsx apps/desktop/src/renderer/styles.css
git commit -m "feat: 渲染分页虚拟化桌面时间线"
```

### Task 8：实现 Changes、Reports、Artifacts 与 Preview inspector

- [x] **Step 1：写 panel query/presentation tests**

覆盖 lazy load/context change/stale result、empty/error/truncated、changed files、report detail、artifact metadata、missing artifact、unsafe relativePath text-only、managed preview filter/disclaimer。

- [x] **Step 2：实现只读 tabs**

不得加入 stage/revert/open/export/delete/accept/reject；不渲染 Markdown/HTML；Preview无 bytes 时显示 verified metadata/availability，不画假图。

- [x] **Step 3：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/renderer/ReviewInspector.test.tsx src/renderer/App.test.tsx
npm run lint
cd ..\..
git add apps/desktop/src/renderer/ReviewInspector.tsx apps/desktop/src/renderer/ReviewInspector.test.tsx apps/desktop/src/renderer/App.tsx apps/desktop/src/renderer/styles.css
git commit -m "feat: 完成桌面只读结果复核面板"
```

### Task 9：完成 responsive、accessibility 与 reload UI Gate

- [x] **Step 1：补齐 integration tests**

覆盖 1920/1440/1280/760 layout class、drawer mutual exclusion、multi-scroll、long identity、focus restore、live region、screen reader labels、reduced motion、reload snapshot、runtime crash clear。

- [x] **Step 2：调整 App composition/styles**

动态状态不得改变 toolbar尺寸；timeline始终主区；inspector/left drawer overlay不得遮住所有主视图；narrow open一侧时关闭另一侧。

- [x] **Step 3：验证并提交**

```powershell
cd apps\desktop
npx vitest run src/renderer
npm run typecheck
npm run lint
cd ..\..
git add apps/desktop/src/renderer apps/desktop/vite.config.ts
git commit -m "feat: 固化桌面复核视图响应式与无障碍"
```

### Task 10：建立 Playwright unpacked/package E2E

- [x] **Step 1：配置隔离的 fixture project**

Playwright 1.61.1 使用锁定 Electron；unpacked rich fixture通过专用 Vite entry，所有 fixture guard-valid；package ignore e2e。

- [x] **Step 2：实现 real AppHost packaged flow**

test-owned temp workspace、native dialog stub、UI create/select/rename/archive/reload；不直接写 thread store；before/after PID与cleanup。

- [x] **Step 3：多 viewport 与 long timeline assertions**

1280x720、1440x900、1920x1080、760x560 截图；bounding-box无重叠、主要 region 非零、无横向 overflow、DOM row bound、console error/unhandled rejection为0。

- [x] **Step 4：验证并提交**

```powershell
cd apps\desktop
npm run test:e2e:unpacked
npm run package:dir
npm run test:e2e:packaged
cd ..\..
git add apps/desktop/playwright.config.ts apps/desktop/e2e apps/desktop/package.json apps/desktop/package-lock.json apps/desktop/scripts/package-desktop.mjs
git commit -m "test: 覆盖桌面只读复核 Playwright E2E"
```

### Task 11：加强 production architecture 与 smoke evidence

- [x] **Step 1：更新 production scan**

允许 exact Week71 methods/channels；继续拒绝 generic IPC、Node/fs/process/shell、remote URL、Week72 catalog/composer、Week73 turn/approval/cancel。扫描 production executable files，排除 map 但 inventory记录其体积。

- [x] **Step 2：实现 `Invoke-DesktopReadOnlySmoke.ps1`**

组合 unpacked fixture、package、real AppHost E2E、4 viewport capture 与 orphan；只清理 owner PID/temp，不按进程名 bulk kill。

- [x] **Step 3：扩展 baseline**

保留 Week70 3-second baseline，并新增 2,000-item fixture 的 DOM row、renderer working set/private bytes与交互时间；不得把不同口径数据直接比较。

- [x] **Step 4：验证并提交**

```powershell
cd apps\desktop
npm run build
npm run check:production-security
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopReadOnlySmoke.ps1
git add apps/desktop/scripts/check-production-security.mjs tools/Invoke-DesktopReadOnlySmoke.ps1 tools/Measure-DesktopBaseline.ps1
git commit -m "test: 加强桌面只读复核发布 Gate"
```

### Task 12：运行 Week 71 全量 Gate

- [x] **Step 1：Desktop clean install + verify**

使用 Week70 已验证 Electron cache；`npm ci`、audit、contract/notices、typecheck、lint、全部 Vitest、build/security；0 skipped。

- [x] **Step 2：.NET Release/full suite**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

Expected：7 projects、0 warnings/errors；full suite 0 failed/0 skipped。

- [x] **Step 3：CLI dirty-source regression**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -AllowDirtySource
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

- [x] **Step 4：package + E2E/smoke matrix**

```powershell
cd apps\desktop
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -CrashRestart
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopReadOnlySmoke.ps1
```

- [x] **Step 5：package/process/long timeline sampling**

任一 Week70同口径 package/process/memory指标增长 >15% 必须根因、修复或 Blocked；Playwright/fixture不得进入 package。long timeline 使用单独口径记录 DOM row、memory、interaction。

### Task 13：文档、封板与 clean-source acceptance

- [x] **Step 1：创建 `71_week_review.md`**

记录实际完成/Deferred、bridge channels、notification/resync、thread/timeline/panel coverage、依赖/audit/notices、test counts、Playwright截图、package/process/long timeline、首次失败、source与Week72输入。

- [x] **Step 2：范围与 drift scan**

证明 contract/generated无改动；Renderer无 Node/raw IPC/path authority；非 generated Desktop source只出现 reviewed Week71 methods；无 catalog/turn/terminal/preview bytes/delete surface；fixture/e2e未进package。

- [x] **Step 3：Gate 通过后更新状态**

计划全部勾选；总排期改为 Week66-71 Passed、Week72 pending；任一 Critical Gate 失败则保持执行中或标记Blocked。

- [x] **Step 4：提交实现与文档**

```powershell
git add apps/desktop tools docs_md/weekly/71_week_read_only_thread_timeline_review.plan.md docs_md/weekly/71_week_review.md docs_md/weekly/66_77_week_cli_0_6_desktop_app_schedule.md
git commit -m "feat: 完成第71周桌面只读复核"
```

- [x] **Step 5：最终 clean HEAD acceptance**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
cd apps\desktop
npm run package:dir
npm run test:e2e:packaged
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -CrashRestart
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopReadOnlySmoke.ps1
```

Expected：manifest/checksums bind最终 commit，`sourceDirty=false`、`releaseAcceptance=true`、SDK 9.0.308；最终 package来自同一 clean source，全部 orphan delta 0。

## Verification Matrix

| 层 | 必须证明 | 自动化入口 |
|---|---|---|
| Transport | exact response/notification union、capability、validator、ordering | client/process tests |
| Runtime | workspace snapshot、restart clear、typed query、event generation | runtime tests |
| IPC/Preload | exact 14+2、sender/args/result、frozen no generic | IPC/bridge/security tests |
| Resync | subscribe race、duplicate/gap、coalesce、stale epoch、reload | reducer/controller tests |
| Thread UX | list/filter/select/create/rename/archive/conflict | Sidebar/App tests |
| Timeline | 14 types、pagination、dedupe、virtual DOM、recovery | Timeline tests + fixture E2E |
| Review | Changes/Reports/Artifacts/Preview metadata only | Inspector tests |
| Security | no Node/path/file URL/HTML/generic IPC/Week72-74 write surface | production scan |
| E2E | unpacked rich fixture + packaged real AppHost + reload | Playwright projects |
| Visual | 1920/1440/1280/760 no overlap/overflow/blank | screenshots + box assertions |
| Process | startup/close/crash/E2E owner cleanup | smoke scripts |
| Regression | Desktop verify、.NET full、CLI smoke、clean acceptance | standard Gates |

## 周末 Gate

Week 71 只有同时满足以下条件才可标记 Passed：

1. `desktop-v1` contract/schema/examples/generated C#/TS byte-stable；没有新增或重定义 method/capability/limit。
2. Main exact协商 `thread.changed`；notification envelope/params runtime validate，合法 notification 不污染 pending response，invalid notification fail closed。
3. notification只触发 authoritative list/get resync；duplicate/gap/workspace mismatch/runtime generation/reload规则有 deterministic tests，不直接 patch durable truth。
4. bridge 只有 exact 14 invoke + 2 event；无 raw/generic IPC、Node/fs/process/shell、arbitrary method/path/URL/channel。
5. workspace snapshot只在 Main runtime memory；Renderer reload一致，runtime restart/crash后清空且不自动 reopen，不写 localStorage/IndexedDB。
6. thread list/select/create/rename/archive/filter、revision conflict、truncated/empty/error全部可操作；无 delete、turn run/resume、composer。
7. timeline 14 frozen types均可读；100-item pagination、sequence/id invariant、2,000-item virtual DOM bound、collapse/long word/redaction/recovery通过。
8. Changes/Reports/Artifacts只读projection通过；relative path不成为link；无 stage/revert/open/export/delete。
9. Gerber/TIFF Preview tab只显示 managed ownership/verification/availability metadata与非 correctness disclaimer；不渲染伪造位图、不执行外部工具、不 accept/reject。
10. 1920x1080、1440x900、1280x720、760x560 无重叠、横向溢出、空白主视图或不可达 controls；keyboard/label/focus/reduced-motion通过。
11. unpacked rich fixture与packaged real AppHost Playwright都通过；Renderer reload后workspace/thread state与AppHost truth一致，console/unhandled error为0。
12. fixture、Playwright、E2E marker/workspace不进入 production bundle/package；CSP与Week70 Electron security baseline无回退。
13. Desktop clean install/audit/verify、全部 Vitest/Playwright、production build/security 通过；0 skipped。
14. .NET Release 0 warnings/errors、full suite 0 failed/skipped、CLI dirty/clean smoke通过。
15. packaged startup/window-close/crash/read-only E2E全部 exit 0；Desktop/AppHost及test-owned process orphan delta 0。
16. Week70同口径 package/process/memory增长不超过15%，或 review有根因、修复/Blocked；long timeline另有明确口径。
17. `71_week_review.md`、plan、schedule状态一致，并记录 source、依赖、test count、截图、baseline、首次失败与Week72输入。
18. final clean-source acceptance bind最终提交，manifest `sourceDirty=false` / `releaseAcceptance=true`，最终 Desktop package/smoke来自同一 clean HEAD。

若 Gate 1-5、7、10-13 或15失败，Week72不得通过 fake Renderer truth、放宽 validators/CSP、自动 workspace reopen、generic IPC或跳过 packaged E2E绕过。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| notification 与 response混流导致 client误杀 | strict union + real process ordering；notification不访问pending id |
| event丢失/乱序污染Renderer truth | event只作hint；gap full resync；durable list/get覆盖 |
| reload后workspace丢失 | Main runtime保存canonical snapshot；只在同generation恢复，restart清空 |
| stale request覆盖新selection | context epoch + reducer guard；不依赖promise完成顺序 |
| thread list >200无cursor | 显示capped banner；不伪造完整列表；协议扩展留未来评审 |
| timeline只能forward page | 从0按nextSequence显式加载；不实现reverse/current tail假语义 |
| long timeline DOM/memory膨胀 | 100-item page + virtualizer + 2,000-item DOM/memory Gate |
| review panel诱导文件权限扩张 | 只渲染projection text；no file URL/open/export/path IPC |
| Preview排期与协议能力不一致 | 本周仅managed metadata/disclaimer；bytes/accept留Week74 |
| Playwright fixture混入package | dedicated entry + packager ignore + production marker scan/inventory |
| packaged native dialog难自动化 | Playwright Main-process临时stub；不加production test IPC/env bypass |
| E2E误杀其他Electron/AppHost | owner PID/parent/path跟踪；禁止按进程名bulk kill |
| virtualizer/Playwright依赖增大notice/package | exact lock、audit/notices；Playwright dev-only，package inventory证明pruned |

按责任回退：notification transport、runtime facade、bridge、Renderer state、Thread UI、Timeline、Review panels、Playwright、smoke/production scan分别独立提交。任何回退不得恢复 generic IPC、unvalidated result、event-as-truth、localStorage workspace、unsafe CSP或Renderer path authority。

## Week 72 输入

Week 71 Gate 通过后，Week 72 只能依赖以下稳定输入：

- exact reviewed Week71 bridge、strict command/result/event validators与current-window sender guard。
- negotiated `thread.changed` + authoritative resync + workspace/runtime/request epoch语义。
- in-memory reload snapshot、thread list/select/create/rename/archive、revision conflict与restart clear规则。
- paginated/virtualized 14-type timeline projection与read-only historical approval/tool/command cards。
- Changes/Reports/Artifacts/managed Preview metadata tabs及no file/path authority基线。
- unpacked rich fixture、packaged real AppHost Playwright、多viewport/long timeline/orphan evidence。
- Week72 composer/catalog/attachment context必须新增独立 reviewed surface；不得复用thread/review command为generic request，也不得把本周 source pointer/relative path直接当attachment authority。
