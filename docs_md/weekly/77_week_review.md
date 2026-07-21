# Week 77 执行回顾：CLI/Desktop 0.6.0 Release Acceptance

状态：In Progress

日期：2026-07-21

起点提交：`ed5822e6858023fabc5376b48f573dd684ee777f`

## 当前结论

Week 77 已开始执行，尚未形成 release decision。用户曾确认第一版 source freeze，并形成提交 `b03fb987954ae7c8955ef546491d85101f747e2e`；该 revision 的 clean-source 回归发现必须修复的 performance evidence 与 renderer retention 问题，因此其 evidence/candidate 资格已废弃。修复后的新 revision 尚待再次 source freeze；未生成 RC，未创建 tag，未推送或发布制品。

## 首次失败与修复

- Week 76 保留失败：renderer private-bytes retention 曾为 `17.72%` 与 `17.11%`，不能由后续重跑覆盖。
- 诊断确认 Week 76 long-session 使用“第一次 reload 后单点”对“完整 workload 后 30 秒单点”的非对称口径，且 role 聚合丢失 PID。Week 77 改为 warm/post 两侧相同 30 秒窗口、5 秒原始采样、稳定后 renderer median、逐 PID/role 记录；阈值仍为 15%，retries 仍为 0。
- .NET 定向脚本测试首次被 Windows Defender 短暂锁住 `CSharpAiCli.AgentFramework.dll` 的 Release obj 输出；记录首败后关闭 build server，受影响 `4/4` 测试通过。
- 连续执行 `verify → package:dir` 时，首次 package build 被 Windows 短暂拒绝重写已生成的 `desktop-contracts.ts`（Node `UNKNOWN`）；没有修改源码或 Gate，单独重跑 `package:dir` 后成功。最终 AppHost SHA-256 更新为 `08E83C05AC42347BA30A2013CA1EEE9758D79B4A42D80132E5B88340E859983A`。
- 第一版 freeze 的 clean-source 回归中，首次 `npm ci` 停在 Electron postinstall；只终止该次安装拥有的 npm/cmd/node 进程树后，使用仓库 `.electron-cache` 作为显式 cache 重跑成功，`npm audit` 为 0 vulnerability。该环境失败保留在 review，不视为首轮成功。
- 同一 revision 的 clean `npm run test:e2e:performance` 首轮 private-bytes retention 为 `21.54%`，随后完整单命令 5-profile Gate 的 profile 1 为 `18.16%`，其余 4 个通过；packaged baseline 子进程也曾非零退出且旧脚本未保存该轮 JSON。该 revision 的 performance evidence 判定失败，不以重跑覆盖。
- 修复把固定 30 秒窗口明确拆成 20 秒 settle 加末 3 个固定采样点的中位数，warm/post 两侧完全对称，保留全部 7 个原始样本、阈值 `15%` 与 retries `0` 不变；直接运行也会写入唯一命名的失败 JSON。packaged baseline schema 升至 v2，每轮无论成功或失败都先记录 status、原始 samples、process/temp delta 与安全化 failure，再决定命令退出码。
- 产品侧修复在 `closeTerminal` 成功后释放 renderer 中已关闭终端的完整 scrollback 投影并补回归断言，避免面板隐藏后继续持有大输出。新 bundle 的单 profile 为 working set `8.63%`、private bytes `7.62%`、cleanup `0/0`。
- 第二版 freeze `27c4abecba3c04e40d48378061dcaeed8bb07704` 的 clean-source `.NET` 首轮为 `1398/1399`：未修改的原子文件替换并发测试一次返回 “Thread could not be updated”；该单测随后连续 `5/5`、全量矩阵 `1399/1399` 通过，按瞬时 Windows 文件争用记录首败，不覆盖。
- 同一 revision 的 clean performance 中，5 个 long-session profile 全部通过，但 packaged baseline 第 4/5 轮在根进程退出后固定 500ms 检查点仍见 1 个 owned child，稍后系统检查已无残留，聚合 Gate 正确为 `Failed`。该 revision 的 evidence/candidate 资格已废弃；baseline cleanup 改为最多 10 秒、每 250ms 检查已知 owned PID，最终 Gate 仍要求 delta 为 0，并在超时时保存剩余 PID/role。
- 第三版 freeze `a44c12724569b661e7c8484e1f03ce86426695f7` 的 clean 自动化矩阵全部通过，但 Candidate A 在 staging 的绝对路径/secret 扫描处被 Windows PowerShell 5.1 拒绝：脚本使用了仅较新运行时支持的 `string.Contains(value, StringComparison)` 重载。候选尚未移动到 final root，staging 已清理，Candidate B 未启动；该 revision 的 candidate 资格废弃。修复改用 PowerShell 5.1/.NET Framework 支持的 `IndexOf(value, StringComparison) -ge 0`，安全检查语义不变。
- 第四版 freeze `e6c374c6e80e7d8c3b0bb217b629e498c08bb5c6` 的 `.NET` 首轮再次在相同 `Concurrent_reads_reconcile_atomic_manifest_and_turn_replacements` 测试失败（`1398/1399`），因此不再归类为单次环境噪声。产品修复仅在 Windows manifest overwrite 原子替换遇到 sharing/access-denied 时做最多 8 次、递增 25ms 的有界重试；不可变记录写入、路径错误和其他 I/O 错误仍立即 fail closed，并新增真实短暂 reader lock 回归测试。
- 第五版 freeze `f5f06bf388d1d5a612d2213a67d999d00ba9665d` 的首轮失败来自新增测试夹具：锁释放使用线程池 `Task.Run`，在全量并行测试压力下没有在预期 75ms 调度，导致测试自身超过产品的有界重试窗口（`1399/1400`）。夹具改用专用线程固定释放锁；产品重试次数、间隔和 fail-closed 边界均未改变。

