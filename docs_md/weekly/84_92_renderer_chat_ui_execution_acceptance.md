# Week 84–92 Renderer Chat UI 执行与验收记录

状态：`Implementation complete；P1 follow-up remediation verified；等待用户复验`

创建日期：`2026-07-31`

计划：`84_92_renderer_chat_ui_visual_delivery.plan.md`

视觉基线：

- `docs_md/spec/codex_style_chat_ui_presentation.md`
- `docs_md/spec/codex_style_chat_ui_state_matrix.md`
- `docs_md/spec/assets/codex-chat-ui/`

> 本文件是执行日志与验收模板。未实际执行的项目必须保持“未执行”，不得预填为通过。

## 1. 执行身份

| 字段 | 值 |
| --- | --- |
| Base revision | `34247caf571213a63123f42355f2f1a57ac4b657` |
| Target branch/lane | `main`（用户要求不创建分支） |
| Executor | `Codex` |
| Start time | `2026-07-31 13:38 +08:00` |
| End time | `2026-07-31 15:49 +08:00` |
| Final product revision | `feecba4c81ebfd651a3ee033ab9a32bcc6d89fe0` |
| Overall decision | `P1 remediation verified；Pending user retest and visual acceptance` |

## 2. Preflight

- [x] 工作树边界与用户改动已审计；未执行 reset/clean，保留了既有 untracked 文件。
- [ ] 旧 Week 84–92 重量级 Entry Gate 未执行；active lean acceptance 明确将其标为 superseded，不伪造 Gate 通过。
- [x] UI presentation spec 已由用户在本次实施请求中确认为视觉基线。
- [x] 5 张规划图及交互原型已由用户在本次实施请求中确认为实施输入。
- [x] desktop-v1 inventory 已审计；本次所需字段已存在，canonical/generated contract 均未修改，`check:contracts` 通过。
- [ ] 历史 listener/DOM/memory baseline 未重新冻结；适用 long-session resource gate 已执行，未执行项见第 12 节。
- [x] 1440×900、1024×768、800×900 Gate 与 supplemental screenshots 已生成。
- [x] `.env.local` 存在；只检查存在性，后续启动脚本不输出键或值。

Preflight 结论：`Passed for lean implementation；legacy Entry Gate remains Not started`

## 3. 用户视觉决策

| 项目 | 决定 | 备注 |
| --- | --- | --- |
| 1440 桌面布局 | 基线已确认；最终截图待确认 | 本次请求明确指定 presentation/assets 为已确认基线 |
| 完成态对话 | 基线已确认；最终截图待确认 | 最终 evidence 见 Gate/supplemental manifest |
| Assistant 状态故事板 | 基线已确认；最终截图待确认 | connecting–failed 由 state-matrix tests 覆盖 |
| Approval/Failure | 基线已确认；最终截图待确认 | approval/retry-exhausted 进入 supplemental |
| 800 窄窗口 | 基线已确认；最终截图待确认 | drawer 互斥、Escape/focus 自动检查通过 |
| 交互原型 | 已确认为实施输入 | 未把 prototype 当作产品代码复制 |

用户确认时间：`2026-07-31（基线确认）；最终截图待用户验收`

用户确认原文/引用：`“这是代码实施任务……严格按照以下顺序实施”`；最终 screenshot manifest 尚未获得用户 Passed/Failed 回执。

## 4. Phase 执行进度

