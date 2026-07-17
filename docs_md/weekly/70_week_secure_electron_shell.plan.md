# 第 70 周执行计划：Secure Electron Shell

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 把 Week 66 的 Electron spike 固化为可发布验证的安全 Desktop Shell，使 Renderer 只能通过冻结、typed、runtime-validated 的 Preload allowlist 打开 workspace、观察 AppHost 状态并在崩溃后显式重启，同时保持无 Node、无通用 IPC、无任意文件/进程能力。

**Architecture:** 继续使用 `desktop-v1` 与 Week 69 generated TypeScript contract，不修改协议方法、capability 或 AppHost 业务权威。Electron Main 拆分为 AppHost transport、runtime supervisor、IPC registry、安全策略和窗口工厂；Preload 只投影四个 reviewed channel；Renderer 只消费 bridge projection，显示最小三栏 shell、workspace picker 与 runtime failure/restart 状态。

**Tech Stack:** Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、.NET SDK `9.0.308`、`desktop-v1` framed JSON-RPC over stdio。

---

状态：Passed；Task 1-13 与全部周末 Gate 已通过

更新时间：2026-07-17

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`3473edc450f644ff0d89541e33eb032aac1c5ed8`

执行约束：按用户要求直接在当前分支开发，不创建 worktree 或新分支。

## 本周目标

Week 69 已经把 AppHost 固化为 bounded、typed、single-writer 的 `desktop-v1` authority，但 Electron 侧仍是 Week 66 spike：Main 入口同时承担窗口、IPC、AppHost 生命周期和 smoke 分支；AppHost 响应被 TypeScript 泛型强转而没有逐方法 runtime validation；Preload 只有浅层 allowlist；UI 只能显示 workspace open happy path；权限请求、下载、webview、任意开发 URL、IPC sender 与 restart race 尚未形成完整 Gate。

本周结束时，Desktop 必须形成正式安全 Shell：Main 对 AppHost 做严格握手和响应验证，runtime supervisor 串行管理 start/restart/stop，Renderer 能清楚区分 starting/ready/failed/restarting/stopped，workspace picker 的路径只进入 AppHost guard，Preload 不暴露通用 Electron/Node surface，BrowserWindow/session/CSP/navigation policy 有自动化证据，packaged crash/restart/window-close 后 AppHost orphan delta 为 0。

Week 70 只交付 shell 与 lifecycle。Thread list/timeline、Changes/Reports/Artifacts 内容留给 Week 71；composer/catalog context 留给 Week 72；`turn.start`、agent loop、approval、cancel 和 streaming 留给 Week 73。

## 起点与稳定输入

### Week 69 已冻结输入

- Protocol：exact `desktop-v1`，schema version `1`。
- Contract SHA256：`0e89e542511ee9a1531db2bda55daa75ed4593257bdf5e9b80a99b065dcd33a4`。
- Contract surface：16 methods、1 notification、4 capabilities、58 transport types。
- 本周只允许 Main 调用 `app.initialize`、`workspace.open`、`app.shutdown`；不协商 `thread.changed` capability。
- generated TypeScript 已提供 `DESKTOP_METHODS`、`DESKTOP_METHOD_METADATA`、`PROTOCOL_LIMITS`、`isInitializeResult`、`isWorkspaceOpenResult` 与 `isShutdownResult`。
- AppHost lifecycle：initialize 5 秒、query 15 秒、shutdown drain 2 秒；Desktop 端 shutdown 总窗口继续为 2.5 秒。
- AppHost process E2E 已覆盖 read-only/concurrent/cancel/fatal/disconnect/shutdown，stdout 只包含完整 frame。
- Week 69 package：77 files、466,666,142 bytes；`app.asar` 3,315,885 bytes；AppHost 79,563,784 bytes；3 秒 working set 406,433,792 bytes；private bytes 241,147,904 bytes；headless/window-close orphan delta `0 / 0`。

### 现有 Desktop spike

- `src/main/index.ts` 创建 BrowserWindow、注册 3 个 channel、启动/关闭 AppHost 并处理两个 smoke 环境变量。
- `src/main/apphost-client.ts` 已有 spawn、framing、request id、pending timeout、stderr 16 KiB tail 与 graceful shutdown fallback。
- `src/main/security.ts` 已设置 `contextIsolation=true`、`nodeIntegration=false`、`sandbox=true`、`webSecurity=true` 并拒绝新窗口/跨页 navigation。
- `src/preload/index.ts` 暴露 `initialize`、`openWorkspace`、`onRuntimeStatus`，没有暴露 raw `ipcRenderer`。
- Renderer 已有标题栏、左侧 placeholder、workspace open 中央面和 runtime inspector。
- Production CSP、notice drift、package resource path、headless/window-close smoke 与基础性能采样已有脚本。

### 本周必须关闭的差距

- `AppHostClient.request<T>` 接受任意 method string 和泛型 T，未要求 generated result validator。
- frame decoder 未严格验证 Content-Type、fatal UTF-8、JSON-RPC response shape、unknown/duplicate response id 与 EOF partial frame。
- Main 没有单独 runtime supervisor；start/exit/restart/quit 竞态可能污染状态或产生双 AppHost。
- Renderer 通过 `initialize()` 主动取握手结果，订阅状态与首次快照存在竞态；crash 后没有 reviewed restart command。
- IPC handler 未验证 `event.senderFrame` 是否来自当前受控 Renderer。
- `VITE_DEV_SERVER_URL` 可接受任意字符串；packaged/dev renderer target 没有 exact allowlist helper。
- session permission、permission check、download、webview attach 与 drag-navigation 尚无统一 deny policy。
- Preload 对 `ipcRenderer.invoke` 返回值只依赖 TypeScript 类型，没有逐值 runtime validation。
- workspace open domain failure、AppHost start/crash/restart、bridge unavailable、dialog cancel 没有完整 UI 状态测试。
- 右侧 inspector 在窄窗口直接消失，左侧 collapse 按钮不可用；没有可操作的响应式 drawer 语义。

## 方案选择

### 采用：增量固化并按责任拆分

