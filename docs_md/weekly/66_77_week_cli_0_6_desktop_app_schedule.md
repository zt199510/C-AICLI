# C-AICLI 0.6.0 Desktop App 12 周开发排期

更新时间：2026-07-18

状态：Week 66-75 已完成；Week 75 recovery/long-session unpacked 与 packaged Gate 已通过

详细产品与技术边界统一以 `docs_md/spec/desktop_app_development_framework_0_6_0.md` 为准。本排期负责把该框架拆成 Week 66-77 的依赖顺序、周末验收和发布 Gate，不扩展产品范围。

## 排期结论

0.6.0 是从 CLI-only Runtime 进入跨进程 Desktop 产品的首次版本，包含新的 .NET Application 层、AppHost、versioned protocol、Electron Main/Preload、React Renderer、桌面 E2E 和独立发布链。当前仓库没有这些工程或契约，因此不按最低 8 周压缩执行，采用 **12 周**排期：

- Week 66-69 先锁定共享服务、持久化和协议，阻止 Desktop 形成第二套执行或安全逻辑。
- Week 70-72 先交付安全的 Desktop shell 和只读复核，再接入 composer 与受控上下文。
- Week 73-74 才开放 write-capable turn、审批、终端和 Gerber/TIFF 人工决策闭环。
- Week 75-77 专门处理故障恢复、安全、可访问性、性能、打包和发布验收。

任何 Critical Gate 未通过时，后续周次顺延；不得通过删减安全、恢复、测试或发布证据来维持名义周数。

## 起点评估

### 已有可复用基础

- `0.5.0` 已记录为 Accepted，版本基线为 `.NET 9` / `win-x64`。
- 已有 agent loop、workspace guard、approval、shell policy、secret redaction、trace、session、job、queue、report 和 artifact store。
- 已有 Skills、Experts、Automations、stdio MCP v1、Project Pack v1 与 Gerber/TIFF 受控工作流。
- 已有默认不依赖模型、网络或真实外部工具的 build/test/smoke 习惯。
- 2026-07-15 本次评估验证 Release build 为 `0 warnings / 0 errors`。
- 受控单处理器 full suite 为 `1261 passed / 0 failed / 0 skipped`。

### 当前差距

| 范围 | 当前状态 | 0.6.0 差距 |
|---|---|---|
| .NET solution | 5 个项目：Core、Cli、AgentFramework、ProjectPacks、Tests | 缺少 `CSharpAiCli.Application` 与 `CSharpAiCli.AppHost` |
| CLI/application 边界 | `CliCommandFactory` 集中承载大量命令编排 | Desktop 不得复制该逻辑；需先抽出可复用 use case |
| Desktop 工程 | 不存在 `apps/desktop`、`package.json` 或 lockfile | Electron/Main/Preload/Renderer 和测试链均需建立 |
| Protocol | 不存在 desktop schema、framing、handshake 或 contract generation | 需建立 versioned desktop-v1 和 feature negotiation |
| Thread/timeline | 已有 session/job/queue/report/artifact 记录 | 缺少 thread/turn 投影、稳定 sequence、恢复与去重契约 |
| Node 环境 | Node `22.13.0`、npm `11.7.0` 可用 | 依赖版本、许可、lockfile 和 Electron 兼容性尚未 Gate |
| .NET SDK | 用户目录存在锁定的 `9.0.308` | 当前默认 PATH 先解析到系统 `10.0.301`；构建入口需显式稳定 |
| 测试确定性 | 受控 full suite 通过 | 标准并发运行复现 2 个 wall-clock/timeout 失败，不能把该波动带入 Desktop E2E |
| Source baseline | 0.5.0 验收文档和 0.6.0 框架存在未提交改动 | 开始产品实现前需冻结可追踪 source revision |

### 评估判断

