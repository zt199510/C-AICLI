# C-AICLI Codex 式连续对话与连接重试验收报告

## 1. 报告信息

| 项目 | 内容 |
| --- | --- |
| 需求基线 | `docs_md/spec/codex_style_continuous_chat_retry.md` |
| 验收对象 | CLI、.NET AppHost、desktop-v1 contract、React Renderer、自动化测试 |
| 实现提交 | `34247ca feat: add continuous chat provider retry flow` |
| 开发分支 | `main`，未创建功能分支 |
| 实施日期 | 2026-07-30 |
| 报告日期 | 2026-07-31 |
| 验收结论 | **工程验收通过；真实模型连续对话待产品手工确认** |

本报告以已确认开发规格为唯一需求基线。自动化测试、静态检查、协议一致性检查、Release 构建及真实桌面进程启动均已完成。由于自动化环境未代替用户在 Electron 窗口内发送真实模型消息，最终产品验收保留一项人工确认。

## 2. 实施范围与结果

### 2.1 Provider/runtime

- 在 `IToolCallingModel.Start/Continue` 的单次 provider/model 请求边界增加重试包装。
- 最大请求次数固定为 6 次，即 1 次初始请求和最多 5 次追加重试。
- 实现可取消的有界指数退避，最大等待 4 秒。
- 支持 transport、timeout、流式中断、HTTP 408、429 和 5xx 重试。
- HTTP 400、401、403、配置错误、审批拒绝和用户取消不重试。
- 当前请求超时按 attempt 独立计时；整个 Turn 仍受总体超时约束。
- Stop 同时取消当前 provider 请求和等待中的下一次重试。
- 流式中断后每个 attempt 使用独立内容缓冲，新 attempt 内容不会与失败 attempt 拼接。
- 重试只重新调用失败的模型边界，不重新运行 Agent Turn 或已完成工具。
- `Continue` 对已提交的工具结果按 `CallId` 去重，防止同一请求重试时重复累积。

### 2.2 Durable state、AppHost 与协议

- canonical desktop-v1 contract 增加 `ProviderProgressData`、`provider.attempt` timeline 类型，以及 phase、attempt、retry、assistant identity 和安全错误字段。
- 先修改 canonical contract，再运行生成脚本生成 TypeScript/C# contracts；未直接手改生成文件。
- AppHost 将 provider attempt 进度写入 durable timeline，并折叠到 Turn 权威状态。
- 保留 Turn、revision、durable timeline 和 AppHost authority。
- assistant message identity 在同一个 Turn 内保持稳定。
- 处理时间继续以持久化的 `createdAtUtc` 和 `completedAtUtc` 为权威来源。
- 对 retry-exhausted Turn 的显式重新尝试增加副作用检查：若已完成工具、命令、审批或变更记录，拒绝整 Turn 重放。

### 2.3 Renderer 与交互

- 点击发送后立即创建乐观用户消息和 AI 占位，不等待 RPC 返回。
- Composer 立即清空；权威层拒绝时保留输入并支持恢复。
- 使用 composer intent identity 与 AppHost 权威 Turn/用户消息对账，权威消息到达后移除乐观副本。
- connecting、thinking、streaming、final 始终投影为同一个 AI block。
- 显示“正在连接 → 正在思考 → 正在生成回复 → 已完成”。
- retry-wait 显示“连接中断，正在重试 n/5...”。
- retry exhausted 显示“连接失败，已重试 5 次”，并提供“重新尝试”和“复制错误信息”。
- “复制错误信息”仅复制 AppHost 提供的安全、脱敏诊断。
- Stop 移至 Composer 主按钮位置；审批状态下发送与 Stop 操作互斥。
- 普通对话不再显示大型 running Turn 卡片。
- 完成、停止和失败均显示权威处理时间。
- `aria-live` 只播报阶段变化，不逐 token 重复朗读。

## 3. 验收标准核对

| # | 验收标准 | 状态 | 验收证据 |
| --- | --- | --- | --- |
| 1 | 用户消息立即出现，权威对账后重复数为 0 | 通过 | Controller 测试验证 RPC 未完成前已显示；Timeline 测试验证 authority 到达后无重复消息 |
| 2 | AI 占位、流式内容和 final 为同一可见消息 | 通过 | stable assistant identity；connecting/thinking/streaming/final 单 block 测试通过 |
| 3 | 可重试失败最多总调用 6 次 | 通过 | 初始成功 1 次、第 1 次重试成功 2 次、第 5 次重试成功 6 次、耗尽严格 6 次 |
| 4 | 非重试错误严格调用 1 次 | 通过 | 400、401、403 分类测试均为单次调用 |
| 5 | cancel 后新增 provider 调用数为 0 | 通过 | cancel during request 和 cancel during backoff 回归测试通过 |
| 6 | 工具、命令、文件写入和审批重复执行数为 0 | 通过 | 自动重试只包裹 model boundary；工具计数保持 1；显式 restart 对已完成副作用 fail closed |
| 7 | 不同 attempt 的流式内容交叉拼接数为 0 | 通过 | 每 attempt 独立 buffer；Renderer attempt replacement 测试通过 |
| 8 | 完成、停止和最终失败显示处理时间 | 通过 | Renderer 状态与 duration 测试通过，时间来自 durable Turn timestamps |
| 9 | 普通对话不显示大型 running Turn 卡片 | 通过 | TaskControls 测试确认普通 running 状态不渲染大卡片 |
| 10 | 测试、typecheck、lint、build 全通过且单项少于 5 分钟 | 通过 | 见第 4 节，最长单项约 1 分 20 秒 |
| 11 | Week83 listener P1 不作为前置阻塞且未恶化 | 通过 | 本次未修改该 listener，相关全量测试无回归 |
| 12 | 使用 `.env.local` 启动真实程序且不泄露密钥 | 部分通过 | `.env.local` 已在独立进程环境加载，Electron 与 Release AppHost 启动成功且未输出密钥；真实消息发送结果待用户手工确认 |