保留现有依赖、打包链、generated contract 和最小 UI，把 `index.ts` 降为 composition root。新增小型 runtime supervisor、IPC registry、window factory 和 preload bridge factory，使 process、security 与 UI state 可以独立测试。该方案变更面最小，并且不会把 Week 71/72 业务提前塞进 Main。

### 不采用：继续扩展单一 `index.ts`

短期文件较少，但 AppHost exit、window destroyed、restart、dialog、sender validation 与 quit 会共享全局变量，难以用 controlled fake 验证竞态，也不利于证明 Main 没有业务权威。

### 不采用：引入 Electron service framework 或 Renderer state library

本周状态规模只需要 React reducer 与明确 class；新增框架会扩大依赖、notice、CSP 和 package surface。Zustand/Radix/Playwright 等依赖在实际出现 Week 71/72 需求时再按排期评审，不为 shell 预埋。

## 范围冻结

### 本周必须交付

| 能力 | Week 70 输出 | 权威 |
|---|---|---|
| Runtime lifecycle | 单 AppHost start/restart/stop、stale generation 抑制、bounded shutdown | Electron Main supervisor |
| Transport validation | strict frame/UTF-8/JSON-RPC/id/generated result validation | Main AppHost client |
| Handshake | exact version/hash/capabilities/security/limits validation | generated contract + Main |
| IPC allowlist | status snapshot、status event、restart、workspace picker | Main/Preload reviewed bridge |
| Sender validation | packaged file URL 与 exact loopback dev URL | Main security policy |
| Browser security | isolated sandbox、permission/download/webview/navigation deny、strict CSP | Main/session/Vite |
| Workspace picker | Main system dialog，路径只发送给 AppHost，Renderer 只见 canonical outcome | Electron Main + AppHost |
| Failure UX | start/crash/protocol/restart failure 的 stable safe 状态和显式重启 | Main projection + Renderer |
| Responsive shell | 可折叠左侧、可切换右侧 inspector、稳定中央 workspace 面 | Renderer |
| Packaged evidence | startup、window-close、crash/restart、orphan、screenshot、package/process sampling | Desktop scripts |

### 本周明确不做

- 不修改 `protocol/desktop-v1/contract.json`、schema、examples、C# generated output 或 method/capability/limit。
- 不在 Main/Preload/Renderer 接入 `thread.list/get/create/rename/archive/delete`、catalog、changes、report 或 artifact method。
- 不协商或消费 `thread.changed`；Week 71 接入时再建立 notification/reconnect/resync UI contract。
- 不新增 `turn.start`、turn cancel、agent loop、model、tool、command、approval、terminal、MCP 或 external tool IPC。
- 不自动重开上一个 workspace，不把 raw selected path 写入 local storage；runtime 重启后要求用户重新选择 workspace。
- 不暴露 `ipcRenderer.send/invoke/on`、`shell.openExternal`、`fs`、`child_process`、`process`、任意 path 或任意 channel 给 Renderer。
- 不接受 Renderer 传入 workspace path、AppHost executable、cwd、URL、protocol version、contract hash、capability 或 security conclusion。
- 不加载 remote content，不允许任意 external link；Week 70 没有产品所需外链，统一 deny。
- 不加入 Playwright；多 viewport packaged E2E 按总排期在 Week 71 建立，本周只冻结 capture evidence 与 Renderer unit contract。
- 不引入 Zustand、Radix、Markdown、Shiki、Monaco 或 xterm；它们在出现对应业务面时单独评审。

## 冻结 bridge contract

`src/shared/bridge-contract.ts` 使用以下唯一 surface；channel 名、状态和 message bound 必须由同文件 validator 自动检查，不在 Main、Preload 或 Renderer 重复字符串：

```ts
export const IPC_CHANNELS = Object.freeze({
  getRuntimeStatus: "runtime:get-status",
  restartRuntime: "runtime:restart",
  openWorkspace: "workspace:open",
  runtimeStatus: "runtime:status",
});

export type RuntimeState =
  | "starting"
  | "ready"
  | "restarting"
  | "stopping"
  | "stopped"
  | "failed";

export type RuntimeCode =
  | "runtime-starting"
  | "runtime-ready"
  | "runtime-restarting"
  | "runtime-stopping"
  | "runtime-stopped"
  | "apphost-start-failed"
  | "apphost-exited"
  | "protocol-invalid"
  | "restart-failed";

export interface RuntimeStatus {
  schemaVersion: 1;
  state: RuntimeState;
  code: RuntimeCode;
  message: string;
  canRestart: boolean;
  protocolVersion: "desktop-v1" | null;
}

export interface DesktopBridge {
  getRuntimeStatus(): Promise<RuntimeStatus>;
  restartRuntime(): Promise<RuntimeStatus>;
  openWorkspace(): Promise<WorkspaceOpenResult | null>;
  onRuntimeStatus(listener: (status: RuntimeStatus) => void): () => void;
}
```

约束：

- `RuntimeStatus.message` 是 fixed safe message，UTF-8 不超过 256 bytes；不包含 stderr、exception、raw path、request payload 或 exit signal text。
- `canRestart=true` 只允许 `failed`；starting/restarting/ready/stopping/stopped 均为 false。
- `ready` 必须携带 `protocolVersion="desktop-v1"`；其他状态必须为 null。
- validator 拒绝 unknown member、unknown state/code、错误 schema、超长/非字符串 message 与不一致组合。
- Renderer 不再调用 `app.initialize`；Main 在窗口外完成 handshake，Renderer 只读取 runtime snapshot。
- `openWorkspace()` 不接受参数；Main dialog selection 直接进入 AppHost `workspace.open`，结果必须通过 generated `isWorkspaceOpenResult`。

状态、code 与 fixed message 映射冻结如下：

| State | Allowed code | Fixed message | canRestart |
|---|---|---|---:|
| `starting` | `runtime-starting` | `Starting AppHost` | false |
| `ready` | `runtime-ready` | `AppHost ready` | false |
| `restarting` | `runtime-restarting` | `Restarting AppHost` | false |
| `stopping` | `runtime-stopping` | `Stopping AppHost` | false |
| `stopped` | `runtime-stopped` | `AppHost stopped` | false |
| `failed` | `apphost-start-failed` | `AppHost failed to start` | true |
| `failed` | `apphost-exited` | `AppHost stopped unexpectedly` | true |
| `failed` | `protocol-invalid` | `AppHost protocol validation failed` | true |
| `failed` | `restart-failed` | `AppHost restart failed` | true |

