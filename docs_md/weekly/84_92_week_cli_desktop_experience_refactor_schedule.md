# C-AICLI CLI 与 Desktop 体验重构 Week 84-92 开发排期

更新时间：2026-07-28

状态：`Active；采用精简验收`

> **2026-07-30 变更：** 周排期保留，原逐 Gate 证据工作流不再作为周任务前置条件。
> 验收以 `docs_md/plans/07_cli_desktop_experience_refactor_lean_acceptance.md`
> 为准，优先推进产品代码。

范围依据：

- `docs_md/plans/07_cli_desktop_experience_refactor.plan.md`
- `docs_md/weekly/84_92_week_goal_execution_contract.md`
- `docs_md/weekly/84_92_week_goal_control.schema.json`
- `docs_md/weekly/84_92_week_gate_result.schema.json`
- `docs_md/weekly/84_92_week_handoff.schema.json`
- `docs_md/weekly/84_92_week_gate_requirements.json`
- `tools/validate-week84-92-goal-evidence.py`
- `tools/week84_92_goal_integrity.py`
- `tools/week84_92_evidence_anchor.py`
- `tools/test_validate_week84_92_goal_evidence.py`
- `tools/test_week84_92_goal_integrity.py`
- `tools/test_week84_92_gate_requirements.py`
- `tools/test_week84_92_evidence_anchor.py`
- `docs_md/spec/desktop_app_development_framework_0_6_0.md`
- `docs_md/weekly/83_week_review.md`
- `artifacts/week83-approval-projection-remediation/week84-handoff.json`

## Goal 模式与已确认授权

Week84–92 使用一个持续到最终验收的总 Goal，逐周 checkpoint，不把每周拆成可独立
宣称完成的 Goal。权威执行、恢复、失败和边界处理规则位于
`84_92_week_goal_execution_contract.md`。

用户已为本 Goal 一次性确认契约中的 1–8 项，摘要如下：

- 允许在仓库内编辑、构建、测试、打包和生成 ignored evidence。
- 允许本地 `codex/week84-92-refactor`、隔离 worktree、checkpoint commit 和 local merge；禁止 push、tag、upload、deploy、release。
- 允许按锁文件恢复既有依赖，并只清理本 Goal 记录的 owned process 与精确测试临时目录；新增依赖、lockfile 变化和范围外破坏性动作仍需 boundary request。
- 真实 provider 使用 Goal 全周期累计上限 **120 turns**；`.env.local` 与真实调用只授权给 `W84-G6/G7/G8`、`W92-G7`，所有 harness-mediated turn 以 attempt/outcome 逐笔记账，真实 secret 只进入 frozen gateway。candidate child 只拿 loopback/占位 key；首次真实 Gate 前还必须闭合 OS-isolation 或用户明确接受 cooperative-candidate trust boundary。其余 Gate（尤其 W91 smoke）必须 credential-free/mock、零 provider scope、零新增 turn。
- OpenCowork 只作为 Chat-first 信息架构与交互层级参考，保留 C-AICLI 品牌和既有 authority。
- W86、W89、W92 是仅有的强制用户确认点；W91 Windows Narrator/人工 UX Gate 由 Goal-owned test operator 保存 exact candidate evidence，并在 W92 交给用户一并复核，不新增 W91 用户暂停。
- shared frozen contract、阈值、安全权限扩大和 contract 未覆盖的 controlled write 继续逐项请求，不因总授权自动放开。

用户确认减少可预见的中途停顿，但不覆盖 sandbox 的逐次审批，也不允许把
`NotRun`、拒绝或边界 NO 改写为 Passed。

三个统一 schema 使用 JSON Schema Draft 2020-12，`schemaVersion` 固定为 `1.0.0`：

