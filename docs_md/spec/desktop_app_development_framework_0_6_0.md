# C-AICLI 0.6.0 Desktop App 开发框架说明

更新时间：2026-07-15

状态：方向已确认，进入排期与实现准备

## 决策摘要

0.6.0 建设一个 Windows-first、本地单用户、可审计的 C-AICLI Desktop App。产品交互采用 **80% Codex App + 20% WorkBuddy**：

- Codex App 作为核心交互模型，主界面围绕 workspace、task thread、对话时间线、审批和结果复核展开。
- WorkBuddy 作为任务管理和能力组织补充，提供多任务历史、Skills、Experts、Automation 和可交付结果入口。
- 不复制 Codex 的全部产品能力，也不复制 WorkBuddy 的办公套件、IM 控制或团队平台形态。
- 不把现有 CLI 包一层界面；CLI 与 Desktop 必须复用同一套 .NET application services、安全策略和持久化边界。

技术框架确定为：

- Desktop shell：Electron。
- Renderer：React + TypeScript。
- Bundler：Vite。
- Runtime：现有 C#/.NET 9 Runtime，加新的 Application 与 AppHost 边界。
- Transport：Electron Main 与 .NET AppHost 之间使用带版本握手的 framed JSON-RPC over stdio。
- Packaging：Windows `win-x64` Desktop package，包含 Electron UI 与 self-contained .NET AppHost。

## 产品定位

0.6.0 Desktop App 是 C-AICLI Runtime 的本地产品界面，不是一个新的通用聊天产品。

它解决以下问题：

- 将 task、plan、model、tool、approval、changes、test、report 和 artifact 组织在同一条可复核时间线中。
- 降低 CLI 参数、状态文件和多条复核命令给用户带来的认知成本。
- 为 Gerber/TIFF 等垂直工作流提供可视化预览、验证状态和人工决策入口。
- 为后续 Skills、Experts、Automation 和多任务管理建立稳定的交互框架。

它不改变以下产品原则：

- Windows-first、C#/.NET-first、私有模型友好。
- 本地执行优先，默认不提供远程控制。
- 高风险动作必须保留审批、workspace guard、dirty-workspace、shell policy 和 secret redaction。
- CLI 继续是可脚本化、可诊断、可发布的正式产品入口。
- App 不得创建第二套 agent loop、job store、artifact store 或安全策略。

## 0.6.0 成功标准

一个用户应当能够在 Desktop App 中完成以下闭环：

1. 打开一个本地 workspace。
2. 创建或恢复一个 task thread。
3. 通过聊天输入任务，并使用 `@file`、`@folder`、Skill、Expert 或 Automation 上下文。
4. 实时查看 plan、模型输出、工具调用、命令结果和状态变化。
5. 在对话时间线中查看并处理审批请求。
6. 查看 Changes、Terminal、Reports、Artifacts 和 Gerber/TIFF Preview。
7. 取消当前 turn，或在安全重校验后恢复可恢复的任务。
8. 从最终总结复核 changed files、commands、tests、artifacts、warnings 和 remaining risks。
9. 关闭并重新打开 App 后，从持久化记录恢复线程和可交付结果。

默认验收不得依赖真实模型、真实 Gerbv/ImageMagick、外部网络或远程服务。真实模型与真实工具验证继续显式 opt-in。

## 交互策略

### 80% Codex App

主体验是一个面向工程任务的对话线程：

- 每个 thread 绑定一个 workspace，不允许 turn 在执行中静默切换 workspace。
- 每条用户输入创建一个 turn；一个 thread 可以包含多个连续 turn。
- plan、tool call、approval、command、verification、changes 和 final summary 都是结构化 timeline item。
- 工具输出默认折叠，失败、风险、审批和需要用户判断的内容优先展示。
- Thread 可以新建、恢复、重命名、归档；删除需要显式确认并遵守持久化策略。
- 对话不是自由聊天记录，而是任务、证据和交付结果的统一视图。

### 20% WorkBuddy

WorkBuddy 思路只进入任务管理和能力组织：

- 左侧展示 workspace、进行中任务、历史任务和状态摘要。
- 输入区提供 Skills、Experts 和 Automation 的统一选择入口。
- Timeline 和右侧面板展示报告、制品和可交付结果。
- 0.6.0 不建设独立的 Automation 控制台或团队任务中心。
- 当任务量、手动 Automation 使用量和历史检索需求达到明确阈值后，再规划独立 WorkBuddy 式任务中心。

## 主界面信息架构

