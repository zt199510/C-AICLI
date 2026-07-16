# 第 68 周执行计划：Thread、Turn、Timeline 与持久化投影

状态：实现与技术验证已完成；clean-source release acceptance 待提交后补跑

更新时间：2026-07-16

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

基线提交：`051087b965cbf5420c7f75eab34faa65357a5342`

## 本周目标

在 Week 67 Application contract 之上建立 0.6.0 唯一的 thread/turn/timeline 持久化基础，使 CLI 与未来 AppHost 可以共享稳定、可恢复、可分页且 UI-safe 的任务投影。

本周结束时，系统必须具备 versioned thread、turn、timeline records，明确的状态机、单调 sequence、expected revision、atomic commit、corrupt-state diagnostics，以及 thread create/list/get/rename/archive/delete Application use case。旧 CLI session 必须通过显式、可回退的导入流程投影为 timeline，不能自动改写或删除原 session。

本周只建设持久化与只读/管理型 Application contract，不修改 `desktop-v1` method，不接入 Desktop 页面，不启动 agent turn，不持久化 approval grant。Week 69 再把通过 Gate 的 contract 接入 AppHost protocol，Week 71 再建设只读 thread/timeline UI，Week 73 才接入 write-capable turn。

## 起点与稳定输入

### Week 67 已交付

- `CSharpAiCli.Application` 已提供统一的 `ApplicationResult<T>`、9 类 error category、page/diagnostic/aggregate limits、redaction 和 cancellation contract。
- workspace snapshot 已提供稳定 `workspaceId`、canonical root 和不含 secret value 的 capability/configuration 投影。
- report identity 已冻结为 `job:<jobId>` / `session:<sessionName>`，artifact identity 直接复用 `ManagedArtifactStore`。
- Application 已能查询 catalog、changes、report 和 artifact metadata；CLI `changes` 已通过真实 parity。
- Application 只单向依赖 Core/ProjectPacks；AppHost 只依赖 Application；public contract 不暴露 CLI、renderer 或 store 类型。
- Release build 为 `0 warnings / 0 errors`；标准并发 full suite 基线为连续两次 `1288/1288`。
- Desktop tests 基线为 5 files / `11/11`；Week 67 package 为 465,075,187 bytes，AppHost 为 79,113,224 bytes，3 秒 working set 为 406,507,520 bytes。

### 现有持久化权威

- `FileConversationStore` 保存 schema-v1 session transcript，已有 create/load/list/rename/delete 和单文件 atomic replace，但没有 revision、bounded read 或 thread identity。
- `JobRecordStore`、`TaskQueueStore`、`ManagedProjectPackRunStore` 和 `ManagedArtifactStore` 已分别拥有 job、queue、run/report/artifact truth。
- Project Pack run store 已验证 expected revision、exclusive mutation lock、atomic replace、strict schema、reparse denial 和 corrupt-state fail-closed，可作为新 store 的实现参考。
- `ConversationTranscript` 将 messages、tool calls、errors 和 agent runs 分开保存；旧记录不是完整有序 event log，迁移只能做显式、确定性的派生投影。

### 当前必须关闭的差距

- 尚无 thread/turn/timeline id、record、状态机、sequence 或 revision contract。
- 尚无 thread 专用 metadata store，也没有跨重启的 committed timeline 边界。
- session/job/queue/report/artifact 仍是分散 truth，尚无统一的 UI-safe timeline pointer projection。
- 旧 session list/show/rename/delete 直接操作 transcript；没有显式 migration preview、source fingerprint 或 rollback 语义。
- active turn 在 AppHost/进程退出后的恢复分类尚未冻结，不能把未知 `running` 猜成成功。
- Week 69 所需 `thread.list/get/create/...` protocol 尚无稳定 Application input。

## 范围冻结

### 本周必须交付

| 能力 | 本周输出 | 权威/依赖 |
|---|---|---|
| Thread record | identity、workspace binding、title、status、revision、turn refs、committed sequence、origin | 新增唯一 thread metadata store |
| Turn record | identity、thread binding、ordinal、status、revision、timestamps、stop/error、source pointers | thread store 下的 versioned immutable snapshot |
| Timeline item | stable id、thread/turn、sequence、timestamp、type、typed bounded payload、source pointer | immutable committed item |
| Lifecycle | create/list/get/rename/archive/delete、expected revision、explicit confirmation | Application service + thread store |
| Projection | session/job/queue/report/artifact pointer 到 UI-safe timeline | 复用 Week 67 queries 与既有 stores |
| Migration | preview/import/rollback-safe session projection、source fingerprint、idempotency | `FileConversationStore` + fake timeline projector |
| Recovery | active state 显式转为 failed/interrupted projection，不自动重放 | expected revision mutation |

