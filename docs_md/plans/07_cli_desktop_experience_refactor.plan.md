# 阶段 07 - CLI 可维护化与 Desktop Chat-first 体验重构计划

更新时间：2026-07-30

状态：`Week92 Candidate / Awaiting visual acceptance`

> **2026-07-30 执行规则变更：** 原 104 Gate 重型证据流水线不再作为产品开发阻塞条件。后续按
> `docs_md/plans/07_cli_desktop_experience_refactor_lean_acceptance.md`
> 执行最小必要验证；W86、W89、W92 仍保留用户视觉验收。

> **当前进度：** Week84–91 产品实现与精简自动验收已完成，Week92 产品候选为
> `4cba061`。Desktop `verify` 与完整 .NET 回归通过；剩余阻塞项仅为用户对
> W86、W89、W92 的视觉确认。详细结果见
> `docs_md/weekly/92_week_review.md`。

## 结论

完整完成本阶段建议按 **9 个日历 Week（Week 84-92）** 承诺，并在 Week85-89 使用 Renderer 与 CLI 两条隔离工作流并行推进。

- Week 84 将 Week83 遗留的 `provider-recovery-listener-retention` P1 记为非阻塞技术债；仅当本次 UI 修改触及相关生命周期且对照显示恶化时才修复。
- Week 85-89 的 Renderer lane 重构边界、状态、设计系统、Chat-first Shell、对话投影、Composer、审批和右侧工作区。
- Week 85-89 的 CLI lane 分五步模块化 7,673 行的 `CliCommandFactory`，保持命令、输出、退出码和安全语义兼容。
- Week 90 合并两条线并完成跨面集成，Week91 做完整 hardening，Week92 做最终 acceptance。

该估算假设 CLI 与 Renderer 使用独立 worktree、固定共享冻结区和独立负责人/Agent，并由一个集成负责人统一收口。若由一个实现者完全串行执行两个 lane，应按约 **14 Week** 估算；不应把 CLI 的 5 周工作压缩成两周机械搬迁。

只完成视觉壳约需 4-5 周，但不满足本阶段的“做到”：它不会解决 Renderer controller 耦合、事件投影、CLI 巨型装配文件、资源回归和发布证据问题。

## Goal 执行契约

Week84–92 作为一个总 Goal 执行，逐周使用不可跳过的 checkpoint；权威 runbook
为 `docs_md/weekly/84_92_week_goal_execution_contract.md`。机器控制和验收统一使用：

- `docs_md/weekly/84_92_week_goal_control.schema.json`
- `docs_md/weekly/84_92_week_gate_result.schema.json`
- `docs_md/weekly/84_92_week_handoff.schema.json`
- `docs_md/weekly/84_92_week_gate_requirements.json`
- `tools/validate-week84-92-goal-evidence.py`
- `tools/week84_92_goal_integrity.py`
- `tools/week84_92_evidence_anchor.py`
- `tools/week84_92_trusted_executor.py`
- `tools/week84_92_provider_turn_harness.py`
- `tools/week84_92_commands/` 下五个 W84-G0 adapters
- `tools/week84_92_provider_scenarios/` 下四个 frozen scenarios
- `tools/test_validate_week84_92_goal_evidence.py`
- `tools/test_week84_92_goal_integrity.py`
- `tools/test_week84_92_gate_requirements.py`
- `tools/test_week84_92_evidence_anchor.py`
- `tools/test_week84_92_trusted_executor.py`

以上文件连同 W84-G0 command-control bundle、计划与三个 scoped `.gitattributes` 组成固定 44-file bootstrap control plane。三个 schema 均为 JSON Schema Draft 2020-12，`schemaVersion: 1.0.0`；tracked Gate requirements manifest 冻结 104 个 Gate 各自的命令、断言、证据和最低用例数，语义验证器、append-only integrity helper 与 immutable evidence-anchor helper 负责 schema 无法表达的跨文件计数、原始字节 hash、provider ledger、首败/用户决定账本、controlled-write、ignored evidence 防改写和最终闭合对账。每个周计划
必须拥有明确 Entry Gate、逐 Gate evidence、exact candidate identity 和 schema-valid
handoff；仅测试绿灯而无 lineage、首败、命令、hash 或 cleanup 不能推进。

每个产品命令采用 adapter observation 与 independent sealed verifier 两阶段验真。adapter 只能执行并
留下 stdout/stderr/exit/checkout 观察，不能自行输出受信 `Passed` marker 或贡献测试计数；只有同一
strict-prior command-control 中 exact unittest test/oracle 对 adapter report 做 raw-hash 绑定后，
verifier report 才能贡献 Gate counts。Gate evidence 必须同时引用两类 report。