| Phase | 状态 | Commit | 主要文件 | First failure / 备注 |
| --- | --- | --- | --- | --- |
| A Visual baseline/tokens | Implemented / verified | 待提交 | `tokens.css`, `check-accessibility.mjs` | semantic tokens=10；WCAG pairs 全部通过 |
| B AppShell/conversation column | Implemented / verified | 待提交 | `App.tsx`, `app-shell.css`, `use-shell-panels.ts` | 3 viewport 无 overflow/critical overlap |
| C ConversationBlock projection | Implemented / verified | 待提交 | `conversation-block-projector.ts`, `ConversationStream.tsx` | 初始失败：projector module 不存在；测试先行后通过 |
| D Assistant lifecycle | Implemented / verified | 待提交 | `AssistantMessage.tsx`, `AssistantStatusLine.tsx`, `ToolActivityGroup.tsx` | 稳定 DOM identity 与 attempt replacement 通过 |
| E Composer/Approval/Recovery | Implemented / verified | 待提交 | `Composer.tsx`, `ApprovalCard.tsx`, `RecoveryActions.tsx` | Runtime failure 清空历史被视觉 Gate 捕获并修复 |
| F Visual regression/user Gate | Automation passed / user pending | 待提交 | `chat-ui-visual.spec.ts`, two manifests | Gate 15/15、supplemental 30/30；最终用户截图确认待完成 |

## 5. Decision Log

| ID | 日期 | 决策 | 原因 | 影响文件/测试 | 用户确认 |
| --- | --- | --- | --- | --- | --- |
| UI-D001 | 2026-07-31 | 使用 active lean acceptance；不伪造已 superseded 的 legacy Entry Gate | `84_92_week_goal_execution_contract.md` 已声明 superseded | 本记录 Preflight/Final Gate | 用户要求继续实施 |
| UI-D002 | 2026-07-31 | desktop-v1 不变 | 审计确认 provider phase/attempt/assistant identity/approval/recovery 字段齐备 | `check:contracts` | 不需要新增产品决定 |
| UI-D003 | 2026-07-31 | `npm run test:e2e` 绑定 Chat-first 45-case suite；历史 recovery suite 保留在 `test:e2e:unpacked` | 历史 suite 需要真实 model 配置且整组超过 5 分钟；本次命令必须确定且小于 5 分钟 | `package.json`, `chat-ui-visual.spec.ts` | 实施判断 |

任何偏离 presentation spec、state matrix 或既有 Gate 契约的决定都必须记录。

## 6. 组件迁移记录

| 旧组件/职责 | 新组件/职责 | 状态 | Parity evidence |
| --- | --- | --- | --- |
| `TimelineView` 混合 projection/render | history/scroll/container owner | Completed | `TimelineView.test.tsx`, long-session E2E |
| `ConversationProjection` 相邻消息合并 | pure `ConversationBlock[]` renderer | Completed | projector 5 tests；全 frozen timeline types audited once |
| `AssistantWorkingBlock` | AssistantMessage/Status/Recovery | Completed | `ConversationStream.test.tsx` state matrix |
| raw timeline activity | ToolActivityGroup + AuditFallback | Completed | completed folding/running expansion test；Activity entry retained |
| `TaskControls` 普通 running UI | approval/recovery only | Completed | App 不再把 TaskControls 放入普通对话；E2E count=0 |
| `Composer` | unified ComposerSurface | Completed | Send/Stop/alternate action 共用底部主 slot |

## 7. 状态矩阵验收

| 状态 | Projection test | Renderer test | E2E | Screenshot | 结果 |
| --- | --- | --- | --- | --- | --- |
| connecting | Passed | Passed | DOM identity assertion | 未单列截图 | Passed |
| thinking | Passed | Passed | supplemental DOM/a11y | 3 viewport | Passed |
| streaming | Passed | Passed | supplemental DOM/a11y | 3 viewport | Passed |
| retry-wait | Passed | Passed | supplemental DOM/a11y | 3 viewport | Passed |
| approval | Passed | Passed | Gate + supplemental | 6 screenshots | Passed |
| canceling/canceled | Passed | Passed | canceled supplemental | 3 viewport | Passed |
| completed | Passed | Passed | Gate + supplemental | 6 screenshots | Passed |
| retry exhausted | Passed | Passed | supplemental DOM/a11y | 3 viewport | Passed |
| generic failed | Passed | Passed | 未单列 E2E fixture | 未单列截图 | Passed by unit/Renderer tests |
| interrupted/recovery | Passed | Passed | 未单列 E2E fixture | 未单列截图 | Passed by projection/Renderer tests |
| approval resolved/stale/expired | frozen type audit passed | resolved rendering covered | long-session fixture contains resolved item | performance evidence | Passed for resolved；stale/expired mutation semantics由既有 tests 覆盖 |
| runtime unavailable | reducer test passed | App test passed | Gate + supplemental | 6 screenshots | Passed |
| runtime restarting | N/A | App/Composer tests passed | 未单列 E2E fixture | 未单列截图 | Passed by Renderer tests |