- `84_92_week_goal_control.schema.json`：中央 Goal checkpoint、授权、provider turn、当前 week/lane/gate 和恢复动作。
- `84_92_week_gate_result.schema.json`：单个 Gate 的 `Passed / Failed / NotRun / NotApplicable`、命令、首败和 evidence hash。
- `84_92_week_handoff.schema.json`：跨周 exact revision、identity、Gate、P0/P1 与 next Entry Gate。
- `84_92_week_gate_requirements.json`：逐 Gate 冻结必须执行的命令、断言、证据类型、最低用例数和 provider/controlled-write/manual policy。
- `tools/validate-week84-92-goal-evidence.py`：计算 104-Gate registry、跨文件 rollup、provider ledger、controlled-write 和 Goal completion，弥补 JSON Schema 无法做数组求和与原始文件 hash 对账的边界。
- `tools/week84_92_goal_integrity.py`：校验 append-only 首败/用户决定 hash-chain、pre-handoff snapshot、候选绑定的用户 challenge request 与 handoff parent DAG。
- `tools/week84_92_evidence_anchor.py`：把每个完成的 Week/lane 的 ignored Gate、snapshot、handoff 和 ledger prefix 原始字节封入固定 tracked anchor；anchor 的 first-add Git blob 不可改写。`tools/week84_92_trusted_executor.py` 在 child 启动前预留 provider budget、验证 tracked controlled-write approvals/preauthorization/lease，并生成 Gate/candidate-bound provenance；产品命令固定先生成 adapter observation，再由 strict-prior exact unittest test/oracle 独立验真，adapter 自报 `Passed` 或贡献 counts 均 fail closed。`tools/week84_92_provider_turn_harness.py` 只接受 executor 独占创建的 descriptor，通过 loopback counting gateway 逐 request 生成 observation，不接受 caller provider argv。W84-G0 command-control bundle、五个 command adapters 与四个 provider scenarios 同样属于 44-file bootstrap control plane；provider scenario 必须启动并观察 exact packaged candidate，不得用合成 HTTP 代替产品 E2E。五个 tracked test 文件是 bootstrap 和每次契约变更的强制回归入口。

## 排期结论

采用 **9 个日历周的双 Lane 排期（Week84-92）**。

Week84 是不可跳过的稳定化前置周，不属于 UI redesign：它必须关闭 Week83 移交的 listener-retention P1，形成 exact clean refactor baseline。Week85-89 由 Renderer 和 CLI 两条隔离 lane 并行；Week90-92 统一集成、hardening 和验收。

```text
Week84      baseline remediation
Week85-89   Renderer lane || CLI lane
Week90      cross-lane integration
Week91      full hardening
Week92      final acceptance
```

本地 refs 固定为：evidence/control `codex/week84-92-refactor`、Renderer
`codex/week84-92-renderer`、CLI `codex/week84-92-cli`、W90–92 产品集成
`codex/week84-92-integration`。Week85–89 不按周另建分支；每周双 lane registry barrier 以
evidence-only merge 进入两个 persistent lane。W90 从 exact control tip 创建 integration ref 并首次
合并 sibling 产品 history；R90、R91 evidence barrier 再依次 merge 回 integration，而产品 commit
不进入永久 control spine。

9 周承诺依赖两个独立 worktree/负责人和一个集成 owner。共享冻结区包括 Application、AppHost、`desktop-v1`、generated contracts、release scripts 和 Week84 resource controls。若只有一个实现者完全串行执行两个 lane，按约 14 周估算；不得把 CLI 五周拆分压成两周大搬迁。

## 起点评估

| 范围 | 当前状态 | 本排期差距 |
|---|---|---|
| Application/Core | CLI 与 AppHost 已共享结构化 service 和安全策略 | 不重写，只保护依赖方向 |
| Desktop protocol | exact `39 invoke + 2 event`，生成 C#/TS contract | 增加 Renderer adapter，不默认改协议 |
| Renderer controller | 单文件 568 行，拥有多域状态和副作用 | 拆分 feature ownership 与 subscription lifecycle |
| App shell | 固定三栏，中央继续堆 Terminal/Controls/Composer | 改为 Chat-first 与可选 context panel |
| Timeline | 原始 timeline item 直接成为主要卡片 | 增加不丢审计信息的 presentation projection |
| UI system | 176 行全局 CSS 承担全部层级 | 建 tokens、primitives 和 feature styles |
| CLI | `CliCommandFactory.cs` 7,673 行、26 个顶层命令 | composition root + feature command modules |
| Resource baseline | Week77/80 大部分 Gate 通过 | Week83 recovery listener `+193 > +40`，仍有 P1 |
| Release status | 0.6.0 formal release `Blocked` | 本排期不擅自重写正式 release 决定 |

## 固定依赖顺序

