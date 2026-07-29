# Week 84–92 Goal Execution Contract

更新时间：2026-07-28

状态：`Authorized / Ready for bootstrap`

适用计划：

- `docs_md/plans/07_cli_desktop_experience_refactor.plan.md`
- `docs_md/weekly/84_92_week_cli_desktop_experience_refactor_schedule.md`
- `docs_md/weekly/84_week_renderer_listener_retention_refactor_baseline.plan.md`
- `docs_md/weekly/85_week_*` 至 `docs_md/weekly/92_week_*`

机器契约：

- `docs_md/weekly/84_92_week_goal_control.schema.json`
- `docs_md/weekly/84_92_week_gate_result.schema.json`
- `docs_md/weekly/84_92_week_handoff.schema.json`
- `docs_md/weekly/84_92_week_gate_requirements.json`
- `tools/validate-week84-92-goal-evidence.py`
- `tools/test_validate_week84_92_goal_evidence.py`
- `tools/week84_92_goal_integrity.py`
- `tools/test_week84_92_goal_integrity.py`
- `tools/test_week84_92_gate_requirements.py`
- `tools/week84_92_evidence_anchor.py`
- `tools/test_week84_92_evidence_anchor.py`

Schema version：`1.0.0`

## 1. 单一总 Goal

Week84–92 作为一个不可拆分的总 Goal 执行，不为每周创建彼此独立、可以提前宣布完成的 Goal。

建议 Goal objective：

> 按顺序完成 C-AICLI Week84–92 的全部计划、Gate、Review、Handoff 和 Evidence；在 OpenCowork Chat-first 信息架构参考下保留 C-AICLI 品牌，保持协议、安全、CLI 行为兼容和资源阈值不回归；只有 Week92 达到 Refactor Accepted、开放 P0/P1 均为 0 且必要用户验收完成时才结束 Goal。

固定 checkpoint：

```text
W84
  -> W85 Renderer || W85 CLI
  -> W86 Renderer || W86 CLI + user visual acceptance
  -> W87 Renderer || W87 CLI
  -> W88 Renderer || W88 CLI
  -> W89 Renderer || W89 CLI + user visual acceptance
  -> W90 cross-lane integration
  -> W91 hardening + Narrator/manual UX evidence
  -> W92 final acceptance + user visual acceptance
```

总 Goal 只有以下运行状态：

- `Active`：仍有可安全推进的工作、待运行 Gate 或待用户确认。
- `Complete`：仅限 Week92 输出 `Refactor Accepted`，全部必要 Gate 为 `Passed`，开放 P0/P1 为 `0`，Week86/89/92 用户视觉验收和 Week91 Narrator/人工 UX 证据均已闭合。
- `Blocked`：同一个 blocking condition 已连续三个 Goal turn 重复出现，没有剩余安全工作、替代路径或可执行的 boundary request，并已保存完整阻断证据。

预算接近上限、单个 Gate 失败、首次收到 `NO`、等待用户确认、工作困难或一条 lane 暂停，都不能单独成为结束 Goal 的理由。

## 2. 用户已确认的执行授权

用户已在本 Goal 启动前一次性确认授权项 1–8。执行记录必须把这次确认视为明确边界，而不是无限授权。

### A1：本地 Git 与隔离开发

允许：

- 将本次计划、Goal bootstrap 文档、三个 schema、`84_92_week_gate_requirements.json`、
  用于 raw-byte identity 的三个 scoped `.gitattributes`、语义验证器、integrity/anchor helper、
  trusted executor、provider turn harness、对应五个 tracked test、W84-G0 command-control bundle、五个 W84-G0 command scripts，
  以及 `tools/week84_92_provider_scenarios/` 下四个安全敏感 scenario 纳入同一个本地 bootstrap
  checkpoint commit。最终白名单固定为 44 个普通 `100644` 文件，不包含产品源码或既有测试。
- 创建并切换本地 evidence/control 分支 `codex/week84-92-refactor`；W90 Entry 时才从 exact
  control tip 创建本地产品集成分支 `codex/week84-92-integration`。
- 为 Renderer lane 与 CLI lane 创建隔离 worktree/本地分支；固定 persistent refs 分别为
  `refs/heads/codex/week84-92-renderer` 与 `refs/heads/codex/week84-92-cli`，不得按周换名或由调用方自报。
- 进行本地 checkpoint commit 和本地 merge。

禁止：

- `push`、创建远程分支或 PR。
- 创建 tag。
- 上传 artifacts。
- release、publish、deploy 或修改远程状态。

任何以上禁止动作都需要新的、针对该动作的用户授权，不能从 A1 推导。

### A2：仓库写入与验证

允许在仓库范围内修改源码、测试、文档、generated contracts/notices；允许执行 build、test、lint、typecheck、package、unpacked/packaged E2E、security、accessibility、resource 与 smoke 验证；允许在 ignored `artifacts/` 中写入 evidence。

每次写入必须能映射到当前 Week 计划、Gate 或阻断修复。不得借重构扩大产品能力。

### A3：依赖恢复

允许按当前 `global.json`、project files 和 lockfiles 从官方 npm/NuGet 源恢复现有依赖。

未经新的用户确认，不得：

- 新增依赖。
- 升级或降级依赖版本。
- 修改 lockfile。
- 切换非官方或未知 package feed。

Goal 使用仓库锁定的 `.NET SDK 9.0.308`；实际 SDK identity 必须进入 Gate evidence。

### A4：进程与临时目录

允许终止仅由本 Goal 启动、记录 PID 且 ownership 可证明的 Electron、AppHost、Node、MSBuild 或 .NET 子进程。

允许清理经过绝对路径与 ownership 校验的：

- build output。
- Playwright result。
- 本 Goal 测试临时目录。
- harness 创建的临时 workspace。

不得终止未知或用户拥有的进程，不得对 workspace 根、用户目录、未解析变量、glob 或 ownership 不明路径执行递归删除。

### A5：产品与视觉边界

视觉目标冻结参考 `AIDotNet/OpenCowork@b4afc37d0f77a4bce8da7c16deb2bddc94bd5dfb` 的 Chat-first 信息架构、信息密度和交互层级，同时保留 C-AICLI 品牌与现有产品 authority。后续 OpenCowork upstream 变化不能静默改变本 Goal 范围。

本 Goal 不做：

- OpenCowork 像素级复制。
- Agent Teams、插件市场、远程控制等新增能力。
- 为视觉效果迁移 provider、ThreadStore、approval 或执行 authority 到 Renderer。
- 默认修改 `desktop-v1`。

Windows 是 Week84–92 的正式验收平台。

### A6：真实 Provider

真实 provider 与 `.env.local` 读取授权只适用于 Week84 和 Week92 的以下 Gate：

- `W84-G6`：provider read-only 与 recovery。
- `W84-G7`：连续五轮 provider resource profiles。
- `W84-G8`：受控写入闭环中的单次 provider turn。
- `W92-G7`：provider read-only、recovery、连续五轮 resource profiles 与受控写入闭环。

上述 Gate 只允许读取 `.env.local` 中必要的：

- `OPENAI_MODEL`
- `OPENAI_BASE_URL`
- `OPENAI_API_KEY`

允许用于 provider read-only、recovery 和连续 resource Gate。真实配置值只能注入 bootstrap-frozen
provider harness 内的 gateway，candidate/scenario child 只接收 loopback URL、model 与不可用的占位 key；
真实值不得输出、进入命令文本、日志、截图、evidence 或 commit。
在读取前必须只用 scrubbed Git metadata 证明 `.env.local` 是 canonical regular/no-reparse、当前
untracked、被 ignore 且整个可达 history 从未包含该路径；tracked、未 ignore 或历史曾提交都在
打开文件前 fail closed，并且错误不得回显内容。

