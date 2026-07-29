# Week 90 执行计划：CLI 与 Desktop Cross-lane Integration

状态：`Blocked by Week89 Renderer and CLI Gates`

创建日期：2026-07-28

固定产品集成分支：`codex/week84-92-integration`（control spine：`codex/week84-92-refactor`）

执行依据：`84_92_week_goal_execution_contract.md`。中央 Goal、Gate 和 handoff
分别使用 `84_92_week_goal_control.schema.json`、
`84_92_week_gate_result.schema.json`、`84_92_week_handoff.schema.json`
（`schemaVersion: 1.0.0`）。

## Entry Gate

必须同时存在：

- `artifacts/week89-context-review-workspace/week89-renderer-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Desktop Experience Lane Ready for Integration`。
- `artifacts/week89-cli-automation-pipeline/week89-cli-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=CLI Lane Ready for Integration`。
- 两条 lane 各自 exact clean revision、full test 结果、P0/P1 和 dirty state。
- Week84 resource baseline 与 `39 invoke + 2 event` inventory。
- 两个 handoff 指向同一 Week84 ancestry、依赖锁和 shared-freeze baseline，W89 用户视觉确认为 Passed。
- 中央 Goal control 位于 Week90 Entry，真实 provider 累计 turn 与授权 ledger 可追踪且未超过 Goal 总上限 120；本周 integration 默认不新增 provider turn。

任一 lane 为 Blocked、handoff schema 无效或 lineage 不一致时，将
`gates/W90-G0.json` 写为 Failed 并停止；不得通过只合并另一条 lane 继续。

## Goal

把两个隔离 worktree 的成果合并到一个可追踪 integration candidate，解决 architecture/test/docs/build 冲突，证明 Renderer UI 重构与 CLI composition 重构没有通过共享 Application/Core/协议产生交叉回归。

## 合并顺序

1. 从包含 W89 两个 immutable registry 的 exact control-spine tip `Ct` 开始，不从旧 Week84 tree 另建分支。
2. 以 exact parents `[Ct, refs/codex/week84-92/sealed/w89-renderer]` 真 merge Renderer lane；merge commit 本身必须携带全部 Renderer 单边 blob/mode contribution，随后运行 Desktop verify 与定向 E2E。
3. 以 exact parents `[renderer-merge, refs/codex/week84-92/sealed/w89-cli]` 真 merge CLI lane；merge commit 本身必须携带全部 CLI 单边 contribution，随后运行 .NET/CLI 定向与 full tests。
4. 对每个 merge base 重算 contribution/tree；拒绝 `-s ours`、ours 后补拷、squash、cherry-pick、替代 tip 或 lane 注册后追加的新 tip。双方同改路径必须先生成 tracked conflict-resolution manifest。
5. 运行完整 solution + Desktop cross-surface matrix。
6. 只修复真实 integration conflict；不新增 feature。

每步记录 pre/post revision 和首次失败。不得在两条 lane 都合并后才开始定位全部问题。

## Shared Freeze 复核

比较 Week84 baseline 与 integration candidate：

- `CSharpAiCli.Application`、Core、AppHost、ProjectPacks。
- `protocol/desktop-v1` 与 generated C#/TS contracts。
- release/package/smoke scripts。
- security and architecture tests。

任何共享冻结区差异必须有对应 lane 的明确评审、测试和 reason；无来源差异直接 Blocked。

## Architecture 收口

- CLI command modules 只依赖允许的 Application/Core/ProjectPacks surface。
- AppHost/Desktop 不依赖 CLI module/factory。
- Renderer leaf feature 不直接访问 global preload bridge。
- single subscription owner 和 DesktopGateway boundary 保持。
- `CliCommandFactory` compatibility façade 与 `CliRootComposer` 没有第二套 dependency truth。
- docs 中的目录、命令 inventory、protocol count 与实际一致。

## Integration Matrix

### .NET / CLI

- solution build/full tests。
- CLI command/help/output/exit-code inventory comparison。
- default smoke、release build/package applicable tests。
- queue/automation/pipeline recursive delegation。
- agent/approval/session/job/report/artifact/pack flows。

### Desktop

- `npm run verify`。
- workspace/thread/conversation/composer/approval/context panel unit tests。
- unpacked read-only/write/recovery/terminal/artifact E2E。
- responsive/visual/a11y fixtures。
- listener/DOM/resync/private-bytes controls。

### Cross-surface

- 同一 Application use case 经 CLI 与 AppHost 得到等价安全结论。
- CLI refactor 未改变 Desktop package/AppHost resolution。
- Renderer refactor 未改变 CLI stores、config 或 release artifact。
- process/temp/config cleanup 为 0。

## 非目标

- 不增加新协议、新 CLI 命令或新 UI feature。
- 不趁 integration 重写共享 Application service。
- 不接受“各 lane 单独通过”替代合并后的完整 rerun。

## 机器证据

固定目录：`artifacts/week90-cli-desktop-integration/`。

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或覆盖。provider ledger binding 使用
专用 `provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`；组根只保留组级 canonical 文件。

```text
entry-gate.json
merge-ledger.json
shared-freeze-diff.json
architecture-boundaries.json
cli-compatibility.json
desktop-integration.json
screenshot-manifest.json
cross-surface.json
candidate-identity.json
commands.json
provider-ledger-bindings/W90-G0/provider-ledger-binding.json
first-failures/
gates/W90-G0.json ... W90-G7.json
final-summary.json
handoff-readiness.json
week90-integration-handoff.json
```

`merge-ledger.json` 必须记录 exact control tip、两个 registry/sealed refs、Renderer pre/post merge、CLI pre/post merge、正确 merge base 与逐 path contribution
和每步首次失败；`shared-freeze-diff.json` 必须为每个差异记录来源 lane、reason、
review 和验证证据。每个 Gate 结果与 Week91 handoff 必须通过对应统一 schema。

## Critical Gates

- [ ] W90-G0 两条 lane handoff、identity、P0/P1 完整。
- [ ] W90-G1 分步合并与首次失败证据完整。
- [ ] W90-G2 shared freeze 差异全部 reviewed。
- [ ] W90-G3 architecture/dependency boundaries 通过。
- [ ] W90-G4 CLI full compatibility matrix 通过。
- [ ] W90-G5 Desktop verify/E2E/visual/resources 通过。
- [ ] W90-G6 cross-surface behavior 和 cleanup 通过。
- [ ] W90-G7 exact clean integration candidate/handoff 完成。

## Exit 与 Handoff

`W90-G7` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence，不能引用尚未
生成的 `week90-integration-handoff.json`。Gate Passed 后才单向生成最终 handoff，再生成
Gate 外 `preseal-receipt.json` 并封存 anchor；任何后置结果不得回写 Gate。

只有 W90-G0 至 W90-G7 全部 schema-valid 且为 `Passed`、两条 lane 都已纳入、
未评审 shared-freeze 差异为 0、cleanup/P0/P1 为 0，才能生成 schema-valid
`week90-integration-handoff.json`，其中 `decision=ReadyForNextCheckpoint`、
`laneOutput.summary=Integration Candidate Ready for Hardening`。handoff 必须绑定 exact integration revision、两条父
handoff、dependency/protocol/command/package identities、Gate evidence hash 和
中央 Goal checkpoint。

任何单 lane 合并、适用 Gate `NotRun`、identity 不闭合或合并后未完整 rerun 都只能
令 handoff `decision=Blocked`。
