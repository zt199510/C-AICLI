# 第 70 周回顾：Secure Electron Shell

状态：Passed；实现、Desktop/.NET/CLI 回归、packaged smoke、视觉复核与 package/process Gate 全部通过

更新时间：2026-07-17

## 完成范围

- 冻结 4-channel `DesktopBridge`：`runtime:get-status`、`runtime:restart`、`workspace:open`、`runtime:status`；Preload 返回 frozen projection，不暴露 raw `invoke/send/on`、Node、path、shell 或 process surface。
- `RuntimeStatus` 固定 schema/state/code/message/canRestart/protocolVersion 组合，拒绝 unknown member 与超过 256 UTF-8 bytes 的 message；Renderer 只显示 stable safe message。
- `AppHostClient` 对 Content-Length、reviewed Content-Type、header/body bound、partial EOF、fatal UTF-8、JSON、JSON-RPC root/id/result/error 与 generated method result validator fail closed；unknown/duplicate response id 会终止 owned child 并清理 pending。
- initialize 严格验证 `desktop-v1`、contract SHA256、三项 negotiated capability、generated limits 与 security summary；Main 只使用 initialize/workspace.open/shutdown。
- 新增 single-child runtime supervisor，覆盖 generation isolation、single operation、unexpected exit、explicit restart、quit priority、bounded stop 与非 ready workspace guard；不自动 restart 或 reopen workspace。
- Main 拆分为 composition root、window factory、security policy、runtime supervisor 与 IPC registry；workspace path 只由 system dialog 进入 AppHost，Renderer 只得到 generated validator 验证后的 canonical result。
- BrowserWindow 固定 context isolation、sandbox、no Node、no webview、no drag navigation、packaged no DevTools；permission/check/download/webview/new-window/navigation 默认 deny，开发 URL 只接受 exact `http://127.0.0.1:5173/`。
- Renderer 完成 runtime starting/ready/failed/restarting、workspace picker、显式 restart、左侧与 inspector controls；1440 三栏、1120 inspector drawer、760 双 drawer 默认关闭且互斥。
- production security scan 验证 CSP、Renderer Node/raw IPC surface、Preload exact channel 与 Week 71-73 business method absence；Renderer build 清理旧 hash chunks，避免 stale bundle 进入 `app.asar`。

## Source 与环境

- Branch：`week-02-cli-commands-doctor-config`，按用户要求原地开发，未创建 worktree 或新分支。
- Week 70 起点：`3473edc450f644ff0d89541e33eb032aac1c5ed8`；实现与性能修复 source：`8b35f440736614b28a184882dee165f1e19a6b17`。
- .NET SDK：`9.0.308`；Node/npm：`v22.13.0` / `11.7.0`。
- 新增 exact devDependencies：`@testing-library/react@16.3.0`、`@testing-library/user-event@14.6.1`、`jsdom@26.1.0`；npm audit 为 0 vulnerabilities。
- notices 首次发现 `@csstools/color-helpers@5.1.0` 的 MIT-0，核对包内许可证文本后加入显式 allowlist；notice drift check 通过。

## Bridge 与 security matrix

| 边界 | 结论 | 自动化证据 |
|---|---|---|
| Main transport | strict frame/UTF-8/envelope/id/generated result、fatal cleanup | `apphost-client.test.ts`、真实 `apphost-process.test.ts` |
| Runtime | single client/operation、generation、explicit restart、bounded stop | `apphost-runtime.test.ts`、crash-restart smoke |
| IPC/Preload | 3 invoke + 1 event、sender/arg/result validation、frozen bridge | `ipc-bridge.test.ts`、`bridge.test.ts` |
| Browser | sandbox/no Node/no webview、permission/download/navigation deny | `security.test.ts`、`window.test.ts`、production scan |
| Workspace | Main dialog path -> AppHost -> canonical validated result | IPC unit tests、real AppHost integration |
| Renderer | stable failure UX、restart、workspace、responsive drawers | `App.test.tsx`、三张 packaged capture |

## 自动化测试

- Desktop clean install：291 packages；audit `0 vulnerabilities`。
- Desktop verify：11 files / `57 passed / 0 failed`；contract/notices、typecheck、ESLint、Main/Preload/Renderer build 与 production security 全部通过。
- .NET Release：7 projects，`0 warnings / 0 errors`；full suite `1365 passed / 0 failed / 0 skipped`，约 75 秒。
- CLI dirty-source `Build-Release.ps1 -AllowDirtySource` 与 `Invoke-SmokeTests.ps1` 通过；真实 Gerber/TIFF、daemon/API 与 real-model smoke 按脚本约定未显式启用。
- packaged headless、window-close、crash-restart、capture-shell 全部 exit 0；每条路径 Desktop/AppHost orphan delta 均为 `0 / 0`。

