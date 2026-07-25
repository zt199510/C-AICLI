# Week 81 执行计划：Renderer Memory Retention 最小产品修复

状态：Waiting for Week80 Diagnosis

创建日期：2026-07-25

所属阶段：Week80 `Diagnosis Complete` 后的单一 P1 修复

建议分支：`codex/week81-renderer-memory-remediation`

## Entry Gate

Week81 只有在以下文件存在且结论为 `Diagnosis Complete` 时才可启动：

- `docs_md/weekly/80_week_review.md`
- `artifacts/week80-renderer-private-bytes/diagnosis.json`
- `artifacts/week80-renderer-private-bytes/diagnosis-handoff.json`

新对话必须从 handoff 指定的 exact clean revision 创建分支，并重新确认：

- source HEAD；
- package/AppHost identity；
- root cause category；
- affected files；
- reproduction/control profiles；
- required deterministic tests；
- prohibited shortcuts。

任一项缺失、冲突或仍为假设时，Week81 状态保持 `Blocked`，返回 Week80；不得自行挑一个可能的组件开始改。

## Goal

只修改 Week80 evidence 支持的最小产品边界，关闭 Renderer private-bytes retention 根因，同时保持：

- `desktop-v1` 不变；
- production real runtime 不 fallback；
- durable timeline ordering 和 identity 不变；
- write/shell 逐动作 durable approval 不变；
- ThreadStore 为权威事实源；
- crash/restart 无自动 replay；
- Renderer stale response protection 不退化；
- workspace guard、redaction、cleanup 和 tool boundary 不放宽。

Week81 只允许以下最终结论：

- `Candidate Ready for Requalification`：确定性回归、credential-free 全矩阵与诊断复测通过，形成新 clean revision 和 package identity。
- `Blocked`：根因假设被证伪、修复引入语义回归，或资源指标仍不满足 handoff 定义。

Week81 不得直接写 `Preview Ready`；真实 provider 最终重判属于 Week82。

## 明确禁止

- 不修改 15% threshold、warm/post idle window、workers 或 retries。
- 不通过 forced GC、定时 reload、清空用户可见历史或缩短 ThreadStore retention 过 Gate。
- 不丢 durable event、不把 unknown/interrupted 推断为 completed。
- 不减少审批、验证、Changes、Reports 或 recovery 可见性。
- 不用“只保留最后一个 turn”破坏完整 thread history。
- 不扩大成通用状态管理重写、协议升级、provider 重构或 UI redesign。
- 不读取 `.env.local`；Week81 默认是 credential-free 修复阶段。

## Phase 0：冻结 handoff 与失败复现

1. 读取 Week79 review、Week80 review 和 diagnosis handoff。
2. 验证工作树 clean，创建建议分支。
3. 运行 Week80 指定的最小 reproduction 和 control profile。
4. 确认失败指标、调用放大系数或 retained-type aggregate 与 handoff 一致。
5. 保存 Week81 pre-fix evidence，不能由 post-fix 结果覆盖。

Phase 0 Gate：根因可在 exact baseline 上重现；否则停止并返回 Week80。

## Phase 1：先写确定性回归

回归测试必须验证根因语义，而不是直接断言易抖动的 OS private bytes。

根据 diagnosis category 选择最小集合：

### Projection/resync

- N 个连续 thread-changed events 的 full resync 次数有明确上限；
- concurrent event burst 正确 coalesce；
- incremental fetch 使用 authoritative `afterSequence`；
- terminal/restart/workspace reopen 仍触发必要 full resync；
- stale detail response 不能覆盖较新 revision；
- duplicate event identity 不产生重复 fetch 或 timeline item。

### Reducer/state retention

- replace projection 后旧 timeline/detail 不再被 next state 引用；
- append/replace 均按 item identity 去重；
- state 中 timeline item 数不超过 authoritative projection；
- thread switch/workspace switch 清除旧上下文；
- composer draft、review 和 terminal state 不持有旧 Thread detail。

### Listener/DOM lifecycle

- mount/unmount 后 listener count 回到基线；
- repeated turn completion 不增加订阅；
- closed terminal/review projection 释放；
- timeline virtualization 的 mounted card 数保持 bounded。

### Provider transient object

- raw provider/runtime response 不进入 Renderer state；
- durable safe projection commit 后 transient bridge object 可释放；
- IPC request map、abort controller 和 pending promise 在 success/failure/cancel 后归零；
- redacted timeline payload 与 provider object identity 不共享长生命周期引用。

测试必须先在 baseline 失败，再在修复后通过。若无法形成确定性失败，停止并更新 Week80 diagnosis，不以资源 profile 代替回归。

## Phase 2：实施最小修复

### 若根因是 resync/full projection 放大

允许：

- 将 event burst 合并为一个在途 resync + 一个 dirty follow-up；
- 在安全情况下使用 `afterSequence` 增量获取；
- terminal、restart、workspace reopen 和 conflict reconciliation 保留 full authoritative resync；
- 增加 request cancellation/epoch guard，释放 obsolete response；
- 用 bounded numeric counters 验证调用次数。

不得：