### 本周明确不做

- 不新增 `thread.*`、`turn.*` 或 `timeline.*` desktop-v1 method，不修改 `protocol/desktop-v1/contract.json`；该范围属于 Week 69。
- 不修改 Electron Main、Preload、Renderer、bridge allowlist 或页面，不创建 mock UI truth；该范围属于 Week 69-71。
- 不启动模型、agent loop、tool、shell、MCP、Project Pack driver 或 write-capable turn。
- 不实现 approval request/resolve，不持久化 approval grant、token、override、raw tool arguments 或任意安全结论。
- 不迁移或重写 CLI `session` 命令，不改变其参数、输出、exit code 或底层 transcript 文件。
- 不复制 job、queue、session、report、trace、run 或 artifact 内容；thread 只保存稳定 pointer 和 UI-safe cache/projection。
- 不读取 artifact content，不重新计算 managed artifact hash，不扩大 ownership、verification 或 correctness 声明。
- 不自动扫描并导入全部旧 session；migration 必须由显式请求触发并可证明不修改 source。
- 不建设搜索索引、跨 workspace 任务中心、scheduler、worker lease、heartbeat 或多 write-worker 并发。

## 责任边界与目录布局

### 1. Ownership

- Core 新增 persisted records、validators、state transition table 和 `ThreadStore`；它负责 schema、路径、atomicity、revision、lock、corrupt diagnostics 和 source ownership。
- Application 新增 `ThreadApplicationService`、request/result DTO、timeline projector 和 migration use case；它负责 workspace binding、redaction、pagination、pointer hydration 和 cancellation。
- AppHost、CLI 和 Desktop 本周不得直接读取 thread store。AppHost 接入留到 Week 69；现有 CLI session path 保持不变。
- store entity 不直接作为 Application public DTO；public API 继续禁止 `TextWriter`、renderer、`ParseResult`、Electron 和 store 类型。

### 2. Store root

新 store 使用现有用户级状态根，即 `CliEnvironmentSnapshot.UserConfigPath` 所在 `.caicli` 根目录，不把 thread 写入 workspace：

```text
<state-root>/threads/
  <thread-id>/
    thread.json
    turns/
      <turn-id>.r<revision>.turn.json
    timeline/
      <sequence>-<item-id>.timeline.json
    .thread.lock
  .deleting-<thread-id>-<nonce>/
```

- `thread.json` 是 commit manifest；只有 manifest 引用的 turn revision 和 `committedSequence` 以内 timeline item 属于已提交状态。
- turn snapshot 和 timeline item 先写临时文件、flush 并原子 rename，最后 atomic replace `thread.json` 作为 commit point。
- manifest 替换前崩溃只会留下未提交 orphan；读取时不得把 orphan 当作成功事件，cleanup 只能在验证 root、ownership 和 reparse chain 后执行。
- manifest 已提交但引用文件缺失、重复 sequence、错误 thread/turn binding 或 hash/shape 不一致时返回 corrupt-state，不自动修复。
- store 路径和任意现存 ancestor 出现 symlink/reparse point 时 fail closed；不得递归操作 workspace 或 source session 路径。

## Persisted Contract

### 1. Identity 与 schema

- `ThreadRecord`、`TurnRecord`、`TimelineItemRecord` 分别使用独立 `CurrentSchemaVersion = 1`。
- thread、turn、item id 均使用类型前缀加 24 位 lowercase hex：`thread_<24hex>`、`turn_<24hex>`、`item_<24hex>`；路径只由验证后的 id 构造。
- import thread id 由 source kind + canonical session identity + source fingerprint 确定性生成；普通 create 使用 CSPRNG。
- 所有时间使用 UTC `DateTimeOffset`；created/started/completed/updated 必须满足单调关系。
- JSON 使用 strict unmapped-member rejection、最大 depth 和 UTF-8 byte bound；不接受任意 extension data。

