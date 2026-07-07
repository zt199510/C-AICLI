## 第 30 周回顾

状态：已验收

已完成：
- 新增 `approvalMode` 配置，默认值为 `on-request`，并支持 `never`、`on-request`、`on-failure`、`always` 四种模式。
- 为工具补齐风险 metadata，覆盖 `read`、`write`、`shell`、`dangerous-shell` 等风险层级。
- 实现统一 approval policy resolver，让配置、命令行覆盖和工具风险进入同一套审批决策。
- `tools call` 支持 `--approval <mode>`，并保留 legacy `--approve` 兼容行为。
- `exec` 支持 `--approval <mode>`，并将审批结果纳入 agent/tool event 输出。
- shell 与 patch 工具的 approval request 已包含 risk level、command/path 和 reason 等 metadata。
- dangerous shell 会给出明确风险状态；被拒绝时以 `dangerous-shell-denied` 作为 approvalStatus 暴露。
- 更新安全模型、配置说明、quickstart 等文档，并增加 approval mode、dangerous command、`--approve` 兼容等测试覆盖。

验证：
- 命令：`dotnet build 'C:\Users\10335\.config\superpowers\worktrees\C-AICLI\week-30-approval-permission-profiles\src\CSharpAiCli.sln'`
- 结果：通过，0 warnings，0 errors。
- 命令：`dotnet test 'C:\Users\10335\.config\superpowers\worktrees\C-AICLI\week-30-approval-permission-profiles\src\CSharpAiCli.sln'`
- 结果：通过，455 passed，0 failed，0 skipped。
- 说明：上述命令由 controller 从 `C:\Users\10335` 工作目录运行；因为 worktree 的 `global.json` pin 到 SDK `9.0.308`，而本机可用 .NET SDK 为 `10.0.301`，所以使用 worktree 外部工作目录加绝对 solution 路径完成 net9.0 项目的 build/test。

运行时说明：
- 默认审批模式为 `on-request`。
- 非交互场景不会弹出审批 UI；需要审批时会按当前模式返回结构化拒绝/状态事件。
- dangerous shell 被拒绝时会以 `dangerous-shell-denied` 作为 approvalStatus，便于 text 与 JSON event 稳定消费。
- legacy `--approve` 仍保持兼容，适用于既有 smoke/direct-tool 调用。
- `tools call` 与 `exec` 使用 `--approval <mode>`；`run` 不提供 `--approval`。

风险：
- 当前没有交互式 approval UI，审批仍是 CLI 参数、配置和非交互策略驱动。
- `on-failure` 尚未实现 sandbox retry escalation；失败后不会自动升级 sandbox/权限重试。
- 还没有 `config set approvalMode` 命令，用户需要通过现有配置文件路径维护该配置。
- `exec` 的真实 tool-call loop 仍受后端能力限制，后续 direct OpenAI SDK continuation 仍需要继续完善。

第 31 周输入：
- 继续推进已有第 31 周计划中的 session resume management，让 `chat`/`exec` 能恢复和管理已有 transcript。
- 后续 sandbox/security 工作推进时，保持 approval events、approvalStatus 和风险 metadata 的语义一致。
