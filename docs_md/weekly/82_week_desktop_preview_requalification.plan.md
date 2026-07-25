# Week 82 执行计划：Desktop Provider Preview 全矩阵复验与重判

状态：Waiting for Week81 Candidate

创建日期：2026-07-25

所属阶段：Week81 memory remediation candidate 的独立 requalification

建议分支：`codex/week82-desktop-preview-requalification`

## Entry Gate

只有满足以下条件才可启动：

- Week80 最终结论为 `Diagnosis Complete`；
- Week81 最终结论为 `Candidate Ready for Requalification`；
- `week82-handoff.json` 存在；
- Week81 candidate source revision clean；
- package/AppHost identity 与 handoff 一致；
- credential-free full matrix 已通过；
- Week79/80 首败 evidence 仍保留。

任一条件不满足，Week82 立即 `Blocked`，不得自行重建一个不同 candidate 后继续。

## Goal

从 Week81 exact clean candidate 独立重建并重跑受 Renderer memory fix 影响的完整矩阵，证明：

1. provider read-only、controlled write、durable approvals、crash/restart 和 review projection 不退化；
2. Renderer working set/private bytes 在 5 个连续独立 provider-backed profiles 中均 `<=15%`；
3. source/package/AppHost identity、timeline、Changes、Reports、磁盘与 cleanup 一致；
4. 无开放 P0/P1 时，才允许把当前内部 Preview 资格写为 `Preview Ready`。

Week82 最终只能写：

- `Preview Ready`：全部 W82 Gates 关闭，只允许内部 Preview。
- `Blocked`：任一硬 Gate 未关闭或仍有 P0/P1。

即使 Week82 为 `Preview Ready`，0.6.0 正式 release 仍保持 Week77 `Blocked`。不得创建 tag、上传制品、发布 Release，或使用 `Accepted`、`Released`、`Production Ready`。

## 授权边界

### Credential-free Gate

可直接运行：

- source/package identity；
- .NET/Desktop/E2E/security/accessibility/protocol；
- fake/fixture/performance controls；
- evidence validator；
- disposable workspace/profile。

### Provider-backed Gate

新对话必须取得新的独立授权。Week79/80 授权不得复用。

授权建议分两级：

1. Read-only/recovery/resource：
   - `.env.local` 三项模型配置仅注入 packaged Desktop 子进程；
   - disposable projects；
   - 默认仅 `workspace.read_text`；
   - recovery 可在审批前 crash/cancel；
   - 不允许 write/shell。
2. Controlled write：
   - 单独明确授权；
   - project B；
   - 最多 2 个 allowlisted files；
   - diff `<=160` 行；
   - 一个 exact test command；
   - apply_patch 与 shell 分别 durable approval；
   - 不允许其他工具或网络。

凭据值不得进入父进程、测试输出、timeline、report、evidence 或 Git。

## Evidence 目录

创建 ignored 根：

```text
artifacts/week82-desktop-preview-requalification/
  manifest.json
  credential-free.json
  provider-readonly.json
  provider-write.json
  provider-recovery.json
  provider-resource-profile-1.json
  provider-resource-profile-2.json
  provider-resource-profile-3.json
  provider-resource-profile-4.json
  provider-resource-profile-5.json
  resource-summary.json
  final-summary.json
```

所有文件必须通过新 validator：

- exact source/package/AppHost identity；
- status/checks/counts/durations/cleanup delta；
- 无 secret、配置值、raw prompt/response、header、完整 environment、绝对路径或未经脱敏 diagnostics；
- completed 与 timeline/Changes/Reports/磁盘一致；
- failed attempt 不被后续结果覆盖。

## Phase 0：Candidate 冻结

1. 读取 Week79、Week80、Week81 reviews 与 handoff。
2. 验证 exact candidate HEAD、branch、clean status。
3. 从 candidate 重建 package，不复用旧 output。
4. 记录 package/AppHost identity，并与 Week81 handoff 比较。
5. 初始化 Week82 evidence 和 validator。
6. 确认 tracked dotenv count 为 0。

