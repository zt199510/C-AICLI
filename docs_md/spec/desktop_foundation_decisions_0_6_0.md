# C-AICLI 0.6.0 Desktop Foundation 决策记录

更新时间：2026-07-16

状态：Week 66 技术 Gate 已确认；source revision 随本提交固化

## 起点

- Git branch：`week-02-cli-commands-doctor-config`，tracking `origin/week-02-cli-commands-doctor-config`。
- Week 66 实现起点 HEAD：`fc90798`。
- 0.5.0 accepted package source：`09378e66463e2830bd432793d863fcfe8f8156bd`。
- 根版本继续是 `0.5.0`；Desktop spike 的 npm package version 是 `0.6.0`，不提前改写 CLI release version。
- 实现开始前工作区已经包含 0.5.0 acceptance 和 0.6.0 framework 文档改动；本周未回退或覆盖这些改动，并与 Week 66 实现一起纳入本提交。

## 决策

### D1：先建立 Application read-only vertical slice

新增 `CSharpAiCli.Application`，首个 use case 为 workspace open。它直接复用 `WorkspaceContext`、`WorkspaceGuard` 和稳定 `ToolErrorCode`，返回结构化结果，不写 CLI 文本、不依赖 Electron、不读取 Renderer 安全结论。

Week 66 不大范围重写 `CliCommandFactory`。Week 67 从 workspace/catalog/changes/report/artifact read-only use case 开始迁移，并以 CLI/Application parity test 保持文本、exit code、安全结论和 store identity。

### D2：AppHost 是唯一 Desktop runtime authority

新增 `CSharpAiCli.AppHost`，使用单父进程 stdio，不监听 TCP。AppHost 负责：

- protocol framing、handshake、version/capability negotiation；
- Application service 调用；
- workspace、approval、policy、redaction、store 和 process lifecycle 的最终决定；
- Renderer/Main 断开时的终止与后续 interrupted-state 边界。

AppHost stdout 只允许协议帧。bounded diagnostics 使用 stderr，不把 request payload、路径内容、secret 或 approval grant写入诊断。

### D3：`contract.json` 是 reviewed protocol source

`protocol/desktop-v1/contract.json` 统一定义：

- protocol version；
- frame/diagnostic limits；
- method name、params/result type；
- 共享 C#/TypeScript record/interface。

`apps/desktop/scripts/generate-contracts.mjs` 同时生成 C# 与 TypeScript；`--check` 在不改文件的情况下拒绝 drift。生成的 C# 保持 checked-in，使普通 .NET build 不依赖 Node。

Week 66 方法：`app.initialize`、`workspace.open` 和仅用于父进程正常关闭的 internal `app.shutdown`。Thread/timeline 方法按 Week 68-69 加入同一 contract source。

### D4：协议边界先固定

| 边界 | Week 66 值 | 处理 |
|---|---:|---|
| Header | 8,192 bytes | 超限终止 session |
| Body | 1,048,576 bytes | 读写两侧拒绝 |
| stderr diagnostics | 16,384 bytes | Main 只保留尾部 bounded 文本 |
| Request timeout | 5,000 ms | Main 拒绝 pending request |
| Shutdown timeout | 1,500 ms | 超时后 kill fallback |

Framing 使用 ASCII `Content-Length` header 和原始 UTF-8 JSON body。测试覆盖 partial header/body、missing/invalid/duplicate length、oversize、malformed JSON、unknown method、unsupported version、EOF、正常 shutdown 和 crash。

### D5：Electron 安全边界

- `contextIsolation=true`
- `nodeIntegration=false`
- Renderer sandbox enabled
- `webSecurity=true`
- `allowRunningInsecureContent=false`
- navigation/new window 默认拒绝
- production CSP 不允许 localhost、remote URL、`unsafe-eval` 或 `unsafe-inline`
- preload 只暴露 `initialize()`、`openWorkspace()`、`onRuntimeStatus()`

Renderer 不提交任意路径。`openWorkspace()` 由 Main 打开系统目录选择器，再由 AppHost 使用 workspace guard 重验。未来 drag/drop、clipboard path 和附件仍走相同重验路径。

### D6：依赖版本与来源

| Dependency | Version | 决策 |
|---|---:|---|
| Node | 22.13.0 baseline | 满足 Electron/Vite/ESLint engine |
| npm | 11.7.0 baseline | lockfile v3 |
| Electron | 41.1.0 | 官方 win-x64 ZIP checksum 已验证；43.1.1 官方下载在当前网络超时，不引入第三方 mirror |
| React / React DOM | 19.2.7 | exact lock |
| Vite | 8.1.4 | exact lock |
| TypeScript | 6.0.3 | `typescript-eslint 8.64.0` 支持 `<6.1`；拒绝强装 TypeScript 7 |
| ESLint | 10.7.0 | flat config |
| Vitest | 4.1.10 | Node process tests |
| Electron Packager | 20.0.2 | unpacked Windows spike package |

