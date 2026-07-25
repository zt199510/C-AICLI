# Week 78 执行计划：0.6.0 Preview 状态收口与真实项目试用

状态：Completed / Blocked（2026-07-24 收尾；Desktop 生产 AppHost 缺少真实模型 runtime）

创建日期：2026-07-23

所属阶段：Week 66-77 Desktop 0.6.0 主周期后的 Preview 收口与 dogfooding

起点提交：`e8089fad9b1cc1106babe7c33c940f5a3fb61124`

执行分支：`week-02-cli-commands-doctor-config`

## Goal

在不扩大 0.6.0 已冻结产品边界、不改变 Week 77 `Blocked` 发布决定的前提下，完成发布状态文档一致性收口，并使用受控、可回滚的真实 C#/.NET 工程副本验证 Desktop/CLI 在真实模型、真实本地 MCP 和真实工程任务中的可用性、安全性、可恢复性与资源终态。

Week 78 的目标不是通过增加功能掩盖 Week 77 的 Narrator 缺口，也不是重新宣称 0.6.0 `Accepted`。本周只允许形成以下两种结论：

- `Preview Ready`：状态文档一致、自动化基线未回归、规定的真实项目场景完成、没有 P0/P1 安全或数据完整性问题，可以继续内部 Preview/dogfooding。
- `Blocked`：任一硬 Gate 未关闭；保留首败和证据，不推广 Preview，不创建 tag，不发布制品。

如果后续希望把 0.6.0 Desktop 标记为正式 `Accepted`，必须另行获得用户授权，重新开启 Windows Narrator 人工 Gate，并按当时的 clean revision 和 candidate identity 执行发布验收；Week 78 不自动执行该路径。

## 创建基线

- Week 66-75 的实现 Gate 已完成，从 Application/AppHost、ThreadStore、`desktop-v1`、安全 Electron Shell，推进到 composer、turn、approval、terminal、artifact、Gerber/TIFF control plane 和故障恢复。
- Week 77 最终 source-bound candidate revision：`c74f93f45b0cae4a06cd70ee6dbca1b333a5973e`。
- Week 77 closeout HEAD：`e8089fad9b1cc1106babe7c33c940f5a3fb61124`，计划创建前工作区 clean。
- Week 77 自动化最终证据：`.NET diagnostic 1400/1400`、Desktop `23 files / 102 tests`、unpacked E2E `9/9`、packaged E2E `8/8`、accessibility `2/2`、packaged smoke `8/8`、protocol `3/3`。
- Week 77 performance：packaged baseline `5/5`、long-session `5/5`，所有 process/temp delta 为 0；package 相对 Week 66 `+6.13%`，AppHost `+0.45%`。
- Candidate A/B 各自 smoke `8/8`；payload inventory `78/78`，archive SHA-256 均为 `1A258F9412B47D3199B2BFA2072AB37DC97412ABC38A2A6DE2AAB1E58746EF3B`。
- Week 77 正式 comparison 为 `Failed`，唯一最终原因是 `Narrator evidence is not a Passed manual run.`；Narrator evidence 为 `Skipped`、`manualNarratorRun=false`。
- 当前 Accepted 基线仍为 CLI `0.5.0`；0.6.0 Desktop 必须保持 `Blocked / Preview`，不得因本周试用被描述为正式 release。
- Reviewed Desktop bridge 继续冻结为 exact `39 invoke + 2 event`；默认不修改 `desktop-v1` method/event、权限、capability 或 limits。

## 执行原则

- 本文件只创建执行计划；创建计划时不运行真实模型、不读取或写入凭据、不连接用户 MCP、不修改真实业务仓库、不生成或发布 RC。
- 后续真实模型、真实 MCP、真实外部工具均为显式 opt-in。缺少用户授权、凭据、模型名、server identity 或授权 fixture 时，必须记录 `Skipped/Unproven`，不得猜测或代填。
- `OPENAI_API_KEY`、MCP secret、token、完整用户 prompt、原始模型响应、绝对用户路径和未脱敏 diagnostics 不得进入 Git、JSON evidence、截图、review 或命令输出。
- 所有 write-capable 试用必须在 disposable clone、临时 worktree 或用户明确指定的可回滚副本中执行；不得把当前权威仓库或用户业务仓库直接交给不受控写路径。
- 每个场景执行前保存 commit、status、文件 inventory 和预期允许修改范围；执行后比较实际 diff、进程、临时目录和外部副作用。
- 首次失败必须保留。允许为诊断收集 trace/screenshot/stderr，但“重跑到绿”不能改写首次结果。
- 不按进程名全局 kill。只管理本次场景记录的 test-owned 根 PID、descendant PID、workspace 和 temp root。
- 默认 credential-free 自动化 Gate 必须独立于真实模型/MCP Gate通过；外部服务不稳定不能掩盖本地回归。
- 不创建或推送 tag，不 push，不上传 artifact，不发布 GitHub Release；这些动作需要用户单独授权。