| 区域 | 主要内容 | 0.6.0 行为 |
|---|---|---|
| 左侧导航 | Workspaces、任务列表、历史、状态 | 新建/切换 workspace，创建/恢复/重命名/归档 thread，按状态筛选 |
| 中央时间线 | 用户消息、plan、model、tool、approval、command、verification、summary | 流式追加，结构化折叠，失败与审批突出显示 |
| 输入区 | Prompt、附件、`@` mentions、Skill、Expert、Automation、模型与权限摘要 | 支持文件/目录和能力选择；显示当前 workspace 与 approval policy |
| 右侧检查区 | Changes、Terminal、Reports、Artifacts、Gerber/TIFF Preview | 按 thread/turn 上下文切换，支持只读检查和明确的用户操作 |
| 顶部任务栏 | Workspace、thread 标题、运行状态、取消、更多操作 | 不放置营销内容；状态与操作保持紧凑稳定 |

窗口变窄时，右侧检查区变为可切换抽屉；左侧导航可折叠。中央时间线和输入区始终是第一优先级。

## Timeline 模型

Renderer 不直接拼接 CLI 文本。AppHost 输出稳定的结构化 timeline contract。

0.6.0 必须覆盖以下 item 类型：

| Item | 用途 |
|---|---|
| `user.message` | 用户任务与附件引用摘要 |
| `assistant.message` | 流式模型文本与最终文本 |
| `plan.updated` | 计划步骤及状态 |
| `tool.started` / `tool.completed` | 工具名称、风险、输入摘要、结果摘要和耗时 |
| `command.started` / `command.completed` | 命令摘要、退出码、bounded stdout/stderr 和验证归类 |
| `approval.requested` / `approval.resolved` | 风险说明、目标、决策和决策人 |
| `changes.updated` | Changed files、diff stat 和 change set identity |
| `report.available` | Report 类型、状态和 artifact pointer |
| `artifact.available` | Artifact 类型、验证状态和安全打开方式 |
| `warning.raised` | 可恢复警告、限制和剩余风险 |
| `turn.completed` | 终态、stop reason、commands、tests、changes、artifacts 和 risks |

所有 item 必须包含稳定 id、thread id、turn id、sequence、timestamp 和 schema version。重连或恢复时按 sequence 去重，不能只依赖瞬时 UI 事件。

## 任务状态模型

Thread 与 turn 分开建模：

- Thread 状态：`idle`、`running`、`waiting-for-approval`、`failed`、`completed`、`archived`。
- Turn 状态：`queued`、`running`、`waiting-for-approval`、`canceling`、`canceled`、`failed`、`completed`。
- 一个 thread 在 0.6.0 同时最多运行一个 write-capable turn。
- 多个 thread 可以存在于左侧列表，但并发 write worker 不属于 0.6.0。
- App 重启后不得把未知 `running` 状态猜成成功；必须恢复为可诊断的 interrupted/failed 状态或进入已有安全恢复路径。

## 技术架构

```text
Electron Renderer (React + TypeScript)
        |
        | allowlisted preload API
        v
Electron Main / Preload
        |
        | framed JSON-RPC 2.0 over stdio
        v
CSharpAiCli.AppHost (.NET 9)
        |
        v
CSharpAiCli.Application
        |
        +--> CSharpAiCli.Core
        +--> CSharpAiCli.ProjectPacks
        +--> existing session/job/queue/report/artifact stores
```

### Electron Renderer

Renderer 只负责视图、局部交互状态和展示缓存：

- React + TypeScript 构建 UI。
- Zustand 保存当前选择、面板状态、未提交输入和服务端投影缓存。
- Radix UI primitives 提供可访问的菜单、弹窗、Tabs、Tooltip 和 Dialog 基础。
- Lucide 提供图标，不维护私有 SVG 图标集。
- `react-markdown` 与 Shiki 渲染 Markdown 和代码块。
- Monaco Diff Editor 渲染 changes/diff。
- xterm.js 渲染集成终端。
- Renderer 不持有 API key，不读取任意本地文件，不启动进程，不直接访问 .NET stores。

依赖版本由 0.6.0 初始化 spike 验证并锁定在 lockfile 中。本说明固定框架类别，不提前写死未经验证的具体版本。

### Electron Main 与 Preload

Main process 是桌面生命周期和最小可信桥：

- 启动、监控并终止当前 AppHost 子进程。
- 验证 AppHost handshake、protocol version、进程身份和退出状态。
- 管理窗口、系统对话框、受控 external link 和应用升级边界。
- 不实现 agent loop、approval decision、workspace guard 或业务 store。
- AppHost stdout 只允许协议帧，stderr 用于 bounded process diagnostics。

