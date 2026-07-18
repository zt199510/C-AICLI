# Week 75 执行计划：故障恢复、长会话与 Desktop E2E

**Goal:** 在 Week 74 已完成的 Desktop write-capable turn、approval/cancel、用户 Terminal、Artifacts 与 Gerber/TIFF 人工闭环之上，系统化验证并加固故障恢复、长会话与完整 packaged Desktop E2E。Week 75 不以新增产品能力为目标，而是证明 AppHost crash、Renderer reload、Main exit、protocol disconnect、partial notification、corrupt thread 和 missing artifact 都不会把未知运行态猜成成功、不会自动重放 write step、不会留下子进程或临时目录，并为成功、deny、cancel、crash、restart、corrupt-state 六条用户闭环建立可重复的自动化证据。
**Architecture:** 延续 Application/Store 单一事实源、AppHost 单 workspace authority、Main 生命周期 owner、Preload exact bridge 与 Renderer 可丢弃 projection。通知仍只是 dirty hint；reload/reconnect 后必须以 `workspace.open`、`thread.list/get`、`composer.get`、review queries 和持久化 revision 重建状态。恢复逻辑按故障域分层：Renderer 只做 authoritative resync，Main 只管理 AppHost generation/restart，AppHost 在 EOF/disconnect 时取消 session 与有界 drain，Application/Store 决定 interrupted/failed/restart eligibility。任何层都不得根据内存状态、最后一条 notification 或 pending Promise 推断 committed outcome。
**Tech Stack:** .NET SDK `9.0.308` 目标、Core/Application/AppHost versioned stores、`desktop-v1` framed JSON-RPC over stdio、Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`。故障与负载测试优先使用 deterministic fake runtime、controllable streams/process fixtures、fake clocks/barriers 和 test-owned workspace，不使用依赖固定 sleep 的时序断言。

---

状态：Passed

创建日期：2026-07-18

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`c677c5932520b17b313f573b316857f01760e877`

前置事实：

- Week 74 已提交：`c677c59 完成 Week74 终端与制品人工闭环`，工作区起点干净。
- Week 74 已通过 `.NET full suite 1384/1384`、AppHost Debug/Release build、Desktop `19 files / 86 tests`、contract/notices/typecheck/lint/build、安全扫描、unpacked 与 packaged E2E。
- `desktop-v1` 当前 reviewed surface 为 exact `39 invoke + 2 event`；Week 75 默认不新增业务 method/event。
- Packaged E2E 已可使用版本匹配的本地 Electron archive；网络下载失败不是跳过 packaged Gate 的理由。
- Real Gerbv/ImageMagick/LibTIFF/Magick.NET correctness smoke 仍为 opt-in skipped；Week 75 只验证 Desktop control plane 与资源清理，不扩大制造/图像 correctness 声明。

执行约束：

- 本文件只创建 Week 75 实施蓝图，不在本次任务中实现功能。
- 后续执行默认在当前分支原地开发；除非用户另行要求，不创建新 worktree 或新分支。
- 不覆盖用户已有无关改动；执行前若工作树不干净，先记录 baseline 并隔离本周文件。
- E2E retry 只能收集 trace、screenshot、stderr、process/temp inventory；首次失败后重试通过不能把该 case 计为稳定 Passed。
- 只有全部 Critical Gate 首轮稳定通过后，plan、review、schedule 才能同步改为 Passed。

## 本周目标

1. 建立 crash/reload/exit/disconnect/partial-notification 恢复矩阵，明确每一故障点的事实源、用户可见状态、可重试动作和禁止动作。
2. 证明未知 `running` 不会被恢复代码猜成 `completed`；AppHost/transport 丢失时只能由持久化事实确定 completed，否则进入 interrupted/failed/recovery-required。
3. 证明 resume 与 restart 语义分离：resume 只继续安全 checkpoint，restart 创建新 attempt；两者都必须由用户显式触发，绝不自动重放 write、approval、terminal input 或 external-tool step。
4. 加固 `thread.changed` sequence 去重、乱序/重复/缺口处理和重连补偿；任何通知异常都回到 authoritative list/get，而不是修补本地 truth。
5. 覆盖长 timeline、大 diff、大 terminal output、慢 consumer、output backpressure、Renderer 多次 reload 与 workspace reopen，保证 buffer、listener、request、process 和临时目录有界释放。
6. 建立真实 packaged Electron + packaged AppHost + deterministic fake turn runtime 的 E2E 矩阵，覆盖成功、deny、cancel、crash、restart、corrupt-state 六条闭环。
7. 建立可诊断但不掩盖 flaky 的测试基础设施：确定性 fault injection、每 case 独立 workspace/profile、首轮结论、失败证据保留、终态 inventory。

## 稳定输入与待关闭差距

### 已有可复用能力

- `AppHostRuntime` 已使用 generation 隔离旧 client event，unexpected exit 不自动 restart，并提供显式 `restartRuntime()`。
- AppHost 已有 strict framing、request timeout/cancel、bounded output queue、backpressure、disconnect drain 和 ordered `thread.changed` sequencer。
- Renderer 已有 sequence/item-id merge invariant、single-flight dirty resync、reload 后 authoritative thread/composer fetch。
- Application/Store 已有 interrupted/failed、resume/restart、stale revision、corrupt record、missing artifact、partial evidence 和 no-replay 语义。
- Terminal supervisor 已有 64 KiB bounded output、显式 truncation、process-tree cleanup；Artifacts/Gerber 已有 ownership/hash/path/revision recheck。
- 当前 E2E fixture 已覆盖只读、composer、terminal/artifact/Gerber 基础路径和 Renderer reload；packaged 路径目前只验证 AppHost ready/open workspace shell，尚未覆盖完整 write/recovery 矩阵。

### 本周必须关闭的差距

- Main/AppHost 生命周期单测已覆盖基础 crash/restart，但尚缺 crash 位于 write commit 前后、notification response 间隙、workspace reopen 和旧 generation late event 的系统矩阵。
- unpacked fixture 使用 `fixture-main.cjs`/`fixture-preload.cjs`，不能替代 packaged Main + packaged AppHost 的真实边界证据。
- 当前 Playwright 只有一个串行 spec；六条业务闭环、corrupt-state fixture、process/temp inventory 和首轮稳定性结果尚未独立建模。
- 长 timeline 已使用虚拟列表，但 timeline page merge、大 payload、terminal truncation、slow writer/backpressure 和 repeated reload 的资源上限尚无一组统一 workload 证据。
- 现有 cleanup 主要检查 Desktop/AppHost/terminal；MCP、agent shell、external tool 与 test-owned temp root 需要统一 before/after inventory。

## 范围冻结

### 本周包含

- AppHost crash、protocol invalid/disconnect、Main/window close、Renderer reload/navigation rejected、partial frame/notification、late response/event 的故障注入与恢复测试。
- interrupted/failed recovery projection、显式 resume/restart、commit-unknown reconciliation、write no-replay、approval no-reuse。
- corrupt thread/turn/composer/report/artifact manifest 与 missing/tampered artifact 的 fail-closed UX 和 E2E fixture。
- sequence duplicate/out-of-order/gap、workspace id mismatch、old generation event、resync coalescing 与 authoritative refetch。
- 长 timeline、大 diff、大 terminal output、slow consumer/backpressure、重复 reload/reopen、listener/task/buffer/process/temp cleanup。
- unpacked 快速矩阵、packaged 完整六路径矩阵、trace/screenshot/stderr/inventory 诊断产物和首轮判定。
- 必要的测试脚本、fixture builder、Playwright project/tag 划分、review 与 schedule 更新。

### 本周不包含

- 自动重启 AppHost、后台无限重连、自动 resume/restart、自动 approval、write request transport retry 或 terminal command replay。
- 扩大 protocol body/header/output/timeline/diff/terminal hard limit 来让压力测试通过。
- 新业务 method、generic IPC、generic fault-injection IPC、生产 Renderer 可调用的 crash/corrupt/test hook。
- 多 workspace 并发、跨设备 session、remote runner、云同步、terminal persistence、artifact restore/delete/prune UI。
- Week 76 的完整 security/accessibility/performance RC 审计；本周只记录与恢复/资源有关的观察值和回归。
- Real model、real MCP service、real Gerber/ImageMagick correctness 作为 required Gate；可选 smoke 必须继续显式 opt-in。

## 恢复状态机与事实源

### 1. 故障域与恢复规则

| 故障 | 立即状态 | 权威恢复动作 | 禁止行为 |
|---|---|---|---|
| Renderer reload | 丢弃本地 projection | 重新订阅后 list/get/composer/review resync | 从 localStorage 恢复 running/approval truth |
| AppHost crash / protocol disconnect | Main 标记 runtime failed，清空 workspace projection | 用户显式 restart AppHost，再显式 reopen workspace | 自动 restart、自动重发 pending mutation |
| Main/window exit | 触发 bounded AppHost/child shutdown | 下次启动从 store 重建 | 保留 detached AppHost/terminal/MCP |
| response 丢失但 commit 可能完成 | client outcome unknown | 根据 mutation id、revision、thread/store projection 对账 | 因 Promise reject 再次执行 write |
| partial/duplicate notification | 将 workspace/thread 标记 dirty | coalesced authoritative refetch | 用 notification payload直接补写 canonical state |
| corrupt thread/store | 保留可诊断失败并隔离有效记录 | 用户修复/移除损坏输入后重新查询 | 猜测字段、静默跳过指定损坏对象 |
| missing/tampered artifact | review/export/accept fail closed | 重新生成或显式新 attempt | 接受 stale evidence 或旧 hash |

### 2. Turn 恢复不变量

- 持久化 terminal state 是唯一完成依据；内存 supervisor、UI spinner、transport response 和 notification 都不是完成证明。
- crash 位于 commit 前：不得出现 completed；crash 位于 commit 后但 response 前：重连必须读到 committed revision，client mutation id 防止重复提交。
- interrupted 只能显式 resume 或 restart；failed 是否允许 restart 由 Application projection 决定，Renderer 不自行启用按钮。
- resume 不重复已完成 write step；restart 必须创建新的 turn/attempt identity，并保留 parent/source link 与旧 partial evidence。
- approval deny/cancel 是终态；旧 approval token、request id、policy identity 和 revision 不得在 restart 后复用。
- terminal、MCP、agent shell、Gerber/external tool 在 AppHost/Main exit 后必须终止；其 stdout/stderr 尾部只能作为 bounded diagnostic，不能决定业务成功。

## Notification、重连与 Backpressure

### 1. Sequence 和 resync

- 相同 `eventSequence` + 相同 identity 视为重复 dirty hint，只触发至多一次合并 resync。
- sequence 倒退、缺口、同 sequence 不同 identity、workspace id 不匹配或旧 generation event 都不得改变 canonical projection。
- resync 运行中收到新 dirty hint 时只设置 dirty flag；完成后再执行一次 authoritative fetch，禁止并发风暴。
- workspace reopen 后清空上一 workspace 的 sequence cursor、selection 和 review projection；late event 必须被 generation/workspace guard 丢弃。
- `thread.get` page merge 继续按 sequence + item id 双不变量校验；冲突显示 recovery-required 并执行全量 authoritative reload。

### 2. 有界负载

冻结并测试现有 hard bounds，不因压力测试失败而静默放宽：

- protocol header `8 KiB`、body `1 MiB`、stderr tail `16 KiB`；AppHost writer queue 维持 frame/aggregate byte 上限。
- terminal output ring buffer `64 KiB`，截断必须可见且 cursor 单调；slow polling 不导致无界历史保留。
- timeline 通过分页与虚拟列表承载；测试数据应跨多个 page，并包含重复/缺口/大 payload 边界。
- diff/report/artifact projection 使用既有 bounded DTO、truncated 标识和按需 detail，不把 raw full diff/artifact bytes 注入 Renderer store。
- response-too-large、output-backpressure、timeout、transport-closed 都返回稳定分类或终止 session；不得 silent drop、无限 await 或无限 retry。

## E2E 方案

### 1. Harness 分层

- `unit/component`：fake clocks、barriers、controllable stream/client，覆盖 commit/response/notification race 和 listener/resource ownership。
- `AppHost process E2E`：真实 stdin/stdout framing + test workspace，覆盖 disconnect、partial frame、backpressure、shutdown 与 child cleanup。
- `unpacked Electron E2E`：快速 UI/Renderer 状态矩阵，可使用 test-owned fixture main/preload，但不得计作 packaged boundary 证据。
- `packaged Electron E2E`：启动 `out/C-AICLI Desktop-win32-x64/caicli-desktop.exe` 与 `resources/apphost/CSharpAiCli.AppHost.exe`，通过真实 Main/Preload/`desktop-v1` 调用 Application 的 deterministic fake turn runtime。

禁止把 test-only generic IPC 或 fixture preload 打进 `app.asar`。故障注入优先从 Playwright 控制 Electron process、test-owned store fixture、已有 `forceTerminateForTest` 的受限 Main 路径或 AppHost process harness 完成；若必须增加 test hook，生产构建安全扫描必须证明默认不可达且无 Renderer bridge surface。

### 2. Packaged 六条闭环

| Case | 最小路径 | 必须断言 |
|---|---|---|
| success | open -> create/queue -> run -> allow -> completed | 单次 write、最终 summary、reload 后一致、无 orphan |
| deny | run -> approval -> deny | denied 终态、无被拒 write、不可 resume/reuse approval |
| cancel | running/approval wait -> cancel | canceling -> canceled、child 停止、reload 后不变 |
| crash | 运行中 kill AppHost -> runtime failed | 不猜 success、不自动 restart/replay、旧 generation event 无效 |
| restart | 显式 restart AppHost -> reopen -> restart turn | 新 attempt identity、旧 evidence 保留、仅执行一次 |
| corrupt-state | 注入 corrupt thread/turn/artifact fixture -> open/review | stable corrupt-state、有效对象仍可见、无静默修复/accept |

每个 case 使用独立临时 workspace、独立 APPDATA/profile、唯一 sentinel 和进程基线；case 结束后检查文件句柄可释放、临时目录可删除，并记录 Desktop/AppHost/agent shell/MCP/terminal/external-tool before/after delta。

### 3. 长会话 Workload

- Timeline：至少跨多个 protocol page，混合 message/reasoning/tool/command/approval/report，验证滚动、选择、resync 后顺序与 DOM 虚拟化。
- Diff：生成接近但不超过既有 projection 上限的多文件 diff，并单独验证 oversize/truncated 路径。
- Terminal：持续输出超过 64 KiB，验证 truncation marker、cursor、cancel/close、Renderer reload 后 bounded projection 与 process cleanup。
- Notification：burst、duplicate、gap、late old-generation event，验证 resync 调用次数有界且最终 revision 与 store 一致。
- Lifecycle：同一 case 重复 reload/reopen/crash-restart 多轮，验证 listener count、pending request、working set趋势和 test-owned temp inventory 回落。

本周不设最终性能 SLA，但必须记录 workload 规模、耗时、peak/after working set 与是否回落，作为 Week 76 performance baseline 输入。发现持续增长或超过 Week 66 同口径预算 `15%` 时必须调查并记录，不能只增加 timeout。

## Flaky 治理规则

- Playwright `retries` 保持 `0`；CI/本地 Gate 采用单 worker、独立 fixture 和 deterministic readiness signal。
- 不用固定 `sleep` 判断业务完成；等待 protocol/store/UI state 或受控 barrier。仅 cleanup grace period 可使用短、有上限的轮询。
- 首轮失败立即保存 trace、screenshot、video（如启用）、Main/AppHost bounded stderr、scenario seed、workspace inventory 和 process tree。
- 可执行一次诊断重试确认可复现性，但 review 仍记录首轮失败；修复后必须从 clean fixture 重新运行完整 affected matrix。
- 不用提高全局 timeout 掩盖竞态。确需调整 timeout 时，必须给出 workload/冷启动数据并限定到具体 project/case。
- 测试结束必须在 `finally` 清理 owned process；cleanup 失败本身是 case failure，不能由后续全局 kill 转成 Passed。

## 测试计划

### .NET / AppHost

- crash at pre-commit/post-commit/pre-response/post-response，验证 mutation id、revision 与 no-replay。
- EOF/disconnect/partial header/body、slow writer、backpressure、late write denial、bounded drain。
- shutdown/crash 时 write supervisor、terminal、MCP/external process tree cleanup 与 interrupted projection。
- corrupt/unsupported/duplicate sequence thread，valid-neighbor preservation，missing/tampered artifact fail closed。
- resume/restart identity、parent attempt、partial evidence preservation、approval no-reuse。
- long timeline page/cursor、large diagnostic/diff/result truncation和 response upper bound。

### Electron Main / Preload / Renderer

- AppHost generation race、explicit restart、workspace clear/reopen、old client exit/event ignore、stop/restart concurrency。
- partial/duplicate/gap/out-of-order notification，resync coalescing，Renderer unmount/reload listener cleanup。
- long timeline virtualization、large diff bounded rendering、terminal truncation与 repeated reload。
- corrupt-state/missing-artifact recovery banner、disabled actions、safe error，无 raw path/secret/stack 泄露。
- exact bridge/security inventory 仍为 `39 invoke + 2 event`；如契约确需变化，先 review contract、更新双端生成与 method-count Gate。

### E2E / packaged

- unpacked 六路径快速矩阵通过。
- packaged 六路径使用真实 package/Main/Preload/AppHost 通过，不能以 fixture preload 替代。
- AppHost crash + explicit restart + workspace reopen + turn restart 完成一条跨进程恢复链。
- 长 timeline/diff/terminal workload 至少在 unpacked 完整执行；packaged 执行代表性 bounded case。
- 每条 case 的 process/temp delta 为 0，失败诊断不包含 sentinel secret 或 managed absolute path。

## Critical Gate

Week 75 只能标记 Passed，当且仅当：

1. `.NET full suite`、AppHost Debug/Release build 全部通过；恢复/竞态定向测试不依赖 sleep-based luck。
2. `npm run check:contracts`、`check:notices`、`typecheck`、`lint`、`test`、`build` 与 production security scan 全部通过。
3. success、deny、cancel、crash、restart、corrupt-state 六条 unpacked 与 packaged required case 首轮稳定通过，Playwright retries 为 0。
4. AppHost crash/transport unknown 不会猜成 success；write、approval、terminal input、external-tool step 均无自动重放。
5. sequence duplicate/gap/out-of-order/old-generation tests 通过，最终 projection 来自 authoritative resync。
6. 长 timeline、大 diff、大 terminal output 和 backpressure 均有 bounded/truncated/resource-release 证据，无 unbounded queue/listener/buffer。
7. 每条 E2E 终态无 test-owned Desktop、AppHost、agent shell、MCP、terminal、Gerber/ImageMagick/external-tool orphan；临时目录可删除且 delta 为 0。
8. 任一 case 首轮失败后即保持 Gate failed，直到修复并完整 clean rerun；不能用 retry pass 覆盖。
9. review 准确记录 workload、耗时、资源、skipped real-tool evidence 与 Week 76 输入，不扩大 correctness/performance 声明。

## 任务拆分

1. Baseline 与矩阵冻结
   - 记录 HEAD、dirty state、SDK/Node/Electron、contract hash/method count、现有 test counts。
   - 建立 fault × layer × expected state × cleanup owner 表和六条 E2E case id。
   - 冻结首轮判定、artifact retention、process/temp inventory 格式。

2. Store/Application 恢复正确性
   - 补齐 commit unknown、interrupted/failed、resume/restart、approval no-reuse 测试。
   - 补齐 corrupt thread/turn/composer/artifact 与 valid-neighbor/missing evidence 测试。
   - 保证所有用户可见 recovery projection 使用稳定 safe code/message。

3. AppHost protocol 与 process lifecycle
   - 增加 partial/disconnect/backpressure/late-write/stop race 的 deterministic harness。
   - 验证 write supervisor、terminal、MCP/external process 的 bounded shutdown 与 interrupted 落盘。
   - 统一 process tree 和 temp-root inventory helper。

4. Main/Renderer 重连与长会话
   - 加固 generation/workspace guards、notification sequence policy 和 coalesced resync。
   - 覆盖 repeated reload/unmount/reopen 的 listener/request cleanup。
   - 加入 timeline/diff/terminal workload fixtures 与 bounded UI assertions。

5. E2E fixture 与六路径
   - 将 scenario/workspace builder 与 UI driver 分离，每 case 独立 profile/workspace/sentinel。
   - 先完成 unpacked 快速矩阵，再通过真实 packaged Main/AppHost 执行六条闭环。
   - 保证 test-only fixture/hook 不进入 production app.asar 或 bridge。

6. Flaky audit 与 clean rerun
   - 首轮运行保存证据；逐项消除 race、共享目录、端口、进程和 fixed-sleep 依赖。
   - affected matrix 修复后从 clean fixture 完整重跑，禁止只重跑失败断言。
   - 运行 orphan/temp deletion 与 bounded diagnostics redaction 检查。

7. Review 与 Week 76 交接
   - 创建 `75_week_review.md`，记录六路径、首轮/clean rerun、process/temp、workload/resource 数据。
   - 仅在 Gate 全过后把 plan/schedule 状态改为 Passed。
   - 向 Week 76 交付 security review targets、资源趋势、package inventory 和剩余 flaky/known limitation。

## 建议验证命令

```powershell
dotnet test src/CSharpAiCli.Tests/CSharpAiCli.Tests.csproj -c Debug --no-restore
dotnet build src/CSharpAiCli.AppHost/CSharpAiCli.AppHost.csproj -c Debug --no-restore
dotnet build src/CSharpAiCli.AppHost/CSharpAiCli.AppHost.csproj -c Release --no-restore