Week85–91（包括 W91 package/smoke/hardening）以及 Week84/92 的其他 Gate 均不得读取 `.env.local`、调用真实 provider、声明 provider scope 或消耗 provider turn；这些 Gate 的 provider-facing smoke 必须使用 credential-free deterministic fake/mock，`providerTurnsConsumed` 必须为 `0`。中央 ledger 绑定可以在这些 Gate 中只读校验，但不得追加 turn。

真实 provider 的总预算为 **120 turns**，是整个 Goal 的累计上限，不是每周、每个 lane 或每次 rerun 的上限。每个 Gate 必须记录本次消耗、累计消耗与剩余额度。达到 120 turns 后不得继续调用真实 provider；需要先创建 boundary request 并获得新的用户确认。

失败 rerun 仍计入预算。credential-free fake/deterministic tests 不计入 provider turns。

按仓库冻结 harness 计算，`Passed` 的最低真实 provider 工作量为：`W84-G6=3`（read-only 1 + recovery 2）、`W84-G7=30`（五个 profile，每个 1 warm-up + 5 measured）、`W84-G8=1`，以及 `W92-G7=34`（同一 read-only/recovery/resource 序列 + controlled write 1），全 Goal 最低共 `68/120 turns`。这些是完整 workload 的下限，不是可通过减少 workload 达到的目标；失败尝试和完整 rerun 仍逐 turn 累加。每个 provider Gate 必须保存与 raw ledger hash 绑定的结构化 receipt，证明 phase、profile、warm-up/measured 次数、连续序列和 exact candidate，不能以 `providerTurnsConsumed=0` 或仅 credential-free evidence 写成 `Passed`。

provider ledger 使用 write-ahead reservation 与 append-only attempt event 语义。每个真实 provider segment 在启动 isolated child 前，必须先由冻结的 `week84_92_trusted_executor.py` 原子追加一个或多个 `TurnReserved`；reservation 立即占用 120-turn 预算，稳定绑定不可复用的 `reservationId`、`attemptId`、Gate、phase、candidate 与 run identity。child 返回后逐项追加 `TurnCompleted`，attempt 只可追加一个 `AttemptFinished=Passed|Failed`；终结后不得重开或跨 Gate 复用。进程崩溃、取消或超时留下的 reservation 仍计入上限，恢复动作只能补写 `Failed` completion/finalization，不能删除、覆盖或退回预算。Gate 写为 `Passed` 时，每个 provider receipt 的 `attemptId` 必须等于 Gate `authorization.providerSuccessfulAttemptId`，并绑定该唯一 attempt 的连续 reservation range、exact Gate/candidate/run identity 与 raw ledger prefix；只有该最终成功 attempt 必须精确满足冻结 phase 顺序和工作量。它之前的失败 attempt 可以保留且计入总账，但不得被拼接进成功 workload，也不得存在未终结 attempt 或成功 attempt 之后的同 Gate/candidate reservation。

canonical ledger 只存在于 `artifacts/week84-92-goal-control/provider-turn-ledger.json`，不得复制到
Gate artifact。每个 Gate 的 authorization 使用 `providerLedgerPrefixSha256` 绑定该 Gate 完成
时的 canonical prefix；执行 provider/budget 对账的 Gate 还必须保存唯一 canonical 路径
`<artifact-root>/provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`，由验证器重算
reservation/event/combined prefix。live ledger 后续增长不得使历史 Gate 失效。

ledger 顶层字段固定为 `schemaVersion`、`journalVersion=week84-92-provider-runtime-v1`、
`goalId`、`maxTurns=120`、`usedTurns`、`remainingTurns`、`ledgerSequence`、`entries`、
`attemptEventCount`、`lastAttemptEventSha256` 与 `attemptEvents`。每个 reservation entry
固定为 `sequence/eventType/reservationId/gateId/phase/productCandidate/attemptId/runId/`
`reservedAt/previousEntrySha256/entrySha256`；attempt event 使用独立 hash chain，公共字段
固定为 `attemptEventSequence/eventType/previousAttemptEventSha256/attemptEventSha256`，
并按 `TurnCompleted` 或 `AttemptFinished` 的 exact payload 扩展。两条链均用移除自身 hash
字段后的 canonical JSON 计算，不接受 alias、缺项、额外字段、断链或事后重排。

真实请求的独立 append-only journal 固定为
`artifacts/week84-92-goal-control/provider-runtime-journal.json`，其
`journalVersion=week84-92-provider-execution-v1`，顶层固定为
`schemaVersion/journalVersion/goalId/eventCount/lastEventSha256/events`。中央 Goal 与每个
pre-handoff snapshot 的 provider authorization 必须以
`runtimeJournalPath/runtimeJournalSha256/runtimeEventCount/lastRuntimeEventSha256` 绑定当时的
原始 bytes 与 prefix；Gate-local provider ledger binding、pre-seal receipt 和 anchor 还必须绑定
相同 runtime prefix。reservation、completion、runtime observation 三者不能缺项、重复或错配。

四个 provider scenario 是 bootstrap 控制面，而不是 product candidate 可替换的测试脚本。executor、
harness 与 manifest 必须同时绑定其 path/raw SHA-256/first-add blob；scenario 只能从 Goal-owned 临时
目录启动，gateway 只接受冻结 path、method 和 JSON body shape，并在转发前拒绝真实 secret 值。
这些应用层控制本身不等于 OS filesystem/network sandbox：在 candidate 仍能读取宿主仓库或任意
直接出网时，不能声称所有真实调用都进入 ledger。首次 provider Gate 前必须存在一项明确的 boundary
decision：要么以实测 OS/container isolation 限制 candidate 只读 staged package、只写 Goal temp 且
网络只能到 frozen gateway；要么由用户明确接受 cooperative-candidate trust root，并把“120 硬上限”
限定为 harness-mediated 请求。两者都未满足时，W84-G6 保持 `NotRun`，不得读取 `.env.local`。

boundary decision 不是文档口头声明。W84 与 W92 分别 first-add 且不可改写：

```text
docs_md/weekly/84_92_provider_boundary_decisions/W84-G6.json
docs_md/weekly/84_92_provider_boundary_decisions/W92-G7.json
```

每项固定绑定 mode、exact candidate、完整 package-tree manifest、entrypoint/argv、允许的 Gate/scope/
turn 数、`maxTurns=120`、签发/失效/撤销状态以及用户 challenge receipt；`strong-isolation` 还必须绑定
filesystem/network/process canary receipt 与实际 sandbox policy hash，`cooperative-candidate` 必须明确
记录“仅 harness-mediated 请求可证明硬上限，候选可信是 external trust root”。缺失、过期、被撤销、
candidate/package 漂移或 mode 与 receipt 不一致时，executor 必须在读取 `.env.local`、reservation 与
child start 前 fail closed。cooperative 模式不得生成 `isolated-packaged-child`、`all-egress-mediated`
或 OS containment claim；strong 模式未通过 canary 也不得降级后继续。

provider package identity 必须覆盖完整可启动目录，而不是某个可任意选择的单文件。prior-sealed package
command 生成 canonical manifest：固定 repo-relative package root、每个 regular/no-reparse entry 的
规范相对路径/mode/bytes/SHA-256、entry count、total bytes、tree-root SHA-256、exact entrypoint 与 argv，
并绑定 product candidate 和 build receipt。拒绝缺项、额外项、case collision、ADS、symlink/reparse、
hardlink alias、敏感路径或 package root 外引用。harness 必须把完整树按原布局复制到新 Goal-owned
staging root，复制前后重算相同 tree root，实际启动该 entrypoint；不得把任意文件改名为
`package-under-test.bin` 或仅运行 scenario 后合成 HTTP。