### 2. Thread record

Thread manifest 只保存：

- `schemaVersion`、`threadId`、`revision`、`workspaceId`、canonical workspace root identity。
- bounded title、status、created/updated/archived timestamp。
- ordered turn refs：turn id、ordinal、current turn revision、status、source pointer 摘要。
- `committedSequence`、timeline item count、persisted byte count 和当前 active turn id。
- optional origin：`native` 或 `session-import`，以及已脱敏 source id/fingerprint；不保存 source 内容。
- redaction/persistence policy 摘要，明确 raw secrets、approval、artifact content 和 full command output 均未存储。

thread revision 从 0 开始；每个成功 mutation 只增加 1。失败、重复幂等请求或 validation error 不增加 revision。

### 3. Turn record

Turn snapshot 只保存：

- schema、turn/thread id、ordinal、revision、status、created/started/completed timestamp。
- bounded task summary，不保存未脱敏 prompt；完整对话仍由 source session transcript 决定。
- stop reason、stable error code、mode projection 和 source correlation。
- session/job/queue/report/trace/run/artifact typed pointers；每个 pointer 保留 source id、可选 source revision/hash 和 availability snapshot。
- timeline first/last sequence 与 item count，不重复保存 timeline payload。

同一 thread 最多一个非终态 turn。turn snapshot 文件不可原地覆盖；新 revision 写新文件，manifest commit 后旧 revision 才可 best-effort 清理。

### 4. Timeline item

所有 item 共享 envelope：

- `schemaVersion`、`itemId`、`threadId`、`turnId`、`sequence`、`timestampUtc`、`type`。
- `source` typed pointer、`status`、bounded summary、typed payload、redaction metadata。
- 禁止任意 JSON 字符串、raw exception、stack trace、raw command stdout/stderr、tool arguments、secret、approval grant 和 artifact content。

冻结 item type allowlist：

- `user.message`、`assistant.message`、`plan.updated`。
- `tool.started`、`tool.completed`、`command.started`、`command.completed`。
- `approval.requested`、`approval.resolved`。
- `changes.updated`、`report.available`、`artifact.available`、`warning.raised`、`turn.completed`。

Week 68 只由 fake/session/pointer projector 生成只读 item；live tool、approval、command streaming 留到 Week 73。未实现的 live producer 不得伪造成功事件。

### 5. Sequence 与幂等

- sequence 从 1 开始，在 thread 内严格递增且连续；manifest 保存最后 committed sequence。
- append request 必须携带 expected thread revision、expected next sequence 和 stable mutation id。
- 相同 mutation id + 相同 canonical payload 重试返回原结果，不追加、不增加 revision。
- 相同 item/mutation id 但 payload 不同返回 conflict；gap、倒序、重复 sequence 或跨 thread/turn item 均拒绝。
- list/get projection 按 sequence 返回，支持 `afterSequence` cursor；Renderer/Week 69 consumer 可按 sequence 去重，不能只依赖通知到达顺序。

## 状态机

### Thread states

冻结为 framework 已定义的状态：

- `idle`、`running`、`waiting-for-approval`、`failed`、`completed`、`archived`。

核心转换：

- create -> `idle`。
- `idle|completed|failed` -> `running`，仅当不存在其他 active turn。
- `running` -> `waiting-for-approval|failed|completed`。
- `waiting-for-approval` -> `running|failed`。
- `idle|completed|failed` -> `archived`；active thread 不可 archive。
- `archived` 本周不可自动恢复或启动 turn；unarchive 留待后续显式设计。

Thread status 由 active/latest turn 和 archive 状态派生；Application caller 不能任意写入 status。

### Turn states

冻结为 framework 已定义的状态：

- `queued`、`running`、`waiting-for-approval`、`canceling`、`canceled`、`failed`、`completed`。

核心转换：

- create -> `queued`。
- `queued` -> `running|canceled`。
- `running` -> `waiting-for-approval|canceling|failed|completed`。
- `waiting-for-approval` -> `running|canceling|failed`。
- `canceling` -> `canceled|failed`。
- `canceled|failed|completed` 为终态，不允许回退。

Week 68 只测试和持久化转换，不执行真实 turn。approval 状态只是一项 projection，不保存 grant。

### Restart/recovery

