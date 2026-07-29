# Week 88 CLI Lane 执行计划：Project Packs 与 Artifacts Modules

状态：`Blocked by Week87 CLI Gate`

创建日期：2026-07-28

固定 lane 分支：`codex/week84-92-cli`

并行 Renderer 计划：`88_week_composer_inline_approval_task_controls.plan.md`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week87-cli-exec-skills-queue/week87-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Agent/Queue Modules Ready`。
- Week87 `compatibility-diff.json` 的 command/help/output/exit mismatch 均为 `0`，required Gate 全为 `Passed`。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema，绑定 Week87 exact clean CLI revision、baseline inventory hashes 和 worktree dirty state；本 lane Entry 结果写入 `gates/W88-C0.json`。
- Application、AppHost、protocol/generated 和 release scripts 冻结区无未授权差异。

任一条件不满足，Week88 CLI lane 保持 `Blocked`。

## Goal

拆分最大的 Project Packs 命令域及 Artifacts 命令，保持 Gerber/TIFF 计划、执行、验证、人工决策、路径/identity 安全和稳定 JSON/markdown schema。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week88-cli-packs-artifacts/gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。
组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week88-cli-packs-artifacts/
  gates/W88-C0.json ... W88-C5.json
  baseline-identity.json
  command-inventory.before.json
  command-inventory.after.json
  help-inventory.before.json
  help-inventory.after.json
  output-exit-inventory.before.json
  output-exit-inventory.after.json
  packs-artifacts-matrix.before.json
  packs-artifacts-matrix.after.json
  compatibility-diff.json
  optional-real-tool.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week88-cli-handoff.json
  first-failures/
```

before/after 使用同一 fake tool、tool identity/hash、checkpoint、managed artifact 和 path fixtures。`compatibility-diff.json` 的 command/help/text/JSON/markdown/stdout/stderr/error/exit mismatch count 必须全部为 `0`；`optional-real-tool.json` 只允许 `Passed`、`Failed` 或带原因的 `NotRun`，`NotRun` 不计作 correctness 通过。schema 与 first-failure 规则按统一执行契约。

## Phase 0：Packs/Artifacts Baseline

冻结：

- packs list/doctor/plan/run/restart/accept/reject 等 inventory。
- tool path、probe、input/output、dry-run、approval 和 external dependency validators。
- artifacts list/show/verify/export/prune 等 inventory。
- JSON/markdown report、exit code、error code、managed ownership 和 no-overwrite behavior。

## Phase 1：Project Packs Module

- 将约 1,399 行 packs 命令与领域 helper 按 builder/validator/renderer 拆分。
- 只移动 CLI wiring；ProjectPacks/Core/Application 逻辑不复制。
- 保留 fixed tool identity、hash、staging/checkpoint、manual reapproval 和 correctness boundary。

## Phase 2：Artifacts Module

- 迁移 list/show/verify/export/prune 等 reviewed 命令。
- 保留 ownership、retention、relative path、hash/tamper、missing/corrupt 和 no-overwrite。
- 删除/覆盖类行为继续 fail closed；不扩大 UI 或 CLI 权限。

## Phase 3：Compatibility Matrix

- fake tool/fixture default tests。
- dry-run、probe、approval deny、stale checkpoint、tamper、missing artifact。
- JSON/markdown/text output 和 stable error codes。
- optional real Gerber/TIFF tool smoke 只有在环境明确具备并授权时运行；Skipped 不包装为通过。

## 非目标

- 不重构 Project Pack runtime/business model。
- 不新增 external tools、下载或 correctness 声明。
- 不修改 Renderer/shared frozen files。

## 机器验收命令与判定

从仓库根目录按顺序运行，首次结果写入 `test-results.json`：

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
git diff --check
```

inventory harness 对 baseline/after 执行 packs/artifacts 的同一 fake/default compatibility manifest。机器判定同时满足：

- 全树 top-level count/顺序仍为 exact before（顶层 `26`）；本周域 command/help/option/default/validator canonical diff 为 `0`。
- packs plan/run/restart/accept/reject 与 artifacts list/show/verify/export/prune 的 text/JSON/markdown/stdout/stderr/stable error/exit mismatch count 均为 `0`。
- fake tool identity/hash、dry-run、probe、approval deny、stale checkpoint、tamper、missing/corrupt、managed ownership、relative path、retention 与 no-overwrite observed outcome diff 为 `0`。
- 删除/覆盖 fixture 均 fail closed；outside-root write count、unreviewed executable count 和 ownership bypass count 均为 `0`。
- optional real-tool 未获环境与授权时必须为 `NotRun` 且不计入 correctness；若实际运行则原样记录 `Passed` 或 `Failed`，不得用 default fake matrix 覆盖。
- full .NET、Release build、default credential-free smoke exit code 全为 `0`，owned process/temp/config cleanup delta 为 `0`。

## Critical Gates

- [ ] W88-C0 packs/artifacts required inventory/snapshot missing count 为 `0`。
- [ ] W88-C1 Project Packs schema/output/exit mismatch count 为 `0`。
- [ ] W88-C2 Artifacts ownership/path/hash/retention outcome mismatch count 为 `0`。
- [ ] W88-C3 fake/default matrix failed/missing case count 为 `0`。
- [ ] W88-C4 `optional-real-tool.json` 状态为 `Passed`、`Failed` 或带原因的 `NotRun`，并记录授权、环境、命令和结果。
- [ ] W88-C5 .NET full、smoke、release/package applicable tests 全绿。

## Exit Gate / Handoff

`W88-C5` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未生成
的 `week88-cli-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成 Gate 外
`preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

- `gates/W88-C0.json` 至 `gates/W88-C5.json` 逐个通过 Gate result schema且全为 `Passed`；W88-C4 判定的是 optional real-tool 状态是否被准确记录，当 `optional-real-tool.json.status=NotRun` 时周结论不得声称 real-tool correctness。
- `compatibility-diff.json` 全部 mismatch count 为 `0`，`final-summary.json` 的 open P0/P1 为 `0/0`。
- `week88-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Packs/Artifacts Modules Ready`，并绑定 exact clean HEAD、shared frozen diff、fake/default matrix、optional-real-tool 状态和 Week89 entry prerequisites。
- exact clean HEAD 不包含 Renderer lane 或共享冻结区的未授权差异。

周 Review 结论只能为 `CLI Packs/Artifacts Modules Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值。