每个真实 segment 还必须保存 `package-launch` receipt，至少绑定 staged tree/entrypoint/argv、package
process start/exit、Job 或 sandbox attach-before-release 状态、boundary mode/policy、scenario source、
run token、gateway observation 与产品可观察断言。frozen scenario 的职责是驱动并观察实际 package；
request 只能由该 run 的产品路径触发并与 reservation/observation 对账。gateway 对 outbound API payload
只构造 allowlist 字段，不把内部 descriptor/envelope 直接发送给 provider；真实 base URL 只允许 HTTPS
（credential-free loopback 自测例外）。upstream response 在返回 candidate 前必须以跨 chunk rolling
scan 阻断真实 secret，所有 report/stdout/stderr/evidence 再做完整 secret 扫描。

### A7：受控写入

只允许 Week84 与 Week92 各执行一次计划内 controlled-write Gate，并且必须满足：

1. 当前 controlled-write Gate 之前按 DAG 排序的全部 prerequisite non-write Gate 已通过（Week84 为 `W84-G0..G7`，Week92 为 `W92-G0..G6`），并在执行前冻结每个前置 Gate result 的相对路径、raw SHA-256、Gate id、candidate 与 `Passed` 状态；写入后的 closure Gate 不得被伪装成前置条件。
2. 仅使用 harness 创建并拥有的 `caicli-week*-write-*` 临时 workspace。
3. 唯一目标是把唯一 `result.txt` 从 `fail` 改为 `pass`。
4. 只执行计划冻结的唯一 `dotnet msbuild ...` 验证命令。
5. 不写用户项目、其他 workspace 或其他文件。
6. evidence 对路径、provider 配置和 write arguments 做脱敏。
7. 完成后先验证 ownership，再清理本 Goal 临时 workspace。
8. 创建 lease 前必须先形成 tracked canonical preauthorization，绑定 Week/Gate/exact candidate、前置 Gate bindings、harness workspace identity、冻结 transition/命令以及两个 durable approval decision；lease 一经首次 write 尝试即记为 consumed，失败、取消或超时也不得续期、补发、重放或再次执行。

两个 approval decision 与汇总 preauthorization 分别固定在
`docs_md/weekly/84_92_controlled_write_authorizations/<gate-id>/approval-apply-patch.json`、
`approval-shell.json` 和 `preauthorization.json`。三者均使用 `-text` raw-byte identity、single first-add
commit 与不可改写 Git bytes；两个 approval identity 必须不同，decision 必须先于 preauthorization，
且各自绑定 candidate tree、全部前置 Gate raw hash、workspace、`result.txt fail -> pass` 与冻结
`.NET SDK 9.0.308` msbuild command。缺失、单 approval、hash/时间/ancestry 漂移时必须在生成
tombstone、预留 turn 或启动 child 前失败，reservation delta 与 child count 都必须为 0。

lease 的 durable consume 记录必须在 write child 启动前以 exclusive-create 方式写入并单独提交到 canonical tracked 路径 `docs_md/weekly/84_92_controlled_write_tombstones/W84-G8.json` 或 `W92-G7.json`。该 tombstone 使用 `-text` raw-byte identity，首次 add 后不得删除、改写或追加历史；candidate 必须是 preauthorization、approval decisions 与 tombstone 各 first-add commit 的严格祖先。冻结 executor 只有在这四个文件的 worktree/index/HEAD/first-add blob、前置 Gate raw hashes 与 receipt binding 全部一致时才可预留 controlled-write turn；provider ledger 中一旦出现该 Gate 的 controlled-write reservation，即永久拒绝第二次 reservation，即使第一次失败、取消、超时或进程崩溃。

tombstone 顶层只允许 `schemaVersion`、`goalId`、`gateId`、`productCandidate`、`leaseId`、
`nonceSha256`、`issuedAt`、`consumedAt`、`preconditionGateBindings` 与
`preauthorizationBinding={path,sha256,commit}`；receipt 分别以
`controlledWriteTombstone={path,sha256,commit}` 和 `controlledWritePreauthorization={path,sha256,commit}`
绑定 consume 与事前授权，不接受同义 alias 或冲突的重复字段。
两个授权周之间的 lease、nonce 与 durable approval identity 必须全局唯一，且 tombstone/
lease/write 时间必须处于对应 Gate 时间窗内并满足既定先后关系。

任何偏离以上形状的写入都不在授权内，必须创建 boundary request。

controlled-write Gate 的 evidence 必须包含可机读 receipt，并由 Gate evidence 的 raw SHA-256 绑定。receipt 至少证明：Week/Gate/exact candidate、harness ownership 与已校验的相对 workspace identity、唯一变更路径 `result.txt`、`fail -> pass`、唯一冻结的 `.NET SDK 9.0.308` `dotnet msbuild` 命令、两个不同且不可重放的 durable approval、全部 prerequisite non-write Gate 的 path/raw SHA-256/candidate/Passed bindings，以及 cleanup 后进程、临时路径和配置残留均为 `0`。receipt 还必须绑定唯一 `leaseId`、签发/消费时间、`leaseStatus=Consumed` 和首次 write attempt identity，证明本周没有第二张 lease、第二次 write attempt 或重放。布尔字段 `shapeVerified=true` 只能作为汇总，不能替代 receipt 内容。

### A8：用户人工验收

必须在以下 checkpoint 邀请用户确认，不得由实现 Agent 代签：

- Week86：Chat-first Shell、布局层级、C-AICLI 品牌和三种 viewport 截图矩阵。
- Week89：Conversation、Composer、Context/Review workspace 的完整体验截图矩阵。
- Week92：最终 packaged candidate 的视觉与产品接受。

Week91 还必须由 Goal-owned test operator 记录 Windows Narrator 与人工 UX Gate；自动化 accessibility 通过不能替代 Narrator/人工证据。该证据绑定 exact candidate，并在用户已允许的 Week92 checkpoint 与最终视觉 manifest 一并复核，不创建第四个用户暂停点。

用户确认前，对应人工 Gate 必须为 `NotRun`；明确不适用且有计划依据时才可为 `NotApplicable`。不得把“已展示截图”自动写为 `Passed`。

## 3. 双 Lane 与冻结区

Week85–89 使用两个隔离 lane：

| Lane | 默认写入范围 | 禁止写入范围 |
| --- | --- | --- |
| Renderer | `apps/desktop` 与对应 tests/docs | `src/CSharpAiCli.Cli` |
| CLI | `src/CSharpAiCli.Cli` 与对应 tests/docs | `apps/desktop` |

Gate 到 branch 的映射只取 bootstrap-frozen requirements manifest：Week85–89 的 `renderer`/
`cli` 分别必须位于上述两个 persistent refs；Week84 `baseline` 必须位于
`refs/heads/codex/week84-92-refactor`，Week90 `integration`、Week91 `hardening` 与 Week92
`acceptance` 必须位于 `refs/heads/codex/week84-92-integration`。branch/ref、
worktree 或 lane 不匹配时在 child 启动前 fail closed。

共享冻结区：

- Application/Core public contracts。
- AppHost authority 与 lifecycle contract。
- `desktop-v1` protocol。
- generated C#/TypeScript contracts。
- provider、ThreadStore durable truth 与 approval contract。
- release/package scripts。
- Week84 resource controls、阈值、workload、workers、retries 和 measurement barriers。