```text
W84 clean resource baseline
  -> W85 state/subscription seams
  -> W86 app shell/design system
  -> W87 conversation projection
  -> W88 composer/approval
  -> W89 context/review workspace
  -> W90 cross-lane integration
  -> W91 full hardening
  -> W92 acceptance

W84 clean source baseline
  -> W85 CLI composition/diagnostics modules
  -> W86 CLI jobs/review/session modules
  -> W87 CLI exec/skills/queue modules
  -> W88 CLI packs/artifacts modules
  -> W89 CLI automation/pipeline compatibility
  -> W90 cross-lane integration
  -> W91 full hardening
  -> W92 acceptance
```

W85-89 的 Renderer lane 不修改 `src/CSharpAiCli.Cli`；CLI lane 不修改 `apps/desktop`。两条线默认不改共享冻结区，并在 Week90 第一次正式合流。

## 逐周计划

### Week 84：Renderer Listener Retention 修复与 Refactor Baseline

详细计划：`84_week_renderer_listener_retention_refactor_baseline.plan.md`

状态：`Waiting for deterministic reproduction`

主要交付：

- 绑定 Week83 product candidate `P`、documentation closure、package/AppHost/Renderer identity，并以 44-file bootstrap revision `B` 作为 W84-G0 execution candidate，证明 `P..B` 的冻结 product/package inputs 零差异。
- 用 credential-free deterministic regression 复现 two-turn approval-to-canceled 的 listener retention。
- 区分 product-owned listener、test observer 和 Electron/React baseline。
- 如确认产品问题，只做最小 Renderer lifecycle cleanup；禁止 UI redesign 和协议/provider 变更。
- 重建 exact clean package，重跑 credential-free、provider read-only、recovery、连续五轮 resource Gate。
- DAG 中位于 write 前的 prerequisite non-write Gates（Week84 `G0..G7`）全绿并冻结其 path/raw SHA-256/candidate/Passed bindings 后，先提交两个 distinct approval decisions 与 canonical preauthorization，再按用户已确认的 A7 有界条件签发一次性 lease 并执行一次 controlled-write；后置 closure Gate 不伪装成前置条件，首次 write attempt 即消费 lease，不得补发、重放或扩大形状。
- 生成 canonical `week84-baseline-handoff.json`，并保留 canonical 等价兼容文件 `week85-refactor-handoff.json`。

周末 Gate：listener delta 回到冻结上限，完整矩阵通过，P0/P1 为 0，且形成可追踪 clean baseline；否则 Week85 `Blocked`。

### Week 85：双 Lane 架构起步

Renderer：`85_week_renderer_feature_boundaries_state_split.plan.md`

CLI：`85_week_cli_composition_diagnostics_modules.plan.md`

状态：`Blocked by Week84`

Renderer 交付 DesktopGateway、feature reducers/hooks、single subscription owner；CLI 交付 command inventory、composition context、public façade 以及低风险诊断/配置模块。

周末 Gate：两个 lane 各自 full 定向测试通过，互不修改对方目录，共享冻结区无漂移，Week84 controls 不回归。

### Week 86：Shell 与 CLI Session/Query Modules

Renderer：`86_week_chat_first_shell_design_system.plan.md`

CLI：`86_week_cli_jobs_review_session_modules.plan.md`

Renderer 交付 tokens、primitives、Chat-first shell 和三视口 drawer；CLI 迁移 jobs/ci/review/tools/run/session/chat 并拆分对应 tests。

周末 Gate：新 Shell feature parity、focus/a11y/resources 通过；CLI text/JSON/store/injection snapshots 等价。

### Week 87：Conversation 与 CLI Agent/Queue Modules

Renderer：`87_week_conversation_projection_timeline.plan.md`

CLI：`87_week_cli_exec_skills_queue_modules.plan.md`

Renderer 交付 ConversationBlock projector、message/tool/approval/result 展示和有界审计；CLI 迁移 exec/skills/queue 并冻结 current-root delegation、NDJSON 和 job correlation。

周末 Gate：conversation golden/long-session/resources 通过；queue -> exec/skills 全兼容且无第二套 root state。

### Week 88：Core Chat Interaction 与 CLI Packs/Artifacts

Renderer：`88_week_composer_inline_approval_task_controls.plan.md`

