# 第 72 周执行计划：Composer、Catalog 与受控上下文

**Goal:** 在 Week 71 已通过验收的安全 Electron Shell 与只读复核闭环上，交付多行 Composer、线程级临时 draft、Skills/Experts/Automations Catalog、`@file` / `@folder` 受控上下文、当前模型与 approval policy 摘要，以及可审计的下一-turn 排队语义。Renderer 只能提交 bounded intent，文件系统路径、类型、大小、workspace ownership、reparse 与 catalog revision 必须由 AppHost/Application 重新验证；本周不启动 agent、不流式输出、不处理 approval、不修改当前运行 turn 的 tool arguments。

**Architecture:** 扩展 `desktop-v1` reviewed contract，而不是把 Week 71 的 thread/review bridge 变成 generic request。Application/Core 新增 controlled-context resolver 与单个 pending composer intent store；AppHost 是 catalog、workspace、path identity、queue revision 与有效配置摘要的权威。Main 只为 native file/folder picker 持有瞬时绝对路径，并在返回 Renderer 前先交给 AppHost 验证；Preload 仅暴露逐方法 frozen API。Renderer reducer 管理未发送 draft、mention 菜单、validation/queue 状态和 workspace/thread/runtime epoch。`composer.enqueue` 只持久化一个待消费 intent，不创建或执行 Turn；Week 73 再原子消费该 intent、创建 Turn 并进入执行状态。