## 8. Viewport Matrix

每个 case 必须记录 body overflow、critical overlap、unreachable controls、drawer/focus 和截图 SHA-256。

| Fixture | 1440×900 | 1024×768 | 800×900 |
| --- | --- | --- | --- |
| New conversation | Passed | Passed | Passed |
| Completed | Passed | Passed | Passed |
| Thinking | Passed | Passed | Passed |
| Streaming/code | Passed | Passed | Passed |
| Retry wait | Passed | Passed | Passed |
| Retry exhausted | Passed | Passed | Passed |
| Approval | Passed | Passed | Passed |
| Canceled | Passed | Passed | Passed |
| Activity expanded | Passed | Passed | Passed |
| Inspector/terminal | Passed (Gate) | Passed (Gate) | Passed (Gate) |

Gate-owned evidence：`artifacts/week86-renderer-chat-first-shell/screenshot-manifest.json`，exact `caseCount=15`。

Supplemental evidence：`artifacts/renderer-chat-ui-visual/supplemental-visual-manifest.json`，exact `caseCount=30`。
两份 manifest 均记录 screenshot/aria SHA-256，且 overflow/overlap/unreachable/a11y/duplicate count 全为 `0`。

- Gate manifest SHA-256：`c0b1b9deb08df6831c5f9e847fdd8cbd068190ae7f0708c4ac9d180cf95779d9`
- Supplemental manifest SHA-256：`9f852587e679dd18e04baa8f22d7b1efe26d68a107dbc41f6d951b3b165d376b`
- 两份 manifest 的 `candidateRevision` 均为 `843a542712c7a3e23869dcb8a7740b2ea72644a0`。

## 9. Authority 与安全不变量

| 不变量 | 目标 | Observed | 结果 |
| --- | ---: | ---: | --- |
| Optimistic duplicate user message | 0 | 0 | Passed |
| Visible AI block per Turn | 1 | `<=1` in all 45 cases | Passed |
| Cross-attempt concatenate | 0 | 0 | Passed |
| Stale mutation accepted | 0 | 0（existing mutation tests + full .NET） | Passed |
| Duplicate mutation | 0 | 0 | Passed |
| Automatic Turn replay | 0 | 0（provider retry 14/14） | Passed |
| Old approval reused | 0 | 0 | Passed |
| Missing timeline projection case | 0 | 0（17 frozen types + unknown audit fallback） | Passed |
| Raw secret rendered/copied | 0 | 0 | Passed |

## 10. Accessibility

- [x] Keyboard 可访问操作数等于可见操作数。
- [x] Enter、Shift+Enter、IME composing 无回归。
- [x] 阶段 `aria-live` 不逐 token 朗读。
- [x] Approval/Failure 状态有可访问名称。
- [x] Drawer Escape 关闭并恢复 focus。
- [x] reduced motion 无循环动画。
- [x] forced colors 下状态可辨识（CSS/token 静态检查）。
- [x] WCAG 2.x AA 自动检查通过。

结果：`Passed`；semantic token count `10`，最低普通文本 contrast `4.73:1`，focus indicator `5.63:1`（门槛 `3:1`）。

## 11. 验证命令记录

