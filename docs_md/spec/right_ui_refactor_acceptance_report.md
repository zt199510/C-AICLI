# 右侧 UI 重构与 Codex 式三面板布局实施验收记录

> 2026-08-06 后续视觉基线说明：用户提供的 Windows Codex 实机截图已取代本文第 8 节的旧几何结论。PanelTop 继续浮动；PanelBottom 现为 Composer 下方的真实贴底 dock，不再绝对定位覆盖时间线；任务顶栏横跨对话区和 PanelRight，PanelRight 改为宽版白色工作区中的居中工具启动器。本文其余旧截图与数值仅作为历史证据。

日期：2026-08-04

范围：`apps/desktop` 右侧 Workspace Inspector

基线：`artifacts/ui/right-ui-refactor/Codex式三面板布局调整开发计划.docx` 与对应新对话执行提示词；原右侧 UI 规划作为能力与安全基线
说明：第 1-7 节保留上一阶段“纵向工具导航 + 详情卡”的历史验收；第 8 节记录本次三面板增量调整的当前最终状态。

## 1. 实施结果

- 将横向五标签改为“Environment / Results and evidence / Sub-agents”三组纵向工具导航。
- 新增统一 Current tool 详情卡，继续承载 Changes、Terminal、Reports、Artifacts、Preview 原有组件与命令边界。
- 宽屏 Inspector 默认以内联 360px 右栏显示；1024px 及以下为右侧覆盖抽屉。
- 抽屉支持关闭按钮、Escape、遮罩点击、打开后焦点进入、Tab 焦点约束和关闭后焦点回归。
- 缺少协议支持的分支、Git 写操作、Pull Request、比较分支和子智能体数据以 Unknown / Unavailable / Disabled 显示，不把失败伪装为空数据。
- Commit or push 导航项为禁用按钮；切换工具不会执行写操作。

## 2. 能力映射

| 原能力 | 新入口 | 保留内容 |
| --- | --- | --- |
| Changes | Environment / Changes | 状态、dirty、Git 摘要、diff 统计、文件与警告 |
| Terminal | Environment / Terminal | 打开、输入、轮询、复制、取消、关闭与有界输出 |
| Reports | Results and evidence / Reports | 列表、选择与结构化报告详情 |
| Artifacts | Results and evidence / Artifacts | 列表、详情、验证、预览元数据与导出 |
| Preview | Results and evidence / Preview | Gerber/TIFF 元数据、刷新、验证与人工决策 |

## 3. 响应式与视觉验收

| 视口 | Inspector 矩形 | 详情可见高度 | 横向溢出 | 编辑器重叠 | 结果 |
| --- | --- | ---: | ---: | --- | --- |
| 1440×900 | left 1080 / right 1440 / top 56 / bottom 900 | 275px | 0px | 无 | 通过 |
| 1024×768 | left 664 / right 1024 / top 56 / bottom 636 | 85px | 0px | 无 | 通过 |
| 800×900 | left 440 / right 800 / top 56 / bottom 768 | 217px | 0px | 无 | 通过 |

浏览器手工验收同时确认：右对齐、360px 宽度、遮罩存在、打开后焦点进入 Changes、Escape 与遮罩关闭后焦点回到 Show workspace inspector；800px 下只有一个抽屉打开。浏览器控制台无错误或警告。

## 4. 自动验证

- `npm run verify`：通过。
  - contracts / notices drift：通过。
  - WCAG 2.x AA 语义颜色对比度：通过。
  - TypeScript：通过。
  - ESLint：通过。
  - Vitest：33 个测试文件、194 个测试全部通过。
  - main / preload / renderer 生产构建与生产安全检查：通过。
- `npm run test:e2e`：通过。
  - 现有 Chat-first Gate 与 supplemental 矩阵共 45 个截图案例。
  - 三档均满足零 body 横向溢出、零 composer/drawer 重叠、可达操作、命名操作和窄屏抽屉互斥约束。
- 右侧 UI 定向测试：3 个测试文件、21 个测试全部通过。

## 5. 安全与回归边界

- 未修改后端协议，未新增依赖。
- 未修改 ThreadSidebar 组件或其已验收视觉；仅复用现有 shell 的触发与面板状态接口。
- 工作区开始时已有的未跟踪文件 `docs_md/spec/codex_style_continuous_chat_retry_acceptance_report.md` 保持未修改。
- Git 写操作没有新增执行入口；未来接入时仍需独立确认、防重复提交和失败恢复。

## 6. 与视觉基线的偏差

- 1024×768 与 800×900 的抽屉底边分别为 636px 和 768px，比效果基线各上移 16px。原因是仓库现有视觉安全门要求抽屉与 Composer 完全不相交；以内容可操作性和安全约束优先。
- 覆盖式抽屉中的工具行使用 32px 紧凑高度，桌面仍为 35px。该调整用于同时容纳 11 个明确入口并保留可读详情高度；焦点环、命中区域和键盘导航均已验证。
- Branch、Pull Request、Compare branches 和 Sub-agents 因当前 `desktop-v1` 无对应字段或命令，仅提供明确的未知/不可用状态；没有伪造数据，也没有扩大协议范围。