## Runtime 状态机

```text
stopped -> starting -> ready
                    -> failed
ready   -> failed                 AppHost unexpected exit
failed  -> restarting -> ready    explicit user restart
                      -> failed
ready   -> stopping -> stopped    app quit
failed  -> stopping -> stopped    app quit
```

- 同时最多一个 start/restart/stop promise；重复 restart 返回同一 promise 或 stable busy snapshot，不 spawn 第二个 child。
- 每次 start 分配递增 generation；旧 child 的迟到 exit/error 不得覆盖新 generation 状态。
- unexpected exit 不自动 restart，不自动 reopen workspace，不猜测旧 session 状态。
- restart 先完成旧 child bounded stop/kill，再创建新 client 并重新 exact handshake。
- quit 优先级高于 restart；进入 stopping 后拒绝 workspace 与 restart。
- Main 内部可以保留 bounded stderr tail 用于本地诊断，但只向 Renderer 投影 stable RuntimeCode/message。

## 文件布局

### 新建

| 文件 | 单一职责 |
|---|---|
| `apps/desktop/src/main/apphost-runtime.ts` | Runtime state machine、generation、start/restart/stop serialization |
| `apps/desktop/src/main/apphost-runtime.test.ts` | controlled fake client 的 lifecycle/race/safe status tests |
| `apps/desktop/src/main/ipc-bridge.ts` | reviewed handler 注册、sender guard、dialog -> AppHost mapping |
| `apps/desktop/src/main/ipc-bridge.test.ts` | allowlist、sender、dialog cancel/failure/restart tests |
| `apps/desktop/src/main/window.ts` | BrowserWindow 创建、renderer target、navigation/session policy 组合 |
| `apps/desktop/src/main/window.test.ts` | packaged/dev target、ready/destroyed/single-window tests |
| `apps/desktop/src/preload/bridge.ts` | 可注入 invoke/on adapter 的 frozen bridge factory 与 output validation |
| `apps/desktop/src/preload/bridge.test.ts` | exact channel、invalid output、unsubscribe、deep surface tests |
| `apps/desktop/src/renderer/App.test.tsx` | runtime/workspace/restart/responsive shell interaction tests |
| `apps/desktop/src/renderer/test-setup.ts` | jsdom cleanup 与最小 DOM test setup |

### 修改

| 文件 | 修改职责 |
|---|---|
| `apps/desktop/src/main/index.ts` | 只保留 app bootstrap、single instance、composition 和 smoke mode |
| `apps/desktop/src/main/apphost-client.ts` | strict decoder、response envelope、generated validator、fatal protocol cleanup |
| `apps/desktop/src/main/apphost-client.test.ts` | framing/UTF-8/envelope/id/result adversarial tests |
| `apps/desktop/src/main/apphost-process.test.ts` | real handshake/workspace/crash/restart/cleanup integration |
| `apps/desktop/src/main/security.ts` | web preferences、sender、navigation、permission、download、webview policy |
| `apps/desktop/src/main/security.test.ts` | 每个安全默认值和 deny branch 的自动断言 |
| `apps/desktop/src/main/window-lifecycle.ts` | live-window guard 与 ready/focus/close helpers |
| `apps/desktop/src/main/window-lifecycle.test.ts` | destroyed webContents 与 late status regression |
| `apps/desktop/src/preload/index.ts` | 只注入 adapter 并 expose frozen bridge |
| `apps/desktop/src/shared/bridge-contract.ts` | 上述四 channel、status schema、strict validators |
| `apps/desktop/src/renderer/global.d.ts` | 继续只声明 `window.caicli: DesktopBridge` |
| `apps/desktop/src/renderer/App.tsx` | runtime reducer、workspace picker、failure/restart 与 shell controls |
| `apps/desktop/src/renderer/styles.css` | wide/medium/narrow layout、drawer、focus、overflow、reduced motion |
| `apps/desktop/index.html` | production/dev CSP 保持模板化但冻结完整 directive |
| `apps/desktop/vite.config.ts` | jsdom test project、exact loopback dev CSP 与 test setup |
| `apps/desktop/package.json` | 锁定 Renderer test dependencies，不新增 runtime dependency |
| `apps/desktop/package-lock.json` | exact npm lock |
| `apps/desktop/THIRD_PARTY_NOTICES.md` | generator 更新新增 test dependency notice |
| `apps/desktop/scripts/check-production-security.mjs` | CSP、bundle、bridge、remote URL 和 Node surface scan |
| `tools/Invoke-DesktopSmoke.ps1` | packaged crash/restart 与 screenshot mode、orphan/process checks |
| `docs_md/weekly/66_77_week_cli_0_6_desktop_app_schedule.md` | Gate 通过后标记 Week 70 Passed、Week 71 pending |

### 周末创建

- `docs_md/weekly/70_week_review.md`：记录实际 source、测试计数、security matrix、package/process、screenshots、偏差和 Week 71 输入。

## Execution Tasks

### Task 1：冻结起点与 dependency delta

**Files:**
- Read: `docs_md/weekly/69_week_review.md`
- Read: `apps/desktop/package.json`
- Read: `apps/desktop/package-lock.json`
- Modify: `docs_md/weekly/70_week_secure_electron_shell.plan.md`（仅在执行发现事实偏差时修正）

- [x] **Step 1：记录 source 与环境**

```powershell
git status --short --branch
git rev-parse HEAD
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
node --version
npm --version
```

Expected：HEAD 从 `3473edc450f644ff0d89541e33eb032aac1c5ed8` 开始；SDK `9.0.308`；Node `v22.13.0`；npm `11.7.0`。若工作区已有用户改动，记录并保留，不覆盖。

- [x] **Step 2：安装锁定 Renderer test dependencies**

```powershell
cd apps\desktop
npm install --save-dev --save-exact @testing-library/react@16.3.0 @testing-library/user-event@14.6.1 jsdom@26.1.0
npm run generate:notices
npm audit
cd ..\..
```

