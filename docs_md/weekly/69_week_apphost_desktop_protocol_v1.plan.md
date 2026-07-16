# 第 69 周执行计划：AppHost 与 Desktop Protocol v1

状态：执行中；Step 1-15 已完成，待 Step 16 clean-source release acceptance

更新时间：2026-07-16

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

基线提交：`991e5bed5102077932f162abd034655946c8985c`

## 本周目标

在 Week 66 已建立的 AppHost/framing/generation skeleton、Week 67 Application query contract 和 Week 68 thread/turn/timeline persistence contract 之上，完成 0.6.0 Desktop 使用的正式 `desktop-v1` Application protocol。

本周结束时，AppHost 必须成为单父进程、单 workspace session 的 bounded JSON-RPC authority：完成严格 initialize/capability negotiation、workspace session、workspace/thread/catalog/changes/report/artifact typed dispatch、request cancellation、timeout、并发与 backpressure 边界，以及 process-scoped ordered `thread.changed` notification。所有业务结果只能来自 Application DTO；AppHost 不得直接读取 Core/ProjectPacks store、解析 CLI 文本或维护第二套 thread truth。

本周 Gate 必须在没有 Electron 参与的情况下完成 contract、framing、dispatcher、真实 stdio process 与 cleanup E2E。TypeScript generated contract 必须随 reviewed schema 同步，但 Electron Main、Preload、Renderer 和页面接入留到 Week 70-71。本周不启动 agent turn，不处理 approval，不产生 live tool/command timeline，不扩大 Desktop write authority。

## 起点与稳定输入

### Week 66 已交付的 skeleton

- `CSharpAiCli.AppHost` 已存在，项目只声明对 `CSharpAiCli.Application` 的直接引用。
- `DesktopProtocolFraming` 已实现 ASCII `Content-Length` + UTF-8 JSON body，冻结 header 8 KiB、body 1 MiB。
- `DesktopRpcServer` 已支持 `app.initialize`、`workspace.open`、internal `app.shutdown`，并要求 initialize 先完成。
- `protocol/desktop-v1/contract.json` 是 reviewed source；生成器同步输出 checked-in C# records 与 TypeScript interfaces，`--check` 可拒绝 drift。
- Electron Main 已有真实 AppHost spawn/handshake/shutdown/crash skeleton，但 Week 69 不接入新的业务 method。
- packaged AppHost resource path、stderr 16 KiB tail、headless/window-close cleanup 与 orphan delta 0 已有可复用证据。

### Week 67 已交付的 Application contract

- `ApplicationResult<T>`、9 类 error category、bounded/redacted diagnostic、page limit、aggregate 768 KiB target 与 cancellation contract 已冻结。
- workspace snapshot 提供稳定 `workspaceId`、canonical root、capability 和不含 secret value 的 configuration projection。
- catalog、changes、report、artifact metadata use case 已返回结构化 Application DTO；report identity 为 `job:<id>` / `session:<name>`。
- artifact use case 只读取 managed metadata，不读取内容、不重新 hash、不提升 verification/correctness 结论。
- CLI/Application parity 与 architecture tests 已证明 Application 不依赖 CLI、AppHost 或 Electron。

### Week 68 已交付的 thread contract

- schema-v1 thread/turn/timeline records、typed ids、状态机、expected revision、continuous sequence、manifest commit point 与 corrupt/reparse fail-closed 已通过 Gate。
- `ThreadApplicationService` 已提供 create/list/get/rename/archive/delete、workspace binding、timeline cursor page、pointer hydration、redaction、aggregate bound 与 cancellation。
- active turn 读取返回 recovery-required；显式 interrupted recovery 只在 store 层终结为 failed，不自动 replay。
- session preview/import 已具备 fingerprint/idempotency/rollback-safe 语义，但本周不把 migration 加入协议。
- Week 68 最终 full suite 连续两次 `1321/1321`；Desktop 5 files / `11/11`。
- Week 68 package 465,312,755 bytes，AppHost 79,350,792 bytes，3 秒 working set 404,094,976 bytes，双 smoke orphan delta 0。

### 当前必须关闭的差距

- 当前协议只有 3 个 method，没有 thread/catalog/changes/report/artifact dispatch。
- AppHost 只执行 workspace open，没有 Application-owned workspace session，无法安全复用 `CliEnvironmentSnapshot`。
- 当前 generator 只特殊处理 string、boolean、string array；没有 numeric/date/custom array/nullable、constraints、runtime validator、notification 或 contract hash。
- JSON-RPC root/params 尚未 strict reject unknown member；request id、duplicate id、repeat initialize、workspace-required 和 response size 语义未冻结。
- Server 逐帧串行执行，没有 in-flight 上限、request cancel、server timeout、single writer、bounded output queue 或 backpressure 策略。
- 当前 stdout 只写 response，不支持 notification；notification ordering、reconnect/resync 与非权威语义尚未冻结。
- EOF/disconnect 只结束 read loop，尚无统一 session cancellation、in-flight drain 和 late-write denial 证据。
- 现有 process E2E 只覆盖 initialize/workspace/shutdown，未覆盖业务 DTO、corrupt input、并发、cancel、timeout、backpressure、secret redaction 与 child cleanup。

## 范围冻结

### 本周必须交付

