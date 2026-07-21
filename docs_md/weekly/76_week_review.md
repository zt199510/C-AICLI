# Week 76 执行回顾：Security、Accessibility、Performance 与 Release Candidate

状态：Blocked

日期：2026-07-18

起点提交：`d564d9920224f45e1987a0aa239904cabca731c6`

## 结论

Week 76 的自动化 security、accessibility、package audit、performance/protocol measurement 与 RC 构建护栏已经落地。最终 package 维持 exact `39 invoke + 2 event` reviewed surface，自动化未发现高风险 trust-boundary 旁路，package denylist 命中数为 0，`npm audit` 返回 0 vulnerability；键盘闭环、ARIA、focus restoration、WCAG AA contrast、reduced motion、forced colors、四个视口和 Electron 200% zoom 均有自动化回归。

Week 76 不能标记 Passed，也没有生成 `0.6.0-rc.1`：长会话 renderer private-bytes retention 曾两次达到 `17.72%` 与 `17.11%`，可重复超过 15% 硬门槛；后续两个独立 profile 虽回落到 `12.67%` 与 `14.78%`，但诊断重跑不能抹去首轮失败。Windows Narrator 人工 smoke 未执行；当前实现工作树也不是 clean source。RC 脚本按设计 fail-fast 返回 `Release candidate requires a clean Git source tree.`，因此没有 candidate、archive 或 RC smoke 证据。

## 主要实现

- Security/package：增加 package/ASAR inventory 与 denylist 审计，检查 CSP、navigation、new-window、webview、permission、download、drop、preload/renderer forbidden surface、Electron licenses、AppHost 与 Magick.NET redistribution notice；生产 package 自动使用版本匹配的本地 Electron archive。
- Accessibility：composer mention 完整键盘模型与 `aria-activedescendant`；review tabs roving focus；thread create/rename/archive、drawer、restart confirmation 与 mutation completion 的 focus restoration；live status/alertdialog 语义；reduced-motion、forced-colors、focus indicator 与 WCAG AA contrast checks。
- Protocol/security tests：补充 invalid/negative/oversized/truncated `Content-Length`、navigation/window/permission/download 拒绝、exact bridge surface 与 secret/path 不泄漏检查。
- Performance：重写 5-run packaged baseline，按 Main/Renderer/GPU/Utility/AppHost PID 记录 working set/private bytes、30 秒 idle、package size、进程与临时目录清理；新增三轮 framing burst benchmark。
- Release：新增 clean-source RC orchestrator，校验 source/AppHost/contract identity、evidence status、payload inventory、checksums、notices、root containment、reparse point、secret/path scan、覆盖保护与 deterministic archive。
- Store concurrency：atomic manifest replacement 的 bounded reader 使用 `FileShare.ReadWrite | FileShare.Delete`，避免读句柄阻断 writer；稳定重读与 fail-closed 语义保持不变。

## 最终自动化证据

| Gate | 结果 |
|---|---|
| `.NET full suite` | `1398/1398` Passed，0 skipped，约 1m33s |
| AppHost build | Debug/Release 均 `0 warnings / 0 errors` |
| Desktop verify | contracts/notices/accessibility/typecheck/lint/security/build Passed；`21 files / 97 tests` |
| Unpacked E2E | `9/9` Passed，single worker，retries 0，约 1.4m |
| Packaged E2E | `8/8` Passed，single worker，retries 0，约 1.1m |
| Accessibility hardening | unpacked + packaged `2/2`；四视口、200% zoom、reduced motion、forced colors、ARIA snapshot |
| Package audit | 78 files、12 ASAR entries、39 invoke、2 event、0 forbidden payload |
| Dependency audit | `npm audit --audit-level=high`：0 vulnerability |
| RC dirty-source guard | Passed：真实 dirty tree 被拒绝，未产生 candidate |

## Package 与 identity

- Package：`464,704,992` bytes、78 files；相对 Week 66 `437,851,431` bytes 增长 `6.13%`，低于 15%。
- `app.asar`：`575,912` bytes，SHA-256 `8A0830A29DE4F621B6E6C22B31E3783F0984D8CB91B3ED42F99BBEA6D03267B3`；相对 Week 66 同口径减少 `73.53%`。
- AppHost：`79,916,552` bytes，SHA-256 `3EA3C27A54CB106ED098DFE1600FDB2B43E532707B831DF263415C4441E025BE`。
- Desktop executable SHA-256：`D694717F955C83EA6096E3ABCD501A421439E0F164F77884CC8576012F318558`。
- 最终 performance/protocol JSON 均绑定上述 AppHost identity；因 source dirty，baseline 状态明确为 `Measured`，不能作为 clean RC 的 `Passed` evidence。