Week85–89 默认不修改共享冻结区。若一个 lane 确实需要修改：

1. 停止该项修改，不停止另一 lane。
2. 保存问题、最小候选差异与兼容影响。
3. 创建 boundary request。
4. 获得用户确认或在 Week90 integration 中按契约处理后再继续。

Week90 前不得通过跨 lane 直接编辑来“顺手解决”集成问题。

## 4. 固定 Gate 状态

所有机器 Gate、人工 Gate 与检查项只能使用：

- `Passed`：按冻结参数完整执行并满足全部 acceptance assertions；有可定位 evidence。
- `Failed`：已执行且至少一个 acceptance assertion 失败；必须保存首次失败。
- `NotRun`：尚未执行、上游 Gate 阻断、等待授权或等待用户确认；必须记录原因和 `nextAction`。
- `NotApplicable`：当前 checkpoint 按计划明确不适用；必须记录计划依据。不得用它规避必跑 Gate。

禁止使用 `Skipped`、`Green`、`Success`、`Blocked`、`Unproven` 或空值代替 Gate 状态。`Blocked` 只能是 handoff/Goal 决策，不能伪装成 Gate 执行结果。

Gate 只有在以下信息完整时才可写为 `Passed`：

1. source/package/AppHost/Renderer/CLI identity（适用部分）。
2. 实际命令、退出码、开始/结束时间。
3. command counts 与 test counts。
4. auth scopes 与 provider turns。
5. acceptance assertions。
6. evidence 路径与 hash（计划要求时）。
7. cleanup 结果。
8. 开放 P0/P1 与后续动作。

## 5. 首次失败与重跑

每个 Gate 的第一次正式失败必须作为 `firstFailure` 单独保留：

- 不覆盖、不改写成最终绿灯。
- 保存发生时间、phase、redacted command identity、exit code、摘要与 evidence refs。
- 后续若确认是 harness/observer 问题，也保留原始记录，并用 `classification` 与 `supersededBy` 解释。
- 修复后必须按原冻结参数重跑完整 Gate；不能只运行失败 case 拼接通过。
- provider resource 五轮是不可拆分 Gate；不能用五次分散的单轮绿灯组成结果。

并发测试造成的 contention 可以解释，但原始失败仍必须保留；完整串行 rerun 的命令、计数和差异必须另记。

`ReadyForNextCheckpoint` 或 `GoalComplete` 只表示当前 required Gates 已闭合，不表示历史上从未失败。handoff 的 `firstFailures` 必须精确覆盖其 Gate results 中所有非空 `firstFailure`，并保持 failure id、classification、evidence refs 与 resolved 状态；不得为了进入 Ready/Complete 把该数组清空。handoff 的 Gate reference 还必须绑定 raw Gate JSON 的 SHA-256，使重跑后的汇总无法静默替换首败记录。

## 6. Identity 与计数

每个 checkpoint 在写产品代码前冻结 identity，在 handoff 时再次记录：

- branch、worktree、source HEAD、dirty state。
- baseline、product candidate 与 documentation/checkpoint commit 的不同角色。
- CLI build identity。
- Desktop package tree、`app.asar`、AppHost 与 Renderer bundle identity。
- Node/npm/Electron/.NET SDK identity。
- generated contract 与 protocol method/event count（适用时）。

W84-G0 明确区分 Week83 product baseline `P` 与 Goal bootstrap revision `B`：`B` 必须直接建立在
冻结 bootstrap parent 上，diff 只能是 44 个控制面文件，且 `P..B` 在冻结 product/package input
路径上的 diff 必须为 0；W84-G0 的 execution candidate 是 `B`。之后每个 Gate 记录自己的 exact
candidate；同组 candidate 只允许 ancestor-or-equal 单调前进，每个 Gate candidate 都必须是该组
最终 candidate 的祖先，最后一个 Gate candidate 必须等于 snapshot/handoff/anchor candidate。
除这种已证明 product-input-equivalent 的 bootstrap revision，以及 manual request 或 controlled-write
所需的受限不可变后继 commit 外，不得把纯文档、harness 或 evidence commit 冒充产品变化。

W84-G0 的 `baseline-identity.json` 是 canonical、additional-key-closed 的 identity receipt。它必须逐字段
绑定 `entry.json` 的 raw SHA-256、`B/P`、Week83 documentation closure、bootstrap parent 与冻结
product-input path 集，同时绑定
`artifacts/week83-approval-projection-remediation/package-identity.json` 的冻结 raw SHA-256，并精确投影
Desktop package、完整 package tree（含 bytes/file count）、`app.asar`、AppHost 与 Renderer bundle
identity。验证器必须独立执行 `P..B` 冻结 product-input paths 的 zero-diff 检查；只写
`status=Passed`、遗漏任一 identity、嵌套或额外字段、或同时改写 source receipt 与引用 hash 均不得通过。

command counts 至少包含：

- total、passed、failed、notRun、notApplicable。

test counts 至少包含：

- discovered、passed、failed、skipped、notRun、notApplicable。

`skipped` 只描述测试框架报告的测试数量，不是 Gate 状态；任何新增 skip 必须在 review 中解释。

## 7. Boundary Request：提前确认而不是中途结束

出现以下情况时，必须创建 boundary request：

- 需要新增/升级依赖或修改 lockfile。
- 需要修改共享冻结区或扩大产品能力。
- 需要超出 controlled-write 形状的外部写入。
- provider 累计 turns 将超过 120。
- 需要 push、tag、PR、release、publish 或 deploy。
- 需要终止 ownership 不明进程或清理 ownership 不明路径。
- 需要读取已授权三项之外的 secret/config。
- 用户视觉结论或 Goal-owned operator 的 Narrator/人工 UX 观察存在实质歧义。
- 用户的 `NO` 使当前实现路径不再允许，但仍可能存在边界内替代方案。

处理规则：

1. 立即停止越界动作，不执行猜测性修改。
2. 记录 `requestId`、类别、准确动作、原因、影响、最小替代方案与所需用户决定。
3. `nextAction.kind` 写为 `AwaitUserConfirmation`。
4. 继续其他 lane、credential-free tests、文档、诊断或所有不依赖该决定的安全工作。
5. 用户回答 `Approved` 后只执行获批 scope；回答 `Denied` 后在原边界内重排，不自动结束 Goal。
6. 同一阻断连续三个 Goal turn、已无安全工作与替代路径时，才允许把 Goal 标记 `Blocked`。

Boundary request 的用户问题必须具体到动作和边界，不能只问“是否继续”。

## 8. 每周执行与独立复核

每个 Week/lane 固定执行：

```text
Entry Audit
  -> baseline identity freeze
  -> implementation
  -> targeted tests
  -> full regression
  -> independent review
  -> Gate result set
  -> Week review
  -> handoff
  -> next checkpoint
```

实现 Agent 不能自行宣布 checkpoint 通过。独立复核至少检查：

- diff 与计划 scope。
- 冻结区与 auth scopes。
- first failure preservation。
- command/test counts。
- provider turns accounting。
- evidence 与 identity。
- Gate 状态是否符合四值定义。
- P0/P1、cleanup、boundary requests 与 next action。

任一 required Gate 为 `Failed` 或 `NotRun` 时，handoff 不能写 `ReadyForNextCheckpoint`。`NotApplicable` 必须有计划依据且不能用于必要 Gate。

## 9. 机器状态与 Evidence 约定

总 Goal 的唯一可恢复状态文件：