- 读取 active `running`、`waiting-for-approval` 或 `canceling` 状态时不得猜测成功，也不得自动调用 worker、model 或 tool。
- store 先返回 `recovery-required` diagnostic；显式 `RecoverInterrupted` 必须携带 expected revision。
- recovery 把 active turn 转为 `failed`，stable stop reason 为 `interrupted`，thread 转为 `failed`，追加 `warning.raised`/terminal projection。
- recovery 不修改 source session/job/queue/run，不重放命令，不恢复 approval，并保持已 committed timeline 不变。

## Application Use Cases

### Thread create/list/get

- `Create` 接受已验证 workspace snapshot、bounded title 和 cancellation token；同一 canonical workspace 的 identity 必须稳定。
- `List` 必须显式按 workspace id 过滤，默认 page 50、最大 200，按 updated desc + thread id 稳定排序；corrupt thread 作为 diagnostics，不隐藏有效记录。
- `Get` 返回 thread metadata、turn summaries 和 timeline page；timeline 默认 50、最大 100，返回 `nextSequence`/`truncated`。
- Application projection 重新使用 Week 67 redaction/diagnostic/aggregate limits，单次结果目标不超过 768 KiB。

### Rename/archive/delete

- `Rename` 需要 expected revision；title 非空，去除首尾空白，最大 512 UTF-8 bytes，redaction 后再计数。
- `Archive` 需要 expected revision；只允许无 active turn 的 `idle|completed|failed` thread。
- `Delete` 只允许 archived thread，必须同时提供 expected revision 和与 thread id 完全相同的 confirmation value。
- delete 在 exclusive lock 下重新读取 revision，验证 thread-owned root 后把目录原子 move 到 `.deleting-*` quarantine，再删除。
- delete 只删除 thread manifest、turn snapshots、timeline items 和 lock；绝不删除 session、job、queue、report、trace、run、artifact 或 workspace 文件。
- revision race、open handle、reparse、partial cleanup 返回稳定 conflict/denied/unavailable diagnostic；quarantine cleanup 可重试但不能扩大路径范围。

### Pointer hydration

- timeline source pointer 只接受 `session`、`job`、`queue`、`report`、`trace`、`run`、`artifact` allowlist。
- Application 通过既有 store/Week 67 services 读取当前 metadata；persisted status 只是带 source identity 的 cache，不是第二套 correctness truth。
- missing/corrupt/stale source 保留 timeline item 和 stable pointer，同时返回 diagnostic/availability；不能把 missing 当 empty，也不能删除历史 item。
- artifact pointer 只返回 metadata；不打开内容、不 verify、不 preview、不重新 hash。

## Boundaries

| 项目 | 默认/上限 | 规则 |
|---|---:|---|
| thread list page | 默认 50，最大 200 | 读取 `limit + 1` 判断 truncated |
| timeline page | 默认 50，最大 100 | `afterSequence` cursor；aggregate budget 可提前截断 |
| title | 最大 512 bytes UTF-8 | 先脱敏/normalize，再检查边界 |
| thread manifest | 最大 1 MiB | 超限为 corrupt/limit error，不部分反序列化 |
| turn snapshot | 最大 128 KiB | 只含 metadata 与 pointers |
| timeline item | 最大 16 KiB | typed payload；不保存 raw output/content |
| turns per thread | 最大 1,000 | 超限拒绝新 turn，不丢历史 |
| timeline items | 最大 10,000 | 同时受 thread persisted bytes 64 MiB 上限约束 |
| migration transcript | 最大 4 MiB / 10,000 source records | 在 `FileConversationStore` 权威读取入口执行 bound |
| source pointers per turn | 每类最大 200 | stable order + truncated diagnostic |
| diagnostics | 复用 Week 67：100 x 4 KiB | 先脱敏再截断 |
| Application result | 目标不超过 768 KiB | 为 Week 69 1 MiB protocol envelope 留余量 |

任何 existing loader 若只能无界读取，必须在权威 Core store 增加 bounded API；不得在读取完整无界文件后只截断 Application DTO。

## Session Migration

### Preview

- `PreviewSessionImport` 只处理调用方显式指定的 validated session name，不扫描或自动导入全部历史。
- 先使用 bounded strict loader 验证 schema/path binding，再计算 source fingerprint：source kind + canonical session id + bounded content SHA256。
- preview 返回 projected thread title、turn/item count、source identity、warnings、truncation 和 unsupported fields，不写任何文件。
- corrupt、unsupported、oversize、changed-during-read 或 reparse source 返回稳定 diagnostic，不生成 partial thread。

