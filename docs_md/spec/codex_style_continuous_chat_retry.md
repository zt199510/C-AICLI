# C-AICLI Codex 式连续对话与连接重试开发规格

状态：`需求已确认，等待在新对话中开发`

创建日期：`2026-07-30`

目标版本：当前 `main` 后续增量

## 1. 目标

将 Desktop 对话交互调整为 Codex App 风格的连续消息流：用户发送后立即看见自己的消息，AI 在同一位置显示工作状态并流式更新回复；临时连接失败时，在同一个 Turn 内最多自动重试 5 次；结束后显示本次处理时间。

这不是删除 Turn。Turn、revision、durable timeline 和 AppHost authority 继续作为内部事实来源，只是不再用大型 Turn 卡片打断普通对话。

## 2. 已确认的产品需求

### 2.1 即时发送

- 用户点击发送后，用户消息立即显示在当前对话右侧。
- Composer 立即清空并保持可输入。
- 消息继续追加到当前 thread，不创建新 thread。
- 乐观消息必须通过稳定 identity 与 AppHost 返回的权威消息对账；权威消息到达后替换本地占位，不得重复显示。
- 发送请求若立即被权威层拒绝，保留用户文字并给出可恢复错误，不静默丢失输入。

### 2.2 AI 工作状态

用户消息下方立即出现同一 Turn 的 AI 占位消息，状态顺序为：

```text
正在连接 -> 正在思考 -> 正在生成回复 -> 已完成
```

- 状态显示在 AI 消息内部，不显示永久独立的大型 Turn 卡片。
- 第一段模型内容到达后，原 AI 占位直接变成流式回复。
- 同一个 Turn 的 assistant 增量与 final 必须合并为同一条 AI 消息。
- 工具、审批和审计事件仍保留，但默认不打断主对话；需要用户操作时使用对应消息内的紧凑控件。

### 2.3 自动重试次数

- 初始请求正常执行 1 次。
- 初始请求发生可重试连接失败后，最多再自动重试 5 次。
- 因此单次模型请求最多执行 6 次：`1 次初始请求 + 5 次自动重试`。
- UI 中的重试编号只计算追加重试，显示为：

```text
连接中断，正在重试 1/5...
```

- 第 5 次追加重试仍失败后，不再自动请求。
- 重试必须复用同一 thread、Turn 和 AI 消息位置，不得重复插入用户消息或创建多个可见 AI 回复。

### 2.4 可重试错误

仅下列临时失败允许自动重试：

- 网络连接建立失败；
- 请求超时或流式读取意外中断；
- HTTP `408`；
- HTTP `429`；
- HTTP `5xx`；
- provider 明确标记为临时、可重试的不可用错误。

下列情况禁止自动重试：

- HTTP `400`、`401`、`403`；
- API Key、权限、模型或参数配置错误；
- 安全策略、审批拒绝或工具权限错误；
- 用户主动停止；
- 已确认不可重试的 provider 错误；
- 任何可能重放已完成副作用的整 Turn 重启。

错误分类必须由 provider/runtime 权威层给出，Renderer 不得根据错误文案猜测。

### 2.5 重试安全边界

- 自动重试放在失败的 provider/model 请求边界，不得通过重新执行整个 Turn 实现。
- 已完成的工具调用、文件写入、命令或审批不得因模型连接重试而再次执行。
- 每次 provider 尝试需要稳定的 Turn identity 和独立 attempt 序号。
- 用户停止必须取消当前 provider 请求以及等待中的下一次重试。
- 重试等待使用可取消的有界退避；若 provider 返回合法 `Retry-After`，可以优先采用，但必须设置上限。
- 退避策略集中配置并使用可注入时钟/延迟，以便测试不产生真实长等待。

### 2.6 流式中断

若 AI 已显示部分文字后流式连接中断：

- 重试等待期间暂时保留已经显示的部分内容；
- 在同一 AI 消息中显示当前重试状态；
- 新 attempt 开始输出后，用新 attempt 的内容替换未完成内容；
- 不把不同 attempt 的片段直接拼接；
- 最终只保留成功 attempt 的完整回复；
- attempt 历史保留在有界审计信息中，不在主对话生成多条回复。

### 2.7 最终失败

5 次追加重试全部失败后，在原 AI 消息位置显示：