```text
artifacts/week84-92-goal-control/goal-state.json
artifacts/week84-92-goal-control/first-failure-ledger.json
artifacts/week84-92-goal-control/user-decision-ledger.json
artifacts/week84-92-goal-control/provider-turn-ledger.json
artifacts/week84-92-goal-control/provider-runtime-journal.json
```

中央状态为 `Active`/`Blocked` 时，已存在的 Gate 必须是各组 canonical contiguous prefix，后一 Gate 只能在前一 Gate `Passed` 后出现；组开始前必须已有全部直接父组的 immutable anchor registry entry，且不得跨过尚未封存的更早 Week barrier。`activeCheckpoint`、`activeLanes`、week entry/exit/review/handoffs 与 lane state 必须由最低未封存 frontier 精确推导。并行 Week 中某 lane 已封存当前组而 sibling 尚未封存时，该 lane 仍记为 `Active`，不得退回 `NotStarted` 抹掉已封存进度。

Week85–92 的 tracked evidence 使用永久独立 control spine，而不是要求 divergent product histories 提前包含彼此。`codex/week84-92-refactor` 在 W84 anchor/registry 后只允许 registry/bundle checkpoint；Renderer 与 CLI 从该 checkpoint 分叉到隔离 worktree，W90 再从 exact control tip 创建 `codex/week84-92-integration`。所有 ignored Gate、snapshot、handoff 与四条中央 ledger 仍只写入 control-spine worktree 的 canonical `artifacts/`，由 bootstrap-frozen coordinator 以 common-dir scoped lock 串行追加；product command 从对应 candidate worktree执行，不能创建各自的 ledger fork。executor、pre-seal 与 anchor builder 必须从 scrubbed Git worktree metadata 唯一推导 candidate root 与 control root，不接受调用方用任意绝对路径替换两者。

CLI 仍只接受 `--repo-root <candidate-root>`，不得新增 `--control-root` 或从报告内容信任任意 evidence root。W84 允许 candidate/control 同根；从 W85 linked-worktree 开始，validator 与 executor 必须由 candidate 的 common-dir、branch ref、worktree backlink 和唯一 control-branch worktree 重建 control root。tracked source/schema/requirements/command-control 与 Git candidate identity 只从 candidate root 读取；ignored Goal/Gates/handoffs/reports/streams/ledgers、CAS 与 anchor registry 只从 control root 读取。command-verifier descriptor 的 `evidenceRoot` 必须等于该重建结果，candidate 中同名伪 artifact 不得替代 control evidence。

每组 lane closure 固定区分 `P`（exact product candidate）、`S`（seal commit）与 `A`（anchor commit）。普通组 `S == P`；只有 manifest 枚举的 provider-boundary decision、manual request、controlled-write approvals/preauthorization/tombstone 可以构成 `P..S` 的线性 single-parent checkpoint delta。每个 delta path 只能 first-add 一次，之后不可修改；除这些 exact path 外，`S` 与 `P` 的所有 path/mode/blob 必须相同。`A` 必须是 `S` 的 direct single-parent child，且唯一 diff 是新增该组 canonical anchor 的一个普通 `100644` blob。仅证明 `P` 是 `A` 祖先不充分：merge、`-s ours`、产品 revert、anchor cherry-pick、额外路径或非线性 checkpoint chain 均 fail closed。

每个 `A` 立即由固定本地 preservation ref `refs/codex/week84-92/sealed/<group-id>` 指向，直到 W90 真合流后仍不得删除或移动。SHA 文本本身不保证 Git object 可达；ref 缺失、target 不同、对象丢失或 shallow/replace/graft/alternate repository 均不得注册或推进。anchor 的父绑定同时记录 dependency group、原始 SHA-256、Git blob SHA 与原始 anchor commit；off-branch 验证必须以 `git cat-file <A>:<canonical-path>` 读取该 blob，而不是信任当前 worktree 中恰好同名的文件。

control spine 按固定 DAG 逐组 first-add：

```text
docs_md/weekly/84_92_anchor_registry/<group-id>.json
docs_md/weekly/84_92_anchor_registry/<group-id>.bundle.json
```

每个 registry checkpoint 是前一 control checkpoint 的 direct non-merge child，diff 只能新增本组 registry 与 bundle manifest；spine 上禁止产品、package、test/oracle、canonical anchor 或后加再 revert。registry 绑定 `P/S/A` commit/tree、anchor path/blob/raw hash、preservation ref、checkpoint delta、父 registry、上一 serialization registry、control base 以及 evidence bundle。bundle manifest 按 canonical UTF-8 path 排序记录每个普通 evidence 文件的 mode/bytes/SHA-256，以及四条 ledger 的 checkpoint prefix snapshot；其 count、total bytes 与 content-root hash 由 coordinator 重算。evidence bytes 通过已打开的可信 handle 复制到 ignored Goal-owned content-addressed store，使用 temp + flush + exclusive atomic publish；拒绝 traversal、绝对/device path、case alias、ADS、symlink/reparse、hardlink alias 和复制后的 identity/hash 漂移。registry/bundle 不保存机器绝对路径。

同一 Week 的两个 registry 都存在后，该 control checkpoint 才是下一周 barrier。checkpoint 以 evidence-only merge 进入两条 lane：merge 的第一父必须是本 lane sealed tip，第二父必须是 exact control checkpoint，且相对第一父只引入 registry/bundle 路径，不能带入 sibling product。W86/W87/W88/W89 Entry 分别要求前一周两个 registry 的 downward-closed barrier；同 lane 的 protocol parent anchor 仍必须在本 lane ancestry 中。这样两个 lane 能看到全局验收进度而不在 W90 前合并彼此产品。

它必须符合：

```text
docs_md/weekly/84_92_week_goal_control.schema.json
```

`first-failure-ledger.json` 是 append-only、可重算 SHA-256 hash chain。中央 `firstFailureLedger` 绑定其 raw file hash、entry count 和 last entry hash；每个非空 Gate `firstFailure` 必须对应唯一 ledger entry。每个 pre-handoff `goal-control-snapshot.json` 冻结当时的 ledger binding，语义验证器要求所有历史 snapshot 的 count/last hash 都是当前 ledger 的真实前缀，因此后续重跑不能通过同时清空 Gate 与 handoff 字段来删除已经进入 checkpoint 的首次失败。ledger 只保存脱敏摘要、相对 evidence refs 和不可重放的 identity，不保存 secret、完整命令或绝对用户路径。

failure ledger 顶层固定包含 `schemaVersion`、`goalId`、`hashAlgorithm=sha256-canonical-json-v1`、`entryCount`、`lastEntrySha256` 与 `entries`。每个 entry 固定包含 `sequence`、`failureId`、`observedAt`、`checkpoint`、`lane`、`gateId`、`phase`、`classification`、脱敏 `summary`、`evidenceRefs`、`previousEntrySha256` 与 `entrySha256`。`entrySha256` 的输入是移除自身字段后的 entry，按 UTF-8、Unicode 原样、key 排序、无多余空格的 canonical JSON（`sort_keys=true`、separators `(',', ':')`）计算；第一项 previous 为 `null`，后续必须精确指向上一项。任何缺项、重号、断链、重算不一致或 snapshot prefix 不一致均 fail closed。