## 4. 自动化验证记录

所有命令均在 `D:\AI\C-AICLI` 或 `apps\desktop` 下执行，单个命令未超过 5 分钟。

### 4.1 .NET

由于仓库 `global.json` 指定 .NET SDK 9.0.308，而当时系统级 SDK 为 10.0.302，验收使用已安装 SDK 的 MSBuild/VSTest 入口直接执行，未修改 `global.json`。

| 验证 | 结果 | 实际耗时 |
| --- | --- | --- |
| 关键路径测试 | 70/70 通过 | 约 7 秒 |
| 完整 C# 测试程序集 | 1440/1440 通过 | 约 1 分 20 秒 |
| Release 全解决方案构建 | 通过 | 约 9 秒 |

完整测试命令：

```powershell
dotnet "C:\Program Files\dotnet\sdk\10.0.302\vstest.console.dll" `
  .\src\CSharpAiCli.Tests\bin\Debug\net9.0\CSharpAiCli.Tests.dll `
  "/Logger:console;verbosity=minimal"
```

Release 构建命令：

```powershell
dotnet "C:\Program Files\dotnet\sdk\10.0.302\MSBuild.dll" `
  .\src\CSharpAiCli.sln /t:Build /p:Restore=false `
  /p:Configuration=Release /v:minimal
```

### 4.2 Desktop/Renderer

| 验证 | 结果 | 实际耗时 |
| --- | --- | --- |
| `npm run check:contracts` | 通过 | 小于 1 秒 |
| `npm run typecheck` | 通过 | 约 4 秒 |
| `npm run lint` | 通过 | 约 4 秒 |
| `npm test` | 29 个文件、139/139 通过 | 约 8 秒 |
| `npm run check:accessibility` | WCAG 2.x AA 检查通过 | 约 1 秒 |
| `npm run build` | contract/notices/typecheck/main/renderer/security 全通过 | 约 9 秒 |

production renderer 构建完成 1794 个模块转换；production security 检查通过。canonical contract check 未发现生成文件漂移。

## 5. Retry/error/state 测试矩阵

| 场景 | 期望 | 结果 |
| --- | --- | --- |
| 初始请求成功 | provider 调用 1 次 | 通过 |
| 初始失败，第 1 次重试成功 | provider 调用 2 次 | 通过 |
| 第 5 次追加重试成功 | provider 调用 6 次 | 通过 |
| 5 次追加重试全部失败 | provider 调用 6 次并标记 retry exhausted | 通过 |
| HTTP 400/401/403 | 不重试，调用 1 次 | 通过 |
| timeout/408/429/500/503 | 使用可重试预算 | 通过 |
| 请求中 Stop | 取消当前请求，不再调用 | 通过 |
| backoff 中 Stop | 取消等待，不发起下一 attempt | 通过 |
| 工具完成后 Continue 请求中断 | 工具执行次数保持 1 | 通过 |
| 失败 attempt 已产生部分内容 | 新 attempt 输出替换旧内容，不拼接 | 通过 |
| retry exhausted 后显式重试且已有副作用 | 拒绝整 Turn 重放 | 通过 |

## 6. 启动验收记录

2026-07-30 使用仓库根目录 `.env.local` 启动开发态 Electron，并指定 Release AppHost：

- Electron 主进程启动成功；
- Release `CSharpAiCli.AppHost.dll` 子进程启动成功；
- 启动检查仅输出进程状态，未输出 `.env.local` 内容或 API Key；
- Git 工作树在提交后保持干净。

2026-07-31 按磁盘清理要求，已停止上述进程并删除 Git 忽略的可重建产物。因此当前验收机上没有正在运行的 Desktop/AppHost，重新手工验收前需要重新构建。

建议人工验收步骤：

1. 重新构建 Release AppHost 和 Desktop production bundle。
2. 使用 `.env.local` 启动 Desktop，打开测试工作区。
3. 发送普通消息，确认用户消息立即出现且最终不重复。
4. 观察同一 AI 消息依次显示连接、思考、生成和完成状态。
5. 在请求或重试等待阶段点击 Stop，确认没有新的 provider attempt。
6. 使用可控临时故障验证 1/5 至 5/5、流式内容替换和 retry-exhausted 操作。
7. 检查日志与“复制错误信息”内容，确认无 API Key 或原始敏感诊断。

## 7. 交付物

- `main` 实现提交：`34247ca`。
- canonical desktop-v1 contract 及生成的 TypeScript/C# contracts。
- provider retry、错误分类、取消与副作用防重放测试。
- Renderer 乐观消息对账、单 AI 消息、状态、Stop、处理时间和失败操作测试。
- 本验收报告。

## 8. 已知事项与最终结论

- Week83 listener P1 为既有非阻塞技术债，本次未修改且没有测试证据表明其恶化。
- 为避免副作用重放，retry-exhausted Turn 若已有已完成操作，“重新尝试”会安全拒绝并显示错误，而不是自动复制整个 Turn。
- 清理后的 `bin/obj/dist/out/resources/apphost/artifacts` 均可重新生成，但自动化测试和构建需要先恢复相应产物。
- 最终真实 provider 连续对话仍需用户在已启动的桌面窗口中完成一次人工确认。

**结论：代码、协议、自动化测试和构建验收通过；待真实模型连续对话人工确认后，可将总体状态更新为“完全通过”。**