## 明确不做

- 不新增模型 provider、provider routing、MCP transport、工具 schema 或任意 generic IPC。
- 不扩展为浏览器 Web UI、远程控制、团队权限、后台 scheduler、并发 write worker、插件市场、IDE 或办公套件。
- 不把 user terminal 变成 agent terminal，不绕过 approval，不允许模型直接获得 shell/process/file authority。
- 不改变 Gerber/TIFF correctness 声明；metadata、preview、fake tool、文件存在和 exit 0 仍不等于制造或图像正确。
- 不为了通过试用而放宽 redaction、workspace guard、approval、timeout、memory、cleanup 或 retry Gate。
- 不重写 Week 66-77 的历史 review；只在需要处增加当前状态说明或指向最终 Week 77/78 结论。
- 不重新打开 Narrator 环节，除非用户在 Week 78 执行期间明确改变决定并授权单独的 release-acceptance 路径。

## Phase 0：发布状态与文档一致性收口

先对当前文档做状态审计，建立单一、无歧义的发布叙述：

- `docs_md/release/final_acceptance_0.6.0.md` 保持权威结论：`Blocked`。
- `docs_md/release/final_acceptance.md` 明确 0.5.0 是最后 Accepted 版本，0.6.0 已结束验收并为 Blocked，而不是 “acceptance pending”。
- 更新 `CHANGELOG.md`、`capability_status.md`、`installation.md`、`quickstart.md`、`known_limitations.md`、`troubleshooting.md` 中仍写作 “Week 77 pending” 或可能暗示正式发布的内容。
- 更新 `product_positioning_and_roadmap.md`：Week 66-77 周期完成，0.6.0 Desktop 为 Blocked/Preview；后续是内部试用，不是 Accepted release。
- 更新 `66_77_week_cli_0_6_desktop_app_schedule.md` 的 Week 77 状态，并以 post-cycle extension 方式链接本计划，不改写原 12 周历史范围。
- 保留 Week 73 当周 package pending 的历史事实，同时明确该 carryover 已在 Week 74 使用受校验 Electron cache 关闭。
- 全库搜索 `acceptance pending`、`Week 77 正在执行`、`Week 77 Ready`、`release acceptance in progress` 和类似陈述，逐项分类为需要更新或应保留的历史记录。

文档硬规则：

- “实现完成”“自动化通过”“Preview Ready”“Accepted release”是四个不同结论，不得混用。
- CLI 0.6 metadata、Desktop candidate 和正式 Accepted 版本必须分别表达。
- Candidate hash、source revision 和 closeout HEAD 不得混为同一个 identity。
- Narrator、real model、real MCP、real Gerber/TIFF 保持各自独立的 `Passed/Failed/Skipped/Unproven` 状态。

Phase 0 Gate：所有当前状态型发布文档一致指向 Week 77 `Blocked`；`rg` 审计没有未解释的过期 current-state 声明；`git diff --check` 通过。

## Phase 1：真实项目试用协议与数据安全

在运行模型前创建 versioned pilot manifest，建议写入 ignored 目录：

```text
artifacts/week78-preview-pilot/
  pilot-manifest.json
  credential-free-baseline.json
  real-model-readonly.json
  real-model-write.json
  real-mcp.json
  recovery.json
  resource-summary.json
  pilot-summary.json
```

`pilot-manifest.json` 至少记录：

- schema version、执行日期、source revision、source dirty 状态、CLI/Desktop/AppHost identity。
- 操作者的非敏感显示名或稳定代号；不得记录账户、邮箱、token 或 credential 来源。
- 每个项目的脱敏 project id、语言/框架类别、基线 commit、是否 disposable、允许修改的相对路径。
- 使用的模型标识、MCP server 稳定名称和版本；不得记录 endpoint query、header、token 或本机绝对路径。
- 场景预期、批准模式、允许工具、时间预算、输出边界、通过条件和停止条件。
- process/temp/workspace cleanup 的采样方式。

真实项目输入要求：