首次 `npm ci` 因 Electron postinstall 未使用已有缓存而在 5 分钟上限超时；核对 `.electron-cache` zip SHA256 与 Electron checksums 后，使用官方 cache root 完成 clean install。首次 760×560 capture 显示双 drawer 同时遮挡中央区；新增窄屏初始关闭/互斥逻辑与 Renderer test 后重新 build/package，三尺寸复核通过。首次 baseline 还发现 Vite 保留旧 hash chunks 导致 `app.asar` 增长 69.7%；改为清理 `dist/renderer` 后重新验证并消除陈旧 payload。

## Package、process 与视觉

| Evidence | Week 69 | Week 70 | 变化 |
|---|---:|---:|---:|
| Package files | 77 | 77 | 0 |
| Unpacked package | 466,666,142 | 464,731,162 bytes | -0.41% |
| `app.asar` | 3,315,885 | 1,380,905 bytes | -58.36% |
| Desktop executable | 222,753,280 | 222,753,280 bytes | 0% |
| AppHost executable | 79,563,784 | 79,563,784 bytes | 0% |
| 3-second process count | 6 | 6 | 0% |
| Working set | 406,433,792 | 396,378,112 bytes | -2.47% |
| Private bytes | 241,147,904 | 230,088,704 bytes | -4.59% |
| Lifecycle sample | 7,244 | 7,065 ms | -2.47% |
| Headless/window-close/crash/capture orphan delta | 0 / 0 | 0 / 0 / 0 / 0 | unchanged |

视觉证据：`artifacts/week70-desktop-shell/shell-1440x900.png`、`shell-1120x720.png`、`shell-760x560.png`。PNG 尺寸与非零文件检查通过，人工复核无重叠、空白主视图或文字越界；截图不包含 raw workspace path、secret 或 fixture payload。

## 偏差与处置

- 计划要求的 `superpowers:subagent-driven-development` / `superpowers:executing-plans` skill 在当前会话不可用；采用逐 Task、逐 Gate 的本地执行方式，没有放宽验收。
- MIT-0 是新增 transitive test dependency 的许可证事实，已更新 notice generator allowlist 与 inventory；无 runtime dependency 新增。
- package/clean install 显式使用已存在且 checksum 匹配的 Electron zip；package 本身仍由正式脚本生成，不把 `.electron-cache` 纳入 payload。

## Week 71 稳定输入

- exact 4-channel frozen bridge、strict status snapshot/event 与 current-window sender validation。
- single-child AppHost runtime、explicit restart、bounded stop、stale generation isolation 与 exact `desktop-v1` handshake。
- strict BrowserWindow/session/CSP/navigation/permission/download/webview baseline。
- system picker -> AppHost workspace guard -> canonical `WorkspaceOpenResult` 路径。
- wide/medium/narrow shell、可折叠左侧、可切换 inspector 与 safe runtime/workspace failure states。
- Week 71 新增 thread/read-only bridge 时必须单独评审 `thread.changed` notification negotiation、validation、reconnect/resync 与 Renderer cache；不得扩展为 generic IPC。

## Clean acceptance

实现与验收文档提交 `1283a9134fe175e4e163b32b5c5072ca85aff72d` 后从 clean HEAD 执行：

- `Build-Release.ps1 -ReleaseAcceptance` 与 CLI smoke 通过；manifest/checksums 记录 `sourceRevision=1283a9134fe175e4e163b32b5c5072ca85aff72d`、`sourceDirty=false`、`releaseAcceptance=true`、`sdkVersion=9.0.308`。
- 使用 checksum 匹配的 Electron 41.1.0 cache 运行正式 `npm run package:dir`；Main/Preload/Renderer build、production security、AppHost publish 与 Electron packager 全部通过。
- 从该 clean source package 运行 headless、window-close、crash-restart，全部 exit 0，Desktop/AppHost orphan delta 均为 `0 / 0`。
- 本回顾封板提交后，再从最终 clean HEAD 运行相同 release acceptance、CLI smoke、Desktop package 与三条 packaged smoke，使最终 artifacts 绑定最终提交。