## 7. 明确后置

- 真实 PR、分支比较、Git commit/push 和子智能体数据接入需要后端协议另行授权。
- 工具排序、自定义、跨窗口同步、插件注入、高级分支图和动画细化仍按规划后置。

## 8. Codex 式三面板布局增量验收（当前状态）

### 8.1 用户可见变化

- 对话工具栏右上角新增三个常驻并排按钮，固定顺序为 `PanelTop`、`PanelBottom`、`PanelRight`，分别控制顶置摘要、底部详情和右侧工具导航。
- 三个按钮均提供稳定 `aria-controls`、`aria-pressed`、`title`、`aria-label` 和可见焦点/边框/底线打开状态；原生按钮支持 Enter 与 Space。
- 顶置摘要与底部详情放入新的 `timeline-stage`，使用绝对定位，不参与 `workspace-layout` 网格列计算；Composer 继续位于舞台之外。
- 右侧栏只保留纵向分组工具导航和状态；宽屏内联，紧凑/窄屏为带遮罩和焦点约束的右侧抽屉。
- 点击任一可选工具仅更新 `activePanel` 并自动打开底部详情，不直接调用提交、推送、删除或中止命令；关闭底部详情后仍保留所选工具和工具内部状态。
- 右侧栏不再保留重复的内部整体关闭按钮，统一由右上 `PanelRight` 开关控制；抽屉仍支持 Escape、遮罩点击与焦点回归。

### 8.2 关键文件与职责

| 文件 | 当前职责 |
| --- | --- |
| `apps/desktop/src/renderer/App.tsx` | 三按钮接线、`timeline-stage`、浮层宿主、右侧栏内联/抽屉宿主，以及工具选择后自动打开底部详情 |
| `apps/desktop/src/renderer/app/use-shell-panels.ts` | `summaryOpen`、`bottomPanelOpen`、`toolSidebarOpen`、三个触发器 ref、Escape 顺序、焦点回归、断点迁移及左右栏互斥 |
| `apps/desktop/src/renderer/WorkspaceInspector.tsx` | 共享工具注册表，以及 `WorkspaceSummaryOverlay`、`WorkspaceBottomPanel`、`WorkspaceToolSidebar` 三个独立表面；复用原 Review/Terminal 详情逻辑 |
| `apps/desktop/src/renderer/app/app-shell.css` | 舞台绝对定位、层级、滚动、三按钮状态、宽屏/紧凑/窄屏规则、强制色与无溢出约束 |
| `apps/desktop/e2e/chat-ui-visual.spec.ts` | 三档视觉矩阵、时间线宽度前后测量、Composer/浮层重叠、非模态语义、侧栏定位、抽屉互斥与焦点回归 |
| `apps/desktop/scripts/package-desktop.mjs` | 增加 `.log` 打包排除，防止本地预览日志进入 `app.asar` |

### 8.3 能力与安全回归

| 能力 | 当前承载位置 | 验收结果 |
| --- | --- | --- |
| Changes | 底部详情 | 状态、dirty、Git 摘要、diff、文件与警告保留 |
| Terminal | 底部详情 | 打开、输入、输出、复制、清理、取消、关闭与 resize 控制器边界保留；显示/隐藏不取消会话 |
| Reports | 底部详情 | 加载、列表、选择、错误和空状态保留 |
| Artifacts | 底部详情 | 加载、选择、打开/定位、错误和空状态保留 |
| Preview | 底部详情 | 加载、刷新、验证和失败恢复保留 |
| Environment / Sub-agents | 顶置摘要与右侧导航 | 只显示真实值或明确 Unknown/Unavailable，不从对话文本伪造状态 |

`Commit or push` 与 `Compare branches` 仍为禁用导航项。组件测试确认点击禁用项不会调用选择回调；现有写操作、中止和人工决策继续由原控制器及确认流程保护。

### 8.4 三档响应式与视觉证据

| 视口 | 右侧栏模式 | `timeline-stage` 打开浮层前/后 | 横向溢出 | 摘要/底部重叠 | Composer 重叠 | 结果 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| 1440×900 | 360px 内联网格列 | 792px / 792px | 0px | 0 | 0 | 通过 |
| 1024×768 | 右侧覆盖抽屉 | 784px / 784px | 0px | 0 | 0 | 通过 |
| 800×900 | 右侧覆盖抽屉，与左栏互斥 | 800px / 800px | 0px | 0 | 0 | 通过 |

代表截图：

