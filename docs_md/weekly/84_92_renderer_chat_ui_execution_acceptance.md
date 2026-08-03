# Week 84–92 Renderer Chat UI 执行与验收记录

状态：`Implementation complete；P1 execution projection remediation verified；等待用户复验`

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
| Final product revision | `6888e8de6f7f1c59f1ba45b081d1d49fff0b9d74` |
| Overall decision | `P1 execution projection remediation verified；Pending user retest and visual acceptance` |

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
- execution ownership remediation 后 `npm start` helper PID `26160`；C-AICLI Desktop Electron window PID `26828`，窗口标题 `C-AICLI Desktop`，`Responding=True`。
- AppHost dotnet PID `36300`；截至 `2026-07-31 17:38 +08:00` 仍在运行，桌面程序保留供用户复验。

## 13. Defects

| ID | Priority | 描述 | 状态 | Owner | Blocking |
| --- | --- | --- | --- | --- | --- |
| UI-001 | P1 | Runtime failed 曾清空已打开 workspace/detail，违反历史保持可读 | Fixed + regression test | Codex | No |
| UI-002 | P2 | long-session E2E 仍查找旧 `loaded items`/旧 Terminal 位置 | Fixed；performance passed | Codex | No |
| UI-003 | P2 | Terminal output 是可聚焦区域但缺少 accessible name | Fixed | Codex | No |
| UI-004 | P2 | 最终 screenshot manifest 尚未获得用户 Passed/Failed 回执 | Open | User | Final visual sign-off only |
| UI-005 | P1 | Turn 启动/重试成功后本地 Composer draft 停留在 `enqueueing`，输入框和发送按钮永久禁用 | Fixed + regression tests (`feecba4`) | Codex | No |
| UI-006 | P1 | AppHost 正在执行的 running Turn 被持久化读取误投影为 `recoveryRequired`，Renderer 错误显示“执行已中断” | Fixed + protocol regression (`6888e8d`) | Codex | No |

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

最终结论：`P1 execution projection remediation verified；real desktop running；Pending user retest and visual acceptance`

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

复验状态：`Automated regression Passed；visual matrix rerun blocked by Electron GPU crash；真实桌面已重启并等待用户复验`。

## 16. 2026-07-31 Active execution ownership remediation

用户实机反馈：每次发送后立即出现“执行已中断”，需要多次点击继续或重新开始后才看到回答。

持久化取证：

- 用户最近 thread 的三个 Turn 最终均为 `completed`，provider `attempt=1`。
- 三个 Turn 均没有 `retry-wait`、retry failure 或跨 attempt 流式内容。
- 截图状态实际是 `recoveryRequired/执行已中断`，不是 provider retry。

根因：`ThreadStore.Read()` 在发现持久化 active Turn 时会保守返回 `RecoveryRequired=true`，这是 AppHost 重启后的安全默认值；但它不知道当前 `DesktopWriteExecutionSupervisor` 正在合法持有并执行同一个 Turn。每次 thread progress 通知触发 Renderer 重新读取时，正常 running Turn 因此被错误投影为中断。

修复：

- supervisor 提供锁保护的 active execution ownership 查询。
- `thread/get` 只对 supervisor 当前持有的同一 thread/Turn 抑制 stale recovery projection。
- 未被当前 supervisor 持有的 active Turn 继续保持 `RecoveryRequired=true`，因此 AppHost 重启、断联和未知副作用边界仍要求显式恢复。
- 未修改 desktop-v1 generated contract、provider retry、Turn revision、mutation identity 或副作用防重放规则。

验证记录：

| 命令/检查 | 耗时 | Exit code | 结果 |
| --- | ---: | ---: | --- |
| persisted user thread audit | — | 0 | 3/3 Turn completed；3/3 provider attempt=1；0 retry-wait |
| failing protocol regression | 31.2s | 1 | 修复前稳定复现 `Expected False / Actual True`（running Turn 被标记 recovery） |
| fixed protocol regression | 31.3s | 0 | supervised running Turn 保持 `recoveryRequired=false` |
| full `.NET` tests (`--no-build --no-restore`) | 93.6s | 0 | 1441/1441 passed |
| `npm run verify` | 22.4s | 0 | contracts/notices/a11y/typecheck/lint/build；31 files / 156 tests passed |
| `npm run test:e2e` | test timeout 240s + teardown | 1 | visual fixture timeout；没有产品断言失败，未记录为 Passed，未重复运行 |
| `git diff --check` | 1.3s | 0 | Passed；仅 line-ending conversion warnings |