| 能力 | 本周输出 | 权威/依赖 |
|---|---|---|
| Reviewed contract | method、notification、type、constraint、limit、schema/hash 的唯一 source | `protocol/desktop-v1/contract.json` |
| Generation | deterministic C#/TS DTO、constants、runtime validators 与 drift check | 现有 Node generator，无第二份手写 type |
| Framing | strict Content-Length、UTF-8、depth、partial/oversize/EOF 行为 | AppHost protocol layer |
| Handshake | exact version、contract hash、capability intersection、security/limit summary | AppHost session state |
| Workspace session | Application-owned opaque context，AppHost 不构造/暴露 Core snapshot | Application facade |
| Typed dispatch | workspace/thread/catalog/changes/report/artifact method 到 Application use case | AppHost dispatcher |
| Lifecycle | initialize/open/ready/shutdown/disconnect state machine | 单父进程 stdio session |
| Flow control | request id、并发、rate、timeout、cancel、output queue 与 backpressure | AppHost runtime |
| Notification | process-scoped ordered `thread.changed` | mutation result + single writer |
| E2E | 无 Electron 的真实 process framing/dispatch/cancel/cleanup tests | published/built AppHost |

### 本周明确不做

- 不修改 Electron Main、Preload、Renderer、bridge allowlist、窗口或页面；只允许更新 generated TypeScript contract 和 generator tests。
- 不实现 `turn.start`、`turn.cancel`、`approval.resolve`、agent loop、model、tool、shell、MCP、Project Pack execution 或 terminal。
- `app.cancel` 只取消一个 protocol request，不等同于 agent turn cancel，也不写 turn 状态。
- 不新增 live timeline producer，不发送 `timeline.appended`，不伪造 tool/command/approval/completion notification。
- 不把 session preview/import 暴露为 Desktop method；legacy migration 继续由 Week 68 Application contract 保留，待独立 UX 评审。
- 不接入 Desktop UI fake data，不让 production AppHost 返回 test fixture 或 mock thread truth。
- 不通过 localhost daemon、TCP、named pipe、WebSocket、HTTP 或任意额外 control channel 绕过 stdio。
- 不让 AppHost 直接引用 `CSharpAiCli.Core`、`CSharpAiCli.ProjectPacks`、`CSharpAiCli.Cli`，不读取 `.caicli/threads` 或其他 store 文件。
- 不接受 client 提供的 `CliEnvironmentSnapshot`、canonical root、workspace id、configuration、policy、安全结论或 store path。
- 不复制 Application DTO 为手写 C#/TS 业务模型；protocol mapping 只能是 reviewed generated transport DTO 与 Application projection 的显式 allowlist 映射。
- 不持久化 request、response、notification、capability、cancel token、approval material 或 protocol session state。
- 不把 notification 当 durable truth；断线重连必须依赖 list/get + revision/sequence resync。
- 不升级 Electron/React/Node/npm dependency，不新增 JSON validation/runtime package，除非现有生成器无法在无依赖条件下满足 frozen constraints 且经过单独评审。

## 责任边界与目录布局

### 1. Ownership

- `protocol/desktop-v1/contract.json`：method、notification、type、property constraint、limit 与 protocol error code 的唯一业务 source。
- `protocol/desktop-v1/contract.schema.json`：只验证 contract DSL 自身 shape，不复制 method/type 定义，不是第二套业务 contract。
- `apps/desktop/scripts/generate-contracts.mjs`：解析/验证 DSL，生成 C#、TypeScript、type guards/validators 与 contract SHA256。
- `CSharpAiCli.Application`：新增 Application-owned workspace session/facade；内部持有 `CliEnvironmentSnapshot`，公开表面只暴露 Application context/projection，不暴露 Core/store 类型。
- `CSharpAiCli.AppHost`：framing、JSON-RPC session、dispatch、timeout/cancel、single writer、notification、safe diagnostics 与 process lifecycle。
- `CSharpAiCli.Core` / `ProjectPacks`：继续拥有 store、workspace guard、job/queue/run/artifact truth；本周不为 protocol 复制 schema。
- Electron/CLI：不参与本周业务 dispatch；CLI 行为和 desktop bridge allowlist 保持不变。

### 2. 建议代码布局

```text
protocol/desktop-v1/
  contract.json
  contract.schema.json
  README.md
  examples/
    initialize.request.json
    thread-list.response.json
    thread-changed.notification.json

src/CSharpAiCli.Application/
  DesktopApplicationSession.cs

src/CSharpAiCli.AppHost/Protocol/
  DesktopProtocolFraming.cs
  DesktopRpcSession.cs
  DesktopRpcDispatcher.cs
  DesktopRpcWriter.cs
  DesktopProtocolMapper.cs
  DesktopProtocolDiagnostics.cs
  Generated/DesktopProtocolContracts.g.cs

apps/desktop/src/generated/
  desktop-contracts.ts
```

文件名可服从现有项目风格调整，但职责必须保持分离。不得把所有 session、dispatch、mapping、queue 和 diagnostics 继续堆入单个 switch class。

## Protocol Contract

### 1. Version 与 schema