### Import

- `ImportSession` 必须携带 preview fingerprint；import 前重新读取并比较 fingerprint，避免 preview/import race。
- 同一 source identity + fingerprint 重试返回既有 thread，保证幂等；同一 session name 但内容已变化返回 conflict，不静默覆盖。
- import 只创建 thread-owned files；source transcript 的 bytes、timestamp、name 和 path 必须保持不变。
- `ConversationTranscript` 的 messages/tool calls/errors/agent runs 按 timestamp、source kind priority、original index 做稳定合并；sequence 由 projector 重新连续分配。
- item id 由 source fingerprint + source collection kind + source index 确定性生成；raw tool arguments、secret、approval material 和 full output 不进入 item。

### Rollback

- migration origin 保留 source pointer/fingerprint；删除或 rollback imported thread 只走正常 archive + confirmed delete。
- rollback 不删除、rename 或 rewrite source session；重新 preview 同一 source 仍可得到相同 fingerprint。
- 本周不把 imported thread 反向导出为 session，也不修改 CLI session lifecycle。

## Fake Timeline Projector

- 建立 typed fake source fixture，覆盖 user message、assistant summary、plan、tool/command status、changes、report、artifact、warning 和 turn completed。
- 同一 fixture 必须生成 byte-stable item type/order/id/sequence/source pointer；输入顺序变化不能改变 canonical 结果。
- projector 复用 `DiagnosticSecretRedactor`，先脱敏再执行 item/aggregate bounds；不接受调用方传入任意 JSON payload。
- fake runtime 只用于 contract/store tests 和 Week 69 protocol input，不进入 production Desktop 作为 mock truth。

## Error Contract

优先复用 Week 67 category，并冻结 thread-specific stable code：

- validation：`thread-id-invalid`、`turn-id-invalid`、`timeline-sequence-invalid`、`thread-title-invalid`、`thread-confirmation-invalid`。
- not-found：`thread-not-found`、`turn-not-found`。
- conflict：`thread-revision-conflict`、`thread-active-turn-conflict`、`timeline-append-conflict`、`session-import-source-changed`。
- denied：`thread-path-unsafe`、`thread-reparse-point`、`thread-delete-not-archived`、`thread-archive-active`。
- corrupt-state：`thread-record-corrupt`、`thread-schema-unsupported`、`turn-record-corrupt`、`timeline-record-corrupt`、`thread-reference-missing`。
- limit-exceeded：`thread-limit-exceeded`、`turn-limit-exceeded`、`timeline-limit-exceeded`、`session-import-limit-exceeded`。
- unavailable：`thread-store-unavailable`、`thread-write-failed`、`thread-delete-failed`。

expected domain failure 返回结构化 error/diagnostic；unexpected exception 留给进程顶层转换为安全 internal error。exception message、type、stack 和路径原文不得进入 contract。

## Architecture Tests

新增自动约束：

- Thread persisted records/store 位于 Core；Application public contract 不暴露 store entity、filesystem handle、JSON document 或 renderer。
- Application 仍只依赖 Core/ProjectPacks；Core 不引用 Application、CLI、AppHost 或 Desktop。
- AppHost 本周不新增 thread protocol dispatch；`protocol/desktop-v1/contract.json` 和 generated TS/C# contracts 保持 drift-free。
- CLI session handler 继续使用原路径，本周不解析 thread output，也不形成双写。
- Desktop Main/Preload/Renderer 不读取 `.caicli/threads`、session/job/queue/run/artifact store。
- 新 store 不包含 job/queue/session/report/artifact schema 副本；source pointers 只能引用 stable identity。
- production Application 不引用 fake projector fixture 或 Desktop mock data。

## 执行任务清单

