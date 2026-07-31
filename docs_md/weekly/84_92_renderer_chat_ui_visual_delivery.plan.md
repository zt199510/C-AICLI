# Week 84–92 Renderer UI 视觉交付补充计划

状态：`Draft — waiting for user visual approval`

创建日期：`2026-07-31`

## 1. 文档定位

本文件是 Week 86–88 Renderer lane 的视觉实施补充，不替代既有执行契约、Entry Gate、Evidence schema 或固定 lane 计划：

- Week 86：`86_week_chat_first_shell_design_system.plan.md`
- Week 87：`87_week_conversation_projection_timeline.plan.md`
- Week 88：`88_week_composer_inline_approval_task_controls.plan.md`
- 统一契约：`84_92_week_goal_execution_contract.md`

视觉需求基线：

- `docs_md/spec/codex_style_chat_ui_presentation.md`
- `docs_md/spec/codex_style_chat_ui_state_matrix.md`
- `docs_md/spec/assets/codex-chat-ui/`

若本文与既有安全、authority、revision、resource 或 Gate 契约冲突，以既有统一执行契约和 canonical desktop-v1 为准，并在 Decision Log 中记录冲突，不得静默改写基线。

## 2. 总目标

把当前功能完整但视觉层级分散的界面收敛为 Chat-first 对话体验：

- 用户与 AI 内容成为主视觉；
- provider phase 在同一 AI message 中轻量表达；
- 工具与命令默认折叠；
- approval/recovery 在对应 Turn 内联出现；
- Composer 成为稳定底部锚点；
- Activity/Audit 完整保留 AppHost 权威事实。

## 3. 实施前置条件

开始产品代码修改前必须满足：

- [ ] 用户确认 UI presentation spec。
- [ ] 用户确认 5 张 SVG 规划图，或给出明确修改意见。
- [ ] 交互原型中 connecting、thinking、streaming、retry、approval、completed、failed 的层级被确认。
- [ ] 冻结当前 1440×900、1024×768、800×900 baseline screenshot。
- [ ] 冻结现有 desktop-v1 inventory、Renderer tests、listener/resource baseline。
- [ ] 工作树边界、目标 lane 和 exact base revision 符合统一执行契约。

## 4. 文件与组件边界

预期重点文件：

```text
apps/desktop/src/renderer/
  App.tsx
  TimelineView.tsx
  TimelineProjectionBlock.tsx
  TimelineItem.tsx
  Composer.tsx
  TaskControls.tsx
  timeline-projection.ts
  app/app-shell.css
```

允许新增：

```text
ConversationStream.tsx
AssistantMessage.tsx
AssistantStatusLine.tsx
ToolActivityGroup.tsx
ApprovalCard.tsx
RecoveryActions.tsx
conversation-block-projector.ts
app/design-tokens.css
```

禁止通过 UI 层重新推导 authority IDs、revision 或 retry classification。

## 5. Phase A：视觉基线与 token

对应 Week 86。

### 工作项

- 将当前重复/覆盖 CSS 整理为单一 semantic token source。
- 冻结 canvas、surface、text、accent、warning、danger、spacing、radius、focus 和 motion token。
- 按 Week86 Gate 精确建立 3 个 viewport × 5 个 shell fixture 的 `caseCount=15` baseline manifest。
- 其余 5 个 conversation state fixture 延后到 Phase F，写入独立 supplemental visual manifest，不覆盖 Gate-owned `screenshot-manifest.json`。
- 为 forced colors 和 reduced motion 添加明确规则。

### 测试先行

- token contrast check。
- focus-visible 与 forced-colors fixture。
- screenshot dimensions、body overflow 和 critical overlap assertion。

### Exit 条件

- token 不降低当前 WCAG AA 对比度。
- 不改变 timeline/composer/approval 业务语义。
- 现有 Renderer tests 全绿。

## 6. Phase B：AppShell 与 Conversation column

对应 Week 86。

### 工作项

- 固定 titlebar、navigation、conversation header、conversation canvas、composer slot 和 inspector slot 的 ownership。
- 1440px 保留左右持久面板；1024px 将 Inspector 改为 drawer；800px 左右 drawer 互斥。
- 中央正文和 Composer 对齐到同一 `760px` column。
- 增加“回到最新”scroll affordance，不破坏历史阅读。

### 测试先行

- 15 组既有 Week86 shell fixtures。
- Drawer Escape、focus restore、aria-expanded。
- no body horizontal scroll。

### Exit 条件

- 三视口 conversation/composer 可达。
- 左右 drawer 不同时覆盖主操作。
- thread/workspace 选择行为无回归。

## 7. Phase C：纯 ConversationBlock projection

对应 Week 87。

### 工作项

- 建立穷举 `ConversationBlock` union。
- 将 user、assistant、tool、approval、warning、result、unknown 映射为纯 presentation blocks。
- 保留 sequence、source、status、redaction 和 raw bounded payload 到 audit details。
- 消除 `TimelineView` 中基于相邻 DOM item 的临时合并逻辑。

### 测试先行