1. 最大架构风险不是 Electron UI，而是 CLI 与 Desktop 共享 Application service 的边界。
2. 最大安全风险是 Renderer/Preload 权限扩大、协议输入被信任，以及终端或 approval 形成旁路。
3. 最大一致性风险是 C# records、JSON schema 与 TypeScript types 三份契约漂移。
4. 最大运行时风险是 AppHost 断开、取消、背压、崩溃恢复和孤儿进程清理。
5. 最大交付风险是同时引入 .NET、Node、Electron、Playwright 和双发布链后，测试噪声掩盖真实回归。

因此实施顺序固定为：**共享 use case -> 持久化投影 -> 协议 -> 安全 shell -> 只读复核 -> write path -> 垂直体验 -> hardening -> release**。

## 产品目标

用户在 Windows Desktop App 中完成以下本地闭环：

```text
open workspace
-> create or resume thread
-> compose task with controlled context
-> stream plan/model/tool/command timeline
-> resolve approval or cancel
-> review changes/report/artifact/terminal/preview
-> complete, safely resume, or restart
-> reopen App and recover auditable results
```

默认验收使用 fake model/runtime 和 repository fixture，不依赖模型凭据、网络、真实 Gerbv/ImageMagick 或远程服务。

## 范围护栏

- Desktop write path 必须复用 .NET Application service 和既有安全策略，不解析 CLI 文本。
- AppHost 是 workspace、approval、tool policy、redaction 和持久化的最终权威。
- Renderer 无 Node、任意文件、任意进程、API key 或 store 直接访问能力。
- Thread 只投影现有 session/job/queue/report/artifact truth，不建立第二套任务事实源。
- 0.6.0 只允许单 thread 内一个 write-capable turn，不实现并发 write worker。
- Changes 默认只读；不实现 revert/discard。
- 不实现 Web UI、远程控制、团队/RBAC、后台 scheduler、插件市场、IDE 或办公套件。

## 阶段划分

| 阶段 | 周次 | 主题 | 阶段出口 |
|---|---:|---|---|
| Phase 24 | 66 | Baseline 与 Critical Spike | source、依赖、协议生成、进程桥和打包方向全部形成可验证结论 |
| Phase 25 | 67-69 | Application、Persistence 与 AppHost Protocol | CLI/AppHost 共享 use case；read-only protocol 可恢复、可限界、可清理 |
| Phase 26 | 70-72 | Secure Desktop Shell 与只读复核 | 安全 Electron shell、完整只读 timeline/panels 和受控 composer 可用 |
| Phase 27 | 73 | Task Chat Write Path | fake runtime 下 turn、stream、approval、cancel 和 final summary 闭环通过 |
| Phase 28 | 74 | Terminal 与垂直制品体验 | 用户终端、artifact preview、Gerber/TIFF accept/reject 边界通过 |
| Phase 29 | 75-76 | Recovery、Security 与 Release Candidate | 故障矩阵、E2E、可访问性、性能、安全和候选包收口 |
| Phase 30 | 77 | 0.6.0 Release Acceptance | 双构建、manifest/checksum、packaged smoke、文档与最终决定完成 |

## 逐周计划

### Week 66：基线评估、架构决定与技术 Spike

详细计划：`66_week_desktop_foundation_assessment_and_spike.plan.md`

主要交付：

- 冻结可追踪的 0.5.0 Accepted source 和 0.6.0 spec/schedule 起点。
- 建立 Application/AppHost/Protocol/Desktop 的责任矩阵和依赖方向检查。
- 验证 Electron + React + TypeScript + Vite 在当前 Node/Windows 环境的兼容版本、许可和 lockfile 策略。
- Spike `Content-Length` framing、handshake、schema/type generation、Electron 启停 AppHost 与 self-contained resource packaging。
- 修复或隔离标准 full suite 的 MCP/Local API wall-clock 不确定性，保留受控运行作交叉证据。
- 测量并冻结冷启动、idle memory、长 timeline、protocol payload 和 package size 的 0.6.0 预算方法。

