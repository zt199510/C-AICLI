# Week 85 执行计划：Renderer Feature Boundaries 与 State Split

状态：`Blocked by Week84`

创建日期：2026-07-28

所属阶段：Phase 32 / Renderer architecture seam

固定 lane 分支：`codex/week84-92-renderer`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`

## Entry Gate

- `artifacts/week84-renderer-listener-retention/week84-baseline-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Baseline Ready for Refactor`，并与兼容文件 `week85-refactor-handoff.json` canonical 等价。
- Week84 `gates/W84-G0.json` 至 `gates/W84-G9.json` 均为 `Passed`，开放 P0/P1 为 `0/0`。
- 中央 `artifacts/week84-92-goal-control/goal-state.json` 通过 Goal control schema，记录当前 HEAD、Week84 handoff SHA-256、Renderer worktree dirty state 和冻结目录 diff；本 lane 的 Entry 结果写入 `gates/W85-R0.json`。
- 当前 worktree 干净；`desktop-v1` inventory 为 exact `39 invoke + 2 event`，其 canonical hash 等于 Week84 handoff。

任一条件不满足，Week85 保持 `Blocked`。

## Goal

在不改变现有 UI 信息架构和用户可观察行为的前提下，拆分 Renderer 的状态、请求和 subscription ownership，为 Week86-89 的 Chat-first UI 重构建立稳定 feature seam。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week85-renderer-feature-boundaries/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

固定目录：

```text
artifacts/week85-renderer-feature-boundaries/
  gates/W85-R0.json ... W85-R7.json
  baseline-identity.json
  desktop-protocol-inventory.before.json
  desktop-protocol-inventory.after.json
  subscription-ownership.before.json
  subscription-ownership.after.json
  subscription-ownership.diff.json
  characterization-results.json
  viewport-matrix.json
  accessibility.json
  resource-controls.json
  test-results.json
  final-summary.json
  handoff-readiness.json
  week85-renderer-handoff.json
  first-failures/
```

中央 Goal state、每个 `gates/W85-R*.json` 和 `week85-renderer-handoff.json` 分别通过 Goal control、Gate result、handoff schema。before/after 必须来自同一冻结 fixture 与命令；`subscription-ownership.diff.json` 必须满足 external subscription owner count 均为 `1`、duplicate owner 为 `0`、unmatched add/remove 为 `0`。首个失败写入 `first-failures/`，重跑不得覆盖。

## 范围

创建或迁移：

- `shared/desktop-gateway`：typed bridge adapter、error normalization、test fake。
- `features/runtime`：runtime status/restart/subscription。
- `features/workspace`：open/adopt/context epoch。
- `features/threads`：list/select/create/rename/archive、notification/resync。
- `features/conversation`：detail fetch、pagination、projection merge。
- `features/composer`：draft、mention、catalog、pending intent。
- `features/approval`：resolve/cancel/resume/restart commands。
- `features/review` 与 `features/terminal`：query/mutation state owner。

现有组件可暂时通过 compatibility facade 消费新 feature APIs。

## 非目标

- 不改变 AppShell、颜色、布局、timeline visual 或 composer visual。
- 不新增 protocol method/event。
- 不引入 provider、Application 或 AppHost 改动。
- 不强制引入 Zustand、Redux、React Query 或路由库。
- 不删除 Week81/83/84 diagnostics 和 controls。

## 设计规则

1. 每种 external subscription 只有一个 owner。
2. feature state 不直接读 `window.caicli`；只能通过 `DesktopGateway`。
3. request 必须携带 workspace/thread/selection epoch 或等价 stale guard。
4. cleanup 必须可确定性测试；effect unmount 后不得更新 state。
5. AppHost result/error 保持结构化；UI-safe message normalization 集中处理。
6. feature 之间通过明确 command/query contract 协作，不互相 import 内部 reducer。

## Phase 0：Characterization Tests

- 冻结现有 App/controller 行为：workspace open、thread select、pagination、review tab、composer、approval、cancel/recovery。
- 冻结 notification ordering、stale response、bounded resync 和 listener cleanup。
- 保存 current public facade shape，作为分片期间兼容层。

Phase Gate：现有行为有测试覆盖，未覆盖的竞态先补测试再迁移。

## Phase 1：DesktopGateway

- 定义 Renderer 使用的最小 typed gateway interface。
- 实现 production adapter 包装 preload bridge。
- 实现 deterministic fake，不复制业务 policy。
- 统一 transport exception、safe failure 和 cancellation/disposal。
- architecture test 禁止 feature 直接访问 global bridge。

## Phase 2：Read-side State Split

- 先迁移 runtime、workspace、threads、conversation 和 review。
- 保持 reducer 纯函数、请求 owner 可释放、状态不重复存储。
- thread notification 只进入一个 coordinator；selected detail refresh 仍 bounded。
- 迁移后逐域运行 characterization tests。

## Phase 3：Write-side State Split

- 迁移 composer、approval、turn controls 和 terminal command state。
- command identity、expected revision、clientMutationId 和 pending intent 不变。
- UI compatibility facade 只组合 feature hooks，不再承载业务分支。

## Phase 4：删除重复 ownership

- 移除旧 controller 中已迁移 refs/effects/callbacks。
- 不保留“双订阅 + 双 reducer”过渡终态。
- 输出 feature ownership matrix 和 dependency graph。

## 测试与验证

- feature reducer/unit tests。
- DesktopGateway production/fake validation tests。
- React StrictMode mount/unmount/remount cleanup。
- workspace/thread rapid switch、stale request、duplicate/regressive notification。
- Week81/83 projection regressions、Week84 listener regression。
- Desktop `npm run verify`、unpacked E2E、.NET full regression。

## 机器验收命令与判定

从仓库根目录按顺序运行，首次结果写入 `test-results.json`：

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
Push-Location apps\desktop
npm run verify
npm run test:e2e:unpacked
npm run test:e2e:performance
Pop-Location
git diff --check
```