- canonical timeline inventory exhaustiveness。
- duplicate、out-of-order、truncated、redacted、unknown fixtures。
- same assistant identity across provider phases。
- 2,000 item unit 和 240 item E2E DOM bounds。

### Exit 条件

- 新 timeline type 会使 exhaustiveness test fail closed。
- golden block kind/order/identity canonical diff 为 0。
- notification/reload/authoritative replace duplicate block count 为 0。

## 8. Phase D：Assistant lifecycle UI

对应 Week 87，并承接已实现的 provider progress contract。

### 工作项

- 拆分 `AssistantMessage`、`AssistantStatusLine` 和 `RecoveryActions`。
- 实现 connecting、thinking、streaming、retry-wait、completed、canceled、failed。
- retry wait 保留旧 attempt partial；新 attempt 首段原位替换。
- completed 后隐藏大状态行，只保留耗时。
- ToolActivityGroup 默认折叠，running/failed/approval 自动显示必要摘要。

### 测试先行

- 每个 phase 单一 assistant DOM identity。
- attempt content replacement，不交叉拼接。
- streaming 不逐 token aria-live。
- scroll anchoring 和“回到最新”。

### Exit 条件

- visible assistant block count 始终为 1。
- content cross-attempt concatenate count 为 0。
- main conversation 不出现大型 running Turn card。

## 9. Phase E：Composer、Approval 与 Recovery

对应 Week 88。

### 工作项

- Composer auto-grow、context chips、Send/Stop 原位切换。
- AI 运行时允许编辑 draft，但主按钮保持 Stop，禁止并发 Turn。
- ApprovalCard 放入对应 ToolGroup/Turn block。
- retry exhausted 显示 Retry/Copy；restart side-effect guard 错误原位反馈。
- 将普通运行 TaskControls 从视觉流中移除，只保留例外操作。

### 测试先行

- Enter、Shift+Enter、IME composing。
- Send/Stop/Approve/Deny/Retry 互斥矩阵。
- stale/double/late mutation fail closed。
- thread switch 保留/隔离 draft 和 optimistic exchange。

### Exit 条件

- visible primary mutation action count 不超过 1。
- approval action 参数全部来自 authoritative state。
- automatic replay、duplicate mutation 和 old approval reuse count 为 0。

## 10. Phase F：视觉回归与用户确认

### 自动化

- 生成 3 viewport × 10 fixture 的最终截图和 DOM/a11y snapshot；扩展的 30 组结果写入独立 supplemental visual manifest。
- Week86 Gate-owned manifest 继续保持 exact `caseCount=15`，不得被 supplemental manifest 覆盖或改写。
- 与 baseline 比较结构、overflow、unreachable controls 和关键视觉区域。
- 运行 typecheck、lint、完整 test、build、适用 E2E、listener/resource gates。

### 用户视觉确认

用户确认至少回答：

1. 主对话是否足够突出用户与 AI 内容？
2. 状态是否清晰但不过度抢眼？
3. retry、approval、failed 是否能快速理解并操作？
4. Composer 是否是稳定且自然的主操作位置？
5. 三视口是否具有一致产品感？

未获得用户视觉确认时，机器 Gate 通过也不能将视觉交付标记为完成。

## 11. 验证命令

单个命令最长 5 分钟；超时立即终止并记录 first failure。

```powershell
dotnet build src\CSharpAiCli.sln -c Release
dotnet test src\CSharpAiCli.sln -c Release --no-build
Push-Location apps\desktop
npm run check:contracts
npm run check:accessibility
npm run typecheck
npm run lint
npm test
npm run build
npm run verify
npm run test:e2e:unpacked
npm run test:e2e:performance
Pop-Location
git diff --check
```

只有适用的 resource/performance Gate 已准备好时才运行对应命令，不堆叠超过 5 分钟的任务。

## 12. 验收指标

| 指标 | 目标 |
| --- | ---: |
| Authority reconcile 后重复用户消息 | 0 |
| 同 Turn 可见 AI block | 1 |
| Cross-attempt 内容拼接 | 0 |
| Body horizontal overflow fixture | 0 |
| Critical overlap fixture | 0 |
| Unreachable visible action | 0 |
| Stale mutation accepted | 0 |
| Duplicate mutation/replay | 0 |
| A11y 自动 violation | 0 |
| 未覆盖 timeline type | 0 |
| Open P0/P1 | 0/0 |

## 13. 回退策略

- 每个 Phase 独立提交，禁止把 projection、layout、approval authority 改动混成一个不可回退提交。
- 视觉 token 可独立回退，不影响 desktop-v1。
- ConversationBlock projector 可保留 compatibility facade，直到 golden tests 和 E2E 稳定。
- 任一 authority/revision/replay 测试失败时停止视觉迁移，恢复到上一个 Gate clean revision。
- 不使用 `git reset --hard` 或覆盖用户未提交改动。

## 14. 交付物

- UI presentation spec 与 state matrix。
- 5 张 SVG 规划图和交互原型。
- Phase A–F 产品代码与测试。
- Viewport screenshot manifest、DOM/a11y evidence。
- 执行验收记录：`84_92_renderer_chat_ui_execution_acceptance.md`。
- 用户视觉确认与最终 handoff。
