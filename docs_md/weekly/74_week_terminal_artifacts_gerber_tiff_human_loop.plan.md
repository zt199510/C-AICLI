# Week 74 执行计划：Terminal、Artifacts 与 Gerber/TIFF 人工闭环

**Goal:** 在 Week 73 已完成的 Desktop write-capable turn、durable timeline、approval、cancel 与 recovery controls 基础上，交付 Desktop 的用户显式 Terminal、artifact metadata/preview/export 投影，以及 Gerber/TIFF verification preview 与人工 accept/reject 闭环。Week 74 的核心不是扩大 agent/tool 权限，而是把已经存在的 CLI/ProjectPack artifact 与 human-gate 能力安全投影到 Desktop：用户 terminal 必须与 agent command/tool timeline 保持身份隔离；preview 不能自动 accept；artifact 内容、绝对路径、外部工具和 destructive 操作必须继续由 AppHost/Application/Core 边界控制。
**Architecture:** 延续 `desktop-v1` reviewed contract 与 Main/Preload exact bridge 模式，不引入 generic IPC、raw terminal shell bridge、raw artifact bytes stream 或 Renderer 侧文件权限。AppHost 作为唯一 workspace authority 管理 terminal process/session、artifact lookup/export intent、Gerber/TIFF preview/human decision use case；Renderer 只显示 UI-safe projection 并发送显式用户动作。Terminal、Artifact、Gerber/TIFF 三条能力都必须落到 Application 层可测试 use case，复用现有 ProjectPack、ManagedArtifact、TiffVerification、Job/Report/Artifact stores 和 Week 73 timeline resync。
**Tech Stack:** .NET SDK `9.0.308` 目标、Core/Application/AppHost versioned stores、`desktop-v1` framed JSON-RPC over stdio、Electron `41.1.0`、Node `22.13.0`、npm `11.7.0`、React `19.2.7`、TypeScript `6.0.3`、Vite `8.1.4`、Vitest `4.1.10`、Playwright `1.61.1`。Terminal UI 可采用 xterm.js 但必须先进入 dependency/security/notices gate；若未引入 xterm.js，则交付 bounded plain terminal panel 的最小可验收实现并在 review 中说明。

---

状态：Passed

创建日期：2026-07-18

所属排期：`66_77_week_cli_0_6_desktop_app_schedule.md`

起点提交：`173eb701665a90c8d94545fe7758c3a49ad75c1a`

前置事实：

- Week 73 核心实现已提交：`173eb70 完成 Week73 任务聊天写路径`。
- Week 73 已通过 `.NET full test 1379/1379`、Desktop Vitest `84/84`、Desktop build/security、unpacked E2E。
- Week 73 clean packaged gate 尚未完全闭合：`npm run package:dir` 在 Electron packager 下载阶段 `ECONNRESET`；Week 74 必须优先重试或记录稳定替代证据，不能把 packaged E2E 标记为 Passed。

执行约束：

- 本计划只创建 Week 74 实施蓝图，不实现功能。
- 后续执行继续在当前分支原地开发；除非用户另行要求，不创建新 worktree 或新分支。
- 不得覆盖用户已有无关改动；若执行前工作树不干净，先冻结 baseline。
- 只有所有 Critical Gate 通过后，plan、review、schedule 才能同步改为 Passed。

## 本周目标

1. 建立 Desktop 用户 Terminal 的 reviewed protocol、AppHost supervisor、Application process service 和 Renderer UX；terminal 只能由用户显式打开/输入/关闭，不能作为 agent tool/approval bypass。
2. 将用户 terminal 与 agent command/tool timeline 分离身份：不同 source kind、correlation id、audit label、redaction policy 与 cancel/exit semantics。
3. 接入 artifact metadata/list/detail/preview/export projection，复用现有 ownership、managed path、hash、schema 与 redaction 服务；Renderer 不获得任意文件读取能力。
4. 将 Gerber/TIFF verification evidence、managed preview、accept/reject、safe resume/restart 状态投影到 Desktop，可由用户显式决策，不得由 preview、metadata 或 fake evidence 自动 accept。
5. 缺少 Gerbv/ImageMagick/LibTIFF/Magick.NET 所需环境时，只显示 doctor/diagnostic，不下载工具、不探测未知路径、不扩大 correctness 声明。
6. 覆盖 stale evidence、missing artifact、tamper、double decision、cleanup failure、terminal process leak、bounded output/backpressure 等负向场景。
7. 补齐 Week 73 packaged gate 的阻塞重试记录；如网络仍不可用，review 必须保留 Blocked 条目而不是伪造 packaged E2E 通过。

## 稳定输入