```text
连接失败，已重试 5 次
```

同时提供：

- `重新尝试`：由用户明确启动一个新的 attempt/Turn 语义，不自动重放已完成副作用；
- `复制错误信息`：只复制安全、已脱敏的诊断文本。

用户消息、上下文、失败原因和 attempt 计数必须保留，用户不需要重新输入。

### 2.8 停止与完成时间

- AI 工作期间，Composer 的发送按钮切换为停止按钮。
- 停止后取消当前请求和全部未开始的自动重试，保留已显示内容，Composer 恢复可用。
- 正常完成后，在 AI 回复底部显示：`已处理 8.4 秒`。
- 用户停止后显示：`已停止 · 处理 3.2 秒`。
- 最终失败可以显示：`连接失败 · 处理 12.6 秒`。
- 产品计时从用户发送对应 Turn 创建时间开始，到 completed/canceled/failed 权威时间结束，包括连接、思考、生成和重试等待。
- 已存在的 `createdAtUtc` / `completedAtUtc` 应作为持久化后和重载后的权威计时依据；运行中的动态计时只用于临时显示。

## 3. UI 规则

### 3.1 主对话只保留三类可见对象

1. 用户消息；
2. AI 消息，包括思考、生成、重试、失败和完成状态；
3. 真正需要用户操作的内联审批或恢复控件。

普通运行状态不再呈现以下内容：

- `Turn N: ...` 大卡片；
- 独立 `running` chip；
- 同时出现 `Stop`、`Resume`、`Restart`；
- 与 AI 消息重复的顶部警告。

### 3.2 交互互斥

| 状态 | AI 消息 | Composer 主按钮 | 可见操作 |
| --- | --- | --- | --- |
| connecting | 正在连接 | Stop | Stop |
| thinking | 正在思考 | Stop | Stop |
| streaming | 流式内容 | Stop | Stop |
| retry-wait | 正在重试 n/5 | Stop | Stop |
| approval | 待批准说明 | Send disabled | Approve / Deny |
| completed | 完整回复 + 已处理时间 | Send | 无 |
| canceled | 已有内容 + 已停止时间 | Send | 无 |
| retry-exhausted | 失败说明 + 处理时间 | Send | 重新尝试 / 复制错误信息 |

## 4. 状态与协议要求

Renderer 所需的最小权威信息包括：

- Turn identity、revision 和基础状态；
- 当前执行阶段：connecting/thinking/streaming/retry-wait；
- 当前 provider attempt 与最大追加重试次数；
- 当前 attempt 是否已有流式内容；
- 安全错误分类与 `retryable`；
- Turn created/completed/canceled/failed 时间；
- 当前 active attempt 的 assistant 内容 identity。

若现有 desktop-v1 contract 无法完整表达这些状态，应先修改 canonical contract，再运行生成脚本。禁止直接手改 generated TypeScript 或 C# contracts。

通知、主动刷新和 authoritative reload 必须得到相同的最终投影。旧通知、乱序响应或 thread 切换后的迟到响应不得覆盖当前 thread。

## 5. 建议实现边界

### 5.1 Renderer

- Composer：发送后创建可对账的乐观用户消息；运行期间主按钮切换为 Stop。
- Timeline projection：合并乐观消息与 durable timeline；同 Turn assistant 更新保持单一消息 identity。
- Assistant working block：显示连接、思考、生成、重试、失败和处理时间。
- TaskControls：只保留审批、显式恢复等例外动作；普通运行状态迁移到 AI 消息和 Composer。
- 所有状态必须键盘可达，并通过适度的 `aria-live` 通知，避免每个 token 都被重复朗读。

### 5.2 AppHost/Application

- 保持 Turn、revision、cancel 和 durable timeline authority。
- 暴露 provider attempt 进度和安全错误分类。
- 保证 cancel 可穿透到当前请求和 retry delay。
- 记录最终 attempt 计数、结束原因和时间，但审计数据必须有界。

### 5.3 Provider/Model runtime

- 在单次模型请求边界执行最多 5 次追加重试。
- 不用整 Turn restart 代替网络重试。
- 对 408/429/5xx、timeout、transport/stream interruption 做明确分类。
- 非重试错误立即返回。
- 测试必须使用 fake gateway、fake clock/delay，不真实等待退避时间。