用户已确认契约的 1–8 项授权：允许仓库内实现/验证、ignored evidence、本地
`codex/week84-92-refactor`、隔离 worktree、checkpoint commit 和 local merge；允许
恢复锁定依赖和清理本 Goal 的 owned process/精确临时目录；严禁 push、tag、上传、
部署或发布。真实 provider 的 Week84–92 累计预算为 **120 turns**，但 `.env.local`
读取与真实调用只授权给 `W84-G6/G7/G8` 和 `W92-G7`；turn 必须按
attempt/outcome 逐笔记账、隔离 secret，达到上限前停止并提出 boundary request。真实 secret
只进入 frozen gateway；provider Gate 还必须启动 exact packaged candidate，并在首次真实调用前
闭合 OS-isolation 或用户明确接受的 cooperative-candidate trust boundary，不能用合成 HTTP
替代产品 E2E 或把未受控直连算作已证明的硬上限。
Week85–91（包括 W91 provider-facing smoke）一律使用 credential-free deterministic
fake/mock，provider scope 为空且新增 turn 为 0。

产品人工验收不是自动化的替代项：

- W86：Chat-first Shell 方向与三视口视觉确认。
- W89：完整 context workspace 与交互层级视觉确认。
- W92：exact final candidate 最终视觉确认。

W91 Windows Narrator 与人工 UX 由 Goal-owned test operator 对 exact candidate 执行并保存证据，在 W92 一并交给用户复核，不增加第四个用户暂停点。

OpenCowork 参考冻结为 `AIDotNet/OpenCowork@b4afc37d0f77a4bce8da7c16deb2bddc94bd5dfb`；仅借鉴其 Chat-first 信息架构和交互层级，C-AICLI 品牌、协议、AppHost
authority、安全与资源阈值保持本计划定义；任何超出契约的依赖、shared freeze、
controlled write、协议或权限变化必须单独找用户确认。

## 背景与起点

当前可复用基础：

- `.NET Core -> Application -> CLI/AppHost` 的依赖方向已建立。
- Desktop 使用 Electron Main、Preload、React Renderer 和 typed `desktop-v1` RPC。
- Renderer 不拥有 Node、任意文件、任意进程或 API key 权限。
- Thread、Turn、Timeline、Composer、Approval、Changes、Terminal、Reports、Artifacts 和 Gerber/TIFF 已有结构化 contract 与测试。
- Desktop build、credential-free matrix、packaged smoke、Week77 memory profiles 和 provider read-only 已在 Week83 候选上通过。

当前主要问题：

1. `apps/desktop/src/renderer/use-desktop-controller.ts` 同时承担 runtime、workspace、thread、projection/resync、review、composer 和 approval orchestration。
2. `App.tsx` 固定把左侧、中央、右侧、Terminal、TaskControls 和 Composer 同时铺开，中央对话不是绝对主视图。
3. Timeline 以 protocol item 为主要视觉单位，用户消息、助手消息、工具过程、审批和最终结果没有形成清晰层级。
4. `styles.css` 同时承担 token、布局和全部 feature 样式，缺少稳定 UI primitive。
5. `CliCommandFactory.cs` 约 7,673 行、26 个顶层命令，命令定义、参数验证、依赖装配、执行委托和输出渲染高度集中。
6. Week83 仍有一个开放 P1：packaged recovery 的 JS listener delta `+193`，超过冻结上限 `+40`。在该问题关闭前不得开始大规模 Renderer 改版。

## 产品目标

把 Desktop 恢复为以本地工程任务为核心的 Chat-first 产品：

```text
workspace/thread navigation
-> focused conversation
-> controlled context composer
-> inline tool/approval/result flow
-> optional changes/terminal/report/artifact inspection
-> auditable completion or recovery
```

目标体验借鉴 OpenCowork 的布局层级和组件化方式，但不复制它的多 Agent、团队、插件市场、远程控制、办公套件或 Renderer 工具执行模型。

## 架构目标

```text
CLI command modules --------------------+
                                         v
                                   Application use cases -> Core/Runtime/Safety
                                         ^
Renderer feature UI -> DesktopGateway -> Preload/Main -> AppHost
```

必须保持：

- CLI 与 Desktop 共享 Application/Core truth，不复制业务或安全策略。
- AppHost 是 workspace、approval、tool policy、redaction、persistence 和 recovery 的最终权威。
- `desktop-v1` 在 Week84-92 默认保持 exact `39 invoke + 2 event`；任何变化必须单独评审和生成契约。
- Renderer store 只保存局部 UI 状态、请求投影和可丢弃缓存。
- CLI 重构只改变组合方式，不改变用户可观察行为。