- transport protocol 继续为 `desktop-v1`，本周不创建 `desktop-v2`。
- 每个 params、result、notification params 顶层包含独立整数 `schemaVersion`，本周值为 1。
- initialize 使用 exact `protocolVersion=desktop-v1`；不接受 prefix、case-insensitive、空白归一化或 silent downgrade。
- generator 对 canonicalized `contract.json` bytes 计算 lowercase SHA256，C#/TS 均生成 `CONTRACT_SHA256` 常量。
- initialize result 返回 server contract SHA256；client 可在进入 ready 前比较 generated hash。hash mismatch 为 handshake failure，不继续业务请求。
- checked-in generated files使用 UTF-8/LF 与 deterministic declaration order；同一 source 连续生成必须 byte-identical。

### 2. Method allowlist

| Method | Params 要点 | Application 映射 | Workspace 要求 | Side effect |
|---|---|---|---|---|
| `app.initialize` | version/hash/client identity/capabilities | protocol session | 否 | 建立单次 handshake |
| `app.cancel` | target request id | protocol runtime | 否，initialize 后 | 仅取消 in-flight RPC |
| `app.shutdown` | bounded reason | protocol runtime | 否，initialize 后 | cancel/drain/exit |
| `workspace.open` | caller-selected path | Application session factory + workspace snapshot | 否 | 替换 active workspace session |
| `thread.list` | page size | `ThreadApplicationService.List` | 是 | 只读 |
| `thread.get` | thread id/after sequence/page size | `ThreadApplicationService.Get` | 是 | 只读 |
| `thread.create` | bounded title | `ThreadApplicationService.Create` | 是 | 创建 thread |
| `thread.rename` | thread id/expected revision/title | `ThreadApplicationService.Rename` | 是 | revision mutation |
| `thread.archive` | thread id/expected revision | `ThreadApplicationService.Archive` | 是 | revision mutation |
| `thread.delete` | thread id/expected revision/exact confirmation | `ThreadApplicationService.Delete` | 是 | confirmed delete |
| `catalog.list` | kind/page size | `CatalogApplicationService.Query` | 是 | 只读 |
| `changes.get` | optional validated session name | `ChangesApplicationService.Query` | 是 | 只读 Git/session projection |
| `report.list` | page size | `ReportApplicationService.List` | 是 | 只读 |
| `report.get` | report id | `ReportApplicationService.Get` | 是 | 只读 |
| `artifact.list` | page size/optional run/status | `ArtifactApplicationService.List` | 是 | metadata only |
| `artifact.get` | artifact id | `ArtifactApplicationService.Get` | 是 | metadata only |

method name 使用 ordinal exact match。unknown method 返回 JSON-RPC method-not-found，不能 fallback 到 CLI command、reflection method invocation 或 arbitrary Application service name。

### 3. 本周 notification

只新增一个 production notification：

| Notification | 触发 | Payload |
|---|---|---|
| `thread.changed` | create/rename/archive/delete 成功 committed 后 | schemaVersion、eventSequence、workspaceId、threadId、revision、changeKind、emittedAtUtc |

- `changeKind` allowlist：`created`、`renamed`、`archived`、`deleted`。
- `eventSequence` 是 AppHost process/session 内从 1 开始的严格连续序列，不是 timeline sequence，不跨重启持久化。
- delete notification 保留 caller 成功使用的 expected revision；不重新读取已删除 store，不携带 source path。
- notification 只在 Application mutation 成功时发送；validation/conflict/denied/cancel/timeout 不发送。
- mutation response 先进入 single-writer queue，随后进入对应 notification；同一 session 的 notification 按分配的 `eventSequence` 写出。
- client 未声明 `thread.changed` capability 时不发送该 notification，但 mutation 行为不变。
- notification queue 不允许 silent drop。若 transport 已断开或 backpressure session 终止，store 仍是唯一 truth；下一次连接必须 list/get resync。

本周不定义 timeline、approval、tool、command、report-progress 或 heartbeat notification。

### 4. Application outcome envelope

所有业务 method result 使用同一语义、各自 typed data 的 transport envelope：

- `succeeded`：是否完成 Application use case。
- `data`：method-specific generated DTO；failure 时为 null。
- `error`：code、9 类 category、safeMessage、retryable；success 时为 null。
- `diagnostics`：最多 100 条，每条沿用 Application 4 KiB safe bound。
- `truncated`：Application page/aggregate/diagnostic 是否截断。

expected domain failure 仍是 JSON-RPC success response 中的 `succeeded=false` outcome，不能把 validation/not-found/conflict/corrupt-state 等压平为 transport error。只有 frame、JSON-RPC envelope、handshake、session state、cancel/timeout/backpressure 和 unexpected internal failure 使用 JSON-RPC error。

AppHost mapper 必须逐字段 allowlist 映射 Application DTO；不得 `Serialize(object)` 后以 `JsonNode` 透传未知字段，也不得把 Application record type直接作为 generated protocol result。

### 5. Projection 保真与安全

- thread list/get 保留 revision、status、turn summary、timeline item type/sequence、cursor/truncated、source pointer availability 与 recovery-required。
- catalog 保留 kind/id/title/description/availability 的 Application 语义，不发送 loader path 或 raw manifest。
- changes 只发送 bounded Git/session projection；不发送完整 diff、raw stdout/stderr 或 session transcript。
- report 只发送 structured summary/pointers；不发送 raw prompt、exception、stack 或任意文件内容。
- artifact 只发送 metadata；不打开 content、不 preview、不 verify、不重新 hash。
- root path 只在 workspace.open/snapshot 返回 canonical workspace root；diagnostic、stderr 和 protocol error 不重复回显 caller raw path。
- title/summary/diagnostic 在 Application 已脱敏后映射；AppHost 不实现第二套 secret parser，但仍测试 response/stderr 不含请求中的 secret sentinel。