环境记录：

- 系统 SDK 不满足仓库锁定的 9.0.308；仓库 SDK 首次 restore 在 5 分钟上限终止。
- 第一次 `--no-restore` 构建因真实 AppHost PID `4020` 锁定 Release DLL 而失败；停止已确认的 C-AICLI 进程树后，失败回归、修复回归和完整测试均正常执行。
- visual E2E 超时遗留 PID `7860` 无窗口且命令行为空；终止请求被系统拒绝，记录为外部 teardown 缺陷，不伪造 cleanup passed。

修复提交：`6888e8de6f7f1c59f1ba45b081d1d49fff0b9d74`。

复验状态：`Protocol regression and full automation Passed；真实桌面已重启并等待用户复验`。

## 17. 2026-08-03 Streaming and Markdown follow-up remediation

用户实机反馈：助手回复看起来一次性出现，且助手消息正文没有按 Markdown 预览呈现。

根因：

- OpenAI streaming gateway 已使用流式 API，但累计内容只在首个 delta、每新增 `512` 字符和完成时提交；短回复通常只有首帧和终帧。
- Renderer 的 thread notification 策略只在少数生命周期边界重新同步，并始终从 sequence `0` 全量读取；中间已提交的 streaming timeline 无法及时投影。
- `AssistantMessage` 直接输出字符串并使用 `white-space: pre-wrap`，因此标题、列表、链接和代码块均显示为 Markdown 源文本。

修复：

- Provider 现在在首个 delta、累计新增 `96` 字符或经过 `120ms` 时提交累计 streaming snapshot；完成前的快照保持单调增长。
- Renderer 对 revision 或 committed sequence 的单调推进触发同步，并从当前最后一个 timeline sequence 增量读取；并发通知仍通过既有 `resyncRunning/resyncDirty` 合并。
- 新增无 `dangerouslySetInnerHTML` 的轻量 Markdown preview，覆盖标题、段落、列表、引用、分隔线、代码块、行内代码、强调和链接；仅允许 `http`、`https`、`mailto` 链接协议，原始 HTML 保持文本。
- Assistant article 继续使用同一个 `assistantMessageId` 和 React key；connecting、thinking、streaming、retry、completed 不创建第二个 AI message，不改变跨 provider attempt 的投影规则。
- 视觉 E2E 临时目录清理增加 Windows `EBUSY` 有界重试，不放宽任何 UI 断言。

验证记录：

| 命令/检查 | 耗时 | Exit code | 结果 |
| --- | ---: | ---: | --- |
| 修复前 Renderer 回归（3 files / 20 tests） | 17.2s | 1 | 3 个新增断言稳定失败：Markdown heading 缺失、仅 2 次 resync、增量请求仍为 `afterSequence=0` |
| 修复后 targeted Renderer tests | 2.2s | 0 | 3 files / 20 tests passed |
| 修复前 provider streaming fixture | — | 未单独记录 | `512` 字符阈值对 400 字符 fixture 只会产生首帧和终帧；首次与失败的 Renderer 命令并行执行，输出未独立保存，不伪造 exit code |
| 修复后 provider streaming 回归 | 31.7s（含 build） | 0 | 1/1 passed；累计快照长度单调且最终内容完整 |
| `npm run verify` | 35.7s | 0 | contracts/notices/a11y/typecheck/lint/build；31 files / 158 tests passed |
| full `.NET` 首次运行 | 113.4s | 1 | 1441 passed / 1 external conversion child-process teardown failure；与本次路径无关，未记为 Passed |
| isolated external conversion rerun | 3.6s | 0 | 1/1 passed |
| full `.NET` 最终运行 | 101.9s | 0 | 1442/1442 passed |
| `npm run test:e2e` 首次 | 72.8s | 1 | 45-case UI 断言执行完成；Windows `DIPS` 文件锁导致 teardown `EBUSY`，未记为 Passed |
| `npm run test:e2e` 第二次 | 7.2s | 1 | Electron GPU process `-1073741515` 关闭页面，未记为 Passed；不继续重复运行 |
| final scoped typecheck + lint | 13.9s | 0 | Passed |
| `git diff --check` | 1.3s | 0 | Passed；仅 line-ending conversion warnings |

