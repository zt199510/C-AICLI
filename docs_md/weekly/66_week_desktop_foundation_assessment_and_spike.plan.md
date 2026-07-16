# 第 66 周计划：Desktop Foundation 评估与 Critical Spike

状态：已完成；技术实现、验证与 source revision 随本提交固化

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

## 本周目标

在创建正式 Desktop 产品代码前，回答 0.6.0 的 source、依赖、Application 边界、协议生成、跨进程生命周期、打包和测试确定性问题。Week 66 的产物是可执行的技术决定与最小 spike 证据，不是可演示但不可复用的 UI。

## 已完成的起点评估

- [x] 核对 0.5.0 acceptance、0.6.0 roadmap 与 Desktop framework。
- [x] 盘点当前 solution：Core、Cli、AgentFramework、ProjectPacks、Tests 共 5 个项目；Application/AppHost/Desktop/Protocol 尚不存在。
- [x] 确认 Node `22.13.0`、npm `11.7.0` 可用，当前无 Desktop lockfile。
- [x] 确认用户目录存在仓库锁定的 .NET SDK `9.0.308`；默认 PATH 先解析到系统 `10.0.301`。
- [x] 使用 `9.0.308` 完成 Release build：`0 warnings / 0 errors`。
- [x] 运行标准 full suite：`1259 passed / 2 failed / 0 skipped`；失败为 MCP initialize wall-clock 与 Local API 3 秒 timeout。
- [x] shutdown build servers 后以单处理器复核：`1261 passed / 0 failed / 0 skipped`，耗时约 3 分 15 秒。
- [x] 确认开始实现前需要冻结 0.5.0/0.6.0 文档与 source revision；当前工作区不是干净实现起点。

## Critical Questions

本周结束前必须形成书面结论：

1. 哪些 use case 首先从 CLI 编排中抽到 `CSharpAiCli.Application`，如何证明 CLI 行为与安全语义未变？
2. desktop-v1 的唯一 contract source 是什么，C# records 与 TypeScript types 如何生成或验证？
3. `Content-Length` framing 对 partial read、oversize、并发 request、backpressure 和 disconnect 的边界是多少？
4. Electron Main 如何定位、启动、验证并清理 self-contained AppHost，开发态与 packaged 态如何保持同一协议？
5. Electron/React/Vite/Radix/Zustand/Markdown/Shiki/Monaco/xterm/Playwright 的兼容版本、许可、notices 和 lockfile 策略是什么？
6. 冷启动、idle memory、长 timeline、protocol payload、terminal output 和 package size 如何测量，什么规则构成 release regression？
7. 标准 full suite 的既有 timeout 波动如何变成确定性基线，而不是被未来 Desktop E2E 放大？

## 工作项

### 1. 冻结 source 与责任边界

- [x] 将 0.5.0 Accepted 文档、0.6.0 framework 和 Week 66-77 schedule 纳入可追踪 revision。
- [x] 记录实现起点的 branch、commit、dirty state、SDK、Node、npm 和 Windows runtime。
- [x] 建立 Application/AppHost/Main/Preload/Renderer 的责任矩阵和禁止依赖表。
- [x] 明确 AppHost 是 workspace、approval、policy、redaction、store 与 process lifecycle 权威。
- [x] 明确 thread 只投影现有 session/job/queue/report/artifact truth。

### 2. Application extraction spike

- [x] 选择一个 read-only use case，验证从 CLI 编排抽到结构化 Application service 的最小改动路径。
- [x] 定义 result/error/cancellation/redaction contract，不把 CLI 文本带入 Application。
- [x] 证明 CLI 输出、exit code、安全校验和 store identity 可通过 parity test 保持兼容。
- [x] 记录不能立即抽取的耦合点、预计拆分顺序和回退方案。

### 3. Protocol 与生成链 spike

- [x] 实现一次进程内和一次真实 stdio 的 `Content-Length` frame round-trip。
- [x] 覆盖 partial header/body、invalid length、oversize、malformed JSON、unknown method 和 EOF。
- [x] 对比 schema-first、C#-first 或 reviewed dual-validation 方案，只保留一个 source of truth。
- [x] 生成或验证最小 initialize/workspace contract，并加入 drift 检查；thread contract 按 Week 68 排期进入。
- [x] 冻结 protocol version、capability negotiation 和不兼容版本错误语义。