## Contract DSL 与生成链

### 1. DSL type grammar

生成器必须显式支持并验证以下有限 grammar：

- scalar：`string`、`boolean`、`int32`、`int64`、`datetime`。
- named type reference。
- nullable：仅允许 reviewed property 标记 `nullable: true`，不通过拼接任意 type text 注入代码。
- array：`array` + `items` named/scalar type，必须带 `maxItems`。
- string 必须声明 `maxUtf8Bytes`；allowlist string 必须声明 enum values。
- numeric 必须声明 min/max；datetime 必须是 UTC ISO-8601。
- object 默认 required 且 `additionalProperties=false`；optional property 必须显式声明。

禁止 generator 把 contract 中任意 `type` 字符串原样写入 C#/TS。unknown type、duplicate type/property/method/constant、循环 object、unbounded array/string、invalid enum、missing ref 或 unsafe identifier 必须使 generation/check 失败。

### 2. Generated outputs

- C#：sealed records、JSON property names、strict unmapped-member behavior、method/notification/limit/hash constants、generated validators。
- TypeScript：interfaces、readonly constants、method/notification names、runtime type guards/validators；numeric 映射为 `number` 并执行 safe integer/range check。
- DateTimeOffset 在 C# 使用 UTC `DateTimeOffset`；TypeScript transport 为 string，runtime validator拒绝非 UTC 或 invalid date。
- arrays 在两端都生成只读/readonly projection，不生成 `any`、`unknown as T` 或 index signature。
- `--check` 同时验证 contract DSL、examples、C# output 与 TS output；check mode 不写文件。
- generated 文件必须包含 source path、protocol version 与 contract hash header，不包含生成时间，保证 byte-stable。

### 3. Examples 与 drift

- 每个新增 method 至少一个 valid params/result fixture；error envelope、thread.changed notification 各至少一个 fixture。
- examples 由生成 validator 在 .NET/Node 两端验证；字段缺失、unknown member、wrong type、oversize、invalid enum fixture 必须失败。
- C# reflection 与 TypeScript generated source test 核对 method/type/property 名、nullability、array 和 enum，不接受只比 method count。

## AppHost Session State Machine

冻结状态：

- `created`：进程已启动，只允许 `app.initialize`。
- `initialized`：handshake 完成，可 `workspace.open`、`app.cancel`、`app.shutdown`。
- `workspace-ready`：持有一个 Application-owned workspace session，可调用所有业务 method。
- `shutting-down`：拒绝新请求，cancel in-flight，bounded drain writer。
- `closed`：stdin EOF、stdout failure、fatal framing error 或正常退出后的终态。

核心规则：

- initialize 只能成功一次；repeat/concurrent initialize 返回 stable already-initialized error，不重置 capability/event sequence。
- initialize 前的任何其他 method 返回 initialize-required；malformed frame/JSON 仍按 framing/parse error 优先分类。
- workspace.open 成功后原子替换 active Application session；失败保留原 session，不产生半初始化状态。
- workspace-bound method 不接受 workspace id/root/snapshot 参数，始终使用 server active session；未 open 返回 workspace-required。
- 第二次 workspace.open 必须先 cancel旧 workspace 的 in-flight request；旧 request 不得在新 workspace 下完成或发送 notification。
- shutdown/EOF/disconnect 触发 session cancellation；不 replay、不持久化 request、不把 canceled read 猜成 success。
- fatal framing error 终止 session；单个 valid frame 内的 JSON-RPC/params/domain error 不终止 session。

## Application-owned Workspace Session

Week 69 在 Application 增加 opaque session/facade，解决 AppHost 当前必须接触 Core snapshot 的架构缺口：

- Application factory 接受 caller-selected path/current directory，内部调用 workspace open、`CliEnvironmentSnapshot.Create` 与 snapshot validation。
- 公开 session 类型只暴露 `WorkspaceSnapshotProjection` 和 Application use case methods；内部 snapshot 不可从 public property/constructor/reflection surface 取得。
- AppHost 只保存 opaque Application session，调用 scalar request DTO；不 `using CSharpAiCli.Core`，不调用 `ConfigLoader`、`WorkspaceContext`、`ThreadStore` 或任何 source store。
- workspace session 捕获 open 时的 environment/configuration snapshot；每次需要当前 store metadata 的 Application service仍按现有 authoritative query 读取。
- workspace.open 不接受 API key/model/config value；配置继续由 Application/Core 按 accepted precedence 读取，只输出 presence/source projection。
- session dispose/cancel 只释放 request lifetime，不删除 workspace/thread/session/job/artifact 文件。

不得把 opaque session 放进 protocol data 序列化，也不得为了避免 facade 而让 AppHost 直接构造 Core types。

## Framing 与 JSON-RPC 边界

### 1. Frozen limits

