# Week 92 执行计划：CLI 与 Desktop Refactor Final Acceptance

状态：`Blocked by Week91 Hardening Gate`

创建日期：2026-07-28

固定产品集成分支：`codex/week84-92-integration`（control spine：`codex/week84-92-refactor`）

执行依据：`84_92_week_goal_execution_contract.md`。中央 Goal、Gate 和 handoff
分别使用 `84_92_week_goal_control.schema.json`、
`84_92_week_gate_result.schema.json`、`84_92_week_handoff.schema.json`
（`schemaVersion: 1.0.0`）；每个 Gate 还必须绑定 tracked
`84_92_week_gate_requirements.json` 的唯一条目，并通过语义验证器和 append-only
integrity helper 对账。

## Goal

从 Week91 exact clean candidate 独立重建和复验，汇总 CLI/Renderer 两条 lane、cross-lane integration 和 hardening evidence，形成可审计的阶段最终决定。

本周原则上不修改产品；若发现新 P0/P1，保留首败并返回对应周修复，Week92 状态为 `Blocked`。

## Entry Gate

- `artifacts/week91-refactor-hardening/week91-hardening-handoff.json` 通过 handoff schema，`decision=ReadyForNextCheckpoint`、`laneOutput.summary=Candidate Ready for Final Acceptance`。
- source revision、dependency lock、package/AppHost/Renderer identity 和 handoff 一致。
- Week84-91 reviews/handoffs/evidence 可追踪，开放 P0/P1 为 0。
- worktree 干净，无未说明 generated/contract drift。
- W86/W89 用户视觉确认与 W91 Narrator/人工 UX Gate 均为 Passed。
- 中央 Goal control 位于 Week92 Entry，Goal 级 provider turn ledger 连续、累计不超过 120，且剩余授权足以完成适用复验。

任一条件不满足时写入 `gates/W92-G0.json`，不得开始最终构建或使用历史
candidate/evidence 补齐。

## Phase 0：Independent Clean Build

- 从 exact candidate 建两个独立 build roots。
- 运行 .NET build/test 与 Desktop verify/build。
- 比较 build outputs、package inventory、AppHost、app.asar、Renderer bundle、size/hash。
- 不使用 `-SkipBuild` 或修改已 smoked payload。

## Phase 1：CLI Acceptance

验证并比较重构前 baseline：

- 26 顶层命令和全部子命令 inventory。
- help、description、arguments/options/defaults/validators。
- public Create overload injection。
- text/JSON/NDJSON/stdout/stderr/output file。
- exit code `0/1/2`、logs/trace。
- agent/session/job/queue/automation/pipeline/report/artifact/project-pack schema 与 correlation。
- default smoke、release build/package 和 cleanup。

## Phase 2：Desktop Acceptance

- 1440x900、1024x768、800x900 Chat-first visual/interaction matrix。
- workspace/thread/conversation/composer/approval/cancel/recovery。
- Changes/Terminal/Reports/Artifacts/Preview context workspace。
- audit traceability、redaction、truncated/corrupt/unknown fallback。
- keyboard、screen reader、high contrast、reduced motion。
- unpacked/packaged E2E 和 protocol inventory。
- 使用 exact Week92 candidate 请求用户完成最终视觉确认；必须记录 Passed/Failed
  与截图 hash，不能由 W86/W89 历史确认、Agent 自评或视觉自动化代替。
- 发出最终请求前先生成不可改写的 `screenshot-manifest.json`，绑定 exact candidate、
  全部截图/DOM/a11y snapshot 的相对路径与 raw SHA-256、viewport/fixture identity 和
  redaction attestation。该 manifest 还必须绑定 W91
  `narrator-manual.json`、`operator-ux-attestation.json` 的相对路径/raw SHA-256，以及
  `docs_md/weekly/84_92_evidence_anchors/w91-hardening.json` 的 raw SHA-256。
- Week92 final manifest 固定 `manifestRole=week92-final-user-visual`、viewport set
  `1440x900/1024x768/800x900` 与五个 fixture：`workspace-thread`、`streaming-tools`、
  `waiting-approval`、`failed-recovery`、`context-review-terminal`。每个 viewport/fixture
  组合必须有唯一 PNG screenshot、DOM snapshot 与 a11y snapshot，共 15 组截图和 30 份
  supporting snapshot；DOM/a11y JSON 必须非空并自绑定 exact candidate、artifact type、
  viewport 与 fixture；不得用重复 basename、空壳 JSON 或同一文件冒充多个组合。
- 必须先提交绑定 exact candidate 与上述 `screenshot-manifest.json` raw SHA-256 的
  不可变 `UA-*` challenge request；用户回复必须回显 request id、challenge code 与
  `Passed`/`Failed`。回复后才生成 `visual-acceptance.json` receipt，绑定 request、
  screenshot manifest、规范回复和 user-decision ledger entry；它是回复后 receipt，
  不是 challenge 的预绑定 manifest。随后只追加 user-decision ledger，不能改写 request
  或 screenshot manifest。
