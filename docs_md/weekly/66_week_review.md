# 第 66 周回顾

状态：已完成；Critical Gate G0-G6 Passed，source baseline 随本提交固化

更新时间：2026-07-16

## 完成范围

- 新增 `CSharpAiCli.Application`，以 workspace open 建立首个 read-only structured use case。
- 新增 `CSharpAiCli.AppHost`，实现 framed JSON-RPC stdio、initialize/workspace/shutdown、bounds 和安全错误。
- 建立 `protocol/desktop-v1/contract.json` 单一 source，以及 C#/TypeScript 双端生成和 drift check。
- 建立 Electron 41 + React 19 + TypeScript 6 + Vite 8 secure shell、typed preload 和最小 workspace UI。
- 建立真实 AppHost process handshake/workspace/shutdown/crash test、packaged smoke、orphan check 和性能采样。
- 建立 exact npm lock、完整第三方 dependency inventory、license allowlist 和 production CSP check。
- 调整既有 MCP/Local API integration test 的并发 wall-clock budget；未改变产品 runtime timeout 或 cleanup。
- 创建 `docs_md/spec/desktop_foundation_decisions_0_6_0.md`，冻结责任矩阵、禁止依赖、protocol limits 和 Week 67 输入。

## Source 与环境

- Branch：`week-02-cli-commands-doctor-config`，tracking `origin/week-02-cli-commands-doctor-config`。
- Week 66 起点 HEAD：`fc90798`。
- Accepted 0.5.0 package source：`09378e66463e2830bd432793d863fcfe8f8156bd`。
- SDK：用户目录 `.NET SDK 9.0.308`；系统默认 PATH 先解析到 10.0.301，因此验证显式前置 `%USERPROFILE%\.dotnet`。
- Node/npm：`22.13.0` / `11.7.0`。
- Electron：`41.1.0 win-x64`。官方 ZIP SHA256 与 npm package 内置 checksum 一致；未使用第三方 mirror。
- 实现前已有的 0.5.0/0.6.0 文档改动与本周新增实现由本提交统一固化。