- [x] Step 1：记录 Week 68 起点 commit、branch、dirty state、SDK/Node/npm、Week 67 test/package/process 基线。
- [x] Step 2：评审并冻结 thread/turn/item schema、id、state transition、sequence、revision、limits 和 error code。
- [x] Step 3：实现 Core persisted contracts、strict validators、JSON bounds 和 state transition tests。
- [x] Step 4：实现 manifest commit、immutable turn/item write、exclusive lock、expected revision 和 orphan handling。
- [x] Step 5：实现 thread create/read/list、stable ordering、partial corrupt diagnostics 和 cancellation boundary。
- [x] Step 6：实现 rename/archive/delete，覆盖 confirmation、revision race、quarantine、reparse 和 source preservation。
- [x] Step 7：实现 turn snapshot mutation、单 active turn、terminal immutability 和 interrupted recovery。
- [x] Step 8：实现 timeline append/dedupe/cursor pagination，覆盖 duplicate、gap、out-of-order 和 aggregate truncation。
- [x] Step 9：实现 Application thread create/list/get/rename/archive/delete DTO 与 pointer hydration。
- [x] Step 10：实现 deterministic typed timeline projector 及 byte-stable/redaction/typed payload tests。
- [x] Step 11：为 `FileConversationStore` 增加 authoritative bounded read/fingerprint 入口，保持 CLI session behavior 不变。
- [x] Step 12：实现 session migration preview/import/idempotency/source-changed/rollback-safe tests。
- [x] Step 13：加入 architecture、crash/orphan、corrupt/oversize/reparse/concurrency 和 no-second-truth tests。
- [ ] Step 14：定向测试、Release build、标准并发 full suite 两次与 CLI smoke 已完成；clean-source release validation 待提交后补跑。
- [x] Step 15：运行 Desktop verify、AppHost publish/package、双 packaged smoke、orphan 和同口径 package/process/memory 采样。
- [x] Step 16：执行 `git diff --check`、文档链接检查，并创建 `68_week_review.md`，记录 Week 69 输入。

## 建议日程

| 工作日 | 重点 | 当日退出条件 |
|---|---|---|
| Day 1 | schema、state、layout、limits、architecture skeleton | contract review 完成；非法依赖和状态转换可自动失败 |
| Day 2 | ThreadStore atomicity、revision、CRUD | create/read/list/rename/archive/delete 与 race/corrupt tests 通过 |
| Day 3 | turn/timeline commit、sequence、recovery | duplicate/out-of-order/crash/orphan/restart tests 通过 |
| Day 4 | Application projection、pointer hydration、session migration | deterministic projection、source identity、rollback-safe tests 通过 |
| Day 5 | bounds/redaction hardening、全量回归、package/process、review | 所有 Week 68 Gate 形成可重复证据 |

任何 Day 2 atomic/revision Gate 未通过时，不进入 migration；任何 Day 3 sequence/recovery Gate 未通过时，不把 contract 交给 Week 69 protocol。禁止用 AppHost 私有 store、Renderer local storage 或 fake UI state 绕过。

## 测试矩阵

| 范围 | 必测路径 |
|---|---|
| Contract | id/schema/unknown fields/UTF-8 bounds/timestamps/state transitions/source pointer allowlist |
| Store | empty/create/read/list/atomic replace/expected revision/lock/temp cleanup/reparse/path containment |
| Thread lifecycle | rename/archive/active denial/delete confirmation/delete race/quarantine/source preservation |
| Turn | ordinal/single active/valid transitions/terminal immutable/revision conflict/interrupted recovery |
| Timeline | stable id/sequence/dedupe/gap/out-of-order/cursor/truncated/duplicate payload conflict |
| Corruption | missing manifest/unsupported schema/oversize/missing committed child/binding mismatch/orphan file |
| Projection | deterministic order/redaction/typed payload/missing-corrupt-stale pointers/aggregate 768 KiB |
| Migration | preview/import/idempotent retry/source changed/corrupt/oversize/reparse/source byte identity/rollback |
| Cancellation | pre-I/O/between files/during projection/no partial committed success |
| Architecture | dependency direction/no protocol drift/no direct Desktop store/no CLI dual-write/no schema copy |
| Regression | .NET full suite/CLI smoke/Desktop verify/AppHost publish/package/双 smoke/orphan/performance |

## 验证命令

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build --filter "FullyQualifiedName~Thread|FullyQualifiedName~Turn|FullyQualifiedName~Timeline|FullyQualifiedName~Migration|FullyQualifiedName~Architecture"
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build

powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
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

定向 filter 必须记录实际测试数；0 tests 不能作为通过证据。clean-source release build 必须在提交后运行，不得用 `-AllowDirtySource` 产物声称 acceptance。

## 周末 Gate

