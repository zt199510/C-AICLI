# Week 79 执行计划：Desktop 生产真实模型 Runtime、持久化审批与 Preview Gate 续跑

状态：Ready

创建日期：2026-07-25

所属阶段：Week 78 `Blocked` 后的单一缺陷修复与真实场景续跑

起点提交：`10d13791b070580eb74f3d6223d6e8cc49f41f2e`

执行分支：`week-02-cli-commands-doctor-config`

## Goal

在不改变 `desktop-v1` 协议面、不放宽 workspace guard/approval/redaction/cleanup Gate、也不重新开启 0.6.0 正式发布验收的前提下，把 Desktop AppHost 从生产默认的 `DeterministicFakeTurnExecutionRuntime` 切换到可调用现有 Responses agent path 的真实生产 runtime，并完成：

1. 模型、工具、验证和 final 事件实时、单调、可恢复地写入权威 ThreadStore timeline。
2. write/shell 工具通过现有 Desktop durable approval request/resolve 流逐动作请求人工批准。
3. 缺少模型、凭据、endpoint、审批、上下文或 runtime 能力时安全失败，不退回 fake、不自动 replay。
4. 从新的 clean revision 重跑 Week 78 被阻断的 Desktop real-model read-only、受控写入、crash/restart 和资源观测。

Week 79 只允许以下最终结论：

- `Preview Ready`：W79-G0 至 G9 全部关闭，真实 Desktop 只读、写入、恢复和资源 Gate 通过，无开放 P0/P1；只允许继续内部 Preview。
- `Blocked`：任一硬 Gate 未关闭；保留首败和 evidence，不推广 Preview。

即使 Week 79 为 `Preview Ready`，0.6.0 正式 release 仍保持 Week 77 的 `Blocked`。不得使用 `Accepted`、`Released`、`Production Ready` 描述 Desktop，也不得创建 tag、上传制品或发布 Release。

## 创建基线

- Week 78 closeout HEAD 为 `10d13791b070580eb74f3d6223d6e8cc49f41f2e`，创建计划前工作树 clean。
- Week 78 试用源码 revision 为 `fa13cb12d246476bbca07f2f41d11f4cae68d48f`。
- Week 78 CLI real-model read-only：Passed；只调用一次 `workspace.read_text`，approval 为 `never`，workspace/process delta 为 0。
- Week 78 filesystem stdio MCP：`@modelcontextprotocol/server-filesystem@2026.7.10` Passed；读取、越界拒绝、unknown-tool、配置回滚和 process cleanup 均通过。
- Week 78 Desktop real-model：Failed。`DesktopWriteExecutionSupervisor` 的生产默认构造直接创建 `DeterministicFakeTurnExecutionRuntime`，没有真实生产 `ITurnExecutionRuntime`。
- 因 Desktop real-model Gate 失败，Week 78 受控写入和真实模型 crash/restart 正确保持 Blocked/NotRun。
- 最终确定性基线：.NET `1403/1403`、Desktop `23 files / 102 tests`、unpacked E2E `9/9`、packaged E2E `8/8`、accessibility `2/2`、packaged smoke `8/8`、protocol `3/3`、`npm audit` 0 vulnerability。
- Week 78 最终 package SHA-256 为 `E5A5EA65B5F593D0A1CD03EC29CDDE220C14A0E79D69BACC3B06C097D73C2EEC`；AppHost SHA-256 为 `D6D005B4362861462A156D22B80B9367BDF32074910D964D5B8573F3F71DBE66`。Week 79 修改 AppHost 后这两个 identity 立即失效，只可作为比较基线。
- Reviewed Desktop surface 继续冻结为 exact `39 invoke + 2 event`；默认不修改 contract method/event、capability、limits 或 payload shape。

## 核心设计决定

### 1. Production composition 不得隐式使用 fake