**Tech Stack:** Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`、TanStack React Virtual `3.14.6`、.NET SDK `9.0.308`、versioned `desktop-v1` framed JSON-RPC over stdio。

---

状态：Passed

创建日期：2026-07-17

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`16ba2be32dc7b6c0a13674000cc32c1779d5fc5a`

执行约束：继续在当前分支原地开发，不创建 worktree 或新分支；保留用户已有改动，不覆盖无关文件。计划创建本身不改变 Week 72 的 Pending 状态，只有全部 Critical Gate 通过后才能改为 Passed。

## 本周目标

1. 完成始终可达的多行 Composer、thread-scoped draft、明确的 validation/enqueue/error/queued 状态，以及 model/approval policy 只读摘要。
2. 通过现有 `catalog.list` Application vertical slice 接入 Skills、Experts、Automations；保留 Project Packs 协议兼容，但不把 Automation 选择误当作 schedule/run。
3. 新增 AppHost-authoritative `@file` / `@folder` search、native pick 与 resolve；Renderer 永远不读取文件 bytes，也不获得绝对路径、文件 URL 或通用文件 API。
4. 对 prompt、context 数量、文件/目录 envelope、catalog revision、thread revision、queue revision 与 workspace identity 建立明确上限和 stale-rejection 语义。
5. 将发送动作落成一个持久化、可恢复、可清除且幂等的 pending composer intent；若 thread 正在运行，该 intent 明确表示“下一 turn”，绝不 patch 当前 turn/tool arguments。
6. 覆盖键盘、焦点恢复、screen reader label、IME composition、长文本、窄窗口、workspace/thread 切换竞态与 AppHost restart/error recovery。
7. 保持 Week 71 thread/timeline/review、Electron security、packaged/unpacked E2E、CLI/.NET 与 package/process baseline 全部不回归。

## 起点与稳定输入

### Week 71 已通过输入

- 起点 HEAD 为 `16ba2be32dc7b6c0a13674000cc32c1779d5fc5a`，Week 71 最终 clean-source acceptance 的 `sourceDirty=false`。
- Main/Preload bridge 已冻结为 exact 14 invoke + 2 event，所有 command/result/event 均 runtime validate，sender 必须来自 current window。
- Main runtime 保存当前 generation 的 canonical workspace snapshot；Renderer reload 可恢复，AppHost restart/crash/protocol failure 会清空且不自动 reopen。
- `thread.changed` 只作为 dirty hint；Renderer 通过 workspace/context/selection epoch、sequence 去重与 authoritative list/get resync 收敛。
- thread list/select/create/rename/archive、revision conflict、14-type timeline、Changes/Reports/Artifacts/managed Preview metadata 已通过 packaged/unpacked E2E。
- Week 71 package baseline：package `463,767,212` bytes、`app.asar` `416,955` bytes、6 processes、working set `408,416,256` bytes、private `241,643,520` bytes、orphan delta 0。

### 当前可复用能力

- `CatalogApplicationService` 与 `DesktopApplicationSession.ListCatalog` 已支持 `skills`、`experts`、`automations`、`project-packs`，每次最多 200 项，并返回 bounded projection 与 diagnostics。
- `desktop-v1` 已声明 `catalog.list`、`CatalogItemData` 与 `localCatalogs` workspace capability，但 Week 71 Desktop bridge 尚未向 Renderer 暴露该方法。
- Workspace snapshot 已安全投影 `modelSource`、`approvalMode` 与 `approvalModeSource`；当前 contract 没有可供 Composer 显示的 effective model identity。
- Core 已有 `WorkspaceGuard`、thread revision/state machine、atomic replace、mutation receipt、bounded record 与 corrupt-state diagnostics，可复用但不得降低现有 reparse/ownership 约束。
- Thread contract 已允许 `queued` status，但当前 `ThreadStore.CreateTurn` 会把 queued Turn 视为 active Turn；本周不得借此提前创建或执行 Turn。

### 当前缺口

- 没有 composer/context/queue protocol、Application use case、AppHost handler、Main facade、IPC/Preload channel 或 Renderer state。
- Catalog projection 没有 workspace-bound revision，无法在发送时证明用户选中的 Skill/Expert/Automation 仍来自同一 catalog snapshot。
- 没有 bounded workspace file/folder search、native picker revalidation、type/size envelope、opaque selection token 或 TOCTOU identity。
- 没有 pending intent store；运行中再次发送既不能安全排队，也不能证明没有修改当前 tool arguments。
- Week 71 production scan仍应拒绝 turn execution、approval resolve、terminal、artifact bytes/open/export/delete 和 generic IPC；Week 72 只能增加 reviewed composer/catalog/context surface。

## 方案选择

### 1. “发送”只排队，不执行

Week 72 的 Send 流程固定为：

```text
Renderer draft
-> catalog/context selection tokens
-> composer.enqueue
-> AppHost/Application 全量重校验
-> atomic pending intent
-> Renderer authoritative composer.get
```

- `composer.enqueue` 不调用 agent/model/tool/shell，不创建 timeline item，不创建 TurnRecord，不改变 active turn。
- 每个 thread 最多一个 pending intent；有 active turn 时标记 `next-turn`，无 active turn 时标记 `ready`，两者都由 Week 73 executor 消费。
- Week 73 必须从 AppHost/Core pending intent 消费输入，Renderer 不得再次提交 prompt/path/tool arguments。
- 清除 pending intent 必须走显式 `composer.clear` + expected queue revision，不允许 UI 本地隐藏即视为删除。

### 2. Draft 与 queued intent 分离

- 未发送 draft 是 Renderer 内存中的 thread-scoped UI state，可在同一 Renderer session 内切换 thread 后恢复。
- draft 不写 localStorage/IndexedDB，不进入 thread timeline，不通过 notification 传播；Renderer reload 会清除未发送 draft。
- queued intent 是 AppHost/Application/Core 权威记录，可跨 Renderer reload 与 Desktop restart 恢复，并受 record schema、revision、workspace root identity 和 corruption diagnostics 保护。
- enqueue 成功后才清空对应 draft；失败、stale、conflict 或 disconnect 必须保留 draft 与已选 chips，供用户修复重试。

### 3. 路径只作为 AppHost 输入，不作为 Renderer 权威

- `@file` / `@folder` search 在 AppHost 内枚举；Renderer 只看到 opaque `selectionId`、workspace-relative display path、kind、bounded size/count 与 availability。
- native picker 的绝对路径只存在于 Electron Main 的局部变量；Main 立即调用 `context.resolve`，只有验证后的 descriptor 才能返回 Preload/Renderer。
- Renderer 不得调用 `context.resolve` 传任意 path，不得获取 `fullPath`、`file://` URL、handle、bytes 或 shell-open 能力。
- enqueue 必须再次按 workspace root identity、reparse chain、kind、size/count 与 observed identity 重验；selection token 不是授权凭证。

### 4. Catalog selection 使用 revision，不信任 UI 缓存