Week 68 只有同时满足以下条件才能标记 Passed：

1. thread/turn/timeline schema、id、status、sequence、revision、limits 和 stable error code 已冻结并有 contract tests。
2. manifest 是唯一 commit point；atomic replace、exclusive lock、expected revision 和 crash/orphan 语义有自动化证据。
3. duplicate、gap、out-of-order、revision conflict、missing child、corrupt、unsupported、oversize 和 reparse 均 fail closed。
4. Thread Application create/list/get/rename/archive/delete 返回 bounded、redacted、可取消 DTO，不暴露 store entity。
5. delete 只处理 archived thread-owned files，要求 confirmation + expected revision，不删除任何 source truth。
6. active turn 重启后只能显式恢复为 failed/interrupted projection；不猜成功、不重放、不恢复 approval。
7. session migration 必须显式、确定、幂等、source fingerprint protected 且 rollback-safe；source bytes/path/timestamp 不变。
8. job/queue/session/report/run/artifact 仍是唯一 truth；timeline 只保存 typed pointer 和 UI-safe projection。
9. Application/Core/AppHost/CLI/Desktop architecture tests通过，desktop-v1 contract 无 drift，CLI session behavior 无回归。
10. Release build 为 0 warning / 0 error；标准并发 full suite 连续两次通过；CLI 与 Desktop smoke 通过。
11. packaged headless/window-close 的 AppHost orphan delta 均为 0；package/AppHost/working set 增长超过 Week 67 基线 15% 时已有原因和处置结论。
12. `68_week_review.md` 记录 source、schema、layout、atomicity、revision、migration、bounds、cleanup、测试计数和 Week 69 输入。

若第 2、3、5、6、7 或 8 项失败，Week 69 不得通过 AppHost 私有 schema、自动修复、Renderer local storage、协议 mock data 或 source copy 绕过 Gate。

## 风险与回退策略

| 风险 | 处理 |
|---|---|
| 多文件 commit 在崩溃时产生半状态 | immutable child 先写、manifest 后提交；未提交 orphan 不可见且可安全清理 |
| thread metadata 逐渐复制 source truth | persisted field allowlist + pointer hydration + architecture/schema tests |
| timeline 单文件无限增长 | immutable item、cursor page、item/thread byte caps，不把完整 timeline 塞入 manifest |
| rename/archive/delete 与并发 update 竞态 | exclusive lock + expected revision + commit point re-read |
| delete 误删 source/store/workspace | archived-only、exact confirmation、thread-root quarantine、reparse/path containment tests |
| session 四类集合无法恢复真实因果顺序 | 明确它是派生 projection；timestamp + kind + original index 稳定排序，不宣称原始 event log |
| migration 重复或 source 改变 | content fingerprint + deterministic origin + preview/import recheck + conflict |
| active state 重启后被误判成功 | explicit recovery-required；failed/interrupted terminal projection，不自动 replay |
| typed timeline payload 过早冻结 live execution 细节 | 冻结 envelope、item type 和安全边界；Week 73 producer 只能在兼容字段内扩展 reviewed schema |
| 新 store 增大 AppHost/package/memory | 同口径 package/process sample；超过 15% 必须调查，不通过放宽限制解决 |

回退单位按 store/use case 分层：先保留已验证 persisted contracts/store，再回退未通过的 Application projection 或 migration。任何 migration 未通过 source preservation 时必须整体禁用 import，不得保留部分写入或自动 fallback。

## Week 69 输入

通过 Gate 后，Week 69 只接收以下稳定输入：

- versioned thread/turn/timeline persisted contract、id、state、sequence、revision 和 error code。
- 以 manifest 为 commit point 的 store、expected revision、crash/orphan/corrupt/reparse 行为。
- bounded/cursor-based Thread Application create/list/get/rename/archive/delete use cases。
- typed timeline item envelope、source pointer hydration、redaction 和 aggregate limits。
- explicit session migration preview/import 与 deterministic fake timeline fixtures。
- active state interrupted recovery semantics，以及不持久化 approval/no replay 结论。
- architecture tests、连续 full suite、CLI smoke 和 Desktop process/package comparison baseline。

Week 69 protocol 只能序列化这些 Application DTO，不得直接暴露 store record、文件路径布局、CLI renderer 文本或未审查的任意 JSON payload。