Push-Location apps/desktop
npm run check:contracts
npm run check:notices
npm run typecheck
npm run lint
npm test
npm run build
npm run test:e2e:unpacked
npm run package:dir
npm run test:e2e:packaged
Pop-Location

git diff --check
```

若 Electron 下载路径不稳定，使用仓库已支持且版本匹配的缓存输入后重新从 `package:dir` 开始；不得复用旧 package 假装本次 source revision 已验证。执行阶段可新增单一 Week 75 smoke orchestrator，但它必须 fail-fast、`retries=0`、输出每 case 结论与 cleanup inventory，并由测试覆盖脚本自身的关键断言。

## 风险与处置

| 风险 | 处置 |
|---|---|
| crash 时点不可重复，测试偶发 | 使用 barrier/controllable runtime 定位 commit、response、notification 边界；不用概率 kill + sleep |
| packaged fake path 绕过真实边界 | packaged case 必须启动发布 exe 与 resources AppHost；fixture preload 只计 unpacked 证据 |
| test hook 扩大生产攻击面 | 优先 process/store fault injection；任何 hook 默认不可达、无 Renderer bridge，并进入 security scan |
| retry 掩盖 flaky | retries 固定 0；首次失败单独记账，诊断重试不改变 Gate |
| 长数据通过放宽 limit | 冻结现有 bounds，验证分页/truncated/backpressure；不得扩大 1 MiB/64 KiB 等边界 |
| cleanup 误杀用户进程 | 只追踪 test-owned PID/job/profile/temp root；不按进程名全局清理 |
| corrupt fixture 污染真实 workspace | 每 case 独立临时 workspace，启动前/结束后验证 resolved path 在 test root 内 |
| working set 短期抖动被误判泄漏 | 固定 workload、采样点和 GC/idle window，比较多轮趋势；持续增长才进入 Week 76 blocker |
| real-tool skipped 被误写为 correctness pass | review 明确 `Skipped/Unproven`，Week 75 只声明 control-plane recovery 与 cleanup |

## Week 76 交付输入

- 六条 packaged Desktop 闭环的首轮稳定自动化证据与 retained diagnostics 规则。
- crash/reconnect/no-replay 状态机、sequence/resync 策略和 corrupt-state UX 的冻结结论。
- AppHost/shell/MCP/terminal/external-tool/temp-root 零残留证据。
- 长 timeline、大 diff、大 terminal output 的 workload、耗时、working set 与释放趋势。
- exact bridge/contract inventory、package source revision、AppHost identity，以及未扩大 hard limit 的证明。
- 供 Security、Accessibility、Performance 与 RC 使用的已知限制、skipped real-tool correctness 和待审计 test-hook 结论。