### Week 73 已交付能力

- `turn.write-path` capability 与 `turn.start/cancel/resume/restart`、`approval.resolve` 已进入 `desktop-v1` reviewed contract。
- Main/Preload bridge 已扩展为 exact `26 invoke + 2 event`，安全脚本已更新为 reviewed allowlist。
- AppHost 有单一 write execution supervisor；Application 有 injectable runtime、approval waiter、persisted event sink、cancel/restart 边界。
- Renderer 有 task controls，并通过 authoritative `thread.get`/`composer.get` resync，不重发 canonical prompt/path/tool args。
- `thread.changed` 是 dirty hint；timeline 和 turn state 仍以 ThreadStore/ThreadApplicationService projection 为事实源。

### 既有 Gerber/TIFF 与 artifact 能力

- CLI 已有 ProjectPack Gerber/TIFF plan/run/verify/preview/accept/reject、ManagedArtifact manifest/index、artifact list/show/export/verify/prune。
- `vertical_workflow_hardening.md` 冻结了关键边界：preview 不是 correctness proof，accept/reject 需要当前 hard verification report，artifact prune/export 有 managed ownership 和 hash/path recheck。
- `runtime_logging_diagnostics.md` 已定义 job/artifact pointers、task report、trace/session/report 边界，不允许 raw referenced content、raw tool arguments、raw secrets 或 full diff 进入 UI-safe projection。

## 范围冻结

### 本周包含

- Desktop terminal reviewed method/capability、AppHost terminal supervisor、bounded process spawn/cancel/resize/output ring buffer、terminal session projection。
- Renderer terminal panel：open/close、input、resize、copy selected text、bounded scrollback、exit status、busy/error/permission states。
- Artifact projection：list/detail、preview metadata、safe export intent、missing/corrupt diagnostics、managed ownership status；必要时复用现有 report/artifact stores。
- Gerber/TIFF Desktop panel：verification report summary、preview/contact sheet projection、accept/reject buttons、stale/tamper/missing states、restart/resume guidance。
- Tests：Core/Application terminal service tests、AppHost protocol tests、Main/Preload exact bridge tests、Renderer RTL tests、Desktop security scan、E2E smoke。
- Week 73 packaged gate retry and evidence update.

### 本周不包含

- Agent 使用用户 terminal 输入、terminal 作为 approval bypass、任意 shell automation、remote terminal、PTY sharing 或 terminal session persistence across AppHost restart。
- 任意文件浏览器、arbitrary artifact delete、workspace output destructive delete、accept undo、artifact restore/quarantine recovery。
- Gerber/TIFF manufacturing correctness 证明、模型视觉硬 gate、自动下载外部工具、自动发现未知 tool path。
- 多 workspace terminal concurrency、后台 artifact retention/quota worker、team/RBAC、remote runner、IDE terminal integration。
- Raw binary artifact streaming through JSON-RPC、Renderer 直接读取 managed artifact bytes、Preload 暴露 `context.resolve` 或 generic request。

## 方案设计

### 1. Terminal 使用显式用户身份，不进入 agent tool 边界

新增 optional capability 建议名：`terminal.user-session`。

候选 reviewed methods：

| Method | 类型 | 职责 |
|---|---|---|
| `terminal.open` | mutation | 在当前 workspace 下创建用户 terminal session，绑定 cwd、shell profile、env allowlist 和 client mutation id |
| `terminal.input` | mutation | 向指定 session 写入用户输入；只接受 text chunk，不接受 command object |
| `terminal.resize` | mutation | 更新 cols/rows；无权限扩大 cwd/env/process |
| `terminal.cancel` | mutation | 用户显式请求 Ctrl-C/terminate；必须先持久化 audit event |
| `terminal.close` | mutation | 关闭 session 并清理 process tree |
| `terminal.get` | query | 获取 session 状态、bounded scrollback cursor 和 exit metadata |

关键边界：

- AppHost 管理 process/PTY；Renderer 只调用 reviewed bridge，不持有 shell handle。
- Terminal output 使用 bounded ring buffer 和 explicit truncation marker；不得进入 agent approval decision 或 tool result。
- Terminal timeline item 使用 `terminal.user.*` source kind 或专门 audit label，和 Week 73 `command.started/completed` agent event 分离。
- cwd 必须在 workspace 内；默认 shell 只来自允许列表或系统配置投影，不接受 Renderer 任意 executable path。
- process tree cleanup、orphan detection、large output backpressure、resize storm 和 close race 必须有测试。

### 2. Artifact projection 只暴露 metadata 和受控 export intent

新增或扩展 optional capability 建议名：`artifact.review`。

