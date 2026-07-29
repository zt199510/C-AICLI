# Week 84 执行计划：Renderer Listener Retention 修复与 Refactor Baseline

状态：`Waiting for deterministic reproduction`

创建日期：2026-07-28

所属阶段：Phase 31 / Week83 requalification blocker closure

执行分支：`codex/week84-92-refactor`

统一执行契约：`84_92_week_goal_execution_contract.md`

统一 schema：

- `84_92_week_goal_control.schema.json`
- `84_92_week_gate_result.schema.json`
- `84_92_week_handoff.schema.json`
- `84_92_week_gate_requirements.json`

## Entry Gate

启动前必须读取并核对：

- `docs_md/weekly/83_week_review.md`
- `artifacts/week83-approval-projection-remediation/week84-handoff.json`
- Week83 product candidate `ccf9d82c9fa76c201876ee01d3849902989091e9`
- Week83 documentation closure `7cd1eac2b3ba5f8b9aa9dd6263dcb83d9dd66cd3`
- Week83 package、app.asar、AppHost 和 Renderer bundle identity

Goal bootstrap 可以在不改变产品源码、既有测试 harness、dependency lock 或 package 输入的前提下增加并提交固定 44 个控制面文件：Week84-92 计划/执行契约、三个 schema、104-Gate requirements manifest、三个 scoped `.gitattributes`、跨文件语义验证器、append-only integrity helper、immutable evidence-anchor helper、trusted executor、冻结 provider turn harness、五个 tracked test、W84-G0 command-control bundle、五个 W84-G0 command adapters，以及 `tools/week84_92_provider_scenarios/` 下四个冻结 scenario。不得借 bootstrap 修改产品或既有测试。bootstrap revision `B` 必须直接以 `e6b5c7f71d06a22c70bf534e6764bcb96ef06337` 为唯一 parent，diff 恰好等于 44-path allowlist；`entry.json` 分别记录 Week83 product candidate `P`、Week83 documentation closure、bootstrap parent、`B` 与 changed paths，并证明 `P..B` 在 `src`、Desktop source/HTML/package/lock/config、`Directory.Build.props` 和 `global.json` 上差异为 0。W84-G0 execution candidate 固定为 `B`，不得仍以缺少 adapters 的 `P` 运行。除该已审计控制面差异外，若 lineage、identity、evidence hash 或 dirty state 不一致，Week84 立即 `Blocked`。

五个 W84-G0 adapter 只是 observation source：不得输出受信结果 marker，也不得自行贡献测试
counts。每个 adapter report 必须由 bootstrap-frozen test/oracle 中的 exact unittest 独立验证，
verifier descriptor 绑定 adapter report raw SHA-256、command/Gate/candidate/control revision 与 checkout
identity；Gate command evidence 同时引用 adapter 与 verifier report，否则保持 `NotRun`/`Failed`。

## Goal

关闭 Week83 packaged recovery 中 `JS event listeners delta +193 > +40` 的单一开放 P1，建立可重复的 ownership regression，只做证据支持的最小 lifecycle 修复，并形成 Week85 可以依赖的 exact clean refactor baseline。

Week84 不做 UI redesign，也不开始 CLI 模块化。

## 授权边界

Phase 0-3 全部 credential-free；在它们全绿且 exact candidate identity 冻结前：

- 不读取 `.env.local`。
- 不访问网络或真实 provider。

本 Goal 已获得用户对 Week84 与 Week92 的以下有界授权，授权回执必须写入 ignored Goal control，且平台 sandbox/网络弹窗仍按实际命令批准：