Phase 0 Gate：candidate 和 package identity 一致；无凭据读取；历史失败完整。

## Phase 1：Credential-free 全矩阵

运行：

- full .NET；
- Desktop verify；
- dependency audit；
- package security/audit；
- unpacked E2E；
- packaged E2E；
- accessibility；
- packaged smoke；
- protocol measurement；
- Week77 frozen performance 5 profiles；
- Week80 controls；
- Week81 deterministic regression。

要求：

- workers `1`、retries `0`；
- 任何首败单独保存；
- 不全局 kill process；
- 只清理 recorded owned PID/root；
- process/temp/config delta 为 0；
- `git diff --check` 通过。

任何 functional/security/recovery regression 先按 P0/P1 处理；不得进入 provider-backed 阶段。

## Phase 2：Provider read-only

仅在取得 read-only 授权后：

1. 从 candidate 创建 fresh disposable project A。
2. approval `never`。
3. 只暴露 `workspace.read_text`。
4. exactly one read `global.json`，然后 final。
5. 验证：
   - 真实 provider request；
   - 无 fake fallback；
   - model/tool/final durable timeline 连续；
   - approval/write/shell 为 0；
   - Changes clean；
   - Reports/Renderer/磁盘一致；
   - 无敏感值/rooted path；
   - cleanup 为 0。

## Phase 3：Controlled write

只有用户另行授权 write 后：

- fresh project B；
- allowlist 最多 2 个文件；
- deterministic failing baseline；
- diff `<=160` 行；
- exactly one patch；
- exactly one allowlisted target test command；
- 两个不同 durable approval；
- independent test rerun；
- project B 丢弃，不合并主仓库。

验收保持 Week79 Phase7 全部条件，并额外检查 memory fix 没有造成：

- approval card 丢失；
- Thread detail stale overwrite；
- event/turn identity 复用；
- changes/reports 延迟或重复；
- listener/resync counter 超过 Week81 上限。

## Phase 4：Crash/restart

fresh project-recovery：

1. 启动 provider-backed turn。
2. 等待 model/tool/approval durable boundary。
3. 只终止 recorded owned AppHost PID。
4. crash 后 fail closed、无 completed、无 auto restart/replay。
5. 显式 restart，权威重建 partial timeline。
6. 用户确认后创建新 attempt。
7. old/new turn、model/tool item、provider/approval identity 全部分离。
8. 完成或取消新 attempt。
9. Renderer/resync counters 回到 Week81 定义的 bounded 终态。
10. Changes/Reports/process/temp/config delta 一致。

## Phase 5：Provider resource 5-profile Gate

冻结每个 profile：

- fresh Desktop profile、fresh disposable project；
- 1 个 provider warm-up turn；
- warm window 30 秒；
- 5 秒 sample interval；
- 20 秒 settle；
- settled suffix 3 个样本取 median；
- 5 个 measured provider read-only turns；
- post-idle window 30 秒；
- working set：warm-window peak 对 post-idle settled median；
- private bytes：warm/post settled median；
- workers `1`；
- retries `0`；
- 不 forced GC；
- 不 reload 过 Gate；
- 不延长 idle；
- 不更改 workload。

每个 profile 必须：

- Renderer working set retention `<=15%`；
- Renderer private bytes retention `<=15%`；
- 无显著持续单调增长；
- JS heap/DOM/listener/resync counters 满足 Week81 上限；
- 6 个 provider turns、6 个 read calls；
- approval/write/shell/changed files 为 0；
- process/temp/config delta 为 0；
- 无敏感披露或未经授权网络。

5 个连续独立 profiles 全部通过才关闭资源 Gate。任一失败即整次 Gate failed；不得只重跑失败 profile 到绿。诊断重跑必须另建 evidence，不得替代原 5-profile 结果。

## Phase 6：一致性与隐私审计

跨所有 provider 场景核对：

