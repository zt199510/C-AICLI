# C-AICLI Chat-first UI 状态矩阵

状态：`Draft for implementation`

创建日期：`2026-07-31`

本矩阵定义 Renderer 如何把 AppHost 权威状态投影为唯一可见对话状态。Renderer 不得通过错误文案猜测 retry、approval 或 terminal 语义。

## 1. 状态判定优先级

同一 Turn 同时收到多个字段时，按以下优先级决定主 UI：

1. `approval != null`
2. AppHost/runtime unavailable 或 restarting（页面级状态）
3. `status == canceling`
4. `status == canceled`
5. `status == failed && provider.retryExhausted`
6. `recoveryRequired == true` 或 interrupted/recovery eligible
7. `status == failed`
8. `status == completed`
9. `provider.phase == retry-wait`
10. `provider.phase == streaming`
11. `provider.phase == thinking`
12. `provider.phase == connecting` 或 queued/running 默认值

低优先级状态不得覆盖高优先级状态。例如 approval 存在时不得显示 Stop；terminal Turn 不得继续显示 pulse。

## 2. 主状态矩阵

| 权威条件 | AI message | 状态文案 | 内容策略 | Composer 主按钮 | 内联操作 | aria-live |
| --- | --- | --- | --- | --- | --- | --- |
| queued/running + connecting | 单一占位 | 正在连接 | 无正文 | Stop | 无 | 阶段变化一次 |
| running + thinking | 同一占位 | 正在思考 | 无正文或保留工具前上下文 | Stop | 无 | 阶段变化一次 |
| running + streaming | 同一消息 | 正在生成回复 | 当前 attempt 累积内容 | Stop | 无 | 只播报阶段，不播 token |
| running + retry-wait | 同一消息 | 连接暂时中断，正在重试 n/5 | 暂留失败 attempt，65% opacity | Stop | 无 | 每次 retry 序号一次 |
| waiting-for-approval | 同一 Turn 内 ApprovalCard | 需要批准 | 保留已有 AI/ToolGroup | Disabled | Approve / Deny | 请求出现一次 |
| canceling | 同一消息 | 正在停止 | 保留已有内容 | Disabled Stop | 无 | 一次 |
| canceled | 同一消息 | 已停止 · 处理 x 秒 | 保留已有内容 | Send | 无 | terminal 一次 |
| completed | 同一消息 | 已处理 x 秒 | 成功 attempt 完整内容 | Send | 无 | terminal 一次 |
| failed + retryExhausted | 同一消息 | 连接失败，已重试 5 次 | 保留安全错误/最后可见内容 | Send | Retry / Copy error | terminal 一次 |
| interrupted/recovery-required | 同一消息内 RecoveryBlock | 执行已中断 | 保留权威历史与 reason | Send | eligibility 对应 Resume / Restart | 状态变化一次 |
| failed + 非 retry exhausted | 同一消息 | 处理失败 · 处理 x 秒 | 安全错误摘要 | Send | eligibility 决定恢复操作 | terminal 一次 |
| runtime unavailable | 页面级 runtime banner | AppHost 不可用 | 已提交历史保持可读 | Restart AppHost | 无 | 状态变化一次 |
| runtime restarting | 页面级 runtime banner | 正在重启 AppHost | 已提交历史保持可读 | Disabled | 无 | 状态变化一次 |

## 3. Attempt 内容规则

| 事件 | 可见内容 |
| --- | --- |
| attempt 1 首段内容 | 创建当前 assistant content buffer |
| attempt 1 中断 | 保留 attempt 1 buffer，进入 retry strip |
| attempt 2 connecting/thinking | 继续暂时显示 attempt 1 buffer，但标记为 stale partial |
| attempt 2 首段内容 | 原位清空 attempt 1，显示 attempt 2 buffer |
| attempt 2 继续流式 | 只追加到 attempt 2 buffer |
| attempt 2 成功 final | 同一 message 原位替换为权威 final |
| attempt 2 再次失败 | 保留 attempt 2，等待 attempt 3 |

硬性不变量：

- `visibleAssistantBlockCount(turnId) == 1`。
- `visibleContentAttempt == activeContentAttempt`。
- `content(attempt A) + content(attempt B)` 的交叉拼接次数为 `0`。
- final 到达后以权威 final 为准，不保留 optimistic delta 副本。

## 4. 用户消息与权威对账

