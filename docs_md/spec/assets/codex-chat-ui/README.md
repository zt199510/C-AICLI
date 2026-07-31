# Codex Chat UI 规划资产

创建日期：`2026-07-31`

这些资产用于视觉评审和实现对齐，不是最终产品截图。

| 文件 | 用途 |
| --- | --- |
| `01-desktop-layout.svg` | 1440×900 AppShell、conversation column、Inspector 和 Composer 尺寸关系 |
| `02-conversation-completed.svg` | 普通完成态的信息层级与消息间距 |
| `03-assistant-state-storyboard.svg` | connecting、thinking、streaming、retry、completed、failed 的单消息演进 |
| `04-approval-and-failure.svg` | ApprovalCard 与 retry-exhausted 操作互斥 |
| `05-responsive-layout.svg` | 800×900 窄窗口 drawer 和 Composer 重排 |
| `codex-chat-ui-prototype.html` | 可交互切换 AI 状态的本地原型 |

## 评审方式

1. 先确认 `01` 的整体信息架构和三栏关系。
2. 再确认 `02` 的正文密度、用户消息宽度和工具折叠方式。
3. 使用 `03` 检查状态层级是否一致。
4. 使用 `04` 确认审批与失败操作不会同时出现。
5. 使用 `05` 确认窄窗口没有不可达操作。
6. 打开 HTML 原型，逐个切换状态，确认同一 AI message 原位变化。

## 使用约束

- SVG 中的尺寸是实现目标范围，不要求逐像素复制。
- 产品代码必须使用 semantic CSS variables，不直接复制 SVG 固定色值。
- 视觉图不覆盖 authority、revision、retry、防重放和审批安全契约。
- 最终实现仍需真实 Renderer screenshot、DOM、a11y 和 viewport Gate。