- `DeterministicFakeTurnExecutionRuntime` 保留为 deterministic test fixture，只能通过测试或显式依赖注入使用。
- AppHost 默认 production composition 必须创建真实 runtime factory；缺少配置时返回安全、可复核的 terminal failure。
- 不允许因缺少 key、model、gateway 或工具而 fallback 到 fake。
- 增加 composition 回归，直接断言生产默认 runtime 不是 fake，并断言配置失败不会产生 fake success timeline。

### 2. 不让 AppHost 依赖 CLI

- 将 `CliToolFactory` 中通用 built-in tool registry 构造能力提取到 Core 的共享 factory；CLI 仅保留命令层适配。
- 将 CLI 现有 model/key/base URL 校验和 `OpenAiAgentRunner` 创建逻辑提取为 Core 可复用的 agent factory/result，不复制第二套 provider 规则。
- Application 继续只定义执行契约和持久化语义；AppHost 负责 production composition、Desktop event/approval 适配。
- 不增加 Application -> CLI 或 AppHost -> CLI project reference。

### 3. Timeline 必须在运行中 durable，不得结束后批量伪装实时

- 为 agent loop 增加可选、无 UI 依赖的事件观察接口；CLI 未注册观察器时行为与输出保持不变。
- production runtime 使用容量受限的单生产者 channel 把 `AgentRunEvent` 桥接到 `ITurnExecutionEventSink`。
- channel 容量不得超过现有 `TurnExecutionLimits.MaxEventBatchItems`；满载时施加 backpressure，不丢事件、不无界缓存、不 fire-and-forget。
- agent 在专用 worker 上运行；event consumer 逐项等待 ThreadStore commit。任何持久化失败必须取消 agent 并使 turn fail closed。
- 每个 runtime event 使用当前 turn 内从 1 开始的严格单调 sequence；重复、跳号、乱序和跨 turn identity 必须被 deterministic tests 拒绝。
- 在首个 model/tool event commit 后终止 AppHost时，重启必须能够看到 durable partial timeline，不得推断 completed。

### 4. Desktop write/shell 永远逐动作持久化审批

- 新增 Desktop approval policy adapter，把 Core `ApprovalRequest` 映射到 Application `InteractiveApprovalAction`，再经 `IInteractiveApprovalGateway` 等待现有 `approval.resolve`。
- read 工具自动标记 `not-required`；dangerous shell 始终拒绝。
- write/shell 在 Desktop 中不得因配置为 `always` 而静默自动批准；必须产生 durable、用户可判断的单动作审批。
- safe summary 只包含操作类别、相对目标类别、dirty 状态、risk 和 canonical action hash；不得包含 secret、完整 environment、原始工具参数、完整 diff 或本机绝对路径。
- deny、过期、stale revision、duplicate decision、cancel 和 disconnect 必须释放 waiter 并形成确定性终态。
- approval denial 对 agent retry policy 为 terminal；不得改写参数后自动重试，不得重用 request identity。

### 5. 生产边界保持 bounded

- Week 79 Desktop runtime 仅注册现有 built-in tools；默认关闭 MCP discovery，避免把 Week 79 扩展成模型 + MCP 组合试用。
- 每个 turn 使用固定上限：model turns、tool calls、retry、总时长、事件数量和单项摘要长度均不得高于现有 CLI/Application hard limits。
- `CliEnvironmentSnapshot` 只在进程内传递，不进入 ThreadStore、timeline、report、trace payload 或 JSON evidence。
- workspace config 中的 API key 继续不得用于模型请求；只允许现有受支持来源。
- provider status、timeout 和错误只映射为安全类别；不保存 raw request/response、header、endpoint query 或第三方响应正文。

## 明确不做

- 不新增 provider、provider routing、MCP transport、远程 API、localhost 控制面或 generic IPC。
- 不修改 `desktop-v1`，除非 deterministic 实现证明现有契约无法表达安全终态；若发生该情况，先停工并形成独立变更提案。
- 不把 agent terminal 与 user terminal 合并，不给模型任意 shell/process/file authority。
- 不增加后台 scheduler、并发 write worker、自动 restart、自动 replay 或远端任务恢复推断。
- 不重做 Week 77 Narrator，不生成 RC，不改变正式 release 状态。
- 不把 Week 78 已通过的 MCP 场景与 Week 79 Desktop 模型场景组合运行。
- 不读取或复用既有 ignored dotenv，直到用户对 Week 79 模型、endpoint、凭据使用范围和 disposable workspace 重新明确授权。