## 目标 UI 信息架构

### 左侧导航

- Workspace 标识与切换入口。
- 新建任务、搜索和 thread 状态筛选。
- 进行中、等待审批、失败、已完成和归档 thread。
- 窄窗口下变为 drawer，不挤压中央 composer。

### 中央对话区

- Header 只展示当前 thread、workspace、runtime/turn 状态和必要操作。
- 消息流以 `UserMessage`、`AssistantMessage`、`ToolGroup`、`ApprovalCard`、`ResultSummary` 为主要展示块。
- 原始 timeline item 保留为可展开审计详情，不丢失 sequence、revision、source pointer 或 redaction 状态。
- Composer 固定为中央底部的紧凑任务输入面，不与 Terminal 永久抢占高度。

### 右侧上下文工作区

- Changes、Terminal、Reports、Artifacts、Preview 使用统一 panel shell。
- 默认可关闭；宽屏可调整宽度，窄屏使用 drawer/overlay。
- Panel 内容绑定当前 workspace/thread/turn，不能形成第二套 selection truth。

## Renderer 目标目录

```text
apps/desktop/src/renderer/
  app/
    AppShell.tsx
    DesktopProviders.tsx
  features/
    runtime/
    workspace/
    threads/
    conversation/
    composer/
    approval/
    review/
    terminal/
  shared/
    desktop-gateway/
    ui/
    theme/
    testing/
  main.tsx
```

不强制在 Week85 引入 Zustand。Week83 的开放问题与 listener 生命周期直接相关，因此先使用可测试的 feature reducers/hooks 和单一 subscription owner；只有确定性资源对照证明第三方 store 不增加 retention 时，才可单独评审引入。

## CLI 目标目录

```text
src/CSharpAiCli.Cli/
  Bootstrap/
    CliCompositionRoot.cs
    CliServices.cs
  Commands/
    Abstractions/
    Diagnostics/
    Configuration/
    WorkspaceReview/
    JobsQueueAutomation/
    AgentExecution/
    Sessions/
    McpToolsSkills/
    ProjectPacksArtifacts/
  Rendering/
```

命令模块只负责：

- System.CommandLine 参数、选项和 validators。
- 将 ParseResult 映射为 Application/Core 请求。
- 调用共享 service/factory。
- 选择既有 text/JSON renderer 并返回既有 exit code。

命令模块不得重新实现 approval、workspace guard、shell policy、secret redaction、job/session/report/artifact 或 agent loop。

## 阶段划分

| 阶段 | Week | 主题 | 阶段出口 |
|---|---:|---|---|
| Phase 31 | 84 | Listener P1 与重构基线 | Week83 开放 P1 关闭；形成 exact clean baseline |
| Phase 32 | 85-89 | Renderer 与 CLI 并行重构 | Desktop 体验闭环；CLI 五步模块化兼容通过 |
| Phase 33 | 90 | 跨 Lane 集成 | shared freeze、architecture、full suite 与 merge 冲突收口 |
| Phase 34 | 91 | Hardening | 安全、资源、E2E、package、a11y 和兼容性全矩阵通过 |
| Phase 35 | 92 | Final Acceptance | exact candidate、证据、文档和最终决定收口 |

## 逐周摘要

| Week | Renderer lane | CLI lane / 集成 |
|---:|---|---|
| 84 | Listener retention、deterministic red/green、clean baseline | CLI 冻结；只采集 inventory |
| 85 | DesktopGateway、feature state、single subscription owner | Composition context；诊断、配置与低风险查询模块 |
| 86 | Design tokens、Chat-first Shell、responsive drawers | Jobs、CI、review、tools、run、session、chat 模块 |
| 87 | ConversationBlock projector、message-first timeline | Exec、skills、queue 与 delegation compatibility |
| 88 | Composer、inline approval、cancel/recovery | Project packs、artifacts 与领域 helper 拆分 |
| 89 | Changes/Terminal/Reports/Artifacts/Preview context workspace | Automation、pipeline、recursive root 与 CLI full compatibility |
| 90 | 两条 lane 合并后的 Desktop/CLI architecture 与 shared freeze 收口 | Cross-lane integration、全量编译和冲突修复 |
| 91 | Security、a11y、E2E、resource、package hardening | CLI smoke/release/package、命令 inventory 和兼容性复核 |
| 92 | Exact clean candidate 与最终体验验收 | Evidence、docs、final decision |

详细周计划位于 `docs_md/weekly/84_92_week_cli_desktop_experience_refactor_schedule.md` 及对应 `84_week_...` 到 `92_week_...` 文件。

