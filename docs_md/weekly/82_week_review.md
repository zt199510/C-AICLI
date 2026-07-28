# Week 82 Review：Desktop Provider Preview 重认证

日期：2026-07-28

分支：`codex/week82-desktop-preview-requalification`

产品候选 revision：`e9e062d985545377aa373767f563de6a2bb30a64`

## 最终决定

**Blocked**

Week82 不能标记为 `Preview Ready` 或 GO。W82-G0、G1、G2 已关闭；packaged real-provider recovery 在 durable ThreadStore 已进入 `waiting-for-approval` 后，Renderer 仍停留在 `running`、`Refreshing…` 和 1 个 loaded item，approval card 为 0。该 `renderer-approval-projection-stale` 是一个开放 P1，因此 W82-G4 失败，后续 controlled-write 与五个 provider resource profiles 按 Gate 顺序停止。

这不改变 Week77 的正式发布决定：**0.6.0 formal release 继续 Blocked**。本周没有 push、tag、上传、发布或部署。

## 候选与 package 身份

- exact product candidate：`e9e062d985545377aa373767f563de6a2bb30a64`
- Desktop executable：`C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0`，`222753280` bytes
- package tree：`ADDFBC3114B10E31633F1E9A9500934B9B8F17BCD3222CD4D02217AA606C6E38`，`464708708` bytes，78 files
- app.asar：`D7959F8886DAE4F1E66068D728A8EF74C19231D8ACCA68F67A603FA2CE034C04`，`555012` bytes
- AppHost：`DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA`，`79941168` bytes

`11ebd16`、`7bfa3b7`、`2184089` 仅为 Week82 evidence、provider harness 与首败观测基础设施 revision，没有重新定义产品候选，也没有改变 package identity。

## 历史依据

- Week79 三轮 provider resource 失败原值完整保留：`18.74% / 47.77%`、`10.84% / 36.77%`、`10.77% / 35.84%`。
- Week80 root cause：完整 thread projection 与 transient Renderer materialization 造成 Blink/React allocation pressure。
- Week81 候选修复：通知保持可观测，权威 refresh 限制为每 turn 最多两个生命周期边界，历史 timeline/terminal materialization 有界，status DOM 稳定，ready review projection 去重。
- Week81 deterministic regression 在 handoff baseline 为红，候选为 `26/26` 绿；Week82 没有修改产品代码。

## Credential-free 最终矩阵

| Gate | 结果 |
| --- | --- |
| full .NET | Passed，`1421/1421`，skipped `0` |
| Desktop verify | Passed，25 files，`112/112` |
| dependency audit | Passed，vulnerabilities `0` |
| package audit | Passed，78 files、12 asar entries、forbidden `0` |
| unpacked E2E | Passed，`9/9` |
| packaged E2E | Passed，`8/8` |
| accessibility | Passed，`2/2` |
| packaged smoke | Passed，`8/8` |
| protocol | Passed，3 轮，每轮 `48/48` |
| Week77 frozen profiles | Passed，`5/5`，working set/private bytes 均 `<=15%` |
| Week80 controls | Passed，`C0-C7` |
| Week81 regression | Passed，14 suites，`26/26` |
| cleanup | process/temp/config 全部 `0` |

保留的 credential-free 首败包括 dotnet SDK resolution、错误 solution path、Week80 overprojection 与 callback control；最终成功结果没有覆盖这些记录。

## Provider read-only

用户于本对话独立授权 read-only、recovery 与 resource phases。三项配置值仅由测试进程读取并注入本次 packaged Desktop 子进程；没有写入 evidence、timeline、报告或 Git。

read-only 结果为 Passed：

- provider turns `1`，`workspace.read_text(global.json)` `1`；
- durable timeline items `6`，model/tool/final 连续且 item identity 唯一；
- approval/write/shell/changed files 均为 `0`；
- Renderer resync 为 `2/2`，在 Week81 上限内；
- Changes clean，Reports/Artifacts 为 `0`；
- process/temp/config delta 为 `0/0/0`。

## Recovery 首败与最终阻断

第一次 recovery attempt 中，provider 直接完成且没有请求工具：thread status `completed`、timeline items `3`、tool/approval `0`。同时发现初版 harness 把“观察到 AppHost PID”错误记成“已 crash”。该 attempt 经重新解释后保存在 `provider-recovery-first-failure.json`；没有 write、crash、restart 或 replay，cleanup 为 `0/0/0`。harness 只修正终态观测和实际动作计数，并将同一单工具请求表述得更明确，revision 为 `2184089`。

正式 rerun 到达了真实 durable approval 边界：

- ThreadStore：`waiting-for-approval`，1 turn，至少 1 model item、1 `tool.started`、1 `approval.requested`；
- Renderer：仍为 `running`、`Refreshing…`、1 loaded item；
- approval card：`0`；
- 在固定 UI wait bound 内没有收敛；
- 没有 approve、write、crash、restart 或 replay；
- cleanup 为 `0/0/0`。

这正是计划中禁止的 approval card 丢失或 stale Thread detail projection。由于产品候选身份被冻结，Week82 不在原候选上猜测修复，也不通过再次运行把失败刷绿。

## 停止的下游 Gate

- controlled write：`NotRun`。独立 write 授权未授予，且 W82-G4 已阻断本轮重认证。
- provider resource profiles 1–5：`NotRun`。resource 授权虽已授予，但按 Gate 顺序在 recovery 失败后停止；没有启动任何 profile，也没有单独重跑失败 profile。
- JS heap/DOM/listener/provider resync resource Gate：`NotRun`。

## Critical Gates

| Gate | 结论 | 依据 |
| --- | --- | --- |
| W82-G0 | Passed | exact candidate、package/AppHost identity 与历史 evidence 一致 |
| W82-G1 | Passed | credential-free 全矩阵通过 |
| W82-G2 | Passed | packaged provider exactly-one read，零越界与零 cleanup |
| W82-G3 | NotRun | 独立 write 授权未授予；G4 已阻断 |
| W82-G4 | **Failed** | `renderer-approval-projection-stale`，durable approval 未投影为 UI card |
| W82-G5 | NotRun | 五个 provider resource profiles 在上游失败后停止 |
| W82-G6 | NotRun | provider resource JS/DOM/listener/resync 未测量 |
| W82-G7 | Passed（已执行范围） | 所有已执行场景脱敏且 cleanup `0/0/0` |
| W82-G8 | **Failed** | open P0 `0`，open P1 `1` |

## Week83 解除条件

1. 先建立 deterministic regression：ThreadStore 已提交 `waiting-for-approval`，Renderer 同时持有旧 `running` projection，并在 baseline 稳定复现 approval card 丢失或无限延迟。
2. 只依据该 regression 实施最小 Renderer resync/stale-response 修复；不得修改 provider、approval protocol 或 UI 设计。
3. 形成新的 exact clean candidate 并重建 package；不得把 Week82 文档/harness revision 当成产品候选。
4. 重跑受影响 credential-free 矩阵、provider read-only、controlled write、完整 recovery，以及不可拆分的五个 resource profiles。
5. working set/private bytes、JS heap/DOM/listener/resync、privacy 与 cleanup 必须全部关闭后，才能重新判定 Preview。

下一阶段交接：`artifacts/week82-desktop-preview-requalification/week83-handoff.json`。