Preload 只暴露明确 allowlist 的 typed API。禁止把通用 `ipcRenderer`、文件系统、shell 或 process API 暴露给 Renderer。

### .NET Application 层

新增 `CSharpAiCli.Application`，承载 CLI 与 App 共用的 use cases：

- Workspace open/snapshot。
- Thread/turn 生命周期。
- Agent execution 与 timeline projection。
- Approval request/resolve。
- Changes、reports、artifacts 和 Project Pack 查询。
- Skill、Expert、Automation catalog 与受控执行入口。
- Cancel、resume、restart 和终态汇总。

Application 层返回结构化 contract，不写 CLI 文本，不依赖 Electron，也不接受 Renderer 提供的安全结论。

现有 CLI 应逐步调用 Application services。0.6.0 不要求一次性重写所有 CLI commands，但新 Desktop write path 不得复制 `CliCommandFactory` 的安全和执行逻辑。

### .NET AppHost

新增 `CSharpAiCli.AppHost` 作为 Desktop 专用本地进程：

- 使用 self-contained `win-x64` 发布。
- 通过 stdin/stdout 与 Electron Main 建立单父进程、单会话协议。
- 不监听 TCP 端口，不复用无认证 localhost daemon 作为控制通道。
- 对 request、response、notification、payload、并发数、消息大小和输出频率做边界限制。
- Renderer 或 Main 断开后取消可取消的工作，记录不可安全重放的 interrupted 状态，并清理子进程。
- AppHost 是 workspace、approval、tool policy、redaction 和持久化的最终权威。

## 协议框架

协议采用 JSON-RPC 2.0 语义和 `Content-Length` framing，避免多行内容破坏边界。所有方法和事件必须有独立 schema version。

### 基础方法

| Method | 目的 |
|---|---|
| `app.initialize` | 版本、capability、protocol 和安全模式握手 |
| `workspace.open` | 验证并打开本地 workspace |
| `thread.list` / `thread.get` | 加载任务列表和完整投影 |
| `thread.create` / `thread.rename` / `thread.archive` / `thread.delete` | 管理本地任务；删除要求显式确认、expected revision 和持久化策略校验 |
| `turn.start` / `turn.cancel` | 启动或取消当前 turn |
| `approval.resolve` | 处理当前有效 approval request |
| `changes.get` | 获取 bounded changes/diff projection |
| `report.get` | 获取 report metadata 与受控内容 |
| `artifact.list` / `artifact.get` | 获取 artifact metadata 与受控预览 |
| `catalog.get` | 获取 Skills、Experts、Automations 和模型选项 |
| `terminal.open` / `terminal.write` / `terminal.resize` / `terminal.close` | 管理明确的用户终端会话 |

### 基础事件

| Notification | 目的 |
|---|---|
| `thread.updated` | Thread metadata 或状态变化 |
| `turn.updated` | Turn 状态变化 |
| `timeline.appended` | 追加一个或多个有序 timeline item |
| `approval.requested` | 出现新的用户审批请求 |
| `changes.updated` | Changed files 或 diff identity 变化 |
| `terminal.output` | 用户终端的 bounded output |
| `runtime.warning` | 协议、恢复、资源或环境警告 |

协议必须支持 feature negotiation。UI 不得仅凭版本号假设某项 capability 可用。

## 持久化原则

- App 不建立第二套 job、queue、report 或 artifact truth。
- 新 thread metadata 可以有独立 schema，但只能保存 thread title、workspace identity、turn pointers、状态投影和 UI-safe metadata。
- Thread 通过稳定 id 引用现有 session、job、queue、report、trace 和 artifact 记录。
- 所有新增 store 使用 bounded schema、atomic replace、revision check 和 corrupt-record diagnostics。
- Renderer 的 local storage 仅保存主题、窗口和面板偏好，不保存 transcript、secret、approval grant 或 artifact 内容。
- 旧 CLI session 的迁移必须显式、可回退；0.6.0 不静默删除或改写历史记录。

## 输入区设计

输入区是 Codex 式 composer，并吸收 WorkBuddy 能力入口：

- 普通文本输入和多行编辑。
- `@` 统一 mentions 菜单，至少覆盖 file、folder、Skill 和 Expert。
- Automation 通过显式菜单选择并显示将执行的 target，不把 schedule preview 误写成后台任务。
- 文件和图片附件必须先由 AppHost 验证 workspace/size/type boundary，再进入 turn context。
- 模型、Expert、Skill 和 approval policy 的当前选择在发送前可见。
- 运行中再次发送的输入在 0.6.0 默认排队为下一 turn，不隐式修改正在执行的 tool arguments。

## 右侧检查区