`user-decision-ledger.json` 使用同一 canonical hash-chain 算法；用户 checkpoint 顺序固定为 W86 visual、W89 visual、W92 final visual，W92 的展示材料同时包含 W91 Narrator/operator evidence review。W89/W92 请求前展示 manifest 固定为不可改写的 `screenshot-manifest.json`；回复后的 `visual-acceptance.json` 只是 receipt，不能作为 challenge 的预绑定对象。W92 screenshot manifest 还必须绑定 W91 `narrator-manual.json`、`operator-ux-attestation.json` 的 raw SHA-256 与 `w91-hardening` anchor raw SHA-256。用户若在某 checkpoint 给出 `Failed`，修复后的新 candidate 可以追加同一 acceptance id 的新决定，但旧决定不得覆盖或删除，且后续 checkpoint 不得早于前一 checkpoint 的 `Passed`。每项必须绑定唯一 `decisionRequestId`、该 request 的唯一 first-add `requestCommit`、acceptance id/checkpoint、exact product candidate、展示给用户的 screenshot manifest SHA-256、challenge code、规范响应串、该响应 UTF-8 bytes 的 SHA-256、`confirmedBy=User`、实际状态与时间；`requestCommit` 的 Git commit time 必须早于 `decidedAt`，receipt/handoff/central 三处必须一致。早于其 checkpoint 的 handoff 中未来验收只能是 `NotRun`，不能由 Agent 预填 `Passed`。pre-handoff snapshot 同样冻结该 ledger 的 count/last hash prefix。W91 operator evidence 不写入 user-decision ledger，而由 W91-G2、tracked evidence anchor 与 W92 screenshot manifest 三重绑定。

每次请求 W86/W89/W92 用户验收前，必须先在 exact product candidate 的后继 checkpoint commit 中新增且提交 `docs_md/weekly/84_92_user_acceptance_requests/<decisionRequestId>.json`。请求固定绑定 acceptance/checkpoint、candidate、展示 manifest SHA-256、随机 challenge code、请求时间与 `AwaitingUser`；首次 add commit 后 worktree、index、HEAD bytes 必须始终等于 first-add blob，且 candidate 必须是该 commit 的严格祖先。向用户展示时要求其原样回复 `<decisionRequestId> <challengeCode> <Passed|Failed>`；ledger 保存这一规范响应串并重算其 UTF-8 SHA-256，不能接受任意占位 hash。该机制证明请求先于决定并锁定候选/材料/响应，但不声称对聊天作者身份提供密码学证明；当前用户会话仍是最终 human provenance trust root。

平台当前没有向仓库验证器暴露带签名的 conversation event/user identity，因此静态 Git/JSON
验证不能证明一条 `Passed` 确由用户而非 Agent 编造。三个 manual Gate 明确依赖当前 Codex 用户
会话这一 external trust root：Agent 只能在实际收到完全匹配的回复后抄录决定，不能自行生成；
若对话证据不可用或不确定，Gate 必须保持 `NotRun`。ledger 的 machine claim 仅限 bytes、hash、
candidate、challenge 与时间顺序，不把它描述为平台认证签名。

每个 Gate 保存独立结果：

```text
artifacts/weekNN-*/gates/<gate-id>.json
```

Gate result 中每个 manifest-required evidence 必须使用唯一 canonical 路径
`artifacts/weekNN-*/gate-evidence/<gate-id>/<basename>`；同一组内不同 Gate 即使要求同名
`final-summary.json`、`commands.json` 或 `test-results.json`，也不得共享、覆盖或交叉引用。
每个 required basename 在该 Gate 中必须恰好出现一次，Gate 内 evidence id/path 必须唯一，
且任一路径不得被两个 Gate 同时引用。唯一目录例外是
`provider-ledger-binding.json`，其固定在
`artifacts/weekNN-*/provider-ledger-bindings/<gate-id>/provider-ledger-binding.json`。
Entry Gate 所列的 prior-handoff basename 是 Gate-local、hash-bound 的验证回执；原始 canonical
handoff 仍只保留在父组 artifact 目录并由 transition/anchor 规则独立核验，禁止复制或改写为
当前 Gate 的成功结果。

它必须符合：

```text
docs_md/weekly/84_92_week_gate_result.schema.json
```

每个 Week/lane 保存 handoff：

```text
artifacts/weekNN-*/weekNN-<lane>-handoff.json
```

为避免 live `goal-state.json` 与 handoff 互相保存 raw SHA-256 形成不可解的循环哈希，每个 canonical handoff 在写入前先在同一 artifact 目录冻结 `goal-control-snapshot.json`。handoff 的 `goalControlBinding.path/sha256` 只绑定该 pre-handoff snapshot 的原始 bytes；随后中央 `goal-state.json.finalHandoff` 才绑定 handoff 的原始 bytes。语义验证器必须核对 snapshot 与 handoff 同目录、raw hash、Gate registry、provider ledger sequence、candidate 和当时 checkpoint 状态，不得把 live central state 当作 snapshot。

每个 handoff 还必须用 `parentHandoffs` 绑定直接前驱 canonical handoff 的相对路径、raw SHA-256 与 product candidate。W84 无父项；W85 两 lane 都以 W84 为父；W86–W89 各 lane 只继承同 lane 前一周；W90 同时继承 W89 Renderer 与 CLI；W91 继承 W90；W92 继承 W91。语义验证器同时检查这些 raw bytes binding 与 Git candidate ancestry，禁止用互不相关但格式正确的提交拼成 lineage。

每组 closure 必须遵循不可逆的单向字节依赖，禁止最终 Gate 与 handoff 互相绑定：

1. 最后一个 Gate 只绑定同一 Gate 目录内的 `handoff-readiness.json`；W92-G8 例外绑定
   `evidence-reconciliation.json`，W92-G9 仍绑定 `handoff-readiness.json`。readiness 只证明
   生成条件，不得包含尚未生成的 final handoff、pre-seal receipt 或 anchor 的 raw hash。
2. closure Gate 为 `Passed` 后，冻结 `goal-control-snapshot.json`，再生成 canonical handoff
   （W84 同时生成 canonical 等价 alias）。handoff 可以单向绑定全部已冻结 Gate raw hash。
3. 在 Gate 与 handoff 外、以 control-spine worktree 为 evidence root 运行
   `python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root . --pre-seal-group <group-id>`；
   成功时生成 canonical `preseal-receipt.json`。receipt 必须绑定 validator、anchor helper、
   requirements manifest、全部 Gate、snapshot、handoff/alias、父 registry/anchor 和四条 ledger prefix
   的当前 raw bytes，并记录 exit `0`。不得把 pre-seal 结果回写 Gate、readiness 或 handoff。
4. anchor builder 必须在创建前重新计算并验证该 receipt 仍与当前 bytes 完全一致，随后才生成
   `docs_md/weekly/84_92_evidence_anchors/<group-id>.json`。anchor 还要绑定 receipt 的 path/raw
   SHA-256；receipt 缺失、过期、被改写或 pre-seal 后任一输入漂移都必须在 first-add 前拒绝。
5. anchor 单独 review 后形成上述 `A`，创建/验证 preservation ref，再由 control coordinator 复制
   evidence 到 content-addressed store并生成 bundle manifest/registry。registry commit 通过普通语义
   验证器后，该组才算 sealed；下一周必须等待本周两个 lane registry 都封存。后续追加 ledger 不
   改变已封存 prefix；任何历史 anchor/registry 改写、ref 漂移、CAS byte 漂移、父 DAG 缺口、
   ignored artifact hash 漂移或非 downward-closed registry 集都 fail closed。