## Evidence 目录

执行开始时创建 ignored 目录：

```text
artifacts/week79-desktop-real-model/
  manifest.json
  architecture.json
  credential-free.json
  real-model-readonly.json
  real-model-write.json
  recovery.json
  resource-summary.json
  final-summary.json
```

所有文件至少记录 schema version、source revision、source dirty、package/AppHost identity、状态、脱敏 checks、计数、耗时和 cleanup delta。禁止字段包括 secret/token/API key、raw prompt/response、完整 command environment、绝对路径、raw diagnostics 和未脱敏 provider/MCP stderr。

## Phase 0：Week 78 收尾与 Week 79 基线冻结

1. 将 Week 78 计划状态更新为 `Completed / Blocked`，不改写其首败和最终结论。
2. 在 roadmap 与 12 周历史排期中链接 Week 79，明确它是 post-cycle defect fix，不是 release acceptance。
3. 创建 Week 79 manifest/evidence validator，冻结 workspace、工具、审批、停止条件和 cleanup 采样。
4. 运行 secret/path schema 自检；确认 ignored dotenv 不被 Git 跟踪。
5. 记录当前 source、branch、dirty、SDK、Node/npm/Electron 和 package/AppHost 比较 identity。

Phase 0 Gate：Week 78 历史状态一致；Week 79 边界可机器检查；未读取凭据、未运行真实模型、未修改 disposable project。

## Phase 1：Production Runtime Composition 与共享 Factory

1. 提取 Core 共享 tool registry factory，保持 CLI registry 内容、disabled tools、shell policy、workspace guard 和 tool boundary 语义不变。
2. 提取 Core 共享 OpenAI agent factory，统一 model、key source、base URL 和 gateway 创建错误。
3. 为 `TurnExecutionInput` 或 runtime factory 增加只在内存存在的 `CliEnvironmentSnapshot` 入口；不得改变持久化 record。
4. 新增 production `DesktopAgentTurnExecutionRuntime`，由 AppHost 默认 composition 注入。
5. `DesktopWriteExecutionSupervisor` 删除 `runtime ?? new DeterministicFakeTurnExecutionRuntime()` 生产 fallback；测试显式传 fake。
6. 缺少 model/key、key source 不允许、endpoint 不可用和 agent factory exception 分别映射为安全 terminal result。

最低 deterministic tests：

- production composition 不是 fake；
- fake 只能显式注入；
- missing model/key 不产生 success；
- workspace key 不被使用；
- CLI 与 Desktop factory 对相同 snapshot 给出一致配置判断；
- Desktop 默认不发现 MCP；
- cancellation/dispose 不留下 runtime worker。

Phase 1 Gate：AppHost 默认 turn 已接真实 production runtime；无 CLI 反向依赖；无 fake fallback；所有配置错误 fail closed。

## Phase 2：实时 Agent Event 到 Durable Timeline

实现并验证以下映射：

| Agent/runtime 事件 | Durable timeline |
|---|---|
| startup/plan | `plan.updated` |
| model progress/safe assistant summary | `assistant.message` |
| tool call | `tool.started` |
| tool result | `tool.completed` |
| command start/result | `command.started` / `command.completed` |
| changed files | `changes.updated` |
| verification | `verification.completed` |
| final | `assistant.final` |
| safe warning/error | `warning.raised`，随后由 turn terminal record 收口 |

实现要求：

- tool arguments、tool output 和 model text只允许 bounded safe summary；source pointer 只能是受控相对 identity。
- 相同 agent event 不得产生两个 timeline item。
- final event 与 turn completion 各司其职；没有 final 时不得伪造 assistant success。
- channel producer、consumer、AppHost shutdown 与 turn cancellation 必须共同遵守 bounded deadline。
- sink commit 失败、channel closed、observer exception 和 cancellation race 都要有 deterministic test。