| 项目 | 值 | 规则 |
|---|---:|---|
| header | 8,192 bytes | terminator 前超限即 fatal |
| body | 1,048,576 bytes | 分配前验证；0-byte body 拒绝 |
| JSON depth | 64 | parse 与 deserialize 同一限制 |
| method name | 128 UTF-8 bytes | exact allowlist |
| request id | 1..9,007,199,254,740,991 | JSON safe positive integer；session 内 in-flight 唯一 |
| initialize timeout | 5,000 ms | 超时终止 handshake session |
| default query timeout | 15,000 ms | linked server/client cancellation |
| mutation timeout | 30,000 ms | timeout 不撤销已 committed store mutation |
| shutdown drain | 2,000 ms | 超时停止接受输出并退出 |
| max in-flight | 8 | 第 9 个返回 server-busy，不排队执行 |
| request rate | burst 64 / sustained 32 per second | injectable monotonic clock/token bucket |
| output queue | 64 frames 且 4 MiB | 同时满足 frame count 与 aggregate bytes |
| notification rate | 每个成功 mutation 最多 1 条 | 无 heartbeat/polling/stream spam |
| stderr retained by Main | 16,384 bytes | AppHost 每条 safe diagnostic 最大 4 KiB |

Application result目标继续不超过 768 KiB，为 JSON-RPC envelope 与 framing 留出余量。response/notification 序列化后超过 1 MiB 必须返回/记录 stable response-too-large，不部分写 frame。

### 2. Header 与 body

- header 仅接受 ASCII、CRLF line ending、恰好一个 `Content-Length`；optional `Content-Type` 只允许 reviewed UTF-8 value。
- duplicate length、negative/plus sign/decimal/hex/overflow、obs-fold、NUL/control、bare LF、missing terminator、unknown oversized header 均拒绝。
- body 必须是 strict UTF-8；invalid byte sequence 不得 replacement decode。
- partial body EOF、disconnect 与 cancellation 分类稳定，不把已读 prefix 当 JSON。
- writer 在持有 single-writer ownership 时一次写完整 header/body 并 flush；任何 code path 禁止直接写 stdout。

### 3. JSON-RPC envelope

- request 顶层只允许 `jsonrpc`、`id`、`method`、`params`；unknown member 拒绝。
- `jsonrpc` 必须 exact `2.0`；id 必须符合 safe integer 范围；params 必须 object。
- client notification 除 reviewed cancel form 外不接受；本周 `app.cancel` 使用有 id 的普通 request，返回 typed result。
- duplicate in-flight id 返回 duplicate-request-id，不取消或替换原 request。
- response id 必须原样匹配 accepted numeric id；并发完成顺序不要求等于请求顺序。
- params strict unknown-member rejection；missing/null/oversize/range/enum invalid 统一 invalid-params，不泄露 serializer exception。

## Error Contract

冻结 protocol stable error code：

- framing：`frame-header-incomplete`、`frame-header-too-large`、`frame-header-invalid`、`frame-content-length-missing`、`frame-content-length-invalid`、`frame-body-too-large`、`frame-body-incomplete`、`frame-body-empty`、`frame-utf8-invalid`。
- JSON-RPC：`parse-error`、`invalid-request`、`method-not-found`、`invalid-params`、`response-too-large`。
- handshake/session：`initialize-required`、`already-initialized`、`protocol-version-unsupported`、`contract-mismatch`、`capability-invalid`、`workspace-required`、`workspace-changed`、`shutdown-in-progress`。
- flow control：`duplicate-request-id`、`server-busy`、`request-rate-exceeded`、`request-canceled`、`request-timeout`、`output-backpressure`、`transport-closed`。
- unexpected：`internal-error`，只返回固定 safe message。

JSON-RPC numeric code 分类：

- `-32700` parse error。
- `-32600` invalid request。
- `-32601` method not found。
- `-32602` invalid params。
- `-32603` internal error。
- `-32001..-32019` 依次保留给 version/session/cancel/timeout/busy/backpressure stable server errors；具体映射写入 contract source 并测试，不能散落 magic number。

exception message/type/stack、raw request、raw path、secret、store diagnostic detail 不进入 protocol error 或 stderr。expected Application failure使用 outcome envelope，保持原 code/category/retryable。

## Concurrency、Cancel 与 Backpressure

### 1. Runtime model

- 一个 reader task 串行解析 frame/envelope，只负责 validation 与 dispatch admission。
- 最多 8 个 handler 并发；handler 使用独立 linked CTS，调用 Application cancellation-aware API。
- 一个 bounded single-writer task 独占 stdout，写 response/notification；handler 不直接写 stream。
- in-flight registry 以 numeric id 为 key；register/remove/cancel/shutdown 操作线程安全且 deterministic。
- mutation concurrency 仍由 Week 68 ThreadStore expected revision/OS mutex 决定；AppHost 不增加第二套 revision lock。

### 2. Cancel

- `app.cancel` 指向当前 in-flight request id；已找到并请求 cancel 返回 `accepted=true`，不存在/已完成返回 `accepted=false`，均不猜测业务结果。
- cancel request 自身不可 target 自己；shutdown 不通过 app.cancel 循环实现。
- pre-I/O cancel 不触发 Application；during projection cancel 抛出 cancellation；commit 后 cancel 不能回滚 store。
- 若 mutation 已 committed 但 response 因 cancel/disconnect 未送达，重连后以 store revision/list/get 为准；不自动 retry mutation。
- timeout 与 caller cancel 使用不同 stable code；都不把 OperationCanceledException message 暴露。

### 3. Backpressure