CLI：`88_week_cli_packs_artifacts_modules.plan.md`

Renderer 交付底部 Composer、inline approval/cancel/recovery；CLI 拆分 Project Packs、Artifacts 与领域 helpers，保持路径、identity、report 和 correctness 边界。

周末 Gate：approval/recovery/IME/controlled-context 全绿；packs/artifacts fake/default matrix 与 schema/exit compatibility 全绿。

### Week 89：Context Workspace 与 CLI Full Compatibility

Renderer：`89_week_context_panel_terminal_review_workspace.plan.md`

CLI：`89_week_cli_automation_pipeline_compatibility.plan.md`

Renderer 交付 Changes/Terminal/Reports/Artifacts/Preview 统一 panel；CLI 完成 automation/pipeline recursive composition、factory cleanup 和 26 顶层命令全兼容冻结。

周末 Gate：W89 用户视觉确认 Passed；两个 lane 分别输出 schema-valid
`week89-renderer-handoff.json` 与 `week89-cli-handoff.json`，exact clean、开放
P0/P1 为 0。

### Week 90：Cross-lane Integration

详细计划：`90_week_cli_desktop_cross_lane_integration.plan.md`

状态：`Blocked by both Week89 lanes`

分步合并 CLI 与 Renderer lane，审计共享冻结区，运行跨 surface architecture、full build/test/E2E/resources，形成 exact clean integration candidate。

周末 Gate：两条 lane 单独和合并后均通过；无未解释共享差异或 cleanup 残留；
输出 schema-valid `week90-integration-handoff.json`。

### Week 91：Security、Accessibility、Resource 与 Package Hardening

详细计划：`91_week_refactor_security_resource_hardening.plan.md`

状态：`Blocked by Week90`

对 integration candidate 运行 Desktop/CLI security review、a11y/Narrator、Week77/80/84 resource controls、unpacked/packaged E2E、CLI smoke/release/package 和 cleanup；只允许修复 Gate 暴露的 P0/P1。

周末 Gate：所有 hardening Gate 全绿，W91 Narrator/人工 UX Passed，并形成
schema-valid `week91-hardening-handoff.json` 与 exact Week92 candidate；否则
Blocked。

### Week 92：Final Acceptance

详细计划：`92_week_refactor_final_acceptance.plan.md`

状态：`Blocked by Week91`

独立 clean dual build，复验 CLI full compatibility、Desktop Chat-first 全体验、security/protocol/resources/package/a11y，并收口 evidence、docs 和最终决定。

周末 Gate：W92 用户视觉确认和全部必要 Gate Passed 后，输出 schema-valid `week92-acceptance-handoff.json`，其 `decision=GoalComplete`、`finalDecision=RefactorAccepted`。
`Candidate Ready` 只能保持总 Goal active；`Blocked` 不算完成。

## 跨周 Critical Gates

| Gate | 最晚 Week | 必须满足 |
|---|---:|---|
| R0 Week83 Closure | 84 | listener root cause、red/green、完整 requalification、clean identity |
| R1 Renderer Ownership | 85 | feature state/request/subscription ownership 单一且可测试 |
| R2 Shell Parity | 86 | 新 Shell 三视口、键盘、focus、现有功能 parity |
| R3 Conversation Truth | 87 | UI 聚合不丢 timeline 审计、revision 或 redaction |
| R4 Write Interaction | 88 | approval/cancel/resume/restart 无 bypass、stale 或 replay |
| R5 Review Workspace | 89 | terminal/artifact/preview 不扩大 authority/correctness |
| C1 CLI Composition | 85 | public Create façade、global options、diagnostic modules 等价 |
| C2 CLI Delegation | 87 | exec/skills/queue current-root、NDJSON、correlation 等价 |
| C3 CLI Full Compatibility | 89 | 26 root command inventory 与执行安全语义等价 |
| R6 Cross-lane Integration | 90 | shared freeze、architecture 和合并后 full matrix 通过 |
| R7 Hardening | 91 | security、resources、package、a11y、smoke 和 cleanup 全绿 |
| R8 Refactor Acceptance | 92 | clean dual build、evidence、docs 与最终决定收口 |

## 每周 Definition of Done

每个 Week review 必须记录：