Expected：只修改 `package.json`、`package-lock.json`、`THIRD_PARTY_NOTICES.md`；audit 为 0 vulnerability。若 npm 解析出的 engine/license 不兼容，停止并记录 Blocked，不改用浮动版本。

- [x] **Step 3：提交 dependency delta**

```powershell
git add apps/desktop/package.json apps/desktop/package-lock.json apps/desktop/THIRD_PARTY_NOTICES.md
git commit -m "test: 建立桌面 Renderer 测试环境"
```

### Task 2：先冻结 bridge contract 与 validators

**Files:**
- Modify: `apps/desktop/src/shared/bridge-contract.ts`
- Modify: `apps/desktop/src/main/security.test.ts`
- Modify: `apps/desktop/src/main/index.ts`
- Modify: `apps/desktop/src/preload/index.ts`
- Modify: `apps/desktop/src/renderer/App.tsx`
- Modify: `apps/desktop/src/renderer/global.d.ts`

- [x] **Step 1：写失败测试**

测试必须断言 exact 四 channel、RuntimeStatus 组合约束、256-byte message、unknown member rejection，以及 DesktopBridge 不包含 raw invoke/send/on、path、shell、process 或 generic request。

- [x] **Step 2：运行并确认失败原因**

```powershell
cd apps\desktop
npx vitest run src/main/security.test.ts
```

Expected：因当前 3-channel contract 与旧 `RuntimeStatus` shape 失败，不得通过放宽断言修复。

- [x] **Step 3：实现冻结 contract 并迁移现有调用点**

按“冻结 bridge contract”章节逐字实现 types、constants、`isRuntimeStatus` 与 `assertRuntimeStatus`。validator 必须显式检查 keys，不使用 `value as RuntimeStatus` 作为 validation。Main、Preload、Renderer 同步迁移到新方法名和新 RuntimeStatus shape；此阶段只允许薄适配现有生命周期，`restartRuntime` 在 supervisor 完成前返回 fixed `restart-failed` snapshot，不得复制临时 stop/start 状态机。

- [x] **Step 4：运行测试和 typecheck**

```powershell
npx vitest run src/main/security.test.ts
npm run typecheck
cd ..\..
```

Expected：bridge contract tests 与全项目 typecheck 通过；不得提交一个依赖后续 Task 才能编译的 contract，也不得通过 `any`、optional bridge member 或保留 legacy channel 消除错误。

- [x] **Step 5：提交 contract**

```powershell
git add apps/desktop/src/shared/bridge-contract.ts apps/desktop/src/main/security.test.ts apps/desktop/src/main/index.ts apps/desktop/src/preload/index.ts apps/desktop/src/renderer/App.tsx apps/desktop/src/renderer/global.d.ts
git commit -m "feat: 冻结桌面安全桥契约"
```

### Task 3：严格化 AppHost transport client

**Files:**
- Modify: `apps/desktop/src/main/apphost-client.ts`
- Modify: `apps/desktop/src/main/apphost-client.test.ts`
- Modify: `apps/desktop/src/main/apphost-process.test.ts`
- Modify: `apps/desktop/src/main/index.ts`

- [x] **Step 1：补齐 adversarial failing tests**

覆盖：duplicate/missing Content-Length、存在但 unsupported 的 Content-Type、header/body oversize、partial EOF、invalid UTF-8、invalid JSON、non-object root、wrong `jsonrpc`、non-safe/unknown/duplicate id、result+error 同时存在、unknown root member、generated result validator failure、unknown response id、stderr tail bound 和 protocol failure 后 pending 全部 reject。Content-Type 缺失继续兼容当前 AppHost 只写 Content-Length 的 reviewed output。

- [x] **Step 2：运行当前 tests，确认新增 case 失败**

```powershell
cd apps\desktop
npx vitest run src/main/apphost-client.test.ts
```

Expected：invalid UTF-8/envelope/result validator 等新增 case 失败。

- [x] **Step 3：实现 validator-required request**

所有 request 必须接收 generated validator；timeout 从 `DESKTOP_METHOD_METADATA` 与 `PROTOCOL_LIMITS` 解析，调用点不得手写 5,000/15,000/30,000。response 先严格验证 JSON-RPC envelope 和 id，再运行 method-specific result validator；任一步失败视为 fatal protocol error，kill owned AppHost 并 reject 全部 pending。

```ts
interface DesktopRequestDescriptor<T> {
  method: (typeof DESKTOP_METHODS)[keyof typeof DESKTOP_METHODS];
  timeoutClass: "initialize" | "query" | "mutation" | "shutdown";
  isResult: (value: unknown) => value is T;
}

request<T>(descriptor: DesktopRequestDescriptor<T>, parameters: object): Promise<T>;
```

initialize/workspace/shutdown descriptor 分别引用同名 `DESKTOP_METHODS`、`DESKTOP_METHOD_METADATA` 和 generated result validator，不复制 method string、timeout 数值或 result type。

- [x] **Step 4：严格 UTF-8 与 EOF**

使用 `{ fatal: true }` 的 `TextDecoder`，frame decoder 增加 `finish()`；child stdout close 时若仍有 partial header/body，返回 stable protocol-invalid，不把 raw bytes写入 error 或 stderr。

- [x] **Step 5：真实 process integration**

initialize 使用 `isInitializeResult`，workspace.open 使用 `isWorkspaceOpenResult`，shutdown 使用 `isShutdownResult`。握手额外检查 exact version/hash、三项 requested capability、security transport 和 `rendererNodeAccess=false`。