### Changes

- 显示 git status、diff stat、changed files 和 structured diff。
- 默认只读；任何 revert/discard 操作不进入 0.6.0，除非单独完成安全设计和确认流程。
- 大 diff 按文件和大小分页，不能阻塞聊天时间线。

### Terminal

- 提供一个明确由用户操作的本地 PowerShell/配置 shell 会话。
- Agent tool execution 不得通过 terminal input 注入，也不得把用户终端当作 approval bypass。
- 用户终端与 agent command timeline 使用不同视觉与审计标识。
- AppHost 负责 cwd、process lifecycle、output bounds 和退出清理。

### Reports

- 展示 task report、pipeline/automation/CI report 和 Project Pack report。
- 优先渲染 structured fields，Markdown 作为可交付视图。
- Report path 和内容继续经过 redaction 与 ownership 检查。

### Artifacts

- 展示 artifact 类型、来源、状态、大小、hash、验证和 retention metadata。
- 打开、导出和删除必须复用现有 ownership 与安全服务。
- 0.6.0 不增加自动 retention worker 或云上传。

### Gerber/TIFF Preview

- 只显示 managed、已验证 ownership 的 preview artifact。
- Preview 不是 correctness proof，不能自动 accept。
- Accept/reject 继续要求 AppHost 重新验证当前 hard-verification evidence。
- 缺失外部 Gerbv/ImageMagick 时显示 doctor 诊断，不自动下载或执行未知工具。

## 安全边界

Electron 安全基线：

- `contextIsolation=true`。
- `nodeIntegration=false`。
- Renderer sandbox 开启。
- 严格 Content Security Policy，不执行远程脚本。
- 禁止任意 navigation、新窗口和未确认 external URL。
- Preload API 最小化、冻结并做 runtime validation。
- 文件拖放、剪贴板路径和系统对话框结果必须重新经过 AppHost workspace guard。
- 不把 localhost 视为认证边界，不通过现有 read-only daemon 增加写控制路由。

.NET 安全基线：

- AppHost 不信任 Renderer、Preload、workspace 文件、模型输出或 protocol payload。
- 所有 write、shell、MCP、external tool 和高风险动作继续进入现有 approval 与 policy path。
- Approval 绑定当前 request、tool/input/output/policy identity，不跨 turn 持久化或复用。
- Secret 不进入 timeline、Renderer store、terminal history、report 或 crash dump。
- AppHost、agent child process、MCP、Gerbv/ImageMagick 和 terminal 退出时检查残留进程与临时目录。

## 建议目录结构

```text
apps/
  desktop/
    src/
      main/
      preload/
      renderer/
      generated/
    tests/
    package.json
    vite.config.ts

src/
  CSharpAiCli.Application/
  CSharpAiCli.AppHost/
  CSharpAiCli.Core/
  CSharpAiCli.ProjectPacks/
  CSharpAiCli.Cli/
  CSharpAiCli.Tests/

protocol/
  desktop-v1/
    schemas/
    examples/
```

TypeScript contract 从 reviewed protocol schema 生成或验证，不能长期维护一套与 C# records 手工漂移的类型定义。

## 测试策略

### .NET

- Application service contract tests。
- AppHost framing、handshake、schema、cancel、disconnect 和 backpressure tests。
- Approval、workspace、redaction、store corruption 和 process cleanup regression。
- CLI 与 AppHost 对同一 use case 的行为一致性测试。

### Renderer

- Vitest + React Testing Library 覆盖 timeline、approval、composer、panel 和状态恢复。
- 长 timeline、长路径、长单词、大输出和错误状态不得破坏布局。
- 键盘导航、焦点恢复、screen reader label 和 reduced-motion 行为需要自动化覆盖。

### Desktop E2E

- Playwright 启动 packaged/unpacked Electron App。
- Fake model/runtime 完成 create thread -> run -> approval -> changes -> report -> artifact -> final summary 闭环。
- 覆盖 deny、cancel、AppHost crash、Renderer reload、corrupt thread 和 missing artifact。
- 在 1280x720、1440x900、1920x1080 以及窄窗口验证无重叠、无截断、无空白主视图。
- 终态检查无残留 AppHost、shell、MCP、terminal 或 external tool process。

## 构建与发布

0.6.0 增加独立 Desktop release 流程，不替代 CLI release：

- `npm ci` 使用锁定依赖。
- Renderer/Main/Preload 执行 typecheck、lint、unit test 和 production build。
- AppHost 执行 .NET Release build/test/publish。
- Desktop packaging 将 AppHost 作为受控 resource 打包，并记录其 SHA256。
- Release manifest 记录 Electron、Node、Chromium、AppHost、.NET、protocol 和 source revision。
- 生成第三方 notices、payload inventory、installer/archive checksum 和 packaged smoke 记录。
- 默认 packaged smoke 不需要模型凭据、网络或真实 Gerber/TIFF 工具。

