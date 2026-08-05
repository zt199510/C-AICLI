# C-AICLI Desktop 真实模型与三面板闭环验收方案

日期：`2026-08-04`

状态：`Ready for manual execution`

候选 revision：`128f2cab5bce7d053386a96a0a32c516a63d60b3`

适用范围：当前 `main` 上的真实模型连续对话、Composer 恢复、Stop、持久化会话，以及 Codex 式左侧会话栏、顶部摘要、底部详情和右侧工具导航。

## 1. 验收目标

本轮只回答一个问题：当前候选是否已经形成可供用户稳定试用的真实 Desktop 对话闭环。

通过本方案只能记录为：

- `Manual Product Gate Passed`：当前 revision 的真实桌面产品闭环通过；
- `Blocked`：存在未关闭的 P0/P1，或必需步骤无法完成；
- `NotRun`：尚未获得真实 provider 使用授权，或执行环境未准备好。

本方案不重新开启 `0.6.0` 正式发布决策，不得把结果写成 `Accepted Release`、`Released` 或 `Production Ready`。正式发布仍以 release 文档和独立人工 Gate 为准。

## 2. 基线与现有证据

当前候选已经有以下工程证据：

- `docs_md/spec/right_ui_refactor_acceptance_report.md` 记录三面板布局、三档视口、键盘焦点、生产安全、组件测试、`npm run verify` 和视觉 E2E 通过；
- `docs_md/weekly/84_92_renderer_chat_ui_execution_acceptance.md` 记录连续消息投影、Composer 修复、execution ownership、流式 Markdown 和首段内容延迟修复；
- `docs_md/spec/codex_style_continuous_chat_retry_acceptance_report.md` 记录 provider retry、取消、attempt 隔离与副作用防重放的工程验收，但真实模型连续对话仍待人工确认。

若执行时 `git rev-parse HEAD` 仍等于本方案候选 revision，可引用上述自动化证据，不重复运行已经通过且未受影响的全量矩阵。若产品源码、协议、依赖或测试发生变化，必须先更新候选 revision，并按第 5 节重新运行受影响验证。

## 3. 范围边界

### 3.1 本轮必验

1. Release AppHost 与 production Renderer 能使用授权的真实模型配置启动。
2. 同一会话中至少完成两轮连续对话。
3. 用户消息立即出现，权威消息到达后不重复。
4. AI 回复在同一个可见消息块内经历连接、思考、生成和完成。
5. Markdown 正确渲染，短回复也能观察到流式更新。
6. 第一轮完成后 Composer 可立即输入并发送第二轮，不停留在 `Sending prompt` 或 `Ready to run`。
7. Stop 能取消当前请求或生成，保留已显示内容，并恢复 Composer。
8. 完成会话在重载或重启后仍可读取，且不被误报为“执行已中断”。
9. 1440×900、1024×768、800×900 下的左右栏、顶部摘要、底部详情和 Composer 无关键重叠。
10. 面板按钮、Escape、抽屉互斥与焦点回归可用。
11. UI、日志、截图和验收记录不泄露 API Key、请求头或原始敏感诊断。

### 3.2 自动证据覆盖，不要求对真实 provider 人为注入故障

- 初始请求加最多 5 次追加重试；
- HTTP 408、429、5xx、timeout、transport 与流式中断分类；
- cancel during request 和 cancel during backoff；
- 不同 attempt 内容不拼接；
- 已完成工具、命令、审批或文件写入不被重放；
- retry exhausted 后的安全错误复制与副作用 fail-closed。

不得为了制造 retry 场景而关闭整机网络、修改真实凭据、提交副作用任务或破坏工作区。若已有安全、确定性的 fake/fault harness，可将 retry UI 作为补充验收，但不替代真实模型连续对话。

### 3.3 明确不验

- Git commit、push、Pull Request 或分支写操作；
- 尚未接入协议的 Branch、Compare branches、Sub-agents 真实数据；
- 真实 Gerber/TIFF、远程 MCP 或其他外部写入；
- 插件注入、面板排序、跨窗口同步和可拖动面板高度；
- `0.6.0` 正式 release、打包发布、tag、上传或部署。