- [x] **Step 6：验证**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
cd apps\desktop
npx vitest run src/main/apphost-client.test.ts src/main/apphost-process.test.ts
cd ..\..
```

Expected：strict unit cases 全通过；real AppHost handshake/workspace/shutdown/crash 通过且 test cleanup 无残留 process/temp。

- [x] **Step 7：提交 transport**

```powershell
git add apps/desktop/src/main/apphost-client.ts apps/desktop/src/main/apphost-client.test.ts apps/desktop/src/main/apphost-process.test.ts apps/desktop/src/main/index.ts
git commit -m "feat: 严格验证桌面 AppHost transport"
```

### Task 4：建立 AppHost runtime supervisor

**Files:**
- Create: `apps/desktop/src/main/apphost-runtime.ts`
- Create: `apps/desktop/src/main/apphost-runtime.test.ts`

- [x] **Step 1：用 controlled fake 写状态机失败测试**

覆盖 start success/failure、unexpected exit、explicit restart、concurrent restart、quit during restart、old generation late exit、workspace while not ready、safe status projection 与 stop fallback。所有 race 使用 deferred Promise/fake client，不使用 sleep。

- [x] **Step 2：运行并确认模块不存在**

```powershell
cd apps\desktop
npx vitest run src/main/apphost-runtime.test.ts
```

Expected：FAIL，原因是 `apphost-runtime.ts` 尚不存在。

- [x] **Step 3：最小实现状态机**

实现本计划的 Runtime 状态机、single operation promise、generation check、subscriber snapshot 与 `openWorkspace()` ready guard。runtime factory 只创建 `AppHostClient`，不导入 Electron、React、CLI 或 store。

- [x] **Step 4：验证 deterministic race tests**

```powershell
npx vitest run src/main/apphost-runtime.test.ts
cd ..\..
```

Expected：所有状态转换与竞态 case 通过；无 fake timer 未清理、unhandled rejection 或 orphan child。

- [x] **Step 5：提交 supervisor**

```powershell
git add apps/desktop/src/main/apphost-runtime.ts apps/desktop/src/main/apphost-runtime.test.ts
git commit -m "feat: 管理桌面 AppHost runtime 生命周期"
```

### Task 5：封闭 BrowserWindow、session 与 renderer target

**Files:**
- Modify: `apps/desktop/src/main/security.ts`
- Modify: `apps/desktop/src/main/security.test.ts`
- Create: `apps/desktop/src/main/window.ts`
- Create: `apps/desktop/src/main/window.test.ts`
- Modify: `apps/desktop/src/main/window-lifecycle.ts`
- Modify: `apps/desktop/src/main/window-lifecycle.test.ts`

- [x] **Step 1：写安全策略失败测试**

断言：`contextIsolation=true`、`nodeIntegration=false`、`sandbox=true`、`webSecurity=true`、`allowRunningInsecureContent=false`、`webviewTag=false`、`navigateOnDragDrop=false`；packaged `devTools=false`、development `devTools=true`；permission request/check deny；download cancel；webview attach preventDefault；new window deny；navigation 只允许当前 document reload；dev URL 只允许 exact `http://127.0.0.1:5173/`；packaged 只允许 exact renderer `file:` URL。

- [x] **Step 2：运行并确认缺口**

```powershell
cd apps\desktop
npx vitest run src/main/security.test.ts src/main/window.test.ts src/main/window-lifecycle.test.ts
```

Expected：permission/download/webview/dev target tests 失败或 window module 不存在。

- [x] **Step 3：实现纯 policy helpers 与 window factory**

安全 helper 接收最小接口，便于无 Electron GUI unit test。`window.ts` 负责 1440x900 default、760x560 minimum、menu removal、packaged devTools deny、ready-to-show、destroyed guard、target load 和 policy application；不注册 IPC、不启动 AppHost。

- [x] **Step 4：验证**

```powershell
npx vitest run src/main/security.test.ts src/main/window.test.ts src/main/window-lifecycle.test.ts
cd ..\..
```

Expected：所有 allow/deny branch 通过；late AppHost exit 对 destroyed BrowserWindow 不抛错。

- [x] **Step 5：提交 security/window**

```powershell
git add apps/desktop/src/main/security.ts apps/desktop/src/main/security.test.ts apps/desktop/src/main/window.ts apps/desktop/src/main/window.test.ts apps/desktop/src/main/window-lifecycle.ts apps/desktop/src/main/window-lifecycle.test.ts
git commit -m "feat: 固化 Electron 窗口安全边界"
```

### Task 6：建立 reviewed IPC registry

**Files:**
- Create: `apps/desktop/src/main/ipc-bridge.ts`
- Create: `apps/desktop/src/main/ipc-bridge.test.ts`

- [x] **Step 1：写 handler failing tests**

覆盖 exact 3 invoke handler + 1 event channel、invalid sender、unexpected argument、runtime not ready、dialog cancel、多选/空选防御、workspace domain failure、restart success/failure、disposed handler 和 destroyed window。测试中的 path sentinel 必须证明它只传给 fake runtime，不被写入 status/message/log。

- [x] **Step 2：运行并确认模块不存在**

```powershell
cd apps\desktop
npx vitest run src/main/ipc-bridge.test.ts
```

- [x] **Step 3：实现 registry**

`registerDesktopIpc` 只依赖 reviewed runtime、dialog adapter、live window getter 与 sender guard。所有 invoke method 必须拒绝多余参数；workspace path 来自 Main dialog；返回值分别通过 `isRuntimeStatus` 或 generated `isWorkspaceOpenResult` 后才进入 IPC serialization。

- [x] **Step 4：验证与提交**

```powershell
npx vitest run src/main/ipc-bridge.test.ts
cd ..\..
git add apps/desktop/src/main/ipc-bridge.ts apps/desktop/src/main/ipc-bridge.test.ts
git commit -m "feat: 注册桌面 reviewed IPC allowlist"
```

### Task 7：建立 frozen Preload bridge

**Files:**
- Create: `apps/desktop/src/preload/bridge.ts`
- Create: `apps/desktop/src/preload/bridge.test.ts`
- Modify: `apps/desktop/src/preload/index.ts`

- [x] **Step 1：写 pure adapter failing tests**

断言只调用 exact channel；invoke result 必须分别通过 `isRuntimeStatus`/`isWorkspaceOpenResult`；invalid/unknown result reject fixed error；status event invalid 时丢弃；unsubscribe 只移除自己的 wrapped listener；returned bridge frozen 且没有 generic IPC function。

- [x] **Step 2：实现 factory 并验证**

```powershell
cd apps\desktop
npx vitest run src/preload/bridge.test.ts
npm run typecheck
cd ..\..
```