## Performance 与 protocol

5 次 packaged baseline 的 runtime-ready 采样为 `3884, 3929, 3934, 3938, 3969 ms`，median `3934 ms`、max `3969 ms`，均在 30 秒 deadline 内。首样 aggregate working set 为 `409,915,392` 至 `420,753,408` bytes；30 秒 idle 后为 `399,413,248` 至 `407,846,912` bytes。5 次 process/temp delta 均为 0。

冻结 long-session workload 为 240 timeline items、5 次 reload、50-row bounded diff、70 KiB terminal production/64 KiB retention 与 30 秒 idle。首轮和诊断轮分别出现 private-bytes retention `17.72%`、`17.11%`，因此 performance hard Gate 未关闭；后续两个独立 profile 为：

| Profile | Idle working set | Idle private bytes | Reload transient peak |
|---|---:|---:|---:|
| 1 | `+7.05%` | `+12.67%` | `+45.69%` |
| 2 | `+6.90%` | `+14.78%` | `+43.07%` |

Protocol benchmark 每轮 48 frames、max in-flight 8、burst cap 64，三轮吞吐为 `132.46 / 120.15 / 111.40 fps`，response `48/48`，仅返回预期 `method-not-found`，peak working set 约 33.3 MB，process/temp delta 均为 0。

证据文件：`artifacts/desktop-performance/week76-performance.json`、`artifacts/desktop-performance/week76-protocol.json`、`artifacts/desktop-performance/week76-current/long-session-1.json`、`long-session-2.json`。

## 首轮失败与修复记录

- Desktop contract test 在与 .NET 全量并行时触发 5 秒 wall-clock timeout；受影响 matrix 单独 `13/13` 通过，最终改为顺序运行并完成全量验证。
- Electron packager 首轮在 runtime lookup 阶段停滞；只终止本次 test-owned PID，并把 packager 改为自动发现 repo-local exact Electron archive，最终 package/audit 通过。
- Accessibility hardening 首轮发现 nested Escape 冒泡关闭外层 drawer、mention locator 在异步加载前取值、approval `region` 与既有 `group` contract 不兼容；分别修复事件传播、authoritative wait 与语义兼容。
- 原生 `window.confirm` 被替换为可访问 `alertdialog` 后，Week 75 restart E2E 仍在等待浏览器 dialog；测试改为实际键盘/按钮操作新确认对话框。
- 恢复矩阵首个冷态 approval 两次在 20 秒 harness wait 内仍为 `running`，后续同路径通过；最终状态 wait 调整为 40 秒，冷启动性能继续由独立 30 秒 hard deadline 约束。最终 unpacked/packaged 完整矩阵通过。
- .NET 首轮遇到 stale testhost 文件锁，随后 4 个失败包含新脚本文本断言、测试变量触发 architecture token、atomic reader 阻断 writer 与既有 MCP temp cleanup 抖动；修正新断言/变量，Store 采用 atomic-replace compatible file sharing，affected `4/4` 后全量 `1398/1398`。
- Long-session 首轮 private-bytes retention 为 `17.72%`，后续诊断又出现 `17.11%`；该失败没有改写为 Passed，作为本周 performance Blocker 保留。

## Skipped / Unproven

- Windows Narrator 的启动、thread/composer、approval、review、error/recovery 人工 smoke：未执行。
- Real model、real MCP、real Gerber/Gerbv/ImageMagick/LibTIFF/Magick.NET correctness：未执行，不由 fake-runtime/control-plane 证据外推。
- Clean-source `0.6.0-rc.1` build、archive 与默认 RC smoke：未执行；dirty-source guard 已证明拒绝。

## Week 77 输入

Week 77 不应接受当前 revision 为 release candidate。进入 acceptance 前必须：定位并关闭可重复的 renderer private-bytes `>15%` retention；在 Windows Narrator 完成规定人工闭环；由用户确认并形成 clean source revision；从该 revision 重新生成状态为 Passed 且 identity 一致的 accessibility/performance/smoke evidence，再运行 RC orchestrator 生成 candidate、checksums、inventory、archive 与默认 packaged smoke。