### 4. Electron/AppHost lifecycle 与 packaging spike

- [x] 用 Electron Main 启动最小 AppHost，完成 initialize、正常退出、强制退出 fallback 与 crash detection。
- [x] 验证 AppHost stdout 只传协议、stderr bounded diagnostics、Renderer 不接触 child process。
- [x] 验证 unpacked 与 packaged resource path，不依赖当前工作目录猜测 executable。
- [x] 验证 Main/Renderer 退出后 AppHost 与子进程为 0，临时目录可清理。
- [x] 记录 self-contained AppHost 的 hash/inventory 如何进入 Desktop manifest。

### 5. Desktop dependency 与 security Gate

- [x] 冻结兼容依赖版本和 lockfile；记录 Electron/Chromium/Node/runtime identity。
- [x] 完成依赖许可、第三方 notices、native payload 和已知高风险 advisory 检查。
- [x] 自动断言 `contextIsolation=true`、`nodeIntegration=false`、sandbox 与严格 CSP。
- [x] 设计冻结的 typed preload allowlist；禁止通用 IPC、fs、shell、process 暴露。
- [x] 记录 navigation、新窗口、external URL、drag/drop、clipboard path 的默认拒绝策略。

### 6. 测试确定性与预算

- [x] 定位 MCP initialize 和 Local API timeout 对 CPU/并发/wall-clock 的敏感点。
- [x] 修复测试预算，建立不掩盖产品超时语义的稳定 harness。
- [x] 标准 full suite 连续运行达到本周约定的稳定门槛；受控单处理器结果继续记录作交叉证据。
- [x] 建立 AppHost contract、Renderer unit、Desktop process/package smoke 的测试分层和默认 fake runtime；完整 E2E 仍按后续周次进入。
- [x] 定义 cold start、idle memory、long timeline、payload、output 和 package size 的采样命令与回归规则。

## 明确不做

- 不在 Week 66 实现正式聊天 UI、Terminal、Monaco Diff 或 Gerber/TIFF Preview。
- 不把 Electron 直接连接 CLI stdout 文本或现有 localhost daemon。
- 不为了 spike 复制 agent loop、approval、store、workspace guard 或 artifact ownership 逻辑。
- 不开放 Renderer 的 Node、任意文件、任意进程或通用 IPC 权限。
- 不在技术 Gate 未通过时提前进入 write-capable turn。

## 验证命令基线

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

Desktop spike 建立后必须提供单一、可重复的 npm 入口，至少覆盖 install、typecheck、lint、unit test 和 production build。具体 script 名称由本周 dependency Gate 冻结，不在计划中假设未验证的命令。

## 周末验收 Gate

Week 66 只有同时满足以下条件才可标记 Passed：

1. 实现起点 source revision 可追踪，0.5.0 acceptance 与 0.6.0 scope 无歧义。
2. Application extraction spike 证明 CLI 与 Desktop 可共享结构化 use case，而非复制命令逻辑。
3. framing、schema/type generation、handshake、spawn/crash/cleanup 和 packaged path spike 通过。
4. Renderer 安全基线与 typed preload allowlist 可被自动测试。
5. 标准 full suite 的既有 timeout 风险得到稳定处理或明确 Blocked，不只依赖盲目重试。
6. 依赖版本、许可、lockfile、notices、性能采样和 package inventory 方案已冻结。
7. 创建 `66_week_review.md`，记录命令、计数、耗时、process cleanup、决定、风险和 Week 67 输入。

如果第 2、3、4 或 5 项未通过，Week 67 不得以直接开发 Renderer 或复制 CLI 路径的方式绕过 Gate。

## Week 67 输入

通过 Gate 后，Week 67 只接收以下稳定输入：

- Application/AppHost/Desktop 责任与依赖规则。
- 选定的 contract source 和 generation/validation 命令。
- 已验证的 framing、handshake、process lifecycle 和 packaged resource path。
- 已锁定的 Desktop dependencies、lockfile 与 notices 策略。
- 可重复的 .NET/Node 测试基线和性能采样方法。