## 4. 授权与安全前置

执行真实模型步骤前，由用户明确确认本轮授权。授权范围固定为：

- 只读取仓库根目录 `.env.local` 中执行所需的 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY`；
- 只把上述值注入本次 Desktop/AppHost 进程，不写入报告、截图、命令输出或 Git 跟踪文件；
- 本轮真实 provider 调用最多 6 个 Turn；默认计划使用 3 个 Turn；
- 只在用户指定的 disposable/read-only 测试工作区内操作；
- 不授权文件写入、Shell、Git 写操作、MCP 写操作、发布或外部消息。

授权未确认时，状态保持 `NotRun`。只允许检查 `.env.local` 是否存在，不得读取其内容。

## 5. 执行流程

任何单条自动验证命令和同一批次的总墙钟时间均不得超过 5 分钟。超时即停止并记录，不延长等待或无修改重跑。

### Gate 0：候选身份与工作树

记录但不输出秘密：

```powershell
git rev-parse HEAD
git status --short
git log -3 --oneline
```

通过条件：

- HEAD 与候选 revision 相同，或方案已先更新为新的明确 revision；
- `.env.local` 未被 Git 跟踪；
- 产品源码、协议、依赖和构建配置没有未解释的 dirty change；
- 已知验收文档可以未跟踪或有文档修改，但必须在记录中逐项列明。

### Gate 1：自动证据复核

若 HEAD 与本方案候选完全相同，复核第 2 节已提交证据即可，状态记录为 `Passed by exact-revision evidence`。

若候选发生变化，至少运行：

```powershell
dotnet test .\src\CSharpAiCli.Tests\CSharpAiCli.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProviderRequestRetryTests|FullyQualifiedName~DesktopProtocolTests|FullyQualifiedName~DesktopRpcRuntimeTests"