- `catalog.list` result 增加 deterministic `catalogRevision` 与 `workspaceId`；revision 来源于 canonical bounded item identity，而不是时间戳。
- enqueue 只接受 `{kind, id, catalogRevision}`，Application 重新加载 catalog、确认 id 唯一并比较 revision。
- stale/missing/duplicate catalog item 稳定返回 validation/conflict error；不静默选择同名新项。
- Automation 仅作为下一-turn intent 的能力引用；本周不创建 automation run、不保存 schedule、不调用 target。

### 5. Model 与 approval policy 是只读 effective summary

- Composer state 显示 bounded `effectiveModel`、`modelSource`、`approvalMode`、`approvalModeSource`。
- 当前仓库没有权威 model catalog，因此 Week 72 不伪造 model options，也不接受 Renderer arbitrary model override。
- approval policy 不可由 Renderer 修改；Week 73 执行时仍由 Application/Core policy resolver 重新决定，Composer summary 不是 approval grant。

## 范围冻结

### 本周包含

- Application/Core controlled context query/resolve、catalog revision、pending intent enqueue/get/clear 与 atomic revision。
- `desktop-v1` contract/schema/examples/generated C#/TS 的 reviewed delta，以及 contract hash/handshake 更新。
- AppHost handlers、Main runtime typed facade、exact IPC/Preload bridge 与 production security scan。
- Composer textarea、draft reducer、mentions、catalog picker、native file/folder picker、chips、configuration summary 与 queued status。
- deterministic .NET/Vitest/RTL/Playwright tests、packaged/unpacked smoke、visual/accessibility/package/process evidence。

### 本周不包含

- `turn.start`、agent/model execution、streaming、timeline append、approval request/resolve、cancel/resume/restart execution。
- 当前 active turn tool arguments、prompt、context、model 或 approval policy 的修改。
- 任意 filesystem browser、file content preview、drag/drop、clipboard path ingest、open/export/delete、shell/terminal。
- Automation schedule/run、Skill run、Expert execution、Project Pack run 或 remote catalog/network discovery。
- 多个 pending intent、并发 write worker、跨 thread queue ordering、background scheduler。
- Gerber/TIFF bitmap bytes、external tool、accept/reject；Preview 仍保持 Week 71 metadata-only。

## 冻结的边界与上限

以下上限必须进入 Application constants、contract schema、generated validators 与 tests，不能只存在于 Renderer：

| 项目 | Week 72 上限 |
|---|---:|
| prompt | 64 KiB UTF-8，trim 后不得为空 |
| context selections | 32 |
| catalog selections | 16 |
| 单文件 | 10 MiB |
| 全部 file context | 32 MiB |
| 单 folder 可枚举文件 | 500 |
| 单 folder envelope | 64 MiB |
| context search result | 100 |
| search scanned entries | 5,000 |
| search query | 256 UTF-8 bytes |
| relative display path | 4,096 UTF-8 bytes |
| pending intent per thread | 1 |
| queue mutation id | 128 UTF-8 bytes |

受控上下文规则：

1. `.git`、`.caicli`、managed thread/report/artifact stores、package secrets 与 AppHost runtime temp 不得作为 context。
2. path lexical normalization、final target、每个既存 segment 的 reparse status 与 workspace containment 必须全部通过。
3. file 必须是 regular file；folder 必须是 directory，且枚举不得跟随 reparse point。
4. 文本按 bounded UTF-8/binary sniff 分类；图片只接受明确签名的 PNG/JPEG/GIF/WebP。Week 72 不渲染 bytes。
5. resolve 与 enqueue 都读取 before/after identity；size、last-write、kind 或 target 变化均返回 stale，不自动接受新身份。
6. queued record只保存 workspace-relative reference、kind、size/count、observed identity、catalog identity 与 bounded prompt；不保存绝对路径、file bytes、approval grant 或 tool arguments。

## Protocol、runtime 与 bridge surface

### `desktop-v1` reviewed delta

保留现有 methods；新增或扩展：

| Method | 类型 | 责任 |
|---|---|---|
| `catalog.list` | query，已有 | 增加 `workspaceId` / `catalogRevision`，列出 bounded local catalog |
| `context.search` | query | AppHost 内 bounded 搜索 file/folder candidate |
| `context.resolve` | query，Main-only | 验证 native picker path，返回 opaque descriptor |
| `composer.get` | query | 返回 effective summary 与当前 pending intent metadata |
| `composer.enqueue` | mutation | 全量重校验并原子创建 pending intent |
| `composer.clear` | mutation | expected revision 清除 pending intent |