1. source baseline、branch、最终 product revision 和 dirty state。
2. 实际修改范围与相对计划偏差。
3. build/test/lint/typecheck/E2E/smoke 的真实命令、数量、失败和耗时。
4. 首败、根因、修复和完整 rerun；不得只保留最终绿灯。
5. protocol/method count、package/AppHost/Renderer identity（适用时）。
6. process/temp/config cleanup、secret redaction 和 evidence 目录。
7. Gate table：`Passed / Failed / NotRun / NotApplicable` 与依据。
8. P0/P1、剩余风险、下一周 Entry Gate 和 handoff。
9. Gate 结果只使用统一四值且必要 Gate 全绿时才能写 `Passed`；`Refactor Accepted` 只用于 Week92 最终决定，未运行关键 Gate 必须保持 Goal active 或 handoff `Blocked`。
10. 每个 Gate、handoff 和中央 checkpoint 通过统一 schema 与 tracked 语义验证器校验，且 evidence hash 与当前 exact candidate 一致；完成的 Week/lane 必须有 committed、first-add immutable evidence anchor。
11. Goal 级 `provider-turn-ledger` 与中央 control 对账，累计不得超过 120。
12. W86/W89/W92 用户确认必须先提交 exact-candidate/manifest-bound challenge request，再记录用户回显 request id/challenge 的真实结论；decision 必须绑定 request 的唯一 first-add `requestCommit`，且该 commit time 早于 `decidedAt`。W91 Narrator/operator UX 必须记录真实执行观察并在 W92 manifest 中供用户复核，不能用 ARIA 自动化自评代替。

## 公共验证命令

```powershell
python -B -X utf8 -m unittest tools/test_validate_week84_92_goal_evidence.py tools/test_week84_92_goal_integrity.py tools/test_week84_92_gate_requirements.py tools/test_week84_92_evidence_anchor.py tools/test_week84_92_trusted_executor.py -v
python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root .

dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build

Set-Location apps\desktop
npm run verify
npm run test:e2e:unpacked
```

按周增加：

- Week84：Week80/81/83/84 resource/projection controls，以及按用户已确认 A6 执行的 provider phases。
- Week86：responsive/visual/accessibility fixtures。
- Week87：long-session 与 conversation projection fixtures。
- Week89：terminal/artifact/Gerber unpacked/packaged E2E。
- Week85-89 CLI lane：每周 command/help/output/exit-code snapshots、full .NET 和 smoke。
- Week90：合并后 cross-surface full matrix。
- Week91：CLI smoke/release/package、Desktop security/resource/a11y/package hardening；所有 provider-facing smoke 使用 credential-free deterministic fake/mock，不读取 `.env.local`。
- Week92：全部 packaged/security/performance/protocol/requalification commands。

## 证据目录约定

每周使用独立 ignored 目录：

```text
artifacts/week84-renderer-listener-retention/
artifacts/week85-renderer-feature-boundaries/
artifacts/week85-cli-composition/
artifacts/week86-renderer-chat-first-shell/
artifacts/week86-cli-jobs-review-session/
artifacts/week87-renderer-conversation-projection/
artifacts/week87-cli-exec-skills-queue/
artifacts/week88-renderer-composer-approval/
artifacts/week88-cli-packs-artifacts/
artifacts/week89-context-review-workspace/
artifacts/week89-cli-automation-pipeline/
artifacts/week90-cli-desktop-integration/
artifacts/week91-refactor-hardening/
artifacts/week92-refactor-acceptance/
```

每个 Gate 的 manifest-required evidence 固定写入
`<artifact-root>/gate-evidence/<gate-id>/<basename>`。同名 evidence 不得跨 Gate 共用，
每个 required basename 在所属 Gate 中只能出现一次；provider ledger binding 是唯一例外，
固定写入 `<artifact-root>/provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`。
artifact root 直属路径只保留 canonical Gate result、goal-control snapshot、最终 handoff/兼容 alias
与 Gate-external pre-seal receipt 等组级文件；计划中的 evidence 文件名清单均按此规则解释为
basename 清单，不表示可写入共享的 root-level 文件。

证据不得包含 API key、base URL 凭据、完整绝对用户路径、raw secret、approval grant 或可重放 write arguments。

中央控制 evidence 固定为：