## 6. 推荐开发阶段

### Phase 0：基线与合同

- 复核当前发送、Turn、timeline、stream、cancel 和 retry 路径。
- 冻结现有可见行为测试。
- 定义 attempt/retry/phase 的 canonical contract 和错误矩阵。

### Phase 1：可靠自动重试

- 在 provider/model 请求边界实现 `initial + 5 retries`。
- 实现错误分类、可取消退避、attempt 事件和 retry exhausted 结果。
- 证明工具/写入不会被重放。

### Phase 2：即时消息与单消息投影

- 增加乐观用户消息及权威对账。
- 增加 AI 占位消息。
- 合并同 Turn、同 active attempt 的 assistant 增量和 final。
- 处理 thread switch、迟到通知、重复通知和 reload。

### Phase 3：Codex 式状态 UI

- 完成连接、思考、生成、重试、失败和完成视觉状态。
- 将 Stop 放入 Composer 主按钮。
- 从普通对话移除大型运行 Turn 卡片。
- 增加处理时间与安全错误操作。

### Phase 4：验收与启动

- 运行相关单元测试、Renderer 测试、typecheck、lint 和 production build。
- 单次验证命令不得运行超过 5 分钟；超时即停止并定位，不继续堆叠耗时验证。
- 使用仓库根目录 `.env.local` 启动，禁止输出 API Key。
- 在 main 上提交，不创建功能分支。

## 7. 必须覆盖的测试

### Retry/runtime

- 初始成功：1 次调用；
- 初始失败后第 1 次重试成功：2 次调用；
- 追加第 5 次重试成功：6 次调用；
- 全部失败：总计 6 次调用，显示 retry exhausted；
- 400/401/403：只调用 1 次；
- timeout/408/429/5xx：可重试；
- cancel during request：不再重试；
- cancel during backoff：下一次调用不会发生；
- 已完成工具调用在模型重试后执行次数仍为 1；
- 流式中断后不同 attempt 内容不拼接。

### Renderer

- 点击发送后无需等待 RPC 即看见用户消息；
- 权威消息返回后没有重复用户消息；
- connecting/thinking/streaming 始终只有一个 AI block；
- retry 依次显示 1/5 至 5/5；
- 完成后显示权威处理时间；
- Stop、审批、失败操作互斥；
- thread 切换和迟到响应不会污染当前对话；
- reload 后消息、状态和完成时间一致；
- IME、Enter/Shift+Enter、focus 和 screen reader 行为不回归。

## 8. 验收标准

以下条件全部满足才算完成：

- [ ] 用户发送内容在本地立即出现，权威对账后重复数为 0；
- [ ] AI 占位、流式内容和 final 在同一个可见消息中；
- [ ] 可重试连接失败最多总调用 6 次，追加重试严格为 5 次；
- [ ] 非重试错误调用次数严格为 1；
- [ ] cancel 后新增 provider 调用数为 0；
- [ ] 工具、命令、文件写入和审批的重复执行数为 0；
- [ ] 流式 attempt 内容交叉拼接数为 0；
- [ ] 完成、停止和最终失败均显示正确处理时间；
- [ ] 普通对话中不再出现大型 running Turn 卡片；
- [ ] 相关测试、typecheck、lint、build 全部通过且每项不超过 5 分钟；
- [ ] Week83 listener P1 不作为前置阻塞，仅当本次修改导致其恶化时修复；
- [ ] 使用 `.env.local` 启动后可完成真实连续对话，且日志和 UI 不泄露密钥。

## 9. 非目标

- 不重做左侧会话信息架构；
- 不更换 C-AICLI 品牌；
- 不引入新的富文本编辑器；
- 不把 retry 做成用户不可取消的后台无限循环；
- 不自动重放整个 Turn；
- 不借本任务修复无关历史技术债；
- 不执行超过 5 分钟的耗时验证。

## 10. 开发完成后的交付内容

- main 上的实现提交；
- 受影响的 canonical contract 与生成文件（如确有必要）；
- retry/error/state matrix 测试；
- Renderer 交互测试；
- 验证结果与实际耗时；
- 启动后的手工验收说明；
- 尚未解决但不阻塞本功能的技术债清单。
