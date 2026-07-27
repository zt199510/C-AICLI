# Week 81 Review：Renderer Memory Retention 最小产品修复

状态：`Blocked`

日期：2026-07-27

handoff 起点：`960b230683226e7b313f31fbb771065702a54bc5`

最终受测 source revision：`961a12c57b85e93d59a1a2486e8a41dbbab6cee3`

## 最终决定

Week81 **不能**写为 `Candidate Ready for Requalification`。Week80 指定的 deterministic regression 已完成 baseline 红、修复后绿，最小 Renderer 产品修复、完整 .NET、Desktop verify、package security 和定向 recovery/read-only E2E 均通过；但冻结的 Week77 五 profile Gate 在同一 clean revision、workers `1`、retries `0` 下只有 `3/5` 通过，private bytes 有两项超过 `15%`。一个同根因边界内的额外 memo 候选在新 clean revision 上为 `0/5`，已撤销且失败 evidence 保留。

按 Week81 Critical Gate，任一 Week77 profile 超线都会阻止 candidate。没有修改 15% Gate、workers、retries 或窗口，没有运行 forced GC，也没有用 reload 作为通过条件。由于 W81-G5 失败，后续 full packaged/accessibility/protocol 和 Week80 control rerun 按 gate 停止；不得把 Week80 同包历史绿灯替代本轮失败。

## Entry Gate 与确定性红绿

- Week80 review、`diagnosis.json`、`diagnosis-handoff.json` 均存在，结论为 `Diagnosis Complete`。
- 工作分支从 handoff 的 exact clean `960b230…` 创建。
- baseline 新增 6 项根因回归，结果为 `6 failed / 7 passed / 13 total`：
  - 8 个 turn 生命周期通知只允许 2 次 authoritative resync；
  - 五 turn history 在用户展开前不 materialize event cards；
  - Composer queued-intent DOM 身份稳定；
  - Composer 状态更新无 child-list mutation；
  - TaskControls DOM 身份稳定；
  - recovery banner DOM 身份稳定。
- 最小修复后同组结果为 `13/13`。

baseline evidence 与 fixed evidence 分别保存在 ignored `artifacts/week81-renderer-memory-remediation/baseline-regression.json` 和 `fixed-regression.json`，未互相覆盖。

## 最小产品修改

修改范围只包含 Week80 evidence 指向的 Renderer projection/commit 边界：

- 所有 `thread.changed` 仍 dispatch；authoritative resync 仅在两个重复 committed-sequence 生命周期边界排队，保留 workspace/thread 切换、非 updated 事件与 duplicate identity 语义。
- 历史 timeline 按 turn 分组，用户展开某个 turn 后才 materialize 该 turn 的 event cards；ThreadStore durable history 未删除或截短。
- Composer、TaskControls、recovery/refresh 状态使用稳定 DOM 身份，减少短生命周期节点 churn。
- read-only E2E 增加显式打开 turn 的操作，保持新交互的语义覆盖。

相对 handoff 起点共 11 个文件，`207 insertions / 45 deletions`。没有协议、provider、权限、approval、workspace guard、redaction 或 durable store 修改。

## 验证结果

| Gate | 结果 | 证据 |
| --- | --- | --- |
| deterministic regression | Passed | baseline 6 项必败；修复后 `13/13` |
| Desktop verify | Passed | 25 files，`109/109`；typecheck、lint、build、production security 通过 |
| full .NET | Passed | `1421/1421`，skipped `0`，锁定 SDK 9.0.308 |
| dependency audit | Passed | `0 vulnerabilities` |
| package audit | Passed | 78 files、12 asar entries、forbidden payload `0` |
| recovery E2E | Passed | unpacked recovery `6/6` |
| read-only timeline E2E | Passed | unpacked + packaged `2/2` |
| unpacked E2E 首轮 | Failed | `7/9`；read-only 旧交互已修正，long-session private bytes `16.81%` 仍失败 |
| Week77 frozen five profiles | **Failed** | `3/5`；private bytes `14.26%、14.61%、18.14%、16.33%、14.62%`；working set 全通过，cleanup 全为 0 |
| memo 对照五 profiles | **Failed** | `0/5`；private bytes `16.21%、17.16%、17.53%、17.49%、16.91%`；增量已撤销 |
| full packaged/accessibility/protocol | Not run | W81-G5 已失败，停止扩大验证 |
| Week80 credential-free control rerun | Not run | W81-G5 已失败；历史同包结果不冒充本轮 rerun |
| `git diff --check` | Passed | 无 whitespace error |

第一次 package audit 因测试自有 `apps/desktop/artifacts/` 被误包含而失败；只删除了该空的测试自有目录，根目录 Week81 evidence 未删除。随后从 clean revision 重建并通过 package audit。该首败未被隐瞒。

## 最终 package identity

- Desktop executable SHA-256：`E2A5BE23B0598ACA5827FC3977C48418298AE888D9293D1D51B937528BBB7A38`
- Desktop executable bytes：`222753280`
- AppHost SHA-256：`DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`
- AppHost bytes：`79941168`
- package security：78 files、12 asar entries、forbidden payload `0`

package 已从最终受测 clean revision 重建。未 push、tag、上传、发布或部署。

## Critical Gates

| Gate | 结论 |
| --- | --- |
| W81-G0 handoff 完整且 exact baseline 可复现 | Passed |
| W81-G1 regression baseline 失败、修复后通过 | Passed |
| W81-G2 修改范围与 root cause 一致 | Passed |
| W81-G3 stale response、approval、recovery、timeline ordering | Passed（定向回归） |
| W81-G4 full credential-free matrix | **Not closed** |
| W81-G5 Week77 5 profiles 与 Week80 controls | **Failed** |
| W81-G6 candidate identity 与 cleanup | **Not closed** |
| `git diff --check` | Passed |

## 后续解除条件

1. 返回 Week80 诊断边界，解释为何同一 `E2A5…` package 在 provider profiles 通过但 Week77 frozen fixture 的 post-idle private bytes 稳定为约 `69–75 MB`，并形成新的 deterministic mechanism regression。
2. 只在新增 evidence 锁定组件后实施新的最小修复；不得继续猜测 terminal、review 或通用 state rewrite。
3. 新 clean revision 必须重新执行 full credential-free matrix、Week77 五 profiles 与 Week80 controls；五个 profile 都必须同时满足 working set/private bytes `<=15%`。
4. 只有上述条件关闭后，才能生成 `Candidate Ready for Requalification` handoff 并进入 Week82 Phase 6/7/8/9。