- 只有 `W84-G6`、`W84-G7` 与 `W84-G8` 可按 A6 读取 `.env.local` 的 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY` 并调用真实 provider；真实值只进入冻结 gateway，candidate/scenario child 只拿 loopback URL、model 与占位 key，值不得输出、写入 evidence 或进入日志。W84 其他 Gate 必须 credential-free/mock，provider scope 为空且 `providerTurnsConsumed=0`。G6 前还必须完成 execution contract 所述 OS-isolation 或 cooperative-candidate boundary decision；未决定时 G6 保持 `NotRun`。
- Week84-92 总 provider 上限为 `120 turns`；每次执行前后记录累计值，到达上限立即停止并请求新授权。
- 冻结 workload 的 Week84 最低消耗为 `G6=3`、`G7=30`、`G8=1`，合计 `34 turns`；G7 的每个 profile 都包含 `1 warm-up + 5 measured`。每个真实 provider segment 必须在 child 启动前由冻结 executor 逐笔追加 `TurnReserved`，child 返回后追加 `TurnCompleted`，attempt 最后追加唯一 `AttemptFinished=Passed|Failed`。reservation 一经写入即计入预算；失败、崩溃、取消、超时与完整 rerun 全部计入总账。每个 Passed receipt 的 `attemptId` 必须等于 Gate `authorization.providerSuccessfulAttemptId`，并只绑定唯一满足完整冻结 workload 的成功 attempt，不得拼接失败 attempt。
- controlled write 仅在全部 prerequisite non-write Gate `W84-G0..G7` 通过并冻结其 Gate path/raw SHA-256/candidate/Passed bindings 后生效；后置 closure `W84-G9` 不伪装成前置条件。Week84 与 Week92 各最多一次，只允许 harness-owned `caicli-week*-write-*` 临时 workspace、唯一 `result.txt` 的 `fail -> pass` patch、唯一冻结 `dotnet msbuild` 验证命令及两次 durable approval。两个 immutable approval decisions 与汇总 preauthorization 必须先提交并绑定 candidate/tree、全部 prerequisite、workspace/transition/command；验证后才可 exclusive-create/commit 唯一 consumed tombstone、预留 turn 并启动 child。首次 write 尝试即消费，失败/取消/超时也不得补发或重放，最后经 ownership/path 校验后清理。
- 授权不包含用户项目、任意 workspace 外写入、额外 shell/MCP/network 工具、push、tag、发布或部署。

## 明确禁止

- 不提高 `+40` listener 上限。
- 不使用 forced GC、Renderer reload、extended idle 或减少 workload 让结果变绿。
- 不删除/缩短 durable history、timeline、approval 或 recovery 场景。
- 不修改 provider、approval contract、`desktop-v1`、ThreadStore 或 UI 设计。
- 不把 observer/harness bug 直接包装成产品修复；必须分别证明 ownership。
- 不只重跑失败 profile；五个 provider resource profiles 必须连续、不可拆分。

## Evidence

下列普通 evidence 文件名是 basename 清单；每项必须按所属 Gate 写入
`artifacts/week84-renderer-listener-retention/gate-evidence/<gate-id>/<basename>`，不得在组根目录
共享或覆盖。provider ledger binding 保持下列专用目录；组根仅保留 Gate results、snapshot、
最终 handoff/alias 与 Gate-external pre-seal receipt。

目录：

```text
artifacts/week84-renderer-listener-retention/
  gates/W84-G0.json ... W84-G9.json
  gate-evidence/W84-G0/entry.json
  gate-evidence/W84-G0/baseline-identity.json
  gate-evidence/W84-G0/week83-handoff-verification.json
  gate-evidence/W84-G0/goal-contract-unittest.json
  gate-evidence/W84-G0/trusted-executor-unittest.json
  gate-evidence/W84-G1/regression-before.json
  gate-evidence/W84-G1/listener-ownership.json
  gate-evidence/W84-G2/listener-ownership.json
  gate-evidence/W84-G2/regression-before.json
  gate-evidence/W84-G2/regression-after.json
  gate-evidence/W84-G3/regression-after.json
  gate-evidence/W84-G3/listener-ownership.json
  gate-evidence/W84-G4/credential-free.json
  gate-evidence/W84-G4/regression-after.json
  gate-evidence/W84-G4/final-summary.json
  gate-evidence/W84-G5/baseline-identity.json
  gate-evidence/W84-G5/credential-free.json
  gate-evidence/W84-G6/provider-readonly.json
  gate-evidence/W84-G6/provider-recovery.json
  gate-evidence/W84-G7/provider-resource-profile-1.json
  gate-evidence/W84-G7/provider-resource-profile-2.json
  gate-evidence/W84-G7/provider-resource-profile-3.json
  gate-evidence/W84-G7/provider-resource-profile-4.json
  gate-evidence/W84-G7/provider-resource-profile-5.json
  gate-evidence/W84-G8/controlled-write.json
  gate-evidence/W84-G9/final-summary.json
  gate-evidence/W84-G9/handoff-readiness.json
  provider-ledger-bindings/W84-G6/provider-ledger-binding.json
  provider-ledger-bindings/W84-G7/provider-ledger-binding.json
  provider-ledger-bindings/W84-G8/provider-ledger-binding.json
  final-summary.json
  goal-control-snapshot.json
  handoff-readiness.json
  week84-baseline-handoff.json
  week85-refactor-handoff.json