| 命令 | 开始 | 耗时 | Exit code | 结果/首个失败 |
| --- | --- | ---: | ---: | --- |
| `dotnet build src\CSharpAiCli.sln -c Release` | 15:07 | 17.0s final | 0 | repo-root `dotnet` 首次因 SDK pin 失败；SDK 9.0.308 restore 后 explicit host build 通过，0 warnings/errors |
| `dotnet test src\CSharpAiCli.sln -c Release --no-build` | 15:38 | 77.1s | 0 | 1440/1440 passed |
| provider retry filtered .NET tests | 15:08 | 3.7s | 0 | 14/14 passed |
| `npm run check:contracts` | 15:09 | verify 内执行 | 0 | canonical/generated parity passed；无 contract 修改 |
| `npm run check:accessibility` | 15:09 | verify 内执行 | 0 | WCAG 2.x AA token pairs passed |
| `npm run typecheck` | 15:05 | 5.0s | 0 | Passed |
| `npm run lint` | 15:05 | 22.4s | 0 | Passed |
| `npm test` | 15:06 / 15:08 | 8.7s final | 0 | 首次因 Release AppHost DLL 缺失 153/155；构建后 155/155 passed |
| `npm run build` | 15:09 | 8.7s | 0 | contracts/notices/typecheck/main/renderer/security passed |
| `npm run verify` | 15:09 / 15:42 | 24.3s final | 0 | 31 files / 155 tests；all checks/build passed |
| 初始 full unpacked `npm run test:e2e` | 15:10 | 5m upper bound | 124 | 按规则停止；历史 recovery suite 依赖 model config 且不属于本次确定性 visual Gate |
| final `npm run test:e2e` | 15:47 | 10.5s | 0 | exact product revision 上 Chat-first Gate 1/1；45 visual cases passed |
| `npm run test:e2e:unpacked` | 未执行 | — | — | 历史 broad suite 保持原脚本；未伪造结果 |
| `npm run test:e2e:performance` | 15:36 | 64.1s final | 0 | 两次首失败分别定位旧 UI selectors；迁移后 long-session passed |
| `git diff --check` | 15:05 | 1.4s | 0 | Passed；仅 line-ending conversion warnings |

每个命令必须少于 5 分钟；超时后停止，不继续堆叠耗时验证。

## 12. Resource 与清理

| 指标 | Baseline | Observed | Limit | 结果 |
| --- | ---: | ---: | ---: | --- |
| Listener delta | 待填写 | 未执行 | `<= 40` | Not started |
| Renderer private bytes delta | warm peak/settled median | `10.37%` | `<= 15%` | Passed |
| Renderer working set delta | warm peak/settled median | `0.83%` | `<= 15%` | Passed |
| DOM elements | frozen conversation window | assistant `<=1`；Conversation window `<=80` | frozen limit | Passed for scoped DOM bounds |
| Projection cache survivors | 待填写 | 未执行 | frozen limit | Not started |
| Owned process cleanup delta | 0 | 0 | 0 | Passed |
| Temp/config cleanup delta | 0 | 0 | 0 | Passed |

性能证据：`artifacts/desktop-performance/week77-single-profiles/long-session-2026-07-31T07-36-53.279Z-16724.json`。

真实桌面启动：

- `.env.local` 由启动 PowerShell 读取到当前进程环境；未打印变量名、值或 API key。
- `npm start` helper PID `26660`；C-AICLI Desktop Electron window PID `25028`，窗口标题 `C-AICLI Desktop`。
- AppHost dotnet PID `39724`；截至 `2026-07-31 15:49 +08:00` 仍在运行，桌面程序保留供用户验收。

## 13. Defects

