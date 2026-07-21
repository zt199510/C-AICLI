# Week 77 执行回顾：CLI/Desktop 0.6.0 Release Acceptance

状态：In Progress

日期：2026-07-21

起点提交：`ed5822e6858023fabc5376b48f573dd684ee777f`

## 当前结论

Week 77 已开始执行，尚未形成 release decision。版本/文档与 source-freeze 前修复正在工作树中；未获得用户 source-freeze 确认，未生成 RC，未创建 tag，未推送或发布制品。

## 首次失败与修复

- Week 76 保留失败：renderer private-bytes retention 曾为 `17.72%` 与 `17.11%`，不能由后续重跑覆盖。
- 诊断确认 Week 76 long-session 使用“第一次 reload 后单点”对“完整 workload 后 30 秒单点”的非对称口径，且 role 聚合丢失 PID。Week 77 改为 warm/post 两侧相同 30 秒窗口、5 秒原始采样、renderer median、逐 PID/role 记录；阈值仍为 15%，retries 仍为 0。
- .NET 定向脚本测试首次被 Windows Defender 短暂锁住 `CSharpAiCli.AgentFramework.dll` 的 Release obj 输出；记录首败后关闭 build server，受影响 `4/4` 测试通过。
- 连续执行 `verify → package:dir` 时，首次 package build 被 Windows 短暂拒绝重写已生成的 `desktop-contracts.ts`（Node `UNKNOWN`）；没有修改源码或 Gate，单独重跑 `package:dir` 后成功。最终 AppHost SHA-256 更新为 `08E83C05AC42347BA30A2013CA1EEE9758D79B4A42D80132E5B88340E859983A`。

## Pre-freeze Performance 结果

单命令先完成 packaged baseline 5 runs，再以单 worker、零 retry 连续执行 5 个隔离 long-session profile。package 相对 Week 66 增长 `6.13%`，`app.asar` `-73.53%`，AppHost `+0.44%`；所有 packaged baseline 与 long-session process/temp delta 为 0。

| Profile | Idle working set | Idle private bytes | Reload transient peak |
|---|---:|---:|---:|
| 1 | `+8.25%` | `+7.82%` | `+26.78%` |
| 2 | `+6.90%` | `+4.97%` | `+25.09%` |
| 3 | `+8.40%` | `+10.39%` | `+27.80%` |
| 4 | `+9.53%` | `+8.69%` | `+28.16%` |
| 5 | `+7.45%` | `+8.50%` | `+27.27%` |

全部 idle retention 低于不变的 15% Gate。由于 source dirty，聚合证据状态正确为 `Measured`；clean source revision 后必须原命令全量重跑为 `Passed`。

## Pre-freeze Accessibility 与 Packaged Evidence

- `npm run verify`：Desktop `23 files / 102 tests`，contracts/notices/accessibility/typecheck/lint/security/build 全部通过。
- `npm run measure:accessibility`：unpacked + packaged hardening `2/2`，process/temp delta `0/0`；dirty source 状态 `Measured`。
- `npm run measure:smoke`：packaged success/deny/cancel/crash/restart/corrupt-state/read-only/hardening `8/8`，process/temp delta `0/0`；dirty source 状态 `Measured`。
- 新增 candidate-root 参数化 packaged runner 与双候选 comparison validator；后者要求逐文件 inventory、archive、Desktop/AppHost/ASAR、notices 一致，两份 `8/8` smoke 分别绑定 archive hash，并校验具名人工 Narrator `7/7` evidence。

## Pending

- 在最终 package 上由人工执行 Windows Narrator 7 步 checklist。
- 用户确认 source-freeze commit。
- clean revision 全量 .NET/Desktop/CLI/security/accessibility/performance/protocol 回归。
- 两份独立 RC build、逐文件/manifest/archive comparison 与各自 packaged acceptance smoke。
- 最终 `Accepted` 或 `Blocked` 决定。