## Pre-freeze Performance 结果

单命令先完成 packaged baseline 5 runs，再以单 worker、零 retry 连续执行 5 个隔离 long-session profile。package 相对 Week 66 增长 `6.13%`，`app.asar` `-73.53%`，AppHost `+0.44%`；所有 packaged baseline 与 long-session process/temp delta 为 0。

| Profile | Idle working set | Idle private bytes | Reload transient peak |
|---|---:|---:|---:|
| 1 | `+8.66%` | `+7.87%` | `+27.31%` |
| 2 | `+7.38%` | `+8.23%` | `+27.93%` |
| 3 | `+9.86%` | `+10.73%` | `+27.53%` |
| 4 | `+7.53%` | `+8.68%` | `+26.79%` |
| 5 | `+9.99%` | `+10.22%` | `+27.75%` |

全部 idle retention 低于不变的 15% Gate。由于 source dirty，聚合证据状态正确为 `Measured`；clean source revision 后必须原命令全量重跑为 `Passed`。

## Pre-freeze Accessibility 与 Packaged Evidence

- `npm run verify`：Desktop `23 files / 102 tests`，contracts/notices/accessibility/typecheck/lint/security/build 全部通过。
- `npm run measure:accessibility`：unpacked + packaged hardening `2/2`，process/temp delta `0/0`；dirty source 状态 `Measured`。
- `npm run measure:smoke`：packaged success/deny/cancel/crash/restart/corrupt-state/read-only/hardening `8/8`，process/temp delta `0/0`；dirty source 状态 `Measured`。
- 新增 candidate-root 参数化 packaged runner 与双候选 comparison validator；后者要求逐文件 inventory、archive、Desktop/AppHost/ASAR、notices 一致，两份 `8/8` smoke 分别绑定 archive hash，并校验具名人工 Narrator `7/7` evidence。

## Pending

- 在最终 package 上由人工执行 Windows Narrator 7 步 checklist。
- 用户确认修复后的新 source-freeze commit；第一版 freeze revision 不再用于 acceptance。
- clean revision 全量 .NET/Desktop/CLI/security/accessibility/performance/protocol 回归。
- 两份独立 RC build、逐文件/manifest/archive comparison 与各自 packaged acceptance smoke。
- 最终 `Accepted` 或 `Blocked` 决定。