周末 Gate：Critical Spike 必须全部得到 Passed/Blocked 结论；任何 Blocked 项不得被 fake UI 绕过。

### Week 67：Application Service Foundation

详细计划：`67_week_application_service_foundation.plan.md`

执行结论：Passed；证据见 `67_week_review.md`。

主要交付：

- 新建 `CSharpAiCli.Application`，只依赖 Core/ProjectPacks，不依赖 CLI、Electron 或文本 renderer。
- 建立 workspace open/snapshot、catalog query、changes/report/artifact query 的结构化 use case。
- 从现有 CLI 路径抽出首个 read-only vertical slice，并保留 CLI 文本与 exit-code 兼容性。
- 定义错误分类、bounded result、redaction 和 cancellation contract。
- 加入 architecture tests，禁止 AppHost/Desktop 反向引用 CLI command factory。

周末 Gate：同一 use case 经 CLI 与 Application contract 得到等价的安全结论和数据身份；没有复制 store 或 policy。

### Week 68：Thread、Turn、Timeline 与持久化投影

详细计划：`68_week_thread_turn_timeline_persistence.plan.md`

执行回顾：`68_week_review.md`（Week 68 Gate Passed）

主要交付：

- 定义 versioned thread/turn/timeline records、状态机、sequence 与 revision。
- 建立 atomic replace、expected revision、bounded schema、corrupt record diagnostics。
- 将 session/job/queue/report/artifact 指针投影为 UI-safe timeline，不复制原始 truth。
- 完成 create/list/get/rename/archive/delete 的持久化规则；delete 保留显式确认和竞态检查。
- 建立 fake timeline projector 和旧 CLI session 的显式、可回退迁移策略。

周末 Gate：重启、重复事件、乱序读取、corrupt/truncated record 和 revision conflict tests 全部通过。

### Week 69：AppHost 与 Desktop Protocol v1

详细计划：`69_week_apphost_desktop_protocol_v1.plan.md`

执行结论：Passed；实现、回归、package/process 与 clean-source release acceptance Gate 全部通过，证据见 `69_week_review.md`。

主要交付：

- 扩展现有 `CSharpAiCli.AppHost` skeleton，完成单父进程 stdio、framed JSON-RPC、initialize handshake 和 capability negotiation。
- 扩展 reviewed schema，继续作为 C#/TypeScript contract generation 与 runtime validation 的唯一来源。
- 接入 workspace、thread create/list/get/rename/archive/delete，以及 catalog/changes/report/artifact read-only 方法与有序 notification。
- 对 frame、payload、并发 request、输出频率、stderr、timeout、cancel 和 backpressure 设定边界。
- 覆盖 malformed JSON、unknown method/version、oversize、partial frame、disconnect、child cleanup 和 secret redaction。

周末 Gate：无 Electron 参与的 AppHost contract/E2E 测试通过；stdout 只包含协议帧，退出后无子进程或临时目录残留。

### Week 70：Secure Electron Shell

执行结论：Passed；安全 Shell、回归、packaged smoke、视觉与 package/process Gate 全部通过，证据见 `70_week_review.md`。

详细计划：`70_week_secure_electron_shell.plan.md`

主要交付：

- 建立 `apps/desktop` 的 Main、Preload、Renderer、generated contracts、tests 与 lockfile。
- Electron Main 负责 AppHost 启停、handshake、退出诊断和窗口生命周期。
- Preload 只暴露冻结、typed、runtime-validated allowlist API。
- 强制 `contextIsolation=true`、`nodeIntegration=false`、Renderer sandbox、严格 CSP、navigation/new-window/external-link policy。
- 建立 workspace picker、错误/重启状态和最小三栏响应式 layout，不接入 write-capable turn。

周末 Gate：安全配置自动测试通过；Renderer 无法获得 Node、通用 IPC、任意文件或进程能力；AppHost crash 有明确 UI 状态。