- 同一次 Week92 用户确认必须展示 screenshot manifest 已绑定的 W91 Narrator/operator
  hardening evidence；用户在最终决定中一并确认或拒绝这些观察，不新增 W91 用户暂停点。

## Phase 3：Resource Requalification

- Week77 5-profile Gate。
- Week80 C0-C7 controls。
- Week84 listener ownership/regression。
- long-session/DOM bound/panel lifecycle/terminal cleanup。
- 只有 `W92-G7` 可按用户已确认的第 6 项授权读取 `.env.local` 并调用真实 provider；真实
  secret 只进入 frozen gateway，candidate/scenario child 只接收 loopback/占位 key，而且
  exact packaged executable/AppHost/asar/tree、固定 launch argv、产品 scenario result 与实际
  gateway requests 必须共同绑定。OS-isolation 或用户明确接受的 cooperative-candidate boundary
  未闭合时保持 `NotRun`，不得以 harness 合成 HTTP 冒充产品 E2E；
  W92 其他 Gate 必须 credential-free/mock、无 provider scope 且
  `providerTurnsConsumed=0`。
- `W92-G7` 的冻结最低成功工作量为 `34 turns`（read-only 1 + recovery 2 + 五个
  `1 warm-up + 5 measured` resource profile 共 30 + controlled write 1）。每个真实 provider
  segment 必须在 child 启动前由冻结 executor 追加 `TurnReserved`，返回后追加
  `TurnCompleted`，并以唯一 `AttemptFinished=Passed|Failed` 终结。reservation、失败
  attempt、崩溃恢复与完整 rerun 全部计入 `120 turns` 总账；每个 `Passed` receipt 的
  `attemptId` 必须等于 Gate `authorization.providerSuccessfulAttemptId`，并只绑定该唯一
  attempt 的连续完整 workload，不能拼接失败/部分 attempt。
- controlled write 按用户已确认的第 7 项授权执行，且只在全部 prerequisite non-write
  Gate `W92-G0..G6` 通过并冻结各 Gate result 的 path/raw
  SHA-256/candidate/Passed bindings 后执行；后置 closure `W92-G8/G9` 不伪装成前置
  条件。执行前必须先在 `docs_md/weekly/84_92_controlled_write_authorizations/W92-G7/`
  分别 first-add/commit 两个 distinct durable approval decisions 与汇总 `preauthorization.json`，
  绑定 exact candidate/tree、前置 Gate bindings、harness workspace/transition 和冻结 SDK/命令；
  三项 immutable authorization 全部通过后，才可 exclusive-create 并提交 canonical tracked
  consumed tombstone `docs_md/weekly/84_92_controlled_write_tombstones/W92-G7.json`，再由冻结
  executor 签发唯一一次性 lease。controlled-write `TurnReserved` 即消费 lease，
  失败/取消/超时/崩溃也不得补发、重放或再次执行。
- 所有真实 provider 调用逐 turn 写入 Goal 级 ledger，Week84–92 累计硬上限为
  120；达到上限立即停止并提出 boundary request，不得拆请求、少记或复用旧证据。

若授权被用户撤回或范围不再满足，必须记录 `NotRun` 并保持 Goal active；不得超范围读取 `.env.local` 或用历史 evidence 冒充当前 candidate 通过。

## Phase 4：Docs 与 Evidence Closure

创建：

- `92_week_review.md`。
- `artifacts/week92-refactor-acceptance/gate-evidence/W92-G8/final-summary.json`（自动化/证据
  reconciliation）与独立的
  `artifacts/week92-refactor-acceptance/gate-evidence/W92-G9/final-summary.json`（人工最终决定）；
  两者不得共享路径或 bytes binding。
- command inventory before/after comparison。
- protocol/package/resource/security/a11y summaries。
- source/package/AppHost/Renderer identity manifest。

