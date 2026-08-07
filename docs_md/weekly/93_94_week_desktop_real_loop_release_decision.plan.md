# Week 93–94 计划：Desktop 真实闭环与发布判定

状态：`Planned`

创建日期：2026-08-05

适用范围：当前 `main` 后续形成的单一候选 revision。

## 目标

冻结一个 Desktop 0.6.0 候选，完成真实模型连续对话、停止、恢复、持久化、三档界面与 Windows Narrator 人工检查，并给出唯一最终结论。

本计划优先关闭真实使用和发布判定缺口，不继续扩展产品功能。验收过程保持精简，不建立新的大型 evidence、Gate、schema、截图矩阵或周次验收文件。

## 最终结论

只允许以下三种结果：

- `Accepted`：本计划的必验场景全部通过，开放 P0/P1 为 `0/0`。
- `Blocked`：发现尚未关闭的 P0/P1，或候选无法完成必验场景。
- `NotRun`：缺少真实 provider 授权、人工操作条件或必要运行环境。

不得使用“基本通过”“大致可用”“Candidate Ready”等模糊结论。

## 范围

### 必须完成

1. 冻结并记录 exact candidate revision，确认 `.env.local` 未被 Git 跟踪。
2. 针对三面板和最新 Composer 变更运行受影响的自动回归。
3. 在同一会话完成两轮无工具真实对话，确认上下文连续且消息不重复。
4. 使用第三轮长回复验证 Stop；停止后 Composer 必须恢复可用。
5. 重载或重启一次，确认已完成和已停止内容仍正确持久化。
6. 检查 1440×900、1024×768、800×900 三档布局、面板互斥、Escape 和焦点回归。
7. 完成现有 Windows Narrator 七步人工检查。
8. 确认无秘密泄露、未授权写入、重复副作用和残留 owned process。

### 明确不做

- Git commit、push、Pull Request 或分支写操作。
- Branch compare、Sub-agents、插件、远程控制或团队功能。
- 新一轮 Composer、时间线或三面板视觉重构。
- Gerber/TIFF、MCP、Shell、文件修改等真实工具调用。
- tag、上传、部署、GitHub Release 或对外发布。

## 授权边界

Credential-free 的源码检查、构建和定向测试可以直接执行。

读取 `.env.local` 或调用真实 provider 前，执行对话必须取得用户当次明确授权。授权后也只能：

- 读取 `OPENAI_MODEL`、`OPENAI_BASE_URL`、`OPENAI_API_KEY` 并仅注入本次 Desktop/AppHost 进程；
- 最多使用 3 个 provider turn：两轮连续对话和一轮 Stop；
- 使用无工具、无文件写入、无 Shell、无 Git、无 MCP 的提示词；
- 不在输出、日志、截图或文件中记录配置值、请求头或原始敏感错误。

授权缺失时停止在 `NotRun`，不得读取配置内容，也不得用 fake runtime 代替真实 provider 结论。

## 执行顺序

### Phase 0：冻结候选

- 记录 `HEAD`、最近提交和工作树状态。
- 说明所有既有未提交改动，不覆盖用户文件。
- 若产品代码在验收期间发生修复，以修复后的新 revision 重新冻结候选。
- 不复用 `128f2ca` 的人工验收状态；它早于最新 Composer 重构。

### Phase 1：最小自动回归

只运行与候选改动直接相关的验证：

- Desktop Composer、App、Workspace Inspector 相关测试；
- Desktop typecheck、lint 和 production build/security check；
- provider retry、Desktop protocol/RPC 的定向 .NET 测试；
- 只有布局、焦点、生命周期或协议受影响时才运行相应 E2E。

单条命令和同一批次目标上限均为 5 分钟。首次失败先诊断，不无修改重复运行，也不自动扩大到全仓库、打包或资源矩阵。

### Phase 2：真实对话闭环

取得当次 provider 授权后：

1. 启动 production Renderer 与 Release AppHost，确认没有 fake runtime 或开发预览桥接。
2. 第一轮请求一个短 Markdown 回复；观察用户消息、单一 AI 消息块和流式内容。
3. 第二轮要求基于上一轮改写一项；确认上下文连续、Composer 恢复且无重复消息。
4. 第三轮请求较长的纯文本说明，在首段出现后执行 Stop；确认内容保留、停止后不再追加、Composer 恢复。

第三轮在操作前已完成时允许再尝试一次，但 provider turn 总数仍不得超过 3；因此应优先选择足够长、容易及时停止的提示词，不循环尝试。

### Phase 3：持久化、布局与 Narrator

- 切换会话并返回，然后关闭和重启 Desktop 一次。
- 确认消息顺序、Markdown、完成/停止状态和 Composer 状态没有漂移。
- 在三个规定视口完成一次布局和键盘检查。
- 按 `docs_md/release/narrator_acceptance_0.6.0.md` 完成七步 Narrator 人工检查。
- 需要用户观察时集中提出一次操作请求，不为每个小项创建独立暂停点。

### Phase 4：缺陷处理与重验

- P0：立即停止，保护秘密和工作区，只报告阻塞原因。
- P1：实施一个最小修复，只重跑直接受影响的自动测试和人工场景。
- P2：仅在影响最终判断时写入报告；纯视觉偏好不扩大本轮范围。

不得借验收之名进行架构重构或增加新功能。

### Phase 5：清理与报告

- 本轮如确需临时记录，只能放在一个明确目录：`artifacts/manual-acceptance/week93-94-<short-revision>/`。
- 默认不截图；只有解释 P0/P1 时才可临时保存，最多 3 张。
- 最终判定完成后，验证路径确属上述本轮目录，再删除整个本轮临时目录以及本轮创建的其他验收中间文件。
- 不创建或提交新的 acceptance report、evidence manifest、JSON ledger、截图清单或 week review。
- 不改写历史验收文档来制造通过记录。
- 清理后再次检查工作树与 owned process；不得删除用户已有文件或其他历史目录。
- 最终报告只在对话中交付，不在仓库中留存副本。

## 最终报告格式

报告最多包含以下内容：

```text
结论：Accepted | Blocked | NotRun
候选：<exact revision>
关键结果：自动回归 / 真实连续对话与 Stop / 持久化与三档界面 / Narrator
遗留问题：无，或仅列阻塞的 P0/P1
清理：本轮临时验收文件已删除；owned process 残留为 0
```

不附完整命令日志、重复通过清单、截图矩阵或大段实施过程。

## 完成条件

- exact candidate 明确；
- 自动回归、真实三轮闭环、持久化、三档界面和 Narrator 均有实际结果；
- 开放 P0/P1 为 `0/0` 才能给出 `Accepted`；
- 本轮临时验收文件已经删除；
- 最终只交付一份简短对话报告。

本计划不授权提交、push、tag、上传、部署或发布；这些动作必须由用户另行明确要求。