### Week 71：只读 Thread、Timeline 与复核面板

详细计划：`71_week_read_only_thread_timeline_review.plan.md`

执行结论：Passed；strict notification/resync、只读 Thread/Timeline/复核面板、unpacked/packaged E2E、四视口、package/process 与 CLI/.NET Gate 全部通过，证据见 `71_week_review.md`。

主要交付：

- 完成 workspace/thread 导航、create/resume/rename/archive 与状态筛选。
- 渲染 user/assistant/plan/tool/command/approval/changes/report/artifact/warning/completed timeline item。
- 完成 Changes、Reports、Artifacts 和 Gerber/TIFF managed preview 的只读面板。
- 支持 timeline 分页/虚拟化、折叠、大输出、长路径和重新连接按 sequence 去重。
- 用 Playwright 验证 1280x720、1440x900、1920x1080 和窄窗口无重叠、截断或空白主视图。

周末 Gate：packaged/unpacked fake read-only smoke 可打开既有记录并复核结果；Renderer reload 后状态一致。

### Week 72：Composer、Catalog 与受控上下文

详细计划：`72_week_composer_catalog_controlled_context.plan.md`

执行结论：Passed；证据见 `72_week_review.md`。

主要交付：

- 完成多行 composer、draft、发送状态、模型和 approval policy 摘要。
- 接入 `@file`、`@folder`、Skill、Expert 与 Automation catalog。
- 所有路径、附件、size/type、workspace ownership 由 AppHost 重新验证。
- 运行中发送的输入明确排队为下一 turn，不修改当前 tool arguments。
- 覆盖键盘操作、焦点恢复、screen reader label、IME、长文本和错误恢复。

周末 Gate：Renderer 不直接读取附件；越界、reparse、超限、stale catalog 和 workspace 切换竞态均被稳定拒绝。

### Week 73：Task Chat、Streaming、Approval 与 Cancel

详细计划：`73_week_task_chat_streaming_approval_cancel.plan.md`

执行结论：核心实现 Passed；证据见 `73_week_review.md`。.NET 全量、Desktop Vitest、unpacked E2E 与 Desktop build/security 已通过；`package:dir` 在 Electron packager 下载阶段因 `ECONNRESET` 中断，packaged E2E 待网络恢复后重试。

主要交付：

- Application service 接入 turn start、agent execution、streaming timeline 和 final summary。
- 显示 plan/model/tool/command/verification/changes 状态，工具输出默认 bounded/collapsed。
- approval request/resolve 绑定当前 turn、request、policy 和 revision；deny/expired/stale 路径明确。
- 支持 cancel/canceling/canceled、可恢复任务的安全 resume/restart，以及单 write-turn 约束。
- 保持 CLI 对同一执行、安全与报告路径的回归测试。

周末 Gate：fake runtime packaged E2E 完成 create -> run -> approval -> deny/allow -> cancel/resume -> final summary；无 approval bypass 或重复执行。

### Week 74：Terminal、Artifacts 与 Gerber/TIFF 人工闭环

详细计划：`74_week_terminal_artifacts_gerber_tiff_human_loop.plan.md`

状态：Passed（review：`74_week_review.md`）

主要交付：

- 接入由用户显式操作的 xterm.js/PowerShell 会话，AppHost 管理 cwd、resize、bounded output、exit 和 process tree。
- 用户 terminal 与 agent command timeline 使用不同身份和审计标识。
- 接入 artifact metadata/preview/export/delete 的现有 ownership 与安全服务。
- 完成 managed Gerber/TIFF preview、verification evidence、accept/reject 与 safe resume/restart 投影。
- 缺少 Gerbv/ImageMagick 时只显示 doctor 诊断，不下载、不探测未知工具、不扩大 correctness 声明。

周末 Gate：terminal 不形成 tool/approval bypass；preview 不能自动 accept；stale evidence、missing artifact、tamper 和 cleanup tests 通过。