- writer queue 同时按 frame count 与 aggregate bytes 限制；enqueue 必须预先知道完整 serialized frame size。
- query response 入队失败返回/终止为 output-backpressure；不能无限等待或无界增长内存。
- committed mutation 的 response/notification 不 silent drop；若 transport 无法接收则 fail session，并依赖 durable store resync。
- response 优先于同 mutation notification；notification 不能饿死 response。
- blocking/failing output stream tests 使用受控 stream/clock，不依赖 wall-clock sleep。

## Capability Negotiation

initialize params 冻结包含：

- schemaVersion、protocolVersion、contractSha256。
- bounded clientName/clientVersion/clientInstanceId。
- requestedCapabilities allowlist。

initialize result 冻结包含：

- schemaVersion、protocolVersion、contractSha256、serverVersion、serverInstanceId。
- negotiatedCapabilities、method allowlist、notification allowlist。
- protocol limits、security summary。

规则：

- required base capability：framed JSON-RPC、workspace session、application outcome。
- optional capability：`thread.changed` notification；只返回 client 请求且 server 支持的交集。
- unknown requested capability 返回 capability-invalid，不把任意 client string回显。
- capability 只描述可用 transport/use case，不授予 workspace、tool、approval 或 filesystem 权限。
- serverInstanceId 使用 process-scoped random typed id，仅用于诊断连接身份，不持久化、不作为安全 token。

## Testing Matrix

| 范围 | 必测路径 |
|---|---|
| Contract DSL | duplicate/unknown/unbounded/unsafe type、nullable/array/date/numeric/enum、examples、byte-stable generation |
| Generated C# / TS | method/type/property parity、hash、strict DTO、runtime validator、check-mode no write |
| Framing | partial header/body、UTF-8 multiline、duplicate/missing/overflow length、bare LF、control、empty/oversize、EOF/cancel |
| Envelope | malformed JSON、root type、unknown field、id range/duplicate、params missing/null/unknown、method/version exactness |
| Handshake | initialize-required、success、hash/version mismatch、capability intersection、repeat/concurrent initialize |
| Workspace session | open success/failure、old session preservation、replacement cancel、workspace-required、no client snapshot |
| Dispatch | 15 method allowlist、Application data identity、domain failure envelope、page/cursor/truncated/diagnostic |
| Thread mutation | expected revision、confirmation、workspace binding、notification only on success、delete result |
| Pointer/read-only | missing/corrupt/stale source、artifact metadata no-content-read、changes/report redaction |
| Concurrency | 8 admitted/9th busy、out-of-order completion/id match、duplicate id、shutdown drain |
| Cancel/timeout | pre-I/O、during projection、not-found target、race with completion、committed mutation ambiguity |
| Backpressure | queue frame/byte cap、blocking/failing writer、response priority、session fail without partial frame |
| Notification | negotiated only、sequence 1..N、response-before-event、no event on failure、reconnect resync semantics |
| Diagnostics | stdout frames only、stderr bound、secret/path/exception denial、internal-error fixed text |
| Process E2E | real stdio initialize/open/query/mutate/shutdown、malformed/oversize/disconnect/crash、exit code、no child/temp |
| Architecture | AppHost only Application、no store/Core/CLI/Electron dependency、no schema/type copy、Desktop unchanged except generated |
| Regression | Release build、full suite x2、CLI smoke、Desktop verify、publish/package、双 smoke、performance |

定向 filter 必须记录实际 test count，0 tests 不算通过。并发/cancel/backpressure tests 必须使用 controlled gates/manual clock，不通过盲目扩大 timeout 掩盖竞态。

## Architecture Tests

新增自动约束：

- AppHost csproj 只直接引用 Application；AppHost public/implementation source 不引用 Core、ProjectPacks、CLI 或 store type。
- Application-owned workspace session public surface 不暴露 `CliEnvironmentSnapshot`、store、filesystem handle、JSON document 或 renderer type。
- AppHost dispatcher 每个 method 只调用 Application facade；禁止 direct file enumeration、Git process、artifact content read 或 CLI factory。
- `contract.json` 是 method/type/error/notification 唯一 source；generated C#/TS 不允许手工追加未生成 declaration。
- AppHost 不包含 method 名、numeric RPC code、limit 的重复手写 constant；全部来自 generated definition。
- Electron Main/Preload/Renderer/bridge 在 Week 69 不增加 thread/catalog/changes/report/artifact API；generated TS 之外无业务接入 diff。
- AppHost stdout 的所有 write call 只能位于 framing writer；`Console.Write/Out` source scan 必须失败。
- production AppHost 不引用 fake Application、test fixture、Desktop mock data 或 in-memory truth。
- protocol output 不包含 Core ThreadRecord、TurnRecord、TimelineItemRecord、JobRecord、TaskQueueItem 或 ManagedArtifact record type。

## Process E2E 与 Cleanup

- 建立无 Electron 的 AppHost process harness，使用真实 stdin/stdout framing 与独立 stderr capture。
- happy path：initialize -> workspace.open -> thread.create -> thread.list -> thread.get -> rename -> archive -> delete -> shutdown。
- read-only path：catalog.list -> changes.get -> report.list/get -> artifact.list/get；fixture 来自现有 authoritative stores。
- concurrency path：同时发送多个 id，验证 response 可乱序但 id 正确，notification sequence 严格有序。
- disconnect path：有 in-flight query 时关闭 stdin/kill parent side，AppHost bounded 退出，不再写 response。
- malformed/fatal frame path：AppHost 返回/记录 stable code 后退出，不把 raw payload写 stderr。
- shutdown path：拒绝新 request、cancel in-flight、drain bounded output、exit 0。
- 每个 process test 记录 AppHost 与其 child process before/after delta；Git/dotnet 等 child 不得残留。
- temp workspace/session/thread fixtures 由 harness owner 清理；AppHost 不递归删除 workspace 或 source store。
- packaged smoke 继续验证 headless/window-close orphan delta 0；本周没有 Electron business E2E。

