# Week 76 执行计划：Security、Accessibility、Performance 与 Release Candidate

**Goal:** 在 Week 75 已通过故障恢复、长会话和 packaged Desktop E2E Gate 的基础上，对 0.6.0 Desktop 做发布前的安全、可访问性与性能收口，并从干净 source revision 生成首个可追溯 Release Candidate。Week 76 不新增对话或工具能力；本周要证明 Renderer/Main/Preload/AppHost 的信任边界没有旁路，键盘与辅助技术可完成关键用户闭环，Week 75 暴露的 reload working-set 增长得到修复或形成明确 Blocked 决定，且 candidate 的 payload、身份、notices、checksums 和默认 smoke 都可审计。
**Architecture:** 保持 Application/Store 为业务事实源、AppHost 为 workspace/policy/process authority、Main 为窗口与 AppHost 生命周期 owner、Preload 为冻结的 exact bridge、Renderer 为可丢弃 projection。安全审计从 packaged payload 反向验证源码约束；可访问性修复只调整语义、焦点与视觉表现，不复制业务状态机；性能观测使用独立进程树、renderer 指标与确定性 workload，不以扩大 timeout、取消边界或隐藏数据替代修复。RC 构建从 clean Git revision 生成 manifest 与全量 SHA256 inventory，默认 smoke 继续使用 deterministic fake runtime。
**Tech Stack:** .NET SDK `9.0.308`、`desktop-v1` framed JSON-RPC over stdio、Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`、Windows process/CIM performance sampling、PowerShell release evidence scripts。优先复用 Testing Library、Playwright ARIA snapshot、浏览器 media emulation 与现有 package/process harness；若必须新增测试依赖，必须 exact lock、更新 notices，并纳入 dependency/security Gate。

---

状态：Blocked（自动化收口已完成；performance stability、Narrator 与 clean-source RC Gate 未关闭）

创建日期：2026-07-18

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`d564d9920224f45e1987a0aa239904cabca731c6`

前置事实：

- Week 75 已提交：`d564d99 完成 Week75 故障恢复与长会话 E2E`，其 review 状态为 Passed。
- Week 75 clean rerun 已通过 `.NET 1390/1390`、Desktop `19 files / 89 tests`、unpacked `8/8`、packaged `7/7`，Playwright `retries=0`。
- `desktop-v1` reviewed surface 仍为 exact `39 invoke + 2 event`；Week 76 默认不修改 contract surface。
- Week 66 同口径 foundation baseline：package `437,851,431` bytes、`app.asar 2,175,389` bytes、working set `406,470,656` bytes、private bytes `242,315,264` bytes、6 个进程、AppHost orphan delta 0。
- Week 75 长会话记录 5 次 reload 从 `375,568 KiB` 增至 `459,108 KiB`（约 `+22.2%`），terminal/diff 后峰值 `512,800 KiB`；进程退出后归零，但会话内趋势超过 15% 观察线，因此尚不是 performance pass。
- 当前 production security scan 已检查 CSP、Renderer/Preload 禁止 surface 与 reviewed channels；本周需要把检查扩展到实际 `app.asar`/package inventory、运行期 navigation/permission/download 与 adversarial protocol/path/diagnostic 行为。
- Real model、real MCP、real Gerbv/ImageMagick/LibTIFF correctness 仍为显式 opt-in，不属于默认 RC Gate，也不得由 fake-runtime smoke 外推。

执行约束：

- 本文件只创建 Week 76 实施蓝图，不在本次任务中实现功能或生成 RC。
- 后续执行默认在当前分支原地开发；除非用户另行要求，不创建新 worktree 或新分支。
- 不覆盖用户已有无关改动；执行前记录 `git status --short`、HEAD、SDK/Node/npm/Electron 版本和 package baseline。
- 不以增加产品 timeout、E2E retry、固定长 `sleep`、关闭 sandbox/CSP 或减少 workload 数据掩盖性能与稳定性问题。
- 安全、可访问性和性能失败必须保留原始证据；诊断重跑不能把首轮失败计为稳定 Passed。
- RC 必须来自 clean source revision；dirty build 只能标记 validation/non-acceptance，不得命名或记录为 release candidate。
- 只有全部 Critical Gate 通过后，plan、review、schedule 才能同步标记 Passed；任一高风险安全问题或性能硬 Gate 未关闭时状态为 Blocked。

## 本周目标

1. 完成 Renderer/Main/Preload/AppHost 信任边界 review，证明 production package 没有 generic IPC、任意文件/进程/网络能力、test hook、remote content 或未审核 bridge surface。
2. 覆盖 CSP、navigation/new-window、permission/download、external URL、drag/drop、clipboard/path、protocol fuzz、secret redaction 与 crash diagnostics 的运行期和 package 级回归。
3. 审计 approval/cancel 的最多 4 次 conflict reconciliation，证明 retry 始终复用 mutation/request identity，只做权威对账，不重放 turn/tool/write。
4. 让 workspace/thread/timeline/composer/mention/approval/recovery/review/terminal 的主要闭环可用键盘完成，并补齐焦点恢复、ARIA 关系、live region、screen-reader snapshot 与错误语义。
5. 对默认主题做 WCAG AA 对比度检查，验证 reduced motion、forced colors、200% zoom、`760x560` 支持下限和更窄 renderer stress layout。
6. 把 Week 75 长会话测量拆成 Electron Main、Renderer、GPU/utility 与 AppHost 进程指标，记录 cold start、idle 回落、reload retention、long-session peak、listener/request count 和 protocol throughput。
7. 生成首个 `0.6.0-rc.1` Desktop candidate，携带 clean source identity、runtime/protocol/contract 版本、payload inventory、第三方 notices、AppHost SHA256、package/archive checksums 和默认 packaged smoke 证据。

## 稳定输入与待关闭差距

### 已有可复用能力

- `createWebPreferences()` 已固定 `contextIsolation=true`、`nodeIntegration=false`、`sandbox=true`、`webSecurity=true`、`webviewTag=false`、packaged devtools disabled。
- `applyNavigationPolicy()`、session permission/download policy、strict CSP、production bundle scan 与 exact Preload channel inventory 已存在。
- AppHost framing 已有 partial header/body、oversize、malformed/unknown method、disconnect 与 cleanup 测试基础。
- Renderer 已有基本 `aria-label`、`aria-live`、alert/status/tab/listbox 语义、`:focus-visible` 与 `prefers-reduced-motion` 基线。
- Week 75 `desktop-harness.ts` 已提供 case 隔离、owned process tree、temp/profile/sentinel inventory 和 unpacked/packaged 双模式入口。
- `Measure-DesktopBaseline.ps1`、`Invoke-DesktopSmoke.ps1`、`Publish-AppHost.ps1`、package/notices/security scripts 可作为 Week 76 证据脚本起点。

### 本周必须关闭的差距

- 当前 production scan 主要检查构建目录字符串，尚未形成解包后 `app.asar` 与完整 package allowlist/denylist inventory。
- navigation/permission/download policy 有静态测试，但缺少 packaged adversarial E2E；external URL 当前应保持全拒绝，不增加 `shell.openExternal` bridge。
- drag/drop 与 clipboard path 未形成端到端拒绝/重验测试；Renderer 不得把 OS 路径直接变成 AppHost 请求。
- protocol fuzz、crash diagnostics 与 UI/store/report/trace 的跨表面 secret/path sentinel 还没有统一泄漏清单。
- mention listbox、tabs、drawers、rename/archive confirmation、approval/recovery 和 terminal 的完整键盘顺序、Escape 行为与焦点返还尚未系统验证。
- reduced motion 只覆盖 spinner；forced colors、contrast、zoom 和窄窗口的自动证据不足。
- Week 75 只有聚合 working-set 采样，不能判断增长来自 Renderer reload retention、V8/Electron warm-up、GPU/utility、AppHost 或测试插桩。
- Desktop 尚无 source-bound RC manifest、全量 payload SHA256 inventory、独立 checksums 和 candidate smoke 汇总。

## 范围冻结

### 本周包含

- 安全代码/配置/依赖/package review 及必要修复。
- 仅为安全、可访问性、性能和 RC 证据所需的测试与 bounded diagnostics。
- Renderer 语义、键盘、焦点、颜色、动画和响应式布局修复。
- 不扩张 production bridge 的性能采样/测试 harness；test-only 能力不得进入 `app.asar` 或 Renderer global surface。
- Desktop RC build/inventory/checksum/smoke scripts，以及对应脚本测试和 Week 76 review。
- 全量 CLI/.NET/Desktop regression，确保 hardening 不破坏 Week 73-75 write/recovery 闭环。

### 本周不包含

- 不把 Desktop fake runtime 替换为真实 OpenAI/Agent runtime。
- 不新增 drag/drop import、clipboard attachment、external-browser navigation、auto-update、installer 或签名能力。
- 不新增 `desktop-v1` 业务 method/event、generic IPC、localhost control API 或 Renderer Node 权限。
- 不更换 Electron/React/TypeScript 主版本，不做非必要依赖升级。
- 不声称真实 Gerber/TIFF 工具正确性、真实模型质量或 MCP 互操作通过。
- 不做 Week 77 的两次 clean-build 可复现性最终验收、0.6.0 Accepted 决定或正式发布文档收口。

## Security Review 与对抗矩阵

### 1. Trust-boundary inventory

形成一张可审计矩阵，逐项记录 owner、输入、验证点、输出、redaction、limit 与 failure mode：

| Boundary | 必查不变量 |
|---|---|
| Renderer -> Preload | 只能调用 frozen exact typed API；无 `ipcRenderer`、Node、fs、process、network、clipboard 或 generic query |
| Preload -> Main | channel exact allowlist；参数 runtime validation；未知 key/type/size fail-closed |
| Main -> AppHost | framed request 有 version/id/method/size/concurrency 上限；EOF/crash 不回显 payload |
| AppHost -> Application | workspace/path/policy/approval/revision 重新验证；不信任 Renderer projection |
| Application -> Store/Tools | atomic/versioned facts；write/terminal/external tool 保持原 owner 与 approval/cleanup 边界 |
| Package -> Host OS | 无 remote content、下载、任意 navigation、webview、test hook、source map、cache、secret 或非预期 native payload |

审计输出必须确认 `desktop-v1` 仍为 exact `39 invoke + 2 event`；若 surface 必须变化，先单独更新 schema、生成器、双端 validator、security inventory 和 framework decision，不能在 hardening 中隐式加入。

### 2. Browser/Electron policy

- CSP 同时检查生成 HTML 与 packaged `app.asar`：禁止 remote/localhost/ws、`unsafe-eval`、`unsafe-inline`、object/frame/base/form 旁路。
- packaged BrowserWindow 验证 isolation、sandbox、Node disabled、devtools disabled、webview disabled、drag navigation disabled。
- `will-navigate`、`window.open`、`will-attach-webview`、permission request/check 和 download 全部 fail-closed；测试 `http/https/file/javascript/data/custom-scheme`。
- 当前产品没有 external-link feature，因此所有 external URL 均拒绝；不得为了测试通过增加 `shell.openExternal`。
- 模拟 file drop、text/HTML clipboard 与绝对路径文本；不得触发 workspace/context/artifact/terminal 请求，也不得改变当前 workspace。文件/目录只能经现有系统 picker，再由 AppHost guard 重验。

### 3. Protocol、mutation 与 diagnostics

- framing fuzz 覆盖空/重复/负数/溢出/非十进制 `Content-Length`、header/body 截断、无效 UTF-8/JSON、深层/超宽对象、未知 method/version/id、oversize、notification flood 与并发上限。
- 失败响应只返回 stable bounded error，不包含 raw frame、异常堆栈、环境变量、用户 profile、workspace absolute path 或 secret sentinel。
- approval/cancel conflict reconciliation 最多 4 次，所有尝试复用同一 mutation identity；approval 还必须复用并校验 request identity。测试在连续 timeline commit 下 operation count 仍为 1，deny/cancel 不产生 completed/write replay。
- 在 API key、环境值、异常文本、workspace/profile path、terminal stderr 与 fake external-tool diagnostics 中放置不同 sentinel，检查 Renderer、timeline、store、report、stderr、crash log、Playwright trace/screenshot metadata 和 RC evidence；允许的用户可见相对路径与禁止的机器绝对路径必须分别列出。
- dependency audit 记录 lockfile source、exact versions、production/dev dependency 区分、`npm audit` 结果、许可证集合、Electron runtime licenses 与 AppHost/NuGet redistribution notices。网络 advisory 查询无法完成时不得伪装为通过，应记录 Blocked 或补具同 revision 的可信缓存证据。

## Accessibility 与 Visual QA

### 1. 键盘与焦点契约

每条路径只用键盘执行，并断言操作后 focus owner：

1. 打开 workspace fixture，切换 thread，创建、重命名、取消重命名和 archive confirm/cancel。
2. 进入 Composer，输入多行文本，打开 `@` mention，以方向键/Home/End/Enter/Escape 选择或关闭，并回到 textarea。
3. Queue/Start turn，抵达 approval 后在 Approve/Deny 间导航；mutation 完成后焦点回到稳定 task/composer target，不能落到卸载节点。
4. 执行 cancel、recovery resume/restart，关闭/重开左右 drawer，焦点返回触发按钮。
5. 用方向键切换 Changes/Reports/Artifacts/Gerber tabs，tabpanel 与 tab 使用稳定 id、`aria-controls`/`aria-labelledby` 和 roving tab stop。
6. 打开 Terminal，输入、发送、读取 output、cancel/close；输出区域可聚焦但不劫持 composer shortcut。

Dialog/listbox/tab/drawer 必须有 Escape 与 focus restoration 规则；不可只依赖 hover 显示动作；disabled 控件要有可感知原因或邻近说明。不得添加抢焦点的全局 effect，也不得让 notification/resync 重置用户当前 focus。

### 2. Screen reader 与 live regions

- 使用 Playwright ARIA snapshot/role assertions 固化 shell、thread list、timeline、composer、approval、review tabs、terminal 和 error/recovery 状态的可访问树。
- icon-only button 必须有稳定 accessible name；装饰图标 `aria-hidden`；动态计数/状态不能只靠颜色。
- streaming/refresh/terminal 不逐 token 或逐 chunk 播报；只在 bounded phase/terminal-state 变化时通过 `status`/`alert` 通知。
- listbox option 暴露 active/selected 状态；tab/tabpanel、input/help/error、confirmation trigger/dialog 建立完整关系。
- 记录一次 Windows Narrator 人工 smoke：启动、thread/composer、approval、review、error/recovery；人工结果不能替代自动回归，但必须进入 Week 76 review。

### 3. Contrast、motion 与 responsive

- 对默认主题的正文、次要文字、状态 chip、按钮、focus indicator、error/warning/success、terminal 和 disabled states 计算对比度；正文至少 `4.5:1`，大字至少 `3:1`，UI 边界/focus indicator 至少 `3:1`。
- `prefers-reduced-motion: reduce` 下所有非必要 animation/transition 停止；spinner 以静态状态文字保持含义。
- `forced-colors: active` 下焦点、选中、错误、审批和 drawer 边界仍可辨识，不以背景色作为唯一状态。
- packaged capture 覆盖 `1920x1080`、`1440x900`、`1280x720`、支持下限 `760x560`；renderer stress 覆盖 200% zoom 及 `520x480`/`320x480`，后两者只用于布局韧性，不扩大 packaged 最小窗口支持声明。
- 每个尺寸断言无横向页面溢出、无主操作不可达、drawer/approval/composer/terminal 不互相遮挡；视觉截图只保存在 ignored evidence 目录。

## Performance Investigation 与预算

### 1. 测量方法

- 扩展 `Measure-DesktopBaseline.ps1` 或新增单一 Week 76 measurement orchestrator，输出 versioned JSON，不从人类日志反向解析结果。
- 每个场景从新 profile/workspace 启动 packaged app，记录 source/package identity、机器/OS、采样时间、main/renderer/GPU/utility/AppHost PID、working set/private bytes；测试关闭后 owned process/temp delta 必须为 0。
- cold start 从 process start 到 `ready-to-show`、AppHost handshake 和 `runtime-ready` 分段记录；至少 5 次，报告每次、median 与 max，不删除 warm/cold outlier。
- idle memory 在 runtime-ready 后按固定间隔采样 30 秒；long-session 使用 Week 75 同一 240 timeline/50 diff/70 KiB terminal workload，并在每次 reload、workload 后及额外 30 秒 idle 回落期采样。
- listener/request count 只通过 test harness、Electron/Playwright 可观测对象或已有 client counters 收集；不得加入 Renderer 可调用的 production diagnostics bridge。
- protocol benchmark 使用 bounded in-memory/real AppHost framing fixture，固定 frame 数、payload 分布和并发度，记录 duration/throughput/error/backpressure 与 peak memory；它是 regression evidence，不得绕过生产 frame/concurrency limit。

### 2. 判定规则

| 指标 | Week 76 Gate |
|---|---|
| Package total / `app.asar` / AppHost | 与 Week 66 同脚本同口径比较；任一增长 `>15%` 必须解释并修复，否则 Blocked |
| Process lifecycle | 正常、window-close、crash-restart、E2E 终态 owned process/temp delta 均为 0 |
| Reload retention | 排除第一次 warm-up 后，5 次 reload 的同进程角色 working set/private bytes 增长不得继续超过 15%；listener/request 数回到稳定基线 |
| Idle recovery | workload 后 30 秒记录回落；若仍高于稳定 warm baseline 15% 且可重复，必须定位并修复，否则 Blocked |
| Long-session peak | 必须记录分进程来源；不得通过降低 240/50/70 KiB workload、关闭功能或放宽 truncation limit 达标 |
| Cold start | 5 次均在现有 30 秒 smoke deadline 内达到 runtime-ready；记录 median/max，Week 76 先建立分段基线，不虚构 Week 66 同口径比较 |
| Protocol throughput | 固定 benchmark 三次无 frame loss、无 unbounded queue、无 limit bypass；记录全部结果及变异，不以单次最好值作为 Gate |

若 `+22.2%` 被证明是可重复的 warm-up 而非 retention，必须以分 PID、idle 回落、listener/request stability 和至少两轮新 profile 证据支持；只写“Electron 正常增长”不构成关闭。forced GC/`--expose-gc` 只能用于诊断归因，不能作为 release candidate 的通过条件。

## Release Candidate 证据契约

新增或扩展脚本生成 ignored 目录 `artifacts/desktop-rc/0.6.0-rc.1/`，至少包含：

- `release-manifest.json`：schema/candidate id、`0.6.0` product version、clean 40-char source revision、sourceDirty=false、build timestamp、OS/arch、.NET SDK/runtime、Node/npm/Electron/Chromium、AppHost、`desktop-v1` 与 contract SHA256。
- `payload-inventory.json`：以 package-root-relative `/` 路径排序，记录每个 regular file 的 size/SHA256；拒绝 reparse/symlink、重复规范化路径与 package-root escape。
- `checksums.json`：Desktop exe、`app.asar`、AppHost exe、notices/licenses、完整 candidate archive 的 size/SHA256，并与 inventory 交叉一致。
- `security-review.json`、`accessibility-review.json`、`performance.json`、`smoke.json`：记录命令、起止时间、source/package identity、每个 case 状态和 orphan/temp inventory；不包含 secret 或机器绝对 workspace/profile path。
- `THIRD_PARTY_NOTICES.md`、Electron `LICENSE`/`LICENSES.chromium.html` 和 AppHost/NuGet redistribution evidence；缺失任一 required notice 时 candidate Gate 失败。

Package denylist 至少包括源码、source map、tests/E2E、Playwright artifacts、cache、`.git`、`node_modules`、dev scripts/config、PDB、用户配置/API key、临时 workspace/profile 与 Week 76 test hook。允许的 runtime payload 必须在 review 中按目录解释。RC 脚本必须 fail-fast、默认拒绝 dirty source、拒绝覆盖已存在 candidate 目录，并对所有 resolved path 做 root containment 检查。

`0.6.0-rc.1` 是 Week 77 的输入，不是正式 Accepted release。默认 packaged smoke 必须在清空模型凭据、禁网假设、无真实 Gerbv/ImageMagick/MCP 的环境下完成 initialize -> workspace -> thread -> fake turn -> approval -> final -> review -> shutdown，并记录 AppHost SHA256 与 process/temp delta 0。

## 测试计划

### .NET / AppHost

- approval/cancel mutation/request identity、4 次 bounded reconciliation、no replay/no duplicate write 回归。
- framing/parser fuzz、invalid UTF-8/JSON/depth/width/size、unknown method/version、partial EOF、flood/backpressure/concurrency limit。
- diagnostics/redaction sentinel 穿过 initialize/workspace/turn/terminal/external-tool failure 与 crash 路径，不泄露 raw payload、secret、profile/workspace absolute path。
- RC PowerShell 脚本的 clean/dirty source、path containment、symlink/reparse、inventory sorting/hash/checksum、overwrite refusal 和 secret-free manifest 测试。
- AppHost Debug/Release build 维持 0 warnings / 0 errors；full suite 不降低既有 timeout/cleanup 边界。

### Electron Main / Preload / Renderer

- production preferences、session policy、navigation/new-window/webview/permission/download 全拒绝测试。
- bridge frozen/exact surface、unknown key/type/oversize payload、unsubscribe/listener cleanup 与 packaged source scan。
- drop/clipboard/custom URL 不触发 bridge；system picker 结果仍只走 existing context/workspace method 并由 AppHost 重验。
- keyboard/focus/ARIA snapshot/live-region tests 覆盖主要闭环；notification/resync/re-render 不丢焦点。
- contrast token tests、reduced-motion/forced-colors computed behavior、200% zoom 与窄 viewport overflow tests。
- security scan 对 `dist` 与实际 `app.asar`/package 双重执行；package inventory denylist 有自动断言。

### E2E / Packaged / RC

- 新增 security adversarial、accessibility keyboard/ARIA、visual viewport、performance sampling suites；`retries=0`、single worker 用于共享性能证据。
- 继续运行 Week 75 unpacked `8/8` 与 packaged `7/7` recovery/long-session/read-only 矩阵，防止 hardening 回归。
- packaged accessibility 路径至少覆盖 thread/composer/mention/approval/review/terminal/recovery；运行期 security 覆盖 navigation/permission/download/drop/clipboard/protocol/crash redaction。
- RC default smoke、window-close、crash-restart 和 performance workload 都检查 owned descendant/temp cleanup，不使用全局按名称 kill 伪造终态。
- 对 candidate inventory、manifest、checksums、notices 与 smoke identity 做最终交叉校验，保证所有 evidence 指向同一 source revision 和 AppHost hash。

## Critical Gate

Week 76 只能标记 Passed，当且仅当：

- [x] Renderer/Main/Preload/AppHost trust-boundary review 完成，高风险安全问题为 0，exact bridge 仍为 `39 invoke + 2 event` 或所有显式变更已完整 review。
- [x] CSP/navigation/new-window/webview/permission/download/drop/clipboard/path/protocol fuzz/secret/crash diagnostics 自动化通过，package 无 test hook 或禁止 payload。
- [x] approval/cancel reconciliation 证明 identity stable、最多 4 次、operation/write count 为 1、无 replay/bypass。
- [ ] 关键用户闭环可只用键盘完成，focus restoration、ARIA snapshot、live region、Narrator smoke 有证据。
- [x] 默认主题 WCAG AA、focus indicator、reduced motion、forced colors、200% zoom 与四个 packaged viewport/两个 renderer stress viewport 通过。
- [ ] 分 PID performance evidence 关闭 Week 75 `+22.2%`：在冻结 workload 下修复超 15% retention，或 Week 76 明确标记 Blocked；不得以解释替代硬 Gate。
- [x] package/`app.asar`/AppHost 同口径预算、cold start、idle recovery、protocol throughput 和 process/temp cleanup 均有 versioned JSON 证据。
- [x] dependency audit/notices 完整，无 high/critical vulnerability、未知许可证或缺失 redistribution notice。
- [ ] clean-source `0.6.0-rc.1` manifest、inventory、checksums、AppHost SHA256、archive 和默认 packaged smoke 指向同一 revision，且 smoke 不依赖凭据、网络或真实外部工具。
- [ ] `.NET full suite`、AppHost Debug/Release、Desktop verify、unpacked/packaged E2E、RC smoke 与 `git diff --check` 完整 clean rerun 通过。
- [x] 创建 `76_week_review.md`，记录首次失败、修复、命令/通过数/耗时、package/performance 数值、Skipped/Unproven 和 Week 77 稳定输入。

## 任务拆分

1. **冻结 baseline 与 evidence schema**
   - 记录 HEAD、cleanliness、toolchain、Week 66/75 数值和现有 package identity。
   - 定义 security/accessibility/performance/smoke JSON schema 与统一 source/package identity 字段。
   - 为 measurement/RC scripts 先写 path、dirty-source、hash/inventory、secret-free 输出测试。
2. **完成 security inventory 与 adversarial tests**
   - 审计 trust-boundary、exact bridge、BrowserWindow/session/CSP 与 package payload。
   - 补 navigation/permission/download/drop/clipboard/custom scheme 和 protocol fuzz。
   - 补 cross-surface secret/path/crash diagnostic sentinel 与 approval/cancel no-replay 测试。
3. **完成 accessibility semantics 与 keyboard flow**
   - 修复 listbox/tabs/dialog/drawer/input/error/live-region 关系和 focus restoration。
   - 增加 Testing Library + Playwright keyboard/ARIA snapshot tests。
   - 执行 Narrator smoke，记录自动证据不能覆盖的人工观察。
4. **完成 contrast、motion 与 responsive QA**
   - 盘点颜色 token并修复 AA 不合格项；补 focus/forced-colors/reduced-motion。
   - 自动捕获四个 packaged viewport 与 zoom/narrow stress，断言 overflow/遮挡/操作可达。
5. **定位并关闭 Week 75 memory trend**
   - 分 PID、idle/reload/workload 阶段采样，加入 listener/request counter 观测。
   - 先用定向测试定位 retention，再做最小修复；保留修复前后同 workload JSON。
   - 若仍超 15%，停止 RC Passed 声明并把 Week 76 标为 Blocked。
6. **建立 protocol/cold-start/package 基线**
   - 固定 frame workload，三轮记录吞吐、peak 与 backpressure。
   - 五轮新 profile cold start 分段采样；复测 package/process 预算。
7. **生成并验证 `0.6.0-rc.1`**
   - 从 clean revision build/package，生成 manifest/inventory/checksums/notices/archive。
   - 执行默认、window-close、crash-restart、security/accessibility/performance packaged smoke。
   - 交叉核对 source revision、contract hash、AppHost hash、payload 与 evidence identity。
8. **完整 Gate 与文档收口**
   - 从干净 build/test output 完整跑 .NET、Desktop verify、双 E2E 与 RC smoke。
   - 创建 `76_week_review.md`，同步 plan/schedule 状态；未过硬 Gate 时明确 Blocked，不提前进入 Week 77 acceptance。

## 建议验证命令

执行阶段可按实际新增脚本名收口，但应保持单一等价入口，避免多套 smoke 漂移：

```powershell
git status --short
dotnet --version
node --version
npm --version