`package-lock.json` 没有 npm registry 以外的 resolved source。`npm audit` 为 0 vulnerability。完整 295-package license inventory 由 `generate-notices.mjs` 生成并检查；当前许可证集合不包含 GPL/AGPL 或未声明许可证。Electron runtime 自带 `LICENSE` 和 `LICENSES.chromium.html`。

### D7：打包与 AppHost resource

AppHost 使用 `.NET 9` self-contained `win-x64` single-file publish，放在 Electron `resources/apphost`。Main packaged path 只从 `process.resourcesPath` 解析，不依赖 cwd 或 Renderer 输入。

`CAICLI_ELECTRON_ZIP_DIR` 只用于提供 exact official Electron archive 的离线/缓存目录。Electron Packager 仍校验 archive identity；缓存、源码、dev dependencies 和 Node modules 不进入 `app.asar`。

### D8：测试确定性

既有标准 full suite 失败来自测试 wall-clock budget：

- MCP 200 ms timeout 同时包含 PowerShell process startup 与最多约 1.2 秒 cleanup；2 秒上限对并发负载过窄，调整为 4 秒，仍小于 fake server 5 秒自然退出。
- Local API integration 连续发送多次真实 loopback HTTP request；单 request 3 秒上限调整为 10 秒，整体 cancellation 从 15 秒调整为 30 秒。

调整后标准并发 full suite 连续两次 `1269/1269`，不再依赖单处理器才通过。产品 timeout、cleanup 和 Local API runtime code 未改变。

## 责任矩阵

| 层 | 允许职责 | 禁止职责 |
|---|---|---|
| Renderer | view、draft、selection、UI-safe projection | Node、fs、process、API key、store、policy decision |
| Preload | frozen typed allowlist、runtime validation | generic IPC、fs/shell/process bridge、业务状态 |
| Electron Main | window、dialog、AppHost lifecycle、framed client | agent loop、approval decision、workspace guard、business store |
| AppHost | protocol、Application dispatch、安全重验、process authority | CLI text parsing、Renderer trust、TCP control listener |
| Application | structured use case、error/cancel/redaction contract | CLI/Electron dependency、text renderer、第二套 store |
| Core/ProjectPacks | accepted runtime、policy、store、vertical truth | UI state、Electron concern |

禁止依赖方向：

```text
Application -> Cli
Application -> AppHost
Application -> Electron/Desktop
AppHost -> Cli
Renderer -> node:fs / node:child_process / Electron ipcRenderer
Main/Renderer -> existing store files directly
```

## Foundation 性能基线

采样命令：`tools/Measure-DesktopBaseline.ps1`。本次使用 packaged app、3 秒采样、6 秒自动正常退出：

| Evidence | Value |
|---|---:|
| Package files | 76 |
| Unpacked package | 437,851,431 bytes |
| `app.asar` | 2,175,389 bytes |
| AppHost | 75,854,929 bytes |
| Processes at sample | 6 |
| Working set at sample | 406,470,656 bytes |
| Private bytes at sample | 242,315,264 bytes |
| Lifecycle sample | 8,017 ms |
| Orphan AppHost delta | 0 |

本值是 Week 66 foundation comparison baseline，不是最终 SLA。后续周次使用同一命令和机器口径比较：package 或 working set 增长超过 15%、`app.asar` 出现非预期 cache/source/native payload、process count 不回落、orphan delta 非 0 时必须调查。长 timeline、diff、terminal output 的 workload fixture 在对应功能进入时增加；protocol hard limit 从现在起不得静默放宽。

## Week 67 稳定输入

- `CSharpAiCli.Application` read-only workspace slice 和依赖方向。
- `desktop-v1` 单一 contract source、双端生成与 drift check。
- 8 KiB/1 MiB/16 KiB protocol bounds。
- AppHost real-process handshake/workspace/shutdown/crash tests。
- Electron secure Main/Preload 和 three-operation typed bridge。
- exact Node dependency lock、notice inventory、audit 结果和 offline official ZIP入口。
- packaged smoke、performance sample 和 process cleanup scripts。

本决策记录、0.5.0 acceptance 修订、0.6.0 framework/schedule 与 Week 66 实现由同一提交固化；提交完成后 G0 闭合，可按 Week 67 输入继续执行。
