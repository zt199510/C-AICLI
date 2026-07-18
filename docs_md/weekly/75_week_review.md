# Week 75 执行回顾：故障恢复、长会话与 Desktop E2E

状态：Passed

日期：2026-07-18

起点提交：`c677c5932520b17b313f573b316857f01760e877`

## 结论

Week 75 Gate 已在修复首轮失败后通过完整 clean rerun。Desktop 对未知运行态保持 fail-closed：AppHost crash 不推断成功、不自动 restart/replay；显式 turn restart 先把旧 attempt 持久化为 `failed/interrupted`，清除旧 approval，再创建具有新 turn/request identity 的 attempt。成功、deny、cancel、crash、restart、corrupt-state 六条路径均通过 unpacked 与本次源码生成的 packaged 应用验证。

`desktop-v1` surface 未扩张，仍为 exact `39 invoke + 2 event`。Real Gerbv/ImageMagick/LibTIFF/Magick.NET correctness 仍是 `Skipped/Unproven`；本周结论只覆盖 control-plane recovery、bounded projection 与 test-owned resource cleanup。

## 主要实现

- Application/Store：stale active turn 投影为 `recoveryRequired`；显式 restart 原子化提交 interrupted evidence，保留 source link，清除旧 approval，并以稳定 mutation identity 保证幂等。
- Store reader：对 atomic manifest/turn replacement 的瞬态交替窗口做最多 4 次无 sleep 权威重读；corrupt record 仍 fail-closed，并保留 valid neighbor。
- AppHost/process：partial header/body EOF 不回显 payload；外部工具 timeout/cancel 会取消 bounded stdout/stderr reader，进程清理上限未放宽。
- Renderer：notification exact duplicate 合并；gap、倒序、同 sequence 不同 identity 只标 dirty 并触发 authoritative resync；workspace 切换重置 cursor/identity。
- UI mutation：cancel/approval 遇到 runtime timeline revision 竞争时，最多 4 次使用同一 mutation/request identity 对账；不启动或重放 execution。
- 长会话：修复 viewport 高度约束，使 240-item timeline 使用 DOM virtualization；`diffTruncated` 进入 review 状态；terminal 保持 64 KiB ring projection。
- E2E harness：每 case 独立 workspace/profile/APPDATA/sentinel；只跟踪并清理 test-owned main/descendant PID；cleanup failure 本身使 case 失败；Playwright `retries=0`。

## 最终 Gate 证据

| Gate | Clean rerun |
|---|---|
| .NET full suite | `1390/1390` Passed，Debug，1m28s |
| AppHost build | Debug/Release 均 `0 warnings / 0 errors` |
| Desktop contracts/notices/typecheck/lint/security/build | `npm run verify` Passed |
| Desktop unit/component | `19 files / 89 tests` Passed |
| Unpacked E2E | `8/8` Passed，single worker，retries 0，37.6s |
| Packaged E2E | `7/7` Passed，single worker，retries 0，39.7s |
| Diff hygiene | `git diff --check` Passed |

Unpacked 包含六条 recovery 路径、long-session workload 和 read-only reload；packaged 包含六条 recovery 路径及 read-only packaged smoke。每条 recovery case 的 `week75-inventory.json` 断言 `processDelta=[]` 且 `tempRootReleased=true`；最终未依赖全局按名称 kill。

## 六条 packaged 闭环

| Case | 终态证据 |
|---|---|
| success | 新 turn 等待 approval，Approve 后持久化 `completed`，仅一个 attempt |
| deny | Deny 后 `failed / approval-denied`，active approval 清空 |
| cancel | bounded revision reconciliation 后 `canceled / canceled`，执行 token 终止 |
| crash | kill owned AppHost 后 Main 显示 `apphost-exited`；无 completed 文案、无自动 restart |
| restart | 显式 restart AppHost/reopen/restart turn；旧 attempt 为 `failed/interrupted`，新 turn 与 approval request identity 均不同，最终 completed |
| corrupt-state | 损坏 thread 被隔离，valid neighbor 可见；原始损坏文件未静默修复，UI 不泄露 profile/sentinel |