Expected：bridge tests 与 typecheck 通过；`preload/index.ts` 只负责把 `ipcRenderer.invoke/on/removeListener` adapter 注入 factory，并 `contextBridge.exposeInMainWorld("caicli", bridge)`。

- [x] **Step 3：提交 Preload**

```powershell
git add apps/desktop/src/preload/bridge.ts apps/desktop/src/preload/bridge.test.ts apps/desktop/src/preload/index.ts
git commit -m "feat: 验证并冻结桌面 Preload bridge"
```

### Task 8：实现 Runtime 与 workspace Shell 状态

**Files:**
- Create: `apps/desktop/src/renderer/App.test.tsx`
- Create: `apps/desktop/src/renderer/test-setup.ts`
- Modify: `apps/desktop/src/renderer/App.tsx`
- Modify: `apps/desktop/src/renderer/styles.css`
- Modify: `apps/desktop/vite.config.ts`

- [x] **Step 1：配置 jsdom 与 Testing Library**

`vite.config.ts` 继续让 Main/Preload tests 使用 node；`App.test.tsx` 使用 jsdom 和 `test-setup.ts` cleanup。不得把 jsdom 打入 production bundle。

- [x] **Step 2：写 Renderer failing tests**

覆盖：首次 snapshot 与 event race、starting/ready/failed/restarting/stopped、bridge unavailable、open dialog cancel、open pending、workspace success/domain failure、unexpected rejection、crash 清空 workspace、restart button single-flight、focus return、left collapse、right inspector toggle 和 accessible labels。不得 mock thread/timeline/business data。

- [x] **Step 3：运行并确认旧 UI 失败**

```powershell
cd apps\desktop
npx vitest run src/renderer/App.test.tsx
```

- [x] **Step 4：实现最小 Shell**

Renderer 先订阅 status，再调用 `getRuntimeStatus()`，以较新的事件状态为准；所有 action 由 local reducer 串行。failed 状态显示 stable message 与带 `RefreshCw` 图标的 Restart command；workspace open 只显示 canonical result；runtime 离开 ready 立即清空 workspace projection。

- [x] **Step 5：完成响应式布局**

宽屏三栏；中等窗口右侧 inspector 由 toolbar icon 打开 drawer；760-899px 左右两侧均为互斥 drawer，中央 workspace 面不被遮挡。所有 toolbar/icon button 有 tooltip 或 accessible name；长 workspace path 使用 ellipsis/overflow-wrap；动态状态不得改变 toolbar/tile 尺寸；reduced motion 禁止 spinner animation。

- [x] **Step 6：验证 UI tests**

```powershell
npx vitest run src/renderer/App.test.tsx
npm run typecheck
npm run lint
cd ..\..
```

Expected：所有状态与 interaction tests 通过，无 React act warning、console error 或未清理 listener。

- [x] **Step 7：提交 Renderer shell**

```powershell
git add apps/desktop/src/renderer/App.test.tsx apps/desktop/src/renderer/test-setup.ts apps/desktop/src/renderer/App.tsx apps/desktop/src/renderer/styles.css apps/desktop/vite.config.ts
git commit -m "feat: 完成桌面 Runtime 与 workspace Shell"
```

### Task 9：将 Main 降为 composition root

**Files:**
- Modify: `apps/desktop/src/main/index.ts`
- Modify: `apps/desktop/src/main/apphost-process.test.ts`

- [x] **Step 1：写 integration failure/crash/restart tests**

真实 AppHost case 覆盖 app ready、workspace open、forced child termination -> failed snapshot、explicit restart -> ready、final shutdown。记录 before/after owned process delta；不得通过 auto restart 掩盖 failed 状态。

- [x] **Step 2：组合 runtime/window/IPC**

`index.ts` 只执行 single-instance lock、app ready、window factory、policy installation、runtime creation、IPC registration、before-quit bounded stop 和 reviewed smoke modes。第二实例只 restore/focus live window，不启动第二 AppHost。

- [x] **Step 3：处理 quit/restart races**

`before-quit` 只阻止一次；stop 完成或 2.5 秒 deadline 到达后退出。window-all-closed、AppHost exit、ready-to-show 与 smoke timer 全部使用 live-window guard；不得向 destroyed webContents 发送状态。