定向 feature、DesktopGateway、StrictMode cleanup 与 rapid-switch tests 作为 `npm run verify` 的必跑测试集；测试名与数量写入 `characterization-results.json`。机器判定同时满足：

- protocol after 为 exact `39 invoke + 2 event`，canonical hash 与 before 相同。
- subscription diff 的 duplicate/unmatched 均为 `0`；mount/unmount/remount 后 owner count 恢复到 before。
- `viewport-matrix.json` 覆盖 `1440x900`、`1024x768`、`800x900`，每个视口 `horizontalOverflowPixels=0`、`criticalOverlapCount=0`、`unreachableControlCount=0`。
- `accessibility.json` 中 `npm run check:accessibility` exit code 为 `0`，自动化 violation count 为 `0`。
- `resource-controls.json` 引用 Week84 handoff 中冻结阈值与 workload：listener delta `<=40`、Renderer private-bytes delta `<=15%`，DOM/resync observed 均不超过对应 frozen limit；不得 forced GC、reload、延长 idle 或减少 workload。
- 上述命令 exit code 全为 `0`，owned process/temp/config cleanup delta 全为 `0`。

## Critical Gates

- [ ] W85-R0 Week84 clean handoff 有效。
- [ ] W85-R1 characterization tests 覆盖迁移路径。
- [ ] W85-R2 global bridge 只由 DesktopGateway adapter 使用。
- [ ] W85-R3 read-side feature split 完成且无双 subscription。
- [ ] W85-R4 write-side feature split 完成且 identity/revision 不变。
- [ ] W85-R5 listener/DOM/resync/private-bytes controls 不回归。
- [ ] W85-R6 `desktop-v1` exact inventory 不变。
- [ ] W85-R7 full verify/E2E/.NET 与 cleanup 通过。

## Exit Gate / Handoff

`W85-R7` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence；该文件证明本
lane 的 Gate、summary、candidate、父 handoff、cleanup 与开放问题已具备生成交接的条件，
不得引用尚未生成的 `week85-renderer-handoff.json`。`W85-R7` Passed 后才单向生成最终
handoff，再生成 Gate 外 `preseal-receipt.json` 并封存 anchor；后两者不得回写 Gate。

- feature 目录、DesktopGateway 和 compatibility facade。
- ownership/dependency 文档。
- `85_week_review.md`。
- `gates/W85-R0.json` 至 `gates/W85-R7.json` 逐个通过 Gate result schema且全为 `Passed`，`final-summary.json` 的 open P0/P1 为 `0/0`。
- `week85-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=State Boundary Ready`，并绑定 exact clean HEAD、evidence hashes、protocol inventory、subscription/resource bounds 与 Week86 entry prerequisites。
- exact clean HEAD 不包含 CLI lane 或共享冻结区的未授权差异。

周 Review 结论只能写 `State Boundary Ready` 或 `Blocked`；机器 handoff 仍使用 schema 的 `decision` 四值，不得提前宣称新 UI 完成。