```

`week83-handoff-verification.json` 是 W84-G0 自身的 `Passed` 验证回执：它必须绑定原始
`artifacts/week83-approval-projection-remediation/week84-handoff.json` 的路径、raw SHA-256、
原始 `Blocked` 状态、Week83 candidate 和唯一开放 P1；不得复制或改写原始 handoff，
也不得把原始 `Blocked` 伪装成 `Passed`。冻结 executor 必须生成唯一、attempt-bound 的
`goal-evidence-semantic-validate.<attempt>.trusted-test-report.json`：先运行当前 partial-state 语义
验证器，再运行五个控制面 unittest 模块，并绑定原始 stdout/stderr hash、实际 discovered/passed 数和
bootstrap candidate。required `goal-contract-unittest.json` 是引用该唯一 trusted test-report
path/raw SHA-256、attempt、counts 与 candidate 的 canonical `json` receipt，不得冒充第二个
`test-report`。`trusted-executor-unittest.json` 是继续绑定同一 report、executor/control/policy raw
identity 与五组 adapter/verifier report 的 exact `json` 摘要；两个摘要都不得成为 child 自报 test
envelope，也不得被 reports 反向引用。

`baseline-identity.json` 必须是 canonical exact receipt：raw-hash 绑定同 Gate 的 `entry.json` 和冻结
Week83 `package-identity.json`，逐字段记录 `B/P`、documentation closure、bootstrap parent、冻结
product-input zero-diff proof，并精确投影 Desktop package、package tree、`app.asar`、AppHost 与
Renderer bundle identity；additional keys、generic `Passed` 或单文件 identity 均 fail closed。

中央 `artifacts/week84-92-goal-control/goal-state.json` 必须通过 Goal control schema；每个 Gate 必须按原始字节绑定 tracked `84_92_week_gate_requirements.json` 中自己的唯一条目；`gates/W84-G0.json` 至 `gates/W84-G9.json` 必须逐个通过 Gate result schema；`week84-baseline-handoff.json` 必须通过 handoff schema，`decision=ReadyForNextCheckpoint` 且 `laneOutput.summary=Baseline Ready for Refactor`。兼容文件 `week85-refactor-handoff.json` 与 canonical handoff 内容等价。W84-G0 必须执行 `python -B -X utf8 -m unittest tools/test_validate_week84_92_goal_evidence.py tools/test_week84_92_goal_integrity.py tools/test_week84_92_gate_requirements.py tools/test_week84_92_evidence_anchor.py tools/test_week84_92_trusted_executor.py -v`；每次推进前还必须运行 `python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root .` 完成 schema、requirements、append-only ledger、parent DAG、immutable evidence anchor 与跨文件对账。所有 first failure 单独保留，不得由 rerun 覆盖。W84 handoff/snapshot/alias 完成后先运行 `--pre-seal-group w84-baseline`；通过后才能生成并提交 `docs_md/weekly/84_92_evidence_anchors/w84-baseline.json`，提交后普通验证器必须通过，之后才可开放 Week85 Entry Gate。

## Phase 0：Lineage 与 Baseline 冻结

1. 记录 source/head/branch/dirty state。
2. 校验 Week83 handoff、product candidate、package tree、app.asar、AppHost、Renderer bundle。
3. 验证 Week83 credential-free matrix 和正式 recovery failure evidence 未被修改。
4. 记录 Node/npm/Electron/.NET SDK 和测试入口。

Phase Gate：exact baseline 可追踪，evidence 完整；否则停止。

## Phase 1：Deterministic Ownership Regression

构造与 Week83 正式场景同态的 credential-free 流程：

```text
turn A waiting-for-approval
-> owned AppHost interruption/recovery projection
-> turn B waiting-for-approval
-> cancel before approval
-> symmetric two-frame observation
```

同时采集：

- product-owned event listener add/remove identity 与 owner。
- React/Electron baseline listeners。
- harness observer、frame barrier 和 diagnostics listener。
- thread notification subscription、runtime subscription、terminal/resize/document handlers。
- DOM nodes、documents、resync queue/runners、pending requests。

要求 regression 在 Week83 baseline 稳定红灯，并证明 `+193` 中哪些属于 product、observer 或 measurement boundary。

Phase Gate：无法确定性复现或 ownership 仍混合时，状态保持 `Blocked`，不得进入产品修改。

## Phase 2：最小修复

只有 ownership 指向产品 lifecycle 时才修改产品：

- 每个 subscription 有唯一 owner 和对称 cleanup。
- effect dependency 不因 projection identity 变化重复注册全局 listener。
- stale request/runner completion 不保留 closure、DOM 或 listener。
- terminal/review/timeline materialization 不在 panel/turn 切换后残留。

测试必须先在 baseline 红，再在修复后绿；产品差异限制在证据指向的最小 Renderer 文件和定向 tests。

若根因完全属于 observer/harness，只修正 harness，并在相同 Week83 product candidate 上重新证明 Gate；不得虚构产品 revision。

Phase Gate：定向 regression 满足 frozen listener/DOM/resync bounds，且语义检查不回归。

## Phase 3：Credential-free Full Matrix

必须串行运行并记录首次结果：

- .NET full suite。
- Desktop `npm run verify`。
- dependency/package/security audits。
- unpacked 与 packaged E2E/smoke。
- accessibility 与 protocol 三轮。
- Week77 5-profile Gate。
- Week80 C0-C7 controls。
- Week81/83 projection regressions。
- Week84 listener ownership regression。
- process/temp/config cleanup。

Phase Gate：任一 P0/P1 失败则停止；不得进入 provider phase。

## Phase 4：Exact Candidate 与 Provider Non-write Gates

进入 `W84-G5` 时，四个 `tools/week84_92_provider_drivers/*.mjs` 必须在该 Gate 的 command-control
commit 中作为 `prepared` 的 `fixture`/`parser` source 一次性 first-add/seal，不能充当 oracle/test、
不能贡献 Gate counts。`W84-G6/G7/G8` 只能通过 `runtimeDrivers` 引用这一 strict-prior G5 origin；
任一 driver 在当前 provider control 才首次加入、origin 不等于 first-add、或 origin 后发生改写均阻断 provider child。

1. 从 exact clean revision 重建 package，由 prior-sealed package command 生成完整 regular/no-reparse
   inventory、entrypoint/argv、entry count/bytes 与 tree-root SHA-256；单文件 identity 不可接受。
2. 记录 package/tree/app.asar/AppHost/Renderer identity，并把 build receipt 与 exact candidate 绑定。
3. 在读取 `.env.local` 前 first-add/commit
   `docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json`，绑定用户 challenge response、
   exact candidate/package tree、mode、允许的 G6/G7/G8 scopes/turns 与适用 canary；未闭合保持 NotRun。
4. harness 按原目录 staged 完整 package、重算 tree，实际启动固定 entrypoint；frozen scenario 只驱动/
   观察产品，保存 package-launch receipt，不得自行合成 HTTP 冒充产品请求。
5. 按边界决定和 A6 有界授权运行 provider read-only。
6. 运行完整 recovery；保留 Week83 同一语义与 listener workload。
7. recovery 全绿后连续运行不可拆分五轮 resource profiles。
8. 每个 provider attempt 都逐 turn 记录 `attemptId/attemptOutcome`；任一 workload 失败时以 `Failed` 终结并保留全部已运行 evidence，停止写入阶段。
9. `Passed` receipt 的 `attemptId` 必须等于 Gate `authorization.providerSuccessfulAttemptId`，
   且只绑定最后一个完整成功 attempt 的连续 ledger range；不能把多个失败/部分 attempt
   拼成一个成功序列。

Phase Gate：read-only、recovery、五轮 resource 和 cleanup 全部 Passed。

## Phase 5：Controlled Write（有界条件授权）

仅在 Phase 0-4 全绿后：

- 绑定 exact candidate、全部 prerequisite non-write Gate `W84-G0..G7` result 的 path/raw SHA-256/candidate/Passed 状态、harness-owned 临时 workspace、唯一 `result.txt` 和冻结测试命令。
- 核对 Goal authorization receipt、provider turn 预算与平台命令批准。
- 先在 `docs_md/weekly/84_92_controlled_write_authorizations/W84-G8/` 分别 first-add/commit `approval-apply-patch.json`、`approval-shell.json` 和汇总 `preauthorization.json`；冻结 executor 必须验证两项不同 approval、全部前置 Gate、candidate tree、workspace、transition、SDK/command 的 raw hash、时间与不可改写 Git identity，任何失败必须保持 0 reservation/0 child。
- preauthorization 通过后，以 exclusive-create 生成只属于 `W84-G8` 的 canonical tracked consumed tombstone `docs_md/weekly/84_92_controlled_write_tombstones/W84-G8.json`，绑定 preauthorization、上述前置 Gate、candidate、lease/nonce，并在 write child 启动前完成 single first-add commit；冻结 executor 必须验证其不可改写 Git identity。
- 由冻结 executor 先追加唯一 controlled-write `TurnReserved`，再运行一次 write/approval/verification/review 闭环；reservation 即消费 lease，即使失败、取消、超时或崩溃也不得补发。
- 验证没有第二张 lease、第二次 write、自动重放、旧 approval 复用或 target 扩大。
- 恢复/清理测试拥有的 workspace state。

授权条件、candidate identity 或平台命令批准任一不满足时记录 `NotRun`，最终最多为 `Candidate Ready`，不得伪造通过。

## Phase 6：Handoff

`W84-G9` 只把 Gate-local `handoff-readiness.json` 作为 closure evidence；它证明
`W84-G0..G8`、summary、candidate、provider prefix、cleanup、开放问题以及 canonical/alias
生成计划已闭合，不得引用尚未生成的两个 handoff。`W84-G9` Passed 后才单向生成
`week84-baseline-handoff.json` 和 canonical 等价 alias；随后在 Gate 外生成
`preseal-receipt.json` 并封存 anchor，任何后置结果不得回写 Gate 或 handoff。

创建 schema-valid `week84-baseline-handoff.json`，并生成 canonical 等价的兼容文件 `week85-refactor-handoff.json`，至少包含：

- schemaVersion、decision、finalDecision、createdAt。
- Week83 baseline、Week84 fix 和 documentation revision。
- package/tree/app.asar/AppHost/Renderer identity。
- listener ownership root cause 与 regression id。
- credential-free/provider/resource/write Gate 状态。
- P0/P1、cleanup delta、protocol inventory。
- Week85 可依赖的 exact clean revision。

## Critical Gates

- [ ] W84-G0 Week83 lineage、identity 和 evidence 完整。
- [ ] W84-G1 deterministic baseline regression 稳定红灯。
- [ ] W84-G2 ownership 被明确归类，最小修复/observer correction 有证据。
- [ ] W84-G3 定向 regression 与语义检查绿灯。
- [ ] W84-G4 credential-free full matrix 全绿。
- [ ] W84-G5 exact clean package 与 identity 冻结。
- [ ] W84-G6 provider read-only 与 recovery 全绿。
- [ ] W84-G7 连续五轮 provider resource Gate 全绿。
- [ ] W84-G8 按用户已确认的 A7 有界条件授权执行一次 controlled write，并为 `Passed`。
- [ ] W84-G9 P0/P1、cleanup、docs 和 handoff 收口。

## 最终结论

Week84 只允许：

- `Baseline Ready for Refactor`：G0-G9 全部 `Passed`，开放 P0/P1 为 0；用户已给出 A7 有界条件授权，因此 G8 不得以 `NotRun` 进入 Week85。
- `Candidate Ready`：自动化和 non-write Gates 全绿，但仍有独立人工/写入 Gate 未授权。
- `Blocked`：listener、资源、语义、identity、cleanup 或其他关键 Gate 失败。

Week84 不得写 `UI Refactor Complete`、`0.6.0 Accepted` 或 `Released`。
