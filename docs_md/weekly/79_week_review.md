# Week 79 Review：Desktop 生产真实模型 Runtime 与 Preview Gate

日期：2026-07-25

分支：`week-02-cli-commands-doctor-config`

最终受测 source revision：`8e227a4ca050e9bdff5d25d61bf89725fed26104`

## 最终决定

**Blocked**

W79-G0 至 W79-G8 已在最终 packaged Desktop identity 上关闭；W79-G9 未关闭。修正 provider warm baseline 并降低测量观察器扰动后，最终 30 秒对称 idle 观测的 Renderer working set retention 为 `10.77%`，但 private-bytes retention 为 `35.84%`，可重复超过 `15%` 观察线。因此仍有一个资源 P1，Week 79 不得写为 `Preview Ready`。

这个决定只针对内部 Preview Gate。Week 77 对 0.6.0 正式 release 的 `Blocked` 决定保持不变；本周没有创建 tag、上传制品或发布 Release，也不得使用 `Accepted`、`Released`、`Production Ready` 描述 Desktop。

## 最终身份与边界

- package executable SHA-256：`6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29`
- AppHost SHA-256：`DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`
- 真实模型配置只注入每个授权的 packaged Desktop 子进程；父进程、独立测试和证据文件不保存配置值。
- production tool catalog 保持 7 个内建工具；MCP discovery 默认关闭。
- write/shell 均需逐动作 durable approval；危险 shell 继续 fail closed。
- `desktop-v1` 协议面未扩展。

## 首败、修复 revision 与受影响重跑

| 范围 | 首次失败 | 修复或收口 | 受影响重跑 |
| --- | --- | --- | --- |
| Production runtime | production AppHost 仍使用 deterministic fake 路径 | `a8a48a2` 接通共享真实模型 runtime；`3b8cd51` 冻结缺配置 fail-closed | 定向 .NET、Desktop、package 与 packaged failure E2E |
| Desktop Git boundary | packaged workspace Git 子进程超时 | `dc0c8f2` 修复 child-process 边界 | Phase 5 全矩阵与 real-model read-only |
| Resource Gate 基线 | credential-free long-session working-set 口径受 Windows trim 影响 | `fd1f603` 固定 warm peak / settled median 对称统计 | unpacked E2E 9/9 |
| Runtime events | 审批可见时前序事件尚未确认 durable commit | `424c963` 串行确认事件与审批 | .NET、Desktop、unpacked/packaged E2E、real-model |
| Approval registration | Renderer 通知早于 waiter 注册 | `a4ab1d8` 先注册审批 waiter | 审批定向测试与 Phase 7 |
| MCP cleanup | 测试侧临时进程清理不稳定 | `f82e228` 稳定测试清理 | .NET 全矩阵 |
| Renderer projection | 旧 Thread detail response 可覆盖较新状态 | `c51ba86` 加 request/epoch 顺序保护 | Desktop、E2E 与真实模型投影检查 |
| Sequential approvals | 第一动作完成后 waiter identity 未轮换 | `8dab49a` 支持连续逐项审批 | Phase 7 双审批与完整 Gate |
| Crash/restart identities | 重启后的不同 turn 复用了 model/tool timeline item identity | `8e227a4` 将 turn identity 纳入 deterministic item seed，并加顺序 turn 回归 | Phase 5、Phase 6/7、Phase 8 |
| Phase 9 首轮资源观测 | provider path 未在 warm baseline 前执行，Renderer 为 `18.74% / 47.77%` | 增加独立 provider warm-up turn，保留首败 | 第二、第三资源观测 |
| Phase 9 第二轮资源观测 | working set 降至 `10.84%`，private bytes 仍为 `36.77%` | 终态投影轮询由 125 ms 降至 1 s，减少观察器扰动 | 第三资源观测 |
| Phase 9 最终资源观测 | working set `10.77%`，private bytes `35.84%` | 未关闭；记录 open P1 | 最终决定为 `Blocked` |

任何产品 source 修复均形成了新的 clean revision，并按受影响范围重建 package、重跑 credential-free 与真实模型场景。Phase 9 的两项 harness 口径修正没有改变产品 source，也没有把超线解释为 Electron warm-up 后豁免。

## Credential-free 最终矩阵

| Gate | 最终结果 |
| --- | --- |
| .NET | `1421/1421`，skipped `0` |
| Desktop verify | 23 files，`103/103` |
| dependency audit | high severity `0` |
| package audit | 78 files、12 asar entries、forbidden payload `0` |
| unpacked E2E | `9/9` |
| packaged E2E | `8/8` |
| accessibility | `2/2` |
| packaged smoke | `8/8` |
| protocol | 3 轮，每轮 `48/48` |
| cleanup | process/temp/config delta 全部 `0` |

最终 revision 的第一次 unpacked E2E 在 source build 后发现 4 个测试自有 MSBuild node-reuse/conhost 残留，结果为 `8/9`。脚本只终止记录到的 4 个测试自有 PID；corrupt-state 定向重跑 `1/1`、完整 unpacked 重跑 `9/9`。首败和处置保留在 credential-free evidence 中。

## 真实模型场景

### Read-only