Electron 会显著增加安装包与内存占用。0.6.0 接受这一成本，但必须记录冷启动时间、idle memory、长线程内存和 package size，避免无边界增长。

## 分阶段交付

### Phase 1：Application 与 Protocol Foundation

- 建立 `CSharpAiCli.Application`、`CSharpAiCli.AppHost` 和 desktop protocol v1。
- 打通 workspace、thread list/get/create 与 fake timeline。
- 建立 contract generation、framing、handshake 和 process cleanup tests。

### Phase 2：Desktop Shell 与只读复核

- 建立 Electron Main/Preload/Renderer、安全配置和基础布局。
- 完成左侧任务列表、中央 timeline、右侧 Changes/Reports/Artifacts。
- 支持已有记录恢复，不执行 write-capable turn。

### Phase 3：任务聊天闭环

- 接入 turn start、streaming、tool/command timeline、approval 和 cancel。
- Composer 接入 file/folder/Skill/Expert/Automation catalog。
- 完成 final summary 与 thread persistence。

### Phase 4：Terminal 与垂直制品体验

- 接入用户终端、artifact preview 和 Gerber/TIFF Preview。
- 接入 human accept/reject 和 safe resume/restart 投影。
- 保持 preview 与 correctness proof 的明确边界。

### Phase 5：Hardening 与 Release

- 完成 security review、E2E、long-session、crash recovery、accessibility 和 visual QA。
- 构建可复现或可解释的 Windows package、manifest、checksums 和 release docs。
- 记录 Accepted、Preview、Deferred 和 known limitations。

## 0.6.0 明确不做

- 不做浏览器 Web UI、远程访问或公网部署。
- 不做团队账号、RBAC、团队知识库或跨用户共享。
- 不做后台 scheduler、Windows Task Scheduler 注册或 unattended automation worker。
- 不做并发 write worker 或多个任务同时修改同一 workspace。
- 不做插件市场、远程 pack 分发或自动 provider routing。
- 不做 IDE、代码编辑器或完整文件管理器。
- 不做通用办公助手、IM 控制或 WorkBuddy 式办公套件。
- 不做独立 WorkBuddy 式任务中心；0.6.0 只建立未来所需的数据与导航基础。
- 不通过 Electron 绕过 CLI/.NET 已有的安全、审计和发布边界。

## 后续 WorkBuddy 式任务中心触发条件

满足以下至少三项后，再评估独立任务中心：

- 用户通常同时维护十个以上活跃 thread。
- Automation 手动运行与历史复核成为高频主路径。
- 用户需要按 Skill、Expert、Automation、状态或时间跨 workspace 检索。
- 单纯左侧任务列表无法有效呈现等待审批、失败和交付状态。
- 已经具备稳定 scheduler/worker 设计，而不是只展示无法执行的计划数据。

任务中心仍应复用同一 Thread/Application/AppHost contract，不建立第二套任务系统。

## 关键验收 Gate

0.6.0 只有满足以下条件才可标记 Accepted：

1. Renderer 不具备 Node、任意文件或任意进程权限。
2. Desktop write path 全部复用 .NET Application 与现有安全策略，不解析 CLI 文本。
3. Thread/timeline/approval/artifact contract 具备版本、边界和 corrupt-state tests。
4. Fake runtime packaged smoke 完成完整任务聊天和复核闭环。
5. Approval deny、cancel、crash、restart 和 orphan cleanup 路径通过。
6. Changes、Reports、Artifacts 和 Gerber/TIFF Preview 不扩大 ownership 与 correctness 声明。
7. CLI 0.5.0 已有核心行为和 release smoke 不回归。
8. Desktop package 记录 source revision、依赖/runtime 版本、inventory、notices 和 checksums。

## 参考边界

- Codex App：借鉴 project/task/thread、composer、timeline、approval、review、terminal 和 artifact 复核体验。
- WorkBuddy：借鉴多任务历史、Skills、Experts、Automation 和交付物组织方式。
- C-AICLI：保留 Windows/.NET、本地私有模型、可审计安全策略和 Gerber/TIFF 垂直工作流作为自身差异化。

最终产品决策：

> Codex App 作为核心交互模型，WorkBuddy 作为任务管理和能力组织的补充；Electron/React 提供桌面体验，C#/.NET AppHost 保持执行、安全和数据权威。