| ID | Priority | 描述 | 状态 | Owner | Blocking |
| --- | --- | --- | --- | --- | --- |
| UI-001 | P1 | Runtime failed 曾清空已打开 workspace/detail，违反历史保持可读 | Fixed + regression test | Codex | No |
| UI-002 | P2 | long-session E2E 仍查找旧 `loaded items`/旧 Terminal 位置 | Fixed；performance passed | Codex | No |
| UI-003 | P2 | Terminal output 是可聚焦区域但缺少 accessible name | Fixed | Codex | No |
| UI-004 | P2 | 最终 screenshot manifest 尚未获得用户 Passed/Failed 回执 | Open | User | Final visual sign-off only |
| UI-005 | P1 | Turn 启动/重试成功后本地 Composer draft 停留在 `enqueueing`，输入框和发送按钮永久禁用 | Fixed + regression tests (`feecba4`) | Codex | No |

Final Gate 要求 open P0/P1 为 `0/0`。

## 14. 最终验收

- [x] Presentation spec 全部 mandatory 规则已实现。
- [x] State matrix 每行有 test evidence。
- [x] 三视口矩阵全部通过。
- [x] Authority/security 不变量全部通过。
- [x] Accessibility 全部通过。
- [x] Required full tests/build/scoped E2E 全部通过。
- [ ] 用户对最终 screenshot manifest 明确确认。
- [x] `.env.local` 真实程序启动且无密钥泄露。
- [x] exact final product revision 与 evidence hash 已记录。

最终结论：`P1 remediation verified；real desktop restart pending；Pending user retest and visual acceptance`

签署：

| 角色 | 结论 | 日期 | 备注 |
| --- | --- | --- | --- |
| 自动化 Gate | Passed | 2026-07-31 | Gate 15/15，supplemental 30/30 |
| 开发验收 | Passed | 2026-07-31 | Required commands passed；legacy broad unpacked 未伪造 |
| 用户视觉验收 | 待确认 | — | — |

## 15. 2026-07-31 Composer follow-up remediation

用户实机反馈：首次对话失败、provider retry 成功后，Turn 已完成且 AppHost ready，但 Composer 仍显示 “Sending prompt.”，输入框与发送按钮不可用。

根因：`enqueueComposer` 将 thread-scoped draft 置为 `validating/enqueueing` 后，没有在 `startTurn` 成功、失败、选择切换或异常退出时统一收敛 transient 状态。AppHost authoritative pending intent 已清除，但 Renderer draft 仍保持 busy，形成永久禁用。

修复：

- 新增 reducer `settle` action，仅将 `validating/enqueueing` 收敛为 `editing`，不覆盖 terminal error。
- `enqueueComposer` 使用 `finally` 覆盖所有退出路径；失败路径同时保留可见错误。
- conversation harness 使用与产品 Composer 相同的 busy/disabled 规则，并验证第一条发送完成后可以继续输入和发送第二条。
- 未修改 desktop-v1 contract、AppHost authority、provider retry、Turn/revision 或副作用执行语义。

验证记录：

| 命令 | 耗时 | Exit code | 结果 |
| --- | ---: | ---: | --- |
| failing regression (`use-desktop-controller.conversation.test.tsx`) | 2.69s | 1 | 修复前稳定复现 `expected editing / received enqueueing` |
| targeted Renderer tests | 1.47s | 0 | 2 files / 4 tests passed |
| `npm run typecheck` | 5.0s | 0 | Passed |
| `npm run lint` | 4.7s | 0 | Passed |
| `npm run test` | 9.2s | 0 | 31 files / 156 tests passed |
| `npm run build` | 11.1s | 0 | contracts/notices/typecheck/main/renderer/security passed |
| `npm run verify` | 23.1s | 0 | all checks/build；31 files / 156 tests passed |
| `npm run test:e2e` | 192.4s | 1 | Electron GPU process exited (`-1073741515`) and closed the page；不是产品断言失败，未记录为 Passed |
| `git diff --check` | 1.2s | 0 | Passed；仅 line-ending conversion warnings |

修复提交：`feecba4c81ebfd651a3ee033ab9a32bcc6d89fe0`。

复验状态：`Automated regression Passed；visual matrix rerun blocked by Electron GPU crash；等待真实桌面复验`。
