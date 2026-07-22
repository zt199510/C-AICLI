# Week 77 执行回顾：CLI/Desktop 0.6.0 Release Acceptance

状态：Blocked

日期：2026-07-22

起点提交：`ed5822e6858023fabc5376b48f573dd684ee777f`

## 当前结论

Week 77 已按 `Blocked` 收尾。最终冻结 source revision 为 `c74f93f45b0cae4a06cd70ee6dbca1b333a5973e`；clean-source 自动化、两份独立 RC build、两份 packaged smoke 和 payload reproducibility 已完成。用户明确放弃 Windows Narrator 人工验收，因此 N1-N7 保持 `Skipped/Unproven`，G6 未关闭，0.6.0 不得声明 `Accepted`。

正式双候选比较按设计 fail closed，最终 evidence 为 `Failed`，原因是 `Narrator evidence is not a Passed manual run.`。没有创建或推送 tag，没有上传制品，也没有发布 GitHub Release。

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

## 最终冻结 revision 与 clean-source 结果

最终 source freeze 为 `c74f93f45b0cae4a06cd70ee6dbca1b333a5973e`（branch `week-02-cli-commands-doctor-config`）。以下 evidence 均绑定该 revision：

- `.NET` 首次全量为 `1397/1400`。三个首次失败分别为 fake-driver timeout 后仍见 live PID、MCP 临时 root 瞬时 in-use，以及 detached child cleanup timing；这些失败不以重跑覆盖。受影响定向矩阵随后 `3/3`，diagnostic 全量为 `1400/1400`。测试留下一个空的 test-owned 临时目录；确认无存活进程，但删除动作被执行策略拒绝，因此不声明已删除。
- `npm run verify`：Desktop `23 files / 102 tests`，contracts/notices/accessibility/typecheck/lint/security/build 全部通过；`npm audit` 为 0 vulnerability。
- Desktop E2E：unpacked `9/9`、packaged `8/8`。
- `npm run measure:accessibility`：unpacked + packaged hardening `2/2`，process/temp delta `0/0`。
- `npm run measure:smoke`：packaged success/deny/cancel/crash/restart/corrupt-state/read-only/hardening `8/8`，process/temp delta `0/0`。
- Protocol evidence：`3/3` 通过；每轮 `48/48` response，process/temp delta `0/0`。
- CLI ReleaseAcceptance build 和默认 smoke 通过；win-x64 archive SHA-256 为 `C99DC3893B80642FCAB629115985A37716B8D093024A03E63A22974BA5FB23C6`。
- real model、daemon/API、real Gerber/TIFF 未执行，保持 `Skipped/Unproven`。

## 最终 Performance hard Gate

`artifacts/desktop-performance/week77-performance.json` 为 `Passed`：packaged baseline 5/5、long-session profile 5/5，package Gate 为 true，所有 process/temp delta 为 0。package 相对 Week 66 增长 `6.13%`，`app.asar` `-73.53%`，AppHost `+0.45%`。

| Profile | Idle working set | Idle private bytes |
|---|---:|---:|
| 1 | `-3.794%` | `-6.200%` |
| 2 | `+2.900%` | `+1.653%` |
| 3 | `+9.228%` | `+8.999%` |
| 4 | `+9.158%` | `+8.593%` |
| 5 | `-3.829%` | `-8.782%` |

5 个 profile 均低于不变的 15% hard Gate，workers 为 1、retries 为 0。

## 双候选 identity、smoke 与 comparison

- Candidate A：`artifacts/desktop-rc-week77-c74-a/0.6.0-rc.1`
- Candidate B：`artifacts/desktop-rc-week77-c74-b/0.6.0-rc.1`
- 两份 archive SHA-256：`1A258F9412B47D3199B2BFA2072AB37DC97412ABC38A2A6DE2AAB1E58746EF3B`
- Desktop SHA-256：`2D7B6598352665B64527F16D006B4818AFD5EC9DE43FB00693B5AE5A86F93FEA`
- AppHost SHA-256：`C69CF9DCA3BFFBFE95E38568642FDEA7C438EFD0D7D3D3F47F59FDC3434C9EC2`

Candidate A/B 各自 packaged smoke 均为 `8/8`，cleanup `0/0`，并分别绑定相同 archive hash。两份 payload inventory 均为 `78/78`，逐文件无差异；除预期的 `buildTimestampUtc` 外 manifest identity 一致，archive、Desktop、AppHost、ASAR 与 notices 一致。

Blocked 收尾时，正式 comparison 工具先暴露两个工具兼容性问题：Windows PowerShell 5.1 不支持 `[IO.Path]::GetRelativePath`，以及默认 ANSI 读取会破坏 UTF-8 evidence。分别以提交 `8a9ce9b` 和 `e00cff1` 修复并补脚本测试；这些是 closeout tooling 修正，不改变两份 candidate 的冻结 source revision。最终 comparison 通过 identity、payload 和两份 smoke 校验后，在 Narrator Gate fail closed：

```text
status: Failed
failure: Narrator evidence is not a Passed manual run.
```

## Narrator 与最终决定

用户明确放弃 Narrator 环节。`artifacts/desktop-acceptance/narrator-accessibility.json` 如实记录 `status: Skipped`、`manualNarratorRun: false`，N1-N7 全部为 `Skipped`；没有填写人工通过时间，也没有把自动 accessibility 结果冒充 Narrator 结果。

最终决定：**Blocked**。自动化与双候选结果保留，但 G6 未关闭，不生成或推广最终 release；后续若重新启动 Narrator 验收，必须以当时明确授权和可验证的人工 evidence 重新作决定。