W90 是两条产品 history 第一次正式合流。独立 integration ref 以 exact W89 control-spine tip `Ct` 为
第一父，先 merge registry-bound Renderer W89 anchor tip 得到 `MR`，再以 `[MR, CLI-W89-A]`
形成第二个 exact-parent merge；不得用 squash、cherry-pick 或仅 ancestry-equivalent tip 代替。
每个 merge commit 本身必须携带相对正确 merge base 的全部单边 blob/mode contribution；`-s ours`
后再补拷产品同样失败。只有双方同时修改同一路径时才允许 tracked conflict-resolution manifest，
并在任何集成修复前先冻结 merge receipt。W90 注册完成后将 evidence-only R90 barrier merge 回
integration ref 再进入 W91，W91/R91 到 W92 同理；product history 不进入永久 control spine，
W90–92 继续使用同一 registry/anchor 协议。

Week92 使用同一单向流程但中央状态切换顺序固定：中央 Goal 仍为 `Active` 时，先完成
W92-G8 的历史 evidence reconciliation 和 W92-G9 的用户决定/GoalComplete readiness；两个
Gate 都 Passed 后才生成 `GoalComplete` handoff；随后在 Gate 外运行 pre-seal、生成并提交
第 14 个 anchor、preservation ref、bundle 与 registry；再把中央状态切换为 `Complete`；最后在所有已封存 Gate 外运行普通验证器
的 `--require-complete` 终验。pre-seal 与 `--require-complete` 都不得回写 W92-G8/G9 或 handoff。

它必须符合：

```text
docs_md/weekly/84_92_week_handoff.schema.json
```

每次 checkpoint 推进前还必须运行 tracked 语义验证器；它以固定 104-Gate registry 重新计算 Gate/命令/测试计数，核对 handoff 集、provider raw ledger SHA/sequence、controlled-write 周次与次数，并在 Week92 证明中央 `Complete` 真实闭合：

```powershell
python -B -X utf8 -m unittest tools/test_validate_week84_92_goal_evidence.py tools/test_week84_92_goal_integrity.py tools/test_week84_92_gate_requirements.py tools/test_week84_92_evidence_anchor.py tools/test_week84_92_trusted_executor.py -v
python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root .
```

冻结的 `goal-evidence-semantic-validate` policy 必须先对当前 partial state 运行语义验证器，
再在同一个 attempt-bound 受信 report 中运行上述五个 unittest 模块；W84-G0 与 W92-G8 必须保存该
consolidated test-report。W84-G0 required `goal-contract-unittest.json` 是逐字段绑定该唯一 report 的
path/raw hash、attempt、counts 与 candidate 的 canonical `json` receipt；
`trusted-executor-unittest.json` 只能继续绑定同一 report、executor/control/policy identity 与五组
adapter/verifier report。两者都不能声明为第二个 `test-report` 或调用方自报 test envelope；依赖方向
固定为 reports → summaries → Gate，reports 不得反向引用 summary/Gate。第 14 个 anchor 提交且中央状态切换
`Complete` 后，再在封存 Gate 外运行 `--require-complete`；其结果是可重现的 Goal 终验
记录，不回写任何已封存 Gate。验证器、integrity/anchor helper、任一测试或 `jsonschema`
不可用时 fail closed，不得仅凭手填 `countsBalanced=true` 推进。

任何 `minimumTestCount > 0` 的 Gate 都必须由 bootstrap-frozen
`tools/week84_92_trusted_executor.py` 以 `shell=false` 启动 test-count-source command，并提供
Gate-local `test-report`。固定 `week84-92-trusted-executor-v1` provenance envelope 必须逐字节
绑定 executor source raw hash、exact Gate/candidate、manifest 中允许的 command id、脱敏
invocation、开始/结束时间、exit code 以及互不相同的 UTF-8 stdout/stderr raw hash；报告 counts
必须由受支持的 runner output/parser 得出并与 Gate counts 完全一致，不能由调用方同时伪造
command 与 report 自证。所有 `minimumTestCount > 0` Gate 还必须使用 manifest 中唯一冻结的
`week84-92-semantic-test-v1` exact command policy：当前 Python interpreter identity、逐项 argv、
固定 redacted invocation、`python-unittest-output` parser、canonical policy hash 与 canonical argv
hash 必须同时匹配；任意 `python -c`/marker 替换即使输出形状合法也不得通过。该 semantic
control-plane test-report 不能替代 Gate manifest 中其余产品 build/test/E2E command evidence。

manifest 中除 `goal-evidence-semantic-validate` 外的每个 `requiredCommandId` 都必须由同一
bootstrap-frozen executor 的 `trusted-product-command` 模式执行，但 product candidate 不得
提供或修改当前 Gate 的 oracle。除 W84-G0 外，每个 Gate 在产品/测试实现前先创建唯一 tracked
`docs_md/weekly/84_92_command_control/<gate-id>.json`；manifest 的 `commandControl` 冻结
predecessor 来源与 mode。该 control bundle 与本 Gate 新增/变更的
`tools/week84_92_commands/<command-id>.py`、tests、fixtures、parsers 必须在同一个 direct
single-parent control commit 中冻结，commit parent 必须由 Gate DAG/parent handoff/entry base
推导而非 caller 自报，diff 只能是 bundle 列出的 exact control/oracle paths，不能包含产品、
dependency/lockfile 或既有冻结 harness 变化。bundle、adapter 与 oracle bytes 使用 `-text`，
worktree/index/HEAD 必须始终等于 control-revision blob。

Gate candidate 默认必须是 control revision 的严格后继；`controlRevision..candidate` 不得改写任何
bundle/adapter/oracle/fixture/parser、scoped `.gitattributes`、dependency lock 或 bootstrap harness。
除 W84-G0 bootstrap 外，仅 `W84-G1/regression-baseline` 与
`W84-G2/ownership-classification` 可以令 execution candidate 等于 control revision，且 control 必须是
前一 Gate candidate 的 direct single-parent child。该窄例外必须保存 `week84-production-projection-v1`
清单：`excludedPaths` 只能是本次 control document 与已批准的 sealed adapter/oracle/test/fixture/parser/
source-tree prepared paths；排除这些路径后，control revision 的全部 tracked production projection 必须与
直接 parent 逐项相等并绑定 entry count 与 canonical root hash。错误 Gate kind、额外生产路径、非直接
parent、漏列/多列 prepared path 或 projection 漂移均 fail closed；不得为诊断 Gate 制造空 product commit。
executor 不接受 caller argv，只从 immutable adapter/policy 推导固定 argv，以 `shell=false`、
scrubbed environment 和冻结 runner parser 执行 first-control blob。产品命令固定采用两阶段验真：

1. `adapter` 只执行被观察 workload。executor 将其 stdout/stderr/exit code 与执行前后 checkout identity
   写入 Gate-local observation report；该 report 的 `commandRole=adapter`、
   `countsSource=adapter-observation-only`，trusted test counts 必须全部为 0。adapter 不具备宣告
   `Passed` 的 authority；stdout/stderr 出现 `CAICLI_PRODUCT_COMMAND_RESULT=` 或其他受信结果 marker
   时立即以 `ADAPTER_SELF_ATTESTATION` fail closed。
2. executor 随后只执行同一 strict-prior command-control 中 `role=test|oracle` source 的 exact
   `verification.arguments`。每个 argument 必须是该 sealed source 内的完整 unittest 名称，且其
   `verifiesCommandIds` 必须包含被验证命令；caller 不能增加、删除、改序或替换测试。
3. executor 以 canonical verifier descriptor 绑定 adapter report 相对路径/raw SHA-256、command id、
   adapter exit code、Gate/candidate/control revision 与 checkout identity。只有独立 verifier report
   可以贡献 discovered/passed/failed/skipped counts；report 必须列出 `verifiedAdapterReports`，且
   adapter 成功、全部 exact verifier 通过、绑定与执行后 identity 均一致，产品命令才可 `Passed`。
