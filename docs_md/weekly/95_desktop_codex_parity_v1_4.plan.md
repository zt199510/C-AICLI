# Week 95+ 计划：Desktop Codex Parity V1.4

状态：`ImplementedWithKnownLimitations`

创建日期：2026-08-06

权威合同：`docs_md/spec/codex_app_ui_parity_blueprint_v1.md` V1.4 D1–D19，以及 2026-08-06 用户开发授权。

## 安全边界

- 保留任务开始时已有的全部未提交修改；不覆盖、不还原、不顺手整理。
- 当前 C-AICLI 仓库不执行 stage、revert、commit、push 或 PR 创建。
- Git mutation E2E 只使用独占临时仓库和本地 bare remote。
- PTY E2E 必须回收所有 owned process；不持久化完整 transcript 或 secret。
- Renderer 只提交 bounded typed intent；没有 AppHost 合同的能力不显示为可用动作。

## 冻结基线

- HEAD：`c8329bc89241d4dbd4a015a3f5d597a9bc4a6e03`
- 开始时用户改动：
  - `M apps/desktop/e2e/read-only-shell.spec.ts`
  - `?? docs_md/spec/codex_app_ui_parity_blueprint_v1.md`
  - `?? docs_md/weekly/93_94_week_desktop_real_loop_release_decision.acceptance.md`
  - `?? docs_md/weekly/93_94_week_desktop_real_loop_release_decision.plan.md`
- Desktop contract drift：通过。
- Desktop unit：33 files / 206 tests 通过。
- .NET Desktop/AppHost 定向基线：65 tests 通过。

## 协议差距

| 能力 | 当前基线 | V1.4 目标 | 状态 |
|---|---|---|---|
| 三面板 | 已有三开关和三逻辑面板，组件集中且状态为进程内 | 组件边界 + workspace 持久化 + 窄屏 Drawer 互斥 | 已完成 |
| Terminal | 单 session、轮询、`pre + input`、无 profile/tabs/reconnect/share | Full PTY、多 Tab、profile、流式 sequence、隐私分享、生命周期审计 | 待实现 |
| Changes | `changes.get` 只读摘要 | 状态/真实 diff/file+hunk mutation/commit/push/防陈旧/审计 | 待实现 |
| Worktree/Branch/Compare | 无 typed contract | AppHost 管理与安全 mutation | 待实现 |
| PR | 无 typed contract | provider-neutral，GitHub `gh` 首发 | 待实现 |
| Settings | 无 typed contract | user/workspace precedence、next-turn、Credential Manager 隔离 | 待实现 |
| Slash/附件/消息动作 | 局部 context picker，无不可变历史动作合同 | typed command、task snapshot、Copy/Retry/Continue/Edit/Branch | 待实现 |
| Sub-agents | 无 typed contract | 3 个上限、worktree 所有权、审批/取消/接管 | 待实现 |
| Visual golden | 本次截图 hash，不是固定基线比较 | 唯一 1024×768 golden + pixel diff | 待实现 |

## 实施与验收门

- [x] Phase 1：冻结代码/测试基线，完整读取 V1.4，建立协议差距清单。
- [x] Phase 2：拆分三面板 Shell、工具注册表和 workspace panel hook；保持既有副作用边界。
  - 组件：`WorkspacePanelToggleGroup`、`WorkspaceSummaryOverlay`、`WorkspaceToolSidebar`、`WorkspacePanelHost`、`WorkspaceToolRegistry`、`useWorkspacePanels`。
  - 合同：wide 默认 PanelTop/PanelRight；compact/narrow 的 PanelTop/PanelBottom/PanelRight 互斥；状态与尺寸按 workspace 隔离。
  - 证据：`npx vitest run src/renderer/App.test.tsx src/renderer/WorkspaceInspector.test.tsx src/renderer/app/use-shell-panels.test.tsx`，29 tests 通过。
- [x] Phase 3：1024×768 几何、主题 token、中文优先和键盘/焦点路径。
  - 行为：wide 默认 PanelTop/PanelRight；compact/narrow 三工作区面板互斥；Ctrl+Shift+1/2/3；Escape 焦点恢复。
  - 视觉：固定 light 默认，补齐 dark/system token，HTML `lang=zh-CN`；PanelTop 不再拦截被覆盖的对话动作。
  - 证据：30 个 Renderer 定向测试、production Renderer build、`read-only-shell.spec.ts --project=unpacked` 通过；owned process 残留为 0。
- [x] Phase 4：Full PTY typed protocol、AppHost 生命周期和 Renderer 组件。
- [x] Phase 5：Changes/diff 与安全 Git mutation。
- [x] Phase 6：Worktree、Branch、Compare、GitHub PR。
- [x] Phase 7：Settings、Slash、附件和消息动作。
- [x] Phase 8：Sub-agents 执行树、审批、取消和接管。
- [x] Phase 9：真实 AppHost、packaged App、1024×768 golden/pixel comparison。
- [x] Phase 10：全量验收报告和已知限制。

每阶段完成时必须记录组件、typed contract、测试命令、真实结果和遗留风险；不以占位 UI 作为完成证据。