- initialize 新增并 exact 协商 `composer.controlled-context` capability。
- workspace capabilities 新增 `controlledContext`；未协商或 workspace 不支持时 UI 显式 disabled，不尝试 fallback。
- 不新增 notification；single-window 自身 mutation 完成后用 `composer.get` authoritative resync。`thread.changed` 仍只处理 thread truth。
- 所有 request/result 保留 `schemaVersion=1`、strict unknown-member rejection、bounded arrays/strings、workspace/thread/queue revision 与 typed error union。
- contract、JSON schema、examples、generated C#/TS 与 SHA256 必须同一提交生成并通过 stale check。

### Main/Preload exact delta

Week 72 后 bridge 固定为 Week 71 的 14 invoke + 下列 7 invoke，event仍为 2：

```text
catalog:list
context:search
context:pick-file
context:pick-folder
composer:get
composer:enqueue
composer:clear
```

- `context:pick-*` 只能在 Main 调用 Electron dialog；取消返回 typed canceled result，不返回空 path/string。
- Main 私有 `context.resolve` facade 不暴露为 raw IPC；Renderer 不能构造 native path request。
- 每个 channel 有独立 command validator、generated result validator、timeout metadata 与 current-window sender guard。
- bridge object、nested namespaces 与 arrays 均 deep-freeze；无 `invoke(channel, payload)`、无 arbitrary method/channel/path。

## Renderer state 与交互

### Reducer state

```text
drafts[workspaceId/threadId]
  text
  contextSelections[]
  catalogSelections[]
  status: editing | validating | enqueueing | queued | error
  error

composerSnapshot
  workspaceId
  threadId
  queueRevision
  pendingIntent
  effectiveModel/source
  approvalMode/source
```

- 所有 async completion 同时匹配 runtime generation、workspace/context epoch、selection epoch 与 request id。
- workspace change/restart 清空 draft map、menu results、tokens 与 composer snapshot；thread switch 只切换 keyed draft。
- enqueue success 先 authoritative `composer.get`，确认 pending intent identity 后再清空 draft。
- stale response、dialog late completion、catalog late completion 或旧 thread enqueue result不得覆盖当前选择。

### Composer UX

- Composer 固定在中央 timeline 底部，timeline 仍是主要滚动区域；窄窗口下不遮挡 Inspector drawer 或 sidebar controls。
- `Enter` 发送、`Shift+Enter` 换行；`compositionstart` 到 `compositionend` 期间 Enter 不发送。
- `@` menu支持 Files、Folders、Skills、Experts、Automations 分组；Arrow Up/Down、Home/End、Enter、Escape 与 Tab 有确定行为。
- attach buttons使用 native file/folder picker；选择中、验证中、取消、拒绝、stale 与超限有不同状态。
- chip显示相对 display path 或 catalog display name，可在 enqueue 前移除；绝对路径不出现在 DOM、title、aria-label 或错误文本。
- model/approval summary始终可见，但明确标注为 effective configuration，不表现为本周可编辑 selector 或 approval grant。
- thread running/waiting-for-approval 时发送显示 `Queued for next turn`；idle/completed/failed thread显示 `Ready for next turn`。二者都不假装已经执行。
- archived thread、无 workspace、runtime 非 ready、无 selected thread、空 prompt、pending intent已存在时 Send disabled并说明原因。

### Accessibility 与错误恢复

- textarea、attach buttons、mention combobox/listbox/options、chips remove、Send 与 Clear queue 均有稳定 accessible name/state。
- menu open/close、picker cancel、enqueue success/error 后焦点回到触发元素或 Composer；错误摘要使用 `role=alert`，queued status使用非打断 live region。
- long prompt、long relative path、CJK/emoji、RTL片段与单个长 token 不造成横向溢出；respect reduced motion。
- rejection保留 draft，指向具体 selection；workspace/revision/catalog stale 提供 refresh/reselect，不自动替换用户选择。

## 文件布局

预期新增或主要修改：