- 至少 2 个独立、可回滚的 C#/.NET 工程副本；推荐 1 个小型库和 1 个含测试的应用。
- 第 3 个场景可以复用其中一个项目的全新副本执行 crash/restart/recovery，不得复用已经被模型修改的 workspace。
- 项目不得包含真实客户数据、生产密钥、私有证书、未脱敏日志或不可公开源文件，除非用户明确授权相应本地处理范围。
- 如项目包含 Git submodule、reparse point、超大二进制、生成目录或私有 package source，必须在 manifest 中先声明并确定排除/允许规则。

Phase 1 Gate：所有 workspace 都可恢复或丢弃；允许修改范围和停止条件可机器检查；evidence schema 不包含 secret/path/raw-prompt 字段。

## Phase 2：Credential-free 基线复验

任何真实模型/MCP 试用前，从 clean Week 78 source revision 顺序运行本地确定性 Gate。显式使用仓库锁定 SDK：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build-server shutdown
dotnet build src\CSharpAiCli.sln -c Release --no-restore
dotnet test src\CSharpAiCli.sln -c Release --no-build

Set-Location apps\desktop
npm ci
npm audit --audit-level=high
npm run verify
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
npm run measure:accessibility
npm run measure:smoke
npm run measure:protocol
```

如果 Phase 0 仅修改文档，仍需运行与文档/脚本相关的定向测试、release docs audit 和 `git diff --check`。在进入最终 Week 78 decision 前，至少再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -ReleaseAcceptance
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
```

基线判定：

- .NET full suite 不允许失败或 skipped；测试数量不得低于 Week 77 的 1400，除非 review 对删除/合并逐项解释。
- Desktop verify 不得低于 `23 files / 102 tests`；contracts/notices/accessibility/typecheck/lint/security/build 全部通过。
- unpacked `9/9`、packaged `8/8`、accessibility `2/2`、packaged smoke `8/8`。
- `npm audit` high/critical 为 0，package denylist 为 0。
- 所有 test-owned process/temp delta 为 0。
- 本周默认不要求重做双 RC reproducibility；如 source 或 package runtime 发生变化，则旧 Candidate A/B identity 立即失效，必须在 review 中明确，不得继续引用为当前 package。

Phase 2 Gate：credential-free 基线完整通过，首次失败如实写入 Week 78 review；未通过时停止真实模型 write scenario。

## Phase 3：真实模型只读与真实 MCP 探针

### 3.1 CLI real-model 只读 smoke

仅在用户显式提供并授权当前进程使用 `OPENAI_API_KEY`、`OPENAI_MODEL` 后执行：

```powershell
$env:CAICLI_REAL_MODEL_SMOKE = "1"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-SmokeTests.ps1
Remove-Item Env:CAICLI_REAL_MODEL_SMOKE
```

必须验证：

- 使用 `--approval never` 的 bounded read-only task。
- 模型只调用预期的 `workspace.read_text` 等只读工具。
- fixture 内容与 Git tree 字节不变。
- 输出包含结构化成功与 tool-call evidence，但 evidence 只保存脱敏摘要，不保存完整响应。
- 退出后无 CLI、MCP、child process 或 temp root 残留。

### 3.2 Desktop real-model 只读任务

- 在 disposable project A 创建新 thread，使用真实模型执行“定位一个已知测试失败或解释指定模块”的只读任务。
- 工具 allowlist 仅包含读取、搜索和测试诊断；禁止 write、shell 或 artifact export。
- 预先写明应识别的文件/符号/失败原因，验收模型是否给出可复核引用，而不是按文案相似度判断。
- Renderer timeline、Reports、Changes 必须与权威 Application/AppHost 结果一致，Changes 保持 clean。
- 模型失败、超时或 provider error 必须呈现为安全终态；不得自动重放。

### 3.3 真实本地 MCP

- 使用用户明确指定、版本固定、可本地关闭的 stdio MCP server；不得在本周新增 transport 或绕过既有 config。
- 先运行 static diagnostics/list，确认 server command、capability 和工具 schema 可审计。
- 只调用一个无副作用、bounded、可验证的读取工具；预期输出提前写入 pilot manifest。
- 验证 unknown tool、oversize、timeout、server exit 和 malformed response 至少一个失败路径。
- server command、环境变量和 stderr evidence 必须脱敏；测试结束后确认 server process tree 为 0。