Phase 2 Gate：运行中 timeline 可观察、可持久化、严格有序；故障时没有丢失后仍宣称 completed 的路径。

## Phase 3：Desktop Durable Approval Bridge

1. 将 Core approval request 转换为现有 durable approval action。
2. write/shell 调用在工具执行前持久化 `approval.requested` 并暂停 agent worker。
3. Renderer 通过既有 `approval.resolve` 提交 allow/deny；不新增 IPC。
4. allow 后只释放当前 canonical action 一次；参数或 workspace revision 改变必须重新申请。
5. deny/expire/cancel/disconnect 后工具不得执行，turn 不自动 replay。
6. 重启后旧 approval 不得被新 attempt 复用；旧 waiter 必须已释放。

最低 deterministic tests：

- read 不请求审批；
- write/shell 各请求一次且执行前 durable；
- dangerous shell 永远拒绝；
- allow、deny、stale、duplicate、expire、cancel、disconnect；
- canonical hash 对同一动作稳定，对参数/target/revision 变化不同；
- safe summary 不含绝对路径、key、environment 或 raw diff；
- denial 不触发自动 retry。

Phase 3 Gate：没有 approval bypass、重复执行、悬空 waiter、identity reuse 或敏感信息投影。

## Phase 4：Runtime 终态、报告与 Changes 一致性

1. 把 `AgentRunResult` 成功、失败、timeout、cancel、approval denial 和 provider error 映射为 `TurnRuntimeResult`。
2. final summary 只使用 bounded sanitized text；失败时保留 safe error code 与 stop reason。
3. changed files 和 verification event 与实际工具结果一致；禁止仅根据模型文案生成 Changes。
4. Reports/Changes 继续从权威 Application 服务读取，不从 Renderer 本地推断。
5. 同一 client mutation id 保持 idempotent；AppHost 重连不得自动再次启动 turn。
6. 真实 provider request 已发送但本地未知时只记录 interrupted/unknown，不推断远端是否完成或计费。

Phase 4 Gate：timeline、turn status、Reports、Changes 与磁盘事实一致；所有未知状态 fail closed。

## Phase 5：Credential-free 完整回归与最终 Package

先运行定向测试，再运行完整 Gate：

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test src\CSharpAiCli.sln -c Release --no-restore

