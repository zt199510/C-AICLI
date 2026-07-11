## 第 43 周回顾

状态：已验收

已完成：
- 定义 `AgentFailureKind`、`AgentRetryDecisionInput`、`AgentRetryPolicy`、`AgentRetryAttempt` 和 `AgentFailureSummary`，覆盖 model/tool/approval/patch/shell/verification/budget 失败来源。
- `exec` 支持有限失败反馈 retry：verification failure、shell failure 和可修复 tool error 会以 bounded structured feedback 回传给模型。
- 增加 `agentRunLimits.maxRetries` 与 `exec --max-retries`；默认 retry budget 为 1，`--max-retries 0` 显式禁用自动 retry。
- retry 后合并 changed files、verification command history、commands、stop reason，并在 budget exhausted 时输出可复核 failure summary。
- fake end-to-end 测试覆盖 verification failure 后再次 read/search/patch/verify 的修复路径。
- shell failure retry 测试覆盖 stdout/stderr bounded feedback；approval/policy/workspace guard/dirty checks 仍走原 tool 安全路径。
- 更新 capability status 和 known limitations。

验证：
- 命令：`dotnet test D:\AI\C-AICLI\src\CSharpAiCli.sln --no-restore`（从 `D:\AI` 执行）
- 结果：通过，1010 passed，0 failed
- 命令：`dotnet build D:\AI\C-AICLI\src\CSharpAiCli.sln --no-restore`（从 `D:\AI` 执行）
- 结果：通过，0 warnings，0 errors

运行时说明：
- 由于仓库根目录 `global.json` 锁定 SDK `9.0.308` 且本机安装 SDK 为 `10.0.301`，验证命令从父目录 `D:\AI` 使用绝对 solution path 执行。
- retry 是有限修复流程，不是无限循环；budget 耗尽后任务明确失败，并报告剩余风险。
- retry 不提升权限，不绕过 approval、workspace guard、dirty-workspace checks、shell policy 或 dangerous command detection。
- 自动 verification 仍沿用第 42 周策略：只使用显式 `VerificationCommand:`/`ValidationCommand:` 或单一明确 workflow command。
- 真实模型 smoke 未运行，仍由凭据和 opt-in 设置控制。

风险：
- 真实模型能否修复失败仍依赖模型质量和 bounded feedback 的可读性。
- 极长输出会被截断；复杂失败可能需要人工查看完整日志或重新运行命令。
- failure summary 是复核辅助，不提供自动 rollback；changed files 仍需人工复核。

第 44 周输入：
- 收敛 retry/failure summary 在 trace、session export、JSON event 中的字段稳定性。
- 补充真实模型 opt-in retry smoke，记录 provider/model 差异。