Set-Location .\apps\desktop
npm run verify
npm run test:e2e
```

只在修改 Main、Preload、生命周期、焦点或安全边界时补跑对应 hardening E2E。未修改的 release/package、资源和全 solution 矩阵不重复运行。

### Gate 2：真实程序启动

1. 构建 Release AppHost；若依赖已存在，优先使用 `--no-restore`。
2. 构建 production Desktop bundle。
3. 获得第 4 节授权后，通过既有安全启动方式只把三个模型配置项注入当前 Desktop/AppHost 进程。
4. 启动时不设置 `VITE_DEV_SERVER_URL`，避免把开发服务器当作 production Renderer 证据。
5. 打开用户指定的测试工作区。

通过条件：

- Electron 窗口和 Release AppHost 均启动并保持响应；
- UI 显示 runtime ready，不出现 crash/restart 循环；
- 控制台、日志与截图中没有配置值或 API Key；
- 没有启动第二个重复 AppHost 或遗留不可控进程。

### Gate 3：两轮连续真实对话

第一轮建议提示词：

> 请用 Markdown 回答：包含一个二级标题、三项有序列表和一个代码块，简要说明本地工程 Agent 的安全边界。不要调用工具。

观察并记录：

- 点击发送后，用户消息是否在 RPC 完成前立即出现；
- Composer 是否立即清空；
- 是否只出现一个 AI 消息块；
- 状态是否按实际进度显示连接、思考、生成和完成；
- 回复是否分段更新，而不是完成后整块突然出现；
- 标题、列表、代码块是否按 Markdown 渲染；
- 完成后是否显示处理时间；
- 权威消息到达后用户消息和 AI 消息是否各只有一份。

第二轮建议提示词：

> 延续上一条，只把第二项改写得更具体，并保持三项列表结构。不要调用工具。

通过条件：

- 第二轮能直接发送，Composer 没有停留在旧 busy/pending 状态；
- 模型正确利用同一 thread 的上一轮上下文；
- 两轮均为单一用户消息和单一 AI 消息投影；
- 没有错误显示“执行已中断”、要求重复 Resume/Restart，或生成重复 Turn。

建议记录两项非阻塞性能数据：`发送到 connecting`、`发送到首段 assistant 内容`。真实 provider 的 time-to-first-content 不设硬阈值，但必须区分 provider 延迟与客户端等待完整响应。

### Gate 4：Stop 与恢复

第三轮建议提示词：

> 请写一篇约 800 字的说明，分成至少六个 Markdown 小节，逐项解释本地 Agent 的审计流程。不要调用工具。

在首段内容出现后点击 Composer 主按钮位置的 Stop。

通过条件：

- Stop 可立即操作，且没有同时出现冲突的 Send/Restart 主操作；
- 已显示内容保留，状态变为已停止并显示处理时间；
- 停止后不再追加新的 provider 内容；
- Composer 恢复输入和发送能力；
- 不产生第二条用户消息、第二条 AI 消息或重复副作用。

若模型在操作前已经完成，记录为 `Inconclusive`，只允许再尝试一次更长的纯文本请求；不得循环消耗真实 provider Turn。

### Gate 5：重载与持久化

1. 切换到另一条会话，再返回本次会话。
2. 关闭并重新启动当前 Desktop；确认 owned AppHost 正常退出后再启动。
3. 重新打开同一测试工作区和会话。

通过条件：

- 已完成与已停止内容仍可读取，顺序和 Markdown 结构正确；
- 处理时间来自持久化状态，不在重载后明显漂移；
- 已完成 Turn 不被标记为 recovery required；
- 没有重复消息、空白替换、旧 Composer queue 回写或错误 pending 状态。

### Gate 6：三面板视觉与键盘

在 1440×900、1024×768、800×900 三档窗口尺寸逐一检查：

1. 打开和关闭顶部摘要、底部详情、右侧工具导航。
2. 在右侧选择 Changes、Terminal、Reports、Artifacts 或 Preview，确认底部详情自动打开且只切换展示状态。
3. 使用 Tab 到达三个面板按钮；使用 Enter/Space 打开；使用 Escape 关闭。
4. 在 800×900 下依次打开左侧会话栏和右侧工具栏，检查两者互斥。
5. 打开顶部与底部浮层后滚动消息时间线，并保持 Composer 可见可操作。

通过条件：

- 1440px 下右侧栏为内联布局；1024px 与 800px 下为右侧抽屉；
- 顶部摘要和底部详情不改变 `timeline-stage` 宽度；
- 没有 body 横向滚动、内容裁切或 Composer/浮层关键重叠；
- Escape 关闭最后打开的表面并把焦点还给对应按钮；
- 800px 下左右抽屉不会同时打开；
- Branch、PR、Compare branches、Commit/push 和 Sub-agents 继续明确显示 Unknown/Unavailable/Disabled，不伪造数据；
- 点击只读/禁用入口不会执行 Git、Shell、终端或外部写操作。

### Gate 7：秘密、日志与进程收口

1. 检查本轮可见 UI、复制错误信息、截图名称和验收记录。
2. 正常关闭 Desktop，确认 owned AppHost 随之退出。
3. 再次记录 `git status --short`，确认没有模型配置、临时日志或意外产品文件进入工作树。

通过条件：

- API Key、请求头、完整 `.env.local` 内容和原始敏感错误出现次数为 0；
- 未授权写操作、Git 写操作、MCP 写操作和外部消息次数为 0；
- 没有由本轮产生的 orphan Desktop/AppHost；
- 工作树增量只包含明确的验收记录或已授权修复。

## 6. 证据要求

本地非跟踪证据建议放入：

```text
artifacts/manual-acceptance/2026-08-04-128f2ca/
├─ 01-startup.png
├─ 02-first-turn-streaming.png
├─ 03-second-turn-completed.png
├─ 04-stopped-turn.png
├─ 05-desktop-1440x900.png
├─ 06-compact-1024x768.png
├─ 07-narrow-800x900.png
└─ acceptance-notes.md
```

截图不得包含 API Key、环境变量窗口、请求头、用户私人路径内容或不属于测试工作区的数据。最终跟踪文档只记录：

- exact revision；
- 执行时间与操作者；
- 各 Gate 的 `Passed`、`Failed`、`NotRun` 或 `Inconclusive`；
- provider Turn 数；
- 必要的脱敏时间数据；
- 缺陷编号和复现步骤；
- 最终明确签署。

## 7. 缺陷分级与停止规则

### P0：立即停止

- secret、跨工作区路径或私人数据泄露；
- 未授权文件、Shell、Git、MCP 或外部写入；
- 重复执行副作用或数据损坏；
- fake runtime 被误当真实 provider；
- 无法停止或收回 owned process。

### P1：本轮 Blocked

- 用户或 AI 消息重复；
- Composer 完成后持续不可用；
- 正常 running/completed Turn 被误报中断；
- Stop 无效或停止后继续产生内容；
- 持久化内容丢失、错序或重复；
- 三面板遮挡 Composer、关键操作不可达或抽屉互斥失效；
- Desktop/AppHost crash loop。

### P2：记录后评估

- 不影响任务完成的视觉间距、文案或轻微滚动问题；
- provider 首段内容延迟较长，但客户端阶段和流式行为正确；
- 已知环境性 E2E teardown/GPU 问题，且本轮真实桌面与产品断言未失败。

发现 P0/P1 后停止扩大测试，保存已取得的脱敏证据，只针对一个明确复现路径修复。修复后重跑直接受影响 Gate，不自动扩大到 release 全矩阵。

## 8. 最终判定

`Manual Product Gate Passed` 必须同时满足：

- Gate 0–7 全部通过；允许 Gate 1 使用 exact-revision 已提交证据；
- 必需人工场景没有 `NotRun` 或 `Inconclusive`；
- open P0/P1 为 `0/0`；
- 用户对最终三档视觉和真实连续对话给出明确 Passed；
- 证据绑定 exact revision，且未泄露秘密；
- 本轮没有扩大为 release、push、tag、发布或部署。

否则结论为 `Blocked` 或 `NotRun`，不得用“基本通过”“代码看起来正常”替代。

## 9. 验收记录模板

| 项目 | 结果 |
| --- | --- |
| Exact revision |  |
| 执行日期/时间 |  |
| 操作者 |  |
| 测试工作区 | 脱敏名称： |
| Provider Turns |  / 6 |
| Gate 0 候选身份 |  |
| Gate 1 自动证据 |  |
| Gate 2 真实启动 |  |
| Gate 3 两轮连续对话 |  |
| Gate 4 Stop 与恢复 |  |
| Gate 5 重载与持久化 |  |
| Gate 6 三面板视觉/键盘 |  |
| Gate 7 安全与清理 |  |
| Open P0/P1/P2 |  /  /  |
| 用户视觉确认 |  |
| 最终结论 | `Manual Product Gate Passed` / `Blocked` / `NotRun` |

签署备注：

> 我确认以上结果只适用于所记录的 exact revision 和当前内部 Desktop Preview，不代表 `0.6.0` 正式发布已获接受。

## 10. 通过后的收口动作

1. 把 `docs_md/spec/codex_style_continuous_chat_retry_acceptance_report.md` 更新到 exact revision，并将真实模型人工确认从待确认改为实际结果。
2. 在 `docs_md/weekly/84_92_renderer_chat_ui_execution_acceptance.md` 补充最终用户视觉签署、截图位置和执行时间。
3. 保留 `docs_md/spec/right_ui_refactor_acceptance_report.md` 的历史工程证据，只追加本轮签署链接，不重写既有结果。
4. 单独提交验收文档收口；是否 push 或创建 PR 由用户另行决定。
5. 完成闭环后，再为右侧面板设计唯读 Git branch、ahead/behind 和 compare summary 的 `desktop-v1` 合约，不在本轮混入 Git 写能力。