Set-Location apps\desktop
npm audit --audit-level=high
npm run verify
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
npm run measure:accessibility
npm run measure:smoke
npm run measure:protocol
```

硬要求：

- .NET 不得低于 `1403/1403`，新增测试全部通过，0 skipped。
- Desktop 原有 `23 files / 102 tests` 不得减少；新增 composition/runtime/approval tests 必须计入新总数。
- unpacked `9/9`、packaged `8/8`、accessibility `2/2`、packaged smoke `8/8`、protocol `3/3` 不得回归。
- `npm audit` high/critical 为 0；package forbidden payload 为 0。
- 所有 test-owned process/temp delta 为 0。
- 重新计算 package/AppHost SHA-256；Week 78 package identity 标记为 superseded。

新增 production composition E2E 必须证明：无模型配置时 UI 得到安全失败，而不是 fake plan/tool/final success。

Phase 5 Gate：credential-free 全矩阵通过；生产包内没有 fake default、测试 fixture、secret 或未审核入口。

## Phase 6：Desktop Real-model Read-only

只在用户重新明确授权 Week 79 的模型标识、endpoint、凭据使用范围和 disposable project A 后执行。不得因 Week 78 曾授权而自动复用。

执行边界：

- 从 Phase 5 clean revision 新建 project A disposable clone 和全新 thread。
- 临时 workspace config 仅允许 `workspace.read_text` 与 `workspace.search_text`；禁用 plan、write、shell、git write 和 MCP discovery。
- 任务预声明一个可机器复核的文件、符号和事实；模型必须通过只读工具定位并解释。
- approval 为 never；max turns/tool calls/timeout 固定。
- 通过 packaged Desktop/AppHost 发起，不以 CLI 成功代替。
- Renderer timeline、thread terminal、Reports 和 Changes 与 AppHost/ThreadStore 一致；Changes clean。
- 保存模型/tool 次数、safe error category、首响应/首 tool/终态耗时和 cleanup delta，不保存原文。

Phase 6 Gate：真实 Desktop provider request、只读 tool call、durable timeline、clean Changes 和 cleanup 全部 Passed；任何 fake fallback、写请求、路径/secret 泄漏或 orphan 为 P0。

## Phase 7：真实模型受控写入

仅在 Phase 5、6 全部通过后，在 project B 全新 disposable clone 执行。

执行前冻结：

- 允许文件最多 2 个，默认沿用：
  - `src/CSharpAiCli.Core/Diagnostics/LogPathResolver.cs`
  - `src/CSharpAiCli.Tests/LogPathResolverTests.cs`
- 最大 diff 160 行；禁止 solution/package/runtime/protocol/release/security 文件。
- 预先构造或选择一个确定性失败测试，并记录 baseline failure。
- 允许的唯一 shell command 为精确的目标 test filter；不得使用通用 shell。
- 每个 apply_patch 和 test command 都必须分别出现 durable approval，操作者逐项 allow/deny。

验收：

- 模型修改仅落在 allowlist；
- timeline 的 tool/approval/verification/changes 与实际 Git diff 一致；
- 独立于模型运行目标测试并通过；
- 无生成物、mode/换行漂移、未跟踪文件或重复写入；
- scenario 后丢弃或还原 project B，不合并回主仓库。

Phase 7 Gate：逐动作审批、bounded diff、目标测试、Reports/Changes/磁盘事实和 cleanup 全部一致。

## Phase 8：真实模型 Crash、Restart 与恢复

仅在 Phase 6 通过后，于 project-recovery 全新 clone 执行：

1. 启动 packaged Desktop real-model turn。
2. 等待至少一个 model/tool timeline item 已 durable commit。
3. 仅终止脚本记录为 owned 的 AppHost PID，不按进程名全局 kill。
4. 确认 Renderer 显示 interrupted/unknown/recovery-required，不显示 completed，不自动 restart/replay。
5. 显式 restart AppHost、重新打开 workspace，从 ThreadStore 重建旧 turn 与 partial timeline。
6. 只有满足 restart 条件并由用户确认时创建新 attempt；旧 attempt 保持原 identity 与 interrupted/failed 状态。
7. 新 turn、provider request 和 approval identity 全部不同；旧 waiter/approval 不复用。
8. 完成或取消新 attempt，验证 Changes、Reports、process/temp 终态。

Phase 8 Gate：未知状态 fail closed、无自动 replay、旧新 identity 分离、authoritative resync 正确、cleanup delta 为 0。

## Phase 9：资源、隐私与最终决定

每个真实场景记录：

- runtime ready、首模型事件、首 tool、首 approval、终态耗时；
- model/tool/approval/provider call 数量；
- changed files、diff 行数、测试、report/artifact 数量；
- Main/Renderer/GPU/Utility/AppHost PID 和关闭后 delta；
- working set/private bytes 的 warm、peak、post-idle bounded samples；
- workspace/temp/config 是否逐字节恢复；
- secret/path/raw diagnostics/未经授权网络访问计数。

至少运行一个完整 provider-backed Desktop long-session/idle 场景。持续 working set 或 private bytes 超过 Week 77 对称口径 15% 观察线、出现单调增长或 cleanup 非零时记录 P1，不以 Electron warm-up豁免。

创建 `docs_md/weekly/79_week_review.md`，逐项引用首次失败、修复 revision、受影响重跑和最终 Gate。最终只能写 `Preview Ready` 或 `Blocked`。

Phase 9 Gate：evidence 脱敏、identity 可追踪、所有 delta 为 0、无开放 P0/P1。

## 缺陷分级与重跑规则

- P0：approval bypass、未授权写入/网络、secret/path 泄漏、错误 workspace、重复执行、数据损坏、fake 冒充 real、无法控制 owned process。立即停止真实写入与恢复，Week 79 `Blocked`。
- P1：真实 runtime 无法恢复、timeline/ThreadStore/Changes 不一致、事件丢失却 completed、稳定 orphan、持续资源超线。修复并从新 clean revision 重跑受影响矩阵；未关闭则 `Blocked`。
- P2：安全诊断、文案、人工步骤或传参摩擦，不影响权限与磁盘事实。进入 backlog，可在无 P0/P1 时继续。
- P3：视觉和便利性建议，不在 Week 79 扩大。

任何 source 修复必须：

1. 保存首败 evidence。
2. 加 deterministic regression test。
3. 形成新的 clean revision。
4. 重建 package/AppHost identity。
5. 重跑所有受影响的 .NET/Desktop/E2E/security/real-model 场景。
6. 明确废弃旧 revision 的当前资格。

## Week 79 Critical Gates

- [ ] W79-G0 Week 78 历史结论、Week 79 manifest、授权边界和 evidence schema 一致。
- [ ] W79-G1 production AppHost 默认使用真实 runtime factory，fake 只能显式测试注入，配置失败不 fallback。
- [ ] W79-G2 agent events 实时、bounded、严格有序地提交 durable timeline，持久化失败 fail closed。
- [ ] W79-G3 write/shell 每动作 durable approval；deny/stale/cancel/disconnect 无执行、重试或 waiter 泄漏。
- [ ] W79-G4 turn terminal、Reports、Changes、verification 与磁盘事实一致，unknown/interrupted 不推断成功。
- [ ] W79-G5 clean-source .NET/Desktop/package/E2E/security/accessibility/protocol 全矩阵通过，cleanup 为 0。
- [ ] W79-G6 packaged Desktop real-model read-only 通过，无 fake fallback、写入、secret/path 泄漏或 orphan。
- [ ] W79-G7 受控写任务只修改 allowlist，逐动作审批、测试、diff 和 timeline 一致。
- [ ] W79-G8 crash/restart 无自动 replay，旧新 attempt/request/approval identity 分离，authoritative resync 正确。
- [ ] W79-G9 provider-backed resource/idle 观测低于 15% 观察线，evidence 脱敏且所有 process/temp/config delta 为 0。
- [ ] `git diff --check` 通过；创建 `79_week_review.md` 并记录 `Preview Ready` 或 `Blocked`。

## 推荐执行顺序

1. 提交本计划与 Week 78 状态链接。
2. 创建 manifest、evidence validator 和 credential-free architecture tests。
3. 提取共享 tool/agent factory，删除 production fake fallback。
4. 实现 bounded event bridge 与 runtime terminal mapping。
5. 实现 durable approval bridge。
6. 完成定向测试和完整 credential-free package Gate。
7. 请求 Week 79 独立真实模型授权。
8. 依次执行 Desktop read-only、受控写入、crash/restart。
9. 完成 provider-backed resource/idle 观测。
10. 修复 P0/P1 时形成新 clean revision 并重跑受影响矩阵。
11. 创建 Week 79 review，做最终 Preview decision。

## 计划交付物

- `docs_md/weekly/79_week_desktop_real_model_runtime.plan.md`
- Week 78/roadmap/schedule 的当前状态链接
- production Desktop runtime、共享 agent/tool factory、bounded event bridge、durable approval adapter
- deterministic .NET/AppHost/Desktop/E2E regression tests
- ignored `artifacts/week79-desktop-real-model/` evidence
- `docs_md/weekly/79_week_review.md`

## 完成定义

Week 79 完成不等于 0.6.0 正式发布。完成只表示：

- production Desktop 不再用 deterministic fake runtime 冒充 turn execution；
- 真实模型执行、timeline、approval、Reports/Changes 和 recovery 形成可审计闭环；
- Week 78 被阻断的只读、写入、恢复和资源场景得到真实、可引用的结论；
- 最终 `Preview Ready` 或 `Blocked` 与 evidence 一致；
- Week 77 正式 release 的 `Blocked` 决定保持不变。