候选 reviewed methods：

| Method | 类型 | 职责 |
|---|---|---|
| `artifact.preview` | query | 返回 managed artifact 的 UI-safe preview metadata 或 preview pointer，不返回任意 bytes |
| `artifact.export` | mutation | 用户显式导出 managed artifact 到 picker/approved destination，AppHost 执行 ownership/hash/path recheck |
| `artifact.verify` | query | 复用 managed artifact identity verification，返回 stable diagnostics |

现有 `artifact.list/get` 可继续承载 metadata；如字段不足，扩展 `ArtifactMetadataData` 与 generated validators。

关键边界：

- `artifact.export` 不能覆盖现有文件，除非有明确 replace flow；Week 74 默认 reject existing target。
- export 不可写出 workspace/source protected roots，必须使用 AppHost/Main picker 或 Application resolved destination。
- Renderer 永远不获得 absolute managed path、raw bytes、source path secrets 或 delete/prune 能力。
- missing/corrupt/tampered artifact 可与 valid records 并列显示，但指定 artifact invalid 应稳定失败。

### 3. Gerber/TIFF human gate 映射到 Desktop review 面板

Gerber/TIFF Desktop projection 复用 CLI hardening：

- `packs.verify` hard pass 只能进入 `awaiting-acceptance`，不能 accept。
- `packs.preview` 只证明 preview/inspection UX，`correctnessProof=false` 必须可见或在 UI 状态中不可误导。
- `packs.accept/reject` 必须重新加载当前 run/checkpoint/manifest/job、verification report hash、artifact identity、based-on revision。
- fake driver、metadata valid、file exists、preview generated、dry-run ready 均不能成为 accept 依据。
- accept/reject 是一次性 human decision；double decision、stale revision、report mutation、artifact tamper、missing preview 均 fail closed。

候选 reviewed methods：

| Method | 类型 | 职责 |
|---|---|---|
| `gerber.review.get` | query | 获取 run state、verification summary、preview availability、human decision eligibility |
| `gerber.preview` | mutation/query | 生成或读取 managed preview；不声明 correctness proof |
| `gerber.accept` | mutation | 用户显式 accept 当前 eligible run |
| `gerber.reject` | mutation | 用户显式 reject 当前 eligible run |

如为了减少协议面，也可先复用 `artifact.preview/export/verify` 加 `report.get`，Gerber/TIFF accept/reject 留在 Week 75；但 Week 74 Gate 要求人工闭环，推荐本周完成专用 reviewed methods。

### 4. Renderer UX

布局建议：

- 中央 timeline 保持事实源展示；Terminal 作为下方或右侧可折叠工作面，不覆盖 composer。
- Review inspector 增加 Artifacts/Gerber tabs：artifact list、preview summary、verification evidence、accept/reject card。
- Accept/reject 按钮必须显示 stale/missing/tamper/verification-not-hard-pass 禁用原因；reject 可允许简短 reason，严格长度限制。
- Terminal open/close/kill 需要显式 button，不用 keyboard shortcut 触发 destructive close。
- Screen reader：terminal status、exit code、truncation、preview correctness disclaimer、decision result 均有 aria-live/status。

## 协议与桥接冻结

Week 73 当前桥接为 exact `26 invoke + 2 event`。Week 74 如新增 6 个 terminal methods、3 个 artifact methods、4 个 Gerber methods，桥接目标将变为 `39 invoke + 2 event`；如果裁剪协议面，必须在 review 中写明最终数字。

必须同步：

- `protocol/desktop-v1/contract.json`
- `protocol/desktop-v1/examples/methods.json`
- generated C# / TS contracts
- `apps/desktop/src/shared/bridge-contract.ts`
- Main request descriptors、runtime wrapper、IPC registry
- Preload validation bridge
- security tests 和 `check-production-security.mjs`
- AppHost capability negotiation，未协商 capability 时隐藏/拒绝对应 methods

禁止：

- `desktop:request`、generic `terminal:write`、raw shell object、raw artifact path、`thread:delete`、Renderer `context.resolve`
- Preload 暴露 Node、fs、child_process、shell.openExternal、terminal process handle

## 测试计划

### .NET

- Terminal process service:
  - open rejects outside cwd, unknown shell, duplicate mutation mismatch, oversized input, output flood.
  - cancel/close persist audit before token/process kill.
  - process tree cleanup and exit status recorded.
- AppHost protocol:
  - capability negotiation hides terminal/artifact/Gerber methods when absent.
  - strict params reject unknown fields, oversized strings, stale revisions.
- Artifact Application:
  - preview/export/verify rechecks ownership, hash, path, missing/corrupt/tamper.
  - export rejects existing/outside/reparse destinations.