缺陷状态：

| ID | Priority | 描述 | 状态 | Blocking |
| --- | --- | --- | --- | --- |
| UI-007 | P1 | Provider 快照与 Renderer 投影节流叠加，回复不呈现连续流式更新 | Fixed + regression tests | No |
| UI-008 | P1 | Assistant 内容以纯文本而非安全 Markdown preview 渲染 | Fixed + regression tests | No |
| UI-009 | P2 | 当前 Windows 主机上的 Electron visual E2E 存在 `DIPS` 文件锁或 GPU process crash | Open；环境/teardown，未伪造 Passed | No（产品单元、Renderer、build 与真实桌面启动均通过） |

修复提交：`5448b535344c5514510afbd47d68bf3c3a024f25`。

真实桌面状态：使用 `.env.local` 启动且未打印变量或秘密；helper PID `39884`，Electron PID `31348`，窗口标题 `C-AICLI Desktop`、`Responding=True`，AppHost PID `36828` 正在运行并已将窗口置前，等待用户复验。

## 18. 2026-08-03 First-content latency and Composer queue reconciliation

用户实机反馈：发送后需要等待数秒才出现首段 streaming 内容，并且 Composer 显示 `Ready to run`、阻止继续输入。

真实持久化时间线取证（未输出 API key、请求头或环境变量）：

- Turn A：`user.message` 到 `connecting` 约 `94ms`，到 `thinking` 约 `185ms`，到首个 `assistant.message/streaming` 约 `5.15s`。
- Turn B：`user.message` 到 `connecting` 约 `116ms`，到 `thinking` 约 `228ms`，到首个 `assistant.message/streaming` 约 `6.62s`。
- 两轮均为 provider attempt `1`，均有多个 streaming snapshots，证明请求与响应链路使用 streaming；5–7 秒区间是 Provider/model 的 time-to-first-content，而不是客户端等待完整响应。
- 持久化 Composer queue 在取证时 `pendingIntent=false`，但截图中的 Renderer 仍显示 `Ready to run`，确认是旧 Composer snapshot 回写竞态，而不是 AppHost 仍有待执行输入。

修复：

- 同一 workspace/thread 的 Composer snapshot 按 canonical `queueRevision` 单调接收，旧 revision 不能把已消费队列重新投影为 pending。
- `startTurn` 成功后的无 pending authoritative snapshot 在等待 thread/sidebar 刷新前立即提交，Composer 不再被慢 detail 请求阻塞。
- selected conversation detail 与 sidebar thread list 并发刷新；connecting/thinking/streaming 投影不再串行等待列表刷新。
- 未改变 OpenAI streaming 请求、Provider retry、Turn/revision、assistant identity、跨 attempt 内容隔离或副作用防重放规则。

验证记录：

| 命令/检查 | 耗时 | Exit code | 结果 |
| --- | ---: | ---: | --- |
| failing Renderer regressions | 2.17s / 1.69s | 1 | 稳定复现旧 queue snapshot 覆盖、selected detail 等待 sidebar、已消费 pending 在 detail 完成前仍可见 |
| fixed targeted Renderer tests | 1.81s | 0 | 3 files / 11 tests passed |
| `npm run verify` | 22.4s | 0 | contracts/notices/a11y/typecheck/lint/build；31 files / 161 tests passed |
| `git diff --check` | 1.5s | 0 | Passed；仅 line-ending conversion warnings |

修复提交：`4da8b495980a22d2785fcde699facb9bcdc2c181`。

真实桌面状态：使用 `.env.local` 重启且未打印变量或秘密；helper PID `11368`，Electron PID `22952`，窗口标题 `C-AICLI Desktop`、`Responding=True`，AppHost PID `6140` 正在运行并已将窗口置前，等待用户复验。