### Week 75：故障恢复、长会话与 Desktop E2E

详细计划：`75_week_failure_recovery_long_sessions_desktop_e2e.plan.md`

状态：Passed（review：`75_week_review.md`）

执行回顾：`75_week_review.md`

主要交付：

- 覆盖 AppHost crash、Renderer reload、Main exit、protocol disconnect、partial notification、corrupt thread 和 missing artifact。
- 验证 interrupted/failed 恢复，不把未知 running 猜成成功，不自动重放 write step。
- 覆盖 sequence 去重、重连补偿、backpressure、长 timeline、大 diff、大 terminal output 和资源释放。
- 建立完整 fake runtime packaged E2E 矩阵和进程/临时目录终态检查。
- 修复 flaky E2E；重试只能用于采样诊断，不能作为通过依据。

周末 Gate：成功、deny、cancel、crash、restart、corrupt-state 六条闭环均有稳定自动化证据，终态无 AppHost/shell/MCP/terminal/external-tool orphan。

### Week 76：Security、Accessibility、Performance 与 Release Candidate

主要交付：

- 完成 Renderer/Main/Preload/AppHost trust-boundary review 和依赖/第三方 notice 审计。
- 覆盖 CSP、navigation、external link、drag/drop、clipboard path、protocol fuzz、secret 与 crash diagnostics。
- 完成键盘导航、焦点、screen reader、contrast、reduced motion 和窄窗口 QA。
- 记录冷启动、idle memory、长 timeline memory、protocol throughput 和 package size，对 Week 66 预算做回归判断。
- 生成首个 Desktop release candidate、payload inventory、AppHost SHA256、manifest、checksums 和默认 packaged smoke。

周末 Gate：高风险安全问题为 0；性能超预算必须有修复或明确 Blocked 决定；candidate smoke 不依赖凭据、网络或真实外部工具。

### Week 77：0.6.0 Release Acceptance

主要交付：

- 将 CLI 与 Desktop 版本、protocol、source revision、runtime 和 capability 文档收口到 `0.6.0`。
- 运行 .NET/Node 全量 build、test、lint、typecheck、Desktop E2E 和 CLI regression smoke。
- 从干净 source revision 做两次 Desktop package，比较 inventory、AppHost hash、installer/archive size 与 SHA256。
- 验证 packaged initialize、workspace、thread、fake run、approval、changes、report、artifact、preview、restart 和 cleanup。
- 更新 changelog、installation、configuration、security、known limitations、capability status 与 final acceptance。
- 明确记录 Accepted、Preview、Deferred 和未执行的 real-model/real-tool opt-in evidence。

周末 Gate：满足框架说明中的 8 项关键验收 Gate 才可标记 Accepted；否则发布决定为 Blocked，不降低 Gate。

## Critical Gates

| Gate | 最晚周次 | 必须满足 |
|---|---:|---|
| G0 Source Baseline | 66 | 0.5.0 Accepted revision 和 0.6.0 spec/schedule 可追踪，工作区实现起点干净 |
| G1 Technical Feasibility | 66 | Electron/Node/.NET、framing、contract generation、spawn/cleanup 和 packaging spike 通过 |
| G2 Shared Runtime Contract | 69 | Application/AppHost 不复制 CLI 安全逻辑；protocol 有 version/bounds/corrupt/disconnect tests |
| G3 Read-only Desktop | 71 | 安全 shell 和已有 thread/result 复核通过，Renderer 权限边界通过 |
| G4 Write Path | 73 | turn/approval/cancel/resume 单写闭环通过，CLI regression 通过 |
| G5 Vertical Review | 74 | terminal 与 Gerber/TIFF preview/human decision 不扩大权限或 correctness |
| G6 Release Candidate | 76 | security/accessibility/performance/E2E/package evidence 收口 |
| G7 Final Acceptance | 77 | 双构建、默认 packaged smoke、checksums、docs 和 cleanup 全部通过 |