| 阶段 | 用户消息表现 | AI 表现 |
| --- | --- | --- |
| 点击发送同步阶段 | 立即显示 optimistic user message | 立即显示 connecting 占位 |
| enqueue 返回 intent identity | optimistic message 绑定 authority identity | 保持同一占位 |
| Turn/user timeline 到达 | 权威消息替换 optimistic，重复数为 0 | 根据 provider phase 更新 |
| 权威拒绝 | 保留文字并显示恢复输入 | 显示安全错误，不创建 Turn |
| thread 已切换后迟到响应 | 不污染当前 thread | 不污染当前 thread |

## 5. Approval 子状态

| Approval 状态 | 展示 | 操作 | 测试要求 |
| --- | --- | --- | --- |
| active | ApprovalCard + risk/safe summary | Approve / Deny | double/late/thread-switch fail closed |
| resolved-approved | 原位只读“已批准” | 无 | 不复用旧 request/revision |
| resolved-denied | 原位只读“已拒绝” | 无 | 不触发 provider retry |
| stale | 原位只读“请求已失效” | 无 | mutation accepted count = 0 |
| expired | 原位只读“请求已过期” | 无 | mutation accepted count = 0 |

## 6. 操作互斥矩阵

| 状态 | Send | Stop | Approve | Deny | Retry | Copy error | Resume | Restart | Restart AppHost |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| connecting/thinking/streaming/retry-wait | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| approval active | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| approval resolved/stale/expired | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| canceling | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| completed/canceled | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| interrupted/recovery-required | 1 | 0 | 0 | 0 | 0 | 0 | eligibility | eligibility | 0 |
| retry exhausted | 1 | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 |
| runtime unavailable | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| runtime restarting | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

同一状态下 visible primary mutation action count 不得超过 `1`；Approval 的 Approve/Deny 作为一个互斥操作组计算。

## 7. Activity disclosure

| 活动状态 | 默认展开 | 主对话摘要 |
| --- | --- | --- |
| queued | 否 | 准备执行 |
| running | 是，仅展示当前项 | 正在执行：{safe name} |
| waiting approval | 是 | 需要批准：{safe summary} |
| completed | 否 | 已完成 n 项操作 |
| failed | 是 | 操作失败：{safe error} |
| redacted | 否 | 受保护的操作记录 |
| unknown | 是 AuditFallback | 未识别的审计事件 |

## 8. Reload 与通知一致性

- 主动刷新、notification append 和完整 reload 必须得到相同 block kind/order/identity。
- terminal reload 后只使用 `createdAtUtc/completedAtUtc` 计算处理时间。
- notification revision/sequence 旧于当前 authority 时不得回退 UI。
- duplicate notification 不增加用户消息、AI message 或 ToolGroup 数量。
- thread/workspace owner 变化时释放旧 projection cache、optimistic exchange 和 live announcement owner。

## 9. 文案表

| Key | 中文文案 |
| --- | --- |
| connecting | 正在连接 |
| thinking | 正在思考 |
| streaming | 正在生成回复 |
| retryWait | 连接暂时中断，正在重试 {retry}/5 |
| completed | 已处理 {seconds} 秒 |
| canceling | 正在停止 |
| canceled | 已停止 · 处理 {seconds} 秒 |
| retryExhausted | 连接失败，已重试 5 次 |
| failedDuration | 处理失败 · 处理 {seconds} 秒 |
| interrupted | 执行已中断 |
| runtimeUnavailable | AppHost 不可用 |
| runtimeRestarting | 正在重启 AppHost |
| approvalApproved | 已批准 |
| approvalDenied | 已拒绝 |
| approvalStale | 请求已失效 |
| approvalExpired | 请求已过期 |
| toolSummary | 已完成 {count} 项操作 |
| approvalTitle | 需要批准 |
| retryAction | 重新尝试 |
| copyErrorAction | 复制错误信息 |
| restoreInput | 恢复输入 |
| latest | 回到最新 |

文案不得包含 raw exception、API Key、完整绝对敏感路径或未经 AppHost 脱敏的 provider payload。

## 10. 测试映射

每一行主状态矩阵至少有：

- 一个纯 projection 单元测试；
- 一个 Renderer interaction/DOM 测试；
- terminal、approval、retry、thread switch 至少一个 E2E fixture；
- 1440×900、1024×768、800×900 screenshot/overflow assertion。