固定机器证据还必须包含：

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week92-refactor-acceptance/gate-evidence/<gate-id>/<basename>`，不得在组根目录共享或
覆盖。provider ledger binding 使用专用 `provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`；
组根仅保留 Gate results、snapshot、最终 handoff/alias 与 Gate-external pre-seal receipt。

```text
entry-gate.json
independent-build-a.json
independent-build-b.json
cli-compatibility.json
desktop-acceptance.json
screenshot-manifest.json
visual-acceptance.json
security-protocol.json
resource-requalification.json
provider-ledger-bindings/W92-G0/provider-ledger-binding.json
provider-ledger-bindings/W92-G7/provider-ledger-binding.json
provider-ledger-bindings/W92-G9/provider-ledger-binding.json
provider-readonly.json
provider-recovery.json
provider-resource-profile-1.json
provider-resource-profile-2.json
provider-resource-profile-3.json
provider-resource-profile-4.json
provider-resource-profile-5.json
controlled-write.json
package-cleanup.json
commands.json
first-failures/
gates/W92-G0.json ... W92-G9.json
final-summary.json
goal-control-snapshot.json
evidence-reconciliation.json
handoff-readiness.json
week92-acceptance-handoff.json
```

每个 Gate 文件必须通过 `84_92_week_gate_result.schema.json`；最终中央状态必须通过
`84_92_week_goal_control.schema.json`；`week92-acceptance-handoff.json` 必须通过
`84_92_week_handoff.schema.json`。W92-G8 的 `final-summary.json` 必须引用全部 Week84–92
handoff/Gate/evidence hash；W92-G9 的同名 Gate-local 文件只汇总人工最终决定与其前置绑定，
两者都不允许复制历史 Passed 替代当前 candidate 复验。最终还必须运行
`python -B -X utf8 -m unittest tools/test_validate_week84_92_goal_evidence.py tools/test_week84_92_goal_integrity.py tools/test_week84_92_gate_requirements.py tools/test_week84_92_evidence_anchor.py tools/test_week84_92_trusted_executor.py -v`，使 104-Gate requirements、append-only 首败/用户决定账本、write-ahead provider ledger、一次性 write tombstone、immutable evidence-anchor DAG、handoff parent DAG 与 exact checkout fail-closed。W92 closure 使用固定单向顺序：

1. 中央 Goal 保持 `Active`；W92-G8 只绑定 `evidence-reconciliation.json`，它不得引用
   W92-G8/G9、最终 handoff、pre-seal 或 anchor；W92-G9 只绑定
   `handoff-readiness.json`，不得引用尚未生成的最终 handoff。
2. W92-G8/G9 都为 Passed 后才生成 `GoalComplete` handoff；随后在两个 Gate 外运行
   `python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root . --pre-seal-group w92-acceptance`
   并生成与当前全部 bytes 绑定的 `preseal-receipt.json`，不得回写 Gate 或 handoff。
3. anchor builder 重新验证 receipt 后，生成、review、stage 并 commit 第 14 个
   `docs_md/weekly/84_92_evidence_anchors/w92-acceptance.json`；不得改写前 13 个 anchor。
4. 第 14 个 anchor 校验通过后，才把中央状态从 `Active` 切换为 `Complete`。
5. 最后在已封存 Gate 外部运行
   `python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root . --require-complete`
   作为可重现的 Goal 终验记录。

W92-G8/G9 不得把 pre-seal、尚未存在的第 14 个 anchor 或尚未切换的 `Complete` 状态的
`--require-complete` 结果写成 Gate 内证据；终验失败时 Goal 不能标记完成，且不得回写
已提交 anchor。

更新：

- 阶段 07 plan 状态。
- Week84-92 schedule 实际状态与偏差。
- Desktop architecture/development guide。
- release capability/known limitations/changelog（只记录事实，不擅自发布）。

## Final Gates

- [ ] W92-G0 Week84-91 lineage、reviews、handoffs、P0/P1 完整。
- [ ] W92-G1 independent clean dual build 和 identity comparison 通过。
- [ ] W92-G2 CLI full compatibility 为零未评审差异。
- [ ] W92-G3 Desktop Chat-first 全功能/三视口/a11y 通过。
- [ ] W92-G4 security/protocol/redaction/authority boundaries 通过。
- [ ] W92-G5 listener/memory/DOM/resync/long-session resources 通过。
- [ ] W92-G6 unpacked/packaged/CLI smoke/package/cleanup 通过。
- [ ] W92-G7 provider 与 controlled-write Gate 均按已确认授权执行并为 `Passed`；若为 `NotRun`，只能输出 `CandidateReady` 且 Goal 保持 active。
- [ ] W92-G8 docs/evidence/identity/final decision 完整。
- [ ] W92-G9 W86/W89/W92 用户视觉确认、W91 Narrator、120-turn ledger 与 Goal completion 条件闭合。

## 最终决定

只允许：

- `Refactor Accepted`：G0-G9 全部 schema-valid 且为 `Passed`，无开放 P0/P1。
- `Candidate Ready`：产品、自动化、安全和资源 Gate 通过，但独立人工/provider/write Gate 未获授权或未执行；必须明确列出。
- `Blocked`：存在任何关键失败、未解释差异、资源/安全回归、cleanup 残留或开放 P0/P1。

## Goal 完成判定

只有 W92-G0 至 W92-G9 全部 schema-valid 且为 `Passed`、所有适用 Gate 无
`NotRun`、W86/W89/W92 用户视觉确认与 W91 Narrator 均 Passed、provider turns
累计不超过 120、开放 P0/P1 和 cleanup 均为 0、最终决定为
`Refactor Accepted`，并且最终 handoff 为 `decision=GoalComplete`、`finalDecision=RefactorAccepted` 时，才允许把总 Goal 更新为 `complete`。

`Candidate Ready` 只是中间状态：总 Goal 必须保持 active，列出缺失授权/人工 Gate
并继续请求完成；不得用它结束 Goal。`Blocked` 也不是完成，按统一执行契约保留首败、
回到责任周修复；只有同一不可推进阻塞连续三个 Goal turn 后才可将 Goal 标为
`blocked`。

本决定不自动创建 tag、push、上传、部署或将 0.6.0 标记为 Released；这些动作和正式 release acceptance 需要用户另行授权。