```text
protocol/desktop-v1/
  contract.json
  contract.schema.json
  examples/

src/CSharpAiCli.Core/
  Threads/ComposerIntentContracts.cs
  Threads/ComposerIntentStore.cs

src/CSharpAiCli.Application/
  ComposerApplicationContracts.cs
  ComposerApplicationService.cs
  ControlledContextApplicationService.cs
  CatalogApplicationService.cs
  DesktopApplicationSession.cs

src/CSharpAiCli.AppHost/Protocol/
  DesktopProtocolMapper.cs
  DesktopRpcServer.cs
  Generated/DesktopProtocolContracts.g.cs

apps/desktop/src/main/
  desktop-requests.ts
  apphost-client.ts
  apphost-runtime.ts
  ipc.ts

apps/desktop/src/preload/
  bridge.ts
  index.ts

apps/desktop/src/renderer/
  Composer.tsx
  MentionMenu.tsx
  composer-state.ts
  use-desktop-controller.ts
  App.tsx
  styles.css

apps/desktop/e2e/
apps/desktop/scripts/check-production-security.mjs
tools/Invoke-DesktopComposerSmoke.ps1
docs_md/weekly/72_week_review.md
```

实际命名可按现有模块边界微调，但不得把 context path resolution 放进 Renderer/Preload，也不得把 pending intent 持久化放进 Electron local state。

## Execution Tasks

### Task 1：冻结起点、协议 delta 与失败语义

- [x] **Step 1：记录 clean baseline**

记录 HEAD/status、SDK/Node/npm/Electron、contract hash、test counts、Week 71 package/process baseline 与已有 bridge inventory。

- [x] **Step 2：先写 protocol proposal tests**

固定 method/capability/type names、全部上限、success/error invariant、workspace/thread/queue revision、stale/corrupt/not-found/denied/limit categories。

- [x] **Step 3：建立 architecture guard**

禁止 Application 依赖 CLI/Electron；禁止 Renderer/Preload 使用 Node/fs/path/process/raw IPC；禁止 Week 72 新 surface调用 turn execution/approval/terminal/artifact bytes。

### Task 2：实现 Core pending composer intent store

- [x] **Step 1：定义 versioned records**

定义 pending intent、context reference、catalog reference、queue revision、mutation receipt 与 bounded validation；不复用 TurnRecord 表示尚未执行的输入。

- [x] **Step 2：实现 atomic get/enqueue/clear**

使用 expected thread revision、expected queue revision 与 client mutation id；覆盖 duplicate mutation、sharing race、partial temp、corrupt/truncated、archive/delete conflict。

- [x] **Step 3：冻结 lifecycle**

rename保留 queue；archive/delete必须先 clear 或稳定拒绝；Renderer/Desktop restart可恢复；不得自动消费或创建 Turn。

### Task 3：实现 controlled context Application service

- [x] **Step 1：实现 bounded search**

按 workspace-relative query搜索 file/folder，拒绝 managed/private roots、reparse、越界与扫描超限，只返回 safe descriptor。

- [x] **Step 2：实现 native path resolve**

复用 `WorkspaceGuard` 后继续检查 reparse chain、regular file/directory、type signature、size/count envelope与before/after identity。

- [x] **Step 3：实现 enqueue-time revalidation**

opaque token只用于关联候选；enqueue从权威 workspace重新解析，覆盖 swapped target、reparse insertion、rename/delete、size/type change与workspace switch。

### Task 4：扩展 Catalog 与 Composer Application vertical slice

- [x] **Step 1：catalog revision**

对 canonical sorted bounded projection生成 deterministic revision；覆盖同名、顺序变化、source change、truncated与diagnostics。

- [x] **Step 2：Composer state query**

返回 effective model/policy safe summary、pending intent metadata、queue revision与capability，不返回 secret/config file path。

- [x] **Step 3：Composer enqueue/clear**

验证 prompt、thread/archive/active state、queue conflict、catalog/context identities，并将 active thread输入标记为 next-turn而非修改 active turn。

### Task 5：更新 `desktop-v1` contract 与生成物

- [x] **Step 1：修改唯一 contract source**

加入 capability、workspace capability、5个 composer/context methods与 catalog revision fields；同步 limits和exact enum。

- [x] **Step 2：更新 schema/examples/generated**

生成 C#/TS，新增 valid/stale/limit/corrupt examples，验证所有 examples双向 runtime validate。

- [x] **Step 3：更新 handshake hash**