Phase 3 Gate：real-model CLI、Desktop read-only 与真实 MCP 各自有明确 `Passed/Failed/Skipped`；任何越权工具调用、未批准写入、secret/path 泄漏或 orphan 均为 P0，并立即停止后续 write scenario。

## Phase 4：真实模型受控写任务

仅在 Phase 2 和 Phase 3 安全 Gate 通过后，在 disposable project B 执行一个小而真实的 C#/.NET 修改：

- 任务必须有确定性验收，例如修复一个预先构造的单测失败、增加一个 bounded validation、或完成一个不超过 3 个源文件的局部重构。
- 执行前记录 baseline test、允许修改的相对路径、禁止区域、最大文件数、最大 diff 行数、允许命令和成功标准。
- approval 模式必须要求人工批准 write/shell；审批 UI 必须展示安全摘要，禁止把原始 secret、完整 command environment 或绝对路径暴露给 Renderer。
- 模型不得修改 solution/package/runtime/protocol/release/security 文件，除非该场景专门授权。
- 完成后必须由独立命令运行目标测试，并检查完整 Git diff、未跟踪文件、文件 mode、换行和生成物。
- 结果必须形成 Changes、task report 和必要的 managed artifact；不得用自然语言“已完成”替代测试证据。
- 如果模型第一次提交的修改不通过，保留首次失败。允许同一 thread 内在用户可见的反馈后修正，但必须记录 attempt 数、额外 approval 和最终 diff。
- 场景结束后还原或丢弃 disposable workspace；不得把 pilot 修改合并回主仓库。

Phase 4 Gate：

- 只修改 manifest 允许的文件。
- 每个 write/shell 动作都有正确 approval。
- 目标测试与预声明 acceptance 通过。
- timeline/report/diff 与磁盘事实一致。
- 无自动 replay、重复写入、悬空 approval、orphan process 或未释放 temp root。

## Phase 5：Crash、Restart 与恢复试用

在新的 disposable project 副本执行：

1. 启动真实模型任务并在已产生 durable timeline 后终止 owned AppHost。
2. 确认 UI 显示未知/中断状态，不推断 completed，不自动 restart 或 replay。
3. 显式 restart AppHost、重新打开 workspace，并从权威 ThreadStore 重建状态。
4. 如 turn 满足 restart 条件，要求用户确认后创建新 attempt；旧 attempt 保持 `failed/interrupted`。
5. 确认旧 approval 清理，新 turn/request/approval identity 不复用。
6. 完成或取消新 attempt，检查 Changes、Reports、process/temp 终态。

不得为了制造故障而终止未被本场景记录为 owned 的进程。真实 provider 请求是否已经在远端计费不能由本地状态推断；review 只记录本地 request/turn identity 与可观察终态。

Phase 5 Gate：未知状态 fail closed、无自动重放、旧/新 identity 分离、authoritative resync 正确、cleanup delta 为 0。

## Phase 6：资源、隐私与可用性总结

每个真实场景记录：

- 启动到 runtime ready、首个模型响应、首个 tool call、任务终态耗时。
- model/tool/MCP call 数量和安全化错误类别；不保存 raw request/response。
- approval 次数、deny/cancel/restart 次数、人工纠正次数。
- changed files、diff 行数、测试结果、报告/artifact 数量。
- Main/Renderer/GPU/Utility/AppHost/MCP/CLI PID 与关闭后 delta。
- workspace/temp root 是否可释放。
- 是否出现绝对路径、secret、原始 diagnostics、未授权网络或未授权文件访问。
- 操作者对任务可理解性、审批可判断性、结果可复核性的简短结论。

至少对一个完整真实任务运行 long-session/idle 观测。该观测不能替代 Week 77 固定 workload Gate，但用于发现真实 provider/MCP 对内存和生命周期的额外影响。若 working set/private bytes 持续超过 Week 77 15% 观察线或出现单调增长，记录为 P1 并进入修复，不以 Electron warm-up 直接豁免。

Phase 6 Gate：所有 evidence 脱敏、identity 可追踪、process/temp delta 为 0；没有无法解释的 package、workspace 或用户配置变化。

## Phase 7：缺陷处置与最终 Preview Decision

缺陷优先级：

- P0：越权写入、approval 绕过、secret/path 泄漏、错误 workspace、重复执行、数据损坏、进程无法控制。立即停止 Week 78，结论 `Blocked`。
- P1：真实任务不可恢复、ThreadStore/identity 不一致、稳定 orphan、持续 memory 超限、测试/报告与磁盘事实不一致。修复后从新 clean revision 重跑受影响矩阵；未关闭则 `Blocked`。
- P2：可理解性、空状态、诊断或人工步骤摩擦，但不影响安全与数据正确性。可形成明确 backlog，不阻止内部 Preview。
- P3：视觉、文案或便利性建议。只记录，不在稳定性周扩大范围。