## 首轮失败与 flaky 治理

首轮失败均保留为失败，不以诊断重试计 Passed：

- 初始 Electron harness 暴露 close 后读取 Playwright process、workspace open/create race、非 exact locator、AppHost 只查 direct child、旧 approval 被误认作新 approval等问题；逐项改为 frozen PID、authoritative readiness、exact role、ancestor-verified descendant 和 identity polling。
- .NET 首轮为 `1388/1389`：既有 fake driver timeout 在进程结束后仍可能留下 pipe read，导致 `ProcessCleanedUp=false`。修复为取消 bounded reader，定向 2/2 后完整 clean rerun 为 1390/1390。
- Unpacked 首轮 affected matrix 出现 cancel revision race，以及 graceful cleanup 的 Playwright control-transport 干扰；修复为同 mutation identity 的 bounded conflict reconciliation 与 `ElectronApplication.close()` 生命周期，最终完整 8/8。
- Packaged 首轮为 6/7，restart 并发读曾返回 `thread-store-unavailable`。尝试让 reader 获取 writer mutex 后出现 writer starvation，该方案被撤销；最终采用 4 次无 sleep stable reread，并加入 24 次 atomic replacement 并发回归。
- 后续 packaged 诊断分别发现 restart readiness 断言过早及 cancel 在连续 timeline commit 下需要多于一次 revision reconciliation；修复后从新 package/profile 完整重跑 7/7。

## 长会话与资源观测

固定 workload：

- Timeline：240 items，3 页，每页 80；加载完毕时 `.timeline-card` DOM 数小于 100。
- Lifecycle：连续 renderer reload 5 次，每次重新从 authoritative page 读取 80 items。
- Diff：50 个 bounded changed-file rows，`diffTruncated=true`，warning 与 capped banner 可见。
- Terminal：模拟产生 71,680 bytes，只保留 65,536 bytes；truncation marker、70 KiB monotonic cursor、cancel/close 均通过。
- Workload E2E：约 2.0-2.5s；关闭后 Electron process 与 test root 均释放为 0。

Working-set 采样（KiB）为 `375568, 390956, 411852, 431076, 445220, 459108, 512800`。5 次 reload 区间从 375,568 增至 459,108（约 `+22.2%`），terminal/diff 完成后的峰值为 512,800。进程关闭后归零，但会话内趋势超过 15% 观察线，因此这不是 performance pass；已作为 Week 76 必查项，需区分 Electron/V8 warm-up、renderer reload retention 与测试插桩成本，并补 idle/GC 稳态采样。

## Package 身份

- Desktop exe：222,753,280 bytes；SHA-256 `E1118E80A0166D6610A22D62D13FDF7AAA012EBC46A35273F842B4179F2D1C0D`
- Packaged AppHost：79,916,552 bytes；SHA-256 `B0CA02FC6F975AC8DE01868FF92827F138686631BB46E93EFFE8F4CC3A436A66`
- Electron：`41.1.0`；package 来自本次源码与版本匹配的本地 Electron cache。

## Week 76 输入

- 以 reload working-set `+22.2%` 和 512,800 KiB 峰值为 performance investigation 起点，增加 idle 回落、renderer PID/heap、listener/request count 观测。
- 审计 bounded approval/cancel conflict reconciliation，确认同 mutation identity、request identity guard 与最多 4 次上限符合 security/no-replay 约束。
- 复核 production package inventory、CSP/navigation/clipboard/path redaction、keyboard/focus/contrast/reduced-motion。
- Real model、real MCP 与 real Gerber/ImageMagick correctness 继续保持显式 opt-in，不得由本周 fake-runtime control-plane 证据外推。