## 公共验证基线

Week 66 起每周至少运行：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

标准 full suite 必须记录真实结果。若出现 wall-clock flake，可增加 build-server cleanup 与单处理器复核，但不得把首次失败从周回顾中删除。

Week 69 起增加 AppHost contract tests。Week 70 起至少运行：

```powershell
npm ci
npm run typecheck
npm run lint
npm test
npm run build
```

Week 71 起增加 Desktop Playwright smoke；Week 73 起增加完整 fake runtime write-path E2E；Week 76-77 增加 packaged smoke、process cleanup、payload inventory、notices、checksums 和双构建复现。

具体 npm script 名称由 Week 66 spike 冻结到 `apps/desktop/package.json`，不得在实现中维护多套等价入口。

## 每周 Definition of Done

每周结束必须创建对应 `NN_week_review.md`，至少记录：

- 实际完成范围、未完成项和与本排期的偏差。
- build/test/lint/typecheck/E2E/smoke 的命令、通过数、失败数和耗时。
- schema/protocol/source revision、package 或 process cleanup 证据。
- 安全、corrupt-state、redaction、recovery 和资源边界结论。
- Accepted/Preview/Deferred 变化及剩余风险。
- 下一周可依赖的稳定 contract；未通过 Gate 时明确 Blocked，不提前进入下游 write path。

## 主要风险与控制

| 风险 | 等级 | 控制 |
|---|---|---|
| CLI 逻辑无法安全抽成 Application use case | P0 | Week 67 先做 read-only vertical slice 和 parity tests；禁止 Desktop 引用 CLI |
| C#/schema/TypeScript contract 漂移 | P0 | Week 66 选定单一 reviewed source；Week 69 加生成一致性 CI |
| Renderer/Main/AppHost 信任边界错误 | P0 | 安全默认值自动测试、runtime validation、AppHost 重验所有路径与决策 |
| approval/cancel/restart 导致重复写入 | P0 | request/revision identity、单写约束、interrupted 状态和 adversarial E2E |
| terminal 成为 agent 或 approval 旁路 | P0 | 用户终端独立身份；AppHost 生命周期/审计；禁止 agent 注入 terminal input |
| 旧 store 与新 thread 投影形成双 truth | P0 | thread 仅持有稳定 pointer；atomic/revision/corrupt tests；不复制 artifact/job 内容 |
| 既有 timeout flake 扩散到 E2E | P1 | Week 66 先收口确定性；标准与受控结果都记录；禁止用盲目重试宣称通过 |
| Electron package 体积或内存无边界增长 | P1 | Week 66 建预算方法，Week 70 起连续采样，Week 76 作为 release Gate |
| UI 范围膨胀为 IDE/任务中心 | P1 | 只实现 spec 信息架构；非目标直接 Deferred，不挤占 hardening 周 |

## 发布定义

0.6.0 只有同时满足以下条件才允许 Accepted：

1. Renderer 不具备 Node、任意文件或任意进程权限。
2. Desktop write path 全部复用 Application services 与既有安全策略。
3. Thread/timeline/approval/artifact contract 有版本、边界和 corrupt-state tests。
4. Fake runtime packaged smoke 完成完整聊天、审批和结果复核闭环。
5. deny、cancel、crash、restart、dedup 和 orphan cleanup 通过。
6. Changes、Reports、Artifacts 和 Gerber/TIFF Preview 不扩大 ownership 或 correctness 声明。
7. CLI 0.5.0 核心行为、full suite 和 release smoke 不回归。
8. Desktop package 记录 source revision、依赖/runtime/protocol 版本、payload inventory、notices 和 checksums。

真实模型与真实 Gerbv/ImageMagick 继续是显式 opt-in 环境证据。未运行不应伪装成通过，已运行也不得扩展为通用 CAM/EDA 正确性证明。