## .NET 验证

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
dotnet test src\CSharpAiCli.sln -c Release --no-build
```

- Release build：7 projects，`0 warnings / 0 errors`。
- 新增 Application/Protocol 定向测试：`8/8`。
- 标准并发 full suite 第 1 次：`1269 passed / 0 failed / 0 skipped`，约 1 分 29 秒。
- 标准并发 full suite 第 2 次：`1269 passed / 0 failed / 0 skipped`，约 1 分 26 秒。
- 起点评估的受控单处理器证据仍为 `1261/1261`；完成后不再需要单处理器才能通过。

## Desktop 验证

```powershell
npm install --ignore-scripts
npm run verify
npm audit --json
npm run package:apphost
$env:CAICLI_ELECTRON_ZIP_DIR = "$env:LOCALAPPDATA\electron\Cache"
node apps\desktop\scripts\package-desktop.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-DesktopSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Measure-DesktopBaseline.ps1
```

- Contract drift、notice drift、typecheck、ESLint、production CSP 和 build 全部通过。
- Desktop tests：5 files，最终 `11 passed / 0 failed`；包含真实 .NET AppHost process、crash 和 destroyed-window path。
- Renderer production bundle：JS 约 195.5 kB / gzip 61.8 kB；CSS 约 3.25 kB / gzip 1.23 kB。
- npm audit：295 dependencies，0 info/low/moderate/high/critical vulnerability。
- Resolved dependency source：全部 `registry.npmjs.org`；无未声明或未审查 license。
- Production CSP：无 localhost/remote connect、`unsafe-eval` 或 `unsafe-inline`。
- Browser QA：1440x900 和 900x620 无 overflow、overlap 或 console error；窄窗口按规则隐藏 inspector。

## Package 与进程证据

| Evidence | Value |
|---|---:|
| Package files | 76 |
| Unpacked package | 437,851,431 bytes |
| `app.asar` | 2,175,389 bytes |
| Desktop executable | 222,753,280 bytes |
| AppHost executable | 75,854,929 bytes |
| AppHost SHA256 | `58B62825415991277789DC526760D8F2976D04E9035A55C270977EBCCF7EF577` |
| 3-second process count | 6 |
| 3-second working set | 406,470,656 bytes |
| 3-second private bytes | 242,315,264 bytes |
| 6-second lifecycle sample | 8,017 ms |
| Orphan AppHost delta | 0 |

早期 package spike 曾把 `.electron-cache` 错误收入 `app.asar`，导致 asar 287.1 MB、目录 722.8 MB。packager ignore policy 修正后 asar 降至 2.17 MB；缓存、源码、Node modules 和 dev dependencies 不再进入 app payload。

## Gate 结论

| Gate | 状态 | 证据 |
|---|---|---|
| G0 Source Baseline | Passed | 起点与 dirty state 已记录；0.5.0 acceptance、0.6.0 framework/schedule 和 Week 66 文档/实现由本提交统一固化。 |
| Application extraction | Passed | structured workspace use case 复用 Core guard/error；Application 无 CLI/Electron 依赖。 |
| Contract/framing | Passed | 单一 source、双端 generation、8 KiB/1 MiB bounds、partial/invalid/oversize/corrupt tests。 |
| AppHost lifecycle | Passed | real process initialize/workspace/shutdown、crash、packaged resource path、orphan delta 0。 |
| Desktop security | Passed | isolated/sandboxed Renderer、3-operation allowlist、navigation deny、production CSP check。 |
| Dependency/license | Passed | exact lock、0 audit vulnerability、registry-only、295-package notice inventory。 |
| Test determinism | Passed | 标准并发 full suite 连续两次 1269/1269。 |
| Performance method | Passed | package/process/memory/lifecycle sample 命令和 comparison baseline 已记录。 |

## 风险与限制

- CLI 尚未调用 Application service；Week 66 只证明 extraction path，Week 67 必须加入真实 CLI/Application parity slice。
- 当前 UI 仅是 secure workspace shell，不包含 thread/timeline/chat/approval/terminal 或 artifact review。
- Electron unpacked foundation 为 437.9 MB、3 秒 working set 406.5 MB，成本显著；后续按相同方法连续采样。
- Electron 43.1.1 的官方二进制在当前网络下载超时，当前冻结 41.1.0。升级必须重新通过 Node engine、checksum、security、package 和 smoke Gate。
- `@electron/packager` 依赖树产生一个 deprecated `boolean@3.2.0` 安装警告；npm audit 为 0，但 Week 76 dependency review 必须再次评估。

## Week 67 输入

- 只读 workspace Application slice 与责任矩阵。
- desktop-v1 contract source、生成器和 protocol limits。
- secure Electron Main/Preload、真实 AppHost lifecycle 和 packaged smoke。
- dependency lock、notice/audit、CSP 和 package inventory Gate。
- 标准并发 `1269/1269` 测试基线。

本提交完成后重新检查工作区状态；若无新增改动，Week 67 可直接使用上述稳定输入。

## 窗口关闭回归修复

2026-07-16 在人工关闭 packaged Desktop 窗口时复现 Electron main-process `Object has been destroyed`：BrowserWindow 已销毁，但 AppHost exit handler 仍向旧 `webContents` 发送 runtime status。

修复与证据：

- runtime status 发送前同时检查 BrowserWindow 与 webContents 的 `isDestroyed()`。
- BrowserWindow `closed` 事件立即清空 `mainWindow` 引用。
- ready-to-show、workspace dialog 和自动关闭入口复用同一 live-window guard。
- 新增 destroyed BrowserWindow / destroyed webContents 单元测试。
- Desktop tests 更新为 5 files / 11 passed。
- packaged `headless` 与 `window-close` smoke 均 exit 0，AppHost orphan delta 均为 0。