- Gerber/TIFF:
  - preview cannot accept; accept requires current hard verification report.
  - stale evidence, double decision, fake evidence, report mutation, artifact tamper fail closed.

### Electron / Renderer

- Main/Preload exact channel inventory and cleanup count.
- Preload validators reject invalid terminal/artifact/Gerber commands.
- Renderer terminal panel handles open/input/output/truncation/exit/cancel states.
- Artifact/Gerber review panel handles preview, missing/corrupt, accept/reject disabled reasons.
- Security scan asserts reviewed channels only and forbidden raw surfaces absent.

### E2E / smoke

- Unpacked E2E:
  - terminal open -> echo/local command -> exit status -> no orphan.
  - artifact metadata visible without absolute path leakage.
  - Gerber/TIFF fake or fixture-backed preview -> reject path -> no accept by preview.
- Packaged E2E:
  - retry Week 73 packaged shell/write smoke first.
  - then run Week 74 terminal/artifact smoke.
- Optional real tool smoke:
  - only behind `CAICLI_GERBER_TIFF_TOOL_SMOKE=1` with explicit reviewed executable/fixture/baseline identities.
  - skipped state is acceptable only if review clearly says correctness remains unproven.

## Critical Gate

Week 74 只能标记 Passed，当且仅当：

- `.NET full suite` 通过。
- `npm test`、`npm run typecheck`、`npm run lint`、`npm run build`、`npm run check:contracts`、`npm run check:notices`、`npm run check:production-security` 通过。
- unpacked E2E 覆盖 terminal/artifact/Gerber review smoke。
- packaged E2E 或等价 packaged AppHost/Desktop smoke 成功；若 Electron 下载继续 `ECONNRESET`，状态必须保持 Blocked/Partial。
- terminal 不形成 tool/approval bypass；terminal output 不进入 raw model/tool context。
- preview 不能自动 accept；accept/reject stale/tamper/missing/double-decision tests 通过。
- process/orphan delta 为 0：AppHost、terminal shell、external Gerber/ImageMagick/Gerbv/Magick 相关 process 均无 test-owned 残留。
- docs/review/schedule 准确区分 Passed、Blocked、Deferred，不扩大 correctness 声明。

## 任务拆分

1. Baseline 与 Week73 carryover
   - 确认 HEAD、dirty state、SDK/Node/Electron 版本。
   - 重试或记录 `package:dir`/packaged E2E 下载阻塞。
   - 冻结 Week74 final protocol method count 目标。

2. Terminal core/Application/AppHost
   - 设计 terminal session record、bounded output、process lifecycle、audit event。
   - 实现 Application terminal service 与 tests。
   - 实现 AppHost terminal supervisor 与 capability gate。

3. Terminal protocol/Electron/Renderer
   - 扩展 contract/generated/Main/Preload exact bridge。
   - 实现 Renderer terminal panel。
   - 更新 security scan 和 RTL tests。

4. Artifact projection
   - 扩展 Application artifact preview/export/verify projection。
   - 接入 AppHost methods 与 Renderer review inspector。
   - 覆盖 ownership/hash/path/reparse/missing/corrupt tests。

5. Gerber/TIFF human loop
   - 复用 verification/preview/accept/reject use case，加入 Desktop-safe projection。
   - UI 显示 hard verification、preview disclaimer、decision eligibility。
   - 覆盖 stale/tamper/double decision/fake evidence tests。

6. E2E、packaged smoke 与 process cleanup
   - 增加 unpacked smoke。
   - 尝试 packaged smoke；若失败，记录网络/缓存原因和复现命令。
   - 进程 orphan 和临时目录清理检查。

7. Review 文档与 schedule
   - 创建 `74_week_review.md`。
   - 只有 gate 全过才把 status 改为 Passed。
   - 若 packaged 或 real tool smoke 未过，明确 Pending/Blocked/Skipped，不写 Accepted。

## 风险与处置

- xterm.js 新依赖风险：先做 dependency/license/security/notices gate；若不满足，采用 plain terminal panel 最小实现。
- Windows process tree cleanup 风险：必须以 job/process tree helper 做 deterministic cleanup；失败时保持 recovery/audit 状态。
- Artifact export destructive 风险：Week 74 默认 no-overwrite；replace/delete/prune UI 不进入本周。
- Gerber/TIFF correctness 误导风险：UI 文案和 data model 必须区分 preview、metadata、hard verification、human decision。
- Electron packaged 下载风险：优先使用缓存；网络失败不得阻塞核心实现 review，但会阻塞 clean packaged Gate。