## Execution Tasks

- [x] Step 1：记录 Week 69 起点 commit/branch/clean state、SDK/Node/npm、Week 68 test/package/process 基线，并复核 Week 66 protocol skeleton。
- [x] Step 2：评审并冻结 method、notification、schema version、capability、outcome、error code 与所有 numeric limits。
- [x] Step 3：扩展 contract DSL/meta-schema/generator，生成 strict C#/TS DTO、validators、hash，加入 examples 与 drift tests。
- [x] Step 4：hardening Content-Length framing、strict UTF-8/JSON depth/request envelope/id/params validation。
- [x] Step 5：实现 Application-owned workspace session/facade，保持 AppHost 只依赖 Application。
- [x] Step 6：实现 AppHost initialize/workspace/shutdown state machine、contract hash 与 capability negotiation。
- [x] Step 7：接入 workspace/catalog/changes/report/artifact typed read-only dispatch 与 Application outcome mapping。
- [x] Step 8：接入 thread list/get/create/rename/archive/delete，覆盖 workspace binding、revision、confirmation 与 cursor。
- [x] Step 9：实现 safe protocol mapper/error/diagnostic bounds，覆盖 secret、path、exception 与 oversize response。
- [x] Step 10：实现 reader/8-handler/single-writer runtime、in-flight registry、rate limit、server timeout 与 `app.cancel`。
- [x] Step 11：实现 bounded output queue/backpressure 与 negotiated ordered `thread.changed` notification。
- [x] Step 12：加入 contract/framing/session/dispatch/concurrency/cancel/backpressure/notification/architecture tests。
- [x] Step 13：加入无 Electron 的真实 AppHost process E2E、disconnect/crash/stdout/stderr/child/temp cleanup tests。
- [x] Step 14：运行 generator check、定向 .NET/Node tests、Release build、标准并发 full suite 连续两次与 CLI smoke。
- [x] Step 15：运行 Desktop verify、AppHost publish/package、双 packaged smoke、orphan 和同口径 package/process/memory 采样。
- [ ] Step 16：执行 `git diff --check`、文档/contract link/drift 检查，创建 `69_week_review.md`；提交后运行 clean-source release acceptance。

## 建议日程

| 工作日 | 重点 | 当日退出条件 |
|---|---|---|
| Day 1 | contract surface、DSL、limits、error、capability review | schema/examples/generation byte-stable；非法 contract 自动失败 |
| Day 2 | framing、envelope、session state、Application workspace facade | strict frame/request/handshake/workspace tests 通过；AppHost 无 Core/store dependency |
| Day 3 | 15 method dispatch、outcome mapping、thread mutation | real Application data identity 与 domain failure tests 通过 |
| Day 4 | concurrency、cancel、timeout、writer/backpressure、notification | controlled race/queue/order/disconnect tests 通过，无 sleep-based flake |
| Day 5 | process E2E、redaction/cleanup、全量回归、package/performance/review | 无 Electron E2E Gate、双 full suite、双 smoke、review 与 clean acceptance 证据完整 |

Day 1 contract/generator Gate 未通过时不得手写 DTO 继续 dispatch。Day 2 workspace facade/architecture Gate 未通过时不得让 AppHost 临时引用 Core。Day 4 cancel/backpressure Gate 未通过时不得把业务 method 交给 Week 70 Main/Preload。

## Verification Commands

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version

cd apps\desktop
npm run generate:contracts
npm run check:contracts
npm run typecheck
npm test
cd ..\..

dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~DesktopProtocol|FullyQualifiedName~AppHost|FullyQualifiedName~ThreadApplication|FullyQualifiedName~ApplicationArchitecture"
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build

powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -AllowDirtySource
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1

cd apps\desktop
npm run verify
npm run package:apphost
$env:CAICLI_ELECTRON_ZIP_DIR = "$env:LOCALAPPDATA\electron\Cache"
node scripts\package-desktop.mjs
cd ..\..
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1 -WindowClose
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Measure-DesktopBaseline.ps1