- packaged Desktop 使用 `.env.local` 中授权的三个模型配置项，且仅注入该 Desktop 子进程。
- exactly one `workspace.read_text(global.json)`，无 approval、write 或 shell。
- terminal、durable timeline、Renderer、Reports、Changes 与磁盘事实一致。
- changed files `0`，owned process delta `0`，未出现敏感值或 rooted path。

### Controlled write

- disposable project B 的目标测试先确定性失败。
- 模型只修改 `LogPathResolver.cs` 与预置测试文件，总 diff `27` 行。
- `workspace.apply_patch` 与精确 `dotnet test` command 分别取得不同 durable approval。
- 模型内测试和独立重跑均通过；timeline、Renderer、Changes、Reports 与磁盘一致。
- project B 已丢弃，没有合并到主仓库。

### Crash/restart

- 只终止脚本记录的 owned AppHost PID，Desktop Main 保持存活。
- crash 后 fail closed，不显示 completed，不自动 restart 或 replay。
- 显式重启后从 ThreadStore 权威恢复旧 partial timeline；旧 attempt 为 `interrupted`。
- 用户确认的新 attempt 使用不同 turn、model/tool item 与 approval identity。
- 新 attempt 在审批前取消，changed files `0`；Renderer、review 与 cleanup 全部一致。

## Provider-backed resource/idle

最终口径：

- 1 个独立 provider warm-up turn；
- 随后 30 秒 warm window，5 秒一次采样；
- 20 秒 settle 后取末 3 个采样中位数；
- 同一线程执行 5 个 provider-backed read-only turns；
- 工作负载期间持续记录 Main/Renderer/GPU/Utility/AppHost；
- 随后使用完全相同的 30 秒 post-idle window；
- working set 使用 warm-window peak 对 post-idle settled median；
- private bytes 使用两侧 settled median；
- workers/retries 均不引入重试到绿。

| Attempt | 修正点 | Renderer working set | Renderer private bytes | cleanup |
| --- | --- | ---: | ---: | ---: |
| 1 | 初始口径，provider path 未预热 | `18.74%` | `47.77%` | `0/0/0` |
| 2 | 增加 provider warm-up | `10.84%` | `36.77%` | `0/0/0` |
| 3 | 保持相同 workload，降低观察器轮询 | `10.77%` | `35.84%` | `0/0/0` |

最终 attempt 完成 1 个 warm-up turn 和 5 个测量 turns，共 6 次 read-only tool call、36 个 timeline items、31 个资源 samples、5 个进程角色和 6 个 owned PID。approval、changed files、reports、artifacts、未经授权网络事件和敏感披露计数均为 0；没有检测到显著持续单调增长，关闭后 owned process/temp/config delta 均为 0。

private-bytes retention 在两轮修正后仍稳定约为 `36%`。这不是可由 cleanup、权限或测量高频轮询解释的单次离群值，因此按计划记为开放 P1。

## Critical Gates

| Gate | 结论 | 依据 |
| --- | --- | --- |
| W79-G0 | Passed | Week 78 历史状态、manifest、授权边界与 evidence schema 一致 |
| W79-G1 | Passed | production AppHost 使用真实 runtime factory；缺配置不 fallback |
| W79-G2 | Passed | bounded realtime events 严格有序 durable commit，失败 fail closed |
| W79-G3 | Passed | write/shell 逐动作 durable approval；stale/cancel/disconnect 无执行 |
| W79-G4 | Passed | terminal、Reports、Changes、verification 与磁盘事实一致 |
| W79-G5 | Passed | clean-source regression/package/security/E2E 矩阵通过 |
| W79-G6 | Passed | packaged provider read-only 通过 |
| W79-G7 | Passed | allowlist write、双审批、测试与 diff 一致 |
| W79-G8 | Passed | crash/restart 无 replay，身份隔离与权威 resync 正确 |
| W79-G9 | **Failed** | Renderer private bytes `+35.84%`，超过 `15%`；open P1 |

开放缺陷：

- P0：`0`
- P1：`1` — provider-backed long-session Renderer private-bytes retention

## 后续解除条件

要把 Week 79 从 `Blocked` 改为 `Preview Ready`，必须：

1. 定位 Renderer private-bytes retention 的来源，区分 live JS heap、V8 reservation、Thread projection/event resync 与 provider UI 状态。
2. 若修改 source，加入 deterministic regression test，形成新的 clean revision。
3. 重建 package/AppHost identity。
4. 重跑受影响的 .NET、Desktop、unpacked/packaged E2E、安全、真实 read-only/recovery 与 provider resource/idle 矩阵。
5. 新的对称 long-session 观测必须同时满足 working set/private bytes `<=15%`，且 process/temp/config delta 为 `0`。
6. 保留本次三轮失败 evidence，不得覆盖或删除后以单次绿色结果替代。

## Evidence

忽略目录 `artifacts/week79-desktop-real-model/` 中保留 8 个脱敏 envelope：

- `manifest.json`
- `architecture.json`
- `credential-free.json`
- `real-model-readonly.json`
- `real-model-write.json`
- `recovery.json`
- `resource-summary.json`
- `final-summary.json`

最终 evidence validator 需要继续满足：8 个文件齐全，无 forbidden fields、sensitive-looking values 或 rooted paths；所有已完成场景 cleanup delta 为 `0`。