AppHost/Main exact协商新 hash与capability；旧 hash、缺 capability、unknown member/method全部fail closed。

### Task 6：接入 AppHost handlers 与真实进程测试

- [x] **Step 1：typed dispatch**

每个 method严格 deserialize/validate/map，workspace未打开或identity不匹配稳定拒绝；stdout仍只包含协议帧。

- [x] **Step 2：生命周期与取消**

search/resolve支持 cancellation/timeout；enqueue/clear mutation完成点明确，disconnect不产生半条queue记录。

- [x] **Step 3：real AppHost integration**

覆盖 catalog -> search/resolve -> enqueue -> get -> clear、restart recovery、active turn next-turn、stale revision与invalid token。

### Task 7：扩展 Main runtime、IPC 与 Preload

- [x] **Step 1：typed facade**

使用 generated validators/metadata，不在 Renderer注入 schema/method/page size；restart/crash清空selection token cache和composer snapshot。

- [x] **Step 2：native picker**

Main dialog只允许单 workspace item；返回前调用 `context.resolve`，取消/窗口销毁/late completion均不泄漏path。

- [x] **Step 3：冻结 exact 21+2 bridge**

新增7个 invoke、保持2个event；逐 channel validator、sender guard、deep freeze和negative security tests全部通过。

### Task 8：建立 Composer reducer 与 controller

- [x] **Step 1：draft/state machine**

实现 keyed draft、selection chips、editing/validating/enqueueing/queued/error与disabled reasons；不使用local persistence。

- [x] **Step 2：epoch/race handling**

覆盖 workspace/thread/runtime切换、late dialog、late catalog、double send、disconnect、reload与retry；stale completion不覆盖current state。

- [x] **Step 3：authoritative queue resync**

启动、thread select、enqueue、clear与reload后调用 `composer.get`；只有匹配的pending identity才能清空draft。

### Task 9：实现 Mention、Catalog 与受控附件 UX

- [x] **Step 1：统一 `@` menu**

支持file/folder远端搜索与skills/experts/automations本地catalog分组，truncated/empty/loading/error可辨识。

- [x] **Step 2：选择与 stale recovery**

catalog保存revision，context保存opaque token；stale时保留prompt并引导refresh/reselect，不静默替换。

- [x] **Step 3：native picker与chips**

file/folder独立按钮，descriptor仅显示relative metadata；拒绝项、超限项与取消不加入draft。

### Task 10：完成 Composer layout、keyboard、IME 与 accessibility

- [x] **Step 1：responsive layout**

在1920x1080、1440x900、1280x720、760x560验证Composer始终可达且不遮挡timeline/sidebar/inspector。

- [x] **Step 2：keyboard/IME/focus**

覆盖Enter/Shift+Enter、composition guard、mention navigation、Escape、picker cancel、enqueue/clear/error focus recovery。

- [x] **Step 3：screen reader与长内容**

验证combobox/listbox/options、chips、live status、error alert、disabled reason、CJK/emoji/long token/reduced motion。

### Task 11：扩展 Playwright fixture 与 packaged E2E

- [x] **Step 1：unpacked rich fixture**

提供catalog、file/folder search、stale/limit、active thread与queue fixture；验证draft -> mentions -> enqueue -> reload -> clear。

- [x] **Step 2：packaged real AppHost**

临时 workspace创建真实Skill/Automation/file/folder，走真实dialog stub边界、catalog revision与queue store；不依赖网络、模型或外部工具。

- [x] **Step 3：竞态与console Gate**

workspace切换时dialog/catalog/enqueue late completion全部被丢弃；console/unhandled error为0，test-owned process与temp终态清理。

### Task 12：加强 production/security 与 smoke evidence

- [x] **Step 1：更新 production authority scan**

允许reviewed Week72 symbols，同时继续禁止raw IPC、Node/fs/path、absolute path DOM、file URL、content bytes、turn/approval/terminal/generic request。

- [x] **Step 2：新增 Composer smoke**

`Invoke-DesktopComposerSmoke.ps1` 串联unpacked/packaged Composer E2E、package inventory、四视口截图与orphan检查。

- [x] **Step 3：baseline Gate**

按Week71同口径记录package/app.asar/process/memory；增长 >15% 必须根因、修复或Blocked，fixture/Playwright/source map仍不得入包。

### Task 13：运行 Week 72 全量 Gate并封板

