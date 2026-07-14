# 第 60 周 Isolated Run Staging、Checkpoint 与 Safe Resume Foundation Implementation Plan

状态：已完成

**Goal:** 建立每次 Project Pack 运行的隔离目录、不可变输入 manifest、状态机和 checkpoint，使失败、取消或进程崩溃后仍能判断哪些阶段可以安全恢复，并且不改变源输入。

## 来源

- `docs_md/weekly/58_week_cli_0_5_vertical_workflow_schedule.md`
- `docs_md/weekly/59_week_gerber_tiff_discovery_preflight.plan.md`
- `src/CSharpAiCli.Core/Jobs/JobRecords.cs`
- `src/CSharpAiCli.Core/Queue/TaskQueueStore.cs`
- `src/CSharpAiCli.Core/Exec/ReportPathResolver.cs`

## 本周范围

- 定义 `ProjectPackRunId`、run record、stage status、checkpoint、correlation 和 schema version。
- 建立 user-managed run root，例如 `%USERPROFILE%\.caicli\runs\<run-id>`，并执行 canonical containment/reparse protection。
- 创建 staging input、working、logs、artifacts、reports 子目录；路径布局稳定且不依赖当前 cwd。
- 在执行前重验 plan fingerprint、input hashes、tool identity 和 output conflict。
- 将 bounded source inputs 复制到 staging；源目录保持只读，不跟随 links，不复制 unknown files。
- 建立 stage state machine：`created/discovered/staged/ready/running/verifying/awaiting-acceptance/accepted/rejected/failed/canceled/interrupted`。
- 用 fake driver 打通 create -> stage -> execute -> verify checkpoint，但不接真实工具。
- 接入 queue/job correlation；pack run record 是 job artifact/operational checkpoint，不复制 `taskReport` 真相。
- 增加 cancel 和 resume eligibility 判断；不持久化 approval grant/override。

本周明确不做：

- 不调用真实 Gerber/TIFF 工具。
- 不实现 TIFF metadata verification 或 human accept/reject command。
- 不实现 artifact prune。
- 不支持 concurrent writer、cross-process lease/heartbeat 或 remote worker。
- 不自动恢复 execute 中断；该状态必须人工确认并在 Week 63 定义 restart 语义。

## Managed Run Layout

```text
%USERPROFILE%\.caicli\runs\<run-id>\
|-- run.json
|-- input-manifest.json
|-- plan.json
|-- staging\
|-- working\
|-- logs\
|-- artifacts\
`-- reports\
```

规则：

- `run.json` 和 checkpoint 原子写入，corrupt/unsupported schema 返回 diagnostic，不猜测状态。
- staging 文件名来自 inventory mapping，不直接复用不可信相对路径。
- staging copy 后重新计算 hash；不一致时终止。
- managed root 之外的输出只保存 pointer，不由后续 prune 删除。
- run record 不保存 API key、raw tool arguments、raw input content 或 approval token。

## Safe Resume 规则

- `staged/ready`：可重验后继续，执行时重新申请 approval。
- `verifying`：若 artifact hashes 完整，可重跑只读 verifier。
- `awaiting-acceptance`：可继续人工 accept/reject。
- `running/interrupted`：不得自动再次执行外部工具；先检查 partial outputs，并要求显式 restart decision。
- `accepted/rejected/canceled`：terminal，不恢复执行。
- 任何 tool hash、input hash、plan fingerprint 或 policy 变化都使旧 checkpoint invalid。

## 用户入口草案

```powershell
caicli packs run gerber-tiff --plan <plan.json> --dry-run
caicli packs runs show <run-id>
caicli packs runs show <run-id> --output json
caicli packs cancel <run-id>
caicli packs resume <run-id> --dry-run
```

本周 `run --dry-run` 可创建 planned/staged evidence，但不得调用真实工具。是否默认持久化 dry-run 必须在 CLI help 中明确。

## 测试计划

- run id/path containment/atomic writes/corrupt state。
- staging preserves source、rejects links、detects hash change、handles disk full/permission failure。
- fake driver success/failure/timeout/cancel/partial output。
- state transition table covers every allowed/denied edge。
- restart process reads valid checkpoint；unsupported schema fails closed。
- resume invalidated by input/tool/plan/policy change。
- queue/job/run/artifact correlation stable and redacted。
- concurrent process attempting same run gets stable conflict，不产生双执行。

## 任务清单

- [x] Step 1: 定义 run id、run record、stage/checkpoint DTO 和 transition table。
- [x] Step 2: 实现 managed run root/layout 和 path/reparse protection。
- [x] Step 3: 实现 atomic run/checkpoint store 与 corrupt diagnostics。
- [x] Step 4: 实现 bounded staging copy、rename mapping 和 post-copy hash verification。
- [x] Step 5: 实现 pre-run revalidation：plan/input/tool/output/policy。
- [x] Step 6: 实现 fake driver execution contract 和 stage events。
- [x] Step 7: 实现 cancel/resume eligibility，不自动重跑 interrupted execute。
- [x] Step 8: 接入 queue/job correlation 和 artifact pointer。
- [x] Step 9: 增加 run store/staging/state/resume/security tests。
- [x] Step 10: 增加 dry-run packaged smoke 和 cleanup checks。
- [x] Step 11: 更新 runtime diagnostics/security/known limitations 草稿。
- [x] Step 12: 运行 build/test/default smoke。
- [x] Step 13: 创建 `60_week_review.md`，冻结 Week 61 外部执行前置条件。

## 验收标准

- fake driver 可通过同一 state machine 形成完整、可重启读取的 run evidence。
- 源输入在 staging/execute simulation 前后 hash 不变。
- canceled/interrupted/corrupt run 不会被标记 succeeded。
- resume 不复用旧 approval，不绕过 tool/input/output revalidation。
- Week 61 只需提供真实 structured process adapter，不重写 run/store/job 事实源。