任何产品/source 修复都必须：

1. 保存首败 evidence。
2. 加入 deterministic regression test。
3. 形成新的 clean revision。
4. 重新运行受影响的 .NET/Desktop/E2E/security/package/real-pilot 场景。
5. 更新 evidence identity，废弃旧 revision 的当前资格。

Week 78 最终只允许：

- `Preview Ready`：Phase 0-6 硬 Gate 全部关闭，无 P0/P1；真实模型和 MCP evidence 明确绑定授权环境；Week 77 正式 release 仍保持 `Blocked`。
- `Blocked`：任一硬 Gate 未关闭，或者 real-model/MCP 因缺少授权而未能完成本计划的核心试用目标。

不得在 Week 78 review 中使用 `Accepted`、`Released` 或 `Production Ready` 描述 Desktop。

## Week 78 Critical Gates

- [x] W78-G0 当前状态型 release/roadmap/schedule 文档一致记录 0.6.0 `Blocked / Preview`，0.5.0 为最后 Accepted。
- [x] W78-G1 clean-source credential-free .NET/Desktop/CLI/E2E/security/package 回归通过，首败完整保留。
- [x] W78-G2 pilot workspace 全部 disposable/可回滚，允许修改范围、工具和停止条件预先冻结。
- [ ] W78-G3 CLI 真实模型只读通过；Desktop 生产 AppHost 固定使用 fake runtime，真实模型路径失败，本周结论 Blocked。
- [x] W78-G4 一个真实本地 MCP 读取调用和越界/未知工具失败路径通过，server process/temp cleanup 为 0。
- [ ] W78-G5 因 W78-G3 未通过，真实模型受控写任务按 Gate 未启动。
- [ ] W78-G6 因缺少生产 Desktop 真实模型 runtime，真实模型 crash/restart 场景按 Gate 未启动；credential-free 回归不冒充真实试用。
- [x] W78-G7 evidence 不含 secret、绝对用户路径、raw prompt/response 或未脱敏 diagnostics。
- [ ] W78-G8 已执行场景 resource/process/temp/workspace delta 均为 0，但 Desktop 真实模型 runtime 缺口未关闭。
- [x] `git diff --check` 通过；创建 `docs_md/weekly/78_week_review.md` 并记录最终 `Blocked`。

## 推荐执行顺序

1. 提交本计划，确认起点工作区 clean。
2. 完成 Phase 0 文档状态审计与修订。
3. 定义 pilot manifest、项目副本和 evidence schema。
4. 在 clean revision 上运行 credential-free 基线。
5. 请求用户单独授权真实模型、模型名、MCP server 与试用项目范围。
6. 先运行 CLI/Desktop real-model 只读和 MCP 探针。
7. 安全 Gate 通过后执行单个受控写任务。
8. 在全新 workspace 执行 crash/restart/recovery。
9. 汇总资源、隐私、可用性和缺陷。
10. 修复 P0/P1 时形成新 revision 并重跑受影响矩阵。
11. 创建 `78_week_review.md`，决定 `Preview Ready` 或 `Blocked`。

## 任务交付物

- `docs_md/weekly/78_week_0_6_preview_real_project_pilot.plan.md`
- 统一后的当前状态型 release、roadmap 和 schedule 文档。
- ignored `artifacts/week78-preview-pilot/` evidence 集合。
- credential-free baseline、real-model、real-MCP、write-task、recovery 与 resource summary。
- 首次失败、修复、revision/identity、命令、计数、耗时和 cleanup 记录。
- `docs_md/weekly/78_week_review.md`
- 明确的下一阶段建议：继续内部 Preview、重新开启 Narrator release Gate，或保持 Blocked 并先修复真实试用问题。

## 完成定义

Week 78 完成不等于 0.6.0 正式发布。完成只表示：

- 当前发布状态叙述一致；
- 0.6.0 Preview 在真实工程、真实模型和真实 MCP 的授权环境中完成了可审计试用；
- 安全、审批、恢复、资源和结果复核边界得到真实场景验证；
- 最终 `Preview Ready` 或 `Blocked` 结论真实、可引用、没有用自动化或 fake evidence 外推未执行能力。