- [x] **Step 1：Desktop clean install + verify**

运行 `npm ci`、audit、contracts/notices、typecheck、lint、全部Vitest、build与production security；0 skipped。

- [x] **Step 2：.NET Release/full suite + CLI regression**

使用SDK 9.0.308；Release build 0 warnings/errors，full suite 0 failed/skipped，CLI dirty-source release/smoke通过。

- [x] **Step 3：package/E2E/process matrix**

运行package:dir、unpacked/packaged Playwright、startup/window-close/crash-restart、Week71 read-only smoke与Week72 Composer smoke；orphan delta 0。

- [x] **Step 4：创建 `72_week_review.md`**

记录protocol hash/delta、bridge inventory、context limits、catalog/queue semantics、test counts、screenshots、package/process、首次失败、Deferred与Week73输入。

- [x] **Step 5：更新状态并做 clean-source acceptance**

仅全部Gate通过后勾选计划、将schedule改为Week66-72 Passed并提交；最后从clean HEAD运行release acceptance、CLI smoke、package、packaged E2E与Composer smoke，manifest必须 `sourceDirty=false` / `releaseAcceptance=true`。

## Verification Matrix

| 层 | 必须证明 | 自动化入口 |
|---|---|---|
| Core store | one pending、atomic revision、idempotency、corrupt/restart/archive conflict | .NET unit tests |
| Context | containment、reparse、type/size、TOCTOU、bounded search | Application/Core tests |
| Catalog | deterministic revision、stale/missing/duplicate/truncated | Application tests |
| Protocol | exact methods/capability/types/limits/hash/examples | generation + contract tests |
| AppHost | typed dispatch、workspace authority、real-process recovery | AppHost integration |
| Main/IPC | native path remains Main-only、exact 21+2、sender guard | Main/bridge tests |
| Renderer | draft/status/epoch/race/authoritative queue | reducer/controller tests |
| UX | mention/chips/picker/config summary/next-turn semantics | RTL + Playwright |
| Accessibility | keyboard、IME、focus、names/live/error、reduced motion | RTL + E2E |
| Security | no bytes/absolute path/Node/generic/write execution | production scan |
| Regression | Week71 read-only、Desktop verify、.NET full、CLI smoke | standard Gates |
| Release | unpacked/packaged、visual、package/process、clean acceptance | smoke scripts |

## 周末 Gate

Week 72 只有同时满足以下条件才可标记 Passed：

1. `desktop-v1` contract/schema/examples/generated C#/TS一致，new hash exact协商，旧 hash/缺 capability/unknown member fail closed。
2. Week72 protocol只增加reviewed catalog/context/composer surface；无 `turn.start`、approval resolve、terminal、artifact bytes或generic method。
3. pending intent是Core/AppHost唯一权威，每thread最多1个，atomic revision/idempotency/corrupt/restart/archive/delete tests通过。
4. enqueue不创建Turn/timeline、不调用agent/model/tool/shell；active turn的prompt/context/tool arguments byte-stable。
5. Week73可从pending intent消费完整canonical input，不需要Renderer重发path或安全结论。
6. `catalog.list` 返回deterministic workspace-bound revision；stale/missing/duplicate/truncated catalog稳定拒绝。
7. `@file` / `@folder` search与native picker都经AppHost验证；Renderer/Preload从未获得absolute path、file URL、handle或bytes。
8. lexical/final containment、existing-chain reparse、regular kind、type signature、single/total size、folder count与before/after identity全部有negative tests。
9. `.git`、`.caicli`、managed stores、runtime temp、越界、reparse、swapped target、deleted/renamed/oversize candidate均稳定拒绝。
10. exact bridge为21 invoke + 2 event，current-window sender guard、deep freeze、strict command/result validators通过，无raw/generic IPC。
11. model与approval policy只显示effective bounded summary；无arbitrary model override或Renderer approval grant。
12. draft不进入localStorage/IndexedDB；enqueue失败保留draft，成功只在authoritative get确认后清空。
13. running/waiting thread发送明确显示next-turn queue，且不修改当前turn；pending已存在、archived、runtime unavailable等disabled原因可见。
14. keyboard、IME、mention navigation、focus recovery、screen reader labels/live status、long text与reduced motion自动化通过。
15. 1920x1080、1440x900、1280x720、760x560无重叠、横向溢出、空白主视图或不可达Composer controls。
16. unpacked rich fixture与packaged real AppHost E2E都完成draft -> catalog/context -> enqueue -> reload -> clear；console/unhandled error为0。
17. workspace/thread/runtime切换的late dialog/catalog/enqueue completion均被epoch guard丢弃；stale selection不自动升级。
18. fixture、Playwright、test-results、source map、absolute temp/workspace path不进入production package或截图/DOM evidence。
19. Desktop audit/verify、全部Vitest/Playwright/build/security通过，0 skipped；Week71 read-only E2E不回归。
20. .NET SDK 9.0.308 Release 0 warnings/errors、full suite 0 failed/skipped、CLI dirty/clean smoke通过。
21. packaged startup/window-close/crash/read-only/composer smoke全部exit 0；test-owned Desktop/AppHost orphan delta 0。
22. package/app.asar/process/memory相对Week71同口径增长不超过15%，或review有根因、修复或Blocked决定。
23. `72_week_review.md`、plan、schedule状态一致，并记录source、protocol、limits、test count、visual、baseline、首次失败与Week73输入。
24. final clean-source acceptance绑定最终提交，manifest `sourceDirty=false` / `releaseAcceptance=true`，最终package/smoke来自同一clean HEAD。