4. Gate `commands[].evidenceRefs` 必须同时引用 adapter observation 与对应 verifier report；只引用
   adapter、只引用 verifier、使用未绑定的通用测试输出或调用方手写 `{status: Passed}` 均不成立。

两类 report 都必须绑定 policy raw hash、Gate/candidate、`preparedFromRevision`、`controlRevision`、
script/oracle path/blob/raw SHA、before/after checkout identity、timestamps、exit code以及 distinct
stdout/stderr raw hashes。缺失的 234 个未来 adapters 必须按 Gate 逐步先冻结再实现；未准备不能运行，
更不能用当前 candidate 新增一个只打印 `Passed` 的脚本补齐。

W84-G0 是唯一 bootstrap `controlRevision == candidate == B` 例外，只开放 bootstrap allowlist 中
exact 五个 adapter 与既有冻结 semantic suite，并要求 `P..B` product/package inputs diff 为 0；
report 标明 `sourceTrust=bootstrap-external-review`。W84-G1/G2 只适用上文 production projection
严格相等的诊断例外；其他后续 Gate 均要求 strict descendant candidate。Git/runner 机器校验只能证明 oracle 的来源、
时序与不可改写，不能证明任意程序的语义正确；独立 code review 是明确的 external trust root。
同一用户权限下的 linked worktree、control root 与 ignored CAS 也不是 OS access boundary：普通 product/
test child 理论上可以发现并改写同用户文件或调用 broker。除非后续有实测的全命令 sandbox receipt，
本 Goal 对候选代码采用 cooperative-candidate trust，机器 claim 是每次执行前后的 hash/identity/chain
可审计与 fail-closed，不声称对主动恶意 candidate 提供 tamper-resistant 证明。任何命令结束后必须
重新校验 control ref、candidate tree、central ledgers、已存在 registry/CAS 与 report handles；发现
漂移立即记首败并拒绝 Gate Passed。
任意调用方手写 `{status: Passed}` 的 generic JSON 不能满足 command provenance。

provider-boundary decision、manual acceptance 与 controlled-write 是仅有的 checkout 后继例外：
前两者只允许对应 immutable boundary decision/manual request，后者只允许两个 approval decisions、preauthorization 与 canonical tombstone；
Gate candidate 必须是每个 first-add commit 的严格祖先，后继历史不得包含其他路径，且 before/after
identity 仍必须完全稳定。

真实 provider 命令还必须使用单独冻结且 raw-hash pinned 的
`tools/week84_92_provider_turn_harness.py` 与按 phase 固定的四个 bootstrap scenario。executor 只允许 manifest 的 exact batch shapes：
四个 package runtime `.mjs` driver 不属于 bootstrap manifest 的未来文件。它们必须在 `W84-G5`
command-control commit 中以 `prepared` 的 `fixture`/`parser` source 一次性 first-add 并 seal，且不得
贡献 verifier authority；`W84-G6/G7/G8` 与 `W92-G7` 的 `runtimeDrivers` 只能引用该 strict-prior
origin control revision、相同 raw SHA-256/Git blob，origin 到 candidate、index 与 HEAD 均不得改写。
当前 Gate 首次加入 driver、仅在 runtime binding 自报 path/hash、或把 driver 当 oracle/test 均 fail closed。
read-only `1`、recovery `2`、每个 resource profile `6`（一轮 warm-up + 五轮 measured，五个
独立 profile）、controlled-write `1`。每批在 child 前逐 turn reservation；只有 pinned harness
通过受控 provider gateway 输出与每个 reservation 顺序一致的脱敏
`week84-92-observed-provider-request-v1` observation，且无缺失、重复或额外请求，executor 才能
逐项追加 `TurnCompleted`。任意 caller argv、未计数 fan-out、批量自报完成或 observation/hash
不一致都终结 attempt 为 Failed，已 reservation 的 turn 不退回预算。

该机制仍以当前本地 OS、Git 与 Goal-owned test operator 为执行真实性
trust root，不声称提供远程硬件证明。44 个 bootstrap 控制面文件的
worktree/index/HEAD 必须始终等于各自 single first-add blob，且历史不得出现后续改写；独立
Git history/code review 是验证器本身的外部 trust root。bootstrap 同时冻结
三个 scoped `.gitattributes`，把所有 bootstrap 文件以及 anchor、user-acceptance request、
controlled-write approval/preauthorization 与 tombstone JSON 标为 `-text`，确保 Windows checkout 不改写其 raw-byte
identity。bootstrap
文件必须是普通 `100644` blob、不得为 symlink；仓库与 `.git/info/attributes` 不得以
`filter`、`working-tree-encoding`、`ident` 或更深层 attributes 掩盖 worktree/index/HEAD 的
真实差异。截图/视频不仅核对扩展名与 magic，还必须结构化解析完整容器、拒绝截断/伪造
文件并对有界解压后的 metadata 做 secret 扫描；每组还必须有 candidate-bound
`screenshot-manifest.json`/`video-manifest.json` 与 `GoalTestOperator` 脱敏证明。自动扫描只
覆盖文件字节/metadata，不虚构 OCR 能证明像素中无 secret。

所有状态写入使用原子替换；不得在文件中保存 secret、未脱敏绝对用户路径、完整 write arguments 或可重放 approval grant。上下文压缩或执行恢复时，先读取 `goal-state.json`、当前 Week review/handoff 和对应 Gate results，不从聊天记忆猜测状态。

## 10. Checkpoint 完成条件

Week84：

- listener ownership 确定性回归、最小修复和完整 requalification 通过。
- listener delta 不超过冻结 `+40`。
- 形成 exact clean Week85 baseline。

Week85–89：

- 两条 lane 分别通过自身 required Gates。
- 不越过目录 ownership 与共享冻结区。
- Week86/89 用户视觉 Gate 已闭合。
- Week89 两条 lane 均为 `ReadyForIntegration`。

Week90：

- 两条 lane 分步本地合并。
- shared freeze audit、cross-surface full matrix 与 exact candidate identity 通过。

Week91：

- security、resource、package、accessibility、Narrator/人工 UX 和 cleanup 全部闭合。
- 只修复 Gate 暴露的 P0/P1，不新增范围。

Week92：

- 独立 clean dual build 与完整最终 matrix 通过。
- Week92 用户视觉验收通过。
- controlled-write（若 required）满足 A7。
- 开放 P0/P1 为 `0`。
- 输出唯一最终决定 `Refactor Accepted`。
- 输出 schema-valid `artifacts/week92-refactor-acceptance/week92-acceptance-handoff.json`，其中 `decision=GoalComplete`、`finalDecision=RefactorAccepted`。

若 Week92 只能达到 `Candidate Ready`，总 Goal 仍为 `Active`；不得把接近完成写成 `Complete`。

## 11. 明确禁止

整个 Goal 禁止：

- 通过提高 listener/memory 阈值、减少 workload、增加 retries、forced GC、Renderer reload 或延长 idle 让结果变绿。
- 删除或缩短 durable history、approval/recovery/long-session 场景。
- 输出 secret 或把 secret 写入 evidence。
- 以最终成功覆盖首次失败。
- 以视觉改版为由扩大 Renderer authority。
- 以 CLI 文件拆分为由改变命令名、参数、默认值、help、text/JSON/NDJSON、stdout/stderr 或 exit code。
- 未经新授权 push、tag、release、publish、deploy 或扩大外部写入。

本契约若与某周计划存在歧义，采用更严格的安全、证据、Gate 与授权边界；若严格解释会实质改变产品范围，则创建 boundary request 找用户确认。