- source/package/AppHost identity；
- model/tool/approval/provider call 数；
- turn/item/request identity 唯一性；
- timeline sequence 连续性；
- Renderer 与 ThreadStore authoritative projection；
- Changes/Reports/verification 与磁盘；
- workspace/config byte restoration；
- owned Main/Renderer/GPU/Utility/AppHost cleanup；
- secret/path/raw diagnostics/未经授权网络计数。

任何：

- secret/path 泄漏；
- approval bypass；
- duplicate execution；
- wrong workspace；
- event 丢失却 completed；
- unknown/interrupted 推断成功；
- orphan；

均为 P0/P1，Week82 立即 `Blocked`。

## Phase 7：最终重判

创建：

- `docs_md/weekly/82_week_review.md`
- `artifacts/week82-desktop-preview-requalification/final-summary.json`

Review 必须：

- 引用 Week79 三轮资源失败，不删除或改写；
- 引用 Week80 root cause；
- 引用 Week81 fix revision 和 regression；
- 列出 Week82 全部首败、修复和 rerun；
- 给出 W82 Gate table；
- 列出 P0/P1；
- 明确 0.6.0 formal release 仍 `Blocked`。

最终决定：

- 所有 Gate 通过、P0/P1 为 0：`Preview Ready`。
- 其他任何情况：`Blocked`。

不要把 Week79 review 的历史结论重写为从未失败。若 Week82 通过，在 Week79 review 添加带日期的 requalification 链接，说明旧 `Blocked` 已被新 revision 的 Week82 evidence supersede；原始数值和决定必须保留。

## Critical Gates

- [ ] W82-G0 exact candidate、package/AppHost identity 和 historical evidence 一致。
- [ ] W82-G1 full credential-free regression/package/security matrix 通过。
- [ ] W82-G2 provider read-only 通过。
- [ ] W82-G3 controlled write、双审批、测试和磁盘一致。
- [ ] W82-G4 crash/restart、identity separation、authoritative resync 通过。
- [ ] W82-G5 5 个连续 provider resource profiles 均 `<=15%`。
- [ ] W82-G6 JS heap/DOM/listener/resync counters 满足 Week81 上限。
- [ ] W82-G7 evidence 脱敏，所有 process/temp/config delta 为 0。
- [ ] W82-G8 无开放 P0/P1。
- [ ] `git diff --check`、evidence validator 和 review 通过。

## 新对话启动指令

Week81 完成后复制以下内容到新对话：

```text
开始执行 docs_md/weekly/82_week_desktop_preview_requalification.plan.md。
严格读取 Week80/81 reviews 与 week82-handoff.json；
只从 handoff 指定的 exact clean candidate revision 开始。
先完成 credential-free Phase 0–1；不得读取 .env.local。
需要运行 provider read-only/recovery/resource 时停下请求本对话独立授权；
controlled write 必须再次单独授权。
不得修改 15% Gate、workers/retries、warm/post window、workload，
不得 forced GC/reload/延长 idle 或重跑单个失败 profile 到绿。
最终只能写 Preview Ready 或 Blocked，0.6.0 formal release 继续保持 Blocked。
```

Read-only/recovery/resource 建议授权：

```text
授权 Week82 provider read-only、recovery 与 resource phases；
仅使用 .env.local 内 OPENAI_MODEL、OPENAI_BASE_URL、OPENAI_API_KEY，
仅注入本次 packaged Desktop 子进程；
允许 fresh disposable projects；默认只允许 workspace.read_text；
recovery 可终止 recorded owned AppHost 并在审批前取消；
禁止 write、shell、MCP、Git tool 和其他网络行为。
```

Controlled write 建议另发：

```text
授权 Week82 controlled-write phase；
仅限 fresh project B、计划中 2 个 allowlisted files、diff <=160 行、
一个 exact target test command；apply_patch 与 shell 必须分别逐动作 durable approval；
禁止其他工具、文件和网络行为。
```