dotnet test C-AICLI.sln -c Debug --no-restore
dotnet build src/CSharpAiCli.AppHost/CSharpAiCli.AppHost.csproj -c Debug --no-restore
dotnet build src/CSharpAiCli.AppHost/CSharpAiCli.AppHost.csproj -c Release --no-restore

Push-Location apps/desktop
npm ci
npm audit
npm run verify
npm run package:dir
npm run test:e2e:unpacked
npm run test:e2e:packaged
Pop-Location

powershell -NoProfile -ExecutionPolicy Bypass -File tools/Measure-DesktopBaseline.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-DesktopReleaseCandidate.ps1 -CandidateId 0.6.0-rc.1
git diff --check
```

Week 76 实施时应为 security/accessibility/performance/RC smoke 增加明确的 npm/PowerShell 入口，并在 review 中记录最终真实命令、case 数、失败数和耗时。`npm audit` 的 registry/network 条件必须记录；不能把未执行写成 0 vulnerability。

## 风险与处置

| 风险 | 处置 |
|---|---|
| 为测性能增加 production diagnostics bridge | 只用 harness/process/CDP/已有 client counter；package scan 禁止 test hook 与新 Renderer surface |
| forced GC 或延长 idle 掩盖 retention | GC 仅诊断；Gate 使用正常 packaged runtime、固定 30 秒 idle 与冻结 workload |
| 可访问性修复触发重复提交 | 语义/focus 留在 Renderer；Application/AppHost 仍以 request/revision identity 防重 |
| live region 播报流式事件风暴 | 只播报 bounded phase/terminal transitions，不逐 token/chunk announcement |
| “支持窄窗口”扩大产品声明 | packaged 支持下限仍为 `760x560`；更窄尺寸只计 renderer stress evidence |
| external link 测试诱导新增 shell bridge | 当前全部拒绝；真实 external-link feature 延后并需独立 threat model |
| package inventory 含构建机绝对路径或 secret | manifest 只存相对路径与 allowlisted环境信息；sentinel scan 与 schema tests 双重阻断 |
| npm advisory 网络失败被误记为安全通过 | 记录 Blocked 或提供同 lock revision 的可信缓存证据，不写虚假 0 vulnerability |
| Week 75 recovery E2E 因 hardening 回归 | 双模式矩阵完整重跑、retries 0；先修产品/测试确定性，不降低 Gate |
| RC 被误当正式 0.6.0 Accepted | manifest 明确 candidate/non-acceptance；Week 77 才做双构建与最终发布决定 |

## Week 77 交付输入

- clean-source `0.6.0-rc.1` package/archive、manifest、payload inventory、checksums、notices 与 source/AppHost/contract identity。
- security review 高风险问题为 0，adversarial protocol/path/secret/crash/no-replay tests 稳定通过。
- keyboard/ARIA/Narrator/contrast/reduced-motion/forced-colors/viewport 的自动与人工证据。
- cold start、idle、reload、long-session、protocol throughput、package size 与 cleanup 的同口径 JSON baseline；Week 75 `+22.2%` 已关闭，或 Week 76 明确 Blocked 且不得进入 acceptance。
- Week 73-75 write/approval/cancel/recovery/terminal/artifact/Gerber review 双模式 E2E 未回归。
- 明确的 Accepted/Preview/Deferred/Skipped/Unproven 清单；real model、real MCP 与 real Gerber/ImageMagick 仍保持 opt-in 边界。