```text
artifacts/week84-92-goal-control/
  goal-state.json
  authorization.json
  provider-turn-ledger.json
  provider-runtime-journal.json
  first-failure-ledger.json
  user-decision-ledger.json
  decision-log.ndjson
  evidence-index.json
  resume.md
```

完成的 14 个 Week/lane 组还必须依固定 DAG 生成 lane-local
`docs_md/weekly/84_92_evidence_anchors/<group-id>.json`，并由 evidence-only control spine first-add
`docs_md/weekly/84_92_anchor_registry/<group-id>.json` 与 `<group-id>.bundle.json`。每组严格按单向依赖执行：最后
Gate 只绑定 `handoff-readiness.json`（W92-G8 绑定历史 `evidence-reconciliation.json`）；
Gate Passed 后才生成 pre-handoff snapshot 与 canonical handoff/适用 alias；随后在 Gate 外
运行 `--pre-seal-group <group-id>` 并生成 `preseal-receipt.json`；anchor builder 重算 receipt
绑定的全部 Gate、snapshot、handoff、父 registry/anchor 与四条 append-only ledger prefix 后才允许
first-add anchor。anchor commit 必须是 exact candidate/seal 的 direct child且只新增该 anchor；随后创建
固定 `refs/codex/week84-92/sealed/<group-id>`，把 evidence 原子复制到 Goal CAS，并在 control spine
以 direct non-merge commit 新增 registry/bundle。普通验证器只有同时确认 off-branch Git blob、
candidate→seal 限定 delta、preservation ref、CAS bytes 与 registry chain 后才接受。Week85–89
两条 lane 只合并每周双 registry 的 evidence barrier，不在 W90 前合并 sibling 产品；W90 从 exact
control tip 顺序真 merge 两个 W89 sealed tips，并拒绝 ours/squash/cherry-pick。W92 固定为 G8/G9、
GoalComplete handoff、Gate 外 pre-seal receipt、提交第 14 个 anchor/registry、中央切换 `Complete`、
最后在全部封存 Gate 外运行 `--require-complete`；任何后置结果都不得回写 G8/G9/handoff。
W86/W89/W92
验收前另在 `docs_md/weekly/84_92_user_acceptance_requests/` 提交不可变 challenge
request；用户决定账本只接受该 request 之后发生、绑定同一 candidate/manifest 且
`requestCommit` 等于唯一 first-add commit 的决定。

每次上下文压缩或 Goal 自动续跑先校验中央 control、repo HEAD/dirty state、当前周
review/handoff/evidence hash，再按 `nextAction` 恢复；不凭对话记忆猜测进度。

## 范围护栏

- 不复制 OpenCowork 的 feature breadth；只借鉴 Chat-first 信息架构和组件分层。
- 不在 Renderer 执行文件、shell、MCP 或 agent tools。
- 不改 provider、ThreadStore truth、durable history 或 approval contract 来迁就 UI。
- 不为了排期提高 memory/listener 阈值、删测试、forced GC、reload 或延长 idle。
- 不为了缩小 CLI 文件改变命令名称、默认值、输出 schema 或 exit code。
- 不创建 tag、push、上传、部署或发布，除非用户另行明确授权。

## 最终完成定义

“做到”意味着：

- 用户获得清晰的 OpenCowork 风格 Chat-first Desktop 体验；
- 现有 task/approval/review/terminal/artifact 能力全部可用；
- Renderer 状态和 subscription ownership 可维护且无资源回归；
- CLI command composition 已按 feature 模块化且行为兼容；
- 完整自动化与适用人工 Gate 有可追踪证据。

只完成颜色、圆角、图标或静态三栏，不算本排期完成。

总 Goal **只能**在 Week92 最终决定为 `Refactor Accepted`，全部适用 Gate 为
Passed、W86/W89/W92 用户视觉确认和 W91 Narrator 为 Passed、provider turns
累计不超过 120、开放 P0/P1 与 cleanup 为 0 时更新为 `complete`。

`Candidate Ready` 是等待缺失人工/授权 Gate 的中间状态，总 Goal 保持 active；
`Blocked` 不是完成，必须保留首败并回到责任周修复。只有同一不可推进阻塞连续三个
Goal turn 后，才按 Goal 协议标记 `blocked`。