若 Gate 1-10、13-17、19、21 或24失败，Week73不得通过Renderer-local queue、raw path、放宽validator/CSP、复用generic IPC或跳过packaged E2E绕过。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| queued intent与TurnRecord语义混淆 | 使用独立pending record；Week72绝不创建Turn，Week73原子消费 |
| prompt持久化扩大敏感数据面 | bounded record、既有store权限、无日志/diagnostic回显、无approval material/tool args |
| native dialog泄漏绝对路径 | path只在Main局部变量，AppHost resolve后仅返回relative descriptor |
| reparse/TOCTOU绕过 | lexical/final/segment检查 + before/after identity + enqueue再次重验 |
| folder扫描阻塞或资源膨胀 | scanned/result/count/byte/depth bounds、cancellation、无follow reparse |
| catalog在选择后变化 | deterministic revision + enqueue requery + stable stale error |
| Renderer把selection token当权限 | token只做关联，AppHost按workspace/identity全量重验 |
| active turn被next input污染 | pending store与active Turn完全分离，active record/tool args hash regression |
| draft在错误后丢失 | mutation确认前不清draft；error/stale保留text/chips并恢复焦点 |
| reload/local persistence边界不清 | unsent draft仅Renderer内存；queued intent走AppHost/Core并authoritative get |
| model options没有权威来源 | 本周只显示effective model，不伪造remote/local model catalog |
| Automation选择误触发运行 | 只保存catalog reference，不schedule、不invoke target |
| contract扩展破坏Week71 | generated strict tests + old read-only E2E + handshake/hash negative tests |
| E2E误杀其它Electron/AppHost | owner PID/parent/path跟踪，禁止按进程名bulk kill |

按责任回退：Core queue、context resolver、catalog revision、protocol、AppHost、Main/bridge、Renderer state、Composer UX、Playwright与smoke分别独立提交。任何回退不得恢复generic IPC、Renderer path authority、unvalidated catalog selection、event-as-truth、localStorage prompt或turn execution旁路。

## Week 73 输入

Week 72 Gate通过后，Week 73只能依赖以下稳定输入：

- reviewed `desktop-v1` composer/context/catalog methods、generated validators、exact capability/hash与21+2 bridge。
- AppHost/Core pending intent的one-per-thread、workspace/root identity、queue revision、mutation idempotency与restart recovery。
- enqueue-time revalidated prompt、file/folder context references、catalog references及effective model/approval summary。
- `@` mention/native picker UX、draft error preservation、epoch race guard与authoritative `composer.get` resync。
- active turn与next-turn intent严格分离的测试证据；Week73必须原子消费pending intent后才创建Turn/user.message。
- unpacked rich fixture、packaged real AppHost、四视口、accessibility、package/process/orphan与clean-source evidence。
- Week73不得信任Week72的旧path identity/catalog revision作为执行授权；消费pending intent时必须再次验证workspace、context、catalog和approval policy，然后才能进入turn start/streaming/approval/cancel闭环。