- [x] **Step 4：验证**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
cd apps\desktop
npx vitest run src/main/apphost-process.test.ts src/main/apphost-runtime.test.ts src/main/ipc-bridge.test.ts
cd ..\..
```

- [x] **Step 5：提交 composition**

```powershell
git add apps/desktop/src/main/index.ts apps/desktop/src/main/apphost-process.test.ts
git commit -m "feat: 组合安全 Electron Main 生命周期"
```

### Task 10：加强 production security 与 architecture checks

**Files:**
- Modify: `apps/desktop/index.html`
- Modify: `apps/desktop/vite.config.ts`
- Modify: `apps/desktop/scripts/check-production-security.mjs`
- Modify: `apps/desktop/src/main/security.test.ts`

- [x] **Step 1：补齐 CSP/bundle failing checks**

Production HTML 必须包含 `default-src 'self'`、`script-src 'self'`、`style-src 'self'`、`connect-src 'self'`、`img-src 'self' data:`、`object-src 'none'`、`base-uri 'none'`、`form-action 'none'`、`frame-ancestors 'none'`，并拒绝 remote scheme、localhost、WebSocket、`unsafe-eval`、`unsafe-inline`。

Renderer bundle scan 必须拒绝 Node built-in、`require(`、`process.`、`ipcRenderer`、`shell.openExternal`、通用 channel 和 Week 71/72/73 method 名；Preload bundle 只允许四个 reviewed channel。

- [x] **Step 2：验证 dev target 不削弱 production**

开发态只允许 `http://127.0.0.1:5173/` 和 `ws://127.0.0.1:5173`；production build 不得残留这些字符串。任意 `VITE_DEV_SERVER_URL=https://...`、localhost alias、额外 path/port 必须 fail closed。

- [x] **Step 3：运行 production build checks**

```powershell
cd apps\desktop
npm run build
npm run check:production-security
npm run check:contracts
npm run check:notices
cd ..\..
```

Expected：production CSP/bundle/contract/notice checks 全部通过；Renderer source map 不被 security scan 当作 production-executable surface，但 package inventory必须记录其体积。

- [x] **Step 4：提交 checks**

```powershell
git add apps/desktop/index.html apps/desktop/vite.config.ts apps/desktop/scripts/check-production-security.mjs apps/desktop/src/main/security.test.ts
git commit -m "test: 加强桌面 production security Gate"
```

### Task 11：扩展 packaged smoke 与视觉证据

**Files:**
- Modify: `tools/Invoke-DesktopSmoke.ps1`
- Modify: `apps/desktop/src/main/index.ts`（只允许 reviewed smoke mode composition）

- [x] **Step 1：增加 `-CrashRestart` smoke**

packaged Main 在内部启动 AppHost、确认 ready、强制终止当前 owned child、确认 failed、显式 restart、再次 exact handshake、graceful stop 后 exit 0。每个阶段有固定 10 秒上限；stderr 只输出 stable stage code。

- [x] **Step 2：增加 `-CaptureShell` evidence**

分别以 1440x900、1120x720、760x560 启动真实 packaged shell并使用 `webContents.capturePage()` 输出 PNG 到 `artifacts/week70-desktop-shell/`。PowerShell 校验尺寸、非零文件与非单色像素；人工用 image viewer 复核无重叠、空白主视图或文字越界。截图不得包含 raw workspace path、secret 或 test fixture payload。

- [x] **Step 3：所有 smoke 记录 orphan delta**

headless、window-close、crash-restart、capture 每条路径都记录 Desktop/AppHost before/after PID；delta 必须为 0。只终止本次 smoke owner 启动且 parent/path 匹配 package 的 process，不按进程名批量 kill。

- [x] **Step 4：提交 smoke harness**

```powershell
git add tools/Invoke-DesktopSmoke.ps1 apps/desktop/src/main/index.ts
git commit -m "test: 覆盖桌面 crash restart 与 Shell 视觉 smoke"
```

### Task 12：运行 Week 70 全量 Gate

**Files:**
- Verify only

- [x] **Step 1：Desktop clean install 与 verify**

```powershell
cd apps\desktop
npm ci
npm audit
npm run verify
cd ..\..
```

Expected：contract/notice drift、typecheck、ESLint、全部 Vitest、Main/Preload/Renderer build 与 production security 通过；记录 test files/tests/耗时，0 tests 不算通过。

- [x] **Step 2：.NET Release 与 full suite**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

Expected：7 projects、0 warnings/0 errors；标准并发 full suite 0 failed/0 skipped。若首次失败，保留失败证据并按 systematic debugging 查根因，不以重跑替代修复。

- [x] **Step 3：CLI dirty-source regression smoke**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -AllowDirtySource
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

- [x] **Step 4：Desktop package**

```powershell
cd apps\desktop
npm run package:dir
cd ..\..
```

Expected：AppHost resource 位于 `resources/apphost`；package 不包含 `.electron-cache`、源码、scripts、完整 node_modules 或 dev test dependencies。

- [x] **Step 5：Packaged smoke matrix**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -CrashRestart
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -CaptureShell
```

Expected：四条路径 exit 0；每条 Desktop/AppHost orphan delta 0；crash 后先观察 failed 再 restart；三张截图可读且无布局错误。

- [x] **Step 6：同口径 package/process sampling**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Measure-DesktopBaseline.ps1
```

记录 package files、unpacked bytes、`app.asar`、Desktop/AppHost executable、3 秒 process count/working set/private bytes 与 lifecycle。相对 Week 69 任一同口径指标增长超过 15% 时，必须定位 payload/process 原因并修复或明确 Blocked。

### Task 13：文档、提交与 clean-source acceptance

**Files:**
- Create: `docs_md/weekly/70_week_review.md`
- Modify: `docs_md/weekly/70_week_secure_electron_shell.plan.md`
- Modify: `docs_md/weekly/66_77_week_cli_0_6_desktop_app_schedule.md`

- [x] **Step 1：创建 Week 70 review**

记录实际完成/Deferred、bridge surface、security matrix、runtime state/race、test counts、npm audit/notices、package/process、orphan、三张 shell capture、CLI regression、source revision 和 Week 71 稳定输入。

- [x] **Step 2：检查范围与 drift**

```powershell
cd apps\desktop
npm run check:contracts
npm run check:notices
cd ..\..
git diff --check
git status --short --branch
```

另以 source scan 证明非 generated Desktop source 未出现 thread/catalog/changes/report/artifact business method 或 `turn.start`，Renderer 未出现 Node/fs/process/raw IPC。

- [x] **Step 3：Gate 通过后封板状态**

计划勾选 Task 1-13；总排期改为 Week 66-70 Passed、Week 71 pending；review 状态改为 Passed。任一 Critical Gate 失败时保持执行中或标记 Blocked，不提前声明 Week 71 可开始。

- [x] **Step 4：创建实现与 acceptance 文档提交**

```powershell
git add apps/desktop tools/Invoke-DesktopSmoke.ps1 docs_md/weekly/70_week_secure_electron_shell.plan.md docs_md/weekly/70_week_review.md docs_md/weekly/66_77_week_cli_0_6_desktop_app_schedule.md
git commit -m "feat: 完成第70周安全桌面 Shell"
```

- [x] **Step 5：从 clean HEAD 运行 release acceptance 与最终 package**

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
cd apps\desktop
npm run package:dir
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -CrashRestart
```

Expected：CLI manifest/checksums 绑定 clean HEAD，`sourceDirty=false`、`releaseAcceptance=true`、SDK `9.0.308`；最终 Desktop package 从同一 clean HEAD 构建，review 记录该 source revision，三条 smoke 通过且 orphan delta 0。

## Verification Matrix

| 层 | 必须证明 | 自动化入口 |
|---|---|---|
| Bridge contract | exact channels/status、strict validators、无 generic IPC | `security.test.ts`、`bridge.test.ts` |
| Main transport | strict frame/UTF-8/envelope/id/result、fatal cleanup | `apphost-client.test.ts` |
| Runtime | deterministic start/crash/restart/stop/race/generation | `apphost-runtime.test.ts` |
| IPC | sender validation、no args、dialog path only to AppHost | `ipc-bridge.test.ts` |
| Browser security | sandbox/preferences/CSP/navigation/webview/permission/download deny | `security.test.ts`、production scan |
| Renderer | starting/ready/failed/restart/workspace/layout/accessibility | `App.test.tsx` |
| Real AppHost | handshake/workspace/crash/restart/shutdown | `apphost-process.test.ts` |
| Package | resource path、payload exclusion、production CSP/bundle | `package:dir`、security check |
| Process | startup/window close/crash restart/orphan delta 0 | `Invoke-DesktopSmoke.ps1` |
| Visual | 1440x900、1120x720、760x560 非空且无重叠 | `-CaptureShell` evidence |
| Regression | .NET build/full suite、CLI release/smoke、Desktop verify | standard commands |

## 周末 Gate

Week 70 只有同时满足以下条件才可标记 Passed：

1. Main、Preload、Renderer 只共享本计划冻结的 4-channel bridge；没有 raw/generic IPC、Node、fs、shell、process 或 arbitrary path surface。
2. AppHostClient 对 frame、fatal UTF-8、JSON-RPC root/id、result/error 与 generated method result 全部 runtime validate；invalid response fail closed 并清理 pending/child。
3. initialize 验证 exact protocol/hash/capability/security，Main 不协商 `thread.changed`，不调用 Week 71/72/73 method。
4. runtime supervisor 证明 single child、single operation、generation isolation、explicit restart、quit priority 和 bounded stop；无 sleep-based race test。
5. BrowserWindow 为 isolated/sandboxed/no Node/no webview/no drag navigation；packaged devTools、permission、download、new window、external URL 与 arbitrary navigation 全部 deny。
6. packaged renderer 只加载本地 production bundle；dev 只允许 exact `127.0.0.1:5173`，production CSP 不含 localhost/remote/unsafe directive。
7. IPC sender 必须来自当前受控 Renderer；workspace picker 不接受 Renderer path，dialog path 只进入 AppHost，Renderer 只见 validated canonical outcome。
8. AppHost start/crash/protocol/restart failure 都有 stable safe UI 状态；不显示 stderr、exception、raw path/payload，不自动 restart 或 reopen workspace。
9. 三栏 shell 在 1440x900、1120x720、760x560 无重叠、文字越界或空白主视图；左右 panel controls 可用且 keyboard/label/reduced-motion 基线通过。
10. contract、notice、typecheck、lint、全部 Desktop tests、production build/security checks 通过；0 tests 不算通过。
11. .NET Release 0 warnings/0 errors、标准 full suite 与 CLI dirty/clean smoke 通过；首次失败不得从 review 删除。
12. packaged startup、window-close、crash-restart 全部 exit 0，Desktop/AppHost orphan delta 均为 0。
13. package/process/memory 相对 Week 69 增长不超过 15%；超出时 review 有根因、修复或 Blocked 决定。
14. `70_week_review.md` 记录安全矩阵、测试计数、source、package/process/capture 与 Week 71 输入；plan/schedule 状态一致。
15. clean-source release acceptance 绑定最终提交，manifest/checksums 为 `sourceDirty=false`、`releaseAcceptance=true`；最终 Desktop package/smoke 从同一 clean HEAD 运行并在 review 记录 source revision。

若 Gate 1、2、4、5、6、7、8 或 12 失败，Week 71 不得通过 preload business channel、Renderer direct store/mock truth、自动重启或放宽 CSP/permission 绕过。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| Main 拆分后生命周期更复杂 | `index.ts` 只组合；runtime/window/IPC 分别用 controlled fake 覆盖，不共享隐式全局状态 |
| restart 与旧 child exit 竞态 | generation token + serialized operation；迟到 event 只清理自己的 generation |
| strict client 与 AppHost framing 产生语义偏差 | 继续消费 generated limits；real process integration 验证；不复制 protocol method/timeout constant |
| sender URL guard 阻断开发态 | 只允许 exact loopback Vite target；不回退为任意 localhost/remote URL |
| Renderer 需要 crash diagnostic 诱导泄露 stderr | 只显示 stable RuntimeCode/message；bounded stderr 留在 Main 本地，不进 bridge |
| workspace 重启后状态丢失 | Week 70 安全地清空并要求重选；Week 71 用 list/get/resync 恢复，不在 Renderer 猜测 |
| UI test 依赖扩大 package | testing libraries 全部 devDependency；package inventory 证明未进入 pruned payload |
| CSP 与 Vite HMR 冲突 | dev/production transform 分离；production scan 强制无 unsafe/loopback；不为 HMR 放宽 production |
| packaged crash smoke 误杀其他进程 | 只操作 smoke owner 的 PID/parent/path；禁止按进程名批量 kill |
| responsive shell 提前变业务 UI | 只实现 workspace/runtime/panel mechanics；不放 fake threads/timeline/cards/feature说明 |

按责任回退：transport strictness、runtime supervisor、security/window、IPC/Preload、Renderer shell、smoke harness 分别独立提交。任何回退都不得恢复 generic IPC、unvalidated result、arbitrary dev URL、unsafe CSP、auto restart 或 Renderer path authority。

## Week 71 输入

Week 70 Gate 通过后，Week 71 只能依赖以下稳定输入：

- exact 4-channel frozen DesktopBridge、strict status snapshot/event 与 sender validation。
- single-child AppHost runtime supervisor、explicit restart、bounded stop 和 stale generation isolation。
- generated-result-validated Main transport 与 exact `desktop-v1` handshake。
- secure BrowserWindow/session/CSP/navigation/permission/download/webview baseline。
- workspace picker -> AppHost guard -> canonical WorkspaceOpenResult 路径。
- wide/medium/narrow shell、可折叠左侧、可切换右侧 inspector 与 runtime/workspace error states。
- real AppHost process、packaged startup/window-close/crash-restart、orphan delta 0 与 package/process baseline。
- Week 71 新增 thread/read-only bridge 时必须继续消费 generated contract，并单独评审 `thread.changed` negotiation、notification validation、list/get resync 与 renderer cache；不得扩大本周通用 IPC surface。