git diff --check
git status --short --branch
```

提交后从 clean HEAD 运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

dirty-source `-AllowDirtySource` 产物只用于中途 smoke，不得声明 acceptance。最终 manifest/checksums 必须绑定 Week 69 clean HEAD，记录 `sourceDirty=false`、`releaseAcceptance=true` 和 SDK `9.0.308`。

## 周末 Gate

Week 69 只有同时满足以下条件才可标记 Passed：

1. `contract.json` 冻结所有本周 method/notification/type/limit/error；meta-schema、examples、C#/TS generation/runtime validation 与 drift tests 通过。
2. AppHost exact handshake 返回匹配 version/hash/capability/security/limits；repeat/mismatch/unknown capability fail closed。
3. framing 对 partial、invalid、duplicate、oversize、empty、invalid UTF-8、depth、EOF 与 cancel 有稳定自动化证据，body 超限前不分配。
4. Application-owned workspace session 建立；AppHost 只依赖 Application，不接触 Core snapshot、store、CLI 或 Electron。
5. workspace/thread/catalog/changes/report/artifact 13 个 business method 使用 typed params/result 和统一 outcome，保留 Application data/error/diagnostic/truncated 语义。
6. thread mutation 保留 workspace binding、expected revision、confirmation 与 committed result；failure/cancel/timeout 不伪造 notification。
7. reader/handler/writer、8 in-flight、id/rate/timeout/cancel、64-frame/4-MiB backpressure boundary 通过 deterministic tests，无 unbounded queue/task/buffer。
8. negotiated `thread.changed` 只在成功 mutation 后发送，eventSequence 连续、response-before-event、断线后明确依赖 list/get resync。
9. stdout 只包含完整 protocol frame；stderr/JSON-RPC/internal error 均 bounded/redacted，不含 raw payload、secret、path、exception type/message/stack。
10. 无 Electron 的真实 process E2E 覆盖 happy path、read-only、concurrency、malformed、cancel、disconnect、shutdown；退出后 AppHost child/temp delta 为 0。
11. architecture tests 证明没有 protocol schema copy、AppHost direct store、Desktop premature bridge/UI 接入或 production fake truth。
12. Release build `0 warnings / 0 errors`；标准并发 full suite 连续两次通过；CLI smoke 与 Desktop verify 无回归。
13. packaged headless/window-close orphan delta 均为 0；package/AppHost/working set 相对 Week 68 增长超过 15% 时有原因与处置结论。
14. `69_week_review.md` 记录 contract hash、method/type count、limits、state/concurrency/cancel/backpressure、test count、cleanup、package/process 和 Week 70 输入。
15. clean-source release acceptance 绑定最终提交，manifest/checksums 为 `sourceDirty=false`、`releaseAcceptance=true`，acceptance smoke 通过。

若第 1、3、4、5、7、8、9 或 10 项失败，Week 70 不得通过 Electron Main 私有 IPC、手写 TS type、Renderer mock、增大无界 queue、忽略 notification 或 direct store read 绕过 Gate。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| contract type 数量快速膨胀 | composition + shared outcome/pointer/diagnostic types；generator 检查 duplicate/ref/depth，不用 arbitrary JSON |
| generator 变成代码注入入口 | 有限 type grammar、safe identifier、meta-schema、禁止 raw type passthrough、golden output tests |
| AppHost 为构造 snapshot 反向依赖 Core | Application-owned opaque workspace session；architecture test 禁止 AppHost Core namespace/type |
| concurrent response 污染 stdout | single writer 独占 frame output；handler 只能 enqueue immutable serialized frame |
| cancel 与 commit race | Application/store commit 是权威；cancel 不回滚 committed mutation，重连按 revision resync |
| output consumer 停止读取导致内存增长 | frame + aggregate byte bounded queue；超限 fail session，不无界缓存或 silent drop |
| notification 顺序与 store revision 不一致 | 成功 result 后在 sequencer 中分配 eventSequence；notification 非 truth，consumer refetch revision |
| domain failure 被误当 transport crash | expected failure 使用 outcome envelope；RPC error 只用于 protocol/session/runtime |
| error/diagnostic 泄露 request/path/secret | fixed protocol messages + Application redaction + sentinel tests；stderr 只写 stable code |
| process disconnect 留下 child/task | session CTS、bounded drain、EOF/crash E2E、before/after process/temp inventory |
| response 接近 1 MiB 无 envelope 余量 | Application 768 KiB target + serialize-before-enqueue + response-too-large fail closed |
| Week 70 提前依赖未稳定协议 | Week 69 禁止 bridge/UI 接入；只有通过 Gate 的 generated TS、method/capability/notification 进入 Week 70 |

回退单位按层分离：contract/generator、Application workspace facade、AppHost runtime/dispatch、notification。若 concurrency/backpressure 未通过，可保留 reviewed typed read-only method 与串行 test harness，但不得向 Week 70 宣称正式 protocol；不得回退为 CLI text、unbounded JSON、direct store 或 Renderer local truth。

## Week 70 输入

通过 Gate 后，Week 70 只接收以下稳定输入：

- exact `desktop-v1` version + contract SHA256 + generated C#/TS DTO/validators。
- 16 个 method（3 个 app control + 13 个 workspace/business）、统一 Application outcome、stable protocol/domain error 与 frozen limits。
- initialize -> workspace-ready -> shutdown state machine、capability negotiation 和 workspace session semantics。
- bounded concurrent request、numeric id、cancel/timeout/rate/backpressure 与 disconnect cleanup contract。
- ordered negotiated `thread.changed` notification 及其非持久化/resync 语义。
- 无 Electron 的真实 AppHost process E2E、stdout/stderr safety、child/temp cleanup 证据。
- AppHost 只依赖 Application、Main/Preload/Renderer 尚未接入业务 method 的 architecture 结论。
- full suite、CLI smoke、Desktop verify/package/process/performance 与 clean-source acceptance 基线。

Week 70 Main/Preload 只能消费 generated TypeScript contract 和这些稳定语义；不得重新定义 method、type、timeout、error、capability 或 notification，也不得把 AppHost raw transport直接暴露给 Renderer。