- `artifacts/week86-renderer-chat-first-shell/screenshots/open-workspace-desktop-1440x900.png`
- `artifacts/week86-renderer-chat-first-shell/screenshots/open-workspace-compact-1024x768.png`
- `artifacts/week86-renderer-chat-first-shell/screenshots/open-workspace-narrow-800x900.png`
- `artifacts/week86-renderer-chat-first-shell/screenshots/review-terminal-open-compact-1024x768.png`
- `artifacts/week86-renderer-chat-first-shell/screenshots/review-terminal-open-narrow-800x900.png`

自动视觉清单 `artifacts/week86-renderer-chat-first-shell/screenshot-manifest.json`：15 个 Gate 案例；补充矩阵 30 个案例，共 45 个。全部满足零 body 横向溢出、零关键重叠、零不可达/未命名可见操作、浮层绝对定位、浮层非模态、三按钮固定顺序和正确侧栏定位模式。人工检查上述截图未发现裁切、错误滚动容器或 Composer 遮挡。

### 8.5 键盘、焦点与响应式状态

- 三按钮可按 Tab 到达；Enter/Space 使用原生 button 激活。
- Escape 按最后打开顺序关闭底部详情或顶置摘要，并把焦点恢复到相应按钮。
- 紧凑/窄屏右侧抽屉打开后焦点进入当前可选工具，Tab 保持在抽屉工具内；Escape 与遮罩关闭后焦点回到 `PanelRight`。
- 工具导航保留上下/左右方向键、Home、End 的 roving focus。
- 800px 下先打开左侧会话栏，再打开右侧工具栏会自动关闭左栏；摘要和底部详情不参与该互斥。
- 跨 1120px/899px 断点仍保留用户调整的 320-480px 侧栏宽度。

### 8.6 自动验证结果

| 命令 | 结果 |
| --- | --- |
| 定向 Vitest：`App` / `WorkspaceInspector` / `useShellPanels` | 3 个文件、27 项通过 |
| `npm run verify` | 通过；contracts、notices、WCAG AA、TypeScript、ESLint、33 个测试文件/200 项测试、main/preload/renderer 构建及生产安全检查全部通过 |
| `npm run test:e2e` | 通过；45 个视觉案例覆盖 1440×900、1024×768、800×900 |
| `npx playwright test week76-hardening.spec.ts --project=unpacked` | 通过；键盘导航、焦点、强制色与安全边界用例通过 |
| `npm run check:package-security` | 通过；目录包 78 个文件、`app.asar` 12 个条目、禁止负载 0 |

E2E 在本机首次启动时因 Electron GPU 子进程以 `0xC0000135` 退出而无法显示页面；测试启动增加 `--in-process-gpu` 后完整矩阵稳定通过。独立包审计首次因无 `out` 目录报 `ENOENT`；标准打包随后遇到 NuGet/GitHub 网络限制。NuGet 还原与 AppHost 发布在授权联网重试中成功，Electron 下载仍超时，因此使用项目已安装并已通过 E2E 的 Electron 41.1.0 生成受支持的本地 ZIP 缓存，随后目录打包与包审计通过。审计还发现用户已有 `.vite-sidebar-preview.err.log` 会进入 `app.asar`，已通过打包忽略规则排除，没有删除该文件。

### 8.7 用户已有改动保护

- 开始时 `main` 比 `origin/main` 领先 1 个提交，八个右侧 UI/测试文件已有未提交改动，两份验收报告为未跟踪文件。
- 本次直接在这些改动上增量拆分，没有运行 `git reset --hard`、`git checkout --`、清理或覆盖命令。
- 未修改已验收的 `ThreadSidebar` 组件和左侧视觉；仅沿用原壳层互斥接口。
- `docs_md/spec/codex_style_continuous_chat_retry_acceptance_report.md` 保持原内容不变；本报告保留上一阶段历史验收并追加当前结果。

### 8.8 已知偏差与未完成项

- 1024×768 的顶置摘要受 30% 舞台高度限制，内容较多时出现内部滚动条；这是计划允许的有界滚动，不是页面级溢出。
- 抽屉底边继续停在 Composer 上方（沿用既有 132px 安全偏移），没有覆盖整个工作区高度；此偏差用于满足 Composer 可达和零重叠安全门。
- Branch、Pull Request、Compare branches、Git commit/push 和 Sub-agents 的真实数据/写命令仍因 `desktop-v1` 未提供接口而保持 Unknown/Unavailable/Disabled；本次未修改后端协议。
- 工具排序、自定义、跨窗口同步、插件注入和可拖动面板高度继续后置。三面板布局本身没有未完成项。

### 8.9 顶部重复工具栏收尾

- 根据最终界面复核，移除了与左侧品牌区重复的全局顶部工具栏及其 56px 网格占位；工作区现在从窗口顶端开始。
- 工作区标题栏中的打开工作区按钮和三个常驻面板开关保持不变；运行状态继续由壳层状态属性及各状态表面承载。
- 收尾验证通过：`App` 单元测试 12/12、TypeScript 类型检查、renderer 生产构建，以及包含 1440×900、1024×768、800×900 的 `npm run test:e2e` 视觉/响应式矩阵。