- 丢弃 event；
- 仅依赖 Renderer optimistic state；
- 跳过 ThreadStore；
- 在 crash/restart 后延用旧内存状态。

### 若根因是 reducer/React 引用保留

允许：

- 让 replace 构造不引用旧 projection 的新 state；
- 清除已失效 workspace/thread/review/composer projection；
- 保持 timeline window 和 DOM virtualization bounded；
- memoize 仅限不会持有 superseded detail 的稳定值。

不得：

- 删除 durable history；
- 隐藏未完成 turn；
- 把 cleanup 建立在手工 reload 上。

### 若根因是 listener/request 生命周期

允许：

- 对 effect、IPC listener、abort controller、pending request map 增加确定性 dispose；
- 每个成功、失败、cancel、disconnect 分支统一释放；
- 保留 stale response ignore 语义和 client mutation identity。

### 若根因是 V8 reservation 而非 live retention

只有 Week80 handoff 已用 control/repeat batch 证明 stable plateau 时，才允许修改 measurement implementation。仍不得删除 private-bytes Gate；需要定义不依赖偶然低 warm median的新对称统计，并用 Week77/79历史 profiles 离线验证不会把已知失败判绿。

## Phase 3：定向测试

至少运行：

- 新增 deterministic regression；
- `desktop-state` reducer tests；
- `use-desktop-controller` tests；
- Timeline/ThreadSidebar/TaskControls tests；
- preload/IPC bridge tests；
- recovery、stale response、sequential approvals；
- production runtime/event ordering .NET tests；
- `git diff --check`。

任何以下回归为 P0/P1，立即停止：

- event 丢失却 completed；
- approval bypass；
- stale detail overwrite；
- thread switch 后错误 workspace；
- restart 自动 replay；
- changed files/reports 与磁盘不一致；
- listener/request cleanup 非零。

## Phase 4：Credential-free 完整矩阵

从修复后的 clean candidate 运行：

- full `.NET`；
- Desktop verify；
- dependency audit；
- package security/audit；
- unpacked E2E；
- packaged E2E；
- accessibility；
- packaged smoke；
- protocol measurement；
- Week77 frozen long-session 5 profiles，workers `1`、retries `0`；
- Week80 credential-free control matrix。

要求：

- 所有功能 Gate 通过；
- Week77 5 profiles 每个 working set/private bytes `<=15%`；
- process/temp/config delta 全为 0；
- source/package/AppHost identity 一致；
- 首败与修复前 evidence 保留。

## Phase 5：诊断复测

使用 Week80 的相同 reproduction/control profiles，不运行真实 provider：

- root-cause counter 回到预期上限；
- control 不退化；
- JS heap/DOM/listener/projection 指标不再线性增长；
- private bytes 方向与 root-cause counter 一致；
- 不使用 forced GC 或 reload 作为通过条件。

若 Week80 diagnosis 指定必须用 provider 才能复现，Week81 只完成 candidate；真实复验移交 Week82，不在本对话静默读取模型配置。

## Phase 6：形成 clean candidate

1. 提交 source、tests 和必要的诊断 tooling。
2. 确认 `git status` clean。
3. 重建 package。
4. 记录：
   - candidate source revision
   - package SHA-256/bytes
   - AppHost SHA-256/bytes
   - changed files/lines
   - tests/gates
   - cleanup delta。
5. 创建：
   - `docs_md/weekly/81_week_review.md`
   - ignored `artifacts/week81-renderer-memory-remediation/`
   - `week82-handoff.json`。

`week82-handoff.json` 至少包含：

- exact candidate revision；
- package/AppHost identity；
- fixed root cause；
- changed components；
- regression tests；
- affected real-model surfaces；
- required Phase6/7/8/9 reruns；
- prohibited evidence shortcuts。

## Critical Gates

- [ ] W81-G0 Week80 handoff 完整且 exact baseline 可复现。
- [ ] W81-G1 新 regression 在 baseline 失败、修复后通过。
- [ ] W81-G2 修改范围与 root cause 一致，无协议/权限扩大。
- [ ] W81-G3 stale response、approval、recovery 和 timeline ordering 不退化。
- [ ] W81-G4 full credential-free matrix 通过。
- [ ] W81-G5 Week77 5-profile Gate 与 Week80 controls 通过。
- [ ] W81-G6 candidate revision、package/AppHost identity 可追踪且 cleanup 为 0。
- [ ] `git diff --check` 和 evidence validator 通过。

## 新对话启动指令

Week80 完成后复制以下内容到新对话，并把占位符替换为 handoff 中的值：

```text
开始执行 docs_md/weekly/81_week_renderer_memory_remediation.plan.md。
严格读取 Week80 review、diagnosis.json 和 diagnosis-handoff.json；
从 handoff 指定的 exact clean revision 开始。
只允许实施 diagnosis evidence 支持的最小产品修复；
先写会在 baseline 失败的 deterministic regression。
不得修改 15% Gate、增加 retry、使用 forced GC/reload 过 Gate、丢弃 durable history，
也不得读取 .env.local 或运行真实 provider。
完成后形成 clean candidate 和 week82-handoff.json，不宣布 Preview Ready。
```