## 公共验收基线

每周必须至少执行适用子集，Week92 必须全部执行：

```powershell
python -B -X utf8 -m unittest tools/test_validate_week84_92_goal_evidence.py tools/test_week84_92_goal_integrity.py tools/test_week84_92_gate_requirements.py tools/test_week84_92_evidence_anchor.py tools/test_week84_92_trusted_executor.py -v
python -B -X utf8 tools/validate-week84-92-goal-evidence.py --repo-root .

dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build

Set-Location apps\desktop
npm run check:contracts
npm run check:notices
npm run check:accessibility
npm run typecheck
npm run lint
npm test
npm run build
```

从 Week86 起增加视觉和 responsive fixtures；从 Week87 起重跑 long-session/virtualization；从 Week89 起重跑 terminal/review E2E；Week92 重跑 unpacked、packaged、protocol、Week77 profiles、Week80 controls 和 Week84 listener controls。

## 成功标准

### Desktop

1. 1440x900 下中央 conversation 是主视觉，Terminal 和 Review 不再永久压缩 composer。
2. 1024x768 下右侧自动变为 drawer；800px 宽下左右面板均不遮挡核心任务流。
3. Tool/command 过程默认折叠，审批、错误和结果保持显著且可键盘操作。
4. 所有原始 timeline 审计信息仍可访问；sequence/revision/redaction 不被 UI 聚合丢失。
5. Renderer 无 Node、通用 IPC、任意文件或任意进程权限。
6. `desktop-v1` contract、approval identity、turn recovery 和 durable history 语义不变。
7. Week77 private-bytes Gate、Week80 controls 和 Week84 listener Gate 不回归。

### CLI

1. 26 个顶层命令及现有子命令全部保留。
2. `--help` 层级、参数/validator、stdout/stderr、JSON schema 和 exit code 保持兼容。
3. `CliCommandFactory` 不再承载全部 feature 实现，仅保留兼容 facade 或被 composition root 取代。
4. 每个命令模块拥有定向测试；共享 service 和 renderer 不在模块间复制。
5. CLI、AppHost 和 Desktop 继续使用相同 Application/Core use case 与安全策略。

## 非目标

- 不实现 OpenCowork 的多 Agent mode、Agent Teams、插件市场、浏览器工具、SSH、Cron、Draw 或桌面宠物。
- 不将文件、shell 或 tool execution 移入 Renderer。
- 不升级为 `desktop-v2`，不重写 ThreadStore 或 durable timeline。
- 不改变 provider、模型路由、MCP transport 或 Project Pack 业务能力。
- 不以 Tailwind、Zustand、Radix 或任意 UI 库替代架构决定；依赖必须由收益和资源证据驱动。
- 不在本阶段创建 tag、上传制品、发布 Release 或擅自把 0.6.0 改写为正式 Accepted。

## 风险与保护边界

- **基线污染**：Week84 未通过时，Week85 保持 Blocked。
- **视觉重写掩盖语义回归**：先建立 feature seam 和 presentation projector，再切换 DOM。
- **listener/memory 再增长**：每个新 subscription、observer、virtualizer 和 portal 都需要 owner/cleanup tests。
- **协议被 UI 绑架**：UI adapter 消化 presentation 需求，默认不扩展 protocol。
- **CLI 大文件机械搬迁出错**：按命令域小步迁移，每步冻结 help/output/exit-code snapshots。
- **双线合并冲突**：Renderer 与 CLI 可并行，但 Application、generated contracts 和 release scripts 设为共享冻结区。
- **范围膨胀**：OpenCowork 仅作为交互参考；新能力进入独立后续计划。

## 最终决定词汇

Week92 只允许以下结论：

- `Refactor Accepted`：本阶段全部自动化和人工适用 Gate 通过。
- `Candidate Ready`：产品与自动化 Gate 通过，但正式 Preview/Release 所需人工或 provider Gate 未获授权/未完成。
- `Blocked`：存在开放 P0/P1、资源回归、协议/安全回归或关键 Gate 未运行。

总 Goal 只能在最终决定为 `Refactor Accepted`、所有适用自动化/人工 Gate Passed、
W86/W89/W92 视觉确认与 W91 Narrator Passed、provider 累计不超过 120 turns、
P0/P1 和 cleanup 为 0 时标记 `complete`。`Candidate Ready` 仅表示等待未完成的
人工或授权 Gate，Goal 保持 active；不得用它提前结束 Week84–92。

`Refactor Accepted` 不自动等于 `0.6.0 Released`。正式版本状态仍由独立 release acceptance 决定。
